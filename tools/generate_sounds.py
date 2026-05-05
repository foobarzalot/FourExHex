#!/usr/bin/env python3
"""
Procedural sound-effect generator for FourExHex.

Writes 16-bit PCM mono WAV files to ../assets/audio/. Outputs are
deterministic and committed to the repo; this script exists so the
sounds can be regenerated or retuned without losing reproducibility.

Run from anywhere:
    python3 tools/generate_sounds.py
"""

from __future__ import annotations

import math
import os
import random
import struct
import wave
from typing import Iterable, List

SAMPLE_RATE = 44_100
ASSETS_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "audio")


def _samples_to_pcm16(samples: Iterable[float]) -> bytes:
    """Clamp floats in [-1, 1] and pack as little-endian signed 16-bit."""
    out = bytearray()
    for s in samples:
        if s > 1.0:
            s = 1.0
        elif s < -1.0:
            s = -1.0
        out += struct.pack("<h", int(s * 32767.0))
    return bytes(out)


def write_wav(path: str, samples: List[float]) -> None:
    pcm = _samples_to_pcm16(samples)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(SAMPLE_RATE)
        wf.writeframes(pcm)
    print(f"wrote {path} ({len(samples)} samples, {len(samples)/SAMPLE_RATE*1000:.1f} ms)")


def generate_button_click() -> List[float]:
    """
    Soft UI click: a brief noise transient gives it 'tap' character,
    layered with two short sine partials so it has tone instead of
    sounding like static. Exponential decay keeps the tail short and
    unobtrusive — this should feel like pressing a real key, not a
    chiptune blip.
    """
    duration_s = 0.055
    n = int(SAMPLE_RATE * duration_s)

    # Transient: 2 ms of filtered noise, very steep decay.
    transient_len = int(SAMPLE_RATE * 0.002)

    # Tone partials: two sines an octave apart, with the upper partial
    # decaying faster so the click brightens at attack and softens out.
    f1 = 1500.0  # body
    f2 = 3000.0  # sparkle
    tau1 = 0.012  # ~12 ms
    tau2 = 0.006  # ~6  ms

    rng = random.Random(20260505)  # deterministic noise seed
    out: List[float] = []
    # One-pole low-pass on the noise to take the harshness off.
    lp_state = 0.0
    lp_alpha = 0.35

    for i in range(n):
        t = i / SAMPLE_RATE

        # Transient noise burst with linear-ish ramp down.
        noise_amp = 0.0
        if i < transient_len:
            noise = rng.uniform(-1.0, 1.0)
            lp_state = lp_state + lp_alpha * (noise - lp_state)
            ramp = 1.0 - (i / transient_len)
            noise_amp = lp_state * ramp * 0.45
        else:
            # Let the filter relax so we don't get a discontinuity.
            lp_state *= 0.9

        # Sine partials with exponential decay envelopes.
        env1 = math.exp(-t / tau1)
        env2 = math.exp(-t / tau2)
        tone = (
            0.32 * env1 * math.sin(2.0 * math.pi * f1 * t)
            + 0.18 * env2 * math.sin(2.0 * math.pi * f2 * t)
        )

        sample = noise_amp + tone

        # Tiny attack ramp on the very first samples to avoid a DC click.
        if i < 16:
            sample *= i / 16.0

        out.append(sample)

    return out


def generate_unit_place() -> List[float]:
    """
    Soft thud for moving or buying-and-placing a unit on a tile.

    Layered components:
      * Brief filtered noise transient — the 'impact' of contact with
        the board.
      * A low fundamental (~180 Hz) for body / weight, decaying over
        ~120 ms.
      * A higher partial (~440 Hz) shorter-decay so the attack reads
        as "wood/stone" rather than pure bass.

    Total length ~150 ms — long enough to feel substantial, short
    enough to fire on every action without becoming fatiguing.
    """
    duration_s = 0.150
    n = int(SAMPLE_RATE * duration_s)

    # 5 ms transient — thicker than the click's 2 ms because we want
    # 'thump' rather than 'tick'.
    transient_len = int(SAMPLE_RATE * 0.005)

    f_low = 180.0   # body / weight
    f_mid = 440.0   # contact tone
    tau_low = 0.060
    tau_mid = 0.025

    rng = random.Random(20260505 ^ 0xBEEF)
    out: List[float] = []
    # Heavier low-pass on the noise so it reads as 'thud' not 'pop'.
    lp_state = 0.0
    lp_alpha = 0.18

    # Tiny pitch droop on the low partial: pitch falls a few percent
    # over the first ~30 ms, the way a struck object's ringing settles.
    droop_amount = 0.06   # 6% drop
    droop_tau = 0.030

    phase_low = 0.0
    phase_mid = 0.0

    for i in range(n):
        t = i / SAMPLE_RATE

        # Transient: filtered noise burst.
        noise_amp = 0.0
        if i < transient_len:
            noise = rng.uniform(-1.0, 1.0)
            lp_state = lp_state + lp_alpha * (noise - lp_state)
            ramp = 1.0 - (i / transient_len)
            noise_amp = lp_state * ramp * 0.55
        else:
            lp_state *= 0.85

        # Pitch droop: f_low_now = f_low * (1 + droop * exp(-t/droop_tau))
        f_low_now = f_low * (1.0 + droop_amount * math.exp(-t / droop_tau))

        # Phase accumulation so the droop doesn't introduce phase jumps.
        phase_low += 2.0 * math.pi * f_low_now / SAMPLE_RATE
        phase_mid += 2.0 * math.pi * f_mid / SAMPLE_RATE

        env_low = math.exp(-t / tau_low)
        env_mid = math.exp(-t / tau_mid)
        tone = (
            0.55 * env_low * math.sin(phase_low)
            + 0.18 * env_mid * math.sin(phase_mid)
        )

        sample = noise_amp + tone

        if i < 16:
            sample *= i / 16.0

        out.append(sample)

    return out


