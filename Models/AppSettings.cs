namespace EmuLauncher.Models;

public class AppSettings
{
    /// <summary>Where catalog emulators are installed, one sub-folder each.</summary>
    public string EmulatorsDir { get; set; } = "";

    public bool StartFullscreen { get; set; } = true;

    public bool CheckOnStart { get; set; } = true;
}
