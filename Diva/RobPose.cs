using System.Numerics;
using MikuMikuLibrary.Bones;

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
    public RobSkeleton Skeleton { get; }
    public BlockValue[] Values { get; }
    public Matrix4x4[] Nodes { get; }
    public Matrix4x4[] Matrices { get; }

    public RobPose(RobSkeleton skeleton)
    {
        Skeleton = skeleton;
        Values = new BlockValue[skeleton.Blocks.Count];
        Nodes = new Matrix4x4[skeleton.MotionBoneNames.Count];
        Matrices = new Matrix4x4[skeleton.MatrixCount];
        for (int i = 0; i < Nodes.Length; i++) Nodes[i] = Matrix4x4.Identity;
        for (int i = 0; i < Matrices.Length; i++) Matrices[i] = Matrix4x4.Identity;
    }

    public Matrix4x4 InheritMatrix(RobSkeleton.RobBlock b) => b.HasParent ? Matrices[b.Parent] : Matrix4x4.Identity;
    public static Matrix4x4 RotZyx(Vector3 r) => Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z);
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
}