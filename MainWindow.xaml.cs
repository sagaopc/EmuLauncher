using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EmuLauncher.Models;
using EmuLauncher.Services;

namespace EmuLauncher;

/// <summary>
/// The launcher window. This file holds the pages and what they show; input is in
/// MainWindow.Input.cs, dialogs in MainWindow.Overlays.cs and everything that touches an
/// emulator (check, download, launch) in MainWindow.Actions.cs.
/// </summary>
public partial class MainWindow : Window
{
    private enum Screen
    {
        Home,
        Catalog,
        Settings
    }

    public ObservableCollection<Emulator> Emulators { get; } = new();
    public ObservableCollection<CatalogItem> CatalogItems { get; } = new();

    private readonly RepoClient _repo = new();
    private readonly UpdateService _updater;
    private readonly GamepadService _pad = new();
    private readonly Dictionary<string, Process> _running = new();
    private readonly AppSettings _settings;

    private Screen _page = Screen.Home;
    private Emulator? _selected;

    private bool _busy;
    private bool _checking;
    private bool _loadedOnce;
    private bool _fullscreen;
    private bool _mouseInput;
    private CancellationTokenSource? _updateCts;

    public MainWindow()
    {
        InitializeComponent();

        _updater = new UpdateService(_repo);
        _settings = Store.LoadSettings();
        if (string.IsNullOrWhiteSpace(_settings.EmulatorsDir))
        {
            _settings.EmulatorsDir = DefaultEmulatorsDir();
            Store.SaveSettings(_settings);
        }

        // Fullscreen is applied before the window is shown, so it never flashes as a small window first.
        if (_settings.StartFullscreen)
        {
            _fullscreen = true;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }

        DataContext = this;

        Loaded += Window_Loaded;
        Closing += Window_Closing;
        SourceInitialized += (_, _) => Native.UseDarkTitleBar(this);
        Activated += (_, _) => _pad.Enabled = true;
        Deactivated += (_, _) => _pad.Enabled = false;
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseDown += (_, _) => _mouseInput = true;

        _pad.Pressed += OnPad;
        _pad.ConnectionChanged += UpdateHints;

        Emulators.CollectionChanged += (_, _) => OnLibraryChanged();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce) return;
        _loadedOnce = true;

        // Most recently played first, like a console's home row.
        var stored = Store.LoadEmulators()
            .OrderByDescending(emulator => emulator.LastPlayed ?? DateTimeOffset.MinValue)
            .ToList();

        foreach (var emulator in stored)
        {
            ResetStatus(emulator);
            Emulators.Add(emulator);
            LoadIcon(emulator);
        }

        foreach (var entry in Catalog.Load())
            CatalogItems.Add(new CatalogItem(entry));

        RefreshCatalogState();
        RefreshSettingsPage();

        SelectEmulator(Emulators.FirstOrDefault());
        ShowPage(Screen.Home);

        _pad.Enabled = IsActive;
        _pad.Start();

