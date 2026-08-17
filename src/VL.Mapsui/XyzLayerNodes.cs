using System;
using System.Collections.Generic;
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

    /// <summary>
    /// One tile source per URL this node has been given, kept for reuse.
    /// </summary>
    /// <remarks>
    /// **Layers are cheap and sources are not, and only the expensive half needs keeping.**
    /// <c>new TileLayer(existingSource)</c> is legal and a source may back several layers, whereas
    /// every <c>HttpTileSource</c> constructs its own <c>HttpClient</c> — and BruTile's
    /// <c>HttpTileSource</c> is **not IDisposable**, so nothing ever releases it. Rebuilding the
    /// source on every switch therefore leaked one connection pool per switch, reclaimed only when
    /// a finalizer eventually ran, with pooled sockets lingering past that. That is the mechanism
    /// behind this package's 17,000-connection incident running at a slower clock: once per
    /// switch rather than once per frame. Harmless while a person clicks; not harmless in an
    /// installation, or the first time somebody drives the preset index from an LFO.
    ///
    /// **Per node instance rather than static**, deliberately: <c>PersistentCache</c> is settable on
    /// the source, so two nodes on the same URL but given different <c>TileCache</c> folders would
    /// fight over one source. Per instance the count is bounded by the URLs this node has actually
    /// been handed, which for a preset picker is the number of presets.
    ///
    /// **Keyed on URL *and* attribution** because BruTile bakes the attribution into the source at
    /// construction, so a source reused under a different credit would carry the old one — and
    /// silently crediting the wrong provider is precisely the failure this package keeps trying to
    /// design out.
    /// </remarks>
    readonly Dictionary<string, HttpTileSource> _sources = new(StringComparer.Ordinal);

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

    TileLayer Build(string url, string attribution, TileDiskCache cache, out string status)
    {
        // \n cannot occur in either half, so the pair cannot be ambiguous.
        var key = url + "\n" + attribution;
        if (!_sources.TryGetValue(key, out var source))
        {
            source = new HttpTileSource(
                new GlobalSphericalMercator(),
                url,
                name: "XYZ",
                attribution: new BruTileAttribution(attribution),
                userAgent: OpenStreetMapLayerNode.UserAgent);
            _sources[key] = source;
        }

        var layer = new TileLayer(source) { Name = string.IsNullOrWhiteSpace(attribution) ? "XYZ" : attribution };

        // PersistentCache is assigned on EVERY build, both branches, because a reused source
        // arrives carrying whatever cache it was given last time. Returning early here without
        // clearing it would leave the old FileCache attached, so switching the cache off would
        // quietly keep writing - a setting that reports "off" and is not.
        if (!cache.IsOn)
        {
            source.PersistentCache = new BruTile.Cache.NullCache();
            status = TileCache.Describe(cache);
            return layer;
        }

        // Keyed on the template, so two services - or two styles from one service - never read
        // each other's tiles. Without this, changing the URL changed nothing: see
        // TileDiskCache.CacheFor for what that looked like and how long it hid.
        source.PersistentCache = cache.CacheFor(url)!;
        status = cache.Folder;      // CacheStatus re-reads the size every frame from here on
        return layer;
    }

    void Release()
    {
        // AbortFetch before Dispose. The ORDER is right; the reason first written here was not.
        // AbortFetch does not cancel a download - BruTile calls GetByteArrayAsync with no
        // CancellationToken, so up to four in-flight requests finish regardless. What it does is
        // drain the QUEUE, which holds up to 128 tiles, and shrink the window on a real hazard:
        // BruTile's MemoryCache.Add never checks _disposed, so a worker landing after Dispose
        // silently refills a cache that will never be emptied again.
        //
        // The sources in _sources are NOT released here: they are reused across rebuilds, and
        // BruTile offers no way to release one anyway.
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
