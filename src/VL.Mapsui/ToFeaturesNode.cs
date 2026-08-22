using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using VL.Core;
using VL.Core.Import;

using NtsFeature = NetTopologySuite.Features.Feature;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using VLName = VL.Core.Import.NameAttribute;

namespace VL.Mapsui;

/// <summary>
/// Your own data type, drawn on the map: one feature per value, geometry and attributes taken from
/// its properties.
/// </summary>
/// <remarks>
/// **This is the node for having a lot of data.** Define a record once — right-click the canvas,
/// `Record`, give it a `Geometry` property and whatever else describes it — and every place is then
/// an *instance* rather than another length of wiring. A thousand of them is a spread, and the patch
/// is the same size as it was for three.
///
/// The whole chain is `Spread&lt;YourRecord&gt; → ToFeatures → FeatureLayer`.
///
/// **Which property is the geometry: the one whose type is a NetTopologySuite `Geometry`.** If there
/// are several, the one actually named `Geometry` wins. If there is none, or the choice is ambiguous,
/// `Status` says so — a convention that fails quietly is worse than no convention, and this package
/// has paid for that lesson five times.
///
/// **Every other property becomes an attribute**, keyed by the name the patch shows. So a record with
/// `Name` and `Type` is labelled by `LabelStyle` with `Name` — the record is the only place either
/// word is written.
///
/// **Building the data once is what makes a thousand features cheap, and that is the patch's job,
/// not this node's.** Measured on 1,000 features of 500 vertices: a spread built once costs
/// **0.013 ms/frame** downstream, while rebuilding those features every frame costs **43 ms/frame**
/// just to make them. Wrap the construction in a `Cache` region — VL.Skia's own help gives the same
/// advice for paths — or read the data from a file once. A record helps here too, because a record
/// held in a pad keeps its identity, and object identity is the only "has this changed" signal VL
/// has.
///
/// A process node because it keeps two caches: the property list per type, so a thousand records are
/// not reflected over from scratch every frame, and the last result, so an unchanged input returns
/// the same spread instance and lets everything downstream skip its work too.
/// </remarks>
[ProcessNode(Name = "ToFeatures", Category = "Mapsui")]
public class ToFeaturesNode
{
    // Keyed by type, not by instance: the shape of a record never changes at runtime.
    static readonly ConcurrentDictionary<Type, Reader> Readers = new();

    object? _lastInput;
    NtsFeature[] _lastResult = Array.Empty<NtsFeature>();
    string _status = "nothing connected";

    /// <summary>
    /// Times the input was actually converted — a counter the patch can see, because "this runs
    /// once" is not a claim anyone should take on trust.
    /// </summary>
    /// <remarks>
    /// The same idea as `Layers Built`: the expensive mistake in this package was never a wrong
    /// number, it was work happening at the wrong *rate*, and a rate is invisible unless something
    /// counts it. Build the data in a `Create` fragment or a `Cache` region and this settles at 1;
    /// leave it in `Update` and it climbs once per frame.
    /// </remarks>
    internal int Conversions { get; private set; }

    /// <summary>
    /// One feature per value, ready for <see cref="FeatureLayerNode"/>.
    /// </summary>
    /// <remarks>
    /// Values that carry no geometry are skipped rather than dropped silently — the count is on
    /// `Status`.
    ///
    /// The result is the *same* array when the input is the same object, so a `FeatureLayer`
    /// downstream sees an unchanged spread and does nothing. That is the whole reason this is worth
    /// being a process node.
    /// </remarks>
    public IEnumerable<NtsFeature> Update<T>(out string status, out int conversions, IEnumerable<T>? values = null)
    {
        if (values is null)
        {
            _lastInput = null;
            _lastResult = Array.Empty<NtsFeature>();
            status = _status = "nothing connected";
            conversions = Conversions;
            return _lastResult;
        }

        // Identity is VL's change signal: a spread built once - in a Cache region, or read from a
        // file - arrives as the same object every frame, and then there is nothing to do.
        if (ReferenceEquals(values, _lastInput))
        {
            status = _status;
            conversions = Conversions;
            return _lastResult;
        }

        // The reader has to come from the first VALUE, not from typeof(T), because inside the vvvv
        // editor a patched record has no CLR members to reflect over - see Reader.For(IVLObject).
        var first = values.FirstOrDefault(v => v is not null);
        var reader = ReaderFor(first, typeof(T));
        var describes = reader.TypeName ?? typeof(T).Name;

        if (reader.Problem is { } problem)
        {
            _lastInput = values;
            _lastResult = Array.Empty<NtsFeature>();
            status = _status = problem;
            conversions = Conversions;
            return _lastResult;
        }

        var features = new List<NtsFeature>();
        var withoutGeometry = 0;

        foreach (var value in values)
        {
            if (value is null) { withoutGeometry++; continue; }

            var feature = reader.Read(value);
            if (feature is null) withoutGeometry++;
            else features.Add(feature);
        }

        _lastInput = values;
        _lastResult = features.ToArray();
        Conversions++;

        status = _status = withoutGeometry == 0
            ? $"{_lastResult.Length} features from {describes}"
            : $"{_lastResult.Length} features from {describes}, {withoutGeometry} skipped for having no geometry";

        conversions = Conversions;
        return _lastResult;
    }

