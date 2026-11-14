#!/usr/bin/env python3
"""Downloads every character avatar lists in avatar/avatar_show (Microsoft Rocketbox, MIT licence) and Rocketbox's
seated animations into the Unity project (gitignored, ~650 MB).

    python3 ui/tools/fetch_rocketbox.py
    then Unity: Setup.Configure (or menu MedicalVR > Import Rocketbox characters)

Source: https://github.com/microsoft/Microsoft-Rocketbox
"""
import os
import sys
import urllib.request

RAW = "https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/"
MEDIA = "https://media.githubusercontent.com/media/microsoft/Microsoft-Rocketbox/master/"   # if a file is in Git LFS
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "MedicalVR", "Assets", "Avatar", "Rocketbox")
# avatar's logical names (avatar/avatar_show/index.html) -> Rocketbox folder, texture prefix, maps
MODELS = {
    "patient_male_older": ("Adults/Male_Adult_05", "m009", True),
    "patient_male_older_2": ("Adults/Male_Adult_14", "m012", False),
    "patient_male_south_asian": ("Adults/Male_Adult_17", "m022", False),
    "patient_male_young": ("Adults/Male_Adult_06", "m011", True),
    "patient_female_older": ("Adults/Female_Adult_09", "f009", True),
    "patient_female_elderly": ("Adults/Female_Adult_02", "f002", True),
    "patient_female_young": ("Adults/Female_Adult_17", "f006", True),
    "patient_female_young_2": ("Adults/Female_Adult_15", "f018", True),
    "attending": ("Professions/Medical_Male_03", "m153", False),
}
# per sex (m_ / f_): seated idle, seated gesture while talking (the patients)
ANIMS = ["sit_chair_idle_neutral_01", "sit_chair_gestic_thoughtful"]
# the attending stands next to the desk during the feedback: standing idle, standing gesture while talking
STANDING = ["m_idle_neutral_01", "m_gestic_talk_neutral_01"]


def get(path):
    for base in (RAW, MEDIA):
        req = urllib.request.Request(base + urllib.request.quote(path), headers={"User-Agent": "MedicalVR-hackathon/1.0"})
        with urllib.request.urlopen(req, timeout=120) as r:
            data = r.read()
        if not data.startswith(b"version https://git-lfs"):
            return data
    raise RuntimeError("could not download " + path)


def save(path, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 1000:
        return 0
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    data = get(path)
    with open(dest, "wb") as f:
        f.write(data)
    return len(data)


def main():
    total = 0
    for folder, prefix, opacity in MODELS.values():
        model = folder.split("/")[1]
        base = f"Assets/Avatars/{folder}/"
        total += save(base + f"Export/{model}_facial.fbx", os.path.join(OUT, model, f"{model}.fbx"))   # with face blend shapes
        maps = ["body_color", "body_normal", "head_color", "head_normal"]   # specular is not used
        maps += ["opacity_color"] if opacity else []
        maps += ["stetoskop_color", "stetoskop_normal"] if model.startswith("Medical") else []
        for m in maps:
            total += save(base + f"Textures/{prefix}_{m}.tga", os.path.join(OUT, model, "Textures", f"{prefix}_{m}.tga"))
        print("model", model)
    for sex in ("m", "f"):
        for a in ANIMS:
            total += save(f"Assets/Animations/all_animations_max_motextr_static/{sex}_{a}.max.fbx",
                          os.path.join(OUT, "Animations", f"{sex}_{a}.fbx"))
    for a in STANDING:
        total += save(f"Assets/Animations/all_animations_max_motextr_static/{a}.max.fbx", os.path.join(OUT, "Animations", a + ".fbx"))
    with open(os.path.join(OUT, "LICENSE.txt"), "w") as f:
        f.write("Microsoft Rocketbox avatars and animations, MIT licence: https://github.com/microsoft/Microsoft-Rocketbox\n")
    print(f"done, {total / 1e6:.1f} MB into {os.path.normpath(OUT)}")


if __name__ == "__main__":
    sys.exit(main())
