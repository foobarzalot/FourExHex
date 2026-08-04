// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// Hands a staged bug report to the player's mail client, taking the best
/// rung available on this device. Mirrors <see cref="ShareBridge"/>'s shape:
/// static, harmless everywhere, outcome reported through <see cref="Log"/>.
///
/// The rungs trade off recipient against attachment, so each one loses
/// something the one above it kept:
///   1. <see cref="Rung.Native"/> — a platform mail composer carries
///      recipient, subject, body AND attachment. Android only; see the note
///      below for why there is no iOS equivalent.
///   2. <see cref="Rung.ShareSheet"/> — the OS share sheet carries the
///      attachment but has no recipient field, so the address goes on the
///      clipboard and the modal tells the player to paste it. This is the
///      iOS path.
///   3. <see cref="Rung.Mailto"/> — a <c>mailto:</c> URL carries recipient
///      and body but cannot attach; the modal names the staged file instead.
///      The desktop path.
///
/// The native rung is Android-only: its plugin API is a runtime contract (a
/// <c>compileOnly</c> Maven interface plus a manifest entry, bridged over
/// JNI), while an iOS Engine-singleton plugin is statically linked C++ built
/// against the engine's generated headers. iOS therefore takes the share
/// sheet, which carries the bundle and costs the player one paste.
/// </summary>
public static class MailBridge
{
    /// <summary>Which rung <see cref="Compose"/> took. Returned so the modal
    /// can tell the player what still needs doing by hand.</summary>
    public enum Rung
    {
        Native,
        ShareSheet,
        Mailto,
        Unavailable,
    }

    /// <summary>Where reports go — the same address the credits blurb links,
    /// so the binary carries one address rather than two.</summary>
    public const string Address = "foobarzalot@gmail.com";

    /// <summary>Engine singleton exposed by the MailCompose Android plugin
    /// (android_plugin/mailcompose).</summary>
    private const string AndroidSingletonName = "MailCompose";

    public static Rung Compose(StagedBugReport staged)
    {
        // Rung 1: the native composer — the only one that carries recipient
        // and attachment together. It declines when no mail app can take the
        // report, which is ordinary, not an error, and drops through.
        if (OS.HasFeature("android") && TryComposeAndroid(staged)) return Rung.Native;

        // Rung 2: the share sheet attaches the bundle but drops the
        // recipient, so prime the clipboard before handing off.
        if (ShareBridge.Available)
        {
            DisplayServer.ClipboardSet(Address);
            Log.Info(Log.LogCategory.Report, "[report] compose -> share-sheet-fallback");
            ShareBridge.ShareFile(staged.AbsolutePath, BugReportBundle.MimeType,
                staged.Subject);
            return Rung.ShareSheet;
        }

        // Rung 3: desktop. Recipient and body survive; the attachment does
        // not. Reveal the staged file first so it is sitting selected in a
        // file manager window, then open the composer on top of it — that
        // turns "go find this path" into one drag. Order matters: opening
        // the composer last leaves it frontmost.
        string url = BugReport.MailtoUrl(Address, staged.Subject, staged.Body);
        Log.Info(Log.LogCategory.Report,
            $"[report] compose -> mailto-fallback ({url.Length} chars), " +
            $"revealing '{staged.AbsolutePath}'");
        OS.ShellShowInFileManager(staged.AbsolutePath);
        OS.ShellOpen(url);
        return Rung.Mailto;
    }

    /// <summary>
    /// Try the Android composer (ACTION_SEND with EXTRA_EMAIL, targeted at
    /// each installed mail app, attachment via our own FileProvider). Returns
    /// false when the plugin is absent or no mail app can take the report, so
    /// <see cref="Compose"/> falls to the share sheet.
    /// </summary>
    private static bool TryComposeAndroid(StagedBugReport staged)
    {
        if (!Engine.HasSingleton(AndroidSingletonName))
        {
            Log.Warn(Log.LogCategory.Report,
                "[report] MailCompose singleton absent — falling back");
            return false;
        }
        GodotObject plugin = Engine.GetSingleton(AndroidSingletonName);
        bool composed = plugin.Call("compose", Address, staged.Subject,
            staged.Body, staged.AbsolutePath).AsBool();
        if (!composed)
        {
            Log.Warn(Log.LogCategory.Report,
                "[report] android composer declined — falling back");
            return false;
        }
        Log.Info(Log.LogCategory.Report, "[report] compose -> android-native");
        return true;
    }
}
