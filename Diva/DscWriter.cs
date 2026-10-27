namespace Diva;

public sealed class DscWriter
{
    public string SetName { get; private set; }
    public DscBuilder.Dsc Dsc { get; private set; }
    public DscWriter(string setName, DscBuilder.Dsc dsc)
    {
        SetName = setName;
        Dsc = dsc;
    }

    public string CreateDscFile(string outputDirectory, string difficulty = "normal")
    {
        var finalFileName = $"{SetName[..2] + "_" + SetName[2..]}_{difficulty}.dsc".ToLowerInvariant();
        var outputFile = Path.Combine(outputDirectory, "rom", "script", finalFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

        using var writer = new BinaryWriter(File.Create(outputFile));
        writer.Write(0x14050921); // script_format

        var lastTime = 0;
        foreach (var command in Dsc.Commands)
        {
            if (command.Time != lastTime)
            {
                lastTime = command.Time;
                writer.Write(DscBuilder.OpCode("TIME").Value.index);
                writer.Write(lastTime);
            }
            writer.Write(command.Op);
            foreach (var argument in command.Args) writer.Write(argument);
        }
        writer.Write(0);

        return outputFile.ToString();
    }
}