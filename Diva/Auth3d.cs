using System.Globalization;
using System.Numerics;

namespace Diva;

public static class Auth3d
{
    public struct CameraFrame
    {
        public Vector3 Eye;
        public Vector3 Interest;
        public float Roll;
        public float FovX;
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.#######", CultureInfo.InvariantCulture);
    }

    public static string BuildA3daCamera(string pv, IReadOnlyList<CameraFrame> frames, bool[] cameraCuts)
    {
        var lines = new List<string>();

        // god bless functions inside a function :D
        void Static(string key, float value)
        {
            lines.Add($"{key}.type=1");
            lines.Add($"{key}.value={FormatFloat(value)}");
        }

        void Curve(string key, float[] values, float tolerance, bool unwrap = false)
        {
            if (unwrap)
            {
                for (int i = 1; i < values.Length; i++)
                {
                    values[i] -= 2 * MathF.PI * MathF.Round((values[i] - values[i - 1]) / (2 * MathF.PI)); // UnwrapEuler.Wrap
                }
            }
            // same bullshit like in the motion set. compressing all frames if needed
            var keys = CompressFrames(values, cameraCuts, tolerance);
            if (keys.Count == 1)
            {
                Static(key, keys[0].value);
                return;
            }
            for (int i = 0; i < keys.Count; i++)
            {
                var (frame, value, tangent) = keys[i];
                lines.Add($"{key}.key.{i}.data=({frame},{FormatFloat(value)},{FormatFloat(tangent)})");
                lines.Add($"{key}.key.{i}.type=2");
            }
            lines.Add($"{key}.key.length={keys.Count}");
            lines.Add($"{key}.max={frames.Count}");
            lines.Add($"{key}.type=3");
        }
        void Unit(string node)
        {
            foreach (var axis in "xyz") lines.Add($"{node}.rot.{axis}.type=0");
            foreach (var axis in "xyz") Static($"{node}.scale.{axis}", 1);
        }

        const string root = "camera_root.0";
        Unit($"{root}.interest");
        Curve($"{root}.interest.trans.x", frames.Select(f => f.Interest.X).ToArray(), 0.0005f);
        Curve($"{root}.interest.trans.y", frames.Select(f => f.Interest.Y).ToArray(), 0.0005f);
        Curve($"{root}.interest.trans.z", frames.Select(f => f.Interest.Z).ToArray(), 0.0005f);
        Static($"{root}.interest.visibility", 1);
        Unit(root);
        foreach (var axis in "xyz") lines.Add($"{root}.trans.{axis}.type=0");
        Static($"{root}.visibility", 1);
        lines.Add($"{root}.view_point.aspect={FormatFloat(16f / 9f)}");
        Curve($"{root}.view_point.fov", frames.Select(f => f.FovX).ToArray(), 0.0005f);
        lines.Add($"{root}.view_point.fov_is_horizontal=1");
        Curve($"{root}.view_point.roll", frames.Select(f => f.Roll).ToArray(), 0.0005f, true);
        Unit($"{root}.view_point");
        Curve($"{root}.view_point.trans.x", frames.Select(f => f.Eye.X).ToArray(), 0.0005f);
        Curve($"{root}.view_point.trans.y", frames.Select(f => f.Eye.Y).ToArray(), 0.0005f);
        Curve($"{root}.view_point.trans.z", frames.Select(f => f.Eye.Z).ToArray(), 0.0005f);
        Static($"{root}.view_point.visibility", 1);
        lines.Add("camera_root.length=1");
        lines.Add("play_control.begin=0");
        lines.Add("play_control.fps=60");
        lines.Add($"play_control.size={frames.Count}");
        lines.Add("_.converter.version=20050823");
        lines.Add($"_.file_name=CAM{pv.ToUpperInvariant()}_BASE.a3da");
        lines.Add("_.property.version=20050706");

        lines.Sort(StringComparer.Ordinal);

        return "#A3DA__________\n#" + DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture) + "\n" + string.Join("\n", lines) + "\n";
    }

    private static List<(int frame, float value, float tangent)> CompressFrames(float[] values, bool[] cameraCuts, float tolerance)
    {
        if (values.All(x => MathF.Abs(x - values[0]) < 1e-6f)) return new() { (0, values[0], 0) };

        bool Cut(int i) => i >= 0 && i < values.Length - 1 && cameraCuts[i];
        
        var slope = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            bool left = i > 0 && !Cut(i - 1);
            bool right = i < values.Length - 1 && !Cut(i);

            slope[i] = left && right ? (values[i + 1] - values[i - 1]) * 0.5f
                : right ? values[i + 1] - values[i]
                : left ? values[i] - values[i - 1] 
                : 0;
        }

        var keys = new List<(int, float, float)> { (0, values[0], slope[0]) };
        int start = 0;
        while (start < values.Length - 1)
        {
            int best = start + 1;
            if (!Cut(start))
                for (int end = start + 2; end < values.Length && end - start <= 240; end++)
                {
                    if (Cut(end - 1)) break;
                    bool ok = true;
                    for (int k = start + 1; k < end && ok; k++)
                    {
                        float p = InterpolateMotValue(values[start], values[end], slope[start], slope[end], start, end, k) - values[k];
                        ok = MathF.Abs(p) <= tolerance;
                    }
                    if (!ok) break;
                    best = end;
                }
            keys.Add((best, values[best], slope[best]));
            start = best;
        }
        return keys;
    }

    // gotta refactor these out
    private static float InterpolateMotValue(float p1, float p2, float t1, float t2, float f1, float f2, float f)
    {
        float df = f - f1;
        float t = df / (f2 - f1);
        float t_1 = t - 1.0f;
        return (t_1 * t1 + t * t2) * t_1 * df + (t * 2.0f - 3.0f) * (t * t) * (p1 - p2) + p1;
    }
}