    /// <summary>
    /// The reader for whatever kind of value this is, cached so a thousand of them cost one lookup.
    /// </summary>
    /// <remarks>
    /// **A patched record must be read through VL, not through reflection.** Inside the vvvv editor a
    /// record instance has no CLR members corresponding to its properties at all — measured
    /// 2026-08-15, a hand-authored `Landmark` record with `Name`, `Type` and `Geometry` reported its
    /// public members as `__State:Object, Context:NodeContext, Identity:UInt32,
    /// __Program__:VLObjectProgram`. The values live inside `__State`, and only
    /// `IVLObject.Type.Properties` can see them.
    ///
    /// The exported form is different — `vvvvc` emits real `public string Name;` fields — which is
    /// why reading the generated C# was not enough to catch this. Two shapes for one record, and the
    /// editor's is the one that has to work.
    ///
    /// Nothing is cached for the null case: a spread of nothing has no type to learn.
    /// </remarks>
    static Reader ReaderFor(object? first, Type declared)
    {
        if (first is IVLObject patched)
            return Readers.GetOrAdd(patched.Type.ClrType ?? declared, _ => Reader.For(patched.Type));

        return Readers.GetOrAdd(first?.GetType() ?? declared, static type => Reader.For(type));
    }

    /// <summary>
    /// How to turn one value of a given type into a feature — worked out once per type.
    /// </summary>
    /// <remarks>
    /// Reflecting over a thousand records every frame would be the same mistake as rebuilding them
    /// every frame, one layer down. The property list is settled the first time a type is seen.
    ///
    /// Two ways in, because VL has two kinds of value: a **patched record** carries its own
    /// `IVLObject.Type.Properties`, which needs no `AppHost` and gives the names the patch shows; a
    /// plain .NET object is read with `System.Reflection`, applying VL's own naming rule
    /// (`[Name("Some Field")]` when present, the member name otherwise).
    /// </remarks>
    sealed class Reader
    {
        Func<object, NtsGeometry?>? _geometry;
        (string Name, Func<object, object?> Read)[] _attributes = Array.Empty<(string, Func<object, object?>)>();

        /// <summary>Why this type cannot be drawn, or null if it can.</summary>
        public string? Problem { get; private set; }

        /// <summary>What to call this type in a message — VL's name for it, when there is one.</summary>
        public string? TypeName { get; private set; }

        /// <summary>
        /// A reader built from VL's own property model, which is the only one that works on a
        /// patched record inside the editor.
        /// </summary>
        /// <remarks>
        /// `IVLPropertyInfo.Type.ClrType` gives the declared type, so the geometry property is found
        /// without reading a value — which matters, because a record whose geometry happens to be
        /// null on the first instance must still be understood.
        /// </remarks>
        public static Reader For(IVLTypeInfo vlType)
        {
            var reader = new Reader { TypeName = vlType.Name };
            var properties = vlType.Properties.ToArray();

            var geometries = properties
                .Where(p => p.Type?.ClrType is { } clr && typeof(NtsGeometry).IsAssignableFrom(clr))
                .ToArray();

            if (geometries.Length == 0)
            {
                var seen = properties.Length == 0
                    ? "it has no properties at all"
                    : "it has " + string.Join(", ", properties.Select(p => $"{KeyOf(p)}:{p.Type?.Name ?? "?"}"));

                reader.Problem =
                    $"{vlType.Name} has no property of type Geometry, so there is nothing to draw - {seen}. " +
                    "VL.NetTopologySuite makes geometry.";
                return reader;
            }

            var chosen = geometries.Length == 1
                ? geometries[0]
                : geometries.FirstOrDefault(p => KeyOf(p) == "Geometry");

            if (chosen is null)
            {
                reader.Problem =
                    $"{vlType.Name} has {geometries.Length} geometry properties " +
                    $"({string.Join(", ", geometries.Select(KeyOf))}) and none is called Geometry, " +
                    "so which one to draw is ambiguous";
                return reader;
            }

            reader._geometry = value => chosen.GetValue(value) as NtsGeometry;
            reader._attributes = properties
                .Where(p => !ReferenceEquals(p, chosen))
                .Select(p => (KeyOf(p), (Func<object, object?>)(o => p.GetValue(o))))
                .ToArray();

            return reader;
        }

