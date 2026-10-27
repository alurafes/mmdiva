namespace Diva;

public sealed class DscBuilder
{
    public double LengthSeconds { get; private set; }
    public int Bpm { get; private set; }
    public DscBuilder(double lengthSeconds, int bpm)
    {
        LengthSeconds = lengthSeconds;
        Bpm = bpm;
    }
    public sealed class Dsc
    {
        public sealed record Command(int Time, int Op, int[] Args);
        public List<Command> Commands { get; private set; } = new List<Command>();
        public void AddCommand(int time, string name, params int[] args)
        {
            var opcode = OpCode(name);
            if (opcode == null) throw new ArgumentException($"Invalid opcode {name}");

            Commands.Add(new Command(time, opcode.Value.index, args));
        }
    }
    // opcode name, index, paramaters count
    private static Dictionary<string, (int index, int paramatersCount)> opcodes = new()
    {
        ["END"] = (0, 0),
        ["TIME"] = (1, 1),
        ["MIKU_MOVE"] = (2, 4),
        ["MIKU_ROT"] = (3, 2),
        ["MIKU_DISP"] = (4, 2),
        ["MIKU_SHADOW"] = (5, 2),
        ["SET_MOTION"] = (7, 4),
        ["SET_PLAYDATA"] = (8, 2),
        ["DATA_CAMERA"] = (13, 2),
        ["CHANGE_FIELD"] = (14, 1),
        ["EYE_ANIM"] = (18, 3),
        ["MUSIC_PLAY"] = (25, 0),
        ["BAR_TIME_SET"] = (28, 2),
        ["PV_END"] = (32, 0),
        ["PV_BRANCH_MODE"] = (65, 1),
    };
    public static (int index, int paramatersCount)? OpCode(string name) => opcodes.TryGetValue(name, out var opcode) ? opcode : null;
    public Dsc BuildEmptyChart()
    {
        var dsc = new Dsc();
        dsc.AddCommand(0, "PV_BRANCH_MODE", 0);
        dsc.AddCommand(0, "TIME", 0);
        dsc.AddCommand(0, "MUSIC_PLAY");
        dsc.AddCommand(0, "BAR_TIME_SET", Bpm, 3);
        dsc.AddCommand(0, "CHANGE_FIELD", 1);
        dsc.AddCommand(0, "DATA_CAMERA", 0, 1);
        dsc.AddCommand(0, "MIKU_DISP", 0, 1);
        dsc.AddCommand(0, "MIKU_MOVE", 0, 0, 0, 0);
        dsc.AddCommand(0, "MIKU_ROT", 0, 0);
        dsc.AddCommand(0, "SET_PLAYDATA", 0, 1);
        dsc.AddCommand(0, "SET_MOTION", 0, 1, -1, 1000);
        dsc.AddCommand(0, "MIKU_SHADOW", 0, 1);
        dsc.AddCommand(0, "EYE_ANIM", 0, 1, 0);
        dsc.AddCommand((int)Math.Round(LengthSeconds * 100000), "PV_END");
        return dsc;
    }
}