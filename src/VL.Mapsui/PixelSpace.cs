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

/// <summary>
/// A layer that draws nothing but what it can measure about the space it was handed.
/// </summary>
/// <remarks>
/// This exists because "nothing appeared" is the least informative symptom there is, and every
/// wrong guess about VL.Skia's space produced exactly it. A layer that reports its own inputs
/// separates three possibilities in one glance: no text at all means the layer is not being
/// rendered; text in the wrong place means the matrix handling is wrong; text in the right
/// place with implausible numbers means the units are not what we assumed.
///
/// Delete it once VL.Mapsui works. It is scaffolding, not a feature.
/// </remarks>
sealed class DiagnosticsLayer : ILayer
{
    // null means "no natural extent", which is what a diagnostic overlay has.
    public Stride.Core.Mathematics.RectangleF? Bounds => null;

    public bool Notify(INotification notification, CallerInfo caller) => false;

    public void Render(CallerInfo caller)
    {
        var t = caller.Transformation;
        var vb = caller.ViewportBounds;

        PixelSpace.Draw(caller, (canvas, bounds) =>
        {
            using var fill = new SKPaint { Color = new SKColor(0xFF, 0x66, 0x00), IsAntialias = true };
            using var text = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                TextSize = 14f,
                Typeface = SKTypeface.FromFamilyName("Consolas"),
            };

            // A 200x120 box at pixel (40, 40). If the space handling is right this lands near
            // the top-left corner of the window at a legible size, whatever the Renderer's
            // Space pin happens to be set to.
            canvas.DrawRect(SKRect.Create(40f, 40f, 200f, 120f), fill);

            var y = 190f;
            void Line(string s) { canvas.DrawText(s, 40f, y, text); y += 18f; }

            Line("DiagnosticsLayer - drawn at identity matrix");
            Line($"ViewportBounds  {vb.Left:0.##}, {vb.Top:0.##}  size {vb.Width:0.##} x {vb.Height:0.##}");
            Line($"Transformation  scale {t.ScaleX:0.####}, {t.ScaleY:0.####}");
            Line($"                trans {t.TransX:0.##}, {t.TransY:0.##}");
            Line($"Canvas device   {canvas.DeviceClipBounds.Width} x {canvas.DeviceClipBounds.Height}");
            Line("");
            Line("The orange box is 200x120 px at (40,40).");
            Line("If it looks that size, pixel space works.");
        });
    }
}