        /// <summary>
        /// A reader for a plain .NET value, built with `System.Reflection`.
        /// </summary>
        /// <remarks>
        /// The fallback. It works for an imported .NET type and for a record that was **exported**
        /// by `vvvvc` — but not for one running in the editor, which has no CLR members to find.
        /// </remarks>
        public static Reader For(Type type)
        {
            var reader = new Reader { TypeName = type.Name };

            // A patched record is read through VL's own property model, but the CLR side is what
            // tells us the TYPES, and both agree on the member names - so one reflection pass over
            // the CLR members is enough to find the geometry, and IVLObject is used per instance
            // only when it is available.
            var members = Members(type).ToArray();
            var geometries = members.Where(m => typeof(NtsGeometry).IsAssignableFrom(m.Type)).ToArray();

            if (geometries.Length == 0)
            {
                // Naming what WAS found, not only what was missing. "no geometry" alone sent an
                // afternoon into guessing which of a dozen things had gone wrong; the member list
                // answers it in one glance, and it is the difference between a message and a
                // diagnosis.
                var seen = members.Length == 0
                    ? "it has no readable public members at all"
                    : "it has " + string.Join(", ", members.Select(m => $"{m.Name}:{m.Type.Name}"));

                reader.Problem =
                    $"{type.Name} has no property of type Geometry, so there is nothing to draw - {seen}. " +
                    "VL.NetTopologySuite makes geometry.";
                return reader;
            }

            var chosen = geometries.Length == 1
                ? geometries[0]
                : geometries.FirstOrDefault(m => m.Name == "Geometry");

            if (chosen.Read is null)
            {
                reader.Problem =
                    $"{type.Name} has {geometries.Length} geometry properties ({string.Join(", ", geometries.Select(g => g.Name))}) " +
                    "and none is called Geometry, so which one to draw is ambiguous";
                return reader;
            }

            reader._geometry = value => chosen.Read(value) as NtsGeometry;
            reader._attributes = members
                .Where(m => m.Name != chosen.Name)
                .Select(m => (m.Name, m.Read))
                .ToArray();

            return reader;
        }

        public NtsFeature? Read(object value)
        {
            if (_geometry?.Invoke(value) is not { } geometry) return null;

            var builder = ImmutableDictionary.CreateBuilder<string, object>();

            // One path now, whichever kind of value this is: whoever built the reader already chose
            // the right accessors and the right names. Branching here on IVLObject was how the
            // geometry ended up being looked for in the wrong place.
            foreach (var (name, read) in _attributes)
                if (read(value) is { } attributeValue)
                    builder[name] = attributeValue;

            return FeatureHelper.Feature(geometry, builder.ToImmutable());
        }

        /// <summary>
        /// `OriginalName` is the name the patch shows — a property called `Some Field` keeps its
        /// space. VL says so itself: the older `Name` is `[Obsolete]` with *"Got replaced by
        /// NameForTextualCode. Also consider using OriginalName, which can contain spaces."*
        /// </summary>
        static string KeyOf(IVLPropertyInfo property)
            => !string.IsNullOrEmpty(property.OriginalName) ? property.OriginalName : property.NameForTextualCode;

        /// <summary>
        /// Public fields and readable properties, with VL's naming applied. Indexers are skipped —
        /// they take parameters and have no single value to read.
        /// </summary>
        static IEnumerable<(string Name, Type Type, Func<object, object?> Read)> Members(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                yield return (NameOf(field, field.Name), field.FieldType, o => field.GetValue(o));

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                    yield return (NameOf(property, property.Name), property.PropertyType, o => property.GetValue(o));
        }

        static string NameOf(MemberInfo member, string fallback)
            => member.GetCustomAttribute<VLName>()?.Name is { Length: > 0 } named ? named : fallback;
    }
}
