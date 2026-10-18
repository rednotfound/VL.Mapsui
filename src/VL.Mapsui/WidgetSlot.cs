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

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// Mapsui expresses this as two independent alignments. One pin instead of two, because "which
// corner" is the question a patch is actually asking and a widget that needs two pins for its
// position leaves no room for the pins that say what it IS - three inputs is the target, and 94% of
// the ecosystem's nodes take three or fewer.

/// <summary>
/// Where a widget sits in the map window.
/// </summary>
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

// There was a third widget node, Attribution, until 2026-09-26. Mapsui 4.1.9's MapRenderer draws every
// layer's Attribution bottom right by itself, with no widget on the map (AttributionRenderingFacts),
// so the node could only add a second copy: switching it off or moving it changed nothing the
// user could see. The credit a tile layer carries is what reaches the screen - see XyzTileLayerNode.
