using System;
using System.Collections.Generic;
using System.Linq;
using VL.Core;
using VL.Lib.Collections;
using Xunit;

using VLName = VL.Core.Import.NameAttribute;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using WKTReader = NetTopologySuite.IO.WKTReader;

namespace VL.Mapsui.Tests;

/// <summary>
/// Turning a patch's own data type into features — the node that makes "a thousand places" a
/// spread of instances instead of a thousand lengths of wiring.
/// </summary>
/// <remarks>
/// **The assertions are about keys and about how often work happens.** A wrong key is the silent
/// failure the node exists to remove; work happening every frame is the one that makes it useless at
/// scale. Neither shows up in a test that only checks a feature came back.
///
/// A patched record cannot be built without the vvvv runtime — `AppHost.CurrentOrGlobal` is null
/// here, measured — so the `IVLObject` path runs against a hand-written double. It covers our name
/// selection, which is the part we wrote; the GUI round covers the part VL owns.
/// </remarks>
public class ToFeaturesTests
{
    static readonly NtsGeometry Square = new WKTReader()
        .Read("POLYGON ((-0.5 -0.5, 0.5 -0.5, 0.5 0.5, -0.5 0.5, -0.5 -0.5))");

    // ---------- a plain .NET stand-in for a record ----------

    sealed class Landmark
    {
        public NtsGeometry? Geometry;
        public string Name = "Kyoto Station";

        [VLName("Some Field")]
        public string Some_Field = "station";

        public string? Missing = null;
    }

    sealed class NoGeometry
    {
        public string Name = "nowhere";
    }

    sealed class TwoGeometries
    {
        public NtsGeometry? Outline;
        public NtsGeometry? Centre;
    }

    sealed class TwoGeometriesOneNamed
    {
        public NtsGeometry? Geometry;
        public NtsGeometry? Centre;
    }

    static Landmark[] One() => new[] { new Landmark { Geometry = Square } };

    // ---------- the tests ----------

    [Fact]
    public void A_records_property_names_become_the_attribute_keys()
    {
        var node = new ToFeaturesNode();

        var features = node.Update(out var status, out _, One()).ToArray();

        Assert.Single(features);
        Assert.Equal("Kyoto Station", features[0].Attributes["Name"]);
        Assert.Contains("1 features", status);
    }

    /// <summary>
    /// The geometry property is consumed as the geometry, not left behind as an attribute.
    /// </summary>
    [Fact]
    public void The_geometry_property_does_not_also_become_an_attribute()
    {
        var features = new ToFeaturesNode().Update(out _, out _, One()).ToArray();

        Assert.False(features[0].Attributes.Exists("Geometry"));
        Assert.Equal(Square, features[0].Geometry);
    }

    /// <summary>
    /// A property the patch calls <c>Some Field</c> is looked up as <c>Some Field</c>, space and all.
    /// </summary>
    [Fact]
    public void A_space_in_a_property_name_survives()
    {
        var features = new ToFeaturesNode().Update(out _, out _, One()).ToArray();

        Assert.Equal("station", features[0].Attributes["Some Field"]);
        Assert.False(features[0].Attributes.Exists("Some_Field"));
    }

    [Fact]
    public void A_null_valued_property_is_left_out_rather_than_stored_as_null()
    {
        var features = new ToFeaturesNode().Update(out _, out _, One()).ToArray();

        Assert.False(features[0].Attributes.Exists("Missing"));
    }

    // ---------- the convention has to be loud ----------

    /// <summary>
    /// And it names what it DID find, which is the difference between a message and a diagnosis.
    /// </summary>
    /// <remarks>
    /// The first version of this node said only "has no geometry", and when a hand-authored record
    /// hit it in vvvv that sentence was true and useless — it ruled nothing out. Listing the members
    /// it saw, with their types, turns the same failure into an answer.
    /// </remarks>
    [Fact]
    public void A_type_with_no_geometry_says_so_and_lists_what_it_found()
    {
        var features = new ToFeaturesNode().Update(out var status, out _, new[] { new NoGeometry() });

        Assert.Empty(features);
        Assert.Contains("NoGeometry", status);
        Assert.Contains("no property of type Geometry", status);
        Assert.Contains("Name:String", status);          // the member it did see, and its type
    }

    [Fact]
    public void Two_geometry_properties_with_no_obvious_winner_is_reported_as_ambiguous()
    {
        var features = new ToFeaturesNode().Update(out var status, out _,
            new[] { new TwoGeometries { Outline = Square, Centre = Square } });

        Assert.Empty(features);
        Assert.Contains("ambiguous", status);
        Assert.Contains("Outline", status);
    }

