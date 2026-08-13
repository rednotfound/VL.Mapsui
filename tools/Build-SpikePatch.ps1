# Authors spike\Spike.vl.
#
# The document is emitted whole, every time. Editing a .vl in place is how VL.GIS once turned a
# working patch into thirteen duplicated nodes with six colliding IDs: a regex anchor matched
# more often than expected and vvvv had rewritten the IDs in the background anyway.
#
# Every node reference below is copied from a document vvvv itself wrote, not invented.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$target   = Join-Path $RepoRoot 'spike\Spike.vl'

$keys = @(
    'doc','patch','canvas','app','inPatch','inCanvas','pCreate','pUpdate','procDef','frag1','frag2',
    'depCoreLib','depSkia','depProject',
    'padNote','padLon','padLat','padZoom','padSpace','padClient','padColor',
    'nDiag','diagOut',
    'nMap','mapLon','mapLat','mapZoom','mapOut',
    'nLayer','layMap','layDiag','layOut','padDiag','padEnabled','mapEnabled',
    'nR1','r1Bounds','r1Bound','r1Input','r1Color','r1Clear','r1Space','r1Cursor','r1VSync','r1Enabled','r1Form','r1Client','r1Time',
    'nR2','r2Bounds','r2Bound','r2Input','r2Color','r2Clear','r2Space','r2Cursor','r2VSync','r2Enabled','r2Form','r2Client','r2Time'
)
$linkKeys = 1..11 | ForEach-Object { "l$_" }

$ids = @(& (Join-Path $PSScriptRoot 'New-VLId.ps1') -Count ($keys.Count + $linkKeys.Count))
$id = @{}; $i = 0
foreach ($k in $keys)     { $id[$k]   = $ids[$i]; $i++ }
$link = @{}
foreach ($k in $linkKeys) { $link[$k] = $ids[$i]; $i++ }

