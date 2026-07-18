# Sound Design: the Guildhall palette

The game's 18 SFX cues in `assets/audio/` are one sonic family — the **Guildhall** palette: storybook-orchestral timbres (felt mallets, harps, bells, low drums), tuned to the key of D, warm and rounded. All clips are mono, 16-bit, 44.1 kHz WAV, each normalized to **−3 dBFS peak**, so the `VolumeDb` values in `scripts/AudioBus.cs` are the only gain knob — the loudness hierarchy (routine actions quiet → milestones loud, terrain chimes layered under occupant cues) lives entirely there.

## Cue table

| File | VolumeDb | Dur (s) | Design |
|---|---|---|---|
| click.wav | −6 | 0.14 | Soft mallet tap on D6 with faint octave sparkle. Felt, never noticed. |
| place.wav | −4 | 0.32 | Felt-mallet D3 thud with D4 contact tone and low felt noise. The workhorse. |
| tower_place.wav | −8 | 0.40 | Deeper A2 mallet with pitch settle and low rumble — heavier sibling of place. |
| unit_combine.wav | −8 | 0.96 | Harp-like ascending D5–A5–D6 with F#6 shimmer. Small level-up reward. |
| unit_destroyed.wav | −6 | 0.44 | Muted timpani hit with pitch drop (130→85 Hz). Soft, not gory. |
| tower_destroyed.wav | −8 | 0.56 | Low drum drop + brief cymbal-ish air + rubble noise. Loudness-matched with tower_place. |
| tree_cleared.wav | −18 | 0.19 | Woodblock crack (800/1200 Hz) with band-passed snap. Hot transient by design. |
| capital_destroyed.wav | −10 | 1.26 | Three descending drum hits into a D4 minor bell. Routine-event weight, not milestone. |
| bankruptcy.wav | −10 | 1.11 | Arcade "game over" womp: three descending chip steps (triangle lead + sine sub, D4–A3–F3) landing on a pitch-bent D3/D2. |
| game_won.wav | −6 | 2.01 | Full D-major bell peal (D5–F#5–A5–D6–F#6) resolving to sustained D6+A6. The big one. |
| rally.wav | −14 | 0.61 | Airy noise swell + soft A3 horn-ish triangle. One sweep per gesture, no impact. |
| player_defeated.wav | −10 | 2.56 | Low drum onset, D2 gong bloom (inharmonic partials), somber D3 minor-bell overlay. |
| tile_submerged.wav | −8 | 0.66 | Descending harp figure A4–F#4–D4 over a low watery wash. Stays quiet; can fire every turn. |
| viking_arrival.wav | −6 | 2.66 | Recorded clip (wave wash → hull creak) darkened via lowpass, with an A2 hall drone swell and a distant D2 drum as the creak lands. |
| reject_generic.wav | −17 | 0.20 | Muted low mallet (F3). Informative, never punishing. |
| reject_defended.wav | −8 | 0.48 | Brass-metallic clang (466 Hz, inharmonic partials) + tick — audibly distinct from generic reject. |
| gold_captured.wav | −8 | 0.58 | Glockenspiel D6–F#6–A6. Layers under the occupant cue. |
| mountain_captured.wav | −8 | 0.44 | Deep drum thud (90→55 Hz) with low noise body. Low end reads without gain. |

## Design tokens (sonic)

- **Key**: D. Tuned cues use D-major pitches (D3 146.83, D4 293.66, D5 587.33, F#5 739.99, A5 880, D6 1174.66, F#6 1479.98, A6 1760 Hz). Somber cues use minor-third partials (ratio 1.19).
- **Attack character**: felt — 8–15 ms attacks on mallets/bells (vs. 2–6 ms in a "clicky" palette).
- **Partial sets**: gong `[1, 1.48, 2.31, 3.17, 4.5]`; metallic clang `[1, 1.5, 2.4, 3.8]`; minor bell `[1, 1.19, 2, 2.98]`.
- **Loudness**: every file peaks at −3 dBFS (0.707 FS); hierarchy lives entirely in `VolumeDb`.

## Generator / reproducibility

`tools/sfx-engine.js` is the generator source: a browser Web Audio engine containing the exact recipe for every cue (three palettes — `wood`, `guild`, `slate`; the shipped set is `guild`). It is a design reference, not wired to run in the repo. To make regeneration a repo tool, port the `guild` cue functions to Python or run the JS engine headless (Puppeteer/Playwright + `OfflineAudioContext`). Rendering is deterministic **except** noise components use `Math.random()` — seed a PRNG when porting if bit-exact reproducibility matters (perceptually identical either way).

**Dependency**: `viking_arrival.wav` mixes in `tools/source-samples/viking_arrival_original.wav` (an ElevenLabs-generated recording). Keep that file with the generator; the `.gdignore` in `tools/source-samples/` keeps Godot from importing it as a game asset.

## Cue-list gaps

Natural additions if wanted later: `turn_start`, `gold_income`, and a human-loss `game_lost` distinct from `player_defeated`.
