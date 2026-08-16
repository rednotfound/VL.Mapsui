using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

using NtsFeature = NetTopologySuite.Features.Feature;
using AttributesTable = NetTopologySuite.Features.AttributesTable;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace VL.Mapsui.Tests;

/// <summary>
/// What happens when a patch hands the layer a thousand features every frame.
/// </summary>
/// <remarks>
/// The rest of the suite asks whether the layer is *correct*. This one asks whether it is *usable*,
/// which is a different question and the one a real dataset asks first — a prefecture boundary is
/// thousands of vertices, and `SameFeatures` walks all of them, on every feature, on every frame.
///
/// These are timing tests, so they assert on **shape rather than on milliseconds**: that the cheap
/// path is taken, and that it is cheap *relative to* the expensive one. A hard millisecond budget
/// would fail on someone else's machine for reasons that have nothing to do with this code.
/// </remarks>
public class ScaleTests
{
    readonly ITestOutputHelper _out;
    public ScaleTests(ITestOutputHelper output) => _out = output;

    static readonly GeometryFactory Factory = new();

    /// <summary>A closed ring with <paramref name="vertices"/> points — a coastline, roughly.</summary>
    static NtsGeometry Ring(int index, int vertices)
    {
        var points = new Coordinate[vertices + 1];
        var radius = 0.01 + index * 0.0001;

        for (var i = 0; i < vertices; i++)
        {
            var angle = 2 * Math.PI * i / vertices;
            points[i] = new Coordinate(135.75 + radius * Math.Cos(angle), 34.98 + radius * Math.Sin(angle));
        }

        points[vertices] = points[0];
        return Factory.CreatePolygon(points);
    }

    static NtsFeature[] Dataset(int count, int vertices) =>
        Enumerable.Range(0, count)
            .Select(i => new NtsFeature(Ring(i, vertices), new AttributesTable { { "name", $"feature {i}" } }))
            .ToArray();

    /// <summary>
    /// The realistic shape: the data is built once and the same spread arrives on every frame.
    /// </summary>
    /// <remarks>
    /// This is what a patch does when the features come from a file, or from anything inside a Cache
    /// region — which is what VL.Skia's own help tells patchers to do. The layer should notice and
    /// do nothing.
    /// </remarks>
    [Fact]
    public void A_thousand_features_that_never_change_cost_almost_nothing_after_the_first_frame()
    {
        const int Features = 1000, Vertices = 500, Frames = 60;

        var data = Dataset(Features, Vertices);
        using var layer = new FeatureLayerNode();

        layer.Update(out _, out _, data);          // frame 1 builds it

        var watch = Stopwatch.StartNew();
        for (var frame = 0; frame < Frames; frame++)
            layer.Update(out _, out _, data);
        watch.Stop();

        var perFrame = watch.Elapsed.TotalMilliseconds / Frames;
        _out.WriteLine($"{Features} features x {Vertices} vertices, unchanged: {perFrame:0.000} ms/frame");

        Assert.Equal(1, layer.LayersBuilt);
        Assert.Equal(1, layer.FeatureSetsBuilt);

        // A frame is 16.6 ms. Steady-state bookkeeping for unchanged data must not be a
        // measurable slice of it.
        Assert.True(perFrame < 1.0, $"{perFrame:0.000} ms/frame of pure bookkeeping is too much");
    }

    /// <summary>
    /// The genuinely expensive case: new feature objects every frame, saying the same thing.
    /// </summary>
    /// <remarks>
    /// **This is what a patch built from static `Feature` nodes does** — VL evaluates them every
    /// frame and each returns a fresh object, so the per-item `ReferenceEquals` short circuit in
    /// `SameFeatures` never fires and every coordinate of every geometry is walked.
    ///
    /// A first version of this test reused the same feature objects and reported 0.013 ms/frame,
    /// which measured the short circuit rather than the comparison. Left recorded here because the
    /// mistake is instructive: a benchmark that does not do the expensive thing reports that the
    /// expensive thing is cheap.
    /// </remarks>
    [Fact]
    public void What_a_full_value_comparison_costs_when_the_objects_are_new_each_frame()
    {
        const int Features = 1000, Vertices = 500, Frames = 20;

        var data = Dataset(Features, Vertices);
        using var layer = new FeatureLayerNode();
        layer.Update(out _, out _, data);

        var watch = Stopwatch.StartNew();
        for (var frame = 0; frame < Frames; frame++)
            layer.Update(out _, out _, Dataset(Features, Vertices));   // equal contents, new objects
        watch.Stop();

        var perFrame = watch.Elapsed.TotalMilliseconds / Frames;
        _out.WriteLine($"{Features} features x {Vertices} vertices, NEW objects: {perFrame:0.000} ms/frame " +
                       $"(includes building them: see the baseline below)");

        var buildWatch = Stopwatch.StartNew();
        for (var frame = 0; frame < Frames; frame++) Dataset(Features, Vertices);
        buildWatch.Stop();
        var buildPerFrame = buildWatch.Elapsed.TotalMilliseconds / Frames;
        _out.WriteLine($"   of which building the data: {buildPerFrame:0.000} ms/frame");
        _out.WriteLine($"   so the comparison itself:   {perFrame - buildPerFrame:0.000} ms/frame");

        // Still one layer and one feature set: the comparison correctly says "unchanged".
        Assert.Equal(1, layer.LayersBuilt);
        Assert.Equal(1, layer.FeatureSetsBuilt);

        // The point of the measurement, in a form that survives a faster machine: when a patch
        // rebuilds its features every frame, MAKING them costs more than our comparing them. The
        // layer is not the bottleneck in the slow case, and there is nothing here to optimise that
        // would rescue such a patch - only building the data once will.
        Assert.True(perFrame - buildPerFrame < buildPerFrame,
            $"comparison {perFrame - buildPerFrame:0.000} ms should stay below construction {buildPerFrame:0.000} ms");
    }

    /// <summary>
    /// Data that really does change still gets through — the fast path must not swallow it.
    /// </summary>
    [Fact]
    public void Changing_the_data_still_rebuilds_the_contents()
    {
        using var layer = new FeatureLayerNode();

        layer.Update(out _, out _, Dataset(10, 8));
        Assert.Equal(1, layer.FeatureSetsBuilt);

        var moved = Dataset(10, 8);
        moved[3] = new NtsFeature(Ring(999, 8), new AttributesTable { { "name", "moved" } });
        layer.Update(out _, out _, moved);

        Assert.Equal(2, layer.FeatureSetsBuilt);
        Assert.Equal(1, layer.LayersBuilt);
    }
}
