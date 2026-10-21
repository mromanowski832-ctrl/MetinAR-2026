#!/usr/bin/env python3
"""METINAR-018: deterministic procedural synthesis of the starter combat SFX.

Every sound is generated from scratch (oscillators, filtered noise, envelopes) with numpy and the
Python standard library. No samples, recordings, sound libraries or AI audio are used, so the output
is original MetinAR-Owned material. Same script + same seeds = bit-identical WAV files.

Output: 16-bit PCM mono WAV, 44.1 kHz (lossless source; Unity compresses on import per .meta, ADPCM on mobile).

Usage:
  python3 tools/audio/generate_sfx.py            # writes into client/Assets/MetinAR/Audio/SFX
  python3 tools/audio/generate_sfx.py --out DIR  # writes elsewhere (e.g. for a reproducibility check)
  python3 tools/audio/generate_sfx.py --check    # regenerates in a temp dir and compares with the repo bytes
"""
import argparse
import hashlib
import os
import sys
import tempfile
import wave

import numpy as np

SR = 44100
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_OUT = os.path.join(REPO, "client", "Assets", "MetinAR", "Audio", "SFX")


# ---------------------------------------------------------------- building blocks
def t_axis(dur):
    return np.arange(int(round(dur * SR))) / SR


def rng(seed):
    return np.random.default_rng(seed)


def env_ad(n, attack, decay_tau, sr=SR):
    """Linear attack (s) then exponential decay with time constant decay_tau (s)."""
    t = np.arange(n) / sr
    a = np.clip(t / max(attack, 1e-6), 0.0, 1.0)
    d = np.exp(-np.maximum(t - attack, 0.0) / decay_tau)
    return a * d


def fade_out(x, dur=0.01):
    n = min(len(x), int(dur * SR))
    if n > 0:
        x[-n:] *= np.linspace(1.0, 0.0, n)
    return x


