// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FourExHex
using System;
using System.Collections.Generic;
using FourExHex.Controller;
using FourExHex.Model;
using Godot;

/// <summary>
/// Autoload that owns the OS-incoming-file surfaces and turns them into map
/// imports. Two native sources feed it: the godot-share plugin's
/// share-target payload (share sheet → app) and the FileOpen plugin's
/// ACTION_VIEW path (tap a .fxhmap → app). Android pushes via signals; iOS
/// only parks a pending payload, so the inbox also polls at startup and
/// ~0.25 s after every application-resume (Files-app URL delivery can lag
/// foregrounding).
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
    private const string SharePluginSingleton = "SharePlugin";
    private const string FileOpenSingleton = "FileOpen";
    private const double ResumePollDelaySeconds = 0.25;

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

        if (Engine.HasSingleton(SharePluginSingleton))
        {
            GodotObject plugin = Engine.GetSingleton(SharePluginSingleton);
            plugin.Call("set_share_target", true);
            Log.Info(Log.LogCategory.Share,
                "[share] inbox: share target enabled (SharePlugin)");
            plugin.Connect("share_received", Callable.From(
                (Godot.Collections.Dictionary payload) => HandleSharePayload(payload)));
            PollSharePlugin("cold-start");
        }

        if (Engine.HasSingleton(FileOpenSingleton))
        {
            GodotObject plugin = Engine.GetSingleton(FileOpenSingleton);
            Log.Info(Log.LogCategory.Share,
                "[share] inbox: open-with source connected (FileOpen)");
            plugin.Connect("file_open_received", Callable.From(
                (string path) => HandlePaths(new[] { path }, deleteSources: true)));
            PollFileOpen("cold-start");
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationApplicationResumed) return;
        if (!Engine.HasSingleton(SharePluginSingleton)
            && !Engine.HasSingleton(FileOpenSingleton)) return;
        // iOS parks incoming URLs slightly after foregrounding; poll on a
        // short delay instead of immediately (mirrors the plugin's own
        // GDScript wrapper).
        GetTree().CreateTimer(ResumePollDelaySeconds).Timeout += () =>
        {
            PollSharePlugin("resume");
            PollFileOpen("resume");
        };
    }

    private void PollSharePlugin(string reason)
    {
        if (!Engine.HasSingleton(SharePluginSingleton)) return;
        Godot.Collections.Dictionary payload = Engine.GetSingleton(SharePluginSingleton)
            .Call("get_received_data").AsGodotDictionary();
        if (payload.Count == 0)
        {
            Log.Debug(Log.LogCategory.Share, $"[share] inbox: {reason} poll — empty");
            return;
        }
        Log.Debug(Log.LogCategory.Share, $"[share] inbox: {reason} poll — payload");
        HandleSharePayload(payload);
    }

    private void PollFileOpen(string reason)
    {
        if (!Engine.HasSingleton(FileOpenSingleton)) return;
        string path = Engine.GetSingleton(FileOpenSingleton)
            .Call("get_pending_open_path").AsString();
        if (path.Length == 0) return;
        Log.Debug(Log.LogCategory.Share, $"[share] inbox: {reason} open-with '{path}'");
        HandlePaths(new[] { path }, deleteSources: true);
    }

    private void HandleSharePayload(Godot.Collections.Dictionary payload)
    {
        string mime = payload.TryGetValue("mime_type", out Variant m) ? m.AsString() : "";
        string[] files = payload.TryGetValue("file_paths", out Variant f)
            ? f.AsStringArray()
            : Array.Empty<string>();
        Log.Debug(Log.LogCategory.Share,
            $"[share] inbox: payload mime='{mime}' files={files.Length}");
        HandlePaths(files, deleteSources: true);
    }

    private void HandlePaths(string[] paths, bool deleteSources)
    {
        IReadOnlyList<string> accepted = ShareReceiveRules.FxhmapPaths(paths);
        HashSet<string> acceptedSet = new(accepted, StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (!acceptedSet.Contains(path))
            {
                Log.Info(Log.LogCategory.Share,
                    $"[share] inbox: ignored non-map '{path}'");
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
