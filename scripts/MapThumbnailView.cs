// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Live map preview for the New Game map-setup page. Renders the
/// <em>real</em> board into a hidden offscreen <see cref="SubViewport"/> and
/// snapshots it to a static <see cref="ImageTexture"/> shown in a child
/// <see cref="TextureRect"/> — pixel-identical to what Start Game produces, but
/// the heavy <see cref="HexMapView"/> only renders on change and the displayed
/// thumbnail is a cheap texture.
///
/// Two sources, both yielding a full <see cref="GameState"/> (so neutral / gold
/// / mountain tiles all preview correctly):
/// <list type="bullet">
///   <item><see cref="RequestRandom"/> — the seed's procedural board via the
///   shared <see cref="ProceduralGame.Build"/> the play scene uses.</item>
///   <item><see cref="RequestMap"/> — a map-editor-generated map loaded
///   synchronously via <see cref="SaveStore.LoadMap"/>.</item>
///   <item><see cref="RequestSlot"/> — a saved game's board loaded via
///   <see cref="SaveStore.LoadSlot"/> (the Load Game dialog).</item>
/// </list>
/// Requests are coalesced by a token so rapid seed typing only snapshots the
/// latest. Instrumented under <see cref="Log.LogCategory.Display"/>.
/// </summary>
public partial class MapThumbnailView : Control
{
    // Board dimensions the play scene uses for a procedural game (HexMapView's
    // exported defaults; see Main._Ready). Kept in sync so the preview matches
    // Start Game exactly.
    private const int BoardCols = 30;
    private const int BoardRows = 20;
    private const float PreviewHexSize = 48f;

    // Sharpness: render the offscreen board at the thumbnail's true on-screen
    // pixel size (logical size × the window ContentScaleFactor that DisplayScale
    // sets) × a supersample factor, then downscale into the TextureRect. The
    // GLES3 compatibility renderer has no 2D MSAA, so this SSAA is what
    // anti-aliases the board outlines and tiny glyphs. Clamped so one snapshot
    // never allocates an absurd texture.
    private const float Supersample = 3.0f;
    private const int MaxRenderClamp = 1600;

    // Fraction of the snapshot height cropped off the TOP to turn the top
    // row's hex zig-zag into a clean straight edge (the other edges read
    // straight already). Empirically tuned against the rendered hex size.
    private const float TopCropFraction = 0.08f;

    private SubViewport _viewport = null!;
    private HexMapView _map = null!;
    private TextureRect _display = null!;
    private bool _mapInitialized;
    private int _renderToken;

    private SaveStore? _saveStore;

