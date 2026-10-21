import sys
import struct
import io
import os
from dataclasses import dataclass, field

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

@dataclass
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
    ik_links: list[PmxIkLink] = field(default_factory=list)

@dataclass
class PmxModel:
    header: PmxHeader = None
    bones: list[PmxBone] = field(default_factory=list)

def pmx_read_bound(pmx: io.BufferedReader, size: int) -> bytes:
    data = pmx.read(size)
    if len(data) != size:
        raise EOFError()
    return data

def pmx_read_sized_int(pmx: io.BufferedReader, size: int, signed: bool = False) -> int:
    return int.from_bytes(pmx_read_bound(pmx, size), byteorder="little", signed=signed)

def pmx_read_float(pmx: io.BufferedReader) -> int:
    return struct.unpack("<f", pmx_read_bound(pmx, 4))[0]

def pmx_read_flag(pmx: io.BufferedReader) -> bool:
    return pmx_read_bound(pmx, 1) != b"\x00"

def pmx_read_vector3(pmx: io.BufferedReader) -> PmxVec3:
    [x, y, z] = struct.unpack("<3f", pmx_read_bound(pmx, 3 * 4))
    return PmxVec3(x, y, z)

def pmx_read_text(pmx: io.BufferedReader, encoding: str) -> str:
    [buffer_size] = struct.unpack("<i", pmx_read_bound(pmx, 4))
    return pmx_read_bound(pmx, buffer_size).decode(encoding)

def pmx_read_header(pmx: io.BufferedReader) -> PmxHeader:
    data = pmx_read_bound(pmx, 4)
    if (data.decode() != "PMX "):
        raise ValueError("missing PMX file header")

    data = pmx_read_bound(pmx, 5)
    [version, length] = struct.unpack("fb", data)
    encoding = "utf-8" if pmx_read_flag(pmx) else "utf-16"
    [
        additional_vec4, 
        vertex_index_size, 
        texture_index_size, 
        material_index_size,
        bone_index_size,
        morph_index_size,
        rigidbody_index_size
    ] = struct.unpack("7b", pmx_read_bound(pmx, 7))

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
    
    weight_type = pmx_read_bound(pmx, 1)
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
    toon_flag = pmx_read_flag(pmx)
    toon_index_size = header.texture_index_size if not toon_flag else 1
    pmx.seek(toon_index_size, os.SEEK_CUR)
    pmx_read_text(pmx, header.encoding) # metadata
    pmx.seek(4, os.SEEK_CUR) # surface count

def pmx_read_bone(pmx: io.BufferedReader, header: PmxHeader) -> PmxBone:
    bone = PmxBone(
        pmx_read_text(pmx, header.encoding),
        pmx_read_text(pmx, header.encoding),
        pmx_read_vector3(pmx),
        pmx_read_sized_int(pmx, header.bone_index_size, True),
        pmx_read_sized_int(pmx, 4),
        pmx_read_sized_int(pmx, 2)
    )

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

    model = PmxModel()

    with open(pmx_path, "rb") as pmx:
        model.header = pmx_read_header(pmx)

        vertex_count = pmx_read_sized_int(pmx, 4)
        for _ in range(vertex_count):
            pmx_skip_vertex(pmx, model.header)

        face_count = pmx_read_sized_int(pmx, 4)
        for _ in range(face_count):
            pmx_skip_face(pmx, model.header)

        texture_count = pmx_read_sized_int(pmx, 4)
        for _ in range(texture_count):
            pmx_skip_texture(pmx, model.header)

        material_count = pmx_read_sized_int(pmx, 4)
        for _ in range(material_count):
            pmx_skip_material(pmx, model.header)

        bone_count = pmx_read_sized_int(pmx, 4)

        for _ in range(bone_count):
            model.bones.append(pmx_read_bone(pmx, model.header))

        print(f"{model}")

if __name__ == "__main__":
    main()