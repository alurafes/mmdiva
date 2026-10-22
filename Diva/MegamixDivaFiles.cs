using MikuMikuLibrary.Archives.CriMw;

namespace Diva;

public sealed class MegamixDivaFiles : DivaFiles
{
    private readonly string gameRoot;
    // sorted by priority
    private static readonly string[] roots =
    {
        "rom_steam_region_dlc", "rom_steam_region", "rom_steam_dlc", "rom_steam",
        "rom_switch", "rom_ps4_patch", "rom_ps4_dlc", "rom_ps4", "rom",
    };
    private static readonly string[] cpks = { "diva_main.cpk", "diva_main_region.cpk", "diva_dlc00.cpk", "diva_dlc00_region.cpk" };
    private readonly List<CpkArchive> cpkArchives = new();
    private readonly Dictionary<string, (CpkArchive cpk, string entry, int priority)> files = new(StringComparer.OrdinalIgnoreCase);
    private static string cacheDirectory => Path.Combine(Path.GetTempPath(), "mmdiva", "megamix");
    public MegamixDivaFiles(string gameRoot)
    {
        this.gameRoot = gameRoot;

        foreach (var cpk in cpks)
        {
            string path = Path.Combine(gameRoot, cpk);
            if (!File.Exists(path)) continue;
            var cpkArchive = MikuMikuLibrary.IO.BinaryFile.Load<CpkArchive>(path);
            cpkArchives.Add(cpkArchive);
            foreach (var entry in cpkArchive.FileNames)
            {
                string root = "rom", rel = entry;
                int slash = entry.IndexOf('/');
                if (slash > 0 && entry.StartsWith("rom_") && entry[(slash + 1)..].StartsWith("rom/"))
                {
                    root = entry[..slash];
                    rel = entry[(slash + 1)..];
                }
                int priority = Array.IndexOf(roots, root);
                if (priority < 0) continue;
                if (!files.TryGetValue(rel, out var existing) || priority < existing.priority) files[rel] = (cpkArchive, entry, priority);
            }
        }
    }
    public string ResolveFile(string relativePath)
    {
        string rel = relativePath.Replace('\\', '/').TrimStart('.', '/');
        if (!files.TryGetValue(rel, out var file)) return null;

        string cached = Path.Combine(cacheDirectory, file.entry.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(cached))
        {
            var path = Path.GetDirectoryName(cached);
            if (path == null) throw new FileNotFoundException();
            Directory.CreateDirectory(path);
            string tempFile = cached + ".tmp";
            using (var stream = file.cpk.Open(file.entry, MikuMikuLibrary.Archives.EntryStreamMode.OriginalStream))
            using (var tempFileStream = File.Create(tempFile))
            {
                stream.CopyTo(tempFileStream);
            }
            File.Move(tempFile, cached, true);
        }
        return cached;
    }
}