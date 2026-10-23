using System.Numerics;
using MikuMikuLibrary.Archives;
using MikuMikuLibrary.Bones;
using MikuMikuLibrary.Objects;

namespace Diva;

// got from here: github.com/korenkonder/ReDIVA (rob.cpp)
public sealed class RobPose
{
    public struct BlockValue
    {
        public Vector3 Rotation;
        public Vector3 Translation;
        public Vector3 Target;
    }
    public RobSkeleton Skeleton { get; private set; }
    public BlockValue[] Values { get; private set; }
    public Matrix4x4[] Nodes { get; private set; }
    public Matrix4x4[] Matrices { get; private set; }

    public RobPose(DivaFiles diva)
    {
        Skeleton = new RobSkeleton(diva);
        Values = new BlockValue[Skeleton.Blocks.Count];
        Nodes = new Matrix4x4[Skeleton.MotionBoneNames.Count];
        Matrices = new Matrix4x4[Skeleton.MatrixCount];
        for (int i = 0; i < Nodes.Length; i++) Nodes[i] = Matrix4x4.Identity;
        for (int i = 0; i < Matrices.Length; i++) Matrices[i] = Matrix4x4.Identity;

        var restPose = RobRestPoseLoader.Load(diva); // setting to rest pose by default
        Apply(restPose);
    }