        if (_settings.CheckOnStart && Emulators.Count > 0)
            await CheckAllAsync(showResult: false);
        else
            SetStatus(T.L("Ready.", "Pronto."));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _pad.Stop();
        _updateCts?.Cancel();
    }

    // ----------------------------------------------------------------- pages

    private void ShowPage(Screen page)
    {
        _page = page;

        HomePage.Visibility = page == Screen.Home ? Visibility.Visible : Visibility.Collapsed;
        CatalogPage.Visibility = page == Screen.Catalog ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == Screen.Settings ? Visibility.Visible : Visibility.Collapsed;

        RailLibrary.Tag = page == Screen.Home ? "on" : null;
        RailCatalog.Tag = page == Screen.Catalog ? "on" : null;
        RailSettings.Tag = page == Screen.Settings ? "on" : null;

        if (page == Screen.Settings) RefreshSettingsPage();

        UpdateHero();
        UpdateHints();
        FocusDefault();
    }

    private void RailLibrary_Click(object sender, RoutedEventArgs e) => ShowPage(Screen.Home);

    private void RailCatalog_Click(object sender, RoutedEventArgs e) => ShowPage(Screen.Catalog);

    private void RailSettings_Click(object sender, RoutedEventArgs e) => ShowPage(Screen.Settings);

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => SetFullscreen(!_fullscreen);

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Puts the focus where a controller expects it on the current page.</summary>
    private void FocusDefault()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (OverlayOpen) return;

            UIElement? target = _page switch
            {
                Screen.Home => (_selected is null ? null : TileOf(_selected)) ?? (UIElement)PlayButton,
                Screen.Catalog => FirstCatalogCard() ?? (UIElement)AddCustomButton,
                _ => FolderRow
            };

            FocusElement(target);
        }));
    }

    private static void FocusElement(UIElement? element)
    {
        if (element is null || !element.IsVisible || !element.IsEnabled) return;
        element.Focus();
        Keyboard.Focus(element);
    }

    private void FocusLater(UIElement? element)
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusElement(element)));

    private Button? TileOf(Emulator emulator)
        => EmulatorRow.ItemContainerGenerator.ContainerFromItem(emulator) is DependencyObject container
            ? FindChild<Button>(container)
            : null;

    private Button? FirstCatalogCard()
        => CatalogItems.Count > 0 &&
           CatalogList.ItemContainerGenerator.ContainerFromIndex(0) is DependencyObject container
            ? FindChild<Button>(container)
            : null;

    private static TChild? FindChild<TChild>(DependencyObject parent) where TChild : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TChild match) return match;

            var nested = FindChild<TChild>(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    // ------------------------------------------------------------------ home

    /// <summary>The emulator shown in the big card. Focusing a tile selects it.</summary>
    private void SelectEmulator(Emulator? emulator)
    {
        if (!ReferenceEquals(_selected, emulator))
        {
            if (_selected is not null) _selected.PropertyChanged -= Selected_PropertyChanged;
            _selected = emulator;
            if (_selected is not null) _selected.PropertyChanged += Selected_PropertyChanged;
        }

        UpdateHero();
        UpdateHints();
    }

    private void Selected_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateHero();
        UpdateHints();
    }

    private void OnLibraryChanged()
    {
        if (_selected is not null && !Emulators.Contains(_selected))
            SelectEmulator(Emulators.FirstOrDefault());
        else if (_selected is null && Emulators.Count > 0)
            SelectEmulator(Emulators[0]);

        RefreshCatalogState();
        UpdateHero();
    }

    private void UpdateHero()
    {
        var emulator = _selected;

        if (emulator is null)
        {
            // Nothing in the library yet: the big card invites to the download page.
            HeroCover.Visibility = Visibility.Collapsed;
            HeroPlatform.Visibility = Visibility.Collapsed;
            HeroStatusRow.Visibility = Visibility.Collapsed;
            HeroHint.Visibility = Visibility.Visible;
            HeroTitle.Text = T.L("Add your emulators", "Adicione seus emuladores");
            HeroHint.Text = T.L(
                "Pick the emulators you want and the launcher downloads the newest build of each. After that, one button updates or starts them.",
                "Escolha os emuladores que quiser e o launcher baixa a versão mais recente de cada um. Depois, um botão atualiza ou abre.");

            PlayText.Text = T.Download;
            PlayGlyph.Visibility = Visibility.Collapsed;
            InstallGlyph.Visibility = Visibility.Visible;
            UpdateButton.Visibility = Visibility.Collapsed;
            OptionsButton.Visibility = Visibility.Collapsed;
            return;
        }

        HeroCover.Visibility = Visibility.Visible;
        HeroHint.Visibility = Visibility.Collapsed;
        HeroStatusRow.Visibility = Visibility.Visible;

        HeroIcon.Source = emulator.Icon;
        HeroMono.Text = emulator.Monogram;
        HeroMono.Visibility = emulator.Icon is null ? Visibility.Visible : Visibility.Collapsed;

        HeroPlatform.Text = emulator.Platform;
        HeroPlatform.Visibility = string.IsNullOrWhiteSpace(emulator.Platform) ? Visibility.Collapsed : Visibility.Visible;
        HeroTitle.Text = emulator.Name;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(emulator.InstalledVersion))
            parts.Add(T.L("Version", "Versão") + " " + emulator.InstalledVersion);
        if (emulator.IsRunning)
            parts.Add(T.L("Running", "Em execução"));
        if (!string.IsNullOrWhiteSpace(emulator.Status))
        {
            parts.Add(emulator.UpdateAvailable && !string.IsNullOrWhiteSpace(emulator.LatestVersion)
                ? emulator.Status + ": " + emulator.LatestVersion
                : emulator.Status);
        }

        HeroStatus.Text = string.Join("   ·   ", parts);
        HeroDot.Fill = emulator.StatusBrush;
        HeroStatusRow.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var installed = emulator.IsInstalled;
        PlayText.Text = installed ? T.L("Play", "Jogar") : T.L("Install", "Instalar");
        PlayGlyph.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        InstallGlyph.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;

        // "Install" already is the download; the separate update button only makes sense afterwards.
        UpdateButton.Visibility = installed && HasRepository(emulator) ? Visibility.Visible : Visibility.Collapsed;
        OptionsButton.Visibility = Visibility.Visible;
    }

    private void Tile_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (EmulatorOf(sender) is { } emulator) SelectEmulator(emulator);
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (EmulatorOf(sender) is not { } emulator) return;
        SelectEmulator(emulator);

        // With a controller or the keyboard, A on a tile starts it straight away. A mouse click
        // only selects (the Play button is one click away); a double click starts it.
        if (_mouseInput) return;
        RunPrimary(emulator);
    }

    private void Tile_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EmulatorOf(sender) is not { } emulator) return;
        SelectEmulator(emulator);
        RunPrimary(emulator);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            ShowPage(Screen.Catalog);
            return;
        }

        RunPrimary(_selected);
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) RunUpdate(_selected);
    }

    private void Options_Click(object sender, RoutedEventArgs e) => ShowOptions();

    private void RowScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The row scrolls sideways; a mouse wheel only knows up and down.
        RowScroll.ScrollToHorizontalOffset(RowScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private static Emulator? EmulatorOf(object sender)
        => (sender as FrameworkElement)?.DataContext as Emulator;

    // -------------------------------------------------------------- settings

    private void RefreshSettingsPage()
    {
        FolderValue.Text = _settings.EmulatorsDir;
        FullscreenValue.Text = OnOff(_settings.StartFullscreen);
        CheckOnStartValue.Text = OnOff(_settings.CheckOnStart);

        AboutText.Text = T.L(
            $"Emu Launcher {AppVersion()}  ·  Controller: D-pad or left stick to move, A to choose, B to go back, X to update, Y for options, LB/RB to switch pages. Keyboard: arrows, Enter, Esc, U, M, Page Up/Down, F11.\nYour list is kept in {Store.Folder}",
            $"Emu Launcher {AppVersion()}  ·  Controle: direcional ou analógico esquerdo para mover, A para escolher, B para voltar, X para atualizar, Y para opções, LB/RB para trocar de página. Teclado: setas, Enter, Esc, U, M, Page Up/Down, F11.\nSua lista fica em {Store.Folder}");
    }

    private static string OnOff(bool value) => value ? T.L("On", "Sim") : T.L("Off", "Não");

    private static string AppVersion()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        return version is null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void SetFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = T.L("Choose where emulators are installed", "Escolha onde os emuladores são instalados")
        };
        if (Directory.Exists(_settings.EmulatorsDir))
            dialog.InitialDirectory = _settings.EmulatorsDir;

        if (dialog.ShowDialog(this) != true) return;

        _settings.EmulatorsDir = dialog.FolderName;
        Store.SaveSettings(_settings);
        RefreshSettingsPage();
        SetStatus(T.L(
            "New downloads go to the new folder. Emulators already installed stay where they are.",
            "Os próximos downloads vão para a nova pasta. Os emuladores já instalados continuam onde estão."));
    }

    private void SetFullscreen_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartFullscreen = !_settings.StartFullscreen;
        Store.SaveSettings(_settings);
        SetFullscreen(_settings.StartFullscreen);
        RefreshSettingsPage();
    }

    private void SetCheckOnStart_Click(object sender, RoutedEventArgs e)
    {
        _settings.CheckOnStart = !_settings.CheckOnStart;
        Store.SaveSettings(_settings);
        RefreshSettingsPage();
    }

    private async void SetCheckNow_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(Screen.Home);
        await CheckAllAsync(showResult: true);
    }

    /// <summary>Catalog emulators go to their own folder under this one unless the user chooses another.</summary>
    private static string DefaultEmulatorsDir()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmuLauncher", "Emulators");

    // ------------------------------------------------------------ the window

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 720 lines tall is the design size; a narrow window shrinks a little further so the row still fits.
        var scale = Math.Min(Root.ActualHeight / 720.0, Root.ActualWidth / 1180.0);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) return;

        scale = Math.Clamp(scale, 0.5, 6.0);
        StageScale.ScaleX = scale;
        StageScale.ScaleY = scale;
    }

    private void SetFullscreen(bool on)
    {
        if (_fullscreen == on) return;
        _fullscreen = on;

        if (on)
        {
            // Borderless first, then maximised: in that order the window also covers the taskbar.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;

    // ----------------------------------------------------------------- hints

    /// <summary>The footer legend: what each button does right now.</summary>
    private void UpdateHints()
    {
        Hints.Children.Clear();
        var pad = _pad.AnyConnected;

        void Add(string padGlyph, string key, string label)
            => Hints.Children.Add(MakeHint(pad ? padGlyph : key, label, round: pad));

        if (_dialogActive)
        {
            Add("A", "Enter", T.L("Select", "Selecionar"));
            Add("B", "Esc", T.L("Back", "Voltar"));
            return;
        }

        if (_progressActive)
        {
            Add("B", "Esc", T.Cancel);
            return;
        }

        switch (_page)
        {
            case Screen.Home when _selected is { } emulator:
                Add("A", "Enter", emulator.IsInstalled ? T.L("Play", "Jogar") : T.L("Install", "Instalar"));
                if (emulator.IsInstalled && HasRepository(emulator)) Add("X", "U", T.Update);
                Add("Y", "M", T.Options);
                break;

            case Screen.Home:
                Add("A", "Enter", T.L("Select", "Selecionar"));
                break;

            case Screen.Catalog:
                Add("A", "Enter", T.L("Download", "Baixar"));
                Add("B", "Esc", T.L("Back", "Voltar"));
                break;

            default:
                Add("A", "Enter", T.L("Change", "Alterar"));
                Add("B", "Esc", T.L("Back", "Voltar"));
                break;
        }

        Add("LB RB", "PgUp PgDn", T.L("Pages", "Páginas"));
    }

    private UIElement MakeHint(string glyph, string label, bool round)
    {
        var badge = new Border
        {
            MinWidth = 28,
            Height = 28,
            CornerRadius = new CornerRadius(round && glyph.Length == 1 ? 14 : 8),
            Background = (Brush)FindResource("Line"),
            Padding = new Thickness(glyph.Length == 1 ? 0 : 9, 0, glyph.Length == 1 ? 0 : 9, 0),
            Margin = new Thickness(22, 0, 9, 0),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("TextMain"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var text = new TextBlock
        {
            Text = label,
            FontSize = 15,
            Foreground = (Brush)FindResource("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(badge);
        panel.Children.Add(text);
        return panel;
    }
}