def generate_tower_place() -> List[float]:
    """
    Stone-on-stone clack for placing a tower.

    Compared to the unit-place thud, this sound:
      * has a heavier transient with longer-tail noise (the 'rubble'
        of stone scraping against stone),
      * sits lower in the spectrum (130 Hz fundamental, 360 Hz mid),
      * lasts a bit longer (~190 ms),
      * exposes more high-frequency grit through a *separately filtered*
        bright noise component during the first ~25 ms — that's what
        sells 'stone' over 'wood'.

    The pitch droop on the body partial is a touch deeper than the
    unit sound's, so the tower sits noticeably heavier on the ear even
    if you can't articulate why.
    """
    duration_s = 0.190
    n = int(SAMPLE_RATE * duration_s)

    # Transient: 9 ms of low-passed noise for the impact body, plus
    # 25 ms of higher-passed grit for the stone scrape.
    impact_len = int(SAMPLE_RATE * 0.009)
    grit_len = int(SAMPLE_RATE * 0.025)

    f_low = 130.0
    f_mid = 360.0
    tau_low = 0.075
    tau_mid = 0.030

    rng = random.Random(20260505 ^ 0xCAFE)

    out: List[float] = []

    # Two parallel noise filters: a heavy LPF for the impact body, and
    # a high-passed-by-subtraction noise for the grit. We approximate
    # high-pass as (raw - lp_state) so we get a brighter band without
    # writing a real biquad.
    lp_state = 0.0
    lp_alpha = 0.14
    grit_lp_state = 0.0
    grit_lp_alpha = 0.55  # follows the input fast → most energy stays high

    droop_amount = 0.09  # 9% droop — a touch heavier than the unit sound
    droop_tau = 0.035

    phase_low = 0.0
    phase_mid = 0.0

    for i in range(n):
        t = i / SAMPLE_RATE

        # Impact: low-passed noise burst.
        impact_amp = 0.0
        if i < impact_len:
            n1 = rng.uniform(-1.0, 1.0)
            lp_state = lp_state + lp_alpha * (n1 - lp_state)
            ramp = 1.0 - (i / impact_len)
            impact_amp = lp_state * ramp * 0.62
        else:
            lp_state *= 0.85

        # Grit: high-passed noise that decays exponentially over ~25ms.
        grit_amp = 0.0
        if i < grit_len:
            n2 = rng.uniform(-1.0, 1.0)
            grit_lp_state = grit_lp_state + grit_lp_alpha * (n2 - grit_lp_state)
            high_passed = n2 - grit_lp_state
            env = math.exp(-i / (grit_len * 0.5))
            grit_amp = high_passed * env * 0.22

        # Body partials.
        f_low_now = f_low * (1.0 + droop_amount * math.exp(-t / droop_tau))
        phase_low += 2.0 * math.pi * f_low_now / SAMPLE_RATE
        phase_mid += 2.0 * math.pi * f_mid / SAMPLE_RATE

        env_low = math.exp(-t / tau_low)
        env_mid = math.exp(-t / tau_mid)
        tone = (
            0.55 * env_low * math.sin(phase_low)
            + 0.16 * env_mid * math.sin(phase_mid)
        )

        sample = impact_amp + grit_amp + tone

        if i < 16:
            sample *= i / 16.0

        out.append(sample)

    return out


def main() -> None:
    write_wav(os.path.normpath(os.path.join(ASSETS_DIR, "click.wav")),
              generate_button_click())
    write_wav(os.path.normpath(os.path.join(ASSETS_DIR, "place.wav")),
              generate_unit_place())
    write_wav(os.path.normpath(os.path.join(ASSETS_DIR, "tower_place.wav")),
              generate_tower_place())


if __name__ == "__main__":
    main()