$note = @'
VL.Mapsui spike. One question: does a Mapsui map reach the screen inside vvvv?&#xD;&#xA;&#xD;&#xA;LEFT window is the probe, and it draws nothing but what it measured. Read it before looking at the map. An orange 200x120 box at pixel (40,40) plus a few lines of text means pixel-space handling works, and anything still wrong after that is Mapsui's. No box at all means the layer never rendered. A box of the wrong size means the matrix is wrong.&#xD;&#xA;&#xD;&#xA;Change the Space dropdown and the box must NOT move. That is the point: the layer resets the canvas matrix itself instead of trusting a pin whose wrong value is silently replaced by the default.&#xD;&#xA;&#xD;&#xA;RIGHT window is the map, and it is OFF until you switch Enabled on. Opening a document in vvvv runs it, so a map that fetched on open would give you no chance to decline.&#xD;&#xA;&#xD;&#xA;When you do switch it on, watch the first line of the overlay: maps built must reach 1 and STOP. If it climbs, close vvvv at once - a map is being rebuilt every frame, which starts a fresh round of tile requests each time. That exhausted a machine of ephemeral ports and took a home network down. OpenStreetMap runs on donated infrastructure and its tile policy forbids bulk requests.&#xD;&#xA;&#xD;&#xA;Do not launch vvvv with VL.GIS loaded: it uses BruTile 6 and Mapsui.Tiling needs BruTile 5.
'@ -replace "`r?`n", ''

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<Document xmlns:p="property" xmlns:r="reflection" Id="$($id.doc)" LanguageVersion="2024.6.7-0009-ga0a8422da0" Version="0.128">
  <NugetDependency Id="$($id.depCoreLib)" Location="VL.CoreLib" Version="2025.7.0" />
  <NugetDependency Id="$($id.depSkia)" Location="VL.Skia" Version="2025.7.0" />
  <Patch Id="$($id.patch)">
    <Canvas Id="$($id.canvas)" DefaultCategory="Main" CanvasType="FullCategory" />
    <Node Name="Application" Bounds="100,100" Id="$($id.app)">
      <p:NodeReference>
        <Choice Kind="ContainerDefinition" Name="Process" />
        <FullNameCategoryReference ID="Primitive" />
      </p:NodeReference>
      <Patch Id="$($id.inPatch)">
        <Canvas Id="$($id.inCanvas)" CanvasType="Group">

          <Pad Id="$($id.padNote)" Bounds="60,30,600,150" ShowValueBox="true" isIOBox="true" Value="$note">
            <p:TypeAnnotation>
              <Choice Kind="TypeFlag" Name="String" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:fontsize p:Type="Int32">11</p:fontsize>
              <p:stringtype p:Assembly="VL.Core" p:Type="VL.Core.StringType">Comment</p:stringtype>
            </p:ValueBoxSettings>
          </Pad>

          <!--
            Probe first. This layer takes no inputs at all, so if it draws nothing the fault is
            upstream of anything we could have got wrong about Mapsui.
          -->
          <Node Bounds="60,220,140,19" Id="$($id.nDiag)">
            <p:NodeReference LastCategoryFullName="Mapsui.Map" LastDependency="VL.Mapsui.csproj">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="DiagnosticsLayer" />
            </p:NodeReference>
            <Pin Id="$($id.diagOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <Pad Id="$($id.padSpace)" Comment="Space" Bounds="240,220,110,19" ShowValueBox="true" isIOBox="true" Value="Normalized">
            <p:TypeAnnotation LastCategoryFullName="VL.Skia" LastDependency="VL.Skia.dll">
              <Choice Kind="TypeFlag" Name="CommonSpace" />
            </p:TypeAnnotation>
          </Pad>

          <!--
            ClientBounds reports in the current space's own units, so it is the only direct
            answer to "which space is actually in effect". Worth reading here because VL.Skia's
            Sizing docs say one unit is 100 pixels, which VL.GIS's notes currently contradict.
          -->
          <Pad Id="$($id.padClient)" Comment="ClientBounds" Bounds="380,220,200,44" ShowValueBox="true" isIOBox="true" />

          <Pad Id="$($id.padColor)" Comment="Background" Bounds="60,270,143,20" ShowValueBox="true" isIOBox="true" Value="0.1, 0.1, 0.15, 1">
            <p:TypeAnnotation LastCategoryFullName="Color" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="RGBA" />
            </p:TypeAnnotation>
          </Pad>

          <Node Bounds="60,320,165,19" Id="$($id.nR1)">
            <p:NodeReference LastCategoryFullName="Graphics.Skia" LastDependency="VL.Skia.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="Renderer" />
            </p:NodeReference>
            <Pin Id="$($id.r1Bounds)" Name="Bounds" Kind="InputPin" DefaultValue="80, 120, 640, 520">
              <p:TypeAnnotation LastCategoryFullName="System.Drawing" LastDependency="System.Drawing.dll">
                <Choice Kind="TypeFlag" Name="Rectangle" />
              </p:TypeAnnotation>
            </Pin>
            <Pin Id="$($id.r1Bound)" Name="Bound to Document" Kind="InputPin" DefaultValue="True">
              <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
                <Choice Kind="TypeFlag" Name="Boolean" />
              </p:TypeAnnotation>
            </Pin>
            <Pin Id="$($id.r1Input)" Name="Input" Kind="InputPin" />
            <Pin Id="$($id.r1Color)" Name="Color" Kind="InputPin" />
            <Pin Id="$($id.r1Clear)" Name="Clear" Kind="InputPin" />
            <Pin Id="$($id.r1Space)" Name="Space" Kind="InputPin" />
            <Pin Id="$($id.r1Cursor)" Name="Show Cursor" Kind="InputPin" />
            <Pin Id="$($id.r1VSync)" Name="VSync" Kind="InputPin" />
            <Pin Id="$($id.r1Enabled)" Name="Enabled" Kind="InputPin" />
            <Pin Id="$($id.r1Form)" Name="Form" Kind="OutputPin" />
            <Pin Id="$($id.r1Client)" Name="ClientBounds" Kind="OutputPin" />
            <Pin Id="$($id.r1Time)" Name="Render Time" Kind="OutputPin" />
          </Node>

          <!-- The map itself, in its own window so the probe stays readable. -->
          <Pad Id="$($id.padLon)" Comment="Longitude" Bounds="700,220,62,15" ShowValueBox="true" isIOBox="true" Value="139.7">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="Float64" />
            </p:TypeAnnotation>
          </Pad>
          <Pad Id="$($id.padLat)" Comment="Latitude" Bounds="780,220,62,15" ShowValueBox="true" isIOBox="true" Value="35.68">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="Float64" />
            </p:TypeAnnotation>
          </Pad>
          <Pad Id="$($id.padZoom)" Comment="Zoom Level" Bounds="860,220,50,15" ShowValueBox="true" isIOBox="true" Value="12">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="Integer32" />
            </p:TypeAnnotation>
          </Pad>

          <!--
            A process node, not an operation. Written as a static method this was re-evaluated
            every frame, so it built a fresh map and tile layer sixty times a second and never
            released one: 17,000 TCP connections, the machine's ephemeral ports exhausted, and
            a home network taken down. It is also why nothing was ever drawn, since tiles kept
            arriving for a map that had already been discarded.

            ProcessAppFlag is what makes vvvv build it once and call Update per frame.
          -->
          <Node Bounds="700,270,180,19" Id="$($id.nMap)">
            <p:NodeReference LastCategoryFullName="Mapsui" LastDependency="VL.Mapsui.csproj">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="OpenStreetMap" />
            </p:NodeReference>
            <Pin Id="$($id.mapLon)" Name="Center Longitude" Kind="InputPin" />
            <Pin Id="$($id.mapLat)" Name="Center Latitude" Kind="InputPin" />
            <Pin Id="$($id.mapZoom)" Name="Zoom Level" Kind="InputPin" />
            <Pin Id="$($id.mapEnabled)" Name="Enabled" Kind="InputPin" />
            <Pin Id="$($id.layDiag)" Name="Diagnostics" Kind="InputPin" />
            <Pin Id="$($id.mapOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <!--
            Off by default, and that is the point. In vvvv opening a document runs it, so a map
            that fetches on open gives whoever opened it no chance to decline. Switch it on
            deliberately, watch the "maps built" counter stay at 1, and switch it off again.
          -->
          <Pad Id="$($id.padEnabled)" Comment="Enabled" Bounds="960,220,35,35" ShowValueBox="true" isIOBox="true" Value="false">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Toggle</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>

          <!--
            On by default while bringing the package up. An empty map window says nothing about
            why it is empty; a resolution of 0 says Home never ran, and an empty layer list says
            the map was never populated.
          -->
          <Pad Id="$($id.padDiag)" Comment="Diagnostics" Bounds="900,220,35,35" ShowValueBox="true" isIOBox="true" Value="true">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Toggle</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>

          <Node Bounds="700,360,165,19" Id="$($id.nR2)">
            <p:NodeReference LastCategoryFullName="Graphics.Skia" LastDependency="VL.Skia.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="Renderer" />
            </p:NodeReference>
            <Pin Id="$($id.r2Bounds)" Name="Bounds" Kind="InputPin" DefaultValue="760, 120, 640, 520">
              <p:TypeAnnotation LastCategoryFullName="System.Drawing" LastDependency="System.Drawing.dll">
                <Choice Kind="TypeFlag" Name="Rectangle" />
              </p:TypeAnnotation>
            </Pin>
            <Pin Id="$($id.r2Bound)" Name="Bound to Document" Kind="InputPin" DefaultValue="True">
              <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
                <Choice Kind="TypeFlag" Name="Boolean" />
              </p:TypeAnnotation>
            </Pin>
            <Pin Id="$($id.r2Input)" Name="Input" Kind="InputPin" />
            <Pin Id="$($id.r2Color)" Name="Color" Kind="InputPin" />
            <Pin Id="$($id.r2Clear)" Name="Clear" Kind="InputPin" />
            <Pin Id="$($id.r2Space)" Name="Space" Kind="InputPin" />
            <Pin Id="$($id.r2Cursor)" Name="Show Cursor" Kind="InputPin" />
            <Pin Id="$($id.r2VSync)" Name="VSync" Kind="InputPin" />
            <Pin Id="$($id.r2Enabled)" Name="Enabled" Kind="InputPin" />
            <Pin Id="$($id.r2Form)" Name="Form" Kind="OutputPin" />
            <Pin Id="$($id.r2Client)" Name="ClientBounds" Kind="OutputPin" />
            <Pin Id="$($id.r2Time)" Name="Render Time" Kind="OutputPin" />
          </Node>

        </Canvas>
        <Patch Id="$($id.pCreate)" Name="Create" />
        <Patch Id="$($id.pUpdate)" Name="Update" />
        <ProcessDefinition Id="$($id.procDef)">
          <Fragment Id="$($id.frag1)" Patch="$($id.pCreate)" Enabled="true" />
          <Fragment Id="$($id.frag2)" Patch="$($id.pUpdate)" Enabled="true" />
        </ProcessDefinition>
        <Link Id="$($link.l1)" Ids="$($id.diagOut),$($id.r1Input)" />
        <Link Id="$($link.l2)" Ids="$($id.padSpace),$($id.r1Space)" />
        <Link Id="$($link.l3)" Ids="$($id.padColor),$($id.r1Color)" />
        <Link Id="$($link.l4)" Ids="$($id.r1Client),$($id.padClient)" />
        <Link Id="$($link.l5)" Ids="$($id.padLon),$($id.mapLon)" />
        <Link Id="$($link.l6)" Ids="$($id.padLat),$($id.mapLat)" />
        <Link Id="$($link.l7)" Ids="$($id.padZoom),$($id.mapZoom)" />
        <Link Id="$($link.l8)" Ids="$($id.padDiag),$($id.layDiag)" />
        <Link Id="$($link.l9)" Ids="$($id.mapOut),$($id.r2Input)" />
        <Link Id="$($link.l11)" Ids="$($id.padEnabled),$($id.mapEnabled)" />
      </Patch>
    </Node>
  </Patch>
  <ProjectDependency Id="$($id.depProject)" Location="../src/VL.Mapsui/VL.Mapsui.csproj" />
</Document>
"@

# Check the document before writing it, not after. A .vl that fails to parse is not obviously
# broken from inside vvvv - it simply does not load - so every mistake caught here is a round
# trip saved.
#
# XML forbids "--" inside a comment, everywhere and not just in MSBuild.
foreach ($m in [regex]::Matches($xml, '(?s)<!--(.*?)-->')) {
    if ($m.Groups[1].Value -match '--') { throw "XML comment contains '--'" }
}

# And parse the whole thing. A stray double quote inside an attribute value ends the attribute
# early, which is how a sentence with a quoted phrase in it once broke this document.
try { $null = [xml]$xml }
catch { throw "generated document is not well-formed XML: $($_.Exception.Message)" }

# UTF-8 *with* BOM. vvvv will not load the document without it.
[IO.File]::WriteAllText($target, $xml, (New-Object System.Text.UTF8Encoding($true)))
Write-Host "wrote $target"
