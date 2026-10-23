"""METINAR-032 item icons: tiny procedural 2D painter (numpy + Pillow + scipy).

Everything is drawn from code: shapes are polygons/ellipses/strokes rasterised on a 1024² master canvas,
shaded with a bevel/height model (top-left key light), textured with seeded value noise and finally
downsampled to 256² with premultiplied-alpha Lanczos. No external images, fonts or brushes are used.
"""
import math

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

S = 1024  # master resolution
LIGHT = np.array([-0.48, -0.66, 0.58])
LIGHT = LIGHT / np.linalg.norm(LIGHT)
HALF = LIGHT + np.array([0.0, 0.0, 1.0])
HALF = HALF / np.linalg.norm(HALF)
OUTLINE = (0.075, 0.058, 0.05)
YY, XX = np.mgrid[0:S, 0:S].astype(np.float32)


# ------------------------------------------------------------------ geometry
def catmull(points, closed=True, steps=12):
    """Smooth polyline through points (Catmull-Rom)."""
    p = [np.array(q, dtype=float) for q in points]
    n = len(p)
    out = []
    rng = range(n) if closed else range(n - 1)
    for i in rng:
        p0 = p[(i - 1) % n] if closed or i > 0 else p[0]
        p1 = p[i]
        p2 = p[(i + 1) % n]
        p3 = p[(i + 2) % n] if closed or i + 2 < n else p[-1]
        for k in range(steps):
            t = k / steps
            t2, t3 = t * t, t * t * t
            q = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
            out.append((float(q[0]), float(q[1])))
    if not closed:
        out.append(tuple(map(float, p[-1])))
    return out


def bezier(p0, p1, p2, p3=None, steps=48):
    pts = []
    for k in range(steps + 1):
        t = k / steps
        if p3 is None:
            q = [(1 - t) ** 2 * a + 2 * (1 - t) * t * b + t * t * c for a, b, c in zip(p0, p1, p2)]
        else:
            q = [(1 - t) ** 3 * a + 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t * t * c + t ** 3 * d for a, b, c, d in zip(p0, p1, p2, p3)]
        pts.append(tuple(q))
    return pts


def xform(points, angle_deg=0.0, scale=1.0, offset=(512, 512), pivot=(0, 0)):
    a = math.radians(angle_deg)
    c, s = math.cos(a), math.sin(a)
    out = []
    for x, y in points:
        x, y = (x - pivot[0]) * scale, (y - pivot[1]) * scale
        out.append((offset[0] + x * c - y * s, offset[1] + x * s + y * c))
    return out


def _canvas():
    return Image.new("L", (S, S), 0)


def m_poly(points):
    im = _canvas()
    ImageDraw.Draw(im).polygon([tuple(map(float, q)) for q in points], fill=255)
    return np.asarray(im, dtype=np.float32) / 255.0


def m_ellipse(cx, cy, rx, ry=None):
    ry = rx if ry is None else ry
    return (((XX - cx) / rx) ** 2 + ((YY - cy) / ry) ** 2 <= 1.0).astype(np.float32)


def m_rect(x0, y0, x1, y1, r=0):
    im = _canvas()
    ImageDraw.Draw(im).rounded_rectangle([x0, y0, x1, y1], radius=r, fill=255)
    return np.asarray(im, dtype=np.float32) / 255.0


def m_stroke(points, width, caps=True):
    im = _canvas()
    d = ImageDraw.Draw(im)
    pts = [tuple(map(float, q)) for q in points]
    d.line(pts, fill=255, width=int(round(width)), joint="curve")
    if caps:
        r = width / 2.0
        for x, y in (pts[0], pts[-1]):
            d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return np.asarray(im, dtype=np.float32) / 255.0


def m_and(a, b):
    return np.minimum(a, b)


def m_or(*ms):
    return np.maximum.reduce(ms)


def m_sub(a, b):
    return np.clip(a - b, 0, 1)


# ------------------------------------------------------------------ textures
def noise(seed, scale, octaves=4, persistence=0.55):
    """Seeded value-noise fBm in [0,1], S×S."""
    rng = np.random.default_rng(seed)
    acc = np.zeros((S, S), np.float32)
    amp, total = 1.0, 0.0
    cells = max(2, int(S / scale))
    for _ in range(octaves):
        g = rng.random((cells + 1, cells + 1)).astype(np.float32)
        im = Image.fromarray((g * 255).astype(np.uint8)).resize((S, S), Image.BICUBIC)
        acc += amp * np.asarray(im, np.float32) / 255.0
        total += amp
        amp *= persistence
        cells *= 2
    return acc / total


