# Generates gold_captured.wav and mountain_captured.wav for FourExHex
# issue #155, following the synth spec in the design handoff README
# (design_handoff_capture_feedback). 44.1kHz 16-bit mono PCM.
import math
import random
import struct
import wave

SR = 44100
random.seed(155)


def exp_env(t, t_attack, dur, peak, floor=1e-4):
    """WebAudio-style envelope: exponential ramp floor->peak over t_attack,
    then exponential decay peak->floor over the remaining duration."""
    if t < 0 or t >= dur:
        return 0.0
    if t < t_attack:
        return floor * (peak / floor) ** (t / t_attack)
    frac = (t - t_attack) / (dur - t_attack)
    return peak * (floor / peak) ** frac


def add_tone(buf, freq, t0, dur, wave_type, peak, glide_to=None):
    n0 = int(t0 * SR)
    n = int(dur * SR)
    for i in range(n):
        t = i / SR
        if glide_to:
            # phase integral of f(t) = f0 * r^(t/dur), r = f1/f0
            r = glide_to / freq
            phase = 2 * math.pi * freq * dur / math.log(r) * (r ** (t / dur) - 1)
        else:
            phase = 2 * math.pi * freq * t
        if wave_type == 'sine':
            s = math.sin(phase)
        elif wave_type == 'triangle':
            s = 2 / math.pi * math.asin(math.sin(phase))
        else:
            raise ValueError(wave_type)
        if n0 + i < len(buf):
            buf[n0 + i] += s * exp_env(t, 0.012, dur, peak)


def add_noise_lowpass(buf, t0, dur, cutoff, gain):
    """White noise with linear-decay envelope through a one-pole lowpass."""
    n0 = int(t0 * SR)
    n = int(dur * SR)
    a = 1 - math.exp(-2 * math.pi * cutoff / SR)
    y = 0.0
    for i in range(n):
        x = (random.random() * 2 - 1) * (1 - i / n)
        y += a * (x - y)
        if n0 + i < len(buf):
            buf[n0 + i] += y * gain


def write_wav(path, buf):
    peak = max(abs(v) for v in buf) or 1.0
    # Normalize to -3 dBFS so AudioBus VolumeDb settings are the only gain knob.
    scale = 0.707 / peak
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        frames = b''.join(
            struct.pack('<h', int(max(-1.0, min(1.0, v * scale)) * 32767))
            for v in buf)
        w.writeframes(frames)
    print(path, len(buf) / SR, 's')


# --- coin chime: B5 + E6 staggered 85ms + E7 triangle shimmer ---
coin = [0.0] * int(0.55 * SR)
add_tone(coin, 987.77, 0.0, 0.22, 'sine', 0.22)
add_tone(coin, 1318.5, 0.085, 0.30, 'sine', 0.22)
add_tone(coin, 2637.0, 0.17, 0.18, 'triangle', 0.06)
write_wav('assets/audio/gold_captured.wav', coin)

# --- rocky thud: 110->52Hz sine glide + 280ms lowpassed noise burst ---
thud = [0.0] * int(0.32 * SR)
add_tone(thud, 110.0, 0.0, 0.20, 'sine', 0.5, glide_to=52.0)
add_noise_lowpass(thud, 0.0, 0.28, 420.0, 0.4)
write_wav('assets/audio/mountain_captured.wav', thud)