    public override void _Ready()
    {
        // The visible thumbnail: a texture rect that fills this control and
        // letterboxes the board snapshot without distortion.
        _display = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            // Mip-linear so the supersampled snapshot downsamples cleanly into
            // the on-screen rect (no shimmer / aliasing on the shrink).
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        };
        _display.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_display);

        // Hidden offscreen viewport that renders the real board once per change.
        // TransparentBg so any letterbox margin shows the page behind the
        // TextureRect rather than an opaque clear color.
        _viewport = new SubViewport
        {
            // Placeholder; SetViewportToGridAspect resizes it (DPI-aware) before
            // the first snapshot.
            Size = new Vector2I(256, 256),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            TransparentBg = true,
            // It's a preview — never route gameplay input into it.
            GuiDisableInput = true,
            HandleInputLocally = false,
        };
        AddChild(_viewport);

        _map = new HexMapView
        {
            Cols = BoardCols,
            Rows = BoardRows,
            HexSize = PreviewHexSize,
        };
    }

    /// <summary>Wire the store used to load map-editor-generated maps. Must be
    /// called before <see cref="RequestMap"/>.</summary>
    public void SetSaveStore(SaveStore store) => _saveStore = store;

    /// <summary>Preview the board the given seed would procedurally generate,
    /// mirroring the New Game map densities so the preview matches Start Game.</summary>
    public void RequestRandom(int seed) =>
        RequestRandom(seed, new MapGenOptions(
            TreeDensity: GameSettings.TreeDensity,
            MountainDensity: GameSettings.MountainDensity,
            GoldDensity: GameSettings.GoldDensity,
            ClumpingFactor: GameSettings.ClumpingFactor,
            NeutralDensity: GameSettings.NeutralDensity));

    /// <summary>Preview the board for an explicit set of generation options —
    /// used by the campaign confirm sheet, which derives its level's fixed
    /// terrain features rather than reading the freeform toggles. Uses the
    /// freeform roster (<see cref="Player.BuildRoster"/>).</summary>
    public void RequestRandom(int seed, MapGenOptions options) =>
        RequestRandom(seed, options, Player.BuildRoster(), GameSettings.Mode);

    /// <summary>Preview the board for an explicit roster — so a campaign level's
    /// preview shows its actual 2–6 player color set, not the freeform
    /// roster. Uses the freeform menu mode.</summary>
    public void RequestRandom(int seed, MapGenOptions options, IReadOnlyList<Player> roster) =>
        RequestRandom(seed, options, roster, GameSettings.Mode);

    /// <summary>Preview the board for an explicit roster AND mode — the campaign
    /// confirm sheet passes the level's own mode (<c>ModeForLevel</c>) so a fog
    /// level previews fogged regardless of the menu's last selection.</summary>
    public void RequestRandom(int seed, MapGenOptions options, IReadOnlyList<Player> roster, GameMode mode)
    {
        int token = ++_renderToken;
        Log.Debug(Log.LogCategory.Display,
            $"MapThumbnail: request random seed={SeedFormat.ToHex(seed)} token={token} " +
            $"players={roster.Count} mode={mode} trees={options.TreeDensity} " +
            $"mtn={options.MountainDensity} gold={options.GoldDensity}");
        _ = RenderAsync(
            () => ProceduralGame.Build(BoardCols, BoardRows, roster, seed, options, mode: mode),
            $"random seed={SeedFormat.ToHex(seed)}", token);
    }

    /// <summary>Preview a saved game's board by save-slot name (the Load Game
    /// dialog). Loads the in-progress save from the save directory,
    /// versus <see cref="RequestMap"/>'s maps directory.</summary>
    public void RequestSlot(string slotName)
    {
        int token = ++_renderToken;
        Log.Debug(Log.LogCategory.Display,
            $"MapThumbnail: request slot=\"{slotName}\" token={token}");
        _ = RenderAsync(() =>
        {
            if (_saveStore == null)
                throw new InvalidOperationException("MapThumbnailView.SetSaveStore not called");
            return _saveStore.LoadSlot(slotName).State;
        }, $"slot=\"{slotName}\"", token);
    }

    /// <summary>Preview a map-editor-generated map by slot name.</summary>
    public void RequestMap(string mapName)
    {
        int token = ++_renderToken;
        Log.Debug(Log.LogCategory.Display,
            $"MapThumbnail: request map=\"{mapName}\" token={token}");
        _ = RenderAsync(() =>
        {
            if (_saveStore == null)
                throw new InvalidOperationException("MapThumbnailView.SetSaveStore not called");
            return _saveStore.LoadMap(mapName).State;
        }, $"map=\"{mapName}\"", token);
    }

    private async Task RenderAsync(Func<GameState> build, string label, int token)
    {
        GameState state;
        try
        {
            state = build();
        }
        catch (Exception ex)
        {
            // A missing/corrupt map shouldn't crash the menu — log and bail.
            Log.Warn(Log.LogCategory.Display, $"MapThumbnail: build failed for {label}: {ex.Message}");
            return;
        }

        int tiles = 0;
        foreach (HexTile _ in state.Grid.Tiles) tiles++;

        // Size the offscreen viewport to the nominal GRID aspect in the current
        // screen orientation — seed-independent, so re-rolls never resize the
        // frame. A portrait orientation gives the viewport a tall aspect, which
        // makes HexMapView rotate the board −90° to match the in-game portrait
        // map. Computed before the map enters the tree so its _Ready frames at
        // the right size/rotation from the first render.
        SetViewportToGridAspect();

        if (!_mapInitialized)
        {
            _map.Init(state);
            _viewport.AddChild(_map); // _Ready builds visuals + computes zoom
            _mapInitialized = true;
        }
        else
        {
            _map.ReloadState(state, animateNewOccupants: false);
        }
        // Fog Of War: preview the board as the human will actually see it — their
        // start visible, the rest fogged. Pushed before occupants so the
        // occupant pass honours visibility; null (no-op) for non-fog maps.
        _map.ShowFog(VisibilityRules.BuildProjection(state));
        _map.RefreshOccupantVisuals(currentPlayer: null, state.Treasury,
            new HashSet<HexCoord>());

        // Let any SubViewport SizeChanged → RecomputeZoomLevels/ResolveRotation
        // settle, then frame the full grid rectangle (NOT the per-seed land box)
        // so the board stays at a fixed scale/position across re-rolls.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (token != _renderToken) return; // superseded by a newer request
        _map.FrameWholeGrid();

        // Render exactly one frame, wait for the GPU to finish, then snapshot.
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (token != _renderToken) return;

        // Crop the jagged hex-tessellation border off all four edges: cutting
        // the bitmap with a straight line gives clean straight edges (the board
        // exactly fills the viewport, so a ~1-hex inset clears the deepest
        // zig-zag valley, leaving solid tiles at the cropped edge).
        Image img = CropHexBorder(_viewport.GetTexture().GetImage());
        img.GenerateMipmaps(); // so the mip-linear TextureRect downsamples cleanly
        _display.Texture = ImageTexture.CreateFromImage(img);
        Log.Debug(Log.LogCategory.Display,
            $"MapThumbnail: snapshot taken for {label} tiles={tiles} " +
            $"vp={_viewport.Size.X}x{_viewport.Size.Y} token={token}");
    }

    /// <summary>Trim the wavy hex-tessellation row off the TOP edge to a clean
    /// straight line (the board fills the viewport; the top row's pointy-top
    /// hexes leave a zig-zag there). Cropping ~one rendered hex down lands the
    /// cut in solid tiles past the valleys. Other edges read straight already.</summary>
    private Image CropHexBorder(Image full)
    {
        int inset = Mathf.CeilToInt(full.GetHeight() * TopCropFraction);
        int h = full.GetHeight() - inset;
        if (inset <= 0 || h <= 0) return full;
        return full.GetRegion(new Rect2I(0, inset, full.GetWidth(), h));
    }

    private void SetViewportToGridAspect()
    {
        // Nominal grid pixel box (depends only on Cols/Rows/HexSize, not the
        // map content), so the aspect is identical for every seed → a stable
        // frame. OrientedFit swaps to a tall aspect in portrait so HexMapView
        // rotates the board to match the in-game portrait orientation.
        Vector2 grid = _map.PixelSize;
        bool portrait = ScreenLayout.Resolve(
            GetViewportRect().Size.X, GetViewportRect().Size.Y) == ScreenOrientation.Portrait;

        // Render budget = this control's on-screen pixel size (logical × the
        // window ContentScaleFactor DisplayScale set) × supersample, clamped.
        // Falls back to the clamp if we're not laid out yet (Size ~ 0).
        float dpr = GetWindow().ContentScaleFactor;
        if (dpr <= 0f) dpr = 1f;
        float budgetW = Size.X > 1f ? Size.X * dpr * Supersample : MaxRenderClamp;
        float budgetH = Size.Y > 1f ? Size.Y * dpr * Supersample : MaxRenderClamp;
        budgetW = Mathf.Min(budgetW, MaxRenderClamp);
        budgetH = Mathf.Min(budgetH, MaxRenderClamp);

        (float w, float h) = ThumbnailLayout.OrientedFit(grid.X, grid.Y, portrait, budgetW, budgetH);
        var size = new Vector2I(
            Mathf.Max(1, Mathf.RoundToInt(w)),
            Mathf.Max(1, Mathf.RoundToInt(h)));
        if (_viewport.Size != size) _viewport.Size = size;
    }
}
