using MikuMikuLibrary.Archives;
using MikuMikuLibrary.Motions;

namespace Diva;

public sealed class MotionFarcWriter
{
    public string SetName { get; private set; }
    public Motion Motion { get; private set; }
    private DivaFiles diva;
    public MotionFarcWriter(Motion motion, string setName, DivaFiles diva)
    {
        SetName = setName;
        Motion = motion;
        this.diva = diva;
    }

    public string CreateFarcFile(MotionSet templateSet, string outputDirectory)
    {
        templateSet.Motions[0] = Motion;

        using var stream = new MemoryStream();
        templateSet.Save(stream, true);
        stream.Position = 0;

        var finalSetName = SetName.ToUpperInvariant();
        var outputFile = Path.Combine(outputDirectory, "rom", "rob", $"mot_{finalSetName}.farc");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

        using var farc = new FarcArchive { IsCompressed = true, Alignment = 16 };
        farc.Add($"mot_{finalSetName}.bin", stream, true, ConflictPolicy.Replace);
        farc.Save(outputFile);

        return outputFile.ToString();
    }
}