def grain(seed, angle_deg, freq, warp=18.0):
    """Wood-like stripes along angle (0..1)."""
    a = math.radians(angle_deg)
    u = XX * math.cos(a) + YY * math.sin(a)
    n = noise(seed, 160, 3)
    v = np.sin((u + n * warp * 6) * freq * 2 * math.pi / S)
    return 0.5 + 0.5 * v


def col(c):
    return np.asarray(c, np.float32).reshape(1, 1, 3)


def lerp_col(a, b, t):
    t = np.asarray(t, np.float32)[..., None]
    return col(a) * (1 - t) + col(b) * t


# ------------------------------------------------------------------ shading
def shade(mask, color, bevel=26.0, height=1.0, profile="round", ambient=0.42, diffuse=0.78, spec=0.25,
          shininess=24.0, edge=3.0, edge_dark=0.45, vgrad=0.18, emissive=None, extra_height=None):
    """Return (rgb, alpha) for a shaded shape. color: rgb tuple or S×S×3 array."""
    dist = ndimage.distance_transform_edt(mask > 0.5).astype(np.float32)
    t = np.clip(dist / max(bevel, 1e-3), 0, 1)
    if profile == "round":
        h = np.sqrt(1 - (1 - t) ** 2)
    elif profile == "flat":
        h = t
    else:  # "dome": full hemisphere over the whole shape
        h = t ** 0.5
    h = h * bevel * height
    if extra_height is not None:
        h = h + extra_height
    gy, gx = np.gradient(ndimage.gaussian_filter(h, 1.2))
    n = np.stack([-gx, -gy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    ndl = np.clip((n * LIGHT).sum(-1), 0, 1)
    ndh = np.clip((n * HALF).sum(-1), 0, 1)
    base = col(color) if not isinstance(color, np.ndarray) or color.ndim == 1 else color
    ys = np.where(mask > 0.5)[0]
    if len(ys):
        y0, y1 = ys.min(), ys.max()
        vg = 1.0 + vgrad * (0.5 - (YY - y0) / max(1, y1 - y0))
    else:
        vg = 1.0
    lit = (ambient + diffuse * ndl) * vg
    rgb = base * lit[..., None] + spec * (ndh ** shininess)[..., None]
    if edge > 0:
        e = np.clip(1 - dist / edge, 0, 1)
        rgb = rgb * (1 - edge_dark * e)[..., None]
    if emissive is not None:
        rgb = rgb + emissive
    return np.clip(rgb, 0, 1), mask


class Icon:
    def __init__(self):
        self.rgb = np.zeros((S, S, 3), np.float32)
        self.a = np.zeros((S, S), np.float32)
        self.glow = np.zeros((S, S, 3), np.float32)

    def put(self, rgb_a, opacity=1.0):
        rgb, a = rgb_a
        a = a * opacity
        self.rgb = rgb * a[..., None] + self.rgb * (1 - a[..., None])
        self.a = a + self.a * (1 - a)

    def paint(self, mask, color, opacity=1.0):
        """Flat colour overlay (decals, stitches) restricted to mask (premultiplied over)."""
        c = col(color) if not isinstance(color, np.ndarray) else color
        a = mask * opacity
        self.rgb = c * a[..., None] + self.rgb * (1 - a[..., None])
        self.a = a + self.a * (1 - a)

    def mul(self, mask, factor):
        f = 1 - (1 - factor) * mask
        self.rgb = self.rgb * f[..., None]

    def add_glow(self, mask, color, radius=14, strength=1.0):
        g = ndimage.gaussian_filter(mask, radius)
        self.glow += col(color) * (g * strength)[..., None]

    def finish(self, outline_px=9.0, shadow=True):
        """Outline + soft drop shadow + glow, returns 1024² RGBA uint8 (straight alpha)."""
        solid = self.a > 0.02
        out_d = ndimage.distance_transform_edt(~solid).astype(np.float32)
        ring = np.clip(outline_px + 1 - out_d, 0, 1)
        rgb = self.rgb + col(OUTLINE) * (ring * (1 - self.a))[..., None]
        a = self.a + ring * (1 - self.a)
        # glow (emissive bleeding over edges)
        gl = self.glow
        ga = np.clip(gl.max(-1), 0, 1)
        rgb = rgb + gl
        a = np.maximum(a, ga * 0.85)
        if shadow:
            sh = ndimage.gaussian_filter(np.roll(np.roll(a, 22, 0), 12, 1), 16) * 0.42
            a = a + sh * (1 - a)
        straight = np.where(a[..., None] > 1e-4, rgb / np.maximum(a[..., None], 1e-4), 0)
        out = np.dstack([np.clip(straight, 0, 1), np.clip(a, 0, 1)])
        return (out * 255 + 0.5).astype(np.uint8)


def downsample(rgba_u8, size=256):
    im = Image.fromarray(rgba_u8, "RGBA").convert("RGBa").resize((size, size), Image.LANCZOS).convert("RGBA")
    return im
