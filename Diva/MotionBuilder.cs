using System.Numerics;
using MikuMikuLibrary.Archives;
using MikuMikuLibrary.Bones;
using MikuMikuLibrary.Databases;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.Motions;

namespace Diva;

public sealed class MotionBuilder
{
    public sealed class Options
    {
        public float RotationToleranceRad { get; set; } = 0.0015f;   
        public float PositionToleranceMeters { get; set; } = 0.00002f;
    }
    
    public Options BuilderOptions { get; private set; } 
    public RobSkeleton Skeleton { get; private set; }
    public MotionSet Set { get; private set; }
    public Motion BaseMotion { get; private set; }
    public MotionDatabase BaseMotionDatabase { get; private set; }
    private MotionTemplate motionTemplate;
    private DivaFiles diva;

    private void LoadBaseMotionAndTemplate()
    {
        string motDbPath = diva.ResolveFile("rom/rob/mot_db.farc");
        if (motDbPath == null) throw new ArgumentException("mot_db.farc not found");
        using var motDbFarc = BinaryFile.Load<FarcArchive>(motDbPath);
        var motDbEntry = motDbFarc.FileNames.First();
        using var motDbStream = motDbFarc.Open(motDbEntry, EntryStreamMode.MemoryStream);
        BaseMotionDatabase = BinaryFile.Load<MotionDatabase>(motDbStream);
        var setInfo = BaseMotionDatabase.GetMotionSetInfo("PV001");

        // the idea is to use an already working motion file as a template for my own motion file. The game requires things to be in correct order, I ain't figring it out 
        string baseMotPath = diva.ResolveFile("rom/rob/mot_pv001.farc");
        if (baseMotPath == null) throw new ArgumentException("mot_pv001.farc not found");
        using var baseMotFarc = BinaryFile.Load<FarcArchive>(baseMotPath);
        using var baseMotStream = baseMotFarc.Open(baseMotFarc.FileNames.First(x => x.EndsWith(".bin")), EntryStreamMode.MemoryStream);
        Set = new MotionSet();
        Set.Load(baseMotStream);

        BaseMotion = Set.Motions[0];
        motionTemplate = new MotionTemplate(BaseMotion, Skeleton, BaseMotionDatabase.BoneNames);
    }

    public MotionBuilder(RobSkeleton skeleton, Options builderOptions, DivaFiles diva)
    {
        BuilderOptions = builderOptions;
        this.diva = diva;
        Skeleton = skeleton;
        LoadBaseMotionAndTemplate();
    }

    public Motion Build(IReadOnlyList<RobPose.BlockValue[]> frames)
    {
        Console.WriteLine($"{BaseMotion.Id} ${BaseMotion.Name}");
        var motion = new Motion
        {
            Id = BaseMotion.Id,
            Name = BaseMotion.Name,
            HighBits = BaseMotion.HighBits,
            FrameCount = (ushort)Math.Min(frames.Count, ushort.MaxValue),
        };

        foreach (var boneInfo in BaseMotion.BoneInfos) motion.BoneInfos.Add(new BoneInfo { Id = boneInfo.Id, Name = boneInfo.Name });

        foreach (var (blockIndex, _) in motionTemplate.Layout)
        {
            var block = motionTemplate.Skeleton.Blocks[blockIndex];
            if (block.DisableMotAnim != 0)
            {
                for (int k = 0; k < block.KeySetCount; k++) motion.KeySets.Add(new KeySet());
                continue;
            }

            var translations = new Vector3[frames.Count];
            var rotations = new Vector3[frames.Count];
            for (int frame = 0; frame < frames.Count; frame++)
            {
                var blockValue = frames[frame][blockIndex];
                translations[frame] = block.IsIk ? blockValue.Target : blockValue.Translation;
                rotations[frame] = blockValue.Rotation;
            }
            UnwrapEuler(rotations);

            switch (block.BoneType)
            {
                case BoneType.PositionRotation:
                case BoneType.HeadIKRotation:
                case BoneType.ArmIKRotation:
                case BoneType.LegIKRotation:
                    AddChannels(motion, translations, BuilderOptions.PositionToleranceMeters);
                    AddChannels(motion, rotations, BuilderOptions.RotationToleranceRad);
                    break;
                case BoneType.Position:
                case BoneType.Type1:
                    AddChannels(motion, translations, BuilderOptions.PositionToleranceMeters);
                    break;
                default:
                    AddChannels(motion, rotations, BuilderOptions.RotationToleranceRad);
                    break;
            }
        }

        for (int k = 0; k < 7; k++) motion.KeySets.Add(new KeySet());
        return motion;
    }

