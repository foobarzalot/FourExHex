// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Runtime.InteropServices;

/// <summary>
/// Mirror <see cref="Log"/> output into Apple's unified logging system on iOS,
/// so <c>idevicesyslog</c> / Console.app / <c>os_log</c> consumers see lines
/// like <c>DisplayScale:</c> and <c>SafeArea:</c> from a tethered iPhone.
///
/// Godot's <c>GD.Print</c> on iOS uses <c>printf</c> to stdout, which iOS does
/// not capture into the system log for app processes — so without this helper
/// every <see cref="Log"/> diagnostic is invisible on device unless you're
/// running through Xcode's attached console.
///
/// We P/Invoke <c>syslog</c> from libc rather than <c>os_log</c> because
/// <c>os_log</c>'s C macro is varargs over a static format string, which is
/// awkward to marshal from C#. <c>syslog(int, const char *format, ...)</c>
/// accepts a printf-style format; passing <c>"%s"</c> as the format with the
/// message as the single argument neutralizes any <c>%</c> chars in our log
/// content. On modern iOS, syslog output is itself piped into the unified
/// logging system, so this reaches the same place Console.app does.
///
/// The DllImport binds lazily on first call, so referencing this class from
/// desktop / Android code is harmless — only iOS invocations actually try to
/// resolve <c>libc</c>'s <c>syslog</c> symbol.
/// </summary>
internal static class IosLog
{
    /// <summary>LOG_NOTICE from <c>&lt;syslog.h&gt;</c> — visible by default in
    /// Console.app without changing per-subsystem filters.</summary>
    private const int LogNotice = 5;

    [DllImport("libc", EntryPoint = "syslog")]
    private static extern void Syslog(int priority, string format, string message);

    public static void Write(string message)
    {
        Syslog(LogNotice, "%s", message);
    }
}
