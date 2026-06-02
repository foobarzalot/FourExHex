#!/usr/bin/env python3
"""Bake the FourExHex app icon assets.

Emits, from the same palette + DMSerifDisplay glyph data:

1. `icon.svg` (1024x1024, transparent) -- used by Godot for macOS, iOS,
   Windows. Full-bleed pointy-top red hex with brass-gold "4X" inset.

2. `assets/icon/android_fg_432.png` (432x432, transparent) -- adaptive-icon
   foreground for Android. Same hex + "4X" but scaled so the whole hex fits
   inside the 72 dp / 288 px safe square (so any launcher mask shows the
   full hex shape, not just its red interior).

3. `assets/icon/android_bg_432.png` (432x432, opaque slate) -- adaptive-icon
   background for Android. Flat `UiPalette.BgDeep` so the area around the
   hex inside the mask reads as a warm slate frame.

4. `assets/icon/splash_1024.png` (1024x1024, opaque slate) -- shared boot
   splash for the Godot runtime (`project.godot` `boot_splash/image`) AND
   the iOS pre-engine launch storyboard (`export_presets.cfg`
   `storyboard/custom_image@2x|@3x`). Same hex + "4X" centered at 70 % of
   the canvas on a `UiPalette.BgDeep` field, so the launch screen reads as
   the in-game frame with the icon inside.

Run with `python3 tools/build_icon.py` to write all four. Re-runnable so
sizing / palette can be tweaked in code, not by hand-editing the outputs.

Requires: `pip install fonttools Pillow` (use a venv; not project deps).
"""
import argparse
import math
import sys
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.boundsPen import BoundsPen
from PIL import Image, ImageDraw, ImageFont


REPO = Path(__file__).resolve().parent.parent
FONT_PATH = REPO / "fonts" / "DMSerifDisplay-Regular.ttf"
SVG_PATH = REPO / "icon.svg"
ANDROID_DIR = REPO / "assets" / "icon"
ANDROID_FG_PATH = ANDROID_DIR / "android_fg_432.png"
ANDROID_BG_PATH = ANDROID_DIR / "android_bg_432.png"
SPLASH_PATH = ANDROID_DIR / "splash_1024.png"

# Canvas — 1024 squared, transparent background. Like the default Godot icon
# the visual shape IS the hex (no opaque rectangle behind it). This lets the
# macOS Dock / Cmd-Tab selection ring render around the icon's perimeter
# (which it can't do if the icon fills the canvas to its corners). iOS auto-
# applies its own rounded mask; transparent corners are harmless on-device.
CANVAS = 1024
CENTER = CANVAS / 2

# Palette — kept in sync with the in-game source of truth.
#   RED  -> src/FourExHex.Model/GameSettings.cs : player 0 fill
#   GOLD -> scripts/UiPalette.cs                : UiPalette.Gold
RED = "#cd473f"
HEX_BORDER = "#000000"
GOLD = "#d8b65a"

# Hex geometry — pointy-top, 960 px point-to-point (93.75 % of canvas height,
# matches the default Godot icon's proportions). Width = sqrt(3)/2 * 960 ~= 832.
HEX_RADIUS = 480
HEX_BORDER_WIDTH = 32

# "4X" — cap-height scaled with the hex so the proportional fill is unchanged.
TEXT = "4X"
TARGET_CAP_HEIGHT_PX = 430


def hex_vertices(cx: float, cy: float, r: float):
    """Pointy-top hex vertices using HexMapView's angle = 60*i - 30 formula."""
    return [
        (cx + r * math.cos(math.radians(60 * i - 30)),
         cy + r * math.sin(math.radians(60 * i - 30)))
        for i in range(6)
    ]


def hex_polygon_points(cx: float, cy: float, r: float) -> str:
    return " ".join(f"{x:.2f},{y:.2f}" for x, y in hex_vertices(cx, cy, r))


def glyph_for(font: TTFont, char: str):
    cmap = font.getBestCmap()
    if ord(char) not in cmap:
        raise ValueError(f"Font has no glyph for {char!r}")
    glyph_name = cmap[ord(char)]
    glyph_set = font.getGlyphSet()
    return glyph_set, glyph_set[glyph_name]


def glyph_svg_path(font: TTFont, char: str) -> str:
    glyph_set, glyph = glyph_for(font, char)
    pen = SVGPathPen(glyph_set)
    glyph.draw(pen)
    return pen.getCommands()


