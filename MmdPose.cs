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
        public required Vector3 Min { get; set; }
        public required Vector3 Max { get; set; }
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

    public Vector3 GlobalPosition(int bone) => nodes[bone].GlobalTranslation;
    public Quaternion GlobalRotation(int bone) => nodes[bone].GlobalRotation;
    public Vector3 RestPosition(int bone) => nodes[bone].Bone.Position;

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

    public void Evaluate(VmdMotion motion, float frame)
    {
        foreach (var node in nodes)
        {
            (node.AnimationTranslation, node.AnimationRotation) = motion.SampleBone(node.Bone.BoneName, frame);
            node.IkRotation = Quaternion.Identity;
            node.AppendTranslation = Vector3.Zero;
            node.AppendRotation = Quaternion.Identity;
        }
        foreach (var solver in solvers.Values)
        {
            solver.Enabled = motion.IsIkEnabledForBone(solver.IkNode.Bone.BoneName, frame);
        }
        Update(false);
        Update(true);
    }
    
    private void Update(bool afterPhysics)
    {
        foreach (var node in sortedNodes) if (node.Bone.AfterPhysics == afterPhysics) UpdateLocal(node);
        foreach (var node in sortedNodes) if (node.Parent != null) UpdateGlobal(node);

        foreach (var node in sortedNodes)
        {
            if (node.Bone.AfterPhysics != afterPhysics) continue;
            if (node.Bone.InheritParentBoneIndex >= 0 && node.Bone.InheritParentBoneIndex < nodes.Length && (node.Bone.AppendRotate || node.Bone.AppendTranslate))
            {
                UpdateAppend(node);
                UpdateGlobal(node);
            }
            if (solvers.TryGetValue(node, out var solver) && solver.Enabled)
            {
                Solve(solver);
                UpdateGlobal(node);
            }
        }

        foreach (var node in sortedNodes) if (node.Parent != null) UpdateGlobal(node);
    }

    private static void UpdateLocal(Node node)
    {
        var translation = node.Offset + node.AnimationTranslation;
        if (node.Bone.AppendTranslate) translation += node.AppendTranslation;

        var rotation = node.AnimationRotation;
        if (node.IsIkLink) rotation = node.IkRotation * rotation;
        if (node.Bone.AppendRotate) rotation = rotation * node.AppendRotation;

        node.LocalTranslation = translation;
        node.LocalRotation = Quaternion.Normalize(rotation);
    }

    private static void UpdateGlobal(Node node)
    {
        if (node.Parent != null)
        {
            node.GlobalRotation = Quaternion.Normalize(node.Parent.GlobalRotation * node.LocalRotation);
            node.GlobalTranslation = node.Parent.GlobalTranslation + Vector3.Transform(node.LocalTranslation, node.Parent.GlobalRotation);
        }
        else
        {
            node.GlobalRotation = node.LocalRotation;
            node.GlobalTranslation = node.LocalTranslation;
        }
        foreach (var child in node.Children)
        {
            UpdateGlobal(child);
        }
    }

    private void UpdateAppend(Node node)
    {
        var src = nodes[node.Bone.InheritParentBoneIndex];
        if (node.Bone.AppendRotate)
        {
            Quaternion rotation;
            if (node.Bone.AppendLocal) rotation = src.AnimationRotation;
            else rotation = src.Bone.InheritParentBoneIndex >= 0 && src.Bone.AppendRotate ? src.AppendRotation : src.AnimationRotation;
            if (src.IsIkLink) rotation = src.IkRotation * rotation;
            node.AppendRotation = Quaternion.Slerp(Quaternion.Identity, rotation, node.Bone.InheritWeight ?? 0f);
        }
        if (node.Bone.AppendTranslate)
        {
            Vector3 translation;
            if (node.Bone.AppendLocal) translation = src.AnimationTranslation;
            else translation = src.Bone.InheritParentBoneIndex >= 0 && src.Bone.AppendTranslate ? src.AppendTranslation : src.AnimationTranslation;
            node.AppendTranslation = translation * (node.Bone.InheritWeight ?? 0f);
        }
        UpdateLocal(node);
    }

    private void Solve(IkSolver solver)
    {
        foreach (var c in solver.Chains)
        {
            c.PreviousAngle = Vector3.Zero;
            c.PlaneAngle = 0;
            c.Node.IkRotation = Quaternion.Identity;
            UpdateLocal(c.Node);
            UpdateGlobal(c.Node);
        }

        float maxDist = float.MaxValue;
        for (int i = 0; i < solver.Iterations; i++)
        {
            SolveCore(solver, i);
            float dist = Vector3.Distance(solver.Target.GlobalTranslation, solver.IkNode.GlobalTranslation);
            if (dist < maxDist)
            {
                maxDist = dist;
                foreach (var c in solver.Chains) c.SavedIkRotation = c.Node.IkRotation;
            }
            else
            {
                foreach (var c in solver.Chains)
                {
                    c.Node.IkRotation = c.SavedIkRotation;
                    UpdateLocal(c.Node);
                    UpdateGlobal(c.Node);
                }
                break;
            }
        }
    }

    static Vector3 ToLocal(Node n, Vector3 world) => Vector3.Transform(world - n.GlobalTranslation, Quaternion.Inverse(n.GlobalRotation));
    private void SolveCore(IkSolver solver, int iteration)
    {
        var ikTranslation = solver.IkNode.GlobalTranslation;
        for (int ci = 0; ci < solver.Chains.Count; ci++)
        {
            var chain = solver.Chains[ci];
            var node = chain.Node;
            if (node == solver.Target) continue;

            if (chain.HasLimit)
            {
                bool x = chain.Min.X != 0 || chain.Max.X != 0;
                bool y = chain.Min.Y != 0 || chain.Max.Y != 0;
                bool z = chain.Min.Z != 0 || chain.Max.Z != 0;
                if (x && !y && !z) { SolvePlane(solver, chain, iteration, 0); continue; }
                if (y && !x && !z) { SolvePlane(solver, chain, iteration, 1); continue; }
                if (z && !x && !y) { SolvePlane(solver, chain, iteration, 2); continue; }
            }

            var targetPos = solver.Target.GlobalTranslation;
            var chainIk = ToLocal(node, ikTranslation);
            var chainTarget = ToLocal(node, targetPos);

            chainIk = Vector3.Normalize(chainIk);
            chainTarget = Vector3.Normalize(chainTarget);

            float dot = Math.Clamp(Vector3.Dot(chainTarget, chainIk), -1.0f, 1.0f);

            float angle = MathF.Acos(dot);
            if (angle * 180.0f / MathF.PI < 1.0e-3f) continue;
            angle = Math.Clamp(angle, -solver.LimitAngle, solver.LimitAngle);
            var cross = Vector3.Cross(chainTarget, chainIk);
            if (cross.LengthSquared() < 1e-12f) continue;
            cross = Vector3.Normalize(cross);
            var rot = Quaternion.CreateFromAxisAngle(cross, angle);

            var chainRot = node.IkRotation * node.AnimationRotation * rot;
            if (chain.HasLimit)
            {
                var euler = DecomposeXyz(chainRot, chain.PreviousAngle);
                var clamped = Vector3.Clamp(euler, chain.Min, chain.Max);
                clamped = Vector3.Clamp(clamped - chain.PreviousAngle, new Vector3(-solver.LimitAngle), new Vector3(solver.LimitAngle)) + chain.PreviousAngle;
                chainRot = ComposeXyz(clamped);
                chain.PreviousAngle = clamped;
            }

            node.IkRotation = Quaternion.Normalize(chainRot * Quaternion.Inverse(node.AnimationRotation));
            UpdateLocal(node);
            UpdateGlobal(node);
        }
    }

    private void SolvePlane(IkSolver solver, IkChain chain, int iteration, int axisIndex)
    {
        var axis = axisIndex switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
        float min = axisIndex switch { 0 => chain.Min.X, 1 => chain.Min.Y, _ => chain.Min.Z };
        float max = axisIndex switch { 0 => chain.Max.X, 1 => chain.Max.Y, _ => chain.Max.Z };
        var node = chain.Node;

        var chainIk = ToLocal(node, solver.IkNode.GlobalTranslation);
        var chainTarget = ToLocal(node, solver.Target.GlobalTranslation);
        
        chainIk = Vector3.Normalize(chainIk);
        chainTarget = Vector3.Normalize(chainTarget);

        float dot = Math.Clamp(Vector3.Dot(chainTarget, chainIk), -1.0f, 1.0f);
        float angle = Math.Clamp(MathF.Acos(dot), -solver.LimitAngle, solver.LimitAngle);

        float dot1 = Vector3.Dot(Vector3.Transform(chainTarget, Quaternion.CreateFromAxisAngle(axis, angle)), chainIk);
        float dot2 = Vector3.Dot(Vector3.Transform(chainTarget, Quaternion.CreateFromAxisAngle(axis, -angle)), chainIk);

        float newAngle = chain.PlaneAngle + (dot1 > dot2 ? angle : -angle);
        if (iteration == 0 && (newAngle < min || newAngle > max))
        {
            if (-newAngle > min && -newAngle < max) newAngle *= -1;
            else
            {
                float half = (min + max) * 0.5f;
                if (MathF.Abs(half - newAngle) > MathF.Abs(half + newAngle)) newAngle *= -1;
            }
        }
        newAngle = Math.Clamp(newAngle, min, max);
        chain.PlaneAngle = newAngle;

        node.IkRotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, newAngle) * Quaternion.Inverse(node.AnimationRotation));
        UpdateLocal(node);
        UpdateGlobal(node);
    }

    static Quaternion ComposeXyz(Vector3 e) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, e.X) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, e.Y) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, e.Z);

    static Vector3 DecomposeXyz(Quaternion q, Vector3 before)
    {
        var m = Matrix4x4.CreateFromQuaternion(q);
        float m02 = m.M31, m12 = m.M32, m22 = m.M33, m01 = m.M21, m00 = m.M11, m10 = m.M12, m11 = m.M22;
        Vector3 r;
        float sy = Math.Clamp(m02, -1, 1);
        if (1.0f - MathF.Abs(sy) < 1.0e-6f)
        {
            r.Y = MathF.Asin(sy);
            r.X = 0;
            r.Z = MathF.Atan2(m10, m11);
        }
        else
        {
            r.Y = MathF.Asin(sy);
            r.X = MathF.Atan2(-m12, m22);
            r.Z = MathF.Atan2(-m01, m00);
        }

        float pi = MathF.PI;
        Span<Vector3> tests = stackalloc Vector3[]
        {
            new(r.X + pi, pi - r.Y, r.Z + pi), new(r.X + pi, pi - r.Y, r.Z - pi),
            new(r.X + pi, -pi - r.Y, r.Z + pi), new(r.X + pi, -pi - r.Y, r.Z - pi),
            new(r.X - pi, pi - r.Y, r.Z + pi), new(r.X - pi, pi - r.Y, r.Z - pi),
            new(r.X - pi, -pi - r.Y, r.Z + pi), new(r.X - pi, -pi - r.Y, r.Z - pi),
        };
        float Err(Vector3 v) => MathF.Abs(DiffAngle(v.X, before.X)) + MathF.Abs(DiffAngle(v.Y, before.Y)) + MathF.Abs(DiffAngle(v.Z, before.Z));
        float minErr = Err(r);
        foreach (var t in tests)
        {
            float e = Err(t);
            if (e < minErr) { minErr = e; r = t; }
        }
        return r;
    }

    static float DiffAngle(float a, float b)
    {
        float diff = NormalizeAngle(a) - NormalizeAngle(b);
        if (diff > MathF.PI) return diff - 2 * MathF.PI;
        if (diff < -MathF.PI) return diff + 2 * MathF.PI;
        return diff;
    }

    static float NormalizeAngle(float a)
    {
        float r = a % (2 * MathF.PI);
        if (r < 0) r += 2 * MathF.PI;
        return r;
    }
}