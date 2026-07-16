// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// How <see cref="HexMapView"/> interprets a left-mouse-button gesture.
/// <see cref="Pan"/> = drag pans the camera, click fires
/// <see cref="HexMapView.CoordClicked"/> /
/// <see cref="HexMapView.TileClicked"/> (the play scene's behavior;
/// also the editor's behavior under the hand and capital swatches).
/// <see cref="Paint"/> = press/motion fires
/// <see cref="HexMapView.PaintCellEntered"/> per unique cell touched,
/// release fires <see cref="HexMapView.PaintStrokeEnded"/>; no pan,
/// no click events for that gesture (the editor's behavior under the
/// color/water/tree/tower swatches).
/// </summary>
public enum HexDragMode
{
    Pan,
    Paint,
}
