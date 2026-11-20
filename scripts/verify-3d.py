#!/usr/bin/env python3
"""Independent verification of 3D exports (glTF 2.0 / GLB / STL) with trimesh.

Usage: python3 scripts/verify-3d.py <file.glb|.gltf|.stl> [--expect-extents-mm W,D,H]
Requires: pip install trimesh
Official Khronos validation: node scripts/verify-gltf.mjs <file.glb>  (npm install gltf-validator)
"""
import json, struct, sys

import trimesh

failures = 0
args = sys.argv[1:]
expect = None
if "--expect-extents-mm" in args:
    i = args.index("--expect-extents-mm")
    expect = [float(v) for v in args[i + 1].split(",")]
    del args[i:i + 2]
for path in args:
    obj = trimesh.load(path)
    if path.lower().endswith(".stl"):
        ext = [float(v) for v in obj.extents]  # mm, Z up: W, D, H
        print(f"STL  {path}: {len(obj.faces)} triangles, extents {[round(e, 2) for e in ext]} mm")
    else:
        e = [float(v) for v in obj.extents]  # metres, Y up: W, H, D
        ext = [e[0] * 1000, e[2] * 1000, e[1] * 1000]
        print(f"glTF {path}: {len(obj.geometry)} meshes, extents W×D×H {[round(v, 2) for v in ext]} mm")
        if path.lower().endswith(".glb"):
            data = open(path, "rb").read()
            assert data[:4] == b"glTF", "bad GLB magic"
            n = struct.unpack("<I", data[12:16])[0]
            doc = json.loads(data[20:20 + n])
            ids = sum(1 for nd in doc["nodes"] if nd.get("extras", {}).get("dndObjectId"))
            print(f"     {ids} nodes carry an engineering object id")
    if expect and any(abs(a - b) > 0.5 for a, b in zip(ext, expect)):
        print(f"     FAIL: expected extents {expect}")
        failures += 1
sys.exit(1 if failures else 0)
