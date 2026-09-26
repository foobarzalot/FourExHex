// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.IO;
using System.IO.Compression;
using System.Text;

/// <summary>
/// Gzip layer under the per-level campaign attempt files
/// (<c>user://campaign_saves/level_XX.json.gz</c>). A finished game's save
/// with its full replay is a few hundred KB of JSON that gzips to roughly
/// five percent, which is what lets every one of the 256 levels keep its
/// most recent attempt with no pruning policy.
///
/// Standard gzip (<c>gunzip</c>-readable, so a bug report or a curious
/// player can inspect a file) via the BCL's <see cref="GZipStream"/>.
/// <see cref="Decompress"/> passes plain UTF-8 through untouched, so a
/// hand-dropped uncompressed <c>.json</c> still reads.
/// </summary>
public static class SaveCompression
{
    private const byte GzipMagic0 = 0x1f;
    private const byte GzipMagic1 = 0x8b;

    public static byte[] Compress(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] raw = Encoding.UTF8.GetBytes(text);
            gzip.Write(raw, 0, raw.Length);
        }
        return output.ToArray();
    }

    /// <summary>Inflate gzip bytes to text; non-gzip bytes are read as plain UTF-8.</summary>
    public static string Decompress(byte[] bytes)
    {
        if (!IsGzip(bytes)) return Encoding.UTF8.GetString(bytes);
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>True iff <paramref name="bytes"/> starts with the gzip magic header.</summary>
    public static bool IsGzip(byte[] bytes) =>
        bytes.Length >= 2 && bytes[0] == GzipMagic0 && bytes[1] == GzipMagic1;
}
