using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace EmuLauncher.Models;

public class Emulator : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    private string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            Raise();
            Raise(nameof(Monogram));
        }
    }

    /// <summary>The console or platform, e.g. "PlayStation 2". Only shown, never used for logic.</summary>
    private string _platform = "";
    public string Platform
    {
        get => _platform;
        set { if (_platform == value) return; _platform = value ?? ""; Raise(); }
    }

    /// <summary>Id of the built-in catalog entry this emulator was installed from, if any.</summary>
    public string CatalogId { get; set; } = "";

    private string _repoUrl = "";
    public string RepoUrl
    {
        get => _repoUrl;
        set { if (_repoUrl == value) return; _repoUrl = value; Raise(); }
    }

    /// <summary>Optional: only releases whose tag contains this text are considered.</summary>
    public string TagFilter { get; set; } = "";

    private string _installDir = "";
    public string InstallDir
    {
        get => _installDir;
        set { if (_installDir == value) return; _installDir = value; Raise(); }
    }

    private string _exePath = "";
    public string ExePath
    {
        get => _exePath;
        set
        {
            if (_exePath == value) return;
            _exePath = value;
            Raise();
            Raise(nameof(IsInstalled));
        }
    }

    /// <summary>Command line passed to the executable, e.g. "-bigpicture -fullscreen".</summary>
    public string Arguments { get; set; } = "";

    /// <summary>Optional image shown on the tile instead of the executable's own icon.</summary>
    public string IconPath { get; set; } = "";

    private string _installedVersion = "";
    public string InstalledVersion
    {
        get => _installedVersion;
        set
        {
            if (_installedVersion == value) return;
            _installedVersion = value;
            Raise();
            Raise(nameof(InstalledDisplay));
        }
    }

    /// <summary>
    /// When the installed release was published. Rolling releases keep the same tag
    /// ("latest", "continuous"), so the date is what tells a newer build apart.
    /// </summary>
    public DateTimeOffset? InstalledStamp { get; set; }

    public DateTimeOffset? LastPlayed { get; set; }

    private string _latestVersion = "";
    [JsonIgnore]
    public string LatestVersion
    {
        get => _latestVersion;
        set
        {
            if (_latestVersion == value) return;
            _latestVersion = value;
            Raise();
            Raise(nameof(LatestDisplay));
        }
    }

    private string _status = "";
    [JsonIgnore]
    public string Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; Raise(); }
    }

    private Brush _statusBrush = StatusBrushes.Muted;
    [JsonIgnore]
    public Brush StatusBrush
    {
        get => _statusBrush;
        private set { _statusBrush = value; Raise(); }
    }

    private string _statusDetail = "";
    /// <summary>Full error text, e.g. why a check failed.</summary>
    [JsonIgnore]
    public string StatusDetail
    {
        get => _statusDetail;
        set { if (_statusDetail == value) return; _statusDetail = value; Raise(); }
    }

    private bool _updateAvailable;
    [JsonIgnore]
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set { if (_updateAvailable == value) return; _updateAvailable = value; Raise(); }
    }

    private bool _isRunning;
    [JsonIgnore]
    public bool IsRunning
    {
        get => _isRunning;
        set { if (_isRunning == value) return; _isRunning = value; Raise(); }
    }

    private ImageSource? _icon;
    /// <summary>The tile image: <see cref="IconPath"/> or the executable's largest icon.</summary>
    [JsonIgnore]
    public ImageSource? Icon
    {
        get => _icon;
        set { if (ReferenceEquals(_icon, value)) return; _icon = value; Raise(); }
    }

    [JsonIgnore]
    public bool IsInstalled => !string.IsNullOrWhiteSpace(ExePath) && File.Exists(ExePath);

    /// <summary>Call after files changed on disk (install, update, edit).</summary>
    public void RefreshInstalled() => Raise(nameof(IsInstalled));

    [JsonIgnore]
    public string InstalledDisplay => string.IsNullOrWhiteSpace(InstalledVersion) ? "—" : InstalledVersion;

    [JsonIgnore]
    public string LatestDisplay => string.IsNullOrWhiteSpace(LatestVersion) ? "—" : LatestVersion;

    /// <summary>Two letters for the tile while there is no icon ("PCSX2" → "PC", "DuckStation" → "DS").</summary>
    [JsonIgnore]
    public string Monogram => MakeMonogram(Name);

    public static string MakeMonogram(string? name)
    {
        var text = (name ?? "").Trim();
        if (text.Length == 0) return "?";

        var words = text.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
            return (words[0][..1] + words[1][..1]).ToUpperInvariant();

        var first = text[0];
        for (var i = 1; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]) || char.IsDigit(text[i]))
                return (first.ToString() + text[i]).ToUpperInvariant();
        }

        return text.Length >= 2 ? text[..2].ToUpperInvariant() : text.ToUpperInvariant();
    }

    public void SetStatus(string text, Brush brush)
    {
        Status = text;
        StatusBrush = brush;
    }
}

/// <summary>Graphite status colours: grey for "fine", the violet accent for "there is something to do".</summary>
public static class StatusBrushes
{
    public static readonly Brush Muted = Frozen("#9A9AA8");
    public static readonly Brush Ok = Frozen("#6EE7A8");
    public static readonly Brush Update = Frozen("#A78BFA");
    public static readonly Brush Error = Frozen("#F87171");

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
