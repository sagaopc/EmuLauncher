using System.IO;
using System.Net.Http;
using SharpCompress.Archives;

namespace EmuLauncher.Services;

public sealed record UpdateProgress(double Percent, string Message);

/// <summary>
/// Downloads a release, extracts it into a temporary staging folder and only then
/// copies it over the installation directory. The existing installation is never
/// removed before a complete, verified download is on disk.
/// </summary>
public sealed class UpdateService
{
    private static readonly HttpClient Download = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly RepoClient _repo;

    public UpdateService(RepoClient repo) => _repo = repo;

    /// <param name="choose">Asks which published file to install; returning null cancels the update.</param>
    public async Task<ReleaseInfo> UpdateAsync(string repoUrl, string? tagFilter, string installationDirectory,
        IProgress<UpdateProgress>? progress, CancellationToken ct, Func<ReleaseInfo, Task<ReleaseAsset?>>? choose = null)
    {
        var repo = RepoClient.TryParse(repoUrl)
                   ?? throw new InvalidOperationException(T.L(
                       "The repository URL is not valid.", "A URL do repositório não é válida."));

        if (string.IsNullOrWhiteSpace(installationDirectory))
            throw new InvalidOperationException(T.L(
                "No installation directory is configured.", "Nenhuma pasta de instalação está configurada."));

        progress?.Report(new UpdateProgress(0, T.L("Checking for the latest release…", "Procurando a versão mais recente…")));
        var release = await _repo.GetLatestAsync(repo, tagFilter, ct);

        // Let the user pick which published file to install.
        var asset = choose is null ? release.Preferred : await choose(release);
        if (asset is null)
            throw new OperationCanceledException();

        var installDir = Path.GetFullPath(installationDirectory);
        Directory.CreateDirectory(installDir);

        var tempRoot = Path.Combine(Path.GetTempPath(), "EmuLauncher", Guid.NewGuid().ToString("n"));
        var staging = Path.Combine(tempRoot, "staging");
        Directory.CreateDirectory(staging);
        var extension = ExtensionOf(asset);
        var archivePath = Path.Combine(tempRoot, "release" + extension);

        try
        {
            progress?.Report(new UpdateProgress(0, T.L($"Downloading {asset.Name}…", $"Baixando {asset.Name}…")));
            await DownloadAsync(asset, archivePath, progress, ct);

            ct.ThrowIfCancellationRequested();

            var extracted = staging;

            if (extension == ".exe")
            {
                // Some emulators publish one portable .exe instead of an archive: it is the whole install.
                var fileName = SafeFileName(asset.Name, "emulator.exe");
                File.Copy(archivePath, Path.Combine(staging, fileName), overwrite: true);
            }
            else
            {
                progress?.Report(new UpdateProgress(60, T.L("Extracting release…", "Extraindo o release…")));
                await Task.Run(() => Extract(archivePath, staging, progress, ct), ct);

                // GitHub wraps every Actions build file in a zip of its own, so what comes out is
                // often the real archive (a .7z, or a second .zip). Open that as well.
                for (var level = 1; level <= 2; level++)
                {
                    var inner = SingleArchiveIn(extracted);
                    if (inner is null) break;

                    var next = Path.Combine(tempRoot, "staging" + level);
                    Directory.CreateDirectory(next);
                    await Task.Run(() => Extract(inner, next, progress, ct), ct);
                    extracted = next;
                }
            }

            var source = FlattenSingleRoot(extracted);

            ct.ThrowIfCancellationRequested();
            progress?.Report(new UpdateProgress(85, T.L("Installing files…", "Instalando os arquivos…")));
            await Task.Run(() => CopyTree(source, installDir, progress, ct), ct);

            progress?.Report(new UpdateProgress(100, T.L("Done.", "Concluído.")));
            return release;
        }
        finally
        {
            TryDelete(tempRoot);
        }
    }

    private static string SafeFileName(string? name, string fallback)
    {
        var file = Path.GetFileName((name ?? "").Trim());
        if (string.IsNullOrWhiteSpace(file)) return fallback;

        foreach (var invalid in Path.GetInvalidFileNameChars())
            file = file.Replace(invalid, '_');

        return file;
    }

    private static readonly string[] ArchiveExtensions =
    {
        ".zip", ".7z", ".rar", ".tar.gz", ".tgz", ".tar.xz", ".tar.bz2", ".tar"
    };

    private static readonly string[] Sidecars =
    {
        ".sha256", ".sha1", ".sha512", ".md5", ".sig", ".asc", ".txt", ".json", ".zsync"
    };

    /// <summary>
    /// The archive inside an extracted folder when that folder holds nothing else but one archive
    /// (checksums and notes next to it do not count). Null when the folder already is the install.
    /// </summary>
    private static string? SingleArchiveIn(string folder)
    {
        string? archive = null;

        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file).ToLowerInvariant();

