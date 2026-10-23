using System.Numerics;
using Diva;
using MikuMikuLibrary.Bones;
using Mmd;

namespace MMDiva;

public sealed class Retargeter
{

    public class Options
    {
        public bool Fingers = true;
        public bool FootPositions = true;
        public float? Scale;
        public Vector3 Offset;
        public float FloorClampRatio = 0.95f;
        public float PoleHingeWeight = 0.05f;
        public float SoftReachStart = 0.985f;
    }

    public MmdPose MmdPose { get; private set; }
    public RobPose RobPose { get; private set; }
    public Options RetargetOptions { get; private set; }

    private readonly Dictionary<string, Mapping> mappings = new();
    private readonly RobPose.BlockValue[] restValues;
    private readonly Quaternion[] restNodeRot;
    private readonly Vector3[] restNodePos;
    private readonly int mmdRootBone, mmdAnkleL, mmdAnkleR;
    readonly Vector3 mmdRootRest, mmdAnkleLRest, mmdAnkleRRest;
    public float Scale { get; private set; } = 1f;
    public List<string> Warnings { get; } = new();

    private sealed class Mapping
    {
        public NodeMapping NodeMapping;
        public int MmdBoneIndex = -1;
        public Quaternion RestTarget;
    }

    private void DetermineScale()
    {
        int mmdLegL = FindFirstBone("左足"), mmdKneeL = FindFirstBone("左ひざ");
        float mmdLegLength = mmdLegL >= 0 && mmdKneeL >= 0
            ? Vector3.Distance(MmdPose.RestPosition(mmdLegL), MmdPose.RestPosition(mmdKneeL)) + Vector3.Distance(MmdPose.RestPosition(mmdKneeL), MmdPose.RestPosition(mmdAnkleL))
            : MmdPose.RestPosition(mmdRootBone).Y - MmdPose.RestPosition(mmdAnkleL).Y;

        var divaLegL = RobPose.Skeleton.BlockByName("cl_momo_l");
        float divaLegLength = divaLegL.Len0 + divaLegL.Len1;

        Scale = RetargetOptions.Scale ?? divaLegLength / mmdLegLength;
    }

