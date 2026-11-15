using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace EmuLauncher.Models;

/// <summary>An emulator the launcher knows how to download: where its releases are and what to run.</summary>
public class CatalogEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string RepoUrl { get; set; } = "";

    /// <summary>Optional: only releases whose tag contains this text are considered.</summary>
    public string TagFilter { get; set; } = "";

    /// <summary>Sub-folder of the emulators folder. Defaults to the name.</summary>
    public string Folder { get; set; } = "";

    /// <summary>File names (wildcards allowed) of the executable to launch, best first.</summary>
    public List<string> Exe { get; set; } = new();

    /// <summary>Default command line, e.g. to start in a controller-friendly fullscreen mode.</summary>
    public string Arguments { get; set; } = "";
}

/// <summary>A catalog entry as the download page shows it, with whether it is already in the library.</summary>
public sealed class CatalogItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public CatalogItem(CatalogEntry entry) => Entry = entry;

    public CatalogEntry Entry { get; }

    public string Name => Entry.Name;
    public string Platform => Entry.Platform;
    public string Monogram => Emulator.MakeMonogram(Entry.Name);

    private bool _installed;
    public bool Installed
    {
        get => _installed;
        set { if (_installed == value) return; _installed = value; Raise(); }
    }

    private string _state = "";
    public string State
    {
        get => _state;
        set { if (_state == value) return; _state = value; Raise(); }
    }
}
