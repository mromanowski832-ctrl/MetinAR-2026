"""METINAR-007 reference for the Unity hand-landmark proof (independent of Unity / Sentis).

Runs the same two MediaPipe hand models with onnxruntime on one photo, using the same pre/post-processing rules as
client/Assets/MetinAR/Scripts/HandTracking (letterbox 192 palm detector, ROI rotation/shift/scale, 224 landmark crop),
and writes a JSON file the Unity proof (MetinAR.Editor.HandTrackingInferenceProof) can compare against.

Usage: python tools/hand_tracking_reference.py <image> <models_dir> <out.json>
Requires: numpy, pillow, onnxruntime (dev machine only; not shipped).
"""
import json
import math
import sys

import numpy as np
import onnxruntime as ort
from PIL import Image


def anchors():
    out = []
    for grid, per in ((24, 2), (12, 6)):
        for y in range(grid):
            for x in range(grid):
                for _ in range(per):
                    out.append(((x + 0.5) / grid, (y + 0.5) / grid))
    return np.array(out, np.float32)


def sample(im, m, n):
    h, w, _ = im.shape
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32) + 0.5
    x = m[0, 0] * xs + m[0, 1] * ys + m[0, 2] - 0.5
    y = m[1, 0] * xs + m[1, 1] * ys + m[1, 2] - 0.5
    x0 = np.floor(x).astype(int)
    y0 = np.floor(y).astype(int)
    fx = x - x0
    fy = y - y0

    def px(xx, yy):
        valid = (xx >= 0) & (xx < w) & (yy >= 0) & (yy < h)
        v = im[np.clip(yy, 0, h - 1), np.clip(xx, 0, w - 1)].copy()
        v[~valid] = 0
        return v

    return (px(x0, y0) * ((1 - fx) * (1 - fy))[..., None] + px(x0 + 1, y0) * (fx * (1 - fy))[..., None]
            + px(x0, y0 + 1) * ((1 - fx) * fy)[..., None] + px(x0 + 1, y0 + 1) * (fx * fy)[..., None])


def main(image_path, models, out_path):
    im = np.asarray(Image.open(image_path).convert("RGB")).astype(np.float32) / 255.0
    h, w, _ = im.shape
    det = ort.InferenceSession(models + "/hand_detector.onnx")
    lm = ort.InferenceSession(models + "/hand_landmarks_detector.onnx")
    size = max(w, h)
    s = size / 192.0
    m = np.array([[s, 0, (w - size) / 2], [0, s, (h - size) / 2]], np.float32)
    boxes, logits = det.run(None, {det.get_inputs()[0].name: sample(im, m, 192)[None].astype(np.float32)})
    scores = 1 / (1 + np.exp(-np.clip(logits[0, :, 0], -100, 100)))
    i = int(np.argmax(scores))
    b = boxes[0, i]
    a = anchors()[i] * 192
    cx, cy = a[0] + b[0], a[1] + b[1]
    kps = [(a[0] + b[4 + 2 * k], a[1] + b[5 + 2 * k]) for k in range(7)]

    def to_img(p):
        return np.array([m[0, 0] * p[0] + m[0, 2], m[1, 1] * p[1] + m[1, 2]])

    c = to_img((cx, cy))
    k0 = to_img(kps[0])
    k2 = to_img(kps[2])
    bs = max(b[2], b[3]) * s
    rot = math.pi / 2 - math.atan2(-(k2[1] - k0[1]), k2[0] - k0[0])
    rot = (rot + math.pi) % (2 * math.pi) - math.pi
    shift = np.array([0.5 * bs * math.sin(rot), -0.5 * bs * math.cos(rot)])
    c2 = c + shift
    roi = bs * 2.6
    sc = roi / 224.0
    cr, sr = math.cos(rot), math.sin(rot)
    m2 = np.array([[sc * cr, -sc * sr, 0], [sc * sr, sc * cr, 0]], np.float32)
    m2[:, 2] = c2 - m2[:, :2] @ np.array([112, 112], np.float32)
    out = lm.run(None, {lm.get_inputs()[0].name: sample(im, m2, 224)[None].astype(np.float32)})
    pts = out[0].reshape(21, 3)
    img_pts = [[float(m2[0, 0] * p[0] + m2[0, 1] * p[1] + m2[0, 2]), float(m2[1, 0] * p[0] + m2[1, 1] * p[1] + m2[1, 2])] for p in pts]
    result = {
        "image": {"width": w, "height": h},
        "palmScore": float(scores[i]),
        "presence": float(np.ravel(out[1])[0]),
        "handedness": float(np.ravel(out[2])[0]),
        "roi": {"centerX": float(c2[0]), "centerY": float(c2[1]), "size": float(roi), "rotationDeg": math.degrees(rot)},
        "landmarksPx": img_pts,
    }
    with open(out_path, "w") as f:
        json.dump(result, f, indent=2)
    print(json.dumps({k: result[k] for k in ("palmScore", "presence", "handedness", "roi")}))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3])
