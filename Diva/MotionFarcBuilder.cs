using MikuMikuLibrary.Archives;
using MikuMikuLibrary.Motions;

namespace Diva;

public sealed class MotionFarcBuilder
{
    public string SetName { get; private set; }
    public Motion Motion { get; private set; }
    private DivaFiles diva;
    public MotionFarcBuilder(Motion motion, string setName, DivaFiles diva)
    {
        SetName = setName;
        Motion = motion;
        this.diva = diva;
    }

    public string CreateFarcFile(MotionSet templateSet, string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory)) throw new DirectoryNotFoundException($"{outputDirectory} doesn't exist");

        templateSet.Motions[0] = Motion;

        using var stream = new MemoryStream();
        templateSet.Save(stream, true);
        stream.Position = 0;

        var finalSetName = SetName.ToUpperInvariant();
        var outputFile = Path.Combine(outputDirectory, $"mot_{finalSetName}.farc");

        using var farc = new FarcArchive { IsCompressed = true, Alignment = 16 };
        farc.Add($"mot_{finalSetName}.bin", stream, true, ConflictPolicy.Replace);
        farc.Save(outputFile);

        return outputFile.ToString();
    }
}