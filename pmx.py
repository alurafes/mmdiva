import sys
import struct
import io
import os
from dataclasses import dataclass

@dataclass
class PmxHeader:
    version: float
    length: int
    encoding: str
    additional_vec4: int
    vertex_index_size: int
    texture_index_size: int
    material_index_size: int
    bone_index_size: int
    morph_index_size: int
    rigidbody_index_size: int
    model_name: str
    english_model_name: str
    comment: str
    english_comment: str

@dataclass
class PmxVec3:
    x: float
    y: float
    z: float

@dataclass(init=False)
class PmxIkLink:
    link_bone_index: int
    link_has_limit: bool
    min: PmxVec3 = None
    max: PmxVec3 = None

@dataclass(init=False)
class PmxBone:
    bone_name: str
    english_bone_name: str
    position: PmxVec3
    parent_bone_index: int
    layer: int
    flags: int

    tail_bone_index: int = None
    tail_offset: PmxVec3 = None

    inherit_parent_bone_index: int = None
    inherit_weight: float = None

    fixed_axis: PmxVec3 = None

    ik_target_bone_index: int = None
    ik_loop_count: int = None
    ik_limit_angle: float = None
    ik_link_count: int = None
    ik_links: list[PmxIkLink] = None

def pmx_read_sized_int(pmx: io.BufferedReader, size: int, signed: bool = False) -> int:
    data = pmx.read(size)
    return int.from_bytes(data, byteorder="little", signed=signed)

def pmx_read_float(pmx: io.BufferedReader) -> int:
    return struct.unpack("<f", pmx.read(4))[0]

def pmx_read_flag(pmx: io.BufferedReader) -> bool:
    value = pmx.read(1)
    return value != b"\x00"

def pmx_read_vector3(pmx: io.BufferedReader) -> PmxVec3:
    [x, y, z] = struct.unpack("<3f", pmx.read(3 * 4))
    return PmxVec3(x, y, z)

def pmx_read_text(pmx: io.BufferedReader, encoding: str) -> str:
    [buffer_size] = struct.unpack("<i", pmx.read(4))
    return pmx.read(buffer_size).decode(encoding)

def pmx_read_header(pmx: io.BufferedReader) -> PmxHeader:
    if (pmx.read(4).decode() != "PMX "):
        raise ValueError("missing PMX file header")

    [version, length] = struct.unpack("fb", pmx.read(5))
    encoding = "utf-8" if pmx.read(1) == b"\x01" else "utf-16"
    [
        additional_vec4, 
        vertex_index_size, 
        texture_index_size, 
        material_index_size,
        bone_index_size,
        morph_index_size,
        rigidbody_index_size
    ] = struct.unpack("b" * 7, pmx.read(7))

    model_name = pmx_read_text(pmx, encoding)
    english_model_name = pmx_read_text(pmx, encoding)
    comment = pmx_read_text(pmx, encoding)
    english_comment = pmx_read_text(pmx, encoding)

    return PmxHeader(
        version, 
        length, 
        encoding, 
        additional_vec4, 
        vertex_index_size,
        texture_index_size, 
        material_index_size,
        bone_index_size,
        morph_index_size,
        rigidbody_index_size,
        model_name,
        english_model_name,
        comment,
        english_comment,
    )

def pmx_determine_weight_type_size(weight_type: bytes, header: PmxHeader) -> int: 
    match weight_type:
        case b"\x00": return header.bone_index_size # BDEF1
        case b"\x01": return header.bone_index_size * 2 + 4 # BDEF2
        case b"\x02": return header.bone_index_size * 4 + 4 * 4 # BDEF4
        case b"\x03": return header.bone_index_size * 2 + 4 + 4 * 3 * 3 # SDEF
        case b"\x04": return header.bone_index_size * 4 + 4 * 4 # QDEF
        case _: raise ValueError(f"unsupported vertex weight type {weight_type}")

def pmx_skip_vertex(pmx: io.BufferedReader, header: PmxHeader):
    vertex_data_size = 4 * 3 + 4 * 3 + 4 * 2 + 4 * header.additional_vec4
    pmx.seek(vertex_data_size, os.SEEK_CUR)
    
    weight_type = pmx.read(1)
    weight_type_size = pmx_determine_weight_type_size(weight_type, header)
    pmx.seek(weight_type_size, os.SEEK_CUR)

    pmx.seek(4, os.SEEK_CUR) # edge scale

