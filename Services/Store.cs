using System.IO;
using System.Text;
using System.Text.Json;
using EmuLauncher.Models;

namespace EmuLauncher.Services;

public static class Store
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EmuLauncher");

    private static readonly string EmulatorsFile = Path.Combine(Folder, "emulators.json");
    private static readonly string SettingsFile = Path.Combine(Folder, "settings.json");

    /// <summary>User additions to the built-in catalog (see README).</summary>
    public static readonly string CatalogFile = Path.Combine(Folder, "catalog.json");

    /// <summary>The list of the original Emulator Manager, imported once on the first start.</summary>
    private static readonly string LegacyFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EmulatorManager", "emulators.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static List<Emulator> LoadEmulators()
    {
        var own = Read<List<Emulator>>(EmulatorsFile);
        if (own is not null) return Clean(own);

        // First start: bring over what Emulator Manager already tracks. Its file is only read.
        var legacy = Read<List<Emulator>>(LegacyFile);
        return legacy is null ? new List<Emulator>() : Clean(legacy);
    }

    public static void SaveEmulators(IEnumerable<Emulator> emulators)
    {
        try
        {
            Write(EmulatorsFile, emulators.ToList());
        }
        catch (Exception ex)
        {
            throw new IOException(T.L(
                $"Could not save the emulator list: {ex.Message}",
                $"Não foi possível salvar a lista de emuladores: {ex.Message}"), ex);
        }
    }

    public static AppSettings LoadSettings() => Read<AppSettings>(SettingsFile) ?? new AppSettings();

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            Write(SettingsFile, settings);
        }
        catch
        {
            // Settings are a convenience; failing to save them must not interrupt anything.
        }
    }

    public static List<CatalogEntry> LoadUserCatalog()
        => Read<List<CatalogEntry>>(CatalogFile) ?? new List<CatalogEntry>();

    private static List<Emulator> Clean(List<Emulator> emulators)
    {
        emulators.RemoveAll(e => e is null);
        foreach (var emulator in emulators)
        {
            if (string.IsNullOrWhiteSpace(emulator.Id)) emulator.Id = Guid.NewGuid().ToString("n");
            emulator.Name ??= "";
            emulator.Platform ??= "";
            emulator.CatalogId ??= "";
            emulator.RepoUrl ??= "";
            emulator.TagFilter ??= "";
            emulator.InstallDir ??= "";
            emulator.ExePath ??= "";
            emulator.Arguments ??= "";
            emulator.IconPath ??= "";
            emulator.InstalledVersion ??= "";
        }

        return emulators;
    }

    private static TValue? Read<TValue>(string path) where TValue : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<TValue>(json, Options);
        }
        catch
        {
            // A corrupt file should never prevent the app from starting.
            return null;
        }
    }

    private static void Write<TValue>(string path, TValue value)
    {
        Directory.CreateDirectory(Folder);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options), Encoding.UTF8);
        File.Move(temp, path, overwrite: true);
    }
}
