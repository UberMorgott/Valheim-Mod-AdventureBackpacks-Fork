"""Rebuilds the backpack asset bundles from Adventure Backpacks 2.4.6 at the game's texel density.

  python tools/build_bundles.py <upstream_dir> <fork_dir> <out_dir>

upstream_dir: the 2.4.6 bundles (git show v2.4.6:AdventureBackpacks/Assets/Bundles/<name>).
fork_dir:     this fork's 1.9.x bundles (backpack_*) as of commit 2843026 (texel density matched to vanilla), whose
              compressed textures replace the classic ones. The bundles are Git LFS files, so extract each with
              git show 2843026:AdventureBackpacks/Assets/Bundles/<name> | git lfs smudge > <fork_dir>/<name>
              (a plain git show gives the LFS pointer). Any other commit gives other classic textures and other hashes.
out_dir:      written: backpack_* (modern models) and classic_backpack_* (original models).

The output must match tools/build_bundles.sha256 byte for byte (verified with UnityPy 1.25.0, etcpak 0.9.15,
texture2ddecoder 1.0.6, pillow 13.0.0).

Needs: pip install UnityPy etcpak texture2ddecoder pillow (numpy 2.5.2 comes with UnityPy)
"""
import math
import os
import struct
import sys

import numpy
import UnityPy
from PIL import Image
from UnityPy.helpers.ResourceReader import get_resource_data

CAP = 256   # largest texture side kept from the fork's classic textures
STAR = 128  # shared particle texture (512 px BC7 in every bundle)
OLD_ID = "$vapok_mod_item_"
NEW_ID = "$adventurebackpacks_item_"
# Material _Color tints. The satchel's bone trim and toggle sample vanilla bonefragments_d (sRGB mean 219,212,201),
# a loose-pickup texture that reads as flat white on a worn pack; tinted to vanilla wishbone_d (155,127,106), worn bone.
TINTS = {"M_Satchel_Bone": (0.745, 0.629, 0.555)}
MODERN = ["meadows", "black_forest", "swamp", "mountains", "plains", "mistlands", "ashlands", "deepnorth"]
CLASSIC = ["meadows", "black_forest", "swamp", "mountains", "plains", "mistlands"]


def mips(width):
    return int(math.log2(width)) + 1


def raw(texture):
    stream = texture.m_StreamData
    if stream is not None and stream.size:
        return get_resource_data(stream.path, texture.object_reader.assets_file, stream.offset, stream.size)
    return texture.image_data


def clear_stream(texture):
    if texture.m_StreamData is not None:
        texture.m_StreamData.offset = 0
        texture.m_StreamData.size = 0
        texture.m_StreamData.path = ""


def set_image(texture, image, fmt):
    texture.set_image(image, target_format=fmt, mipmap_count=mips(image.width))
    texture.m_MipCount = mips(image.width)
    clear_stream(texture)


def fork_textures(path):
    result = {}
    for obj in UnityPy.load(path).objects:
        if obj.type.name == "Texture2D":
            texture = obj.read()
            result[texture.m_Name] = texture
    if not result:
        # An LFS pointer or wrong file loads as an empty bundle; the classic textures would then silently stay upstream's.
        raise SystemExit(f"{path}: no textures (Git LFS pointer instead of the bundle?)")
    return result


def rename_item(obj):
    # The upstream prefabs carry Vapok's ids ($vapok_mod_item_*); the fork's code (BackpackFactory item names,
    # Backpacks.BackpackTypes) and translations use $adventurebackpacks_item_* since 1.9.14.4.
    try:
        tree = obj.read_typetree()
    except Exception:
        return
    item = tree.get("m_itemData")
    if not item:
        return
    shared = item["m_shared"]
    renamed = False
    for key in ("m_name", "m_description"):
        if shared[key].startswith(OLD_ID):
            shared[key] = NEW_ID + shared[key][len(OLD_ID):]
            renamed = True
    if renamed:
        obj.save_typetree(tree)


def tint(obj):
    tree = obj.read_typetree()
    rgb = TINTS.get(tree["m_Name"])
    if rgb is None:
        return
    colors = tree["m_SavedProperties"]["m_Colors"]
    color = next(value for key, value in colors if key == "_Color")
    color["r"], color["g"], color["b"] = rgb
    obj.save_typetree(tree)


