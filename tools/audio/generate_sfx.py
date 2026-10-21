#!/usr/bin/env python3
"""METINAR-018: deterministic procedural synthesis of the starter combat SFX.

Every sound is generated from scratch (oscillators, filtered noise, envelopes) with numpy and the
Python standard library. No samples, recordings, sound libraries or AI audio are used, so the output
is original MetinAR-Owned material. Same script + same seeds = bit-identical WAV files.

Output: 16-bit PCM mono WAV, 44.1 kHz (lossless source; Unity compresses on import per .meta, ADPCM on mobile).
Every clip is a whole number of 64-sample blocks: Unity's ADPCM import pads a clip up to the next multiple of 64
samples (verified on LuzMen: 88 200 -> 88 256 samples), which would push clips past their length budget and put
~1.3 ms of silence into a loop seam. Lengths are therefore rounded DOWN to a multiple of ADPCM_BLOCK_FRAMES.

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
ADPCM_BLOCK_FRAMES = 64      # Unity ADPCM block; every generated clip length must be a multiple of this
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_OUT = os.path.join(REPO, "client", "Assets", "MetinAR", "Audio", "SFX")


# ---------------------------------------------------------------- building blocks
def n_frames(dur):
    """Sample count for a nominal duration, rounded down to whole ADPCM blocks (never longer than `dur`)."""
    return (int(round(dur * SR)) // ADPCM_BLOCK_FRAMES) * ADPCM_BLOCK_FRAMES


def t_axis(dur):
    return np.arange(n_frames(dur)) / SR


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


# ---------------------------------------------------------------- Aura (METINAR-FPP-001 / #31)
# Prototype timing in FppCombatTimeline (PR #32): cast 1.15 s, burst at 0.65 s, Aura active 6 s on the blade.
# Start = cast (two-hand grip, energy gathering, burst at 0.65 s); Loop = sustained blade hum while active
# (played looped by an AudioSource); Stop = discharge when the Aura expires.
AURA_LOOP_FRAMES = 88192                 # 1378 x 64 (1.99982 s): a whole number of ADPCM blocks -> no import padding
AURA_LOOP_SECONDS = AURA_LOOP_FRAMES / SR


def loop_hz(f):
    """Nearest frequency with an integer number of cycles in the loop length (re-tuned, not cut: seam stays exact)."""
    return round(f * AURA_LOOP_SECONDS) / AURA_LOOP_SECONDS


AURA_BASE_HZ = loop_hz(110.0)            # 220 cycles per loop (110.0102 Hz)
AURA_SHIMMER_HZ = (loop_hz(1320.0), loop_hz(1981.5), loop_hz(2643.0))   # 2640 / 3963 / 5286 cycles per loop


def periodic_noise(r, n, lo, hi):
    """Band-limited noise that is exactly periodic over n samples (built in the frequency domain)."""
    spec = np.zeros(n // 2 + 1, dtype=np.complex128)
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    band = (freqs >= lo) & (freqs <= hi)
    spec[band] = r.standard_normal(band.sum()) + 1j * r.standard_normal(band.sum())
    x = np.fft.irfft(spec, n)
    return x / np.max(np.abs(x))


def aura_layers(t, r, n, periods=1):
    """Steady Aura timbre: low hum, gold-ish harmonics with slow shimmer, airy blue noise band.
    `periods` tiles the (one-period) noise so the same timbre can be rendered over several loop lengths."""
    loop = AURA_LOOP_SECONDS
    hum = tone(t, AURA_BASE_HZ) + 0.45 * tone(t, AURA_BASE_HZ * 2) + 0.2 * tone(t, AURA_BASE_HZ * 3)
    # shimmer partials: integer cycles per loop (loop_hz), slow AM at k/loop Hz -> periodic
    s1, s2, s3 = AURA_SHIMMER_HZ
    shimmer = (0.30 * tone(t, s1) * (0.6 + 0.4 * np.sin(2 * np.pi * (1 / loop) * t)) +
               0.22 * tone(t, s2) * (0.6 + 0.4 * np.sin(2 * np.pi * (2 / loop) * t + 1.3)) +
               0.15 * tone(t, s3) * (0.6 + 0.4 * np.sin(2 * np.pi * (3 / loop) * t + 2.1)))
    air = np.tile(periodic_noise(r, n, 3000, 9000), periods)[:len(t)] * (0.7 + 0.3 * np.sin(2 * np.pi * (4 / loop) * t))
    pulse = 0.85 + 0.15 * np.sin(2 * np.pi * (6 / loop) * t)   # 3 Hz "breathing", periodic in the loop
    return (0.55 * hum + shimmer + 0.12 * air) * pulse


def aura_loop():
    """Seamless loop: all components periodic in AURA_LOOP_FRAMES, no fades -> sample-continuous seam."""
    n = AURA_LOOP_FRAMES
    t = np.arange(n) / SR
    r = rng(1811)
    x = aura_layers(t, r, n)
    # sustained bed under combat for ~6 s: dense signal, so a lower peak than one-shot SFX (RMS ~ -21 dBFS)
    return normalize(x, -12.0)


def aura_start():
    """Cast: rising energy for 0.65 s into a bright burst, settling towards the loop timbre by 1.15 s."""
    dur = 1.2
    t = t_axis(dur)
    n = len(t)
    r = rng(1812)
    burst_t = 0.65
    rise = np.clip(t / burst_t, 0, 1) ** 2
    riser_f = 300 + 1500 * np.clip(t / burst_t, 0, 1) ** 1.5
    riser = (tone(t, riser_f) + 0.5 * tone(t, riser_f * 1.5)) * rise * (t < burst_t + 0.05)
    swell = bandpass(r.standard_normal(n), 1500, 8000) * rise * (t < burst_t + 0.02)
    b = np.maximum(t - burst_t, 0)
    burst_env = (t >= burst_t) * np.exp(-b / 0.12)
    burst = (bandpass(r.standard_normal(n), 600, 9000) + 0.8 * tone(t, 880) + 0.5 * tone(t, 1320) + 0.6 * tone(t, 65 + 40 * np.exp(-b / 0.05))) * burst_env
    bed = aura_layers(t, rng(1811), n) * np.clip((t - burst_t) / 0.3, 0, 1) * 0.5
    x = 0.45 * riser + 0.35 * swell + 1.0 * burst + bed
    return fade_out(normalize(x, -1.0), 0.04)


def aura_stop():
    """Expiry: the hum drops in pitch and fades with a soft crackling discharge."""
    dur = 0.6
    t = t_axis(dur)
    n = len(t)
    r = rng(1813)
    drop = np.exp(-t / 0.25)
    hum = (tone(t, AURA_BASE_HZ * (1 - 0.35 * (1 - drop))) + 0.4 * tone(t, AURA_SHIMMER_HZ[0] * (1 - 0.4 * (1 - drop)))) * drop
    crackle = chitter(r, n, 40, dur_click=0.003, f_lo=3000, f_hi=8000) * np.exp(-t / 0.2)
    air = bandpass(r.standard_normal(n), 3000, 9000) * np.exp(-t / 0.15) * 0.3
    x = 0.8 * hum + 0.5 * crackle + air
    x[: int(0.005 * SR)] *= np.linspace(0, 1, int(0.005 * SR))
    return fade_out(normalize(x, -4.0), 0.05)


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
    ("Combat/SFX_Combat_AuraStart_01.wav", aura_start),
    ("Combat/SFX_Combat_AuraLoop_01.wav", aura_loop),
    ("Combat/SFX_Combat_AuraStop_01.wav", aura_stop),
]


def write_wav(path, x):
    if len(x) % ADPCM_BLOCK_FRAMES != 0:
        raise ValueError("%s: %d samples is not a multiple of %d" % (path, len(x), ADPCM_BLOCK_FRAMES))
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


def check_aura_periodic():
    """The Aura timbre rendered over two loop lengths must repeat exactly (to float precision) and its first period
    must equal the committed loop render: the seam is then the same as any other sample transition."""
    n = AURA_LOOP_FRAMES
    one = aura_layers(np.arange(n) / SR, rng(1811), n)
    two = aura_layers(np.arange(2 * n) / SR, rng(1811), n, periods=2)
    return bool(np.allclose(two[:n], one, atol=1e-9) and np.allclose(two[:n], two[n:], atol=1e-6))


def check_block_alignment(out_dir):
    """Committed WAVs whose sample count is not a whole number of ADPCM blocks (empty list = all aligned)."""
    bad = []
    for rel, _ in SOUNDS:
        path = os.path.join(out_dir, rel)
        if os.path.exists(path):
            with wave.open(path, "rb") as w:
                if w.getnframes() % ADPCM_BLOCK_FRAMES != 0:
                    bad.append("%s (%d)" % (rel, w.getnframes()))
    return bad


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
            periodic = check_aura_periodic()
            print("aura loop periodic: %s" % ("YES" if periodic else "NO"))
            aligned = check_block_alignment(a.out)
            print("lengths multiple of %d samples: %s" % (ADPCM_BLOCK_FRAMES, "YES" if not aligned else "NO " + ", ".join(aligned)))
            return 1 if bad or not periodic or aligned else 0
    for rel, size, sha in generate(a.out):
        print("%-60s %7d B  sha256=%s" % (rel, size, sha))
    return 0


if __name__ == "__main__":
    sys.exit(main())
