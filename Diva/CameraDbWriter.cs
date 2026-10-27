using System.Globalization;
using System.Text;

namespace Diva;

public sealed class CameraDbWriter
{
    public string SetName { get; private set; }
    public int Uid { get; private set; }
    public int FramesCount { get; private set; }
    public CameraDbWriter(string setName, int uid, int framesCount)
    {
        SetName = setName;
        Uid = uid;
        FramesCount = framesCount;
    }

    private string BuildDb()
    {
        var finalSetName = SetName.ToUpperInvariant();
        var lines = new List<string>()
        {
            $"category.0.value=CAM{finalSetName}",
            "category.length=1",
            $"uid.0.category=CAM{finalSetName}",
            $"uid.0.org_uid={Uid}",
            $"uid.0.size={FramesCount}",
            $"uid.0.value=A CAM{finalSetName}_BASE",
            $"uid.length=1",
            $"uid.max={Uid}"
        };

        return "#A3DA__________\n#" + DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture) + "\n" + string.Join("\n", lines) + "\n";
    }

    public string CreateDbFile(string outputDirectory)
    {
        var outputFile = Path.Combine(outputDirectory, "rom", "auth_3d", "mod_auth_3d_db.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

        using var writer = new BinaryWriter(File.Create(outputFile));
        writer.Write(new UTF8Encoding(false).GetBytes(BuildDb()));

        return outputFile.ToString();
    }
}