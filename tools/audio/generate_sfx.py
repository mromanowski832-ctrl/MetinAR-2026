#!/usr/bin/env python3
"""METINAR-018 / METINAR-033: deterministic procedural synthesis of the starter combat and starter-kit SFX.

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


# ---------------------------------------------------------------- Aura (METINAR-FPP-001 / #31)
# Prototype timing in FppCombatTimeline (PR #32): cast 1.15 s, burst at 0.65 s, Aura active 6 s on the blade.
# Start = cast (two-hand grip, energy gathering, burst at 0.65 s); Loop = sustained blade hum while active
# (played looped by an AudioSource); Stop = discharge when the Aura expires.
AURA_LOOP_SECONDS = 2.0      # 88 200 samples; every partial / modulation is periodic in exactly this length
AURA_BASE_HZ = 110.0         # multiple of 1 / AURA_LOOP_SECONDS -> integer cycles per loop


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
    # shimmer partials: frequencies are multiples of 0.5 Hz, slow AM at 1/loop and 2/loop Hz -> periodic
    shimmer = (0.30 * tone(t, 1320.0) * (0.6 + 0.4 * np.sin(2 * np.pi * (1 / loop) * t)) +
               0.22 * tone(t, 1981.5) * (0.6 + 0.4 * np.sin(2 * np.pi * (2 / loop) * t + 1.3)) +
               0.15 * tone(t, 2643.0) * (0.6 + 0.4 * np.sin(2 * np.pi * (3 / loop) * t + 2.1)))
    air = np.tile(periodic_noise(r, n, 3000, 9000), periods)[:len(t)] * (0.7 + 0.3 * np.sin(2 * np.pi * (4 / loop) * t))
    pulse = 0.85 + 0.15 * np.sin(2 * np.pi * (6 / loop) * t)   # 3 Hz "breathing", periodic in the loop
    return (0.55 * hum + shimmer + 0.12 * air) * pulse


def aura_loop():
    """Seamless loop: all components periodic in AURA_LOOP_SECONDS, no fades -> sample-continuous seam."""
    n = int(round(AURA_LOOP_SECONDS * SR))
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
    hum = (tone(t, AURA_BASE_HZ * (1 - 0.35 * (1 - drop))) + 0.4 * tone(t, 1320 * (1 - 0.4 * (1 - drop)))) * drop
    crackle = chitter(r, n, 40, dur_click=0.003, f_lo=3000, f_hi=8000) * np.exp(-t / 0.2)
    air = bandpass(r.standard_normal(n), 3000, 9000) * np.exp(-t / 0.15) * 0.3
    x = 0.8 * hum + 0.5 * crackle + air
    x[: int(0.005 * SR)] *= np.linspace(0, 1, int(0.005 * SR))
    return fade_out(normalize(x, -4.0), 0.05)


# ---------------------------------------------------------------- starter kit (METINAR-033 / #60), seeds 3301..3314
# Equipment, consumables, loot, talers, shop, level up, poison tick. Spec: docs/design (PR #44), names from
# COMBAT_PHYSICS.md section 5 where given. All one-shots (no loops), mono, <= 1.5 s.
def metal_hit(r, n, base, decay, bright=1.0):
    """Inharmonic metal partials (plate / buckle / ring) with a tiny noise transient."""
    t = np.arange(n) / SR
    ratios = [(1.0, 1.0), (1.48, 0.7), (2.27, 0.5), (3.09, 0.35 * bright), (4.12, 0.2 * bright)]
    x = sum(a * tone(t, base * k * r.uniform(0.995, 1.005), r.uniform(0, 2 * np.pi)) * env_ad(n, 0.0006, decay / (1 + 0.5 * i))
            for i, (k, a) in enumerate(ratios))
    return x + 0.4 * bandpass(r.standard_normal(n), 2000, 9000) * env_ad(n, 0.0003, 0.004)


def coin_clink(r, n, base=None):
    """One small coin: bright, short, slightly detuned inharmonic ring."""
    base = base if base is not None else r.uniform(2900, 3600)
    return metal_hit(r, n, base, 0.05, bright=0.8)


def creak(r, n, lo, hi, grain_rate):
    """Leather creak: band noise chopped into dense friction grains."""
    t = np.arange(n) / SR
    grains = chitter(r, n, grain_rate, dur_click=0.006, f_lo=lo, f_hi=hi)
    return bandpass(r.standard_normal(n), lo, hi) * (0.35 + 0.65 * (0.5 + 0.5 * np.sin(2 * np.pi * 23 * t))) * 0.5 + grains


def bell(t, f, decay, start=0.0):
    """Soft bell / chime note starting at `start` s (fundamental + octave + inharmonic 2.76)."""
    tt = np.maximum(t - start, 0.0)
    on = t >= start
    att = np.clip(tt / 0.003, 0, 1)
    x = (np.sin(2 * np.pi * f * tt) + 0.35 * np.sin(2 * np.pi * 2 * f * tt) * np.exp(-tt / (decay * 0.5)) +
         0.15 * np.sin(2 * np.pi * 2.76 * f * tt) * np.exp(-tt / (decay * 0.3)))
    return x * att * np.exp(-tt / decay) * on


def dc_block(x):
    """Remove DC / sub-sonic offset (25 Hz one-pole high-pass) before normalising."""
    return onepole_hp(x, 25.0)


def equip_metal():
    """Putting on metal gear: cloth thump, two plate/buckle clanks, short chain jingle."""
    dur = 0.45
    t = t_axis(dur)
    n = len(t)
    r = rng(3301)
    thump = onepole_lp(r.standard_normal(n), 400) * env_ad(n, 0.004, 0.03) * 2.0
    x = 0.6 * thump
    place(x, 0.8 * metal_hit(r, int(0.35 * SR), 1180, 0.09), 0.02)
    place(x, 0.6 * metal_hit(r, int(0.3 * SR), 1520, 0.07), 0.11)
    jingle = chitter(r, n, 90, dur_click=0.006, f_lo=3500, f_hi=7000) * np.clip((t - 0.12) / 0.02, 0, 1) * np.exp(-np.maximum(t - 0.12, 0) / 0.08)
    x += 0.35 * jingle
    x[: int(0.002 * SR)] *= np.linspace(0, 1, int(0.002 * SR))
    return fade_out(normalize(dc_block(x), -3.0), 0.03)


def unequip_metal():
    """Taking off metal gear: rattle first, then a single lower, softer clank as it is set down."""
    dur = 0.4
    t = t_axis(dur)
    n = len(t)
    r = rng(3302)
    rattle = chitter(r, n, 120, dur_click=0.005, f_lo=3000, f_hi=6500) * np.clip(t / 0.03, 0, 1) * np.exp(-t / 0.07)
    x = 0.4 * rattle
    place(x, 0.7 * metal_hit(r, int(0.3 * SR), 940, 0.08, bright=0.6), 0.12)
    place(x, 0.5 * onepole_lp(r.standard_normal(int(0.08 * SR)), 500) * env_ad(int(0.08 * SR), 0.002, 0.02), 0.12)
    return fade_out(normalize(dc_block(x), -5.0), 0.03)


def equip_leather():
    """Putting on leather: short creak while pulling the strap, then a dull buckle click."""
    dur = 0.4
    t = t_axis(dur)
    n = len(t)
    r = rng(3303)
    env = np.clip(t / 0.04, 0, 1) * np.exp(-np.maximum(t - 0.18, 0) / 0.04)
    x = creak(r, n, 350, 1800, 140) * env
    place(x, 0.7 * metal_hit(r, int(0.1 * SR), 2100, 0.015, bright=0.4), 0.27)
    place(x, 0.5 * onepole_lp(r.standard_normal(int(0.05 * SR)), 900) * env_ad(int(0.05 * SR), 0.001, 0.01), 0.27)
    return fade_out(normalize(dc_block(x), -4.0), 0.03)


def unequip_leather():
    """Taking off leather: buckle release tick, rustle and a soft flap as it drops."""
    dur = 0.35
    t = t_axis(dur)
    n = len(t)
    r = rng(3304)
    x = np.zeros(n)
    place(x, 0.5 * metal_hit(r, int(0.06 * SR), 2300, 0.01, bright=0.3), 0.0)
    rustle = creak(r, n, 500, 3000, 60) * np.clip((t - 0.03) / 0.03, 0, 1) * np.exp(-np.maximum(t - 0.05, 0) / 0.06)
    x += 0.7 * rustle
    place(x, 0.9 * onepole_lp(r.standard_normal(int(0.1 * SR)), 350) * env_ad(int(0.1 * SR), 0.003, 0.025), 0.2)
    x[: int(0.002 * SR)] *= np.linspace(0, 1, int(0.002 * SR))
    return fade_out(normalize(dc_block(x), -5.0), 0.03)


def cork_pop(r, n):
    t = np.arange(n) / SR
    return (tone(t, 900 * np.exp(-t / 0.01) + 380) * env_ad(n, 0.0005, 0.012) +
            0.5 * onepole_lp(r.standard_normal(n), 3000) * env_ad(n, 0.0003, 0.004))


def gulp(r, n, f0):
    """Swallow: low formant sweep up with a wet filtered noise burst."""
    t = np.arange(n) / SR
    f = f0 + f0 * 0.9 * np.clip(t / 0.08, 0, 1)
    body = tone(t, f) * env_ad(n, 0.008, 0.035)
    wet = onepole_lp(r.standard_normal(n), 1100) * env_ad(n, 0.004, 0.02)
    return body + 0.6 * wet


def bubbles(r, n, rate, f_lo, f_hi):
    """Rising-pitch bubble blips (liquid)."""
    out = np.zeros(n)
    pos = 0.0
    while True:
        pos += r.exponential(1.0 / rate)
        i = int(pos * SR)
        m = int(0.02 * SR)
        if i + m >= n:
            break
        tt = np.arange(m) / SR
        f = r.uniform(f_lo, f_hi) * (1 + 3.0 * tt / 0.02)
        out[i:i + m] += np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / 0.006) * r.uniform(0.3, 1.0)
    return out


def potion_drink():
    """Health potion (SFX_Item_Potion_01): cork pop, three gulps with a little liquid, short warm rise."""
    dur = 0.9
    t = t_axis(dur)
    n = len(t)
    r = rng(3305)
    x = np.zeros(n)
    place(x, 0.8 * cork_pop(r, int(0.06 * SR)), 0.005)
    for start, f0 in [(0.2, 170), (0.4, 160), (0.6, 180)]:
        place(x, 1.0 * gulp(r, int(0.15 * SR), f0), start)
    x += 0.25 * bubbles(r, n, 25, 500, 1100) * np.clip((t - 0.15) / 0.05, 0, 1) * np.exp(-np.maximum(t - 0.5, 0) / 0.15)
    warm = (tone(t, 523.25) + 0.5 * tone(t, 784.0)) * np.clip((t - 0.62) / 0.1, 0, 1) * np.exp(-np.maximum(t - 0.72, 0) / 0.09)
    x += 0.25 * warm
    return fade_out(normalize(dc_block(x), -3.0), 0.05)


def antidote_drink():
    """Antidote: cork pop, one quick gulp, then a bright cleansing shimmer (hiss sweeping up + rising chime)."""
    dur = 0.9
    t = t_axis(dur)
    n = len(t)
    r = rng(3306)
    x = np.zeros(n)
    place(x, 0.8 * cork_pop(r, int(0.06 * SR)), 0.005)
    place(x, 1.0 * gulp(r, int(0.15 * SR), 190), 0.18)
    p = np.clip((t - 0.35) / 0.4, 0, 1)
    sweep_env = np.clip((t - 0.33) / 0.05, 0, 1) * np.exp(-np.maximum(t - 0.6, 0) / 0.12)
    hiss = onepole_lp(onepole_hp(r.standard_normal(n), 2000 + 3000 * p), 6000 + 4000 * p) * sweep_env
    x += 0.5 * hiss
    for k, (f, st) in enumerate([(1046.5, 0.36), (1318.5, 0.44), (1568.0, 0.52)]):
        x += 0.35 * bell(t, f, 0.14, st)
    return fade_out(normalize(dc_block(x), -3.0), 0.05)


def loot_pickup():
    """Picking up a (common) item: short cloth/leather swish and a small two-note chime."""
    dur = 0.35
    t = t_axis(dur)
    n = len(t)
    r = rng(3307)
    p = np.clip(t / 0.12, 0, 1)
    swish = onepole_lp(onepole_hp(r.standard_normal(n), 800 + 1500 * p), 2500 + 3000 * p) * np.sin(np.pi * p) ** 2
    x = 0.6 * swish + 0.45 * bell(t, 1046.5, 0.08, 0.06) + 0.4 * bell(t, 1568.0, 0.1, 0.1)
    return fade_out(normalize(dc_block(x), -4.0), 0.03)


def coin_pickup():
    """Picking up talers (SFX_UI_CoinPickup_01): three quick coin clinks."""
    dur = 0.4
    t = t_axis(dur)
    n = len(t)
    r = rng(3308)
    x = np.zeros(n)
    for start, a in [(0.0, 1.0), (0.055, 0.8), (0.12, 0.65)]:
        place(x, a * coin_clink(r, int(0.25 * SR)), start + 0.002)
    return fade_out(normalize(dc_block(x), -3.0), 0.03)


def level_up():
    """Level up (SFX_Stinger_LevelUp_01): rising major arpeggio (C5 E5 G5 C6) into a sustained chord with a sparkle swell."""
    dur = 1.4
    t = t_axis(dur)
    n = len(t)
    r = rng(3309)
    x = np.zeros(n)
    for f, st in [(523.25, 0.0), (659.25, 0.11), (783.99, 0.22), (1046.5, 0.33)]:
        x += 0.5 * bell(t, f, 0.5, st + 0.002)
    chord_env = np.clip((t - 0.33) / 0.05, 0, 1) * np.exp(-np.maximum(t - 0.45, 0) / 0.35)
    chord = sum(a * tone(t, f) for f, a in [(523.25, 0.4), (659.25, 0.3), (783.99, 0.3), (1046.5, 0.25)])
    x += 0.35 * chord * chord_env * (0.85 + 0.15 * np.sin(2 * np.pi * 5.5 * t))
    sparkle = chitter(r, n, 60, dur_click=0.004, f_lo=4000, f_hi=9000) * np.clip((t - 0.3) / 0.15, 0, 1) * np.exp(-np.maximum(t - 0.5, 0) / 0.3)
    x += 0.25 * sparkle
    return fade_out(normalize(dc_block(x), -6.0), 0.15)


def shop_open():
    """Opening the merchant's shop: wooden latch knock and a two-tone shop bell."""
    dur = 0.5   # UI budget <= 0.5 s (#60)
    t = t_axis(dur)
    n = len(t)
    r = rng(3310)
    knock = onepole_lp(r.standard_normal(n), 1000) * env_ad(n, 0.001, 0.02)
    wood = sum(a * tone(t, f) * env_ad(n, 0.001, d) for f, a, d in [(240, 0.6, 0.05), (450, 0.35, 0.03)])
    x = 0.8 * knock + 0.6 * wood
    x += 0.45 * bell(t, 1244.5, 0.12, 0.05) + 0.4 * bell(t, 1661.2, 0.13, 0.13)
    return fade_out(normalize(dc_block(x), -6.0), 0.05)