def glyph_bounds(font: TTFont, char: str):
    glyph_set, glyph = glyph_for(font, char)
    pen = BoundsPen(glyph_set)
    glyph.draw(pen)
    return pen.bounds  # (xmin, ymin, xmax, ymax) in font units


def glyph_advance(font: TTFont, char: str) -> int:
    glyph_set, glyph = glyph_for(font, char)
    return glyph.width


def build_svg() -> str:
    font = TTFont(str(FONT_PATH))
    upem = font["head"].unitsPerEm
    cap_height = getattr(font["OS/2"], "sCapHeight", None) or int(upem * 0.7)
    scale = TARGET_CAP_HEIGHT_PX / cap_height

    # Lay out the "4X" along a font-unit baseline at y=0.
    layout = []
    cursor = 0
    for ch in TEXT:
        d = glyph_svg_path(font, ch)
        bounds = glyph_bounds(font, ch)
        advance = glyph_advance(font, ch)
        layout.append({"char": ch, "d": d, "bounds": bounds, "x_offset": cursor})
        cursor += advance

    # Visual bounding box of the laid-out text in font units (Y-up).
    left_font = layout[0]["x_offset"] + layout[0]["bounds"][0]
    right_font = layout[-1]["x_offset"] + layout[-1]["bounds"][2]
    bottom_font = min(g["bounds"][1] for g in layout)  # descent
    top_font = max(g["bounds"][3] for g in layout)     # ascent
    text_cx_font = (left_font + right_font) / 2
    text_cy_font = (bottom_font + top_font) / 2

    # SVG transform: translate(tx, ty) then scale(s, -s) maps font (x, y) -> (tx + s*x, ty - s*y).
    # Solve for (tx, ty) so font (text_cx_font, text_cy_font) -> (CENTER, CENTER).
    tx = CENTER - scale * text_cx_font
    ty = CENTER + scale * text_cy_font

    glyph_elements = []
    for g in layout:
        glyph_tx = tx + scale * g["x_offset"]
        glyph_elements.append(
            f'  <path d="{g["d"]}" '
            f'transform="translate({glyph_tx:.3f} {ty:.3f}) '
            f'scale({scale:.6f} {-scale:.6f})" '
            f'fill="{GOLD}"/>'
        )

    hex_points = hex_polygon_points(CENTER, CENTER, HEX_RADIUS)
    glyphs_block = "\n".join(glyph_elements)

    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" '
        f'width="{CANVAS}" height="{CANVAS}" '
        f'viewBox="0 0 {CANVAS} {CANVAS}">\n'
        f'  <polygon points="{hex_points}" '
        f'fill="{RED}" stroke="{HEX_BORDER}" '
        f'stroke-width="{HEX_BORDER_WIDTH}" stroke-linejoin="miter"/>\n'
        f'{glyphs_block}\n'
        f'</svg>\n'
    )


# ---------- Android adaptive-icon PNGs ----------

# Android adaptive-icon canvas is 108 dp; the inner 72 dp is the safe square
# guaranteed to show through any launcher mask. At xxxhdpi that's 432 / 288 px.
# The hex must fit fully inside the 288 px safe square so its silhouette
# survives every mask shape.
ANDROID_CANVAS = 432
ANDROID_CENTER = ANDROID_CANVAS / 2
ANDROID_SAFE = 288  # 72/108 of canvas
ANDROID_HEX_RADIUS = 140        # point-to-point half-height = 280, inside 288 safe
# Proportions kept identical to the SVG (cap-height/hex_radius and border/hex_radius)
# so the rasterized Android hex looks like a scaled-down version of the SVG hex.
ANDROID_CAP_HEIGHT_PX = round(ANDROID_HEX_RADIUS * TARGET_CAP_HEIGHT_PX / HEX_RADIUS)
ANDROID_BORDER_WIDTH = round(ANDROID_HEX_RADIUS * HEX_BORDER_WIDTH / HEX_RADIUS)

BG_DEEP = "#23211d"   # UiPalette.BgDeep -- the slate frame around the hex.


