namespace MMDiva;

using Diva;
using Mmd;

public sealed class Baker
{
    public Retargeter Retargeter { get; private set; }
    public Baker(Retargeter retargeter)
    {
        Retargeter = retargeter;
    }
    public List<RobPose.BlockValue[]> Bake(VmdMotion motion)
    {
        int count = motion.MaxFrame * 2 + 1;

        var frames = new List<RobPose.BlockValue[]>(count);
        for (int f = 0; f < count; f++)
        {
            float mmdFrame = f / 2.0f;
            frames.Add(Retargeter.Solve(motion, mmdFrame));
        }

        return frames;
    }
}