
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

public sealed class VmdBoneFrame
{
    public int Frame { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public byte[] Interpolation { get; set; } = Array.Empty<byte>();
    public float Bezier(int channel, float x)
    {
        float x1 = Interpolation[channel + 0] / 127.0f, y1 = Interpolation[channel + 4] / 127.0f;
        float x2 = Interpolation[channel + 8] / 127.0f, y2 = Interpolation[channel + 12] / 127.0f;
        return VmdMotion.EvalBezier(x1, y1, x2, y2, x);
    }
}

public sealed class VmdMorphFrame
{
    public int Frame { get; set; }
    public float Weight { get; set; }
}

public sealed class VmdCameraFrame
{
    public int Frame { get; set; }
    public float Distance { get; set; }
    public Vector3 InterestPoint { get; set; }
    public Vector3 Rotation { get; set; }
    public byte[] Interpolation { get; set; } = Array.Empty<byte>();
    public int Fov { get; set; }
    public bool Orthographic { get; set; }
}

public sealed class VmdIkFrame
{
    public int Frame { get; set; }
    public Dictionary<string, bool> Enabled { get; set; } = new Dictionary<string, bool>();
}

public sealed class VmdMotion
{
    public string? ModelName { get; set; }

    public Dictionary<string, List<VmdBoneFrame>> BoneFrames { get; } = new();
    public Dictionary<string, List<VmdMorphFrame>> MorphFrames { get; } = new();

    public List<VmdCameraFrame> CameraFrames { get; } = new();
    public List<VmdIkFrame> IkFrames { get; } = new();

    public int MaxFrame { get; set; }

    public (Vector3 position, Quaternion quaternion) SampleBone(string name, float frame)
    {
        if (!BoneFrames.TryGetValue(name, out var frames) || frames.Count == 0) return (Vector3.Zero, Quaternion.Identity);
        if (frame <= frames[0].Frame) return (frames[0].Position, frames[0].Rotation);
        if (frame >= frames[^1].Frame) return (frames[^1].Position, frames[^1].Rotation);

        int low = 0, high = frames.Count - 1;
        
        // mmd animations are 30 fps, so for sampling at high fps reasons frames are floats. searching two frames nearby
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (frames[mid].Frame <= frame) low = mid; else high = mid;
        }
        var f0 = frames[low];
        var f1 = frames[high];
        float t = (frame - f0.Frame) / (f1.Frame - f0.Frame);

        var pos = new Vector3(
            Lerp(f0.Position.X, f1.Position.X, f1.Bezier(0, t)),
            Lerp(f0.Position.Y, f1.Position.Y, f1.Bezier(1, t)),
            Lerp(f0.Position.Z, f1.Position.Z, f1.Bezier(2, t)));
        var rot = Quaternion.Slerp(f0.Rotation, f1.Rotation, f1.Bezier(3, t));
        return (pos, rot);
    }

    public bool IsIkEnabledForBone(string ikBoneName, float frame)
    {
        bool enabled = true;
        foreach (var ikFrame in IkFrames)
        {
            if (ikFrame.Frame > frame) break;
            if (ikFrame.Enabled.TryGetValue(ikBoneName, out var e)) enabled = e;
        }
        return enabled;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    // generated shit
    public static float EvalBezier(float x1, float y1, float x2, float y2, float x)
    {
        if (x1 == y1 && x2 == y2) return x;

        float low = 0, high = 1, t = x;
        for (int i = 0; i < 24; i++)
        {
            t = (low + high) * 0.5f;
            float bx = Cubic(x1, x2, t);
            if (bx < x) low = t; else high = t;
        }
        return Cubic(y1, y2, t);

        static float Cubic(float p1, float p2, float t)
        {
            float it = 1 - t;
            return 3 * it * it * t * p1 + 3 * it * t * t * p2 + t * t * t;
        }
    }
}

public static class VmdReader
{
    private static readonly Encoding Cp932 = CreateCp932();

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    private static byte[] ReadBound(BinaryReader reader, int size)
    {
        byte[] data = reader.ReadBytes(size);
        if (data.Length != size) throw new EndOfStreamException("Unexpected end of VMD file.");
        return data;
    }

    private static int ReadInt32(BinaryReader reader)
    {
        return reader.ReadInt32();
    }

