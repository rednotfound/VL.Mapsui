using SkiaSharp;
using VL.Skia;

namespace VL.Mapsui.Tests;

/// <summary>
/// Checks that drawing lands in pixels whatever space the caller is in.
/// </summary>
/// <remarks>
/// Mapsui draws in pixels; VL.Skia hands over a canvas that already carries a transformation,
/// and in its default space the whole view is about 2.8 by 2 units wide. The Renderer has a
/// Space pin meant to change that, but an unrecognised value is silently replaced by the
/// default, so a patch relying on it relies on something whose failure is invisible. The layer
/// resets the matrix itself instead, and these tests are what says so.
///
/// Note what makes this a real test rather than a false proof: the caller is given a
/// transformation that would ruin the result if it were honoured. Drawing onto a bare SKCanvas
/// with no transformation would prove nothing, which is exactly the mistake made once already
/// on this stack.
/// </remarks>
public class PixelSpaceTests
{
    // Something like VL.Skia's default space: one unit is worth a hundred pixels, so a
    // coordinate of 40 would land at 0.4 and be invisible.
    static readonly SKMatrix NotPixels = SKMatrix.CreateScale(0.01f, 0.01f);

    static CallerInfo Caller(SKCanvas canvas, SKMatrix transformation)
        => CallerInfo.InRenderer(200f, 200f, canvas, null).WithTransformation(transformation);

    [Fact]
    public void A_rectangle_at_pixel_forty_lands_at_pixel_forty()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        surface.Canvas.Clear(SKColors.Black);

        PixelSpace.Draw(Caller(surface.Canvas, NotPixels), (canvas, bounds) =>
        {
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(SKRect.Create(40f, 40f, 20f, 20f), paint);
        });

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(SKColors.Red, bitmap.GetPixel(50, 50));     // inside
        Assert.Equal(SKColors.Black, bitmap.GetPixel(10, 10));   // outside, and would be red-ish
                                                                 // if the scale had applied
    }

    [Fact]
    public void The_callers_matrix_is_put_back_afterwards()
    {
        // A layer that leaves the canvas transformed corrupts everything drawn after it, and
        // the symptom would appear in someone else's node.
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        var canvas = surface.Canvas;
        canvas.SetMatrix(NotPixels);
        var before = canvas.TotalMatrix;

        PixelSpace.Draw(Caller(canvas, NotPixels), (c, bounds) => c.DrawRect(SKRect.Create(0, 0, 10, 10), new SKPaint()));

        Assert.Equal(before.ScaleX, canvas.TotalMatrix.ScaleX, 6);
        Assert.Equal(before.ScaleY, canvas.TotalMatrix.ScaleY, 6);
        Assert.Equal(before.TransX, canvas.TotalMatrix.TransX, 6);
        Assert.Equal(before.TransY, canvas.TotalMatrix.TransY, 6);
    }

    [Fact]
    public void The_matrix_is_put_back_even_when_the_drawing_throws()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        var canvas = surface.Canvas;
        canvas.SetMatrix(NotPixels);
        var before = canvas.TotalMatrix;

        Assert.Throws<InvalidOperationException>(() =>
            PixelSpace.Draw(Caller(canvas, NotPixels), (c, bounds) => throw new InvalidOperationException()));

        Assert.Equal(before.ScaleX, canvas.TotalMatrix.ScaleX, 6);
    }

    [Fact]
    public void The_bounds_handed_to_the_drawing_are_the_viewport()
    {
        // Mapsui sizes its viewport from these, which is also what decides which tiles it asks
        // for. Getting them wrong fetches the wrong part of the world.
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        SKRect seen = default;

        PixelSpace.Draw(Caller(surface.Canvas, NotPixels), (canvas, bounds) => seen = bounds);

        Assert.Equal(200f, seen.Width, 3);
        Assert.Equal(200f, seen.Height, 3);
    }
}
