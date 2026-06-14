using System;
using System.Globalization;

/// <summary>
/// The master seed is a full 32-bit value, presented and edited as 8
/// hexadecimal digits ("00000000".."FFFFFFFF") in keeping with the
/// game's hex theme. This is the single source of truth for converting
/// between the stored <see cref="int"/> seed and its hex text form, and
/// for drawing a fresh seed across the whole 32-bit range.
///
/// Pure, Godot-free, and integer-only (no float on the model code path,
/// per the no-floats rule). Used by the play-setup field, the in-game
/// seed label, and the random fallbacks in <see cref="GameController"/>
/// and <see cref="Main"/>.
/// </summary>
public static class SeedFormat
{
    /// <summary>Format the 32-bit pattern as 8 uppercase hex digits,
    /// sign-agnostically (e.g. -1 -> "FFFFFFFF").</summary>
    public static string ToHex(int seed) => ((uint)seed).ToString("X8");

    /// <summary>Parse 1-8 hex digits (any case) into the seed, reading the
    /// value as an unsigned 32-bit pattern (so "FFFFFFFF" -> -1). Returns
    /// false on empty/whitespace, non-hex characters, or overflow.</summary>
    public static bool TryParseHex(string? text, out int seed)
    {
        seed = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!uint.TryParse(text, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint value)) return false;
        seed = unchecked((int)value);
        return true;
    }

    /// <summary>Draw a fresh seed across the full 32-bit range. Unlike
    /// <see cref="Random.Next()"/> (which only spans [0, int.MaxValue]),
    /// this reaches negative ints too, so the high hex digit is reachable.</summary>
    public static int NextSeed(Random rng)
        => unchecked((int)(uint)rng.NextInt64(0, 0x1_0000_0000L));
}