def buy():
    """Buying: two coins paid out, then a confirming rising two-note (G5 -> D6)."""
    dur = 0.45
    t = t_axis(dur)
    n = len(t)
    r = rng(3311)
    x = np.zeros(n)
    place(x, 0.8 * coin_clink(r, int(0.2 * SR), 3100), 0.002)
    place(x, 0.6 * coin_clink(r, int(0.2 * SR), 2800), 0.05)
    x += 0.5 * bell(t, 783.99, 0.1, 0.1) + 0.5 * bell(t, 1174.7, 0.14, 0.19)
    return fade_out(normalize(dc_block(x), -3.0), 0.03)


def sell():
    """Selling: a small shower of coins received, then a bright rising two-note (C6 -> G6)."""
    dur = 0.45
    t = t_axis(dur)
    n = len(t)
    r = rng(3312)
    x = np.zeros(n)
    for start, a in [(0.0, 0.7), (0.035, 0.9), (0.07, 0.6), (0.1, 0.5)]:
        place(x, a * coin_clink(r, int(0.2 * SR)), start + 0.002)
    x += 0.45 * bell(t, 1046.5, 0.1, 0.14) + 0.45 * bell(t, 1568.0, 0.14, 0.22)
    return fade_out(normalize(dc_block(x), -3.0), 0.03)