def _pillow_font_for_cap_height(target_cap_height_px: int) -> ImageFont.FreeTypeFont:
    """Return a DMSerifDisplay font sized so its cap-height ~= target px."""
    font = TTFont(str(FONT_PATH))
    upem = font["head"].unitsPerEm
    cap_height_units = getattr(font["OS/2"], "sCapHeight", None) or int(upem * 0.7)
    # Pillow's "size" arg ~= em-height in pixels. Em = upem font units.
    # cap-height in pixels = (cap_height_units / upem) * size.
    # Solve for size given desired cap-height.
    size = target_cap_height_px * upem / cap_height_units
    return ImageFont.truetype(str(FONT_PATH), size)


def build_android_foreground() -> Image.Image:
    img = Image.new("RGBA", (ANDROID_CANVAS, ANDROID_CANVAS), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    vertices = hex_vertices(ANDROID_CENTER, ANDROID_CENTER, ANDROID_HEX_RADIUS)
    # Fill the hex. Pillow's polygon supports width since 9.4 but we'll draw the
    # outline as thick polylines for guaranteed correctness across versions.
    draw.polygon(vertices, fill=RED)
    closed = vertices + [vertices[0]]
    draw.line(closed, fill=HEX_BORDER, width=ANDROID_BORDER_WIDTH, joint="curve")

    font = _pillow_font_for_cap_height(ANDROID_CAP_HEIGHT_PX)
    # anchor="mm" centers on the text's middle. For all-cap text without
    # descenders, this matches the visual center of the cap-height band.
    draw.text((ANDROID_CENTER, ANDROID_CENTER), TEXT,
              font=font, fill=GOLD, anchor="mm")
    return img


def build_android_background() -> Image.Image:
    return Image.new("RGBA", (ANDROID_CANVAS, ANDROID_CANVAS),
                     _hex_to_rgba(BG_DEEP))


# ---------- Boot splash / iOS launch storyboard PNG ----------

# 1024 canvas matches the SVG icon; the hex is shrunk to ~70 % so the launch
# screen has slate margin around the icon (rather than the SVG icon's
# full-bleed look, which gets uncomfortable on portrait phones once iOS's
# Scale-to-Fit stretches the square image to fit the short axis).
SPLASH_CANVAS = CANVAS
SPLASH_CENTER = SPLASH_CANVAS / 2
SPLASH_HEX_RADIUS = 350
SPLASH_CAP_HEIGHT_PX = round(SPLASH_HEX_RADIUS * TARGET_CAP_HEIGHT_PX / HEX_RADIUS)
SPLASH_BORDER_WIDTH = round(SPLASH_HEX_RADIUS * HEX_BORDER_WIDTH / HEX_RADIUS)


def build_splash() -> Image.Image:
    img = Image.new("RGBA", (SPLASH_CANVAS, SPLASH_CANVAS), _hex_to_rgba(BG_DEEP))
    draw = ImageDraw.Draw(img)

    vertices = hex_vertices(SPLASH_CENTER, SPLASH_CENTER, SPLASH_HEX_RADIUS)
    draw.polygon(vertices, fill=RED)
    closed = vertices + [vertices[0]]
    draw.line(closed, fill=HEX_BORDER, width=SPLASH_BORDER_WIDTH, joint="curve")

    font = _pillow_font_for_cap_height(SPLASH_CAP_HEIGHT_PX)
    draw.text((SPLASH_CENTER, SPLASH_CENTER), TEXT,
              font=font, fill=GOLD, anchor="mm")
    return img


def _hex_to_rgba(hex_color: str, alpha: int = 255):
    h = hex_color.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), alpha)


# ---------- Entry point ----------

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stdout", action="store_true",
                        help="Print SVG to stdout (skip writing icon.svg / Android PNGs)")
    args = parser.parse_args()

    svg = build_svg()
    if args.stdout:
        sys.stdout.write(svg)
        return

    SVG_PATH.write_text(svg)
    print(f"Wrote {SVG_PATH} ({len(svg)} bytes)")

    ANDROID_DIR.mkdir(parents=True, exist_ok=True)
    build_android_foreground().save(ANDROID_FG_PATH, "PNG", optimize=True)
    print(f"Wrote {ANDROID_FG_PATH}")
    build_android_background().save(ANDROID_BG_PATH, "PNG", optimize=True)
    print(f"Wrote {ANDROID_BG_PATH}")
    build_splash().save(SPLASH_PATH, "PNG", optimize=True)
    print(f"Wrote {SPLASH_PATH}")


if __name__ == "__main__":
    main()