    [Fact]
    public void Two_geometry_properties_are_fine_when_one_is_called_Geometry()
    {
        var features = new ToFeaturesNode().Update(out var status, out _,
            new[] { new TwoGeometriesOneNamed { Geometry = Square, Centre = Square } }).ToArray();

        Assert.Single(features);
        Assert.Equal(Square, features[0].Geometry);
        Assert.DoesNotContain("ambiguous", status);
    }

    [Fact]
    public void A_value_with_a_null_geometry_is_skipped_and_counted()
    {
        var features = new ToFeaturesNode().Update(out var status, out _,
            new[] { new Landmark { Geometry = Square }, new Landmark { Geometry = null } }).ToArray();

        Assert.Single(features);
        Assert.Contains("1 skipped", status);
    }

    // ---------- the part that decides whether a thousand of these are usable ----------

    /// <summary>
    /// The same spread across a hundred frames is converted once.
    /// </summary>
    /// <remarks>
    /// This is the node's reason for holding state. Object identity is the only change signal VL
    /// has — measured elsewhere in this suite — so a spread built once, in a `Cache` region or read
    /// from a file, must cost nothing after the first frame.
    /// </remarks>
    [Fact]
    public void A_hundred_frames_of_the_same_spread_convert_once()
    {
        var node = new ToFeaturesNode();
        var data = One();

        for (var frame = 0; frame < 100; frame++) node.Update(out _, out _, data);

        Assert.Equal(1, node.Conversions);
    }

    /// <summary>
    /// And the result keeps ITS identity too, so the layer downstream can skip its work as well.
    /// </summary>
    [Fact]
    public void An_unchanged_input_returns_the_very_same_spread()
    {
        var node = new ToFeaturesNode();
        var data = One();

        var first = node.Update(out _, out _, data);
        var second = node.Update(out _, out _, data);

        Assert.Same(first, second);
    }

    [Fact]
    public void A_different_spread_is_converted_again()
    {
        var node = new ToFeaturesNode();

        node.Update(out _, out _, One());
        node.Update(out _, out _, One());

        Assert.Equal(2, node.Conversions);
    }

    [Fact]
    public void Nothing_connected_gives_no_features_and_says_so()
    {
        var features = new ToFeaturesNode().Update<Landmark>(out var status, out _, null);

        Assert.Empty(features);
        Assert.Contains("nothing connected", status);
    }

    // ---------- a patched record, through a double ----------

    [Fact]
    public void A_patched_records_own_property_names_are_used()
    {
        var features = new ToFeaturesNode().Update(out _, out _, new[] { new FakeRecord(Square) }).ToArray();

        Assert.Single(features);
        Assert.Equal(Square, features[0].Geometry);
        Assert.Equal("Nijo Castle", features[0].Attributes["Name"]);
        Assert.Equal("castle", features[0].Attributes["Some Field"]);
        Assert.False(features[0].Attributes.Exists("Some_Field"));
    }

    /// <summary>
    /// A record shaped the way the vvvv EDITOR shapes one — which is the shape that broke.
    /// </summary>
    /// <remarks>
    /// **Its properties are deliberately invisible to CLR reflection.** They live in a private
    /// `_state`, reachable only through `IVLObject.Type.Properties`, exactly as a patched record's
    /// values live inside `__State` at runtime in the editor. The public CLR surface is the junk VL
    /// puts there — measured on 2026-08-15, a real `Landmark` record reported
    /// `__State:Object, Context:NodeContext, Identity:UInt32, __Program__:VLObjectProgram`.
    ///
    /// The earlier version of this double had real public fields, so CLR reflection found the
    /// geometry and the test passed while the patch drew nothing. A double that is easier to read
    /// than the real thing tests the wrong thing.
    /// </remarks>
    sealed class FakeRecord : IVLObject
    {
        readonly (NtsGeometry Geometry, string Name, string Kind) _state;

        public FakeRecord(NtsGeometry geometry) => _state = (geometry, "Nijo Castle", "castle");

        // The only public CLR members, mirroring what the editor actually exposes.
        public object __State => _state;
        public uint Identity => 1;

        public IVLTypeInfo Type => new FakeType(
            new FakeProperty("Geometry", "Geometry", typeof(NtsGeometry), o => ((FakeRecord)o)._state.Geometry),
            new FakeProperty("Name", "Name", typeof(string), o => ((FakeRecord)o)._state.Name),
            // What a patch property called "Some Field" compiles to.
            new FakeProperty("Some_Field", "Some Field", typeof(string), o => ((FakeRecord)o)._state.Kind));

