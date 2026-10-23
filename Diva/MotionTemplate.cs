using MikuMikuLibrary.Motions;

namespace Diva;

public sealed class MotionTemplate
{
    public Motion Motion { get; }
    public RobSkeleton Skeleton { get; }
    public List<(int block, int keySetOffset)> Layout { get; } = new();
    public int GlobalKeySetOffset { get; }

    public MotionTemplate(Motion motion, RobSkeleton skeleton, IReadOnlyList<string> motDbBoneNames)
    {
        Motion = motion;
        Skeleton = skeleton;

        int keySetCount = motion.KeySets.Count - 1;
        int offset = 0;
        for (int i = 0; offset < keySetCount && i < motion.BoneInfos.Count; i++)
        {
            int blockIndex = skeleton.BlockIndex(BoneName(motion.BoneInfos[i]));
            if (blockIndex == -1)
            {
                // in case when working on, for examlpe, e_kao_cp (skipping to cl_kao) as there is no rob block for that
                i++;
                if (i >= motion.BoneInfos.Count) break;
                blockIndex = skeleton.BlockIndex(BoneName(motion.BoneInfos[i]));
                if (blockIndex == -1) break;
            }
            Layout.Add((blockIndex, offset));
            offset += skeleton.Blocks[blockIndex].KeySetCount;
        }
        GlobalKeySetOffset = offset;

        string BoneName(BoneInfo info) => info.Name ?? motDbBoneNames[(int)info.Id];
    }
}