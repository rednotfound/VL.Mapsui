using System;
using VL.Core;
using VL.Core.CompilerServices;
using VL.NetTopologySuite;

[assembly: AssemblyInitializer(typeof(_VL_.Mapsui.Initializer))]

// The _VL_ namespace keeps this class out of the NodeBrowser: ImportAsIs(Namespace = "VL") imports
// only VL.*. The shape is VL.IO.Redis's (shipped with vvvv 7.4) and VL.PROJ's.
namespace _VL_.Mapsui;

// Offers OpenStreetMap tiles to VL.NetTopologySuite's GeometryEditor window (see OsmBasemap.cs).
// Into AppHost.GLOBAL's services, not the app host passed in: the window runs in
// VL.NetTopologySuite's HDE companion, an app host of its own that never loads this package, and a
// per-app registry falls back to the global one, not to another app's. Configure runs once per app
// host that loads this package; registering the same instance again is harmless.
//
// Guarded: this package's own nodes need none of this, so a failure here (an older
// VL.NetTopologySuite without IBasemap, no global host) must not take the package down with it.

/// <summary>Offers OpenStreetMap tiles to VL.NetTopologySuite's GeometryEditor window.</summary>
public sealed class Initializer : AssemblyInitializer<Initializer>
{
    /// <summary>Registers the basemap in the global service registry.</summary>
    public override void Configure(AppHost appHost)
    {
        base.Configure(appHost);
        try
        {
            AppHost.Global.Services.RegisterService<IBasemap>(global::VL.Mapsui.OsmBasemap.Instance);
        }
        catch (Exception e)
        {
            try { Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(appHost.DefaultLogger, $"VL.Mapsui could not offer its basemap to VL.NetTopologySuite: {e.Message}"); }
            catch (Exception) { }
        }
    }
}