    private static Vector3 ToDiva(Vector3 v) => new(v.X, v.Y, -v.Z);
    private static Quaternion ToDiva(Quaternion q) => new(-q.X, -q.Y, q.Z, q.W);
    // the name is a bit dumb but basically getting the smallest rotation to turn from a to b
    private static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        float dot = Vector3.Dot(a, b);
        if (dot > 0.999999f) return Quaternion.Identity;
        if (dot < -0.999999f)
        {
            var axis = Vector3.Cross(Vector3.UnitX, a);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, a);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c, 1 + dot));
    }

    private static string InheritAlignFrom(string node)
    {
        if (node.StartsWith("nl_") && node.Contains("_c_")) return node.Replace("_c_", "_b_");
        if (node.StartsWith("kl_toe_")) return node.Replace("kl_toe_", "kl_asi_") + "_co";
        return null;
    }

    private Matrix4x4? Desired(string divaNode)
    {
        if (!mappings.TryGetValue(divaNode, out var mapping)) return null;
        var global = ToDiva(MmdPose.GlobalRotation(mapping.MmdBoneIndex));
        return Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(global * mapping.RestTarget));
    }

    private static Vector3 Row(in Matrix4x4 m, int r) => r switch
    {
        0 => new Vector3(m.M11, m.M12, m.M13),
        1 => new Vector3(m.M21, m.M22, m.M23),
        _ => new Vector3(m.M31, m.M32, m.M33),
    };

    private static Matrix4x4 FromRows(Vector3 x, Vector3 y, Vector3 z) => new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);

    private static Matrix4x4 RotOnly(Matrix4x4 m)
    {
        m.Translation = Vector3.Zero;
        return m;
    }
    static Vector3 LocalEuler(in Matrix4x4 desiredRot, in Matrix4x4 parent)
    {
        var p = RotOnly(parent);
        var local = desiredRot * Matrix4x4.Transpose(p);
        return RobPose.GetRotZyx(local);
    }
    static (Vector3 yaw, Vector3 tilt) SplitYaw(in Matrix4x4 q)
    {
        float b = MathF.Atan2(-q.M13, q.M11);
        var rest = q * Matrix4x4.Transpose(Matrix4x4.CreateRotationY(b));
        var tilt = RobPose.GetRotZyx(rest);
        tilt.Y = 0;
        return (new Vector3(0, b, 0), tilt);
    }


    // calculates aa rotation that turns a diva bone's rest rotation into the mmd bone's
    private void DetermineRestTarget()
    {
        var missingBones = new List<string>();
        foreach (var nodeMapping in BoneMap.Create(RetargetOptions.Fingers))
        {
            int node = RobPose.Skeleton.MotionBoneIndex(nodeMapping.DivaNode);
            if (node < 0) continue;

            var mapping = new Mapping { NodeMapping = nodeMapping, MmdBoneIndex = FindFirstBone(nodeMapping.MmdBones) };
            if (mapping.MmdBoneIndex < 0)
            {
                missingBones.Add(nodeMapping.MmdBones[0]);
                continue;
            }
            
            var align = Quaternion.Identity;
            int tipBone = nodeMapping.MmdTips != null ? FindFirstBone(nodeMapping.MmdTips) : -1;
            int tipNode = nodeMapping.DivaTip != null ? RobPose.Skeleton.MotionBoneIndex(nodeMapping.DivaTip) : -1;
            if (tipBone >= 0 && tipNode >= 0)
            {
                var dDiva = restNodePos[tipNode] - restNodePos[node];
                var dMmd = ToDiva(MmdPose.RestPosition(tipBone)) - ToDiva(MmdPose.RestPosition(mapping.MmdBoneIndex));
                if (dDiva.LengthSquared() > 1e-10f && dMmd.LengthSquared() > 1e-10f) align = FromTo(Vector3.Normalize(dDiva), Vector3.Normalize(dMmd));
            }
            else if (nodeMapping.DivaTip == null && InheritAlignFrom(nodeMapping.DivaNode) is string from && mappings.TryGetValue(from, out var prev))
            {
                align = prev.RestTarget * Quaternion.Inverse(restNodeRot[RobPose.Skeleton.MotionBoneIndex(from)]);
            }

            mapping.RestTarget = Quaternion.Normalize(align * restNodeRot[node]);
            mappings[nodeMapping.DivaNode] = mapping;
        }

        if (missingBones.Count > 0) Warnings.Add("MMD bones not found: " + string.Join(", ", missingBones.Distinct()));
    }

    // robPose and mmdPose must be in rest pose already
    public Retargeter(MmdPose mmdPose, RobPose robPose, Options retargetOptions)
    {
        MmdPose = mmdPose;
        RobPose = robPose;
        RetargetOptions = retargetOptions;

        restValues = (RobPose.BlockValue[])RobPose.Values.Clone();
        restNodeRot = RobPose.Nodes.Select(RotationOf).ToArray();
        restNodePos = RobPose.Nodes.Select(m => m.Translation).ToArray();

        mmdRootBone = FindFirstBone("下半身");
        mmdAnkleL = FindFirstBone("左足首");
        mmdAnkleR = FindFirstBone("右足首");

        if (mmdRootBone < 0 || mmdAnkleL < 0 || mmdAnkleR < 0) throw new InvalidDataException("Can't find one of thesebones: 下半身, 左足首, 右足首");

        mmdRootRest = ToDiva(MmdPose.RestPosition(mmdRootBone));
        mmdAnkleLRest = ToDiva(MmdPose.RestPosition(mmdAnkleL));
        mmdAnkleRRest = ToDiva(MmdPose.RestPosition(mmdAnkleR));

        DetermineScale();
        DetermineRestTarget();
    }

    // having a temp variable like this works because blocks are sequential (left pole (tl_up_kata_l) -> left arm (c_kata_l) -> left hand)
    private Vector3 armTarget;

    void SolveArmPole(int poleBlock, RobSkeleton.RobBlock pole, string side)
    {
        var arm = RobPose.Skeleton.BlockByName($"c_kata_{side}");
        var desiredUpperArm = Desired($"j_kata_{side}_wj_cu");
        var desiredForearm = Desired($"j_ude_{side}_wj");
        var shoulder = (Matrix4x4.CreateTranslation(arm.ChainPos) * RobPose.InheritMatrix(arm)).Translation;

        if (desiredUpperArm == null || desiredForearm == null)
        {
            // keep the rest pose arm
            var restTarget = restValues[arm.Index].Target - restNodePos[arm.FirstNode] + shoulder;
            armTarget = restTarget;
            return;
        }

        var upperDir = Row(desiredUpperArm.Value, 0);
        var elbow = shoulder + upperDir * arm.Len0;
        var wrist = elbow + Row(desiredForearm.Value, 0) * arm.Len1;
        armTarget = wrist;

        var axis = wrist - shoulder;
        axis = axis.LengthSquared() > 1e-10f ? Vector3.Normalize(axis) : upperDir;
        var hingeSide = Vector3.Cross(Row(desiredForearm.Value, 2), axis);
        hingeSide = hingeSide.LengthSquared() > 1e-10f ? Vector3.Normalize(hingeSide) : -Vector3.UnitZ;
        var bend = elbow - (shoulder + axis * Vector3.Dot(elbow - shoulder, axis));
        var dir = Vector3.Normalize(bend + hingeSide * (arm.Len0 * RetargetOptions.PoleHingeWeight));

        var polePos = elbow + dir * 0.3f;
        RobPose.Values[poleBlock].Translation = RobPose.InverseTransform(RobPose.InheritMatrix(pole), polePos);
    }

    void SolveLeg(int i, RobSkeleton.RobBlock block, string side)
    {
        var parent = RobPose.InheritMatrix(block);
        var hip = (Matrix4x4.CreateTranslation(block.ChainPos) * parent).Translation;

        var desiredThigh = Desired($"j_momo_{side}_wj");
        var desiredShin = Desired($"j_sune_{side}_wj");
        if (desiredThigh == null || desiredShin == null) return;

        Vector3 ankle;
        if (RetargetOptions.FootPositions)
        {
            int bone = side == "l" ? mmdAnkleL : mmdAnkleR;
            var rest = side == "l" ? mmdAnkleLRest : mmdAnkleRRest;
            int restNode = RobPose.Skeleton.MotionBoneIndex($"e_sune_{side}_cp");
            ankle = restNodePos[restNode] + Scale * (ToDiva(MmdPose.GlobalPosition(bone)) - rest) + RetargetOptions.Offset;
            ankle.Y = MathF.Max(ankle.Y, restNodePos[restNode].Y * RetargetOptions.FloorClampRatio);

            float reach = block.Len0 + block.Len1;
            float soft = reach * RetargetOptions.SoftReachStart;
            var toAnkle = ankle - hip;
            float distance = toAnkle.Length();
            if (distance > soft && distance > 1e-6f)
            {
                float range = reach - soft;
                float eased = soft + range * (1 - MathF.Exp(-(distance - soft) / range));
                ankle = hip + toAnkle * (eased / distance);
            }
        }
        else
        {
            var knee = hip + Row(desiredThigh.Value, 0) * block.Len0;
            ankle = knee + Row(desiredShin.Value, 0) * block.Len1;
        }

        var x = ankle - hip;
        x = x.LengthSquared() > 1e-10f ? Vector3.Normalize(x) : Row(desiredThigh.Value, 0);
        var z = Row(desiredThigh.Value, 2);
        z -= x * Vector3.Dot(z, x);
        z = z.LengthSquared() > 1e-10f ? Vector3.Normalize(z) : Row(desiredThigh.Value, 2);
        var y = Vector3.Cross(z, x);
        var node0 = FromRows(x, y, z);

        RobPose.Values[i].Rotation = LocalEuler(node0, parent);
        RobPose.Values[i].Target = ankle;
    }

    public RobPose.BlockValue[] Solve(VmdMotion motion, float mmdFrame)
    {
        MmdPose.Evaluate(motion, mmdFrame);
        var robValues = RobPose.Values;

        Array.Copy(restValues, RobPose.Values, restValues.Length);
        var rootPos = restNodePos[0] + Scale * (ToDiva(MmdPose.GlobalPosition(mmdRootBone)) - mmdRootRest) + RetargetOptions.Offset;

        for (int i = 0; i < RobPose.Skeleton.Blocks.Count; i++)
        {
            var block = RobPose.Skeleton.Blocks[i];
            string name = block.Name;
            string node0 = RobPose.Skeleton.MotionBoneNames[block.FirstNode];

            switch (block.BoneType)
            {
                case BoneType.PositionRotation:
                    robValues[i].Translation = rootPos;
                    robValues[i].Rotation = Vector3.Zero;
                    break;
                case BoneType.HeadIKRotation:
                {
                    // instead of setting the rotation and target separately we just set the rotation to the final one (from the mmd pose) and put a target directly in front of the final pose so it does nothing
                    string aimed = RobPose.Skeleton.MotionBoneNames[block.FirstNode + 1];
                    var desired = Desired(aimed);
                    if (desired == null) break;
                    var parent = RobPose.InheritMatrix(block);
                    robValues[i].Rotation = LocalEuler(desired.Value, parent);
                    var n0 = RobPose.RotZyx(robValues[i].Rotation) * Matrix4x4.CreateTranslation(block.ChainPos) * parent;
                    robValues[i].Target = n0.Translation + Row(desired.Value, 0) * 0.5f;
                    break;
                }
                case BoneType.Position when name.StartsWith("tl_up_kata_"):
                    SolveArmPole(i, block, name.EndsWith("_l") ? "l" : "r");
                    break;
                case BoneType.ArmIKRotation:
                    robValues[i].Target = armTarget;
                    break;
                case BoneType.LegIKRotation:
                    SolveLeg(i, block, name.EndsWith("_l") ? "l" : "r");
                    break;
                case BoneType.Rotation when block.DisableMotAnim == 0:
                {
                    if (name == "kg_hara_y")
                    {
                        var desired = Desired("n_hara");
                        if (desired == null) break;
                        var etc = Matrix4x4.Transpose(RobPose.RotZyx(RobPose.ConstantRotations["n_hara"])) * desired.Value;
                        (robValues[i].Rotation, robValues[i + 1].Rotation) = SplitYaw(etc);
                    }
                    else if (name == "kl_hara_xz" || name == "kl_kosi_xz")
                    {
                        // set together with the yaw block
                    }
                    else if (name == "kl_kosi_y")
                    {
                        var desired = Desired("kl_kosi_etc_wj");
                        if (desired == null) break;
                        var rotation = desired.Value * Matrix4x4.Transpose(RotOnly(RobPose.InheritMatrix(block)));
                        (robValues[i].Rotation, robValues[i + 1].Rotation) = SplitYaw(rotation);
                    }
                    else if (name == "kl_kosi_etc_wj" || name == "n_hara")
                    {
                        // handled by the yaw/tilt split above
                    }
                    else
                    {
                        var desired = Desired(node0);
                        if (desired != null) robValues[i].Rotation = LocalEuler(desired.Value, RobPose.InheritMatrix(block));
                    }
                    break;
                }
            }
            RobPose.EvaluateBlock(i);
        }

        return (RobPose.BlockValue[])robValues.Clone();
    }

    private static Quaternion RotationOf(Matrix4x4 matrix)
    {
        matrix.Translation = Vector3.Zero;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
    }

    private int FindFirstBone(params string[] names)
    {
        foreach (var name in names)
        {
            int i = MmdPose.Index(name);
            if (i >= 0) return i;
        }
        return -1;
    }

    public record NodeMapping(string DivaNode, string[] MmdBones, string DivaTip = null, string[] MmdTips = null);
    public static class BoneMap
    {
        public static List<NodeMapping> Create(bool fingers)
        {
            var list = new List<NodeMapping>
            {
                new("n_hara", ["腰", "グルーブ", "センター"]),
                new("j_mune_wj", ["上半身"]),
                new("kl_mune_b_wj", ["上半身2", "上半身"]),
                new("kl_kubi", ["首"]),
                new("j_kao_wj", ["頭"]),
                new("kl_kosi_etc_wj", ["下半身"]),
            };

            foreach (var (s, j) in new[] { ("l", "左"), ("r", "右") })
            {
                list.Add(new($"kl_waki_{s}_wj", [$"{j}肩"], $"c_kata_{s}", [$"{j}腕"]));
                list.Add(new($"j_kata_{s}_wj_cu", [$"{j}腕"], $"j_ude_{s}_wj", [$"{j}ひじ"]));
                list.Add(new($"j_ude_{s}_wj", [$"{j}ひじ"], $"e_ude_{s}_cp", [$"{j}手首"]));
                list.Add(new($"kl_te_{s}_wj", [$"{j}手首"], $"nl_naka_{s}_wj", [$"{j}中指１"]));

                list.Add(new($"j_momo_{s}_wj", [$"{j}足D", $"{j}足"], $"j_sune_{s}_wj", [$"{j}ひざD", $"{j}ひざ"]));
                list.Add(new($"j_sune_{s}_wj", [$"{j}ひざD", $"{j}ひざ"], $"e_sune_{s}_cp", [$"{j}足首D", $"{j}足首"]));
                list.Add(new($"kl_asi_{s}_wj_co", [$"{j}足首D", $"{j}足首"], $"kl_toe_{s}_wj", [$"{j}足先EX", $"{j}つま先"]));
                list.Add(new($"kl_toe_{s}_wj", [$"{j}足先EX"]));

                if (!fingers) continue;
                foreach (var (d, m) in new[] { ("hito", "人指"), ("naka", "中指"), ("kusu", "薬指"), ("ko", "小指") })
                {
                    list.Add(new($"nl_{d}_{s}_wj", [$"{j}{m}１"], $"nl_{d}_b_{s}_wj", [$"{j}{m}２"]));
                    list.Add(new($"nl_{d}_b_{s}_wj", [$"{j}{m}２"], $"nl_{d}_c_{s}_wj", [$"{j}{m}３"]));
                    list.Add(new($"nl_{d}_c_{s}_wj", [$"{j}{m}３"]));
                }
                list.Add(new($"nl_oya_{s}_wj", [$"{j}親指０"], $"nl_oya_b_{s}_wj", [$"{j}親指１"]));
                list.Add(new($"nl_oya_b_{s}_wj", [$"{j}親指１"], $"nl_oya_c_{s}_wj", [$"{j}親指２"]));
                list.Add(new($"nl_oya_c_{s}_wj", [$"{j}親指２"]));
            }
            return list;
        }
    }

}