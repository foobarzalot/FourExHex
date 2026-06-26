/// <summary>
/// A tile's mutually-exclusive terrain feature (issue #81). A tile carries at
/// most one of these: it is plain, a gold income hotspot, or a mountain — never
/// gold <i>and</i> mountain. Modelling the pair as one enum (rather than two
/// independent bools) makes the exclusivity structurally impossible to violate:
/// the <see cref="HexTile.IsGold"/> / <see cref="HexTile.IsMountain"/>
/// accessors are convenience views over <see cref="HexTile.Feature"/>, and
/// setting one through them retargets the single field, so the other clears
/// automatically. Orthogonal to <see cref="HexTile.Occupant"/> — a tree, grave,
/// unit, or tower may sit on a gold or mountain tile.
/// </summary>
public enum TerrainFeature
{
    /// <summary>Plain ground — no gold and no mountain.</summary>
    None = 0,

    /// <summary>A gold income hotspot (issue #45). See <see cref="HexTile.IsGold"/>.</summary>
    Gold = 1,

    /// <summary>Defensive high-ground terrain (issue #37). See <see cref="HexTile.IsMountain"/>.</summary>
    Mountain = 2,
}
