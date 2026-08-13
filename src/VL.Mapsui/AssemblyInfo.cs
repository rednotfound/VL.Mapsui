using System.Runtime.CompilerServices;
using VL.Core.Import;

// The counters and helpers the tests assert on are internal on purpose: they exist to catch a
// regression, not to be part of the node surface.
[assembly: InternalsVisibleTo("VL.Mapsui.Tests")]

// Without this every public static method is demoted to a raw .NET reflection node, which the
// NodeBrowser hides. The package still loads, compiles and packs with zero warnings, so the
// symptom is indistinguishable from the package not loading at all. Nine VL.GIS releases
// shipped that way.
[assembly: ImportAsIs(Namespace = "VL")]
