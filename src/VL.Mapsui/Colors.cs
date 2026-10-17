using System;
using Stride.Core.Mathematics;
using VL.Core.Import;

using IStyle = global::Mapsui.Styles.IStyle;
using MapsuiVectorStyle = global::Mapsui.Styles.VectorStyle;
using StyleCollection = global::Mapsui.Styles.StyleCollection;
using Brush = global::Mapsui.Styles.Brush;
using Pen = global::Mapsui.Styles.Pen;
using MapsuiColor = global::Mapsui.Styles.Color;

namespace VL.Mapsui;

/// <summary>
/// Colour conversion, in one place because two nodes need it.
/// </summary>
static class Colors
{
    public static MapsuiColor ToMapsui(Color4 c) => new(
        (int)Math.Round(Math.Clamp(c.R, 0f, 1f) * 255f),
        (int)Math.Round(Math.Clamp(c.G, 0f, 1f) * 255f),
        (int)Math.Round(Math.Clamp(c.B, 0f, 1f) * 255f),
        (int)Math.Round(Math.Clamp(c.A, 0f, 1f) * 255f));
}
