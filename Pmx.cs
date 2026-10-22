
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

public sealed class PmxHeader
{
    public float Version { get; set; }
    public int Length { get; set; }
    public string EncodingName { get; set; } = "utf-8";
    public int AdditionalVec4 { get; set; }
    public int VertexIndexSize { get; set; }
    public int TextureIndexSize { get; set; }
    public int MaterialIndexSize { get; set; }
    public int BoneIndexSize { get; set; }
    public int MorphIndexSize { get; set; }
    public int RigidbodyIndexSize { get; set; }
    public string ModelName { get; set; } = "";
    public string EnglishModelName { get; set; } = "";
    public string Comment { get; set; } = "";
    public string EnglishComment { get; set; } = "";
}

public sealed class PmxIkLink
{
    public int LinkBoneIndex { get; set; }
    public bool LinkHasLimit { get; set; }
    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }
}

public sealed class PmxBone
{
    public required int Index { get; set; }
    public string BoneName { get; set; } = "";
    public string EnglishBoneName { get; set; } = "";
    public Vector3 Position { get; set; }
    public int ParentBoneIndex { get; set; } = -1;
    public int Layer { get; set; }
    public int Flags { get; set; }

    public int TailBoneIndex { get; set; } = -1;
    public Vector3? TailOffset { get; set; }

    public int InheritParentBoneIndex { get; set; } = -1;
    public float? InheritWeight { get; set; }

    public Vector3? FixedAxis { get; set; }

    public int IkTargetBoneIndex { get; set; } = -1;
    public int? IkLoopCount { get; set; }
    public float? IkLimitAngle { get; set; }
    public int? IkLinkCount { get; set; }

    public List<PmxIkLink> IkLinks { get; } = new();

    public bool IsIk => (Flags & 0x0020) != 0;
    public bool AppendRotate => (Flags & 0x0100) != 0;
    public bool AppendTranslate => (Flags & 0x0200) != 0;
    public bool AppendLocal => (Flags & 0x0080) != 0;
    public bool AfterPhysics => (Flags & 0x1000) != 0;
}

public sealed class PmxModel
{
    public PmxHeader? Header { get; set; }
    public List<PmxBone> Bones { get; } = new();
    public Dictionary<string, int> BoneIndexByName { get; } = new Dictionary<string, int>();
}

public static class PmxReader
{
    private static byte[] ReadBound(BinaryReader pmx, int size)
    {
        byte[] data = pmx.ReadBytes(size);
        if (data.Length != size) throw new EndOfStreamException("Unexpected end of PMX file.");
        return data;
    }

    private static void Skip(BinaryReader pmx, long size)
    {
        long target = checked(pmx.BaseStream.Position + size);
        if (target > pmx.BaseStream.Length) throw new EndOfStreamException("Unexpected end of PMX file.");
        pmx.BaseStream.Position = target;
    }

    private static int ReadSizedInt(BinaryReader pmx, int size, bool signed = false)
    {
        byte[] data = ReadBound(pmx, size);
        long value = 0;

        for (int i = 0; i < size; i++) value |= (long)data[i] << (8 * i);
        if (signed)
        {
            int bits = size * 8;
            if ((data[size - 1] & 0x80) != 0) value -= 1L << bits;
        }

        return checked((int)value);
    }

    private static float ReadFloat(BinaryReader pmx)
    {
        return BitConverter.ToSingle(ReadBound(pmx, 4));
    }

    private static bool ReadFlag(BinaryReader pmx)
    {
        return ReadBound(pmx, 1)[0] != 0;
    }

    private static Vector3 ReadVector3(BinaryReader pmx)
    {
        return new Vector3(ReadFloat(pmx), ReadFloat(pmx), ReadFloat(pmx));
    }

    private static string ReadText(BinaryReader pmx, string encodingName)
    {
        int bufferSize = ReadSizedInt(pmx, 4, signed: true);
        if (bufferSize < 0) throw new InvalidDataException("Negative PMX string length.");

        Encoding encoding = encodingName switch
        {
            "utf-8" => Encoding.UTF8,
            "utf-16" => Encoding.Unicode,
            _ => throw new InvalidDataException($"Unsupported encoding: {encodingName}")
        };

        return encoding.GetString(ReadBound(pmx, bufferSize));
    }

    public static PmxHeader ReadHeader(BinaryReader pmx)
    {
        byte[] magic = ReadBound(pmx, 4);

        if (Encoding.ASCII.GetString(magic) != "PMX ") throw new InvalidDataException("Missing PMX file header.");

        float version = ReadFloat(pmx);
        int length = unchecked((sbyte)ReadBound(pmx, 1)[0]);

        bool utf8 = ReadFlag(pmx);
        string encodingName = utf8 ? "utf-8" : "utf-16";

        int additionalVec4 = ReadBound(pmx, 1)[0];
        int vertexIndexSize = ReadBound(pmx, 1)[0];
        int textureIndexSize = ReadBound(pmx, 1)[0];
        int materialIndexSize = ReadBound(pmx, 1)[0];
        int boneIndexSize = ReadBound(pmx, 1)[0];
        int morphIndexSize = ReadBound(pmx, 1)[0];
        int rigidbodyIndexSize = ReadBound(pmx, 1)[0];

        return new PmxHeader
        {
            Version = version,
            Length = length,
            EncodingName = encodingName,
            AdditionalVec4 = additionalVec4,
            VertexIndexSize = vertexIndexSize,
            TextureIndexSize = textureIndexSize,
            MaterialIndexSize = materialIndexSize,
            BoneIndexSize = boneIndexSize,
            MorphIndexSize = morphIndexSize,
            RigidbodyIndexSize = rigidbodyIndexSize,
            ModelName = ReadText(pmx, encodingName),
            EnglishModelName = ReadText(pmx, encodingName),
            Comment = ReadText(pmx, encodingName),
            EnglishComment = ReadText(pmx, encodingName)
        };
    }

