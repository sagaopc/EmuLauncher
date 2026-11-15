using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmuLauncher.Services;

public enum ProviderKind
{
    GitHub,
    GitLab,
    Gitea
}

/// <param name="Actions">True when the link was the repository's Actions page: builds are followed instead of releases.</param>
/// <param name="Workflow">Optional workflow file (e.g. "build.yml") when the link named one.</param>
public sealed record RepoRef(ProviderKind Kind, string Host, string Path, bool Actions = false, string Workflow = "")
{
    public string Display => !Actions
        ? $"{Host}/{Path}"
        : Workflow.Length > 0 ? $"{Host}/{Path}/actions/workflows/{Workflow}" : $"{Host}/{Path}/actions";
}

public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <param name="Assets">Every downloadable file of the release, best Windows candidate first.</param>
/// <param name="IsSourceArchive">True when the version only exists as a tag, so the download is the source archive.</param>
/// <param name="Stamp">When the release (or its newest file) was published; MinValue when unknown.</param>
/// <param name="Note">Something the user should know before installing, e.g. where an Actions build comes from.</param>
public sealed record ReleaseInfo(string Version, IReadOnlyList<ReleaseAsset> Assets, bool IsSourceArchive, DateTimeOffset Stamp,
    string Note = "")
{
    public ReleaseAsset? Preferred => Assets.Count > 0 ? Assets[0] : null;
}

public sealed class RepoClient
{
    // Declared before Http: static fields are initialised in this order and CreateClient reads it.
    private static readonly string Token = (Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "").Trim();

    private static readonly HttpClient Http = CreateClient();

