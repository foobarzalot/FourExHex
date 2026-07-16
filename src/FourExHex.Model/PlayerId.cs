// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Godot-free stable player identity. Wraps a single byte:
/// <c>0</c> = <see cref="None"/> (the default value, used for
/// unowned/neutral tiles); <c>1..N</c> encodes roster index + 1,
/// so a real player never collides with the default.
/// Value-equality and the byte ordering make it safe as a
/// dictionary key and a deterministic tiebreaker.
/// </summary>
public readonly struct PlayerId : IEquatable<PlayerId>, IComparable<PlayerId>
{
    private readonly byte _raw;

    private PlayerId(byte raw) => _raw = raw;

    /// <summary>No owner / neutral. Equals <c>default(PlayerId)</c>.</summary>
    public static readonly PlayerId None = default;

    /// <summary>The identity for the player at <paramref name="rosterIndex"/>.</summary>
    public static PlayerId FromIndex(int rosterIndex)
    {
        if (rosterIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rosterIndex), rosterIndex, "Roster index must be non-negative.");
        }
        return new PlayerId(checked((byte)(rosterIndex + 1)));
    }

    public bool IsNone => _raw == 0;

    /// <summary>Roster index. Only meaningful when <see cref="IsNone"/> is false.</summary>
    public int Index => _raw - 1;

    public bool Equals(PlayerId other) => _raw == other._raw;
    public override bool Equals(object? obj) => obj is PlayerId p && Equals(p);
    public override int GetHashCode() => _raw;
    public int CompareTo(PlayerId other) => _raw.CompareTo(other._raw);
    public override string ToString() => IsNone ? "None" : $"P{Index}";

    public static bool operator ==(PlayerId a, PlayerId b) => a.Equals(b);
    public static bool operator !=(PlayerId a, PlayerId b) => !a.Equals(b);
}