def onepole_lp(x, cutoff):
    """One-pole low-pass; cutoff may be a scalar or a per-sample array (Hz)."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=np.float64), x.shape)
    a = np.exp(-2.0 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc = (1.0 - a[i]) * x[i] + a[i] * acc
        y[i] = acc
    return y


def onepole_hp(x, cutoff):
    return x - onepole_lp(x, cutoff)


def bandpass(x, lo, hi):
    return onepole_lp(onepole_hp(x, lo), hi)


def tone(t, freq, phase=0.0):
    """Sine with (possibly time-varying) frequency in Hz."""
    freq = np.broadcast_to(np.asarray(freq, dtype=np.float64), t.shape)
    return np.sin(2.0 * np.pi * np.cumsum(freq) / SR + phase)


def normalize(x, peak_dbfs):
    peak = np.max(np.abs(x))
    if peak <= 0:
        return x
    return x * (10.0 ** (peak_dbfs / 20.0) / peak)


def place(dst, src, start_s):
    i = int(start_s * SR)
    n = min(len(src), len(dst) - i)
    dst[i:i + n] += src[:n]
    return dst


# ---------------------------------------------------------------- sounds
def sword_swing():
    """Air whoosh: band-passed noise whose centre sweeps up then down, swelling amplitude."""
    dur = 0.38
    t = t_axis(dur)
    r = rng(1801)
    noise = r.standard_normal(len(t))
    p = t / dur
    centre = 500 + 2600 * np.sin(np.pi * np.clip(p * 1.1, 0, 1)) ** 2
    body = onepole_lp(onepole_hp(noise, centre * 0.45), centre)
    body = onepole_lp(body, centre * 1.2)
    amp = np.sin(np.pi * np.clip(p, 0, 1)) ** 1.6 * (0.6 + 0.4 * p)
    whistle = 0.05 * tone(t, 900 + 900 * p) * amp
    x = body * amp + whistle
    return fade_out(normalize(x, -3.0))


def sword_hit():
    """Blade striking chitin/flesh: sharp click, low thump, short metallic ring, gritty noise tail."""
    dur = 0.42
    t = t_axis(dur)
    r = rng(1802)
    n = len(t)
    click = r.standard_normal(n) * env_ad(n, 0.0005, 0.006)
    thump = tone(t, 140 * np.exp(-t / 0.05) + 60) * env_ad(n, 0.001, 0.06)
    ring = sum(a * tone(t, f) * env_ad(n, 0.001, d)
               for f, a, d in [(1870, 0.35, 0.09), (2730, 0.22, 0.06), (4110, 0.12, 0.04)])
    grit = bandpass(r.standard_normal(n), 900, 5000) * env_ad(n, 0.002, 0.05)
    x = 0.9 * click + 1.0 * thump + 0.5 * ring + 0.6 * grit
    return fade_out(normalize(x, -1.0))


def shield_block():
    """Sword on a wooden, metal-rimmed shield: dull wooden knock plus inharmonic rim clang."""
    dur = 0.5
    t = t_axis(dur)
    r = rng(1803)
    n = len(t)
    knock = onepole_lp(r.standard_normal(n), 1200) * env_ad(n, 0.001, 0.025)
    wood = sum(a * tone(t, f) * env_ad(n, 0.001, d)
               for f, a, d in [(210, 0.8, 0.07), (390, 0.5, 0.05), (620, 0.3, 0.035)])
    clang = sum(a * tone(t, f * (1 + 0.003 * np.sin(2 * np.pi * 7 * t))) * env_ad(n, 0.0008, d)
                for f, a, d in [(1240, 0.30, 0.16), (1933, 0.22, 0.12), (2871, 0.16, 0.09), (3605, 0.10, 0.07)])
    x = 1.0 * knock + 0.9 * wood + 0.55 * clang
    return fade_out(normalize(x, -1.0))


def chitter(r, n, rate, dur_click=0.004, f_lo=2500, f_hi=6500):
    """Train of tiny resonant clicks at roughly `rate` per second (spider mandibles)."""
    out = np.zeros(n)
    pos = 0.0
    while True:
        pos += r.exponential(1.0 / rate)
        i = int(pos * SR)
        if i >= n:
            break
        m = int(dur_click * SR)
        tt = np.arange(m) / SR
        f = r.uniform(f_lo, f_hi)
        c = np.sin(2 * np.pi * f * tt) * np.exp(-tt / (dur_click / 3))
        out[i:i + m] += c[:max(0, min(m, n - i))] * r.uniform(0.4, 1.0)
    return out


def spider_attack():
    """Aggressive hiss rising into a lunge, with rapid mandible chitter."""
    dur = 0.55
    t = t_axis(dur)
    r = rng(1804)
    n = len(t)
    p = t / dur
    hiss = bandpass(r.standard_normal(n), 2500, 7500)
    hiss_env = np.clip(p / 0.25, 0, 1) * np.exp(-np.maximum(p - 0.55, 0) / 0.18)
    growl = tone(t, 95 + 40 * p) * (0.5 + 0.5 * tone(t, 31)) * hiss_env * 0.35
    ch = chitter(r, n, 55) * hiss_env
    x = 0.8 * hiss * hiss_env + growl + 0.7 * ch
    return fade_out(normalize(x, -2.0))


def spider_hit():
    """Hurt squeal: short downward chirp over a wet crunch."""
    dur = 0.36
    t = t_axis(dur)
    r = rng(1805)
    n = len(t)
    crunch = onepole_lp(r.standard_normal(n), 2200) * env_ad(n, 0.001, 0.04)
    crunch += 0.6 * chitter(r, n, 180, f_lo=1200, f_hi=3000) * env_ad(n, 0.001, 0.05)
    f = 1900 * np.exp(-t / 0.12) + 500
    squeal = (tone(t, f) + 0.4 * tone(t, f * 1.51)) * env_ad(n, 0.01, 0.09)
    x = 0.9 * crunch + 0.55 * squeal
    return fade_out(normalize(x, -2.0))


def spider_death():
    """Death: long falling screech that breaks up, final crunch and twitching clicks."""
    dur = 1.2
    t = t_axis(dur)
    r = rng(1806)
    n = len(t)
    f = 1500 * np.exp(-t / 0.35) + 180
    trem = 0.6 + 0.4 * np.sin(2 * np.pi * (18 - 10 * t / dur) * t)
    screech = (tone(t, f) + 0.5 * tone(t, f * 1.33) + 0.25 * tone(t, f * 2.07)) * trem
    screech *= env_ad(n, 0.02, 0.38)
    rasp = bandpass(r.standard_normal(n), 800, 4000) * env_ad(n, 0.02, 0.3) * 0.5
    x = 0.6 * screech + rasp
    crunch = onepole_lp(r.standard_normal(int(0.2 * SR)), 1800) * env_ad(int(0.2 * SR), 0.001, 0.05)
    place(x, 0.9 * crunch, 0.62)
    tail = chitter(r, n, 14, f_lo=1500, f_hi=4000) * np.clip((t - 0.7) / 0.1, 0, 1) * np.exp(-np.maximum(t - 0.7, 0) / 0.2)
    x += 0.5 * tail
    return fade_out(normalize(x, -2.0), 0.05)


def spider_step(seed):
    """One leg tap: tiny dry tick on the ground with a soft low body; variants differ by seed."""
    dur = 0.12
    t = t_axis(dur)
    r = rng(seed)
    n = len(t)
    tick = bandpass(r.standard_normal(n), 1500, 6000) * env_ad(n, 0.0003, 0.006)
    second = np.zeros(n)
    place(second, 0.5 * bandpass(r.standard_normal(int(0.02 * SR)), 2000, 7000) * env_ad(int(0.02 * SR), 0.0003, 0.004),
          r.uniform(0.018, 0.04))
    body = tone(t, r.uniform(160, 220)) * env_ad(n, 0.001, 0.02) * 0.35
    x = tick + second + body
    return fade_out(normalize(x, -6.0))


def ui_click():
    """Short, soft UI click: two-partial blip with a filtered transient."""
    dur = 0.06
    t = t_axis(dur)
    r = rng(1810)
    n = len(t)
    blip = (tone(t, 1650) + 0.35 * tone(t, 3300)) * env_ad(n, 0.0005, 0.012)
    tr = onepole_lp(r.standard_normal(n), 5000) * env_ad(n, 0.0002, 0.002)
    x = blip + 0.4 * tr
    return fade_out(normalize(x, -6.0), 0.005)


SOUNDS = [
    ("Combat/SFX_Combat_SwordSwing_01.wav", sword_swing),
    ("Combat/SFX_Combat_SwordHit_01.wav", sword_hit),
    ("Combat/SFX_Combat_ShieldBlock_01.wav", shield_block),
    ("Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Attack_01.wav", spider_attack),
    ("Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Hit_01.wav", spider_hit),
    ("Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Death_01.wav", spider_death),
    ("Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_01.wav", lambda: spider_step(1807)),
    ("Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_02.wav", lambda: spider_step(1808)),
    ("Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_03.wav", lambda: spider_step(1809)),
    ("UI/SFX_UI_Click_01.wav", ui_click),
]


def write_wav(path, x):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    pcm = np.clip(np.round(x * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def generate(out_dir):
    result = []
    for rel, fn in SOUNDS:
        path = os.path.join(out_dir, rel)
        write_wav(path, fn())
        with open(path, "rb") as f:
            data = f.read()
        result.append((rel, len(data), hashlib.sha256(data).hexdigest()))
    return result


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--check", action="store_true", help="regenerate to a temp dir and compare with --out")
    a = ap.parse_args()
    if a.check:
        with tempfile.TemporaryDirectory() as tmp:
            bad = 0
            for rel, size, sha in generate(tmp):
                repo_file = os.path.join(a.out, rel)
                ok = os.path.exists(repo_file) and hashlib.sha256(open(repo_file, "rb").read()).hexdigest() == sha
                print(("OK   " if ok else "DIFF ") + rel)
                bad += 0 if ok else 1
            print("reproducible: %s (%d/%d)" % ("YES" if bad == 0 else "NO", len(SOUNDS) - bad, len(SOUNDS)))
            return 1 if bad else 0
    for rel, size, sha in generate(a.out):
        print("%-60s %7d B  sha256=%s" % (rel, size, sha))
    return 0


if __name__ == "__main__":
    sys.exit(main())
