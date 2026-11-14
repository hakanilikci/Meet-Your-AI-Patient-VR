#!/usr/bin/env python3
"""Downloads the consultation room's art from Poly Haven (CC0, https://polyhaven.com/license) into the Unity project.

    python3 ui/tools/fetch_room_assets.py        # ~25 MB, into ui/MedicalVR/Assets/Room/PolyHaven (gitignored)

Models come as FBX with 1k JPG textures (diffuse, normal, roughness/metal, alpha where the model has it), the
lighting as a 1k HDRI. Editor/RoomImport.cs turns them into prefabs and materials. Without them the room falls back
to simple shapes, so the app still runs.
"""
import json
import os
import sys
import urllib.request

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "MedicalVR", "Assets", "Room", "PolyHaven")
MODELS = [
    "modern_arm_chair_01",      # the patient's chair
    "dining_chair_02",          # the doctor's chair
    "metal_office_desk",
    "drawer_cabinet",
    "steel_frame_shelves_01",
    "book_encyclopedia_set_01",
    "potted_plant_01", "potted_plant_02", "potted_plant_04",
    "ceramic_vase_02",          # the vase for the flowers
    "wall_clock",
    "hanging_picture_frame_02",
    "binder_notebook",
    "modern_ceiling_lamp_01",
]
TEXTURES = ["laminate_floor_02", "beige_wall_001"]   # floor, walls
HDRI = "hospital_room"                               # light and reflections
MAPS = ("diff", "nor_gl", "rough", "metal", "arm", "alpha")


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": "MedicalVR-hackathon/1.0"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def save(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return 0
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = get(url)
    with open(path, "wb") as f:
        f.write(data)
    return len(data)


def wanted(name):
    return any(f"_{m}_" in name for m in MAPS) and name.endswith((".jpg", ".png"))


def main():
    total = 0
    for asset in MODELS:
        files = json.loads(get(f"https://api.polyhaven.com/files/{asset}"))
        fbx = files["fbx"]["1k"]["fbx"]
        total += save(fbx["url"], os.path.join(OUT, asset, asset + ".fbx"))
        # JPG maps from the glTF set (the FBX set ships EXR/PNG); alpha maps only exist as PNG
        tex = {k.split("/")[-1]: v["url"] for k, v in files["gltf"]["1k"]["gltf"]["include"].items()}
        tex.update({k.split("/")[-1]: v["url"] for k, v in files["fbx"]["1k"]["fbx"]["include"].items() if "_alpha_" in k})
        for name, url in tex.items():
            if wanted(name):
                total += save(url, os.path.join(OUT, asset, "textures", name))
        print("model", asset)
    for asset in TEXTURES:
        files = json.loads(get(f"https://api.polyhaven.com/files/{asset}"))
        for m, key in (("diff", "Diffuse"), ("nor_gl", "nor_gl"), ("arm", "arm"), ("rough", "Rough")):
            if key in files and "1k" in files[key]:
                fmt = files[key]["1k"].get("jpg") or files[key]["1k"].get("png")
                if fmt:
                    total += save(fmt["url"], os.path.join(OUT, asset, "textures", f"{asset}_{m}_1k.{fmt['url'].rsplit('.', 1)[1]}"))
        print("texture", asset)
    files = json.loads(get(f"https://api.polyhaven.com/files/{HDRI}"))
    total += save(files["hdri"]["1k"]["hdr"]["url"], os.path.join(OUT, "hdri", HDRI + "_1k.hdr"))
    print("hdri", HDRI)
    with open(os.path.join(OUT, "LICENSE.txt"), "w") as f:
        f.write("All assets in this folder: Poly Haven (https://polyhaven.com), CC0 1.0. "
                "Fetched by ui/tools/fetch_room_assets.py.\n")
    print(f"done, {total / 1e6:.1f} MB downloaded into {os.path.normpath(OUT)}")


if __name__ == "__main__":
    sys.exit(main())
