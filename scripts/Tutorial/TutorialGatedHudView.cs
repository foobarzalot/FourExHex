using System;

/// <summary>
/// IHudView wrapper for tutorial Preview. Same shape as
/// <see cref="TutorialGatedHexMapView"/>: subscribes to a real
/// IHudView, gates input events that map to player-action beats
/// (EndTurnClicked / BuyPeasantClicked / BuildTowerClicked),
/// passes through other inputs (Undo/Territory cycling/etc. — dev
/// affordances during Preview), and delegates output methods.
///
/// Phase 4 adds the BuyPeasant arm-or-reject path (the matching tile
/// click is gated by <see cref="TutorialGatedHexMapView"/>).
/// BuildTowerClicked stays "always reject" until Phase 6.
///
/// CancelActionPressed pass-through also calls
/// <see cref="TutorialPlayer.DisarmIfAny"/> so the player's arm state
/// stays in sync with the controller's pending-action mode.
///
/// Output methods (Refresh / SetMapLabel / ShowTutorialMessage /
/// HideTutorialMessage) delegate transparently — the controller's
/// view-update calls reach the real HUD unchanged.
/// </summary>
public sealed class TutorialGatedHudView : IHudView
{
    private readonly IHudView _real;
    private readonly TutorialPlayer _player;

    public TutorialGatedHudView(IHudView real, TutorialPlayer player)
    {
        _real = real;
        _player = player;

        // Gated input events (re-raised conditionally below).
        _real.EndTurnClicked += OnRealEndTurnClicked;
        _real.BuyPeasantClicked += OnRealBuyPeasantClicked;
        _real.BuildTowerClicked += OnRealBuildTowerClicked;

        // Cancel disarms the BuyPeasant arm in addition to passing through.
        // Out-of-order disarm (no current arm) is a no-op via DisarmIfAny.
        _real.CancelActionPressed += OnRealCancelActionPressed;

        // Pass-through input events: re-raise whatever the real fires.
        _real.UndoLastClicked += () => UndoLastClicked?.Invoke();
        _real.UndoTurnClicked += () => UndoTurnClicked?.Invoke();
        _real.RedoLastClicked += () => RedoLastClicked?.Invoke();
        _real.RedoAllClicked += () => RedoAllClicked?.Invoke();
        _real.NewGameClicked += () => NewGameClicked?.Invoke();
        _real.MainMenuClicked += () => MainMenuClicked?.Invoke();
        _real.NextTerritoryClicked += () => NextTerritoryClicked?.Invoke();
        _real.PreviousTerritoryClicked += () => PreviousTerritoryClicked?.Invoke();
        _real.NextUnitClicked += () => NextUnitClicked?.Invoke();
        _real.PreviousUnitClicked += () => PreviousUnitClicked?.Invoke();
        _real.SaveGameClicked += () => SaveGameClicked?.Invoke();
        _real.DefeatContinueClicked += () => DefeatContinueClicked?.Invoke();
        _real.ClaimVictoryWinNowClicked += () => ClaimVictoryWinNowClicked?.Invoke();
        _real.ClaimVictoryContinueClicked += () => ClaimVictoryContinueClicked?.Invoke();
    }

    public void Unbind()
    {
        _real.EndTurnClicked -= OnRealEndTurnClicked;
        _real.BuyPeasantClicked -= OnRealBuyPeasantClicked;
        _real.BuildTowerClicked -= OnRealBuildTowerClicked;
        _real.CancelActionPressed -= OnRealCancelActionPressed;
        // Pass-through lambdas can't be unsubscribed (closures don't
        // compare equal); they keep the real view alive until the
        // real view itself is freed. PreviewPane drops both the real
        // view and the wrapper at the same teardown point, so this is
        // safe — neither outlives the other.
    }

    // --- Input events ---

    public event Action? BuyPeasantClicked;
    public event Action? BuildTowerClicked;
    public event Action? UndoLastClicked;
    public event Action? UndoTurnClicked;
    public event Action? RedoLastClicked;
    public event Action? RedoAllClicked;
    public event Action? EndTurnClicked;
    public event Action? NewGameClicked;
    public event Action? MainMenuClicked;
    public event Action? NextTerritoryClicked;
    public event Action? PreviousTerritoryClicked;
    public event Action? NextUnitClicked;
    public event Action? PreviousUnitClicked;
    public event Action? CancelActionPressed;
    public event Action? SaveGameClicked;
    public event Action? DefeatContinueClicked;
    public event Action? ClaimVictoryWinNowClicked;
    public event Action? ClaimVictoryContinueClicked;

    private void OnRealEndTurnClicked()
    {
        if (_player.TryAdvanceForEndTurn())
        {
            EndTurnClicked?.Invoke();   // forward to controller
        }
        // If TryAdvanceForEndTurn returned false it has already fired
        // PlayerActionRejected — PreviewPane's subscription shows the
        // toast via _real.ShowTutorialMessage.
    }

    private void OnRealBuyPeasantClicked()
    {
        // Phase 4: arm if the next beat is BuyPeasantBeat (and we're
        // not already armed); the matching tile click is gated by
        // TutorialGatedHexMapView. TryArmBuyPeasant fires
        // PlayerActionRejected on its own when refusing.
        if (_player.TryArmBuyPeasant())
        {
            BuyPeasantClicked?.Invoke();   // forward — controller enters BuyingPeasant mode
        }
    }

    private void OnRealBuildTowerClicked()
    {
        // Phase 6 will add the BuildTowerBeat arm path. Until then, reject.
        _player.NotifyRejected("Build Tower");
    }

    private void OnRealCancelActionPressed()
    {
        // Cancel exits the controller's pending action mode (whether or
        // not the user was armed); make sure our arm state matches so
        // the next BuyPeasant click can re-arm.
        _player.DisarmIfAny();
        CancelActionPressed?.Invoke();
    }

    // --- Output methods: pure delegation ---

    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining)
    {
        // When the next scripted beat is EndTurn, force the End Turn CTA
        // styling — same visual cue ordinary play uses when the player
        // has nothing left actionable. Driven through the existing
        // hasActionableRemaining lever so we reuse HudView's
        // SetEndTurnCta path with no new HUD API.
        if (_player.NextExpectedPlayerBeat is EndTurnBeat)
        {
            hasActionableRemaining = false;
        }

        // When the next scripted beat is BuyPeasant and the dev hasn't
        // armed it yet, light up the Buy Peasant button as the next
        // action. Once they click Buy (we go armed), drop the CTA —
        // the in-mode "Click a tile..." text becomes the active cue.
        bool buyPeasantCta = _player.NextExpectedPlayerBeat is BuyPeasantBeat
                              && !_player.IsArmedForBuyPeasant;
        _real.SetBuyPeasantCta(buyPeasantCta);

        _real.Refresh(state, session, hasActionableRemaining);
    }
    public void SetMapLabel(string text) => _real.SetMapLabel(text);
    public void ShowTutorialMessage(string text) => _real.ShowTutorialMessage(text);
    public void HideTutorialMessage() => _real.HideTutorialMessage();
    public void SetBuyPeasantCta(bool isCta) => _real.SetBuyPeasantCta(isCta);
}
