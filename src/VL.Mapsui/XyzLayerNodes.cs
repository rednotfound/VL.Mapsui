using System;
using VL.Core.Import;

using ILayer = global::Mapsui.Layers.ILayer;
using TileLayer = global::Mapsui.Tiling.Layers.TileLayer;
using HttpTileSource = BruTile.Web.HttpTileSource;
using GlobalSphericalMercator = BruTile.Predefined.GlobalSphericalMercator;
using BruTileAttribution = BruTile.Attribution;

namespace VL.Mapsui;

/// <summary>
/// A tile layer from any XYZ (slippy map) URL template — terrain, satellite, an authority's own
/// service, your own server. Nothing is fetched until Enabled is switched on.
/// </summary>
/// <remarks>
/// **This is what stops the package being about one basemap.** `OpenStreetMap` is a convenience for
/// the most common source; this is the general one, and the architecture should not depend on any
/// single provider.
///
/// The template takes <c>{z}</c> for zoom, <c>{x}</c> for column and <c>{y}</c> for row — the
/// convention every slippy map uses, so a URL copied from a provider's documentation usually works
/// unchanged:
/// <code>https://tile.opentopomap.org/{z}/{x}/{y}.png</code>
///
/// **Attribution is a pin rather than an afterthought.** Nearly every tile service requires it, the
/// `Attribution` widget draws whatever the layers carry, and a layer with none silently contributes
/// nothing to it. Filling it in is the difference between complying and appearing to.
///
/// A process node for the same reason the OpenStreetMap one is: a tile layer owns HTTP connections
/// and a disk cache, and a static method is evaluated every frame.
/// </remarks>
[ProcessNode(Name = "XYZ", Category = "Mapsui.Layers")]
public class XyzTileLayerNode : IDisposable
{
    TileLayer? _layer;
    TileDiskCache? _attached;
    string _url = string.Empty;
    string _attribution = string.Empty;
    string _status = "off";

    /// <summary>Layers built by this node. It should reach 1 and stay there.</summary>
    internal int LayersBuilt { get; private set; }

    /// <summary>Where this layer's tiles go, or why they are not kept.</summary>
    internal string CacheStatus => _layer is not null && _attached is { IsOn: true }
        ? TileCache.Describe(_attached.Folder)
        : _status;

    /// <summary>
    /// The layer to put on a map, or nothing while Enabled is off or the template is unusable.
    /// </summary>
    /// <remarks>
    /// Enabled defaults to off because opening a document in vvvv runs it, and whoever opened it
    /// has agreed to nothing yet — the same rule as everywhere else here, and it matters more for a
    /// source you chose than for one this package suggested.
    ///
    /// A template without <c>{x}</c>, <c>{y}</c> and <c>{z}</c> is refused rather than used: every
    /// tile would resolve to the same URL, so the map would quietly fill with one image repeated,
    /// and the server would be asked for it over and over.
    /// </remarks>
    public ILayer? Update(
        out int layersBuilt,
        out string cacheStatus,
        // Without this the pin reads "Url Template": VL builds a pin name by splitting the
        // parameter at its capitals, and the C# name cannot carry an acronym through that.
        [Pin(Name = "URL Template")] string urlTemplate = "",
        string attribution = "",
        bool enabled = false,
        TileDiskCache? cache = null)
    {
        if (!enabled)
        {
            Release();
            layersBuilt = LayersBuilt;
            cacheStatus = CacheStatus;
            return null;
        }

        var wanted = cache ?? TileCache.Default();
        var url = (urlTemplate ?? string.Empty).Trim();
        var credit = attribution ?? string.Empty;

        if (!IsUsable(url, out var problem))
        {
            Release();
            _status = problem!;
            layersBuilt = LayersBuilt;
            cacheStatus = CacheStatus;
            return null;
        }

        // What the tile source *is* rebuilds it; where the map looks never reaches this node.
        if (_layer is null
            || !ReferenceEquals(wanted, _attached)
            || !string.Equals(url, _url, StringComparison.Ordinal)
            || !string.Equals(credit, _attribution, StringComparison.Ordinal))
        {
            Release();
            _layer = Build(url, credit, wanted, out _status);
            _attached = wanted;
            _url = url;
            _attribution = credit;
            LayersBuilt++;
        }

        layersBuilt = LayersBuilt;
        cacheStatus = CacheStatus;
        return _layer;
    }

    /// <summary>
    /// Whether a template can address tiles at all.
    /// </summary>
    /// <remarks>
    /// Deliberately not silent, and deliberately not clever: this checks that the three placeholders
    /// are present, not that the server exists. A wrong host fails visibly on the first fetch; a
    /// template with no placeholders fails invisibly, forever, which is the worse of the two.
    /// </remarks>
    internal static bool IsUsable(string url, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            problem = "off - no URL template";
            return false;
        }

        foreach (var placeholder in new[] { "{x}", "{y}", "{z}" })
            if (!url.Contains(placeholder, StringComparison.Ordinal))
            {
                problem = $"cannot use {url}: the template needs {{x}}, {{y}} and {{z}} - without them every tile is the same URL";
                return false;
            }

        return true;
    }

    static TileLayer Build(string url, string attribution, TileDiskCache cache, out string status)
    {
        var source = new HttpTileSource(
            new GlobalSphericalMercator(),
            url,
            name: "XYZ",
            attribution: new BruTileAttribution(attribution),
            userAgent: OpenStreetMapLayerNode.UserAgent);

        var layer = new TileLayer(source) { Name = string.IsNullOrWhiteSpace(attribution) ? "XYZ" : attribution };

        if (!cache.IsOn)
        {
            status = TileCache.Describe(cache);
            return layer;
        }

        // Keyed on the template, so two services - or two styles from one service - never read
        // each other's tiles. Without this, changing the URL changed nothing: see
        // TileDiskCache.CacheFor for what that looked like and how long it hid.
        source.PersistentCache = cache.CacheFor(url);
        status = cache.Folder;      // CacheStatus re-reads the size every frame from here on
        return layer;
    }

    void Release()
    {
        // AbortFetch before Dispose: a request in flight otherwise outlives the layer that asked
        // for it, which is how connections accumulate.
        _layer?.AbortFetch();
        _layer?.Dispose();
        _layer = null;
        _attached = null;
        _url = string.Empty;
        _attribution = string.Empty;
        _status = "off";
    }

    /// <summary>Releases the layer and aborts any tile fetch still in flight.</summary>
    public void Dispose() => Release();
}