def pmx_skip_face(pmx: io.BufferedReader, header: PmxHeader):
    pmx.seek(header.vertex_index_size, os.SEEK_CUR)

def pmx_skip_texture(pmx: io.BufferedReader, header: PmxHeader):
    pmx_read_text(pmx, header.encoding)

def pmx_skip_material(pmx: io.BufferedReader, header: PmxHeader):
    pmx_read_text(pmx, header.encoding) # name
    pmx_read_text(pmx, header.encoding) # eng name

    material_size_base = 4 * 4 + 4 * 3 + 4 + 4 * 3 + 1 + 4 * 4 + 4 + header.texture_index_size * 2 + 1

    pmx.seek(material_size_base, os.SEEK_CUR)
    toon_flag = pmx.read(1)
    toon_index_size = header.texture_index_size if toon_flag == b"\x00" else 1
    pmx.seek(toon_index_size, os.SEEK_CUR)
    pmx_read_text(pmx, header.encoding) # metadata
    pmx.seek(4, os.SEEK_CUR) # surface count

def pmx_read_bone(pmx: io.BufferedReader, header: PmxHeader) -> PmxBone:
    bone = PmxBone()

    bone.bone_name = pmx_read_text(pmx, header.encoding) # name
    bone.english_bone_name = pmx_read_text(pmx, header.encoding) # eng name
    bone.position = pmx_read_vector3(pmx)
    bone.parent_bone_index = pmx_read_sized_int(pmx, header.bone_index_size, True)
    bone.layer = pmx_read_sized_int(pmx, 4)
    bone.flags = pmx_read_sized_int(pmx, 2)
    bone.ik_links = list()

    if (bone.flags & 0x0001) != 0: bone.tail_bone_index = pmx_read_sized_int(pmx, header.bone_index_size)
    else: bone.tail_offset = pmx_read_vector3(pmx)

    if (bone.flags & 0x0300) != 0:
        bone.inherit_parent_bone_index = pmx_read_sized_int(pmx, header.bone_index_size, True)
        bone.inherit_weight = pmx_read_float(pmx)

    if (bone.flags & 0x0400) != 0: bone.fixed_axis = pmx_read_vector3(pmx)

    if (bone.flags & 0x0800) != 0: pmx.seek(4 * 3 * 2, os.SEEK_CUR)
    if (bone.flags & 0x2000) != 0: pmx.seek(4, os.SEEK_CUR)

    if (bone.flags & 0x0020) != 0:
        bone.ik_target_bone_index = pmx_read_sized_int(pmx, header.bone_index_size)
        bone.ik_loop_count = pmx_read_sized_int(pmx, 4)
        bone.ik_limit_angle = pmx_read_float(pmx)
        bone.ik_link_count = pmx_read_sized_int(pmx, 4)
        
        for _ in range(bone.ik_link_count):
            link = PmxIkLink()
            link.link_bone_index = pmx_read_sized_int(pmx, header.bone_index_size)
            link.link_has_limit = pmx_read_flag(pmx)

            if link.link_has_limit:
                link.min = pmx_read_vector3(pmx)
                link.max = pmx_read_vector3(pmx)

            bone.ik_links.append(link)
    
    return bone

def main():
    if len(sys.argv) != 2:
        raise ValueError("missing pmx path")

    pmx_path = sys.argv[1]

    with open(pmx_path, "rb") as pmx:
        pmx_header = pmx_read_header(pmx)

        [vertex_count] = struct.unpack("i", pmx.read(4))
        for _ in range(vertex_count):
            pmx_skip_vertex(pmx, pmx_header)

        [face_count] = struct.unpack("i", pmx.read(4))

        for _ in range(face_count):
            pmx_skip_face(pmx, pmx_header)

        [texture_count] = struct.unpack("i", pmx.read(4))
        for _ in range(texture_count):
            pmx_skip_texture(pmx, pmx_header)

        [material_count] = struct.unpack("i", pmx.read(4))
        for _ in range(material_count):
            pmx_skip_material(pmx, pmx_header)

        [bone_count] = struct.unpack("i", pmx.read(4))

        bones: list[PmxBone] = []

        for _ in range(bone_count):
            bones.append(pmx_read_bone(pmx, pmx_header))

        print(f"{pmx_header}, {bones}")

if __name__ == "__main__":
    main()