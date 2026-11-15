using EmuLauncher.Models;

namespace EmuLauncher.Services;

/// <summary>
/// The emulators offered on the download page. Each entry points at the repository that publishes
/// Windows builds; the release lookup, the file choice and the install are the same as for any
/// emulator added by hand. Entries in %APPDATA%\EmuLauncher\catalog.json are added to this list
/// (or replace the entry with the same Id).
/// </summary>
public static class Catalog
{
    public static List<CatalogEntry> Load()
    {
        var entries = BuiltIn();

        foreach (var custom in Store.LoadUserCatalog())
        {
            if (custom is null || string.IsNullOrWhiteSpace(custom.Name) || string.IsNullOrWhiteSpace(custom.RepoUrl))
                continue;

            custom.Platform ??= "";
            custom.TagFilter ??= "";
            custom.Folder ??= "";
            custom.Arguments ??= "";
            custom.Exe ??= new List<string>();
            if (string.IsNullOrWhiteSpace(custom.Id)) custom.Id = custom.Name.Trim().ToLowerInvariant();

            var index = entries.FindIndex(e => string.Equals(e.Id, custom.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) entries[index] = custom;
            else entries.Add(custom);
        }

        return entries;
    }

    private static CatalogEntry Entry(string id, string name, string platform, string repo, string[] exe,
        string arguments = "", string tagFilter = "")
        => new()
        {
            Id = id,
            Name = name,
            Platform = platform,
            RepoUrl = repo,
            Exe = exe.ToList(),
            Arguments = arguments,
            TagFilter = tagFilter
        };

    private static List<CatalogEntry> BuiltIn() => new()
    {
        // Nintendo Switch 2: Eglantine, Lichii and Eden builds. Several are forks that publish their
        // own builds; the launcher keeps each one in its own folder, so they can sit side by side.
        Entry("lichii", "Lichii", "Switch 2 · zortos293",
            "https://github.com/zortos293/Lichii",
            new[] { "lichii.exe", "eglantine.exe" },
            tagFilter: "build"),
        Entry("lichii-actions", "Lichii · Actions", "Switch 2 · builds de teste do GitHub Actions",
            "https://github.com/zortos293/Lichii/actions/workflows/release-binaries.yml",
            new[] { "lichii.exe", "eglantine.exe" }),
        Entry("fritchii", "Fritchii", "Switch 2 · Frizti",
            "https://github.com/Frizti/Lichii",
            new[] { "lichii.exe", "fritchii.exe" },
            tagFilter: "fritchii-v|frizti-v"),
        Entry("lichii-sharkiix", "Lichii Sharkiix", "Switch 2 · Shakoquo",
            "https://github.com/Shakoquo/Lichii-sharkiix-version",
            new[] { "lichii.exe", "eglantine.exe" }),
        Entry("eglantine-community", "Eglantine Community", "Switch 2 · daily",
            "https://github.com/Eglantine-Community/Eglantine",
            new[] { "eglantine.exe" }),
        Entry("eglantine-pokedinner", "Eglantine PokeDinner", "Switch 2 · Franseven",
            "https://git.eden-emu.dev/Franseven/PokeDinner-a-Eglantine-Fork",
            new[] { "eglantine.exe" }),
        Entry("eglantine-selevet", "Selevet Eglantine", "Switch 2 · sylvieistaken",
            "https://github.com/sylvieistaken/selevet-eglantine",
            new[] { "selevet.exe", "eglantine.exe" }),
        Entry("eglantine-smileytatsu", "Eglantine SmileyTatsu", "Switch 2 · low end",
            "https://github.com/SmileyTatsu/Eglantine",
            new[] { "eglantine.exe" }),
        Entry("eglantine-amwap", "Eglantine A Man With A Plan", "Switch 2 · Zelda",
            "https://github.com/A-Man-With-A-Plan-dev/Eglantine",
            new[] { "eglantine.exe", "lichii.exe" }),
        Entry("eden-nx2", "Eden nx2", "Switch 2 · Takoroka",
            "https://git.eden-emu.dev/Takoroka/eden-nx2",
            new[] { "eden.exe" }),
        Entry("eden", "Eden", "Nintendo Switch",
            "https://git.eden-emu.dev/eden-emu/eden",
            new[] { "eden.exe" }),

        // Other Nintendo systems
        Entry("cemu", "Cemu", "Wii U",
            "https://github.com/cemu-project/Cemu",
            new[] { "Cemu.exe" }),
        Entry("azahar", "Azahar", "Nintendo 3DS",
            "https://github.com/azahar-emu/azahar",
            new[] { "azahar.exe" }),
        Entry("melonds", "melonDS", "Nintendo DS",
            "https://github.com/melonDS-emu/melonDS",
            new[] { "melonDS.exe" }),
        Entry("mgba", "mGBA", "Game Boy Advance",
            "https://github.com/mgba-emu/mgba",
            new[] { "mGBA.exe" }),
        Entry("rmg", "RMG", "Nintendo 64",
            "https://github.com/Rosalie241/RMG",
            new[] { "RMG.exe" }),
        Entry("snes9x", "Snes9x", "Super Nintendo",
            "https://github.com/snes9xgit/snes9x",
            new[] { "snes9x-x64.exe", "snes9x*.exe" }),
        Entry("mesen", "Mesen", "NES · SNES · Game Boy",
            "https://github.com/SourMesen/Mesen2",
            new[] { "Mesen.exe" }),

        // Sony
        Entry("duckstation", "DuckStation", "PlayStation",
            "https://github.com/stenzek/duckstation",
            new[] { "duckstation-qt-x64-ReleaseLTCG.exe", "duckstation-qt*.exe" },
            arguments: "-bigpicture -fullscreen"),
        Entry("pcsx2", "PCSX2", "PlayStation 2",
            "https://github.com/PCSX2/pcsx2",
            new[] { "pcsx2-qt.exe", "pcsx2*.exe" },
            arguments: "-bigpicture -fullscreen"),
        Entry("rpcs3", "RPCS3", "PlayStation 3",
            "https://github.com/RPCS3/rpcs3-binaries-win",
            new[] { "rpcs3.exe" }),
        Entry("ppsspp", "PPSSPP", "PSP",
            "https://github.com/hrydgard/ppsspp",
            new[] { "PPSSPPWindows64.exe", "PPSSPP*.exe" }),
        Entry("vita3k", "Vita3K", "PS Vita",
            "https://github.com/Vita3K/Vita3K",
            new[] { "Vita3K.exe" }),

        // Sega, Microsoft, multi-system
        Entry("flycast", "Flycast", "Dreamcast · Naomi",
            "https://github.com/flyinghead/flycast",
            new[] { "flycast.exe" }),
        Entry("xemu", "xemu", "Xbox",
            "https://github.com/xemu-project/xemu",
            new[] { "xemu.exe" }),
        Entry("xenia-canary", "Xenia Canary", "Xbox 360",
            "https://github.com/xenia-canary/xenia-canary-releases",
            new[] { "xenia_canary.exe", "xenia*.exe" }),
        Entry("ares", "ares", "Multi-system",
            "https://github.com/ares-emulator/ares",
            new[] { "ares.exe" })
    };
}
