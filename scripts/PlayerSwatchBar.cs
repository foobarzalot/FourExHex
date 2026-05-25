using System.Collections.Generic;
using Godot;

/// <summary>
/// HUD widget that identifies the active player by a horizontal row of
/// color swatches — one per player in movement (turn) order — instead of
/// the old colored name label. The current player's swatch is enlarged
/// and white-outlined; eliminated players are dimmed in place so the
/// movement order stays stable and you can see who is out.
///
/// Custom-drawn like <see cref="HudIconButton"/>: <see cref="_Draw"/>
/// paints the swatches and the view calls <see cref="SetPlayers"/> +
/// <see cref="QueueRedraw"/> whenever the roster/current player changes.
/// Lives in <c>_statusCluster</c>, which the HUD reparents between the
/// landscape top bar and the portrait bottom bar, so it renders the same
/// in both orientations for free.
/// </summary>
public partial class PlayerSwatchBar : Control
{
    private const float NormalSize = 26f;
    private const float CurrentSize = 38f;
    private const float Gap = 8f;
    private const float DimmedAlpha = 0.30f;

    private Color[] _colors = System.Array.Empty<Color>();
    private bool[] _eliminated = System.Array.Empty<bool>();
    private int _currentIndex = -1;
    private bool _compact;

    public void SetPlayers(IReadOnlyList<Color> colors, IReadOnlyList<bool> eliminated, int currentIndex)
    {
        int n = colors.Count;
        _colors = new Color[n];
        _eliminated = new bool[n];
        for (int i = 0; i < n; i++)
        {
            _colors[i] = colors[i];
            _eliminated[i] = i < eliminated.Count && eliminated[i];
        }
        _currentIndex = currentIndex;
        UpdateMinSize();
        QueueRedraw();
    }

    /// <summary>Compact mode (narrow bars, e.g. low-res portrait): show
    /// only the current player's swatch — there's no room for the full
    /// turn-order row. The HUD drives this from viewport width.</summary>
    public void SetCompact(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact;
        UpdateMinSize();
        QueueRedraw();
    }

    private void UpdateMinSize()
    {
        int n = _colors.Length;
        // Every slot reserves the enlarged width so the current swatch
        // (whichever it is) never clips and the bar width is stable as
        // the highlight moves between players.
        float width = _compact
            ? (n > 0 ? CurrentSize : 0f)
            : (n > 0 ? n * CurrentSize + (n - 1) * Gap : 0f);
        CustomMinimumSize = new Vector2(width, CurrentSize + 4f);
    }

    public override void _Draw()
    {
        if (_colors.Length == 0) return;

        if (_compact)
        {
            if (_currentIndex >= 0 && _currentIndex < _colors.Length)
            {
                DrawSwatch(0, _colors[_currentIndex], isCurrent: true, eliminated: false);
            }
            return;
        }

        for (int i = 0; i < _colors.Length; i++)
        {
            DrawSwatch(i, _colors[i], isCurrent: i == _currentIndex, eliminated: _eliminated[i]);
        }
    }

    private void DrawSwatch(int slot, Color color, bool isCurrent, bool eliminated)
    {
        float slotPitch = CurrentSize + Gap;
        float cy = Size.Y * 0.5f;
        float s = isCurrent ? CurrentSize : NormalSize;
        float cx = slot * slotPitch + CurrentSize * 0.5f;

        Color fill = color;
        if (eliminated && !isCurrent) fill.A = DimmedAlpha;

        var rect = new Rect2(cx - s * 0.5f, cy - s * 0.5f, s, s);
        DrawRect(rect, fill, filled: true);

        if (isCurrent)
        {
            DrawRect(rect, new Color(1f, 1f, 1f, 1f), filled: false, width: 3f);
        }
    }
}
