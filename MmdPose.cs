using System.Numerics;

// basically a port of Saba (see readme references) (SHOUTOUT FOR BEING MIT!!! I DONT WANT TO FIGURE THIS OUT)
public sealed class MmdPose
{
    public sealed class Node
    {
        public required PmxBone Bone { get; set; }
        public Node? Parent { get; set; }
        public List<Node> Children { get; } = new();
        public Vector3 Offset { get; set; }
        public Vector3 AnimationTranslation { get; set; }
        public Quaternion AnimationRotation { get; set; } = Quaternion.Identity;
        public Quaternion IkRotation { get; set; } = Quaternion.Identity;
        public bool IsIkLink { get; set; }
        public Vector3 AppendTranslation { get; set; }
        public Quaternion AppendRotation { get; set; } = Quaternion.Identity;
        
        public Vector3 LocalTranslation { get; set; }
        public Quaternion LocalRotation { get; set; } = Quaternion.Identity;
        
        public Vector3 GlobalTranslation { get; set; }
        public Quaternion GlobalRotation { get; set; } = Quaternion.Identity;
    }
    public sealed class IkChain
    {
        public required Node Node { get; set; }
        public required bool HasLimit { get; set; }
        public required Vector3? Min { get; set; }
        public required Vector3? Max { get; set; }
        public Vector3 PreviousAngle { get; set; }
        public float PlaneAngle;
        public Quaternion SavedIkRotation;
    }
    class IkSolver
    {
        public required Node IkNode { get; set; }
        public required Node Target { get; set; }
        public List<IkChain> Chains { get; } = new();
        public int Iterations { get; set; }
        public float LimitAngle { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public PmxModel Model { get; private set; }
    private readonly Node[] nodes;
    private readonly Node[] sortedNodes;
    private readonly Dictionary<Node, IkSolver> solvers = new Dictionary<Node, IkSolver>();

    private void SetParentsAndOffsets()
    {
        foreach (var node in nodes)
        {
            if (node.Bone.ParentBoneIndex >= 0 && node.Bone.ParentBoneIndex < nodes.Length)
            {
                node.Parent = nodes[node.Bone.ParentBoneIndex];
                node.Parent.Children.Add(node);
                node.Offset = node.Bone.Position - node.Parent.Bone.Position;
            }
            else
            {
                node.Offset = node.Bone.Position;
            }
        }
    }

    private void CreateIkSolvers()
    {
        foreach (var node in nodes)
        {
            if (!node.Bone.IsIk || node.Bone.IkTargetBoneIndex < 0 || node.Bone.IkTargetBoneIndex >= nodes.Length) continue;
            IkSolver solver = new IkSolver { 
                IkNode = node, 
                Target = nodes[node.Bone.IkTargetBoneIndex],
                Iterations = node.Bone.IkLoopCount ?? 0,
                LimitAngle = node.Bone.IkLimitAngle ?? 0f
            };
            foreach (var link in node.Bone.IkLinks)
            {
                if (link.LinkBoneIndex < 0 || link.LinkBoneIndex >= nodes.Length) continue;
                Node chainNode = nodes[link.LinkBoneIndex];
                chainNode.IsIkLink = true;
                solver.Chains.Add(new IkChain
                {
                    Node = chainNode,
                    HasLimit = link.LinkHasLimit,
                    Min = link.Min,
                    Max = link.Max
                });
                solvers[node] = solver;
            }
        }
    }

    public MmdPose(PmxModel model)
    {
        Model = model;
        nodes = model.Bones.Select(bone => new Node {Bone = bone}).ToArray();

        SetParentsAndOffsets();
        CreateIkSolvers();

        sortedNodes = nodes.OrderBy(node => node.Bone.AfterPhysics ? 1 : 0).ThenBy(node => node.Bone.Layer).ThenBy(node => node.Bone.Index).ToArray();
    }
}