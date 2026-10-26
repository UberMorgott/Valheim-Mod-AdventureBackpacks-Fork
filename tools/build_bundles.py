"""Rebuilds the backpack asset bundles from Adventure Backpacks 2.4.6 at the game's texel density.

  python tools/build_bundles.py <upstream_dir> <fork_dir> <out_dir>

upstream_dir: the 2.4.6 bundles (git show v2.4.6:AdventureBackpacks/Assets/Bundles/<name>).
fork_dir:     this fork's 1.9.x bundles (backpack_*), whose compressed textures replace the classic ones.
out_dir:      written: backpack_* (modern models) and classic_backpack_* (original models).

Needs: pip install UnityPy etcpak texture2ddecoder pillow
"""
import math
import os
import sys

import UnityPy
from PIL import Image
from UnityPy.helpers.ResourceReader import get_resource_data

CAP = 256   # largest texture side kept from the fork's classic textures
STAR = 128  # shared particle texture (512 px BC7 in every bundle)
OLD_ID = "$vapok_mod_item_"
NEW_ID = "$adventurebackpacks_item_"
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


def process(env, fork):
    # Each object is read once and saved at most once: ObjectReader.read() re-parses the original bytes,
    # so a second read would undo an earlier save. Streamed data (.resS) is inlined and the .resS dropped.
    for obj in env.objects:
        if obj.type.name == "MonoBehaviour":
            rename_item(obj)
            continue
        if obj.type.name == "Mesh":
            tree = obj.read_typetree()
            stream = tree.get("m_StreamData")
            if stream and stream["size"]:
                tree["m_VertexData"]["m_DataSize"] = get_resource_data(stream["path"], obj.assets_file, stream["offset"], stream["size"])
                tree["m_StreamData"] = {"offset": 0, "size": 0, "path": ""}
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
        process(env, None)
        save(env, out_dir, "backpack_" + biome)
    for biome in CLASSIC:
        env = UnityPy.load(os.path.join(upstream_dir, "classic_backpack_" + biome))
        process(env, fork_textures(os.path.join(fork_dir, "backpack_" + biome)))
        save(env, out_dir, "classic_backpack_" + biome)


if __name__ == "__main__":
    main(*sys.argv[1:4])
