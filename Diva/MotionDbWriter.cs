using System.Globalization;
using System.Text;
using MikuMikuLibrary.Archives;
using MikuMikuLibrary.Databases;

namespace Diva;

public sealed class MotionDbWriter
{
    public MotionBuilder MotionBuilder { get; private set; }
    public string SetName { get; private set; }
    public uint SetId { get; private set; }
    public uint MotionId { get; private set; }

    public MotionDbWriter(MotionBuilder motionBuilder, string setName, uint setId, uint motionId)
    {
        MotionBuilder = motionBuilder;
        SetName = setName;
        SetId = setId;
        MotionId = motionId;
    }
    private MotionDatabase BuildDb()
    {
        var database = new MotionDatabase();
        database.BoneNames.AddRange(MotionBuilder.BaseMotionDatabase.BoneNames);
        MotionSetInfo setInfo = new MotionSetInfo
        {
            Id = SetId,
            Name = SetName,
        };

        MotionInfo motionInfo = new MotionInfo
        {
            Id = MotionId,
            Name = $"{SetName}_ALU_00" // alu as alurafes :sunglasses:
        };

        setInfo.Motions.Add(motionInfo);
        database.MotionSets.Add(setInfo);

        return database;
    }

    public string CreateDbFile(string outputDirectory)
    {
        var outputFile = Path.Combine(outputDirectory, "rom", "rob", "mod_mot_db.farc");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

        var database = BuildDb();

        using var memoryStream = new MemoryStream();
        database.Save(memoryStream, true);
        memoryStream.Position = 0;

        using var farc = new FarcArchive { IsCompressed = true, Alignment = 16 };
        farc.Add("mot_db.bin", memoryStream, true, ConflictPolicy.Replace);
        farc.Save(outputFile);

        return outputFile.ToString();
    }
}