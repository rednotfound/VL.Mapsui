using System;
using System.Linq;
using VL.Core.Import;

// global:: throughout: our own namespace is VL.Mapsui, so a bare "Mapsui.Widgets" would bind to
// VL.Mapsui.Widgets and not exist.
using Map = global::Mapsui.Map;
using IWidget = global::Mapsui.Widgets.IWidget;
using HAlign = global::Mapsui.Widgets.HorizontalAlignment;
using VAlign = global::Mapsui.Widgets.VerticalAlignment;
using ScaleBarWidget = global::Mapsui.Widgets.ScaleBar.ScaleBarWidget;
using ZoomInOutWidget = global::Mapsui.Widgets.Zoom.ZoomInOutWidget;

namespace VL.Mapsui;

/// <summary>Which units a scale bar counts in.</summary>
public enum ScaleBarUnits
{
    /// <summary>Metres and kilometres.</summary>
    Metric,
    /// <summary>Feet and miles.</summary>
    Imperial,
    /// <summary>Nautical miles.</summary>
    Nautical,
}

/// <summary>
/// A scale bar on the map. Nothing is drawn while Enabled is off.
/// </summary>
/// <remarks>
/// Mapsui's Skia renderer already knows how to draw this - measured, not assumed: a fresh
/// MapRenderer arrives with 9 widget renderers registered, ScaleBarWidget among them - so the
/// widget draws the moment it is on the map.
///
/// A process node because Map.Widgets can only be appended to; see <see cref="WidgetSlot{T}"/>.
/// </remarks>
[ProcessNode(Name = "ScaleBar", Category = "Mapsui.Widgets")]
public class ScaleBarWidgetNode
{
    readonly WidgetSlot<ScaleBarWidget> _slot = new();

    /// <summary>Widgets this node has added. It should reach 1 and stay there.</summary>
    internal int WidgetsAdded => _slot.Added;

    /// <summary>The same map, so this sits in the chain between Map and ToSkiaLayer.</summary>
    public Map? Update(
        Map? map,
        bool enabled = true,
        ScaleBarUnits units = ScaleBarUnits.Metric,
        WidgetCorner corner = WidgetCorner.BottomLeft)
    {
        if (map is null) return null;

        var widget = _slot.Ensure(map, m =>
            new ScaleBarWidget(m, global::Mapsui.Projections.ProjectionDefaults.Projection));

        widget.Enabled = enabled;
        widget.UnitConverter = units switch
        {
            ScaleBarUnits.Imperial => global::Mapsui.Widgets.ScaleBar.ImperialUnitConverter.Instance,
            ScaleBarUnits.Nautical => global::Mapsui.Widgets.ScaleBar.NauticalUnitConverter.Instance,
            _                      => global::Mapsui.Widgets.ScaleBar.MetricUnitConverter.Instance,
        };
        WidgetSlot<ScaleBarWidget>.Place(widget, corner);

        return map;
    }
}
