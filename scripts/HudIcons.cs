using Godot;

/// <summary>
/// Static glyph drawing helpers shared by the map editor's HexPaletteButton
/// and the gameplay HUD's HudIconButton. Each method draws into the given
/// CanvasItem centered at <paramref name="center"/>, scaled relative to
/// <paramref name="radius"/> (the "design radius" of the surrounding shape,
/// usually a hex circumradius or half the button's short axis), and tinted
/// by <paramref name="modulate"/> — pass <see cref="Colors.White"/> for the
/// default look or a dimmed color for a disabled button.
/// </summary>
public static class HudIcons
{
    private static readonly Color Outline = new Color(0f, 0f, 0f, 1f);
    private const float OutlineWidth = 1.5f;
    private const float StrokeWidth = 3f;

    /// <summary>
    /// Gold-coin glyph for the map editor's gold-tile brush: a
    /// filled gold disc with a darker rim and an inner ring so it reads as a
    /// coin rather than a plain color swatch (which would be mistaken for a
    /// player-land color).
    /// </summary>
    public static void DrawGold(CanvasItem t, Vector2 center, float radius, Color modulate)
    {
        Color gold = new Color(1f, 0.84f, 0.0f, 1f) * modulate;
        Color rim = new Color(0.70f, 0.52f, 0.05f, 1f) * modulate;
        float r = radius * 0.72f;
        t.DrawCircle(center, r, gold);
        t.DrawArc(center, r, 0f, Mathf.Tau, 32, rim, OutlineWidth * 1.5f);
        t.DrawArc(center, r * 0.6f, 0f, Mathf.Tau, 24, rim, OutlineWidth);
    }

    public static void DrawTree(CanvasItem t, Vector2 center, float radius, Color modulate)
    {
        float r = radius * 0.65f;
        var canopy = new Vector2[]
        {
            center + new Vector2(0f, -r),
            center + new Vector2(r * 0.85f, r * 0.4f),
            center + new Vector2(-r * 0.85f, r * 0.4f),
        };
        t.DrawColoredPolygon(canopy, BoardPalette.ForestCanopy * modulate);
        for (int i = 0; i < 3; i++)
        {
            t.DrawLine(canopy[i], canopy[(i + 1) % 3], Outline * modulate, OutlineWidth);
        }

        float tw = r * 0.18f;
        float ttop = r * 0.4f;
        float tbot = r * 0.75f;
        var trunk = new Vector2[]
        {
            center + new Vector2(-tw, ttop),
            center + new Vector2(tw, ttop),
            center + new Vector2(tw, tbot),
            center + new Vector2(-tw, tbot),
        };
        t.DrawColoredPolygon(trunk, BoardPalette.ForestTrunk * modulate);
        for (int i = 0; i < 4; i++)
        {
            t.DrawLine(trunk[i], trunk[(i + 1) % 4], Outline * modulate, OutlineWidth);
        }
    }

    // Apex, baseR, baseL of a peak of half-extent `r` centered at `center`.
    // The mountain shape for the editor brush button. Only the palette button
    // uses this peak; the on-map mountain tile renders as a black ring channel.
    public static Vector2[] MountainPeakVerts(Vector2 center, float r) => new[]
    {
        center + new Vector2(0f, -0.85f * r),
        center + new Vector2(0.82f * r, 0.62f * r),
        center + new Vector2(-0.82f * r, 0.62f * r),
    };

    /// <summary>
    /// Geometry for the "economy warning" badge: an equilateral up-pointing
    /// triangle of radius <paramref name="r"/> centered at
    /// <paramref name="center"/>, plus the exclamation mark (a vertical bar quad
    /// and a dot). Shared by the HUD bankrupt-toast badge
    /// (<c>HudView.TriangleWarningBadge</c>) and the on-map capital badge
    /// (<c>HexMapView.DrawWarningBadgeAt</c>) so the two read identically; each
    /// caller draws the returned verts in its own mode (immediate <c>_Draw</c>
    /// vs <c>Node2D</c> children).
    /// </summary>
    public static (Vector2[] triangle, Vector2[] bar, Vector2 dotCenter, float dotRadius)
        WarningBadgeGeometry(Vector2 center, float r)
    {
        const float Sqrt3Over2 = 0.8660254f;
        Vector2[] triangle =
        {
            center + new Vector2(0f, -r),
            center + new Vector2( r * Sqrt3Over2, r * 0.5f),
            center + new Vector2(-r * Sqrt3Over2, r * 0.5f),
        };
        float barHalf = r * 0.11f;
        float barTop = center.Y - r * 0.40f;
        float barBottom = center.Y + r * 0.05f;
        Vector2[] bar =
        {
            new Vector2(center.X - barHalf, barTop),
            new Vector2(center.X + barHalf, barTop),
            new Vector2(center.X + barHalf, barBottom),
            new Vector2(center.X - barHalf, barBottom),
        };
        Vector2 dotCenter = new Vector2(center.X, center.Y + r * 0.28f);
        return (triangle, bar, dotCenter, r * 0.11f);
    }