def denied():
    """Refusal (e.g. not enough talers): soft, low falling two-note buzz (E4 -> B3), filtered square-ish."""
    dur = 0.35
    t = t_axis(dur)
    n = len(t)
    x = np.zeros(n)
    for f, st, ln in [(329.63, 0.0, 0.13), (246.94, 0.15, 0.18)]:
        m = int(ln * SR)
        tt = np.arange(m) / SR
        sq = sum(np.sin(2 * np.pi * f * k * tt) / k for k in (1, 3, 5, 7))
        e = np.clip(tt / 0.005, 0, 1) * np.clip((ln - tt) / 0.03, 0, 1)
        place(x, onepole_lp(sq * e, 1800), st + 0.001)
    return fade_out(normalize(dc_block(x), -14.0), 0.02)


def poison_tick():
    """Spider poison tick (SFX_Status_PoisonTick_01): one short wet, sickly bubble blip. One-shot played once per
    DoT tick (every 1 s, COMBAT_PHYSICS.md section 2), not a loop; kept quiet because it repeats."""
    dur = 0.25
    t = t_axis(dur)
    n = len(t)
    r = rng(3314)
    f = 260 * np.exp(-t / 0.06) + 150
    body = (tone(t, f) + 0.4 * tone(t, f * 1.5)) * env_ad(n, 0.004, 0.05)
    hiss = bandpass(r.standard_normal(n), 1500, 5000) * env_ad(n, 0.003, 0.03)
    bl = bubbles(r, n, 40, 350, 700) * np.exp(-t / 0.08)
    x = 1.0 * body + 0.25 * hiss + 0.4 * bl
    return fade_out(normalize(dc_block(x), -8.0), 0.03)


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
    # METINAR-033 (#60) starter kit
    ("Item/SFX_Item_EquipMetal_01.wav", equip_metal),
    ("Item/SFX_Item_UnequipMetal_01.wav", unequip_metal),
    ("Item/SFX_Item_EquipLeather_01.wav", equip_leather),
    ("Item/SFX_Item_UnequipLeather_01.wav", unequip_leather),
    ("Item/SFX_Item_Potion_01.wav", potion_drink),
    ("Item/SFX_Item_Antidote_01.wav", antidote_drink),
    ("Loot/SFX_Loot_Common_01.wav", loot_pickup),
    ("UI/SFX_UI_CoinPickup_01.wav", coin_pickup),
    ("Stinger/SFX_Stinger_LevelUp_01.wav", level_up),
    ("UI/SFX_UI_ShopOpen_01.wav", shop_open),
    ("UI/SFX_UI_Buy_01.wav", buy),
    ("UI/SFX_UI_Sell_01.wav", sell),
    ("UI/SFX_UI_Denied_01.wav", denied),
    ("Status/SFX_Status_PoisonTick_01.wav", poison_tick),
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


def check_aura_periodic():
    """The Aura timbre rendered over two loop lengths must repeat exactly (to float precision) and its first period
    must equal the committed loop render: the seam is then the same as any other sample transition."""
    n = int(round(AURA_LOOP_SECONDS * SR))
    one = aura_layers(np.arange(n) / SR, rng(1811), n)
    two = aura_layers(np.arange(2 * n) / SR, rng(1811), n, periods=2)
    return bool(np.allclose(two[:n], one, atol=1e-9) and np.allclose(two[:n], two[n:], atol=1e-6))


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
            return 1 if bad or not periodic else 0
    for rel, size, sha in generate(a.out):
        print("%-60s %7d B  sha256=%s" % (rel, size, sha))
    return 0


if __name__ == "__main__":
    sys.exit(main())
