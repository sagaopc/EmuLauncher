using System.Diagnostics;
using System.IO;
using System.Windows;
using EmuLauncher.Models;
using EmuLauncher.Services;

namespace EmuLauncher;

/// <summary>Everything that touches an emulator: checking releases, downloading, launching, editing.</summary>
public partial class MainWindow
{
    // ---------------------------------------------------------------- checks

    private static bool HasRepository(Emulator emulator) => !string.IsNullOrWhiteSpace(emulator.RepoUrl);

    /// <summary>The status an emulator has before (or without) a release check.</summary>
    private static void ResetStatus(Emulator emulator)
    {
        emulator.UpdateAvailable = false;
        emulator.StatusDetail = "";

        if (!emulator.IsInstalled)
            emulator.SetStatus(T.L("Not installed", "Não instalado"), StatusBrushes.Muted);
        else
            emulator.SetStatus("", StatusBrushes.Muted);
    }

    private async Task CheckAllAsync(bool showResult)
    {
        if (_checking) return;

        _checking = true;
        var failures = 0;

        try
        {
            foreach (var emulator in Emulators.ToList())
            {
                if (!HasRepository(emulator)) continue;

                SetStatus(T.L($"Checking {emulator.Name}…", $"Verificando {emulator.Name}…"));
                if (!await CheckOneAsync(emulator)) failures++;
            }
        }
        finally
        {
            _checking = false;
        }

        var updates = Emulators.Count(emulator => emulator.UpdateAvailable);

        if (failures > 0)
        {
            SetStatus(failures == 1
                ? T.L("1 repository could not be checked.", "1 repositório não pôde ser verificado.")
                : T.L($"{failures} repositories could not be checked.", $"{failures} repositórios não puderam ser verificados."));
        }
        else if (updates > 0)
        {
            SetStatus(updates == 1
                ? T.L("1 update available.", "1 atualização disponível.")
                : T.L($"{updates} updates available.", $"{updates} atualizações disponíveis."));
        }
        else
        {
            SetStatus(showResult
                ? T.L("Everything is up to date.", "Tudo atualizado.")
                : T.L("Ready.", "Pronto."));
        }
    }

    private async Task<bool> CheckOneAsync(Emulator emulator, CancellationToken ct = default)
    {
        if (!HasRepository(emulator))
        {
            ResetStatus(emulator);
            return true;
        }

        var repo = RepoClient.TryParse(emulator.RepoUrl);
        if (repo is null)
        {
            emulator.LatestVersion = "";
            emulator.UpdateAvailable = false;
            emulator.StatusDetail = T.L(
                "The repository URL could not be understood. It should look like https://github.com/owner/repo.",
                "Não deu para entender a URL do repositório. Ela deve ser parecida com https://github.com/dono/repo.");
            emulator.SetStatus(T.L("Invalid repository URL", "URL do repositório inválida"), StatusBrushes.Error);
            return false;
        }

        emulator.SetStatus(T.L("Checking…", "Verificando…"), StatusBrushes.Muted);
        emulator.StatusDetail = "";

        try
        {
            var release = await _repo.GetLatestAsync(repo, emulator.TagFilter, ct);
            ApplyRelease(emulator, release);
            return true;
        }
        catch (OperationCanceledException)
        {
            ResetStatus(emulator);
            return false;
        }
        catch (Exception ex)
        {
            emulator.LatestVersion = "";
            emulator.UpdateAvailable = false;
            emulator.StatusDetail = ex.Message;
            emulator.SetStatus(T.L("Check failed", "Falha ao verificar"), StatusBrushes.Error);
            return false;
        }
    }

    /// <summary>Sets the status from the newest release compared with what is installed.</summary>
    private static void ApplyRelease(Emulator emulator, ReleaseInfo release)
    {
        emulator.LatestVersion = release.Version;
        emulator.StatusDetail = "";

        if (!emulator.IsInstalled)
        {
            emulator.UpdateAvailable = false;
            emulator.SetStatus(T.L("Not installed", "Não instalado"), StatusBrushes.Muted);
            return;
        }

        var order = CompareWithInstalled(emulator, release);
        if (order > 0)
        {
            emulator.UpdateAvailable = true;
            emulator.SetStatus(
                release.IsSourceArchive ? T.L("New tag", "Nova tag") : T.L("Update available", "Atualização disponível"),
                StatusBrushes.Update);
        }
        else if (order < 0)
        {
            emulator.UpdateAvailable = false;
            emulator.SetStatus(T.L("Newer than the release", "Mais novo que o release"), StatusBrushes.Muted);
        }
        else
        {
            emulator.UpdateAvailable = false;
            emulator.SetStatus(T.L("Up to date", "Atualizado"), StatusBrushes.Ok);
        }
    }