    /// <summary>
    /// Mountain glyph for the editor's mountain brush button: a
    /// solid grey outlined peak (no snow cap), drawn in opaque grey so it reads
    /// against the button's dark slate backdrop. Reads distinctly from the gold
    /// coin and the conifer tree. (Only the palette button uses this peak; the
    /// on-map mountain tile is a black ring channel.)
    /// </summary>
    public static void DrawMountain(CanvasItem t, Vector2 center, float radius, Color modulate)
    {
        float r = radius * 0.9f;
        Vector2[] verts = MountainPeakVerts(center, r);
        Color rock = new Color(0.72f, 0.72f, 0.76f, 1f) * modulate;   // grey, reads on dark slate
        Color outline = Outline * modulate;
        t.DrawColoredPolygon(verts, rock);
        for (int i = 0; i < 3; i++)
        {
            t.DrawLine(verts[i], verts[(i + 1) % 3], outline, OutlineWidth);
        }
    }

    public static void DrawCapital(CanvasItem t, Vector2 center, float radius, Color modulate)
        => DrawStar(t, center, radius * 0.65f, modulate);

    // Five-point gold star (ten alternating outer/inner vertices, first point
    // up), filled gold then outlined. Shared by the capital and
    // next-territory glyphs; `inner` is a fixed 0.4 of `outer`.
    private static void DrawStar(CanvasItem t, Vector2 center, float outer, Color modulate)
    {
        float inner = outer * 0.4f;
        var verts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float angle = -Mathf.Pi / 2f + i * Mathf.Pi / 5f;
            float r = (i % 2 == 0) ? outer : inner;
            verts[i] = center + new Vector2(r * Mathf.Cos(angle), r * Mathf.Sin(angle));
        }
        t.DrawColoredPolygon(verts, new Color(0.97f, 0.80f, 0.22f, 1f) * modulate);
        for (int i = 0; i < 10; i++)
        {
            t.DrawLine(verts[i], verts[(i + 1) % 10], Outline * modulate, OutlineWidth);
        }
    }

    /// <summary>
    /// "Next unit in territory" glyph: a single Recruit-style ring (line
    /// only, matching <see cref="DrawUnit"/>'s recruit ring scale) shifted
    /// down to make room for the math-vector arrow above. Stroke-only so
    /// it inverts on the CTA stylebox like other line glyphs.
    /// </summary>
    public static void DrawNextUnit(CanvasItem t, Vector2 center, float radius, Color stroke)
    {
        // Rightward-facing arrow centered on the button: horizontal shaft +
        // filled triangular arrowhead. Sized to fill the button at the same
        // scale as the EndTurn triangle / undo arrows so the icon family
        // reads at one stroke weight.
        float r = radius * 0.85f;
        float headLen = r * 0.55f;
        float headHalf = r * 0.45f;
        Vector2 tail = center + new Vector2(-r, 0f);
        Vector2 baseMid = center + new Vector2(r - headLen, 0f);
        Vector2 apex = center + new Vector2(r, 0f);
        Vector2 baseUp = baseMid + new Vector2(0f, -headHalf);
        Vector2 baseDown = baseMid + new Vector2(0f, +headHalf);
        t.DrawLine(tail, baseMid, stroke, StrokeWidth);
        t.DrawColoredPolygon(new[] { apex, baseUp, baseDown }, stroke);
    }

    /// <summary>
    /// "Next active territory" glyph: a gold capital star centered on the
    /// button. Star uses <paramref name="modulate"/> for disabled dimming.
    /// </summary>
    public static void DrawNextTerritory(CanvasItem t, Vector2 center, float radius, Color stroke, Color modulate)
        => DrawStar(t, center, radius * 0.82f, modulate);

    public static void DrawHand(CanvasItem t, Vector2 center, float radius, Color modulate)
    {
        Color fill = new Color(0.93f, 0.78f, 0.62f, 1f) * modulate;
        Color outline = Outline * modulate;
        float r = radius * 0.55f;

        float palmHalfW = r * 0.55f;
        float palmTop = -r * 0.05f;
        float palmBot = r * 0.85f;
        Vector2[] palm =
        {
            center + new Vector2(-palmHalfW, palmTop),
            center + new Vector2(palmHalfW, palmTop),
            center + new Vector2(palmHalfW, palmBot),
            center + new Vector2(-palmHalfW, palmBot),
        };
        t.DrawColoredPolygon(palm, fill);
        for (int i = 0; i < 4; i++)
        {
            t.DrawLine(palm[i], palm[(i + 1) % 4], outline, OutlineWidth);
        }

        float fingerW = r * 0.22f;
        float fingerTop = -r * 0.95f;
        float fingerBot = palmTop + r * 0.02f;
        float[] fingerCenters =
        {
            -palmHalfW + fingerW * 0.6f,
            -fingerW * 0.6f,
            fingerW * 0.6f,
            palmHalfW - fingerW * 0.6f,
        };
        foreach (float fx in fingerCenters)
        {
            Vector2[] finger =
            {
                center + new Vector2(fx - fingerW * 0.5f, fingerTop),
                center + new Vector2(fx + fingerW * 0.5f, fingerTop),
                center + new Vector2(fx + fingerW * 0.5f, fingerBot),
                center + new Vector2(fx - fingerW * 0.5f, fingerBot),
            };
            t.DrawColoredPolygon(finger, fill);
            for (int i = 0; i < 4; i++)
            {
                t.DrawLine(finger[i], finger[(i + 1) % 4], outline, OutlineWidth);
            }
        }

        Vector2 thumbBase = center + new Vector2(-palmHalfW, palmTop + r * 0.25f);
        Vector2 thumbTip = thumbBase + new Vector2(-r * 0.55f, -r * 0.32f);
        Vector2 thumbDir = (thumbTip - thumbBase).Normalized();
        Vector2 thumbPerp = new Vector2(-thumbDir.Y, thumbDir.X) * (r * 0.18f);
        Vector2[] thumb =
        {
            thumbBase + thumbPerp,
            thumbTip + thumbPerp,
            thumbTip - thumbPerp,
            thumbBase - thumbPerp,
        };
        t.DrawColoredPolygon(thumb, fill);
        for (int i = 0; i < 4; i++)
        {
            t.DrawLine(thumb[i], thumb[(i + 1) % 4], outline, OutlineWidth);
        }
    }

    public static void DrawTower(CanvasItem t, Vector2 center, float radius, Color modulate)
    {
        float r = radius * 0.55f;
        float halfW = r;
        float top = -r;
        float bot = r * 0.85f;
        float merlonH = r * 0.35f;
        float merlonW = halfW * 0.4f;
        var verts = new Vector2[]
        {
            center + new Vector2(-halfW, bot),
            center + new Vector2(-halfW, top + merlonH),
            center + new Vector2(-halfW, top),
            center + new Vector2(-halfW + merlonW, top),
            center + new Vector2(-halfW + merlonW, top + merlonH),
            center + new Vector2(-merlonW * 0.5f, top + merlonH),
            center + new Vector2(-merlonW * 0.5f, top),
            center + new Vector2(merlonW * 0.5f, top),
            center + new Vector2(merlonW * 0.5f, top + merlonH),
            center + new Vector2(halfW - merlonW, top + merlonH),
            center + new Vector2(halfW - merlonW, top),
            center + new Vector2(halfW, top),
            center + new Vector2(halfW, top + merlonH),
            center + new Vector2(halfW, bot),
        };
        t.DrawColoredPolygon(verts, new Color(0.72f, 0.72f, 0.76f, 1f) * modulate);
        for (int i = 0; i < verts.Length; i++)
        {
            t.DrawLine(verts[i], verts[(i + 1) % verts.Length], Outline * modulate, OutlineWidth);
        }
    }

    /// <summary>
    /// Concentric rings matching HexMapView's in-map unit glyphs:
    /// Recruit=1 ring, Soldier=2, Captain=3, Commander=3+center dot. Scaled
    /// larger than the in-map version so the inner rings stay legible at
    /// HUD-button size (the 0.20/0.37/0.55 hex proportions are too tight
    /// at 44px).
    /// </summary>
    public static void DrawUnit(CanvasItem t, Vector2 center, float radius, UnitLevel level, Color stroke)
    {
        float outerR = radius * 0.78f;
        float middleR = radius * 0.54f;
        float innerR = radius * 0.30f;
        float dotR = radius * 0.13f;

        DrawRing(t, center, outerR, stroke);
        if (level >= UnitLevel.Soldier) DrawRing(t, center, middleR, stroke);
        if (level >= UnitLevel.Captain) DrawRing(t, center, innerR, stroke);
        if (level >= UnitLevel.Commander) DrawFilledDisc(t, center, dotR, stroke);
    }

    private static void DrawRing(CanvasItem t, Vector2 center, float radius, Color stroke)
    {
        const int segments = 28;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.Tau / segments;
            float a1 = (i + 1) * Mathf.Tau / segments;
            Vector2 p0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
            Vector2 p1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
            t.DrawLine(p0, p1, stroke, StrokeWidth);
        }
    }

    private static void DrawFilledDisc(CanvasItem t, Vector2 center, float radius, Color fill)
    {
        const int segments = 16;
        var verts = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            verts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        t.DrawColoredPolygon(verts, fill);
    }

    /// <summary>
    /// Cartoon speech bubble: a rounded-rect body with a tail dropping
    /// from the lower-left and three dots (ellipsis) inside to read as
    /// "text". Stroke-only so it flips white-on-dark / black-on-CTA like
    /// the other line glyphs. Used by the tutorial recorder's Add Text
    /// button (HudIcon.AddText).
    /// </summary>
    public static void DrawSpeechBubble(CanvasItem t, Vector2 center, float radius, Color stroke)
    {
        float halfW = radius * 0.66f;
        float halfH = radius * 0.46f;
        float corner = radius * 0.22f;
        // Body sits a touch high so the tail has room below it.
        Vector2 body = center + new Vector2(0f, -radius * 0.14f);

        Vector2[] outline = RoundedRectPoints(body, halfW, halfH, corner);
        for (int i = 0; i < outline.Length; i++)
        {
            t.DrawLine(outline[i], outline[(i + 1) % outline.Length], stroke, StrokeWidth);
        }

        // Tail: a short open "V" hanging off the lower-left edge.
        Vector2 tailRoot = body + new Vector2(-halfW * 0.18f, halfH);
        Vector2 tailHeel = body + new Vector2(-halfW * 0.58f, halfH);
        Vector2 tailTip = body + new Vector2(-halfW * 0.62f, halfH + radius * 0.40f);
        t.DrawLine(tailRoot, tailTip, stroke, StrokeWidth);
        t.DrawLine(tailTip, tailHeel, stroke, StrokeWidth);

        // Ellipsis dots — "…" — to signal text content.
        float dotR = radius * 0.075f;
        float gap = radius * 0.30f;
        for (int i = -1; i <= 1; i++)
        {
            DrawFilledDisc(t, body + new Vector2(i * gap, 0f), dotR, stroke);
        }
    }

    /// <summary>
    /// Closed polyline approximating a rounded rectangle centered at
    /// <paramref name="center"/>, half-extents <paramref name="halfW"/> ×
    /// <paramref name="halfH"/>, with quarter-circle corners of radius
    /// <paramref name="corner"/>. Corners are sampled so consecutive
    /// points can be stroked with DrawLine.
    /// </summary>
    private static Vector2[] RoundedRectPoints(Vector2 center, float halfW, float halfH, float corner)
    {
        const int perCorner = 4;
        var pts = new System.Collections.Generic.List<Vector2>((perCorner + 1) * 4);
        // Corner centers (TR, BR, BL, TL) with their arc start angles.
        (Vector2 c, float start)[] corners =
        {
            (center + new Vector2(halfW - corner, -halfH + corner), -Mathf.Pi / 2f),
            (center + new Vector2(halfW - corner, halfH - corner), 0f),
            (center + new Vector2(-halfW + corner, halfH - corner), Mathf.Pi / 2f),
            (center + new Vector2(-halfW + corner, -halfH + corner), Mathf.Pi),
        };
        foreach ((Vector2 c, float start) in corners)
        {
            for (int i = 0; i <= perCorner; i++)
            {
                float a = start + (Mathf.Pi / 2f) * ((float)i / perCorner);
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * corner);
            }
        }
        return pts.ToArray();
    }

    /// <summary>
    /// Curved arrow. <paramref name="facing"/> = -1 draws an "undo" arrow
    /// (arrowhead on the left, arc opening down-right); +1 draws "redo"
    /// (arrowhead on the right, arc opening down-left). When
    /// <paramref name="doubled"/> is true, draws a second smaller arrow
    /// nested below the main one to convey "all the way" (Undo Turn /
    /// Redo All) vs the single-step variant.
    /// </summary>
    public static void DrawCurvedArrow(CanvasItem t, Vector2 center, float radius, Color stroke, int facing, bool doubled)
    {
        if (doubled)
        {
            // Outer + inner concentric arrows pointing the same way.
            // Radii chosen so the inner arc's arrowhead clears the outer
            // arc by a comfortable gap (arrowheads sweep tangentially, so
            // they stay roughly on their own circle).
            DrawSingleCurvedArrow(t, center, radius * 0.85f, stroke, facing);
            DrawSingleCurvedArrow(t, center, radius * 0.42f, stroke, facing);
        }
        else
        {
            DrawSingleCurvedArrow(t, center, radius * 0.78f, stroke, facing);
        }
    }

    private static void DrawSingleCurvedArrow(CanvasItem t, Vector2 center, float radius, Color stroke, int facing)
    {
        float r = radius;
        const int segments = 18;

        // Three-quarter arc swept clockwise from one o'clock around to about
        // eight o'clock (the "tail" of the arrow rests at the open end).
        // Mirror horizontally for redo by flipping the X of every point.
        float startAng = -Mathf.Pi * 0.55f;
        float endAng = Mathf.Pi * 0.95f;
        Vector2 prev = ArcPoint(center, r, startAng, facing);
        for (int i = 1; i <= segments; i++)
        {
            float a = Mathf.Lerp(startAng, endAng, (float)i / segments);
            Vector2 cur = ArcPoint(center, r, a, facing);
            t.DrawLine(prev, cur, stroke, StrokeWidth);
            prev = cur;
        }

        // Arrowhead at the start of the arc, pointing roughly tangent to
        // the arc direction at that point (i.e. outward-down for undo,
        // outward-down for redo via the mirrored facing).
        Vector2 tip = ArcPoint(center, r, startAng, facing);
        Vector2 tangent = new Vector2(-Mathf.Sin(startAng), Mathf.Cos(startAng));
        if (facing < 0) tangent.X = -tangent.X;
        // Arrow points "backwards" along the arc (away from where the arc continues).
        Vector2 arrowDir = -tangent.Normalized();
        Vector2 perp = new Vector2(-arrowDir.Y, arrowDir.X);
        float headLen = r * 0.55f;
        float headHalf = r * 0.30f;
        Vector2 baseMid = tip + arrowDir * 0f;
        Vector2 ahTip = tip + arrowDir * headLen;
        Vector2 ahL = baseMid + perp * headHalf;
        Vector2 ahR = baseMid - perp * headHalf;
        var head = new Vector2[] { ahTip, ahL, ahR };
        t.DrawColoredPolygon(head, stroke);
    }

    private static Vector2 ArcPoint(Vector2 center, float radius, float angle, int facing)
    {
        float x = Mathf.Cos(angle) * radius;
        float y = Mathf.Sin(angle) * radius;
        if (facing < 0) x = -x;
        return center + new Vector2(x, y);
    }

    /// <summary>
    /// Filled right-pointing triangle (play / next-turn symbol). The
    /// triangle is painted in <paramref name="fill"/>; pass a high-contrast
    /// color for the surrounding button bg.
    /// </summary>
    public static void DrawEndTurnTriangle(CanvasItem t, Vector2 center, float radius, Color fill)
    {
        float r = radius * 0.68f;
        Vector2 a = center + new Vector2(r, 0f);
        Vector2 b = center + new Vector2(-r * 0.55f, -r * 0.85f);
        Vector2 c = center + new Vector2(-r * 0.55f, r * 0.85f);
        var verts = new Vector2[] { a, b, c };
        t.DrawColoredPolygon(verts, fill);
    }

    /// <summary>
    /// Eight-tooth gear (settings glyph) with a hollow centre. The body is
    /// a stone-grey fill so it visually echoes the in-game tower's palette.
    /// </summary>
    public static void DrawGear(CanvasItem t, Vector2 center, float radius, Color modulate)
    {
        Color fill = new Color(0.72f, 0.72f, 0.76f, 1f) * modulate;
        Color stroke = Outline * modulate;
        const int teeth = 8;
        float outerR = radius * 0.70f;
        float innerR = outerR * 0.78f;
        const int subdivisionsPerTooth = 4;
        int n = teeth * subdivisionsPerTooth;
        var verts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            int phase = i % subdivisionsPerTooth;
            // 0,1 sit on the tooth top; 2,3 sit in the valley between teeth.
            float r = (phase == 0 || phase == 1) ? outerR : innerR;
            verts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        t.DrawColoredPolygon(verts, fill);
        for (int i = 0; i < n; i++)
        {
            t.DrawLine(verts[i], verts[(i + 1) % n], stroke, OutlineWidth);
        }
        // Hollow centre — fill with the same modulated outline color so the
        // hole reads against the gear body regardless of button background.
        float holeR = outerR * 0.32f;
        const int holeSegments = 20;
        var hole = new Vector2[holeSegments];
        for (int i = 0; i < holeSegments; i++)
        {
            float a = i * Mathf.Tau / holeSegments;
            hole[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * holeR;
        }
        t.DrawColoredPolygon(hole, stroke);
    }

    // Six-sided die — used by the map editor's "Generate map" button.
    // Rendered edge-on as a standard isometric projection so the icon
    // reads as 3D: top face (1 pip), right face (2 pips), front face
    // (3 pips). Stroke-only, no fill, so the underlying button bg
    // shows through.
    //
    // Iso geometry: project a unit cube with axes
    //   +x → screen (cos 30°, sin 30°), +y → (0, -1), +z → (-cos 30°, sin 30°)
    // The eight cube corners collapse to seven distinct screen points
    // (back-bottom corner is hidden behind the central front-top D);
    // the silhouette is a regular hexagon (A B E G F C) and three
    // internal cube edges all meet at D.
    public static void DrawDie(CanvasItem t, Vector2 center, float radius, Color stroke)
    {
        float s = radius * 0.78f;     // half-edge of the cube in screen units
        float sx = s * 0.8660254f;    // s * cos 30°
        float sy = s * 0.5f;          // s * sin 30°
        // Visible cube vertices (A B C are top, D is central, E F G are bottom).
        Vector2 A = center + new Vector2(   0f, -s);
        Vector2 B = center + new Vector2( sx,  -sy);
        Vector2 C = center + new Vector2(-sx,  -sy);
        Vector2 D = center;
        Vector2 E = center + new Vector2( sx,   sy);
        Vector2 F = center + new Vector2(-sx,   sy);
        Vector2 G = center + new Vector2(   0f,  s);

        // Hex silhouette + the three internal cube edges meeting at D.
        Vector2[] silhouette = new[] { A, B, E, G, F, C, A };
        t.DrawPolyline(silhouette, stroke, OutlineWidth, antialiased: true);
        t.DrawLine(D, B, stroke, OutlineWidth, antialiased: true);
        t.DrawLine(D, C, stroke, OutlineWidth, antialiased: true);
        t.DrawLine(D, G, stroke, OutlineWidth, antialiased: true);

        // Per-face bilinear interpolation from face-local (u, v) ∈ [0, 1]
        // to screen position, with (0, 0) at the face's "top-left" in
        // viewed-flat coords. Pips on each face use this to sit at
        // standard die-face grid positions (0.5, 0.5 for center,
        // 0.25 / 0.75 for corners).
        Vector2 PipTop(float u, float v) =>
            (1 - u) * (1 - v) * A + u * (1 - v) * B + (1 - u) * v * C + u * v * D;
        Vector2 PipRight(float u, float v) =>
            (1 - u) * (1 - v) * B + u * (1 - v) * D + (1 - u) * v * E + u * v * G;
        Vector2 PipFront(float u, float v) =>
            (1 - u) * (1 - v) * C + u * (1 - v) * D + (1 - u) * v * F + u * v * G;

        float pipR = radius * 0.09f;

        // Top face: 1 pip, dead center.
        t.DrawCircle(PipTop(0.5f, 0.5f), pipR, stroke);

        // Right face: 2 pips on the back-top → front-bottom diagonal.
        t.DrawCircle(PipRight(0.3f, 0.3f), pipR, stroke);
        t.DrawCircle(PipRight(0.7f, 0.7f), pipR, stroke);

        // Front face (left half of the icon): 3 pips on the diagonal.
        t.DrawCircle(PipFront(0.3f, 0.3f), pipR, stroke);
        t.DrawCircle(PipFront(0.5f, 0.5f), pipR, stroke);
        t.DrawCircle(PipFront(0.7f, 0.7f), pipR, stroke);
    }
}