# Vertex attribute byte sizes by Unity VertexAttributeFormat (Float32, Float16, UNorm8, SNorm8, UNorm16, SNorm16,
# UInt8, SInt8, UInt16, SInt16, UInt32, SInt32).
FORMAT_SIZE = [4, 2, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4]
POSITION, WEIGHTS, INDICES = 0, 12, 13
# The modern models (BaalEvan) weight the pack top to Jaw/Head, so it follows the head turning; the vanilla body has
# nothing on the back above the neck, so those weights go to Neck.
TO_NECK = ("Jaw", "Head")
# Their weights are one-sided although the meshes are mirror-symmetric: SM_Backpack_01 has RightShoulder on 1678 verts
# (418 of them left of the centre), LeftShoulder on 136, and 420 left verts have no shoulder weight while their mirror
# twin has, so the left strap stays on the spine while the right one moves. The right side carries the authored straps
# (more shoulder weight, asserted per mesh), so every left vertex with a mirror twin takes the twin's weights with
# Left*/Right* bones swapped, as Blender's "mirror vertex groups" does. Then the shoulder pair's weight is split by x
# between the two joints (all left beyond the left joint, all right beyond the right one, linear in between), which
# makes the centre 50/50 and covers vertices without a twin. Arm weights already sit on their own side.
SHOULDERS = ("LeftShoulder", "RightShoulder")
# Mirror twins lie within 1e-6 of the mesh's x range; widening to 1e-5 finds no further vertex in any modern mesh.
TWIN = 1e-5


def vertex_streams(vertex_data):
    """(start, stride) per stream: Unity packs streams back to back, each start aligned to 16 bytes."""
    channels = vertex_data["m_Channels"]
    count = vertex_data["m_VertexCount"]
    streams, start = [], 0
    for stream in range(max(c["stream"] for c in channels if c["dimension"]) + 1):
        stride = sum(FORMAT_SIZE[c["format"]] * c["dimension"] for c in channels if c["dimension"] and c["stream"] == stream)
        streams.append((start, stride))
        start = (start + stride * count + 15) // 16 * 16
    return streams


def channel_reader(vertex_data, channel, fmt):
    c = vertex_data["m_Channels"][channel]
    start, stride = vertex_streams(vertex_data)[c["stream"]]
    return lambda v: start + v * stride + c["offset"], struct.Struct("<" + fmt * c["dimension"])


def joint_x(bindpose):
    # The joint's mesh-space position is the translation of the inverse bindpose.
    return float(numpy.linalg.inv(numpy.array([[bindpose[f"e{r}{c}"] for c in range(4)] for r in range(4)]))[0, 3])


def bone_weights(tree):
    """Per vertex: (position, list of (bone index, weight))."""
    vd = tree["m_VertexData"]
    data = vd["m_DataSize"]
    w_at, w_fmt = channel_reader(vd, WEIGHTS, "f")
    i_at, i_fmt = channel_reader(vd, INDICES, "I")
    p_at, p_fmt = channel_reader(vd, POSITION, "f")
    out = []
    for v in range(vd["m_VertexCount"]):
        out.append((p_fmt.unpack_from(data, p_at(v)), list(zip(i_fmt.unpack_from(data, i_at(v)), w_fmt.unpack_from(data, w_at(v))))))
    return out


def reweight(verts, names, left_x, right_x):
    """New 4-influence (index, weight) lists. verts: bone_weights() output; names: bone names by index."""
    bone = {name: i for i, name in enumerate(names)}
    neck = bone["Neck"]
    weights = []
    for _, pairs in verts:
        merged = {}
        for i, w in pairs:
            if w:
                i = neck if names[i] in TO_NECK else i
                merged[i] = merged.get(i, 0.0) + w
        weights.append(merged)

    l, r = bone[SHOULDERS[0]], bone[SHOULDERS[1]]
    side = numpy.sign(right_x)  # the right joint's side of x = 0
    pos = numpy.array([p for p, _ in verts])
    xs = pos[:, 0] * side       # > 0 on the right
    shoulder = numpy.array([w.get(l, 0.0) + w.get(r, 0.0) for w in weights])
    assert shoulder[xs > 0].sum() >= shoulder[xs < 0].sum(), "left side carries more shoulder weight than the right"
    swap = [bone.get("Right" + n[4:], i) if n.startswith("Left") else bone.get("Left" + n[5:], i) if n.startswith("Right") else i
            for i, n in enumerate(names)]
    tolerance = TWIN * numpy.ptp(pos[:, 0])
    mirrored = pos * numpy.array([-1.0, 1.0, 1.0])
    source = list(weights)
    for v in numpy.flatnonzero(xs < -tolerance):
        distance = numpy.sqrt(((pos - mirrored[v]) ** 2).sum(1))
        twin = int(distance.argmin())
        if distance[twin] <= tolerance:
            weights[v] = {swap[i]: w for i, w in source[twin].items()}

    out = []
    for (p, _), merged in zip(verts, weights):
        total = merged.pop(l, 0.0) + merged.pop(r, 0.0)
        if total:
            t = min(1.0, max(0.0, (p[0] - right_x) / (left_x - right_x)))
            for i, w in ((l, total * t), (r, total * (1.0 - t))):
                if w:
                    merged[i] = w
        # Unity skins with at most 4 influences, heaviest first; a dropped one is renormalised away.
        kept = sorted(merged.items(), key=lambda iw: (-iw[1], iw[0]))[:4]
        total = sum(w for _, w in kept)
        kept = [(i, w / total) for i, w in kept]
        assert abs(sum(w for _, w in kept) - 1.0) < 1e-5 and not any(names[i] in TO_NECK for i, _ in kept)
        out.append(kept + [(0, 0.0)] * (4 - len(kept)))
    return out


