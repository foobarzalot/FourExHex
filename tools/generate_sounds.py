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


def main() -> None:
    write_wav(os.path.normpath(os.path.join(ASSETS_DIR, "click.wav")),
              generate_button_click())


if __name__ == "__main__":
    main()
