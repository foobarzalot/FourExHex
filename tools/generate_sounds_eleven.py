#!/usr/bin/env python3
"""
ElevenLabs Sound Effects driver for FourExHex.

Each entry in MANIFEST is (output_filename, prompt, duration_seconds,
prompt_influence). Running this script POSTs each prompt to ElevenLabs'
sound-generation endpoint, requests raw PCM 44100 stereo, and wraps the
result as a 16-bit stereo WAV under assets/audio/.

By default an existing file is left alone — re-running the script
won't burn API quota on already-generated sounds. Pass --force to
regenerate.

Reads the API key from ELEVENLABS_API_KEY in the environment.

Usage:
    python3 tools/generate_sounds_eleven.py
    python3 tools/generate_sounds_eleven.py --force
    python3 tools/generate_sounds_eleven.py place.wav tower_place.wav
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import sys
import urllib.error
import urllib.request
import wave
from typing import List, Tuple

API_URL = "https://api.elevenlabs.io/v1/sound-generation"
ASSETS_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "audio")

# Output format the API returns when ?output_format=pcm_44100 is set:
# 16-bit signed little-endian, 44.1 kHz, stereo, no header.
SAMPLE_RATE = 44_100
CHANNELS = 2
BITS_PER_SAMPLE = 16

# (filename, prompt, duration_seconds, prompt_influence)
# - duration_seconds: clamped to [0.5, 30] by the API
# - prompt_influence: 0.0–1.0; higher sticks closer to the prompt and
#   produces less variation, which is what we want for game SFX.
Entry = Tuple[str, str, float, float]
MANIFEST: List[Entry] = [
    (
        "place.wav",
        "A wooden game piece set firmly onto a wooden game board, "
        "single contact, soft thud, dry, brief, no music, no reverb, "
        "no voice.",
        0.5,
        0.7,
    ),
    (
        "tower_place.wav",
        "A heavy stone block set firmly onto a stone surface, single "
        "contact, weighty stone-on-stone thud with subtle textural "
        "edge, defined low body, moderate impact, dry, no sharp click, "
        "no metallic ring, no music, no reverb, no voice.",
        0.6,
        0.75,
    ),
    (
        "unit_combine.wav",
        "A short, bright video-game upgrade chime: three bell-like "
        "notes ascending quickly, sparkling and rewarding, satisfying "
        "with a small flourish, not overly triumphant, dry, no music "
        "bed, no voice, minimal reverb tail.",
        0.6,
        0.7,
    ),
    (
        "unit_destroyed.wav",
        "A short cartoonish smoosh sound for crushing a small enemy "
        "in a fantasy strategy game, soft squelch with a low thud "
        "underneath, satisfying and brief, not gory, dry, no music, "
        "no voice, no reverb.",
        0.5,
        0.75,
    ),
    (
        "tower_destroyed.wav",
        "A short stone tower bursting and collapsing, dense rocky "
        "impact with brief debris and a small dust puff, satisfying "
        "destruction, dry, no music, no voice, no reverb tail.",
        0.7,
        0.75,
    ),
    (
        "tree_cleared.wav",
        "A short single axe chop into a wooden tree trunk, sharp "
        "wood-on-wood impact with brief splintering, dry, no music, "
        "no voice, no reverb tail.",
        0.5,
        0.75,
    ),
    (
        "capital_destroyed.wav",
        "A small wooden flagpole tipping over and clattering onto the "
        "ground, with a cloth banner fluttering down briefly, "
        "lightweight and modest, no stone, dry, no music, no voice, "
        "no reverb tail.",
        0.6,
        0.75,
    ),
    (
        "bankruptcy.wav",
        "A single low somber bell toll, slow and mournful, signaling "
        "loss without being dramatic, brief sustain, dry, no music "
        "underneath, no voice, minimal reverb tail.",
        0.9,
        0.75,
    ),
    (
        "game_won.wav",
        "A short joyful peal of medieval church bells in an ascending "
        "pattern, several bells ringing together rising in pitch, "
        "resolving to a sustained high bell, celebratory and "
        "triumphant, dry, no music underneath, no voice, brief "
        "reverb tail.",
        1.6,
        0.75,
    ),
    (
        "rally.wav",
        "A short crisp whoosh of moving air, like several cloth banners "
        "or capes being swept forward in unison, soft airy attack with "
        "a quick decay and a hint of fabric rustle, no impact at the "
        "end, dry, no music, no voice, no reverb tail.",
        0.7,
        0.75,
    ),
    (
        "player_defeated.wav",
        "A single deep heavy temple gong struck once, very low pitched "
        "with a long booming resonant tail slowly fading, weighty and "
        "final, somber and ominous, dry attack with a full natural "
        "sustain, no music underneath, no voice, no extra reverb.",
        1.5,
        0.75,
    ),
]


def fetch_pcm(prompt: str, duration_seconds: float, prompt_influence: float,
              api_key: str) -> bytes:
    body = json.dumps({
        "text": prompt,
        "duration_seconds": duration_seconds,
        "prompt_influence": prompt_influence,
    }).encode("utf-8")
    req = urllib.request.Request(
        f"{API_URL}?output_format=pcm_44100",
        data=body,
        method="POST",
        headers={
            "xi-api-key": api_key,
            "Content-Type": "application/json",
            "Accept": "audio/wav",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.read()
    except urllib.error.HTTPError as e:
        # Surface the API's JSON error so the user sees what went wrong.
        body = e.read().decode("utf-8", errors="replace")
        raise SystemExit(f"ElevenLabs HTTP {e.code}: {body}") from None


def write_pcm_as_wav(path: str, pcm_bytes: bytes) -> None:
    """
    Wrap raw 16-bit signed little-endian stereo PCM as a WAV file.
    """
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sample_count = len(pcm_bytes) // (CHANNELS * (BITS_PER_SAMPLE // 8))
    with wave.open(path, "wb") as wf:
        wf.setnchannels(CHANNELS)
        wf.setsampwidth(BITS_PER_SAMPLE // 8)
        wf.setframerate(SAMPLE_RATE)
        wf.writeframes(pcm_bytes)
    duration_s = sample_count / SAMPLE_RATE
    print(f"wrote {path} ({sample_count} frames, {duration_s*1000:.1f} ms)")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--force", action="store_true",
                        help="Regenerate even if the output file already exists.")
    parser.add_argument("filenames", nargs="*",
                        help="If given, only regenerate these manifest entries "
                             "(by output filename).")
    args = parser.parse_args()

    api_key = os.environ.get("ELEVENLABS_API_KEY")
    if not api_key:
        print("ELEVENLABS_API_KEY is not set in the environment.", file=sys.stderr)
        return 1

    selected = set(args.filenames) if args.filenames else None
    any_run = False
    for fname, prompt, duration_s, prompt_influence in MANIFEST:
        if selected is not None and fname not in selected:
            continue
        out_path = os.path.normpath(os.path.join(ASSETS_DIR, fname))
        if os.path.exists(out_path) and not args.force:
            print(f"skip {fname} (exists; pass --force to regenerate)")
            continue
        any_run = True
        print(f"generating {fname}: {prompt[:80]}...")
        pcm_bytes = fetch_pcm(prompt, duration_s, prompt_influence, api_key)
        write_pcm_as_wav(out_path, pcm_bytes)

    if not any_run and selected is None:
        print("nothing to do — all manifest files exist. Pass --force to regenerate.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
