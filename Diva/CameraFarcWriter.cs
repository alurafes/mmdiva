using System.Text;
using MikuMikuLibrary.Archives;
using MikuMikuLibrary.Motions;

namespace Diva;

public sealed class CameraFarcWriter
{
    public string SetName { get; private set; }
    public string CameraA3da { get; private set; }
    private DivaFiles diva;
    public CameraFarcWriter(string setName, string cameraA3da, DivaFiles diva)
    {
        SetName = setName;
        CameraA3da = cameraA3da;
        this.diva = diva;
    }

    public string CreateFarcFile(string outputDirectory)
    {
        var finalSetName = SetName.ToUpperInvariant();
        var outputFile = Path.Combine(outputDirectory, "rom", "auth_3d", $"CAM{finalSetName}.farc");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

        using var farc = new FarcArchive { IsCompressed = false, Alignment = 1 };
        farc.Add($"CAM{finalSetName}_BASE.a3da", new MemoryStream(new UTF8Encoding(false).GetBytes(CameraA3da)), true, ConflictPolicy.Replace);
        farc.Save(outputFile);

        return outputFile.ToString();
    }
}