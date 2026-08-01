// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// OS file dialogs for <c>.fxhmap</c> map export/import (issue #92).
/// Built on Godot's <see cref="FileDialog"/> with
/// <see cref="FileDialog.UseNativeDialog"/> on: platforms with native
/// dialog support (macOS/Windows/Linux-with-portal) get the real OS
/// picker; anywhere else Godot renders its in-tree dialog with the same
/// signals, so there is a single code path either way. Each call builds
/// a throwaway dialog node under <paramref name="host"/> and frees it on
/// close.
///
/// Test-excluded (Godot UI); the validation the chosen file flows into
/// (<see cref="MapImport"/>) is the tested part.
/// </summary>
public static class MapFileDialogs
{
    public const string Extension = ".fxhmap";
    private const string Filter = "*.fxhmap;FourExHex Map";

    /// <summary>Save-file dialog. <paramref name="onPath"/> receives the
    /// chosen absolute path with <see cref="Extension"/> guaranteed
    /// appended. Cancel just frees the dialog.</summary>
    public static void ShowExport(Node host, string defaultFileName, Action<string> onPath)
    {
        FileDialog dialog = Build(host, FileDialog.FileModeEnum.SaveFile,
            Strings.Get(StringKeys.EditorExportMap));
        dialog.CurrentFile = defaultFileName;
        dialog.FileSelected += path =>
        {
            if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                path += Extension;
            }
            Log.Debug(Log.LogCategory.Share, $"[share] export dialog -> '{path}'");
            onPath(path);
        };
        Popup(dialog, "export");
    }

    /// <summary>Open-file dialog. <paramref name="onPath"/> receives the
    /// chosen absolute path. Cancel just frees the dialog.</summary>
    public static void ShowImport(Node host, Action<string> onPath)
    {
        FileDialog dialog = Build(host, FileDialog.FileModeEnum.OpenFile,
            Strings.Get(StringKeys.MenuImportMap));
        dialog.FileSelected += path =>
        {
            Log.Debug(Log.LogCategory.Share, $"[share] import dialog -> '{path}'");
            onPath(path);
        };
        Popup(dialog, "import");
    }

    private static FileDialog Build(Node host, FileDialog.FileModeEnum mode, string title)
    {
        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = mode,
            Filters = new[] { Filter },
            Title = title,
            UseNativeDialog = true,
        };
        dialog.Canceled += () =>
        {
            Log.Debug(Log.LogCategory.Share, "[share] file dialog canceled");
        };
        // Free on any close (FileSelected fires before VisibilityChanged-
        // driven teardown; deferred so handlers finish first).
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible) dialog.CallDeferred(Node.MethodName.QueueFree);
        };
        host.AddChild(dialog);
        return dialog;
    }

    private static void Popup(FileDialog dialog, string kind)
    {
        Log.Debug(Log.LogCategory.Share,
            $"[share] {kind} dialog open (native={DisplayServer.HasFeature(
                DisplayServer.Feature.NativeDialogFile)})");
        // The in-tree fallback needs a size; the native dialog ignores it.
        dialog.PopupCentered(new Vector2I(900, 600));
    }
}
