/// <summary>
/// Pure hit-test predicates for the hex map view. Godot-free (plain ints) so
/// they're unit-testable; the view converts a cursor/coord to offset
/// <c>(col, row)</c> and asks here whether it lands on the board.
/// </summary>
public static class HitTestMath
{
    /// <summary>True when offset coordinates <paramref name="col"/>,
    /// <paramref name="row"/> fall inside the <c>cols × rows</c> offset
    /// rectangle — the half-open range <c>[0,cols) × [0,rows)</c>. Used for
    /// hover/paint gating and to skip in-grid cells when baking the water rim.
    /// </summary>
    public static bool InOffsetBounds(int col, int row, int cols, int rows) =>
        col >= 0 && col < cols && row >= 0 && row < rows;
}