    public Matrix4x4 InheritMatrix(RobSkeleton.RobBlock b) => b.HasParent ? Matrices[b.Parent] : Matrix4x4.Identity;
    public static Matrix4x4 RotZyx(Vector3 r) => Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z);
    public static Vector3 GetRotZyx(in Matrix4x4 matrix)
    {
        Vector3 r;
        if (-matrix.M13 >= 1.0f) r.Y = MathF.PI / 2;
        else if (-matrix.M13 <= -1.0f) r.Y = -MathF.PI / 2;
        else r.Y = MathF.Asin(-matrix.M13);

        if (MathF.Abs(matrix.M13) < 0.999999f)
        {
            r.X = MathF.Atan2(matrix.M23, matrix.M33);
            r.Z = MathF.Atan2(matrix.M12, matrix.M11);
        }
        else
        {
            r.X = 0.0f;
            r.Z = MathF.Atan2(matrix.M32, matrix.M22);
            if (matrix.M13 > 0.0f) r.Z = -r.Z;
        }
        return r;
    }
    public static Vector3 InverseTransform(in Matrix4x4 matrix, Vector3 position)
    {
        Matrix4x4.Invert(matrix, out var inverse);
        return Vector3.Transform(position, inverse);
    }
    static Matrix4x4 RotX(float sin, float cos) => new(1, 0, 0, 0, 0, cos, sin, 0, 0, -sin, cos, 0, 0, 0, 0, 1);
    static Matrix4x4 RotY(float sin, float cos) => new(cos, 0, -sin, 0, 0, 1, 0, 0, sin, 0, cos, 0, 0, 0, 0, 1);
    static Matrix4x4 RotZ(float sin, float cos) => new(cos, sin, 0, 0, -sin, cos, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

    // based on RobBlock::get_ex_rotation.
    private static readonly Dictionary<string, Vector3> ExConstRotations = new()
    {
        ["n_hito_l_ex"] = new(0, 0, -9), ["n_hito_r_ex"] = new(0, 0, -9),
        ["n_ko_l_ex"] = new(-8, 0, 16), ["n_ko_r_ex"] = new(8, 0, 16),
        ["n_kusu_l_ex"] = new(0, 0, 9), ["n_kusu_r_ex"] = new(0, 0, 9),
        ["n_naka_l_ex"] = new(0, 0, -1), ["n_naka_r_ex"] = new(0, 0, -1),
        ["n_oya_l_ex"] = new(75, 12, -24), ["n_oya_r_ex"] = new(-75, -12, -24),
    };

    private void SetNode(RobSkeleton.RobBlock block, int sub, in Matrix4x4 matrix)
    {
        Nodes[block.FirstNode + sub] = matrix;
        int index = block.MatrixOfNode[sub];
        if (index >= 0) Matrices[index] = matrix;
    }

    public void Evaluate()
    {
        for (int i = 0; i < Skeleton.Blocks.Count; i++) EvaluateBlock(i);
    }

    public void EvaluateBlock(int index)
    {
        var block = Skeleton.Blocks[index];
        ref var value = ref Values[index];
        var matrix = InheritMatrix(block);

        if (block.DisableMotAnim != 0)
        {
            matrix = Matrix4x4.CreateTranslation(block.ChainPos.X, block.ChainPos.Y, block.ChainPos.Z) * matrix;
            if (ExConstRotations.TryGetValue(block.Name, out var exRot)) matrix = RotZyx(exRot * (MathF.PI / 180.0f)) * matrix;
            SetNode(block, 0, matrix);
            return;
        }

        switch (block.BoneType)
        {
            case BoneType.Position:
                matrix = Matrix4x4.CreateTranslation(value.Translation) * matrix;
                break;
            case BoneType.Type1:
                matrix = Matrix4x4.CreateTranslation(InverseTransform(matrix, value.Translation)) * matrix;
                break;
            case BoneType.PositionRotation:
                matrix = Matrix4x4.CreateTranslation(value.Translation) * matrix;
                matrix = RotZyx(value.Rotation) * matrix;
                break;
            default:
                matrix = Matrix4x4.CreateTranslation(block.ChainPos) * matrix;
                matrix = RotZyx(value.Rotation) * matrix;
                break;
        }

        SetNode(block, 0, matrix);
        if (block.IsIk) SolveIk(block, value.Target, matrix);
    }

    // RobBlock::solve_ik
    private void SolveIk(RobSkeleton.RobBlock block, Vector3 target, Matrix4x4 matrix)
    {
        var localTarget = InverseTransform(matrix, target);
        float targetLenXySq = localTarget.X * localTarget.X + localTarget.Y * localTarget.Y;
        float targetLenSq = localTarget.LengthSquared();
        float targetLenXy = MathF.Sqrt(targetLenXySq);
        float targetLen = MathF.Sqrt(targetLenSq);

        if (MathF.Abs(targetLenXy) > 0.000001f && MathF.Abs(targetLenSq) > 0.000001f)
        {
            var chain = RotY(-(1.0f / targetLen) * localTarget.Z, (1.0f / targetLen) * targetLenXy) * RotZ((1.0f / targetLenXy) * localTarget.Y, (1.0f / targetLenXy) * localTarget.X);
            matrix = chain * matrix;
        }

        if (block.PoleTarget != 0)
        {
            var up = InverseTransform(matrix, Matrices[block.PoleTarget].Translation);
            float upLen = MathF.Sqrt(up.Y * up.Y + up.Z * up.Z);
            if (upLen > 0.000001f)
            {
                up *= 1.0f / upLen;
                matrix = RotX(up.Z, up.Y) * matrix;
            }
        }

        if (block.BoneType == BoneType.HeadIKRotation)
        {
            SetNode(block, 1, matrix);
            matrix = Matrix4x4.CreateTranslation(block.Len0, 0, 0) * matrix;
            SetNode(block, 2, matrix);
            return;
        }

        float len0 = block.Len0, len1 = block.Len1;
        float rotSin, rotCos, rot2ndSin, rot2ndCos;
        if (targetLenSq > 0.000001f)
        {
            float proj = (targetLenSq - len1 * len1) / len0;
            rotCos = Math.Clamp((proj + len0) / (2.0f * targetLen), -1.0f, 1.0f);
            rot2ndCos = Math.Clamp((proj - len0) / (2.0f * len1), -1.0f, 1.0f);
            rotSin = MathF.Sqrt(1.0f - rotCos * rotCos);
            rot2ndSin = MathF.Sqrt(1.0f - rot2ndCos * rot2ndCos);
            if (block.BoneType == BoneType.LegIKRotation) rotSin = -rotSin;
            else rot2ndSin = -rot2ndSin;
        }
        else
        {
            rotSin = 0; rotCos = 1; rot2ndSin = 0; rot2ndCos = -1;
        }

        matrix = RotZ(rotSin, rotCos) * matrix;
        SetNode(block, 1, matrix);
        matrix = Matrix4x4.CreateTranslation(len0, 0, 0) * matrix;
        matrix = RotZ(rot2ndSin, rot2ndCos) * matrix;
        SetNode(block, 2, matrix);
        matrix = Matrix4x4.CreateTranslation(len1, 0, 0) * matrix;
        SetNode(block, 3, matrix);
    }

    private const float H = MathF.PI / 2;
    private const float F = MathF.PI;
    public static readonly Dictionary<string, Vector3> ConstantRotations = new()
    {
        ["n_hara"] = new(0, H, 0),
        ["n_mune_b"] = new(0, -H, -H),
        ["n_kao"] = new(H, 0, -F),
        ["face_root"] = new(H, 0, H),
        ["n_waki_l"] = new(H, 0, H),
        ["n_waki_r"] = new(H, 0, H),
        ["c_kata_l"] = new(-H, -H, -H),
        ["c_kata_r"] = new(-H, H, H),
    };

    public void Apply(Dictionary<string, Matrix4x4> pose)
    {
        Array.Clear(Values);
        for (int i = 0; i < Skeleton.Blocks.Count; i++)
        {
            var block = Skeleton.Blocks[i];
            ref var value = ref Values[i];

            if (ConstantRotations.TryGetValue(block.Name, out var rotation)) value.Rotation = rotation;
            switch (block.BoneType)
            {
                case BoneType.PositionRotation:
                    value.Translation = pose["kl_kosi_etc_wj"].Translation;
                    break;
                case BoneType.Position:
                    value.Translation = block.ChainPos;
                    break;
            }

            if (block.IsIk)
            {
                var node0 = Matrix4x4.CreateTranslation(block.ChainPos) * InheritMatrix(block);
                Vector3 dir = block.Name switch
                {
                    "cl_mune" => Vector3.UnitY,
                    "cl_kao" => Vector3.UnitZ,
                    _ => -Vector3.UnitY,
                };
                string upper = Skeleton.MotionBoneNames[block.FirstNode + 1];
                if (block.BoneType != BoneType.HeadIKRotation && pose.TryGetValue(upper, out var bw)) dir = Vector3.Normalize(new Vector3(bw.M11, bw.M12, bw.M13));
                float reach = block.BoneType == BoneType.HeadIKRotation ? 0.5f : (block.Len0 + block.Len1) * 0.9999f;
                value.Target = node0.Translation + dir * reach;
            }

            EvaluateBlock(i);
        }
    }
}