        public ServiceRegistry Services => throw new NotSupportedException();
        public AppHost AppHost => throw new NotSupportedException();
        public NodeContext Context => throw new NotSupportedException();
        public IVLObject With(IReadOnlyDictionary<string, object> values) => throw new NotSupportedException();
        public object ReadProperty(string key) => throw new NotSupportedException();
    }

    sealed class FakeProperty : IVLPropertyInfo
    {
        readonly Func<object, object?> _read;

        public FakeProperty(string nameForTextualCode, string originalName, Type clrType, Func<object, object?> read)
        {
            NameForTextualCode = nameForTextualCode;
            OriginalName = originalName;
            Type = new FakePropertyType(clrType);
            _read = read;
        }

        public string NameForTextualCode { get; }
        public string OriginalName { get; }
        public string Name => NameForTextualCode;
        public object GetValue(object instance) => _read(instance)!;

        public IVLTypeInfo DeclaringType => throw new NotSupportedException();
        public uint Id => 0;
        public IVLTypeInfo Type { get; }
        public bool IsManaged => false;
        public bool ShouldBeSerialized => true;
        public object WithValue(object instance, object? value) => throw new NotSupportedException();
        public Spread<Attribute> Attributes => Spread<Attribute>.Empty;
        public IEnumerable<TAttribute> GetAttributes<TAttribute>() where TAttribute : Attribute
            => Enumerable.Empty<TAttribute>();
    }

    /// <summary>The bare minimum a property's type has to answer: its CLR type and a name.</summary>
    sealed class FakePropertyType : IVLTypeInfo
    {
        public FakePropertyType(Type clrType) => ClrType = clrType;

        public Type ClrType { get; }
        public string Name => ClrType.Name;

        public Spread<IVLPropertyInfo> Properties => Spread<IVLPropertyInfo>.Empty;
        public Spread<IVLPropertyInfo> AllProperties => Properties;
        public IVLPropertyInfo? GetProperty(string name) => null;
        public string Category => "Test";
        public string FullName => Name;
        public UniqueId Id => default;
        public bool IsPatched => false;
        public bool IsClass => false;
        public bool IsRecord => false;
        public bool IsImmutable => true;
        public bool IsInterface => false;
        public string ToString(bool includeCategory) => Name;
        public object CreateInstance(NodeContext context, IReadOnlyDictionary<string, object?>? arguments = null)
            => throw new NotSupportedException();
        public object GetDefaultValue() => throw new NotSupportedException();
        public IVLTypeInfo MakeGenericType(IReadOnlyList<IVLTypeInfo> arguments) => throw new NotSupportedException();
        public Spread<Attribute> Attributes => Spread<Attribute>.Empty;
        public IEnumerable<TAttribute> GetAttributes<TAttribute>() where TAttribute : Attribute
            => Enumerable.Empty<TAttribute>();
    }

    sealed class FakeType : IVLTypeInfo
    {
        public FakeType(params IVLPropertyInfo[] properties) => Properties = properties.ToSpread();

        public Spread<IVLPropertyInfo> Properties { get; }
        public Spread<IVLPropertyInfo> AllProperties => Properties;
        public IVLPropertyInfo? GetProperty(string name) =>
            Properties.FirstOrDefault(p => p.OriginalName == name || p.NameForTextualCode == name);

        public string Name => "FakeRecord";
        public string Category => "Test";
        public string FullName => "FakeRecord [Test]";
        public UniqueId Id => default;
        public Type ClrType => typeof(FakeRecord);
        public bool IsPatched => true;
        public bool IsClass => false;
        public bool IsRecord => true;
        public bool IsImmutable => true;
        public bool IsInterface => false;
        public string ToString(bool includeCategory) => FullName;
        public object CreateInstance(NodeContext context, IReadOnlyDictionary<string, object?>? arguments = null)
            => throw new NotSupportedException();
        public object GetDefaultValue() => throw new NotSupportedException();
        public IVLTypeInfo MakeGenericType(IReadOnlyList<IVLTypeInfo> arguments) => throw new NotSupportedException();
        public Spread<Attribute> Attributes => Spread<Attribute>.Empty;
        public IEnumerable<TAttribute> GetAttributes<TAttribute>() where TAttribute : Attribute
            => Enumerable.Empty<TAttribute>();
    }
}
