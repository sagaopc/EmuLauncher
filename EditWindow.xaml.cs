using System.IO;
using System.Windows;
using EmuLauncher.Models;
using EmuLauncher.Services;
using Microsoft.Win32;

namespace EmuLauncher;

public partial class EditWindow : Window
{
    private readonly Emulator _emulator;

    public Emulator Emulator => _emulator;
    public bool Removed { get; private set; }

    public EditWindow(Emulator? existing)
    {
        InitializeComponent();

        _emulator = existing ?? new Emulator();

        Title = existing is null ? T.L("Add emulator", "Adicionar emulador") : T.L("Edit emulator", "Editar emulador");
        NameBox.Text = _emulator.Name;
        PlatformBox.Text = _emulator.Platform;
        RepoBox.Text = _emulator.RepoUrl;
        FilterBox.Text = _emulator.TagFilter;
        DirBox.Text = _emulator.InstallDir;
        ExeBox.Text = _emulator.ExePath;
        ArgsBox.Text = _emulator.Arguments;
        IconBox.Text = _emulator.IconPath;
        RemoveButton.Visibility = existing is null ? Visibility.Collapsed : Visibility.Visible;

        SourceInitialized += (_, _) => Native.UseDarkTitleBar(this);
        Loaded += (_, _) => NameBox.Focus();
    }

    private void BrowseDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = T.L("Choose the installation directory", "Escolha a pasta de instalação")
        };
        if (Directory.Exists(DirBox.Text))
            dialog.InitialDirectory = DirBox.Text;

        if (dialog.ShowDialog(this) == true)
            DirBox.Text = dialog.FolderName;
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T.L("Choose the emulator executable", "Escolha o executável do emulador"),
            Filter = T.L("Executables (*.exe)|*.exe|All files (*.*)|*.*", "Executáveis (*.exe)|*.exe|Todos os arquivos (*.*)|*.*")
        };

        var initial = FolderOf(ExeBox.Text) ?? (Directory.Exists(DirBox.Text) ? DirBox.Text : null);
        if (initial is not null) dialog.InitialDirectory = initial;

        if (dialog.ShowDialog(this) != true) return;

        ExeBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(DirBox.Text))
            DirBox.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T.L("Choose an image for the tile", "Escolha uma imagem para o tile"),
            Filter = T.L("Images (*.png;*.jpg;*.jpeg;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All files (*.*)|*.*",
                "Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico|Todos os arquivos (*.*)|*.*")
        };

        var initial = FolderOf(IconBox.Text);
        if (initial is not null) dialog.InitialDirectory = initial;

        if (dialog.ShowDialog(this) == true)
            IconBox.Text = dialog.FileName;
    }

    /// <summary>The existing folder a typed path lives in, or null.</summary>
    private static string? FolderOf(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var folder = Path.GetDirectoryName(path.Trim());
            return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : null;
        }
        catch
        {
            return null;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var repoUrl = RepoBox.Text.Trim();
        var installDir = DirBox.Text.Trim().Trim('"');
        var exePath = ExeBox.Text.Trim().Trim('"');

        if (name.Length == 0)
        {
            Warn(T.L("Give the emulator a name.", "Dê um nome ao emulador."));
            NameBox.Focus();
            return;
        }

        // The repository is optional: without one the launcher only starts the emulator.
        if (repoUrl.Length > 0 && RepoClient.TryParse(repoUrl) is null)
        {
            Warn(T.L(
                "Enter a GitHub, GitLab or Gitea/Forgejo repository URL (for example https://github.com/owner/repo), or leave it empty to only launch this emulator.",
                "Informe a URL de um repositório do GitHub, GitLab ou Gitea/Forgejo (por exemplo https://github.com/dono/repo), ou deixe em branco para apenas abrir este emulador."));
            RepoBox.Focus();
            return;
        }

        if (repoUrl.Length == 0 && exePath.Length == 0)
        {
            Warn(T.L(
                "Enter a repository to download from, or choose the executable of an emulator you already have.",
                "Informe um repositório para baixar, ou escolha o executável de um emulador que você já tem."));
            RepoBox.Focus();
            return;
        }

        if (installDir.Length == 0 && exePath.Length > 0)
            installDir = FolderOf(exePath) ?? "";

        if (installDir.Length == 0)
        {
            Warn(T.L("Choose the directory the emulator is (or will be) installed in.",
                "Escolha a pasta em que o emulador está (ou será) instalado."));
            DirBox.Focus();
            return;
        }

        try
        {
            installDir = Path.GetFullPath(installDir);
        }
        catch (Exception ex)
        {
            Warn(T.L($"The installation directory is not a valid path.\n\n{ex.Message}",
                $"A pasta de instalação não é um caminho válido.\n\n{ex.Message}"));
            DirBox.Focus();
            return;
        }

        _emulator.Name = name;
        _emulator.Platform = PlatformBox.Text.Trim();
        _emulator.RepoUrl = repoUrl;
        _emulator.TagFilter = FilterBox.Text.Trim();
        _emulator.InstallDir = installDir;
        _emulator.ExePath = exePath;
        _emulator.Arguments = ArgsBox.Text.Trim();
        _emulator.IconPath = IconBox.Text.Trim().Trim('"');

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            T.L($"Remove “{_emulator.Name}” from the library?\n\nThe files in {_emulator.InstallDir} will not be deleted.",
                $"Remover “{_emulator.Name}” da biblioteca?\n\nOs arquivos em {_emulator.InstallDir} não serão apagados."),
            T.L("Remove emulator", "Remover emulador"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes) return;

        Removed = true;
        DialogResult = true;
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, T.L("Check the details", "Confira os dados"),
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
