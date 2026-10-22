
using MikuMikuLibrary.Bones;
using System.Numerics;

namespace Diva;

public sealed class RobSkeleton
{
    public sealed class RobBlock
    {
        public required int Index { get; set; }
        public required string Name { get; set; }
        public required BoneType BoneType { get; set; }
        public required bool HasParent { get; set; }
        public required int Parent { get; set; }
        public required int PoleTarget { get; set; }
        public required int DisableMotAnim { get; set; }
        public required Vector3 ChainPos { get; set; }
        public required int FirstNode { get; set; }
        public int NodeCount { get; set; }
        public int[] MatrixOfNode;
        public float Len0, Len1;
        public bool IsIk => BoneType is BoneType.ArmIKRotation or BoneType.LegIKRotation or BoneType.HeadIKRotation;
    }

    public List<RobBlock> Blocks { get; } = new();
    public List<string> MotionBoneNames { get; } = new();
    public short[] NodeParents { get; private set; } = Array.Empty<short>();
    public int MatrixCount { get; private set; }
    public float HeelHeight { get; private set; }
    public (int block, int sub)[] NodeOwner { get; private set; } = Array.Empty<(int block, int sub)>();

    private readonly Dictionary<string, int> blockByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> motionBoneByName = new(StringComparer.OrdinalIgnoreCase);
    public RobBlock BlockByName(string name) => blockByName.TryGetValue(name, out int i) ? Blocks[i] : null;
    public int MotionBoneIndex(string name) => motionBoneByName.TryGetValue(name, out int i) ? i : -1;

    private Skeleton LoadSkeleton(DivaFiles diva)
    {
        var boneDataPath = diva.ResolveFile("rom/bone_data.bin");
        if (boneDataPath == null) throw new ArgumentException("bone_data.bin not found");
        BoneData boneData = MikuMikuLibrary.IO.BinaryFile.Load<BoneData>(boneDataPath);
        return boneData.Skeletons.FirstOrDefault(x => x.Name == "CMN") ?? throw new InvalidDataException($"CMN not found in {boneDataPath}");
    }

    private void ParseSkeleton(Skeleton skeleton)
    {
        HeelHeight = skeleton.HeelHeight;
        MotionBoneNames.AddRange(skeleton.MotionBoneNames);
        NodeParents = skeleton.ParentIndices.ToArray();

        int chainPos = 0, node = 0, matrix = 0;
        var owners = new List<(int, int)>();

        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            var bone = skeleton.Bones[i];
            var block = new RobBlock
            {
                Index = i,
                Name = bone.Name,
                BoneType = bone.Type,
                HasParent = bone.HasParent,
                Parent = bone.Parent,
                PoleTarget = bone.PoleTarget,
                DisableMotAnim = bone.DisableMotAnim,
                ChainPos = skeleton.Positions[chainPos++],
                FirstNode = node
            };

            switch (block.BoneType)
            {
                case BoneType.Rotation:
                case BoneType.Type1:
                case BoneType.Position:
                case BoneType.PositionRotation:
                    block.NodeCount = 1;
                    block.MatrixOfNode = new[] {matrix++};
                    break;
                case BoneType.HeadIKRotation:
                    block.Len0 = skeleton.Positions[chainPos++].X;
                    block.NodeCount = 3;
                    block.MatrixOfNode = new[] { -1, matrix++, -1 };
                    break;
                case BoneType.ArmIKRotation:
                case BoneType.LegIKRotation:
                    block.Len0 = skeleton.Positions[chainPos++].X;
                    block.Len1 = skeleton.Positions[chainPos++].X;
                    block.NodeCount = 4;
                    block.MatrixOfNode = new[] { matrix++, matrix++, matrix++, -1 };
                    break;
            }

            for (int n = 0; n < block.NodeCount; n++) owners.Add((i, n));
            node += block.NodeCount;
            Blocks.Add(block);
            blockByName[block.Name] = i;
        }

        MatrixCount = matrix;
        NodeOwner = owners.ToArray();
        for (int i = 0; i < MotionBoneNames.Count; i++) motionBoneByName[MotionBoneNames[i]] = i;
    }

    public RobSkeleton(DivaFiles diva)
    {
        var skeleton = LoadSkeleton(diva);
        ParseSkeleton(skeleton);
    }
}