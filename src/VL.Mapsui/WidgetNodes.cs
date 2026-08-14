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
using Hyperlink = global::Mapsui.Widgets.Hyperlink;
using ZoomInOutWidget = global::Mapsui.Widgets.Zoom.ZoomInOutWidget;

namespace VL.Mapsui;

/// <summary>
/// Where a widget sits in the map window.
/// </summary>
/// <remarks>
/// Mapsui expresses this as two independent alignments. One pin instead of two, because "which
/// corner" is the question a patch is actually asking and a widget that needs two pins for its
/// position leaves no room for the pins that say what it *is* - three inputs is the target, and
/// 94% of the ecosystem's nodes take three or fewer.
/// </remarks>
public enum WidgetCorner
{
    /// <summary>Top left.</summary>
    TopLeft,
    /// <summary>Top, centred horizontally.</summary>
    TopCenter,
    /// <summary>Top right.</summary>
    TopRight,
    /// <summary>Left, centred vertically.</summary>
    CenterLeft,
    /// <summary>The middle of the window.</summary>
    Center,
    /// <summary>Right, centred vertically.</summary>
    CenterRight,
    /// <summary>Bottom left.</summary>
    BottomLeft,
    /// <summary>Bottom, centred horizontally.</summary>
    BottomCenter,
    /// <summary>Bottom right.</summary>
    BottomRight,
}

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
/// Holds one widget on one map, and puts it there exactly once.
/// </summary>
/// <remarks>
/// **<c>Map.Widgets</c> is a <c>ConcurrentQueue&lt;IWidget&gt;</c>: append only, with no way to
/// remove anything.** So a widget node cannot be a static operation - VL would evaluate it every
/// frame and enqueue sixty widgets a second, and nothing could ever take them out again. Build
/// once, then drive the widget through its properties; <c>Enabled</c> is how a widget goes away.
/// </remarks>
sealed class WidgetSlot<T> where T : class, IWidget
{
    Map? _map;
    T? _widget;

    /// <summary>Widgets this slot has enqueued. It should reach 1 and stay.</summary>
    public int Added { get; private set; }

    /// <summary>
    /// The widget on this map, enqueueing it the first time and whenever the map is a different
    /// object - a new map has none of the old one's widgets, and the old one is on its way out.
    /// </summary>
    public T Ensure(Map map, Func<Map, T> create)
    {
        if (_widget is null || !ReferenceEquals(map, _map))
        {
            _widget = create(map);
            map.Widgets.Enqueue(_widget);
            _map = map;
            Added++;
        }

        return _widget;
    }

    internal static (HAlign h, VAlign v) Align(WidgetCorner corner) => corner switch
    {
        WidgetCorner.TopLeft      => (HAlign.Left,   VAlign.Top),
        WidgetCorner.TopCenter    => (HAlign.Center, VAlign.Top),
        WidgetCorner.TopRight     => (HAlign.Right,  VAlign.Top),
        WidgetCorner.CenterLeft   => (HAlign.Left,   VAlign.Center),
        WidgetCorner.Center       => (HAlign.Center, VAlign.Center),
        WidgetCorner.CenterRight  => (HAlign.Right,  VAlign.Center),
        WidgetCorner.BottomLeft   => (HAlign.Left,   VAlign.Bottom),
        WidgetCorner.BottomCenter => (HAlign.Center, VAlign.Bottom),
        _                         => (HAlign.Right,  VAlign.Bottom),
    };

    /// <summary>Place a widget, whatever kind it is.</summary>
    public static void Place(IWidget widget, WidgetCorner corner)
    {
        var (h, v) = Align(corner);
        widget.HorizontalAlignment = h;
        widget.VerticalAlignment = v;
    }
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

/// <summary>
/// The attribution the tile provider requires, taken from the layers on the map.
/// </summary>
/// <remarks>
/// **This is compliance, not decoration.** OpenStreetMap's tile usage policy requires the
/// attribution to be displayed; every tile layer here has carried the text since the beginning and
/// nothing ever drew it.
///
/// The text is read from the map's own layers rather than typed into a pin, so it stays true when
/// the layers change and cannot be filled in wrongly. Mapsui's Hyperlink widget carries a Url as
/// well, which is why this is a Hyperlink rather than a label.
/// </remarks>
[ProcessNode(Name = "Attribution", Category = "Mapsui.Widgets")]
public class AttributionWidgetNode
{
    readonly WidgetSlot<Hyperlink> _slot = new();
    string _text = string.Empty;

    /// <summary>Widgets this node has added. It should reach 1 and stay there.</summary>
    internal int WidgetsAdded => _slot.Added;

    /// <summary>What is currently being shown, so a patch can check it is not empty.</summary>
    internal string Text => _text;

    /// <summary>The same map, so this sits in the chain between Map and ToSkiaLayer.</summary>
    /// <remarks>
    /// Map comes first and the readout after it, because a node whose return type equals its
    /// *first* parameter type is the fluent shape VL recognises - it gets an `Output` pin and sits
    /// in a chain. Putting the out parameter first would quietly make this something else.
    /// </remarks>
    public Map? Update(
        Map? map,
        out string attribution,
        bool enabled = true,
        WidgetCorner corner = WidgetCorner.BottomRight)
    {
        if (map is null)
        {
            attribution = string.Empty;
            return null;
        }

        var widget = _slot.Ensure(map, _ => new Hyperlink());

        // Read every frame: layers come and go, and an attribution that stops matching the tiles
        // on screen is worse than none, because it is a claim about where they came from.
        var credits = map.Layers
            .Select(l => l.Attribution)
            .Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Text))
            .ToArray();

        _text = string.Join("  |  ", credits.Select(a => a!.Text).Distinct());

        widget.Enabled = enabled;
        widget.Text = _text;
        widget.Url = credits.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a!.Url))?.Url ?? string.Empty;
        WidgetSlot<Hyperlink>.Place(widget, corner);

        attribution = _text;
        return map;
    }
}

/// <summary>
/// Plus and minus buttons that zoom the map.
/// </summary>
/// <remarks>
/// These need clicks to reach them, which only the host can arrange: <see cref="MapsuiLayer"/>
/// routes a mouse press to whichever widget's envelope contains it. That is not the same as
/// deciding what the mouse means for the map itself - a patch still wires drag and wheel to
/// Mapsui.Navigate - because a widget is something the patch explicitly put on the map.
/// </remarks>
[ProcessNode(Name = "ZoomButtons", Category = "Mapsui.Widgets")]
public class ZoomButtonsWidgetNode
{
    readonly WidgetSlot<ZoomInOutWidget> _slot = new();

    /// <summary>Widgets this node has added. It should reach 1 and stay there.</summary>
    internal int WidgetsAdded => _slot.Added;

    /// <summary>The same map, so this sits in the chain between Map and ToSkiaLayer.</summary>
    public Map? Update(
        Map? map,
        bool enabled = true,
        WidgetCorner corner = WidgetCorner.TopRight)
    {
        if (map is null) return null;

        var widget = _slot.Ensure(map, _ => new ZoomInOutWidget());

        widget.Enabled = enabled;
        WidgetSlot<ZoomInOutWidget>.Place(widget, corner);

        return map;
    }
}