public static class RobRestPoseLoader
{
    private const string MIKU_REST_POSE = "mikitm001";
    public static Dictionary<string, Matrix4x4> Load(DivaFiles diva)
    {
        Dictionary<string, Matrix4x4> pose = new(StringComparer.OrdinalIgnoreCase);
        string path = diva.ResolveFile($"rom/objset/{MIKU_REST_POSE}.farc");
        if (path == null || !File.Exists(path)) return pose;

        using var farc = MikuMikuLibrary.IO.BinaryFile.Load<FarcArchive>(path);
        string entry = farc.FileNames.FirstOrDefault(x => x.EndsWith("_obj.bin"));
        if (entry == null) return pose;

        using var stream = farc.Open(entry, EntryStreamMode.MemoryStream);
        var set = new ObjectSet();
        set.Load(stream);

        foreach (var obj in set.Objects)
        {
            if (obj.Skin == null) continue;
            foreach (var bone in obj.Skin.Bones)
            {
                if (!pose.ContainsKey(bone.Name) && Matrix4x4.Invert(bone.InverseBindPoseMatrix, out var world))
                {
                    pose[bone.Name] = world;
                }
            }
        }

        return pose;
    }
}

public static class RobPoseDebug
{
    public static List<MMDiva.Debug.Segment> StickRendererSegments(this RobPose pose)
    {
        var segments = new List<MMDiva.Debug.Segment>();

        for (int n = 0; n < pose.Skeleton.MotionBoneNames.Count; n++)
        {
            int p = pose.Skeleton.NodeParents[n];
            if (p < 0) continue;
            string name = pose.Skeleton.MotionBoneNames[n];
            segments.Add(new MMDiva.Debug.Segment(pose.Nodes[p].Translation, pose.Nodes[n].Translation, MMDiva.Debug.StickRenderer.SideColor(name)));
        }

        return segments;
    }
}