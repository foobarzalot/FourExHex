using System;
using Godot;

/// <summary>
/// Shared on-screen-keyboard avoidance for a focused <see cref="LineEdit"/>
/// (main-menu seed field; save-name modal). Owns the
/// per-frame lift loop: while the field is focused the host polls
/// <see cref="Poll"/> each frame (Godot has no keyboard-height-changed
/// signal and the keyboard animates in), and the controller shifts the
/// host's UI up just enough that the field clears the keyboard with margin.
///
/// The host supplies <paramref name="applyLift"/> — how to translate its own
/// layout (the menu re-lays its centered surface; the save modal nudges its
/// panel's anchor offsets) — and drives <see cref="Poll"/> from its own
/// <c>_Process</c>, gated on the field's focus. The pure math lives in
/// <see cref="KeyboardAvoidance.LiftFor"/> (FourExHex.ViewMath, unit-tested);
/// this class is the Godot-side glue (keyboard height, content scale, logging).
///
/// <c>FOUREXHEX_FAKE_KB=&lt;physical px&gt;</c> simulates an on-screen keyboard
/// on desktop so the lift is testable without a device.
/// </summary>
public sealed class KeyboardLiftController
{
    private readonly LineEdit _field;
    private readonly Action<float> _applyLift;
    private readonly float _margin;
    private readonly string _label;
    private readonly float _fakeKeyboardPhysicalHeight;
    private float _currentLift;

    /// <param name="field">The focused field to keep clear of the keyboard.</param>
    /// <param name="applyLift">Host hook: translate the UI up by N logical px
    /// (0 = rest position).</param>
    /// <param name="margin">Logical-px gap to leave between the field's bottom
    /// edge and the keyboard top.</param>
    /// <param name="label">Log prefix identifying the host (e.g. "MainMenu").</param>
    public KeyboardLiftController(LineEdit field, Action<float> applyLift, float margin, string label)
    {
        _field = field;
        _applyLift = applyLift;
        _margin = margin;
        _label = label;

        string fakeKb = OS.GetEnvironment("FOUREXHEX_FAKE_KB");
        if (fakeKb.Length > 0 && float.TryParse(fakeKb, out float fakeKbHeight))
        {
            _fakeKeyboardPhysicalHeight = fakeKbHeight;
        }
    }

    /// <summary>Current applied lift in logical px (0 = unlifted). Hosts re-pass
    /// this through their layout on resize / safe-area changes so a keyboard
    /// that's up doesn't snap back down.</summary>
    public float CurrentLift => _currentLift;

    /// <summary>Recompute and apply the lift for the current keyboard height.
    /// Call once per frame while the field is focused.</summary>
    public void Poll(float viewportHeight, float contentScaleFactor)
    {
        float physicalHeight = _fakeKeyboardPhysicalHeight > 0f
            ? _fakeKeyboardPhysicalHeight
            : DisplayServer.VirtualKeyboardGetHeight();
        float logicalHeight = contentScaleFactor > 0f
            ? physicalHeight / contentScaleFactor
            : physicalHeight;
        // Measure the field's unlifted bottom edge (add back the applied lift)
        // so the lift doesn't feed back into its own input.
        float fieldBottomY = _field.GetGlobalRect().End.Y + _currentLift;
        float lift = KeyboardAvoidance.LiftFor(fieldBottomY, viewportHeight, logicalHeight, _margin);
        Set(lift);
    }

    /// <summary>Drop the lift back to rest. Call on blur, or when the host frees
    /// the field (the freed field never fires FocusExited).</summary>
    public void Reset() => Set(0f);

    private void Set(float lift)
    {
        if (Mathf.IsEqualApprox(lift, _currentLift)) return;
        _currentLift = lift;
        _applyLift(lift);
        Log.Debug(Log.LogCategory.Display, $"{_label}: keyboard lift -> {lift:0.#} logical px");
    }
}