    /// <summary>True when a GITHUB_TOKEN environment variable is set.</summary>
    public static bool HasToken => Token.Length > 0;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EmuLauncher", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Optional: raising the GitHub API rate limit (60 requests/hour without a token).
        if (Token.Length > 0)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        return client;
    }

    /// <summary>
    /// Adds the GitHub token to a download from the GitHub API (Actions build files need a login).
    /// Downloads from anywhere else are left alone, so the token never leaves GitHub.
    /// </summary>
    public static void Authorize(HttpRequestMessage request)
    {
        if (Token.Length == 0 || request.RequestUri is null) return;
        if (!request.RequestUri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)) return;

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    /// <summary>Parses a GitHub, GitLab or Eden Git (Gitea/Forgejo) repository URL.</summary>
    public static RepoRef? TryParse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var value = url.Trim();

        // git@host:owner/repo.git
        if (value.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            var colon = value.IndexOf(':');
            if (colon < 0) return null;
            value = "https://" + value[4..colon] + "/" + value[(colon + 1)..];
        }

        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.Trim('/');
        path = Regex.Replace(path, @"\.git$", "", RegexOptions.IgnoreCase);

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

        // owner/repo/actions[/workflows/<file>]: the link is the Actions page, so its builds are
        // followed instead of the releases. (GitLab calls these pipelines and nests its groups.)
        var actions = false;
        var workflow = "";
        if (parts.Count > 2 &&
            parts[2].Equals("actions", StringComparison.OrdinalIgnoreCase) &&
            !host.Contains("gitlab", StringComparison.OrdinalIgnoreCase))
        {
            actions = true;
            if (parts.Count > 4 && parts[3].Equals("workflows", StringComparison.OrdinalIgnoreCase))
                workflow = Uri.UnescapeDataString(parts[4]);

            parts = parts.Take(2).ToList();
        }

        // Drop anything after the repository name (e.g. /releases, /tree/main).
        var cut = parts.FindIndex(p => p is "releases" or "tags" or "tree" or "blob" or "commits" or "-");
        if (cut >= 0) parts = parts.Take(cut).ToList();

        if (parts.Count < 2) return null;

        var provider = host switch
        {
            "github.com" => ProviderKind.GitHub,
            "gitlab.com" => ProviderKind.GitLab,
            _ when host.Contains("gitlab", StringComparison.OrdinalIgnoreCase) => ProviderKind.GitLab,
            _ => ProviderKind.Gitea
        };

        return new RepoRef(provider, host, string.Join('/', parts), actions, workflow);
    }

    /// <summary>
    /// Resolves the newest version: the most recently published release (pre-releases included),
    /// or the newest tag when the repository has no releases at all.
    /// </summary>
    /// <param name="tagFilter">Optional: only releases and tags containing this text are considered.</param>
    public async Task<ReleaseInfo> GetLatestAsync(RepoRef repo, string? tagFilter, CancellationToken ct)
    {
        if (repo.Actions)
            return await GetLatestBuildAsync(repo, tagFilter, ct);

        var releasesUrl = repo.Kind switch
        {
            ProviderKind.GitHub => $"https://api.github.com/repos/{repo.Path}/releases?per_page=30",
            ProviderKind.GitLab => $"https://{repo.Host}/api/v4/projects/{Uri.EscapeDataString(repo.Path)}/releases?per_page=30",
            _ => $"https://{repo.Host}/api/v1/repos/{repo.Path}/releases?limit=30"
        };

        var (releasesJson, releasesError) = await TryGetAsync(releasesUrl, ct);
        if (releasesJson is null)
            throw new HttpRequestException(T.L(
                $"Could not read releases from {repo.Display} ({releasesError}).",
                $"Não foi possível ler os releases de {repo.Display} ({releasesError})."));

        var allReleases = ParseReleases(repo.Kind, releasesJson).ToList();
        var releases = allReleases.Where(c => Matches(c, tagFilter)).ToList();
        if (releases.Count > 0)
            return Build(releases.OrderByDescending(c => c.Date).First());

        if (allReleases.Count > 0)
            throw new InvalidOperationException(T.L(
                $"No release of {repo.Display} matches the filter “{tagFilter}”.",
                $"Nenhum release de {repo.Display} combina com o filtro “{tagFilter}”."));

        // Only repositories without release files pay for the extra tags request.
        var tagsUrl = repo.Kind switch
        {
            ProviderKind.GitHub => $"https://api.github.com/repos/{repo.Path}/tags?per_page=100",
            ProviderKind.GitLab => $"https://{repo.Host}/api/v4/projects/{Uri.EscapeDataString(repo.Path)}/repository/tags?per_page=100",
            _ => $"https://{repo.Host}/api/v1/repos/{repo.Path}/tags?limit=100"
        };

        var (tagsJson, tagsError) = await TryGetAsync(tagsUrl, ct);
        if (tagsJson is null)
            throw new InvalidOperationException(T.L(
                $"The repository has no downloadable release files ({tagsError}).",
                $"O repositório não tem arquivos de release para baixar ({tagsError})."));

        return ResolveLatest(repo.Kind, repo, releasesJson, tagsJson, tagFilter);
    }

    /// <summary>
    /// The newest successful GitHub Actions build that still has files to download. Builds of pull
    /// requests and of forks are left out: they run code the repository's maintainers did not write.
    /// </summary>
    /// <param name="filter">Optional: only builds whose workflow, branch, title or number contains this text.</param>
    private async Task<ReleaseInfo> GetLatestBuildAsync(RepoRef repo, string? filter, CancellationToken ct)
    {
        if (repo.Kind != ProviderKind.GitHub)
            throw new InvalidOperationException(T.L(
                "Builds from an Actions page are only supported on GitHub. Use the repository link itself to follow its releases.",
                "Builds de uma página Actions só são suportados no GitHub. Use o link do próprio repositório para acompanhar os releases."));

        var api = $"https://api.github.com/repos/{repo.Path}/actions";
        var runsUrl = repo.Workflow.Length > 0
            ? $"{api}/workflows/{Uri.EscapeDataString(repo.Workflow)}/runs?status=success&per_page=100"
            : $"{api}/runs?status=success&per_page=100";

        var (runsJson, runsError) = await TryGetAsync(runsUrl, ct);
        if (runsJson is null)
            throw new HttpRequestException(T.L(
                $"Could not read the Actions builds of {repo.Display} ({runsError}).",
                $"Não foi possível ler os builds do Actions de {repo.Display} ({runsError})."));

        var allRuns = ParseRuns(runsJson);
        if (allRuns.Count == 0)
            throw new InvalidOperationException(T.L(
                $"{repo.Display} has no successful builds of its own to download.",
                $"{repo.Display} não tem builds próprios concluídos com sucesso para baixar."));

        var wanted = (filter ?? "").Trim();
        var runs = allRuns
            .Where(build => MatchesFilter(build.Probe, wanted))
            .OrderByDescending(build => build.Date)
            .ToList();
        if (runs.Count == 0)
            throw new InvalidOperationException(T.L(
                $"No build of {repo.Display} matches the filter “{wanted}” (it is compared with the workflow, the branch and the build title).",
                $"Nenhum build de {repo.Display} combina com o filtro “{wanted}” (ele é comparado com o workflow, o branch e o título do build)."));

        // Most runs of a repository publish nothing (tests, issue labelling...). One request lists the
        // newest files of the whole repository, which tells which builds have any.
        var (filesJson, filesError) = await TryGetAsync($"{api}/artifacts?per_page=100", ct);
        if (filesJson is null)
            throw new HttpRequestException(T.L(
                $"Could not read the build files of {repo.Display} ({filesError}).",
                $"Não foi possível ler os arquivos de build de {repo.Display} ({filesError})."));

        var filesByRun = ParseArtifacts(repo, filesJson)
            .GroupBy(artifact => artifact.RunId)
            .ToDictionary(perRun => perRun.Key, perRun => perRun.Select(artifact => artifact.File).ToList());

        foreach (var candidate in runs)
        {
            if (filesByRun.TryGetValue(candidate.Id, out var published) && published.Count > 0)
                return Build(new Candidate(candidate.Version, published, IsRelease: true, candidate.Date), NoteFor(candidate));
        }

        // A busy repository can have more recent files than that list reaches: ask the newest builds directly.
        foreach (var candidate in runs.Take(3))
        {
            var (json, _) = await TryGetAsync($"{api}/runs/{candidate.Id}/artifacts?per_page=100", ct);
            if (json is null) continue;

            var found = ParseArtifacts(repo, json).Select(artifact => artifact.File).ToList();
            if (found.Count > 0)
                return Build(new Candidate(candidate.Version, found, IsRelease: true, candidate.Date), NoteFor(candidate));
        }

        throw new InvalidOperationException(T.L(
            $"The successful builds of {repo.Display} have no files to download. GitHub deletes build files after a while (90 days by default).",
            $"Os builds concluídos de {repo.Display} não têm arquivos para baixar. O GitHub apaga os arquivos de build depois de um tempo (90 dias por padrão)."));
    }

    private static string NoteFor(Run build)
    {
        var branch = build.Branch.Length > 0 ? build.Branch : "?";
        var note = T.L(
            $"GitHub Actions build, not a release: workflow “{build.Workflow}”, branch “{branch}”.",
            $"Build do GitHub Actions, não um release: workflow “{build.Workflow}”, branch “{branch}”.");

        // GitHub only hands build files to logged-in users, even for public repositories.
        if (!HasToken)
        {
            note += " " + T.L(
                "No GITHUB_TOKEN is set, so the download goes through the nightly.link service.",
                "Sem GITHUB_TOKEN definido, o download passa pelo serviço nightly.link.");
        }

        return note;
    }

    /// <summary>Chooses the newest release (by publish date) or, failing that, the newest tag.</summary>
    internal static ReleaseInfo ResolveLatest(ProviderKind kind, RepoRef repo, string? releasesJson, string? tagsJson,
        string? tagFilter = null)
    {
        var releases = releasesJson is null
            ? new List<Candidate>()
            : ParseReleases(kind, releasesJson).Where(c => Matches(c, tagFilter)).ToList();

        // Releases are what can actually be installed, so the newest published one wins.
        // This also handles release names that are not version numbers, e.g. "build-178".
        if (releases.Count > 0)
            return Build(releases.OrderByDescending(c => c.Date).First());

        var tags = tagsJson is null
            ? new List<Candidate>()
            : ParseTags(repo, tagsJson).Where(c => Matches(c, tagFilter)).ToList();
        if (tags.Count == 0)
            throw new InvalidOperationException(T.L(
                "The repository has no releases or tags with downloadable files.",
                "O repositório não tem releases nem tags com arquivos para baixar."));

        var versioned = tags.Where(t => LooksLikeVersion(t.Version)).ToList();
        var chosen = versioned.Count > 0
            ? versioned.OrderByDescending(c => c, CandidateComparer.Instance).First()
            : tags[0];

        return Build(chosen);
    }

    private static bool Matches(Candidate candidate, string? tagFilter)
        => MatchesFilter(candidate.Version, tagFilter);

    /// <summary>
    /// True when the text contains the filter. Alternatives are separated by "|":
    /// "fritchii-v|frizti-v" accepts either prefix. An empty filter accepts everything.
    /// </summary>
    public static bool MatchesFilter(string text, string? filter)
    {
        var alternatives = (filter ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return alternatives.Length == 0 ||
               alternatives.Any(alternative => text.Contains(alternative, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(string? Json, string? Error)> TryGetAsync(string url, CancellationToken ct)
    {
        try
        {
            return (await Http.GetStringAsync(url, ct), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, T.L("timed out", "tempo esgotado"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var hint = url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase)
                ? T.L(" GitHub allows 60 anonymous requests per hour; set a GITHUB_TOKEN environment variable to raise the limit.",
                      " O GitHub permite 60 consultas anônimas por hora; defina a variável de ambiente GITHUB_TOKEN para aumentar o limite.")
                : "";
            return (null, T.L("API rate limit reached.", "Limite de consultas da API atingido.") + hint);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    // ------------------------------------------------------------ parsing

    private sealed record Candidate(string Version, List<Asset> Assets, bool IsRelease, DateTimeOffset Date);

    private sealed class CandidateComparer : IComparer<Candidate>
    {
        public static readonly CandidateComparer Instance = new();

        public int Compare(Candidate? x, Candidate? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return VersionUtil.Compare(x.Version, y.Version);
        }
    }

    private static IEnumerable<Candidate> ParseReleases(ProviderKind kind, string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                continue;

            var tag = GetString(release, "tag_name");
            if (string.IsNullOrWhiteSpace(tag)) continue;

            var assets = kind == ProviderKind.GitLab ? ReadGitLabAssets(release) : ReadReleaseAssets(release);
            if (assets.Count == 0) continue;

            var date = ReadDate(release,
                kind == ProviderKind.GitLab ? new[] { "released_at", "created_at" } : new[] { "published_at", "created_at" });

            yield return new Candidate(NormalizeTag(tag), assets, IsRelease: true, date);
        }
    }

    private static IEnumerable<Candidate> ParseTags(RepoRef repo, string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in doc.RootElement.EnumerateArray())
        {
            var name = GetString(tag, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var version = NormalizeTag(name);
            if (!seen.Add(version)) continue;

            var archiveUrl = repo.Kind == ProviderKind.GitLab
                ? GitLabArchiveUrl(repo, name, "zip")
                : GetString(tag, "zipball_url");

            if (string.IsNullOrWhiteSpace(archiveUrl))
            {
                archiveUrl = repo.Kind == ProviderKind.GitLab
                    ? GitLabArchiveUrl(repo, name, "tar.gz")
                    : GetString(tag, "tarball_url");
            }

            if (string.IsNullOrWhiteSpace(archiveUrl)) continue;

            yield return new Candidate(version,
                new List<Asset> { new(version + ".zip", archiveUrl, 0) },
                IsRelease: false,
                Date: ReadDate(tag, new[] { "created_at" }));
        }
    }

    private sealed record Run(long Id, string Version, string Workflow, string Branch, string Probe, DateTimeOffset Date);

    private sealed record Artifact(long RunId, Asset File);

    private static List<Run> ParseRuns(string json)
    {
        var runs = new List<Run>();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("workflow_runs", out var array) ||
            array.ValueKind != JsonValueKind.Array)
            return runs;

        foreach (var run in array.EnumerateArray())
        {
            if (run.ValueKind != JsonValueKind.Object) continue;

            var id = GetLong(run, "id");
            if (id is null) continue;

            // Pull request builds run the contributor's code, not the repository's.
            if (GetString(run, "event").StartsWith("pull_request", StringComparison.OrdinalIgnoreCase)) continue;

            var conclusion = GetString(run, "conclusion");
            if (conclusion.Length > 0 && !conclusion.Equals("success", StringComparison.OrdinalIgnoreCase)) continue;

            // The same goes for a build that ran from a fork.
            var home = run.TryGetProperty("repository", out var homeRepo) ? GetString(homeRepo, "full_name") : "";
            var head = run.TryGetProperty("head_repository", out var headRepo) ? GetString(headRepo, "full_name") : "";
            if (home.Length > 0 && head.Length > 0 && !home.Equals(head, StringComparison.OrdinalIgnoreCase)) continue;

            var number = GetLong(run, "run_number") ?? 0;
            var sha = GetString(run, "head_sha");
            if (sha.Length > 7) sha = sha[..7];

            var workflow = GetString(run, "name");
            var branch = GetString(run, "head_branch");
            var version = sha.Length > 0 ? $"build-{number}-{sha}" : $"build-{number}";
            var probe = string.Join(" ", new[] { workflow, GetString(run, "path"), branch, version, GetString(run, "display_title") });

            runs.Add(new Run(id.Value, version, workflow, branch, probe,
                ReadDate(run, new[] { "updated_at", "run_started_at", "created_at" })));
        }

        return runs;
    }

    /// <summary>The files (artifacts) of one or more builds that can still be downloaded.</summary>
    private static List<Artifact> ParseArtifacts(RepoRef repo, string json)
    {
        var artifacts = new List<Artifact>();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("artifacts", out var array) ||
            array.ValueKind != JsonValueKind.Array)
            return artifacts;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (item.TryGetProperty("expired", out var expired) && expired.ValueKind == JsonValueKind.True) continue;

            var id = GetLong(item, "id");
            var name = GetString(item, "name");
            if (id is null || name.Length == 0) continue;

            long runId = 0;
            if (item.TryGetProperty("workflow_run", out var run) && run.ValueKind == JsonValueKind.Object)
            {
                runId = GetLong(run, "id") ?? 0;

                var homeId = GetLong(run, "repository_id");
                var headId = GetLong(run, "head_repository_id");
                if (homeId is not null && headId is not null && homeId != headId) continue; // built from a fork
            }

            // GitHub always delivers a build file as a zip, whatever the workflow uploaded.
            var fileName = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : name + ".zip";

            // With a token the file comes straight from GitHub. Without one GitHub refuses, and
            // nightly.link (an open-source service made for exactly this) hands out the same file.
            var url = HasToken
                ? $"https://api.github.com/repos/{repo.Path}/actions/artifacts/{id.Value}/zip"
                : $"https://nightly.link/{repo.Path}/actions/artifacts/{id.Value}.zip";

            artifacts.Add(new Artifact(runId, new Asset(fileName, url, GetLong(item, "size_in_bytes") ?? 0,
                ReadDate(item, new[] { "updated_at", "created_at" }))));
        }

        return artifacts;
    }

    private static long? GetLong(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt64(out var number)
            ? number
            : null;

    private static ReleaseInfo Build(Candidate candidate, string note = "")
    {
        var installable = candidate.Assets.Where(IsInstallable).ToList();
        if (installable.Count == 0) installable = candidate.Assets;

        var ordered = installable.OrderByDescending(Score).ToList();

        var assets = ordered
            .Select(a => new ReleaseAsset(
                string.IsNullOrWhiteSpace(a.Name) ? NameFromUrl(a.Url) : a.Name,
                a.Url,
                a.Size))
            .ToList();

        // Rolling releases ("latest", "continuous") replace their files under the same tag,
        // so the newest file date is the better "when was this build published".
        var stamp = candidate.Date;
        foreach (var asset in candidate.Assets)
        {
            if (asset.Date > stamp) stamp = asset.Date;
        }

        return new ReleaseInfo(candidate.Version, assets, IsSourceArchive: !candidate.IsRelease, stamp, note);
    }

    private static readonly string[] NotInstallable =
    {
        ".sha256", ".sha1", ".sha512", ".md5", ".sig", ".asc", ".pdb",
        ".json", ".txt", ".xml", ".yml", ".yaml", ".zsync"
    };

    /// <summary>Checksums, signatures and other sidecar files are left out of the download list.</summary>
    private static bool IsInstallable(Asset asset)
    {
        var name = (asset.Name.Length > 0 ? asset.Name : NameFromUrl(asset.Url)).ToLowerInvariant();
        return !NotInstallable.Any(extension => name.EndsWith(extension, StringComparison.Ordinal));
    }

    private static string GitLabArchiveUrl(RepoRef repo, string tag, string format)
        => $"https://{repo.Host}/api/v4/projects/{Uri.EscapeDataString(repo.Path)}/repository/archive.{format}?sha={Uri.EscapeDataString(tag)}";

    private static List<Asset> ReadReleaseAssets(JsonElement release)
    {
        var assets = new List<Asset>();

        if (release.TryGetProperty("assets", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in array.EnumerateArray())
            {
                assets.Add(new Asset(
                    GetString(asset, "name"),
                    GetString(asset, "browser_download_url"),
                    asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0,
                    ReadDate(asset, new[] { "updated_at", "created_at" })));
            }
        }

        assets.RemoveAll(a => string.IsNullOrWhiteSpace(a.Url));
        return assets;
    }

    private static List<Asset> ReadGitLabAssets(JsonElement release)
    {
        var assets = new List<Asset>();

        if (!release.TryGetProperty("assets", out var assetsEl))
            return assets;

        if (assetsEl.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var href = GetString(link, "direct_asset_url");
                if (string.IsNullOrWhiteSpace(href)) href = GetString(link, "url");

                assets.Add(new Asset(GetString(link, "name"), href, 0));
            }
        }

        // Fall back to the auto-generated source archives.
        if (assets.Count == 0 &&
            assetsEl.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
        {
            foreach (var source in sources.EnumerateArray())
            {
                var href = GetString(source, "url");
                if (string.IsNullOrWhiteSpace(href)) continue;
                assets.Add(new Asset(NameFromUrl(href), href, 0));
            }
        }

        assets.RemoveAll(a => string.IsNullOrWhiteSpace(a.Url));
        return assets;
    }

    private static string GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static DateTimeOffset ReadDate(JsonElement element, string[] properties)
    {
        foreach (var property in properties)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var date))
            {
                return date;
            }
        }

        return DateTimeOffset.MinValue;
    }

    private static string NormalizeTag(string tag)
    {
        var version = tag.Trim();
        if (version.StartsWith('v') || version.StartsWith('V')) version = version[1..];
        return version.Length == 0 ? tag.Trim() : version;
    }

    private static readonly Regex VersionTag = new(@"^v?\d+(\.\d+)*([a-zA-Z][\w.\-]*|-[a-zA-Z0-9][\w.\-]*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Filters out tags such as "build-178", "nightly" or "mkw-2026-10-30-pre".</summary>
    private static bool LooksLikeVersion(string tag) => VersionTag.IsMatch(tag.Trim());

    private static string NameFromUrl(string url)
    {
        try
        {
            return Path.GetFileName(new Uri(url).AbsolutePath);
        }
        catch
        {
            return "release";
        }
    }

    private sealed record Asset(string Name, string Url, long Size, DateTimeOffset Date = default);

    // ------------------------------------------------------------ asset choice

    private static readonly string[] OtherPlatforms =
    {
        "linux", "macos", "mac-os", "osx", "darwin", "android", "ios", "freebsd", "appimage", "flatpak", "deb", "rpm"
    };

    private static readonly string[] Noise =
    {
        "symbols", "debug", "pdb", "source", "srccode", "checksum", "sha256", "sig", "asc", "json", "txt", "md5"
    };

    /// <summary>Ranks release assets so a Windows archive is offered first when one is published.</summary>
    private static int Score(Asset asset)
    {
        var probe = asset.Name.Length > 0 ? asset.Name.ToLowerInvariant() : NameFromUrl(asset.Url).ToLowerInvariant();

        var score = 0;

        if (probe.Contains("win") || probe.Contains("windows") || probe.Contains("x86_64") || probe.Contains("amd64")) score += 6;
        if (probe.Contains("x86_64") || probe.Contains("amd64") || probe.Contains("x64")) score += 4;
        if (probe.Contains("i686") || probe.Contains("i386") || probe.Contains("win32") || probe.Contains("x86-32")) score -= 6;
        if (OtherPlatforms.Any(p => probe.Contains(p))) score -= 40;
        if (Noise.Any(p => probe.Contains(p))) score -= 40;

        if (probe.Contains("arm64") || probe.Contains("aarch64")) score -= 8;
        if (probe.Contains("installer") || probe.Contains("setup") || probe.Contains("_install")) score -= 12;

        if (probe.EndsWith(".zip")) score += 5;
        else if (probe.EndsWith(".7z")) score += 5;
        else if (probe.EndsWith(".tar.gz") || probe.EndsWith(".tgz")) score += 3;
        else if (probe.EndsWith(".msi") || probe.EndsWith(".exe")) score -= 6;
        else score -= 3;

        return score;
    }
}