    private static void AddChannels(Motion motion, Vector3[] values, float tolerance)
    {
        motion.KeySets.Add(CompressFrames(values.Select(v => v.X).ToArray(), tolerance));
        motion.KeySets.Add(CompressFrames(values.Select(v => v.Y).ToArray(), tolerance));
        motion.KeySets.Add(CompressFrames(values.Select(v => v.Z).ToArray(), tolerance));
    }

    private static void UnwrapEuler(Vector3[] rotations)
    {
        for (int frame = 1; frame < rotations.Length; frame++)
        {
            var rotation = rotations[frame];
            var previousRotation = rotations[frame - 1];
            rotations[frame] = Nearest(rotation, previousRotation);
        }

        static Vector3 Nearest(Vector3 v, Vector3 prev) => new(Wrap(v.X, prev.X), Wrap(v.Y, prev.Y), Wrap(v.Z, prev.Z));
        static float Wrap(float value, float previousValue) => value + 2 * MathF.PI * MathF.Round((previousValue - value) / (2 * MathF.PI));
    }

    // no need to store ALL key frames for all motions. Especially if mmd animation doesn't animate fingers. Basically this is a greedy conversion from dense to sparse keyframes by sampling the interpolation curve the game uses
    private static KeySet CompressFrames(float[] values, float tolerance)
    {
        var keySet = new KeySet();
        if (values.Length == 0) return keySet;

        float first = values[0];
        if (values.All(x => MathF.Abs(x - first) < 1e-6f))
        {
            if (MathF.Abs(first) > 1e-7f) keySet.Keys.Add(new Key { Frame = 0, Value = first });
            return keySet;
        }

        var slope = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            if (i == 0) slope[i] = values[1] - values[0];
            else if (i == values.Length - 1) slope[i] = values[values.Length - 1] - values[values.Length - 2];
            else slope[i] = (values[i + 1] - values[i - 1]) * 0.5f;
        }

        keySet.HasTangents = true;
        const int maxSegment = 120;
        int start = 0;
        keySet.Keys.Add(new Key { Frame = 0, Value = values[0], Tangent = slope[0] });
        while (start < values.Length - 1)
        {
            int best = start + 1;
            for (int end = start + 2; end < values.Length && end - start <= maxSegment; end++)
            {
                bool ok = true;
                for (int k = start + 1; k < end; k++)
                {
                    float p = InterpolateMotValue(values[start], values[end], slope[start], slope[end], start, end, k);
                    if (MathF.Abs(p - values[k]) > tolerance)
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok) break;
                best = end;
            }
            keySet.Keys.Add(new Key { Frame = (ushort)best, Value = values[best], Tangent = slope[best] });
            start = best;
        }
        return keySet;
    }

    // once again, rediva comes in clutch!! mot.cpp
    private static float InterpolateMotValue(float p1, float p2, float t1, float t2, float f1, float f2, float f)
    {
        float df = f - f1;
        float t = df / (f2 - f1);
        float t_1 = t - 1.0f;
        return (t_1 * t1 + t * t2) * t_1 * df + (t * 2.0f - 3.0f) * (t * t) * (p1 - p2) + p1;
    }
}