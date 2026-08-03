// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FourExHex
using System;
using System.Collections.Generic;
using FourExHex.Controller;
using FourExHex.Model;
using Godot;

/// <summary>
/// Autoload that owns the OS-incoming-file surfaces and turns them into map
/// imports. Android: the FileOpen plugin's activity receives tap-to-open and
/// share-to-app intents and parks the copied file's path for polling. iOS:
/// opened documents land in Documents/Inbox, scanned directly. Both sources
/// are polled at startup and ~0.25 s after every application-resume (every
/// delivery foregrounds the app; iOS file delivery can lag foregrounding).
///
/// Paths are filtered through <see cref="ShareReceiveRules.FxhmapPaths"/>
/// and imported immediately via <see cref="MapImportFlow.ImportAtPath"/> —
/// no live scene needed. The outcome notice is buffered here;
/// <see cref="MainMenuScene"/> drains it on arrival (or instantly via
/// <see cref="OutcomeReady"/> when the menu is already up).
///
/// <c>FOUREXHEX_FAKE_SHARE_RECEIVE=&lt;absolute path&gt;</c> simulates a
/// received file on desktop (source file retained); the real plugin cache
/// copies are deleted after processing.
/// </summary>
public partial class ShareInbox : Node
{
    private const string FileOpenSingleton = "FileOpen";
    private const double ResumePollDelaySeconds = 0.25;
    // iOS copies every opened document into the sandbox Documents/Inbox
    // before launching/foregrounding the app (we don't declare
    // LSSupportsOpeningDocumentsInPlace), and user:// is the Documents dir
    // on iOS — so tapped files are always waiting here, even when no URL
    // callback reaches the share plugin (cold-start URLs arrive in the
    // scene connection options, before the plugin exists).
    private const string IosInboxDirectory = "user://Inbox/";

    private static string? _pendingMessage;
    private static bool _pendingOk;

    /// <summary>Fires (on the main thread) after a received file has been
    /// processed and an outcome notice is waiting in the buffer.</summary>
    public static event Action? OutcomeReady;

    /// <summary>Take the buffered outcome notice, if any. Consume-once.</summary>
    public static bool TryTakeOutcome(out string message, out bool ok)
    {
        message = _pendingMessage ?? "";
        ok = _pendingOk;
        bool had = _pendingMessage != null;
        _pendingMessage = null;
        _pendingOk = false;
        return had;
    }

    public override void _Ready()
    {
        // One deferred frame so scene-tree boot (and Log configuration) is
        // settled before any cold-start payload is processed.
        CallDeferred(MethodName.InitializeSources);
    }

    private void InitializeSources()
    {
        string fake = OS.GetEnvironment("FOUREXHEX_FAKE_SHARE_RECEIVE");
        if (fake.Length > 0)
        {
            Log.Info(Log.LogCategory.Share, $"[share] inbox: fake receive '{fake}'");
            HandlePaths(new[] { fake }, deleteSources: false);
        }

        PollFileOpen("cold-start");
        ScanIosInbox("cold-start");
    }

    private void ScanIosInbox(string reason)
    {
        if (!OS.HasFeature("ios")) return;
        using DirAccess dir = DirAccess.Open(IosInboxDirectory);
        if (dir == null) return;
        List<string> paths = new();
        foreach (string name in dir.GetFiles())
        {
            paths.Add(ProjectSettings.GlobalizePath(IosInboxDirectory + name));
        }
        if (paths.Count == 0)
        {
            Log.Debug(Log.LogCategory.Share,
                $"[share] inbox: {reason} iOS Inbox — empty");
            return;
        }
        Log.Info(Log.LogCategory.Share,
            $"[share] inbox: {reason} iOS Inbox — {paths.Count} file(s)");
        // Non-map files are deleted too: iOS re-delivers nothing, and a
        // stale Inbox copy would otherwise be re-logged on every resume.
        HandlePaths(paths.ToArray(), deleteSources: true, deleteIgnored: true);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationApplicationResumed) return;
        if (!Engine.HasSingleton(FileOpenSingleton) && !OS.HasFeature("ios")) return;
        // iOS file delivery lags foregrounding slightly; poll on a short
        // delay instead of immediately.
        GetTree().CreateTimer(ResumePollDelaySeconds).Timeout += () =>
        {
            PollFileOpen("resume");
            ScanIosInbox("resume");
        };
    }

    private void PollFileOpen(string reason)
    {
        if (!Engine.HasSingleton(FileOpenSingleton)) return;
        string path = Engine.GetSingleton(FileOpenSingleton)
            .Call("get_pending_open_path").AsString();
        if (path.Length == 0)
        {
            Log.Debug(Log.LogCategory.Share,
                $"[share] inbox: {reason} open-with poll — empty");
            return;
        }
        Log.Info(Log.LogCategory.Share, $"[share] inbox: {reason} open-with '{path}'");
        HandlePaths(new[] { path }, deleteSources: true);
    }

    private void HandlePaths(string[] paths, bool deleteSources, bool deleteIgnored = false)
    {
        IReadOnlyList<string> accepted = ShareReceiveRules.FxhmapPaths(paths);
        HashSet<string> acceptedSet = new(accepted, StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (!acceptedSet.Contains(path))
            {
                Log.Info(Log.LogCategory.Share,
                    $"[share] inbox: ignored non-map '{path}'");
                if (deleteIgnored) DirAccess.RemoveAbsolute(path);
            }
        }
        if (accepted.Count == 0) return;

        SaveStore store = new SaveStore();
        bool anyOk = false;
        List<string> messages = new();
        foreach (string path in accepted)
        {
            MapImportFlow.Outcome outcome = MapImportFlow.ImportAtPath(store, path);
            anyOk |= outcome.Ok;
            messages.Add(outcome.Message);
            if (deleteSources)
            {
                // The plugin's cache copy is ours; drop it so retries/dedupe
                // never re-see it (garbage included).
                DirAccess.RemoveAbsolute(path);
                Log.Debug(Log.LogCategory.Share,
                    $"[share] inbox: consumed source '{path}'");
            }
        }

        _pendingMessage = _pendingMessage == null
            ? string.Join("\n", messages)
            : _pendingMessage + "\n" + string.Join("\n", messages);
        _pendingOk |= anyOk;
        OutcomeReady?.Invoke();
    }
}
