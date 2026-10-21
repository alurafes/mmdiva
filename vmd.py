import sys
import io
import struct
import os
from dataclasses import dataclass, field

@dataclass
class VmdVec3:
    x: float
    y: float
    z: float


@dataclass
class VmdQuaternion:
    x: float
    y: float
    z: float
    w: float

@dataclass
class VmdBoneFrame:
    frame: int
    position: VmdVec3
    rotation: VmdQuaternion
    interpolation: bytes

@dataclass
class VmdMorphFrame:
    frame: int
    weight: float

@dataclass
class VmdCameraFrame:
    frame: int
    distance: float
    interest_point: VmdVec3
    rotation: VmdVec3
    interpolation: bytes
    fov: int
    orthographic: bool

@dataclass
class VmdIkFrame:
    frame: int
    enabled: bool

@dataclass
class VmdMotion:
    model_name: str = None
    bone_frames: dict[str, list[VmdBoneFrame]] = field(default_factory=dict)
    morph_frames: dict[str, list[VmdMorphFrame]] = field(default_factory=dict)
    camera_frames: list[VmdCameraFrame] = field(default_factory=list)
    ik_frames: list[VmdIkFrame] = field(default_factory=list)
    max_frame: int = 0

def vmd_read_bound(vmd: io.BufferedReader, size: int) -> bytes:
    data = vmd.read(size)
    if len(data) != size:
        raise EOFError()
    return data

def vmd_read_sized_int(vmd: io.BufferedReader, size: int, signed: bool = False) -> int:
    return int.from_bytes(vmd_read_bound(vmd, size), byteorder="little", signed=signed)

def vmd_read_float(vmd: io.BufferedReader) -> int:
    return struct.unpack("<f", vmd_read_bound(vmd, 4))[0]

def vmd_read_vector3(vmd: io.BufferedReader) -> VmdVec3:
    [x, y, z] = struct.unpack("<3f", vmd_read_bound(vmd, 3 * 4))
    return VmdVec3(x, y, z)

def vmd_read_quaternion(vmd: io.BufferedReader) -> VmdQuaternion:
    [x, y, z, w] = struct.unpack("<4f", vmd_read_bound(vmd, 4 * 4))
    return VmdQuaternion(x, y, z, w)

def vmd_read_string(vmd: io.BufferedReader, size: int) -> str:
    data = vmd_read_bound(vmd, size)
    return data[:data.find(b"\x00")].decode("cp932")

def vmd_read_flag(vmd: io.BufferedReader) -> bool:
    return vmd_read_bound(vmd, 1) != b"\x00"

def main():
    if len(sys.argv) != 2:
        raise ValueError("missing vmd path")

    pmx_path = sys.argv[1]

    motion = VmdMotion()

    with open(pmx_path, "rb") as vmd:
        try:
            header = vmd_read_bound(vmd, 30).decode("ascii")
            if not header.startswith("Vocaloid Motion Data"):
                raise ValueError("missing vmd header")
    
            is_new_format = "0002" in header
            motion.name = vmd_read_string(vmd, 20 if is_new_format else 10)
    
            bone_count = vmd_read_sized_int(vmd, 4)
            for _ in range(bone_count):
                name = vmd_read_string(vmd, 15)
                frame = VmdBoneFrame(
                    vmd_read_sized_int(vmd, 4),
                    vmd_read_vector3(vmd),
                    vmd_read_quaternion(vmd),
                    vmd_read_bound(vmd, 64)
                )
                motion.bone_frames.setdefault(name, []).append(frame)
                if frame.frame > motion.max_frame: motion.max_frame = frame.frame
    
            morph_count = vmd_read_sized_int(vmd, 4)
            for _ in range(morph_count):
                name = vmd_read_string(vmd, 15)
                frame = VmdMorphFrame(
                    vmd_read_sized_int(vmd, 4),
                    vmd_read_float(vmd)
                )
                motion.morph_frames.setdefault(name, []).append(frame)
                if frame.frame > motion.max_frame: motion.max_frame = frame.frame
    
            camera_count = vmd_read_sized_int(vmd, 4)
            for _ in range(camera_count):
                frame = VmdCameraFrame(
                    vmd_read_sized_int(vmd, 4),
                    vmd_read_float(vmd),
                    vmd_read_vector3(vmd),
                    vmd_read_vector3(vmd),
                    vmd_read_bound(vmd, 24),
                    vmd_read_sized_int(vmd, 4),
                    vmd_read_flag(vmd)
                )
                motion.camera_frames.append(frame)
                if frame.frame > motion.max_frame: motion.max_frame = frame.frame
    
            light_count = vmd_read_sized_int(vmd, 4)
            vmd.seek(light_count * 28, os.SEEK_CUR)
    
            self_shadow_count = vmd_read_sized_int(vmd, 4)
            vmd.seek(self_shadow_count * 9, os.SEEK_CUR)
    
            ik_count = vmd_read_sized_int(vmd, 4)
            for _ in range(ik_count):
                frame_value = vmd_read_sized_int(vmd, 4)
                vmd_read_flag(vmd)
                frame = VmdIkFrame(frame_value, vmd_read_flag(vmd))
                motion.ik_frames.append(frame)
                if frame.frame > motion.max_frame: motion.max_frame = frame.frame
        except EOFError:
            pass

    print(motion)

if __name__ == "__main__":
    main()