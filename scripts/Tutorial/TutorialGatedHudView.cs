using System;

/// <summary>
/// IHudView wrapper for tutorial Preview. Same shape as
/// <see cref="TutorialGatedHexMapView"/>: subscribes to a real
/// IHudView, gates input events that map to player-action beats
/// (EndTurnClicked / BuyPeasantClicked / BuildTowerClicked),
/// passes through other inputs (Undo/Territory cycling/etc. — dev
/// affordances during Preview), and delegates output methods.
///
/// Phase 3c: only EndTurnClicked has a corresponding beat type
/// (EndTurnBeat). BuyPeasant / BuildTower are gated to "always
/// reject" since no matching beats exist yet. Phase 4 / 6 swap the
/// rejection paths for real TutorialValidator calls.
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
        _real.CancelActionPressed += () => CancelActionPressed?.Invoke();
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
        // Phase 3c: no BuyPeasantBeat exists. Always reject.
        _player.NotifyRejected("Buy Peasant");
    }

    private void OnRealBuildTowerClicked()
    {
        // Phase 3c: no BuildTowerBeat exists. Always reject.
        _player.NotifyRejected("Build Tower");
    }

    // --- Output methods: pure delegation ---

    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining) =>
        _real.Refresh(state, session, hasActionableRemaining);
    public void SetMapLabel(string text) => _real.SetMapLabel(text);
    public void ShowTutorialMessage(string text) => _real.ShowTutorialMessage(text);
    public void HideTutorialMessage() => _real.HideTutorialMessage();
}
