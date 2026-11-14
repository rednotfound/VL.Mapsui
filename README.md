# VL.Mapsui

Maps for vvvv gamma, built on [Mapsui](https://mapsui.com):

* Tile layers: OpenStreetMap or any XYZ tile service, cached on disk
* Your own geometry and features on top of the map, with a lat/lon graticule
* Styles by geometry type or by a value, labels, layers shown only at some zooms
* Navigation: center, zoom, drag and wheel, framing your data
* Picking what is under the mouse, converting between screen pixels and degrees
* Widgets: scale bar, zoom buttons
* The map is drawn into VL.Skia, and the mouse is yours to wire

For use with vvvv, the visual live-programming environment for .NET: http://vvvv.org

## Getting started
- Install as [described here](https://thegraybook.vvvv.org/reference/hde/managing-nugets.html) via commandline:

    `nuget install VL.Mapsui -pre`

- Usage examples and more information are included in the pack and can be found via the [Help Browser](https://thegraybook.vvvv.org/reference/hde/findinghelp.html). Start with *Explanation Overview of available nodes*, then *HowTo Show a map*; every node opens its own example with **F1**.
- What changed in each version: [Releases](https://github.com/rednotfound/VL.Mapsui/releases)

## Map tiles
- Tiles are fetched from the internet, so every node that fetches starts with **Enabled** off: switch it on.
- Drawn tiles are cached under `%LOCALAPPDATA%\VL.Mapsui\tiles` for 7 days; delete the folder to reset.
- Each tile service sets its own terms. The credit prints on the map by itself; for an `XYZ` layer, fill in its `Attribution` pin.

## Related packages
- [VL.NetTopologySuite](https://github.com/rednotfound/VL.NetTopologySuite): make the geometry drawn on the map
- [VL.NetTopologySuite.GeoJSON](https://github.com/rednotfound/VL.NetTopologySuite.GeoJSON): read and write GeoJSON
- [VL.PROJ](https://github.com/rednotfound/VL.PROJ): move coordinates from one coordinate system to another

## Contributing
- Report issues on [GitHub](https://github.com/rednotfound/VL.Mapsui/issues)
- How the package is built, tested and released, and why it is shaped the way it is: [the guide](https://github.com/rednotfound/VL.Mapsui/blob/main/docs/GUIDE.md)
- When making a pull-request, please make sure to read the general [guidelines on contributing to vvvv libraries](https://thegraybook.vvvv.org/reference/extending/contributing.html)

## Credits
Based on [Mapsui](https://github.com/Mapsui/Mapsui) (MIT) and [BruTile](https://github.com/BruTile/BruTile) (Apache-2.0), with [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) (BSD-3-Clause) for geometry. Map data from OpenStreetMap is © OpenStreetMap contributors, available under the [ODbL](https://www.openstreetmap.org/copyright).

## License
[MIT](https://github.com/rednotfound/VL.Mapsui/blob/main/LICENSE)