    /// <summary>Positive when the release is newer than the installation, negative when older, zero when the same.</summary>
    private static int CompareWithInstalled(Emulator emulator, ReleaseInfo release)
    {
        var installed = emulator.InstalledVersion;
        if (string.IsNullOrWhiteSpace(installed)) return 1;

        var tolerance = TimeSpan.FromMinutes(2);
        var stamps = emulator.InstalledStamp is not null && release.Stamp > DateTimeOffset.MinValue;
        var installedStamp = emulator.InstalledStamp ?? DateTimeOffset.MinValue;

        if (string.Equals(installed.Trim(), release.Version.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            // Same tag. Only a rolling release ("latest", "continuous") can still carry a newer build.
            return stamps && release.Stamp - installedStamp > tolerance ? 1 : 0;
        }

        // The publish dates are the most reliable order when both are known.
        if (stamps)
        {
            if (release.Stamp - installedStamp > tolerance) return 1;
            if (installedStamp - release.Stamp > tolerance) return -1;
            return 0;
        }

        if (VersionUtil.IsNumbered(installed) && VersionUtil.IsNumbered(release.Version))
            return VersionUtil.Compare(release.Version, installed);

        // Names that cannot be ordered ("build-0a1b2c"): the newest published release is taken as newer.
        return 1;
    }

    // ---------------------------------------------------------------- launch

    /// <summary>A on a tile, or the big button: start it when it is installed, download it otherwise.</summary>
    private async void RunPrimary(Emulator emulator)
    {
        if (OverlayOpen) return;

        if (emulator.IsInstalled) await LaunchAsync(emulator);
        else await UpdateAsync(emulator);
    }

    private async void RunUpdate(Emulator emulator)
    {
        if (OverlayOpen) return;
        await UpdateAsync(emulator);
    }

    private async Task LaunchAsync(Emulator emulator)
    {
        // Already running: show it instead of starting a second copy.
        if (_running.TryGetValue(emulator.Id, out var existing))
        {
            var alive = false;
            try { alive = !existing.HasExited; }
            catch { /* treated as gone */ }

            if (alive)
            {
                Native.BringToFront(existing);
                return;
            }

            _running.Remove(emulator.Id);
            emulator.IsRunning = false;
        }

        if (!emulator.IsInstalled)
        {
            await MessageAsync(
                T.L("Cannot start", "Não foi possível abrir"),
                T.L($"The executable could not be found:\n{emulator.ExePath}\n\nOpen Options > Edit and point it at the right file.",
                    $"O executável não foi encontrado:\n{emulator.ExePath}\n\nAbra Opções > Editar e aponte para o arquivo certo."));
            return;
        }

        try
        {
            var workingDirectory = Directory.Exists(emulator.InstallDir)
                ? emulator.InstallDir
                : Path.GetDirectoryName(emulator.ExePath) ?? "";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = emulator.ExePath,
                Arguments = emulator.Arguments ?? "",
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            });

            emulator.LastPlayed = DateTimeOffset.Now;
            SaveStore();
            SetStatus(T.L($"Started {emulator.Name}.", $"{emulator.Name} foi aberto."));

            if (process is not null) Track(emulator, process);
        }
        catch (Exception ex)
        {
            await MessageAsync(
                T.L("Could not start", "Falha ao abrir"),
                T.L($"{emulator.Name} could not be started:\n\n{ex.Message}",
                    $"Não foi possível abrir o {emulator.Name}:\n\n{ex.Message}"));
        }
    }

    /// <summary>Watches a started emulator so the launcher can come back to the front when it closes.</summary>
    private void Track(Emulator emulator, Process process)
    {
        var started = DateTime.UtcNow;

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
                Dispatcher.BeginInvoke(new Action(() => OnEmulatorExited(emulator, process, started)));

            _running[emulator.Id] = process;
            emulator.IsRunning = true;

            // It may have ended before the event was attached.
            if (process.HasExited) OnEmulatorExited(emulator, process, started);
        }
        catch
        {
            // An emulator that runs elevated cannot be watched; it simply is not tracked.
            _running.Remove(emulator.Id);
            emulator.IsRunning = false;
        }
    }

    private void OnEmulatorExited(Emulator emulator, Process process, DateTime started)
    {
        if (!_running.TryGetValue(emulator.Id, out var current) || !ReferenceEquals(current, process))
            return;

        _running.Remove(emulator.Id);
        emulator.IsRunning = false;

        try { process.Dispose(); }
        catch { /* nothing to release */ }

        // Come back to the front after a play session. A process that ends within seconds is
        // usually a stub that started the real emulator, and that must keep the screen.
        if (_running.Count == 0 && DateTime.UtcNow - started > TimeSpan.FromSeconds(15))
        {
            if (WindowState == WindowState.Minimized)
                WindowState = _fullscreen ? WindowState.Maximized : WindowState.Normal;
            Activate();
        }
    }

    // ------------------------------------------------------ download, update

    /// <summary>
    /// Downloads the newest release into the emulator's folder (the first install and every later
    /// update are the same operation). Returns true when files were installed.
    /// </summary>
    private async Task<bool> UpdateAsync(Emulator emulator)
    {
        if (_busy || OverlayOpen) return false;

        if (!HasRepository(emulator))
        {
            await MessageAsync(
                T.L("Nothing to download", "Nada para baixar"),
                T.L($"{emulator.Name} has no repository configured, so the launcher can only start it. Add one in Options > Edit to get updates.",
                    $"O {emulator.Name} não tem repositório configurado, então o launcher só consegue abri-lo. Adicione um em Opções > Editar para receber atualizações."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(emulator.InstallDir))
        {
            await MessageAsync(
                T.L("Cannot update", "Não foi possível atualizar"),
                T.L("No installation directory is configured for this emulator.",
                    "Nenhuma pasta de instalação está configurada para este emulador."));
            return false;
        }

        if (emulator.IsRunning)
        {
            await MessageAsync(
                T.L("Close the emulator first", "Feche o emulador primeiro"),
                T.L($"{emulator.Name} is running. Its files cannot be replaced until it is closed.",
                    $"O {emulator.Name} está em execução. Os arquivos só podem ser trocados depois de fechá-lo."));
            return false;
        }

        _busy = true;
        _updateCts = new CancellationTokenSource();
        var isNew = !Emulators.Contains(emulator);

        ShowProgress(
            isNew || !emulator.IsInstalled
                ? T.L($"Installing {emulator.Name}", $"Instalando {emulator.Name}")
                : T.L($"Updating {emulator.Name}", $"Atualizando {emulator.Name}"),
            T.L("Checking for the latest release…", "Procurando a versão mais recente…"));

        var progress = new Progress<UpdateProgress>(ReportProgress);

        try
        {
            var release = await _updater.UpdateAsync(
                emulator.RepoUrl, emulator.TagFilter, emulator.InstallDir, progress, _updateCts.Token,
                latest => PickAssetAsync(emulator, latest));

            emulator.InstalledVersion = release.Version;
            emulator.InstalledStamp = release.Stamp > DateTimeOffset.MinValue ? release.Stamp : null;

            // Find what to run on a first install, or when the old executable is gone after an update.
            if (!emulator.IsInstalled)
            {
                var found = ExeFinder.Find(emulator.InstallDir, ExeHints(emulator));
                if (found is not null) emulator.ExePath = found;
            }

            emulator.RefreshInstalled();
            ApplyRelease(emulator, release);
            LoadIcon(emulator);

            if (isNew) Emulators.Insert(0, emulator);
            SaveStore();
            RefreshCatalogState();

            HideProgress();
            SetStatus(T.L($"{emulator.Name} {release.Version} installed.", $"{emulator.Name} {release.Version} instalado."));

            if (!emulator.IsInstalled)
            {
                await MessageAsync(
                    T.L("Installed, but what should be started?", "Instalado, mas o que abrir?"),
                    T.L($"The files are in {emulator.InstallDir}, but the launcher could not tell which program to start. Choose it in Options > Edit.",
                        $"Os arquivos estão em {emulator.InstallDir}, mas o launcher não soube qual programa abrir. Escolha em Opções > Editar."));
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            HideProgress();
            SetStatus(T.L("Cancelled. Nothing that was installed has been removed.",
                "Cancelado. Nada do que já estava instalado foi removido."));
            return false;
        }
        catch (Exception ex)
        {
            HideProgress();
            SetStatus(T.L("The download failed. Nothing that was installed has been removed.",
                "O download falhou. Nada do que já estava instalado foi removido."));
            await MessageAsync(
                T.L($"{emulator.Name} could not be installed", $"Não foi possível instalar o {emulator.Name}"),
                ex.Message);
            return false;
        }
        finally
        {
            _busy = false;
            _updateCts?.Dispose();
            _updateCts = null;
            if (_progressActive) HideProgress();
        }
    }

    /// <summary>Asks which published file to install. The first one is the best match for this PC.</summary>
    private async Task<ReleaseAsset?> PickAssetAsync(Emulator emulator, ReleaseInfo release)
    {
        if (release.Assets.Count == 0) return null;

        var options = release.Assets
            .Select((asset, index) => new DialogOption(
                asset.Name,
                FormatSize(asset.Size),
                index == 0 && release.Assets.Count > 1 ? T.L("Recommended", "Recomendado") : null))
            .ToList();

        var body = release.Assets.Count == 1
            ? T.L("One file was published.", "Um arquivo foi publicado.")
            : T.L($"{release.Assets.Count} files were published. Choose the one to install.",
                  $"{release.Assets.Count} arquivos foram publicados. Escolha qual instalar.");

        if (release.IsSourceArchive)
        {
            body += " " + T.L(
                "This version only exists as a tag, so the download is source code, not a ready-to-run build.",
                "Esta versão só existe como tag, então o download é o código-fonte, não um build pronto para usar.");
        }

        if (release.Note.Length > 0) body += "\n" + release.Note;

        body += "\n" + T.L($"It goes to {emulator.InstallDir}", $"Vai para {emulator.InstallDir}");

        var choice = await DialogAsync(
            T.L($"{emulator.Name} · version {release.Version}", $"{emulator.Name} · versão {release.Version}"),
            body, options);

        return choice >= 0 ? release.Assets[choice] : null;
    }

    private IEnumerable<string> ExeHints(Emulator emulator)
    {
        var entry = CatalogItems
            .Select(item => item.Entry)
            .FirstOrDefault(candidate => MatchesCatalog(emulator, candidate));

        return entry?.Exe ?? new List<string>();
    }

    // --------------------------------------------------------------- catalog

    private static bool MatchesCatalog(Emulator emulator, CatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(emulator.CatalogId))
            return string.Equals(emulator.CatalogId, entry.Id, StringComparison.OrdinalIgnoreCase);

        // Emulators added by hand (or imported from Emulator Manager) are matched by repository.
        // The Actions link of a repository is a different source than its releases (Display tells them apart).
        var mine = RepoClient.TryParse(emulator.RepoUrl);
        var theirs = RepoClient.TryParse(entry.RepoUrl);
        if (mine is null || theirs is null ||
            !string.Equals(mine.Display, theirs.Display, StringComparison.OrdinalIgnoreCase))
            return false;

        // An emulator without a filter (Emulator Manager had none) is that catalog entry;
        // one with its own filter only when the filters agree.
        var filter = (emulator.TagFilter ?? "").Trim();
        return filter.Length == 0 ||
               string.Equals(filter, (entry.TagFilter ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshCatalogState()
    {
        foreach (var item in CatalogItems)
        {
            var mine = Emulators.FirstOrDefault(emulator => MatchesCatalog(emulator, item.Entry));
            item.Installed = mine is not null;
            item.State = mine is null
                ? T.L("Download", "Baixar")
                : mine.IsInstalled
                    ? T.L("In your library", "Na sua biblioteca")
                    : T.L("In your library, not installed", "Na biblioteca, não instalado");
        }
    }

    private async void Catalog_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CatalogItem item) return;
        if (OverlayOpen) return;

        var existing = Emulators.FirstOrDefault(emulator => MatchesCatalog(emulator, item.Entry));
        if (existing is not null && existing.IsInstalled)
        {
            SelectEmulator(existing);
            ShowPage(Screen.Home);
            return;
        }

        var target = existing ?? FromCatalog(item.Entry);
        if (!await UpdateAsync(target)) return;

        SelectEmulator(target);
        ShowPage(Screen.Home);
    }

    private Emulator FromCatalog(CatalogEntry entry)
    {
        var folder = string.IsNullOrWhiteSpace(entry.Folder) ? entry.Name : entry.Folder;
        foreach (var invalid in Path.GetInvalidFileNameChars())
            folder = folder.Replace(invalid, '_');

        return new Emulator
        {
            Name = entry.Name,
            Platform = entry.Platform,
            CatalogId = entry.Id,
            RepoUrl = entry.RepoUrl,
            TagFilter = entry.TagFilter,
            Arguments = entry.Arguments,
            InstallDir = Path.Combine(_settings.EmulatorsDir, folder.Trim())
        };
    }

    private void AddCustom_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || OverlayOpen) return;

        var dialog = new EditWindow(null) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var emulator = dialog.Emulator;
        ResetStatus(emulator);
        Emulators.Insert(0, emulator);
        SaveStore();
        LoadIcon(emulator);

        SelectEmulator(emulator);
        ShowPage(Screen.Home);
        SetStatus(T.L($"Added {emulator.Name}.", $"{emulator.Name} adicionado."));

        CheckInBackground(emulator);
    }

    // --------------------------------------------------------------- options

    private async void ShowOptions()
    {
        if (_selected is not { } emulator || OverlayOpen) return;

        var keys = new List<string>();
        var options = new List<DialogOption>();

        void Add(string key, string label, string? detail = null)
        {
            keys.Add(key);
            options.Add(new DialogOption(label, detail));
        }

        Add("primary", emulator.IsInstalled ? T.L("Play", "Jogar") : T.L("Install", "Instalar"));

        if (HasRepository(emulator))
        {
            if (emulator.IsInstalled)
            {
                Add("update", emulator.UpdateAvailable
                    ? T.L($"Update to {emulator.LatestVersion}", $"Atualizar para {emulator.LatestVersion}")
                    : T.L("Download again", "Baixar de novo"));
            }

            Add("check", T.L("Check for updates", "Procurar atualizações"));
        }

        if (Directory.Exists(emulator.InstallDir))
            Add("folder", T.L("Open folder", "Abrir pasta"));

        Add("edit", T.L("Edit…", "Editar…"), T.L("mouse and keyboard", "mouse e teclado"));
        Add("remove", T.L("Remove from the library", "Remover da biblioteca"), T.L("files are kept", "os arquivos ficam"));

        var body = string.IsNullOrWhiteSpace(emulator.StatusDetail) ? null : emulator.StatusDetail;
        var choice = await DialogAsync(emulator.Name, body, options);
        if (choice < 0) return;

        switch (keys[choice])
        {
            case "primary":
                RunPrimary(emulator);
                break;

            case "update":
                RunUpdate(emulator);
                break;

            case "check":
                if (await CheckOneAsync(emulator))
                    SetStatus(emulator.Name + ": " + emulator.Status);
                else
                    SetStatus(string.IsNullOrWhiteSpace(emulator.StatusDetail) ? emulator.Status : emulator.StatusDetail);
                break;

            case "folder":
                OpenFolder(emulator.InstallDir);
                break;

            case "edit":
                Edit(emulator);
                break;

            case "remove":
                await RemoveAsync(emulator);
                break;
        }
    }

    private void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void Edit(Emulator emulator)
    {
        if (_busy) return;

        var dialog = new EditWindow(emulator) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (dialog.Removed)
        {
            Remove(emulator);
            return;
        }

        emulator.RefreshInstalled();
        ResetStatus(emulator);
        SaveStore();
        LoadIcon(emulator);
        RefreshCatalogState();
        SetStatus(T.L($"Saved {emulator.Name}.", $"{emulator.Name} salvo."));

        CheckInBackground(emulator);
    }

    private async Task RemoveAsync(Emulator emulator)
    {
        var confirmed = await ConfirmAsync(
            T.L($"Remove {emulator.Name}?", $"Remover o {emulator.Name}?"),
            T.L($"It leaves the library. The files in {emulator.InstallDir} are not deleted.",
                $"Ele sai da biblioteca. Os arquivos em {emulator.InstallDir} não são apagados."),
            T.L("Remove", "Remover"));

        if (confirmed) Remove(emulator);
    }

    private void Remove(Emulator emulator)
    {
        Emulators.Remove(emulator);
        SaveStore();
        SetStatus(T.L($"{emulator.Name} removed from the library. Its files were not deleted.",
            $"{emulator.Name} removido da biblioteca. Os arquivos não foram apagados."));
        FocusDefault();
    }

    // --------------------------------------------------------------- helpers

    private async void CheckInBackground(Emulator emulator)
    {
        if (HasRepository(emulator)) await CheckOneAsync(emulator);
    }

    /// <summary>Loads the tile image off the UI thread; the tile shows two letters until it arrives.</summary>
    private async void LoadIcon(Emulator emulator)
    {
        var imagePath = emulator.IconPath;
        var exePath = emulator.ExePath;

        var icon = await Task.Run(() => IconLoader.Load(imagePath, exePath));
        emulator.Icon = icon;
    }

    private void SaveStore()
    {
        try
        {
            Store.SaveEmulators(Emulators);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }
}