def fix_weights(tree, names):
    vd = tree["m_VertexData"]
    c = vd["m_Channels"]
    if c[WEIGHTS]["format"] != 0 or c[INDICES]["format"] != 10 or c[WEIGHTS]["dimension"] != 4 or c[INDICES]["dimension"] != 4:
        raise SystemExit(f"{tree['m_Name']}: unexpected skin vertex format {c[WEIGHTS]} {c[INDICES]}")
    left_x, right_x = (joint_x(tree["m_BindPose"][names.index(n)]) for n in SHOULDERS)
    data = bytearray(vd["m_DataSize"])
    w_at, w_fmt = channel_reader(vd, WEIGHTS, "f")
    i_at, i_fmt = channel_reader(vd, INDICES, "I")
    for v, new in enumerate(reweight(bone_weights(tree), names, left_x, right_x)):
        i_fmt.pack_into(data, i_at(v), *(i for i, _ in new))
        w_fmt.pack_into(data, w_at(v), *(w for _, w in new))
    vd["m_DataSize"] = bytes(data)


def skinned_bones(env):
    """Mesh path id -> bone names of the SkinnedMeshRenderer that draws it."""
    by_id = {obj.path_id: obj for obj in env.objects}
    result = {}
    for obj in env.objects:
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        smr = obj.read_typetree()
        if smr["m_Mesh"]["m_PathID"]:
            result[smr["m_Mesh"]["m_PathID"]] = [
                by_id[by_id[b["m_PathID"]].read_typetree()["m_GameObject"]["m_PathID"]].read_typetree()["m_Name"] for b in smr["m_Bones"]]
    return result


def process(env, fork, reweigh=False):
    # Each object is read once and saved at most once: ObjectReader.read() re-parses the original bytes,
    # so a second read would undo an earlier save. Streamed data (.resS) is inlined and the .resS dropped.
    skins = skinned_bones(env) if reweigh else {}
    for obj in env.objects:
        if obj.type.name == "MonoBehaviour":
            rename_item(obj)
            continue
        if obj.type.name == "Material":
            tint(obj)
            continue
        if obj.type.name == "Mesh":
            tree = obj.read_typetree()
            stream = tree.get("m_StreamData")
            if stream and stream["size"]:
                tree["m_VertexData"]["m_DataSize"] = get_resource_data(stream["path"], obj.assets_file, stream["offset"], stream["size"])
                tree["m_StreamData"] = {"offset": 0, "size": 0, "path": ""}
                changed = True
            else:
                changed = False
            if obj.path_id in skins and tree["m_VertexData"]["m_Channels"][WEIGHTS]["dimension"]:
                fix_weights(tree, skins[obj.path_id])
                changed = True
            if changed:
                obj.save_typetree(tree)
            continue
        if obj.type.name != "Texture2D":
            continue

        texture = obj.read()
        source = fork.get(texture.m_Name) if fork else None
        if texture.m_Name == "star_06":
            set_image(texture, texture.image.convert("RGBA").resize((STAR, STAR), Image.LANCZOS), 25)
        elif source is not None and source.m_Width <= CAP:
            texture.m_Width = source.m_Width
            texture.m_Height = source.m_Height
            texture.m_TextureFormat = int(source.m_TextureFormat)
            texture.image_data = raw(source)
            texture.m_CompleteImageSize = source.m_CompleteImageSize
            texture.m_MipCount = source.m_MipCount
            clear_stream(texture)
        elif source is not None:
            set_image(texture, source.image.convert("RGBA").resize((CAP, CAP), Image.LANCZOS), int(source.m_TextureFormat))
        elif texture.m_StreamData is not None and (texture.m_StreamData.size or texture.m_StreamData.path):
            texture.image_data = raw(texture)
            clear_stream(texture)
        else:
            continue
        texture.save()

    for name in [name for name in env.file.files if name.endswith(".resS")]:
        del env.file.files[name]


def save(env, out_dir, name):
    data = env.file.save(packer="lzma")
    with open(os.path.join(out_dir, name), "wb") as handle:
        handle.write(data)
    print(name, len(data))


def main(upstream_dir, fork_dir, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for biome in MODERN:
        env = UnityPy.load(os.path.join(upstream_dir, "backpack_" + biome))
        process(env, None, reweigh=True)
        save(env, out_dir, "backpack_" + biome)
    for biome in CLASSIC:
        env = UnityPy.load(os.path.join(upstream_dir, "classic_backpack_" + biome))
        process(env, fork_textures(os.path.join(fork_dir, "backpack_" + biome)))
        save(env, out_dir, "classic_backpack_" + biome)


if __name__ == "__main__":
    main(*sys.argv[1:4])