            if (ArchiveExtensions.Any(extension => name.EndsWith(extension, StringComparison.Ordinal)))
            {
                if (archive is not null) return null; // several archives: nothing to guess
                archive = file;
            }
            else if (!Sidecars.Any(extension => name.EndsWith(extension, StringComparison.Ordinal)))
            {
                return null; // real files next to it
            }
        }

        return archive;
    }

    private static string ExtensionOf(ReleaseAsset asset)
    {
        var name = asset.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = Path.GetFileName(new Uri(asset.Url).AbsolutePath); }
            catch { name = ""; }
        }

        var lower = name.ToLowerInvariant();
        foreach (var ext in new[] { ".tar.gz", ".tar.xz", ".tar.bz2", ".zip", ".7z", ".rar", ".tgz", ".tar", ".exe" })
            if (lower.EndsWith(ext, StringComparison.Ordinal)) return ext;

        return ".bin";
    }

    private static async Task DownloadAsync(ReleaseAsset asset, string destination, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        request.Headers.UserAgent.ParseAdd("EmuLauncher/1.0");
        RepoClient.Authorize(request); // only adds the GitHub token to downloads from the GitHub API

        using var response = await Download.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(T.L(
                $"Download failed ({(int)response.StatusCode} {response.ReasonPhrase}).",
                $"O download falhou ({(int)response.StatusCode} {response.ReasonPhrase}).") + DownloadHint(asset.Url));

        var total = response.Content.Headers.ContentLength;

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long received = 0;
        var lastReport = 0L;
        int read;

        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;

            var now = Environment.TickCount64;
            if (now - lastReport < 150 && (total is null || received < total)) continue;
            lastReport = now;

            var percent = total is > 0 ? received * 60.0 / total.Value : 0;
            var verb = T.L("Downloading…", "Baixando…");
            progress?.Report(new UpdateProgress(percent, total is > 0
                ? $"{verb} {Megabytes(received)} / {Megabytes(total.Value)} MB ({received * 100 / total.Value}%)"
                : $"{verb} {Megabytes(received)} MB"));
        }

        if (received == 0)
            throw new InvalidOperationException(T.L(
                "The downloaded release file was empty.", "O arquivo baixado veio vazio."));
    }

    /// <summary>Why the download of an Actions build file may have been refused, and what to do about it.</summary>
    private static string DownloadHint(string url)
    {
        if (url.Contains("://nightly.link/", StringComparison.OrdinalIgnoreCase))
        {
            return " " + T.L(
                "GitHub only hands Actions build files to logged-in users, so without a token the launcher asks the nightly.link service, and it did not deliver this one. Set a GITHUB_TOKEN environment variable to download straight from GitHub.",
                "O GitHub só entrega arquivos de build do Actions a quem está logado, então sem token o launcher pede ao serviço nightly.link, e ele não entregou este. Defina a variável de ambiente GITHUB_TOKEN para baixar direto do GitHub.");
        }

        if (url.Contains("://api.github.com/", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("/actions/artifacts/", StringComparison.OrdinalIgnoreCase))
        {
            return " " + T.L(
                "GitHub refused the GITHUB_TOKEN for this build file. A fine-grained token needs the “Actions: read” permission; the file may also have expired.",
                "O GitHub recusou o GITHUB_TOKEN para este arquivo de build. Um token fine-grained precisa da permissão “Actions: read”; o arquivo também pode ter expirado.");
        }

        return "";
    }

    private static string Megabytes(long bytes) => (bytes / 1024d / 1024d).ToString("0.0");

    private static void Extract(string archivePath, string staging, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        var stagingFull = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;

        IArchive archive;
        try
        {
            archive = ArchiveFactory.OpenArchive(archivePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(T.L(
                $"The release file could not be opened as an archive ({ex.Message}).",
                $"Não foi possível abrir o arquivo do release como um pacote ({ex.Message})."), ex);
        }

        using (archive)
        {
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            if (entries.Count == 0)
                throw new InvalidOperationException(T.L("The release archive is empty.", "O pacote do release está vazio."));

            var done = 0;
            var skipped = 0;
            var lastReport = 0L;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                var key = (entry.Key ?? "").Replace('\\', '/').TrimStart('/');
                done++;

                if (key.Length == 0 || key.Contains("..", StringComparison.Ordinal))
                    continue;

                // Unix symlinks have no useful representation on Windows.
                if (!string.IsNullOrEmpty(entry.LinkTarget))
                {
                    skipped++;
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(staging, key.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var source = entry.OpenEntryStream();
                    using var destination = File.Create(target);
                    source.CopyTo(destination);
                }
                catch
                {
                    // Unreadable entries (e.g. Unix symlinks) are skipped rather than aborting the update.
                    skipped++;
                }

                var now = Environment.TickCount64;
                if (now - lastReport < 120 && done < entries.Count) continue;
                lastReport = now;
                progress?.Report(new UpdateProgress(60 + 25.0 * done / entries.Count,
                    T.L($"Extracting… {done} / {entries.Count} files", $"Extraindo… {done} / {entries.Count} arquivos")));
            }

            if (skipped > 0 && skipped == entries.Count)
                throw new InvalidOperationException(T.L(
                    "No files could be extracted from the release archive.",
                    "Nenhum arquivo pôde ser extraído do pacote do release."));
        }
    }

    /// <summary>Archives usually wrap everything in a single versioned folder; drop that level.</summary>
    private static string FlattenSingleRoot(string staging)
    {
        var directories = Directory.GetDirectories(staging);
        var files = Directory.GetFiles(staging);
        return directories.Length == 1 && files.Length == 0 ? directories[0] : staging;
    }

    private static void CopyTree(string source, string destination, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
            throw new InvalidOperationException(T.L(
                "The release archive did not contain any files.", "O pacote do release não continha arquivos."));

        for (var i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(source, files[i]);
            var target = Path.Combine(destination, relative);

            try
            {
                var folder = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.Copy(files[i], target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(T.L(
                    $"Could not write “{relative}”. Close the emulator if it is running and check that the folder is writable.\n\n{ex.Message}",
                    $"Não foi possível gravar “{relative}”. Feche o emulador se ele estiver aberto e confira se a pasta permite gravação.\n\n{ex.Message}"), ex);
            }

            progress?.Report(new UpdateProgress(85 + 14.0 * (i + 1) / files.Length,
                T.L($"Installing… {i + 1} / {files.Length} files", $"Instalando… {i + 1} / {files.Length} arquivos")));
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // Temporary files are harmless; the OS will clean them up eventually.
        }
    }
}