    private static float ReadFloat(BinaryReader reader)
    {
        return reader.ReadSingle();
    }

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader));
    }

    private static Quaternion ReadQuaternion(BinaryReader reader)
    {
        float x = ReadFloat(reader);
        float y = ReadFloat(reader);
        float z = ReadFloat(reader);
        float w = ReadFloat(reader);

        return new Quaternion(x, y, z, w);
    }

    private static string ReadString(BinaryReader reader, int size)
    {
        byte[] data = ReadBound(reader, size);

        int nullIndex = Array.IndexOf(data, (byte)0);
        int length = nullIndex >= 0 ? nullIndex : data.Length;

        return Cp932.GetString(data, 0, length);
    }

    private static bool ReadFlag(BinaryReader reader)
    {
        return ReadBound(reader, 1)[0] != 0;
    }

    private static void Skip(BinaryReader reader, long size)
    {
        long target = checked(reader.BaseStream.Position + size);
        if (target > reader.BaseStream.Length) throw new EndOfStreamException("Unexpected end of VMD file.");

        reader.BaseStream.Position = target;
    }

    private static void UpdateMaxFrame(VmdMotion motion, int frame)
    {
        if (frame > motion.MaxFrame) motion.MaxFrame = frame;
    }

    public static VmdMotion Read(string path)
    {
        var motion = new VmdMotion();

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        try
        {
            string header = Encoding.ASCII.GetString(ReadBound(reader, 30));

            if (!header.StartsWith("Vocaloid Motion Data", StringComparison.Ordinal)) throw new InvalidDataException("Missing VMD header.");

            bool isNewFormat = header.Contains("0002", StringComparison.Ordinal);
            motion.ModelName = ReadString(reader, isNewFormat ? 20 : 10);

            int boneCount = ReadInt32(reader);
            for (int i = 0; i < boneCount; i++)
            {
                string name = ReadString(reader, 15);
                var frame = new VmdBoneFrame
                {
                    Frame = ReadInt32(reader),
                    Position = ReadVector3(reader),
                    Rotation = ReadQuaternion(reader),
                    Interpolation = ReadBound(reader, 64)
                };

                if (!motion.BoneFrames.TryGetValue(name, out var frames))
                {
                    frames = new List<VmdBoneFrame>();
                    motion.BoneFrames[name] = frames;
                }

                frames.Add(frame);
                UpdateMaxFrame(motion, frame.Frame);
            }

            int morphCount = ReadInt32(reader);
            for (int i = 0; i < morphCount; i++)
            {
                string name = ReadString(reader, 15);

                var frame = new VmdMorphFrame
                {
                    Frame = ReadInt32(reader),
                    Weight = ReadFloat(reader)
                };

                if (!motion.MorphFrames.TryGetValue(name, out var frames))
                {
                    frames = new List<VmdMorphFrame>();
                    motion.MorphFrames[name] = frames;
                }

                frames.Add(frame);
                UpdateMaxFrame(motion, frame.Frame);
            }

            int cameraCount = ReadInt32(reader);
            for (int i = 0; i < cameraCount; i++)
            {
                var frame = new VmdCameraFrame
                {
                    Frame = ReadInt32(reader),
                    Distance = ReadFloat(reader),
                    InterestPoint = ReadVector3(reader),
                    Rotation = ReadVector3(reader),
                    Interpolation = ReadBound(reader, 24),
                    Fov = ReadInt32(reader),
                    Orthographic = ReadFlag(reader)
                };

                motion.CameraFrames.Add(frame);
                UpdateMaxFrame(motion, frame.Frame);
            }

            int lightCount = ReadInt32(reader);
            Skip(reader, (long)lightCount * 28);

            int shadowCount = ReadInt32(reader);
            Skip(reader, (long)shadowCount * 9);

            int ikCount = ReadInt32(reader);
            for (int i = 0; i < ikCount; i++)
            {
                int frameNumber = ReadInt32(reader);
                ReadFlag(reader);
                var frame = new VmdIkFrame
                {
                    Frame = frameNumber
                };
                int count = ReadInt32(reader);
                for (int j = 0; j < count; j++)
                {
                    string name = ReadString(reader, 20);
                    frame.Enabled[name] = ReadFlag(reader);
                }
                motion.IkFrames.Add(frame);
                UpdateMaxFrame(motion, frame.Frame);
            }
        }
        catch (EndOfStreamException)
        {
            
        }

        return motion;
    }
}