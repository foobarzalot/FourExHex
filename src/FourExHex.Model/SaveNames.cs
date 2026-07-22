// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Text.RegularExpressions;

/// <summary>
/// Slot-name sanitation shared by the Godot <c>SaveStore</c> and the
/// headless level-design CLI, so both produce identical on-disk names.
/// </summary>
public static class SaveNames
{
    /// <summary>
    /// Replace anything that isn't <c>[A-Za-z0-9_-]</c> with an
    /// underscore. Keeps slot names safe for file systems and avoids
    /// directory traversal attempts. Truncates to 64 chars; empty or
    /// whitespace input falls back to "save".
    /// </summary>
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "save";
        string cleaned = Regex.Replace(raw.Trim(), "[^A-Za-z0-9_-]", "_");
        if (cleaned.Length > 64) cleaned = cleaned.Substring(0, 64);
        if (cleaned.Length == 0) cleaned = "save";
        return cleaned;
    }
}