    private static int DetermineWeightTypeSize(byte weightType, PmxHeader header)
    {
        return weightType switch
        {
            0x00 => header.BoneIndexSize,          // BDEF1
            0x01 => header.BoneIndexSize * 2 + 4,  // BDEF2
            0x02 => header.BoneIndexSize * 4 + 16, // BDEF4
            0x03 => header.BoneIndexSize * 2 + 40, // SDEF
            0x04 => header.BoneIndexSize * 4 + 16, // QDEF
            _ => throw new InvalidDataException($"Unsupported vertex weight type: {weightType}")
        };
    }

    private static void SkipVertex(BinaryReader pmx, PmxHeader header)
    {
        Skip(pmx, 12 + 12 + 8 + 16L * header.AdditionalVec4);

        byte weightType = ReadBound(pmx, 1)[0];
        Skip(pmx, DetermineWeightTypeSize(weightType, header));

        Skip(pmx, 4); // Edge scale
    }

    private static void SkipFace(BinaryReader pmx, PmxHeader header)
    {
        Skip(pmx, header.VertexIndexSize);
    }

    private static void SkipTexture(BinaryReader pmx, PmxHeader header)
    {
        ReadText(pmx, header.EncodingName);
    }

    private static void SkipMaterial(BinaryReader pmx, PmxHeader header)
    {
        ReadText(pmx, header.EncodingName); // Name
        ReadText(pmx, header.EncodingName); // English name

        long materialSizeBase =
            4L * 4 +
            4L * 3 +
            4 +
            4L * 3 +
            1 +
            4L * 4 +
            4 +
            header.TextureIndexSize * 2L +
            1;

        Skip(pmx, materialSizeBase);

        bool toonFlag = ReadFlag(pmx);
        int toonIndexSize = toonFlag ? 1 : header.TextureIndexSize;

        Skip(pmx, toonIndexSize);

        ReadText(pmx, header.EncodingName); // Metadata
        Skip(pmx, 4); // Surface count
    }

    public static PmxBone ReadBone(BinaryReader pmx, PmxHeader header, int index)
    {
        var bone = new PmxBone
        {
            Index = index,
            BoneName = ReadText(pmx, header.EncodingName),
            EnglishBoneName = ReadText(pmx, header.EncodingName),
            Position = ReadVector3(pmx),
            ParentBoneIndex = ReadSizedInt(pmx, header.BoneIndexSize, signed: true),
            Layer = ReadSizedInt(pmx, 4),
            Flags = ReadSizedInt(pmx, 2)
        };

        if ((bone.Flags & 0x0001) != 0)
        {
            bone.TailBoneIndex = ReadSizedInt(pmx, header.BoneIndexSize, signed: true);
        }
        else
        {
            bone.TailOffset = ReadVector3(pmx);
        }

        if ((bone.Flags & 0x0300) != 0)
        {
            bone.InheritParentBoneIndex = ReadSizedInt(pmx, header.BoneIndexSize, signed: true);
            bone.InheritWeight = ReadFloat(pmx);
        }

        if ((bone.Flags & 0x0400) != 0) bone.FixedAxis = ReadVector3(pmx);
        if ((bone.Flags & 0x0800) != 0) Skip(pmx, 4 * 3 * 2);
        if ((bone.Flags & 0x2000) != 0) Skip(pmx, 4);

        if (bone.IsIk)
        {
            bone.IkTargetBoneIndex = ReadSizedInt(pmx, header.BoneIndexSize, signed: true);
            bone.IkLoopCount = ReadSizedInt(pmx, 4);
            bone.IkLimitAngle = ReadFloat(pmx);
            bone.IkLinkCount = ReadSizedInt(pmx, 4);

            for (int i = 0; i < bone.IkLinkCount; i++)
            {
                var link = new PmxIkLink
                {
                    LinkBoneIndex = ReadSizedInt(pmx, header.BoneIndexSize, signed: true),
                    LinkHasLimit = ReadFlag(pmx)
                };

                if (link.LinkHasLimit)
                {
                    link.Min = ReadVector3(pmx);
                    link.Max = ReadVector3(pmx);
                }

                bone.IkLinks.Add(link);
            }
        }

        return bone;
    }

    public static PmxModel Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var model = new PmxModel
        {
            Header = ReadHeader(reader)
        };

        PmxHeader header = model.Header;

        int vertexCount = ReadSizedInt(reader, 4);
        for (int i = 0; i < vertexCount; i++) SkipVertex(reader, header);

        int faceCount = ReadSizedInt(reader, 4);
        for (int i = 0; i < faceCount; i++) SkipFace(reader, header);

        int textureCount = ReadSizedInt(reader, 4);
        for (int i = 0; i < textureCount; i++) SkipTexture(reader, header);

        int materialCount = ReadSizedInt(reader, 4);
        for (int i = 0; i < materialCount; i++) SkipMaterial(reader, header);

        int boneCount = ReadSizedInt(reader, 4);
        for (int i = 0; i < boneCount; i++)
        {
            var bone = ReadBone(reader, header, i);
            model.Bones.Add(bone);
            model.BoneIndexByName.Add(bone.BoneName, i);
        }

        return model;
    }
}