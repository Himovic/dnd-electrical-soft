#!/usr/bin/env python3
"""Renders a GLB exported by the suite to PNG with a small z-buffer rasteriser (flat shading, edges).

Usage: python3 scripts/render-glb.py model.glb out.png [--hide Door,wall,Roof,Bottom]
Requires: pip install trimesh pillow numpy
"""
import sys
import numpy as np
import trimesh
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
hide = sys.argv[sys.argv.index("--hide") + 1].split(",") if "--hide" in sys.argv else []
W, H = 1400, 1600
sc = trimesh.load(src)
tris, cols = [], []
for node in sc.graph.nodes_geometry:
    if any(h in node for h in hide):
        continue
    T, gname = sc.graph[node]
    g = sc.geometry[gname].copy()
    g.apply_transform(T)
    c = np.array(g.visual.material.baseColorFactor, dtype=float)
    c = c / 255 if c.max() > 1 else c
    for f in g.faces:
        tris.append(g.vertices[f])
        cols.append(np.where(c[:3] <= 0.0031308, 12.92 * c[:3], 1.055 * np.power(c[:3], 1 / 2.4) - 0.055))  # linear -> sRGB
tris = np.array(tris)  # metres, Y up
cols = np.array(cols)
# camera: view from front-left-above
az, el = np.radians(-35), np.radians(18)
fwd = np.array([np.cos(el) * np.sin(az), -np.sin(el), -np.cos(el) * np.cos(az)])  # looking direction
right = np.cross(fwd, [0, 1, 0]); right /= np.linalg.norm(right)
up = np.cross(right, fwd)
P = tris.reshape(-1, 3)
cam = np.stack([P @ right, P @ up, P @ fwd], axis=1)
lo, hi = cam[:, :2].min(0), cam[:, :2].max(0)
scale = 0.9 * min(W / (hi[0] - lo[0]), H / (hi[1] - lo[1]))
sx = (cam[:, 0] - (lo[0] + hi[0]) / 2) * scale + W / 2
sy = H / 2 - (cam[:, 1] - (lo[1] + hi[1]) / 2) * scale
S = np.stack([sx, sy, cam[:, 2]], axis=1).reshape(-1, 3, 3)
zbuf = np.full((H, W), np.inf)
img = np.ones((H, W, 3))
light = -fwd * 0.6 + up * 0.6 + right * 0.3
light /= np.linalg.norm(light)
for t, (a, b, c), col in zip(tris, S, cols):
    n = np.cross(t[1] - t[0], t[2] - t[0])
    if np.linalg.norm(n) < 1e-12:
        continue
    n /= np.linalg.norm(n)
    shade = col * (0.45 + 0.55 * abs(n @ light))
    x0, x1 = int(max(0, np.floor(min(a[0], b[0], c[0])))), int(min(W - 1, np.ceil(max(a[0], b[0], c[0]))))
    y0, y1 = int(max(0, np.floor(min(a[1], b[1], c[1])))), int(min(H - 1, np.ceil(max(a[1], b[1], c[1]))))
    if x1 < x0 or y1 < y0:
        continue
    xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
    d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
    if abs(d) < 1e-9:
        continue
    l1 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / d
    l2 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / d
    l3 = 1 - l1 - l2
    inside = (l1 >= 0) & (l2 >= 0) & (l3 >= 0)
    z = l1 * a[2] + l2 * b[2] + l3 * c[2]
    region = zbuf[y0:y1 + 1, x0:x1 + 1]
    m = inside & (z < region)
    region[m] = z[m]
    img[y0:y1 + 1, x0:x1 + 1][m] = shade
img_u8 = (np.clip(img, 0, 1) * 255).astype(np.uint8)
# crop white margins
mask = (img_u8 < 250).any(axis=2)
ys, xs = np.where(mask)
img_u8 = img_u8[max(0, ys.min() - 20):ys.max() + 20, max(0, xs.min() - 20):xs.max() + 20]
Image.fromarray(img_u8).save(out)
print(f"{out}: {len(tris)} triangles")
