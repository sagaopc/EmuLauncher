using System.Globalization;

namespace EmuLauncher.Services;

/// <summary>
/// The two languages of the launcher. Portuguese is used on a Portuguese Windows, English
/// everywhere else. Text used from XAML is a property here; code uses <see cref="L"/> inline.
/// </summary>
public static class T
{
    public static readonly bool Portuguese =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase);

    public static string L(string english, string portuguese) => Portuguese ? portuguese : english;

    // Rail
    public static string Library => L("Library", "Biblioteca");
    public static string Download => L("Download emulators", "Baixar emuladores");
    public static string Settings => L("Settings", "Ajustes");
    public static string Fullscreen => L("Fullscreen (F11)", "Tela cheia (F11)");
    public static string Exit => L("Exit", "Sair");

    // Home
    public static string YourEmulators => L("Your emulators", "Seus emuladores");
    public static string Update => L("Update", "Atualizar");
    public static string Options => L("Options", "Opções");

    // Catalog
    public static string CatalogTitle => L("Download emulators", "Baixar emuladores");
    public static string CatalogSubtitle => L(
        "Pick an emulator to download its newest build. It shows up in your library when it is done.",
        "Escolha um emulador para baixar a versão mais recente. Ele aparece na biblioteca ao terminar.");
    public static string AddCustom => L("Add another repository…", "Adicionar outro repositório…");
    public static string AddCustomHint => L(
        "Any GitHub, GitLab or Gitea/Forgejo repository. Needs a keyboard.",
        "Qualquer repositório do GitHub, GitLab ou Gitea/Forgejo. Precisa de teclado.");

    // Settings
    public static string SettingsTitle => L("Settings", "Ajustes");
    public static string SettingFolder => L("Emulators folder", "Pasta dos emuladores");
    public static string SettingFullscreen => L("Start in fullscreen", "Iniciar em tela cheia");
    public static string SettingCheckOnStart => L("Check for updates on start", "Procurar atualizações ao abrir");
    public static string SettingCheckNow => L("Check for updates now", "Procurar atualizações agora");
    public static string SettingExit => L("Exit the launcher", "Sair do launcher");

    // Overlays
    public static string Cancel => L("Cancel", "Cancelar");

    // Edit window
    public static string Name => L("Name", "Nome");
    public static string SystemName => L("System (optional)", "Sistema (opcional)");
    public static string RepoUrl => L("Repository URL", "URL do repositório");
    public static string RepoHint => L(
        "GitHub, GitLab or Gitea/Forgejo repository, e.g. https://github.com/owner/repo. To follow the builds of GitHub Actions instead of the releases, paste the Actions link: https://github.com/owner/repo/actions",
        "Repositório do GitHub, GitLab ou Gitea/Forgejo, ex.: https://github.com/dono/repo. Para seguir os builds do GitHub Actions em vez dos releases, cole o link da aba Actions: https://github.com/dono/repo/actions");
    public static string TagFilter => L("Release filter (optional)", "Filtro de release (opcional)");
    public static string TagFilterHint => L(
        "Only releases whose tag contains this text are used, e.g. “fritchii” in a repository that publishes several builds; separate alternatives with |, e.g. “fritchii-v|frizti-v”. With an Actions link it is compared with the workflow, the branch and the build title, e.g. “main”.",
        "Só são usados os releases cuja tag contém este texto, ex.: “fritchii” em um repositório que publica vários builds; separe alternativas com |, ex.: “fritchii-v|frizti-v”. Com um link do Actions, é comparado com o workflow, o branch e o título do build, ex.: “main”.");
    public static string InstallDir => L("Installation directory", "Pasta de instalação");
    public static string ExePath => L("Executable path", "Caminho do executável");
    public static string Arguments => L("Launch arguments (optional)", "Argumentos ao iniciar (opcional)");
    public static string IconPath => L("Tile image (optional)", "Imagem do tile (opcional)");
    public static string Browse => L("Browse…", "Procurar…");
    public static string Remove => L("Remove", "Remover");
    public static string Save => L("Save", "Salvar");
}
