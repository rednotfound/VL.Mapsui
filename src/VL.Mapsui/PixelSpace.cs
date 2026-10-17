using System;
using SkiaSharp;
using VL.Lib.IO.Notifications;
using VL.Skia;

namespace VL.Mapsui;

/// <summary>
/// The bridge between VL.Skia's coordinate space and a renderer that draws in raw pixels.
/// </summary>
/// <remarks>
/// Mapsui draws in pixels with the origin at the top-left, the convention every slippy map
/// uses. VL.Skia does not: the canvas arrives with a transformation already applied, and in the
/// default space the visible area is only about 2.8 by 2 units wide. Handing pixel coordinates
/// straight to such a canvas puts everything hundreds of times off screen, silently.
///
/// Rather than requiring the patch to set the Renderer's Space pin - a value whose wrong
/// setting is indistinguishable from a broken patch, because vvvv replaces an unrecognised one
/// with the default without a word - we reset the matrix ourselves. VL.ImGui does the same
/// thing for the same reason in ToSkiaLayer.
/// </remarks>
static class PixelSpace
{
    /// <summary>
    /// Run <paramref name="draw"/> with the canvas mapping one unit to one device pixel,
    /// origin at the top-left of the viewport, then put the canvas back exactly as it was.
    /// </summary>
    public static void Draw(CallerInfo caller, Action<SKCanvas, SKRect> draw)
    {
        var canvas = caller.Canvas;
        if (canvas is null) return;

        var count = canvas.Save();
        try
        {
            // Identity is device pixels: VL.Skia's transformation is what converts its space
            // into them, so discarding it leaves exactly the space Mapsui expects.
            canvas.SetMatrix(SKMatrix.Identity);
            draw(canvas, caller.ViewportBounds);
        }
        finally
        {
            canvas.RestoreToCount(count);
        }
    }
}
