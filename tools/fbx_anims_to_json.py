"""Reads avatar's Ara clip FBX files (Hannah_anims.zip) and writes the bone curves as JSON for Unity.

Why: Unity 6000.6.9f1 imports these "skeleton only" clip files with zero takes, although each holds one
AnimStack with valid curves. AvatarImport.cs turns this JSON into AnimationClips.
Usage: python3 ui/tools/fbx_anims_to_json.py <dir with Hannah@*.fbx> <out.json> clip1 clip2 ...
Conversion = what Unity's FBX importer does for this file (Y up, cm, no pre-rotation, Euler XYZ):
  rotation q = Rz*Ry*Rx, then right- to left-handed: (x, -y, -z, w); translation (-x, y, z) * 0.01.
"""
import json, math, os, sys
sys.path.insert(0, os.path.dirname(__file__))
from fbxparse import load, child, props70

KTIME = 46186158000.0   # FBX ticks per second

def quat_xyz(rx, ry, rz):
    def axis(a, ax):
        h = math.radians(a) / 2; s = math.sin(h)
        return (s if ax == 0 else 0, s if ax == 1 else 0, s if ax == 2 else 0, math.cos(h))
    def mul(a, b):
        ax, ay, az, aw = a; bx, by, bz, bw = b
        return (aw*bx + ax*bw + ay*bz - az*by, aw*by - ax*bz + ay*bw + az*bx, aw*bz + ax*by - ay*bx + az*bw, aw*bw - ax*bx - ay*by - az*bz)
    q = mul(axis(rz, 2), mul(axis(ry, 1), axis(rx, 0)))
    return [q[0], -q[1], -q[2], q[3]]

def clip(path):
    ver, top = load(path)
    obj = [t for t in top if t[0] == "Objects"][0][2]
    con = [t for t in top if t[0] == "Connections"][0][2]
    byid = {o[1][0]: o for o in obj}
    name = lambda o: o[1][1].split("\x00")[0]
    parent = {}
    curves_of = {}   # curvenode id -> {axis: (times, values)}
    node_of = {}     # curvenode id -> (model id, property)
    for c in con:
        kind, a, b = c[1][0], c[1][1], c[1][2]
        oa, ob = byid.get(a), byid.get(b)
        if oa is None or ob is None: continue
        if kind == "OO" and oa[0] == "Model" and ob[0] == "Model": parent[a] = b
        if kind == "OP" and oa[0] == "AnimationCurveNode" and ob[0] == "Model": node_of[a] = (b, c[1][3])
        if kind == "OP" and oa[0] == "AnimationCurve" and ob[0] == "AnimationCurveNode":
            t = child(oa, "KeyTime")[0][1][0]; v = child(oa, "KeyValueFloat")[0][1][0]
            curves_of.setdefault(b, {})[c[1][3]] = ([x / KTIME for x in t], v)
    def upath(mid):   # Unity path below the prefab root (the clip's own root node is dropped)
        parts = []
        while mid in parent and byid[mid][0] == "Model":
            parts.append(name(byid[mid])); mid = parent[mid]
        return "/".join(reversed(parts))
    stack = [o for o in obj if o[0] == "AnimationStack"][0]
    length = props70(stack)["LocalStop"][0] / KTIME
    bones = {}
    for cn, (mid, prop) in node_of.items():
        ax = curves_of.get(cn, {})
        if not ax: continue
        pr = props70(byid[mid])
        key = "Lcl Rotation" if prop == "Lcl Rotation" else prop
        base = pr.get(key, [0, 0, 0])
        times = next(iter(ax.values()))[0]
        def at(axis, i, k):
            return ax[axis][1][i] if axis in ax and i < len(ax[axis][1]) else base[k]
        b = bones.setdefault(upath(mid), {"t": [round(x, 5) for x in times]})
        if prop == "Lcl Rotation":
            qs = []
            for i in range(len(times)):
                q = quat_xyz(at("d|X", i, 0), at("d|Y", i, 1), at("d|Z", i, 2))
                if qs and sum(a * b for a, b in zip(q, qs[-1])) < 0: q = [-c for c in q]   # no flips between keys
                qs.append(q)
            b["q"] = [round(c, 6) for q in qs for c in q]
        elif prop == "Lcl Translation":
            b["p"] = [round(v, 6) for i in range(len(times)) for v in (-at("d|X", i, 0) * 0.01, at("d|Y", i, 1) * 0.01, at("d|Z", i, 2) * 0.01)]
    return {"length": length, "bones": [dict(path=k, **v) for k, v in sorted(bones.items())]}

def bind(path):
    """Bind pose of the model, same conversion, to compare with what Unity imported."""
    ver, top = load(path)
    obj = [t for t in top if t[0] == "Objects"][0][2]
    out = {}
    for o in obj:
        if o[0] == "Model":
            pr = props70(o); r = pr.get("Lcl Rotation", [0, 0, 0])
            out[o[1][1].split("\x00")[0]] = quat_xyz(*r)
    return out

if __name__ == "__main__":
    src, out, names = sys.argv[1], sys.argv[2], sys.argv[3:]
    # JsonUtility-friendly: lists of objects, flat number arrays (q: x,y,z,w per key; p: x,y,z per key)
    b = bind(os.path.join(src, "Hannah.fbx"))
    data = {"clips": [dict(name=n, **clip(os.path.join(src, f"Hannah@{n}.fbx"))) for n in names],
            "bindCheck": [dict(name=k, q=b[k]) for k in ("root", "spine05", "upperarm01.L")]}
    json.dump(data, open(out, "w"))
    for c in data["clips"]: print(c["name"], round(c["length"], 2), "s,", len(c["bones"]), "bones,", sum("p" in x for x in c["bones"]), "with position")
