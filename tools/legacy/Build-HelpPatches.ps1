# RETIRED - do not run this.
#
# This built the help patch from scratch, which was the right way to get it working: hand-editing
# a .vl went badly elsewhere, and a generator that emits the whole document cannot leave one half
# rewritten. That job is done.
#
# The patch under help\VL.Mapsui\ is now the source of truth. Running this again would overwrite
# it - which already happened once, discarding node positions someone had arranged by hand to
# make the patch readable. Edit the .vl in vvvv instead and let the file keep the change.
#
# Kept because it records where every node reference came from, each copied from a document vvvv
# itself wrote rather than invented.
# Authors help\VL.Mapsui\HowTo Show a map.vl.
#
# The document is emitted whole, every time. Editing a .vl in place is how VL.GIS once turned a
# working patch into thirteen duplicated nodes with six colliding IDs: a regex anchor matched
# more often than expected and vvvv had rewritten the IDs in the background anyway.
#
# Every node reference below is copied from a document vvvv itself wrote, not invented.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$target   = Join-Path $RepoRoot 'help\VL.Mapsui\HowTo Show a map.vl'

$keys = @(
    'doc','patch','canvas','app','inPatch','inCanvas','pCreate','pUpdate','procDef','frag1','frag2',
    'depCoreLib','depSkia','depMapsui',
    'padNote','padEnabled','padCache','padLon','padLat','padZoom','padDiag','padColor',
    'padBuilt','padVpLon','padVpLat','padVpRes',
    'nOsm','osmEnabled','osmCache','osmBuilt','osmOut',
    'nCons','consIn','consIn2','consOut',
    'nMap','mapLayers','mapLon','mapLat','mapZoom','mapOut',
    'nInfo','infoMap','infoLon','infoLat','infoRes','infoW','infoH',
    'nCenter','ctrMap','ctrLon','ctrLat','ctrOut','nZoom','zmMap','zmLevel','zmOut',
    'padNavLon','padNavLat','padNavZoom',
    'nSkia','skMap','skDiag','skOut',
    'nR','rBounds','rBound','rInput','rColor','rClear','rSpace','rCursor','rVSync','rEnabled','rForm','rClient','rTime'
)
$linkKeys = 1..23 | ForEach-Object { "l$_" }

$ids = @(& (Join-Path $PSScriptRoot 'New-VLId.ps1') -Count ($keys.Count + $linkKeys.Count))
$id = @{}; $i = 0
foreach ($k in $keys)     { $id[$k]   = $ids[$i]; $i++ }
$link = @{}
foreach ($k in $linkKeys) { $link[$k] = $ids[$i]; $i++ }

$note = @'
A map, built from separate nodes rather than one node that does everything.&#xD;&#xA;&#xD;&#xA;OpenStreetMap makes a tile layer. Map holds layers and owns the viewport. ToSkiaLayer draws it. Navigate has CenterOn, ZoomToLevel, Drag and ZoomAt for moving it. Nothing here decides for you what the mouse does: read it with VL.Skia MouseState and wire it to Navigate, or drive the map from an LFO, OSC or a keyboard instead. That choice is the reason to be patching at all.&#xD;&#xA;&#xD;&#xA;Enabled starts OFF. Opening a document in vvvv runs it, so a map that fetched on open would give you no chance to decline. Switch it on to see the map.&#xD;&#xA;&#xD;&#xA;Watch Layers Built: it should settle at 1 and stay. A number climbing frame after frame means something rebuilds the layer every frame, and every rebuild starts a fresh round of tile requests. That once exhausted a machine of ephemeral ports and took a home network down. Close vvvv if you see it climb.&#xD;&#xA;&#xD;&#xA;Tiles that were drawn are cached under %LOCALAPPDATA%\VL.Mapsui\tiles, which is what the OpenStreetMap tile policy asks for. A session is a few megabytes. Delete the folder to reset.
'@ -replace "`r?`n", ''

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<Document xmlns:p="property" xmlns:r="reflection" Id="$($id.doc)" LanguageVersion="2024.6.7-0009-ga0a8422da0" Version="0.128">
  <NugetDependency Id="$($id.depCoreLib)" Location="VL.CoreLib" Version="2025.7.0" />
  <NugetDependency Id="$($id.depSkia)" Location="VL.Skia" Version="2025.7.0" />
  <NugetDependency Id="$($id.depMapsui)" Location="VL.Mapsui" Version="0.0.0" />
  <Patch Id="$($id.patch)">
    <Canvas Id="$($id.canvas)" DefaultCategory="Main" CanvasType="FullCategory" />
    <Node Name="Application" Bounds="100,100" Id="$($id.app)">
      <p:NodeReference>
        <Choice Kind="ContainerDefinition" Name="Process" />
        <FullNameCategoryReference ID="Primitive" />
      </p:NodeReference>
      <Patch Id="$($id.inPatch)">
        <Canvas Id="$($id.inCanvas)" CanvasType="Group">

          <Pad Id="$($id.padNote)" Bounds="60,30,620,160" ShowValueBox="true" isIOBox="true" Value="$note">
            <p:TypeAnnotation>
              <Choice Kind="TypeFlag" Name="String" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:fontsize p:Type="Int32">11</p:fontsize>
              <p:stringtype p:Assembly="VL.Core" p:Type="VL.Core.StringType">Comment</p:stringtype>
            </p:ValueBoxSettings>
          </Pad>

          <!--
            Off by default, and that is the point. Opening a document in vvvv runs it.
          -->
          <Pad Id="$($id.padEnabled)" Comment="Enabled" Bounds="60,220,35,35" ShowValueBox="true" isIOBox="true" Value="false">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Toggle</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>
          <Pad Id="$($id.padCache)" Comment="Cache To Disk" Bounds="110,220,35,35" ShowValueBox="true" isIOBox="true" Value="true">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Toggle</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>

          <Node Bounds="60,280,150,19" Id="$($id.nOsm)">
            <p:NodeReference LastCategoryFullName="Mapsui.Layers" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="OpenStreetMap" />
            </p:NodeReference>
            <Pin Id="$($id.osmEnabled)" Name="Enabled" Kind="InputPin" />
            <Pin Id="$($id.osmCache)" Name="Cache To Disk" Kind="InputPin" />
            <Pin Id="$($id.osmBuilt)" Name="Layers Built" Kind="OutputPin" />
            <Pin Id="$($id.osmOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <!--
            The smoke alarm, on a pin rather than hidden in an overlay so the patch can watch it
            or act on it. It should reach 1 and stop.
          -->
          <Pad Id="$($id.padBuilt)" Comment="Layers Built" Bounds="240,280,80,15" ShowValueBox="true" isIOBox="true" />

          <!--
            Map takes a spread of layers, so a single one is wrapped with Cons. That is the
            point of the spread: a patch can put its own layers alongside this one, in an order
            it chooses, rather than being handed a map with a fixed stack inside it.
          -->
          <Node Bounds="240,320,25,19" Id="$($id.nCons)">
            <p:NodeReference LastCategoryFullName="Collections.Spread" LastDependency="VL.CoreLib.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="Cons" />
              <CategoryReference Kind="RecordType" Name="Spread" NeedsToBeDirectParent="true" />
            </p:NodeReference>
            <Pin Id="$($id.consIn)" Name="Input" Kind="InputPin" />
            <Pin Id="$($id.consIn2)" Name="Input 2" Kind="InputPin" />
            <Pin Id="$($id.consOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <Pad Id="$($id.padLon)" Comment="Longitude" Bounds="360,220,62,15" ShowValueBox="true" isIOBox="true" Value="139.7">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="Float64" />
            </p:TypeAnnotation>
          </Pad>
          <Pad Id="$($id.padLat)" Comment="Latitude" Bounds="440,220,62,15" ShowValueBox="true" isIOBox="true" Value="35.68">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="Float64" />
            </p:TypeAnnotation>
          </Pad>
          <Pad Id="$($id.padZoom)" Comment="Zoom Level" Bounds="520,220,50,15" ShowValueBox="true" isIOBox="true" Value="12">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="Integer32" />
            </p:TypeAnnotation>
          </Pad>

          <!--
            The centre and zoom here are the starting view only. They are applied once, when the
            viewport first has a size, and ignored afterwards - otherwise navigating the map
            would be undone by these pins on the very next frame. To move it later, use the
            nodes in Mapsui.Navigate.
          -->
          <Node Bounds="360,280,110,19" Id="$($id.nMap)">
            <p:NodeReference LastCategoryFullName="Mapsui.Maps" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="Map" />
            </p:NodeReference>
            <Pin Id="$($id.mapLayers)" Name="Layers" Kind="InputPin" />
            <Pin Id="$($id.mapLon)" Name="Initial Center Longitude" Kind="InputPin" />
            <Pin Id="$($id.mapLat)" Name="Initial Center Latitude" Kind="InputPin" />
            <Pin Id="$($id.mapZoom)" Name="Initial Zoom Level" Kind="InputPin" />
            <Pin Id="$($id.mapOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <!--
            A Map is opaque in a patch, so it needs a reader or you are holding a value you
            cannot inspect. Reads back what the map is actually looking at, which is not the
            same as the pins above once anything has moved it.
          -->
          <Node Bounds="620,280,110,19" Id="$($id.nInfo)">
            <p:NodeReference LastCategoryFullName="Mapsui" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="ViewportInfo" />
            </p:NodeReference>
            <Pin Id="$($id.infoMap)" Name="Map" Kind="InputPin" />
            <Pin Id="$($id.infoLon)" Name="Center Longitude" Kind="OutputPin" />
            <Pin Id="$($id.infoLat)" Name="Center Latitude" Kind="OutputPin" />
            <Pin Id="$($id.infoRes)" Name="Resolution" Kind="OutputPin" />
            <Pin Id="$($id.infoW)" Name="Width" Kind="OutputPin" />
            <Pin Id="$($id.infoH)" Name="Height" Kind="OutputPin" />
          </Node>
          <Pad Id="$($id.padVpLon)" Comment="Actual Longitude" Bounds="760,270,90,15" ShowValueBox="true" isIOBox="true" />
          <Pad Id="$($id.padVpLat)" Comment="Actual Latitude" Bounds="760,290,90,15" ShowValueBox="true" isIOBox="true" />
          <Pad Id="$($id.padVpRes)" Comment="m per pixel" Bounds="760,310,90,15" ShowValueBox="true" isIOBox="true" />

          <!--
            Moving the map. The Initial pins above are read once and then ignored, because
            reading them every frame would undo any navigation on the very next one. These
            nodes are how a patch moves the view - and where it would wire the mouse, using
            VL.Skia MouseState, or an LFO, or anything else.
          -->
          <Pad Id="$($id.padNavLon)" Comment="Go To Longitude" Bounds="60,400,70,15" ShowValueBox="true" isIOBox="true" Value="139.7">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="Float64" />
            </p:TypeAnnotation>
          </Pad>
          <Pad Id="$($id.padNavLat)" Comment="Go To Latitude" Bounds="140,400,70,15" ShowValueBox="true" isIOBox="true" Value="35.68">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="Float64" />
            </p:TypeAnnotation>
          </Pad>
          <Pad Id="$($id.padNavZoom)" Comment="Go To Zoom" Bounds="220,400,50,15" ShowValueBox="true" isIOBox="true" Value="12">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="Integer32" />
            </p:TypeAnnotation>
          </Pad>

          <Node Bounds="60,430,110,19" Id="$($id.nCenter)">
            <p:NodeReference LastCategoryFullName="Mapsui.Navigate" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="CenterOn" />
            </p:NodeReference>
            <Pin Id="$($id.ctrMap)" Name="Map" Kind="InputPin" />
            <Pin Id="$($id.ctrLon)" Name="Longitude" Kind="InputPin" />
            <Pin Id="$($id.ctrLat)" Name="Latitude" Kind="InputPin" />
            <Pin Id="$($id.ctrOut)" Name="Output" Kind="OutputPin" />
          </Node>
          <Node Bounds="200,430,120,19" Id="$($id.nZoom)">
            <p:NodeReference LastCategoryFullName="Mapsui.Navigate" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="ZoomToLevel" />
            </p:NodeReference>
            <Pin Id="$($id.zmMap)" Name="Map" Kind="InputPin" />
            <Pin Id="$($id.zmLevel)" Name="Zoom Level" Kind="InputPin" />
            <Pin Id="$($id.zmOut)" Name="Output" Kind="OutputPin" />
          </Node>

          <Pad Id="$($id.padDiag)" Comment="Diagnostics" Bounds="360,340,35,35" ShowValueBox="true" isIOBox="true" Value="true">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Toggle</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>

          <Node Bounds="360,400,130,19" Id="$($id.nSkia)">
            <p:NodeReference LastCategoryFullName="Mapsui.Skia" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="ToSkiaLayer" />
            </p:NodeReference>
            <Pin Id="$($id.skMap)" Name="Map" Kind="InputPin" />
            <Pin Id="$($id.skDiag)" Name="Diagnostics" Kind="InputPin" />
            <Pin Id="$($id.skOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <Pad Id="$($id.padColor)" Comment="Background" Bounds="60,400,143,20" ShowValueBox="true" isIOBox="true" Value="0.1, 0.1, 0.15, 1">
            <p:TypeAnnotation LastCategoryFullName="Color" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="RGBA" />
            </p:TypeAnnotation>
          </Pad>

          <Node Bounds="360,460,165,19" Id="$($id.nR)">
            <p:NodeReference LastCategoryFullName="Graphics.Skia" LastDependency="VL.Skia.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="Renderer" />
            </p:NodeReference>
            <Pin Id="$($id.rBounds)" Name="Bounds" Kind="InputPin" DefaultValue="200, 150, 900, 640">
              <p:TypeAnnotation LastCategoryFullName="System.Drawing" LastDependency="System.Drawing.dll">
                <Choice Kind="TypeFlag" Name="Rectangle" />
              </p:TypeAnnotation>
            </Pin>
            <Pin Id="$($id.rBound)" Name="Bound to Document" Kind="InputPin" DefaultValue="True">
              <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
                <Choice Kind="TypeFlag" Name="Boolean" />
              </p:TypeAnnotation>
            </Pin>
            <Pin Id="$($id.rInput)" Name="Input" Kind="InputPin" />
            <Pin Id="$($id.rColor)" Name="Color" Kind="InputPin" />
            <Pin Id="$($id.rClear)" Name="Clear" Kind="InputPin" />
            <Pin Id="$($id.rSpace)" Name="Space" Kind="InputPin" />
            <Pin Id="$($id.rCursor)" Name="Show Cursor" Kind="InputPin" />
            <Pin Id="$($id.rVSync)" Name="VSync" Kind="InputPin" />
            <Pin Id="$($id.rEnabled)" Name="Enabled" Kind="InputPin" />
            <Pin Id="$($id.rForm)" Name="Form" Kind="OutputPin" />
            <Pin Id="$($id.rClient)" Name="ClientBounds" Kind="OutputPin" />
            <Pin Id="$($id.rTime)" Name="Render Time" Kind="OutputPin" />
          </Node>

        </Canvas>
        <Patch Id="$($id.pCreate)" Name="Create" />
        <Patch Id="$($id.pUpdate)" Name="Update" />
        <ProcessDefinition Id="$($id.procDef)">
          <Fragment Id="$($id.frag1)" Patch="$($id.pCreate)" Enabled="true" />
          <Fragment Id="$($id.frag2)" Patch="$($id.pUpdate)" Enabled="true" />
        </ProcessDefinition>
        <Link Id="$($link.l1)" Ids="$($id.padEnabled),$($id.osmEnabled)" />
        <Link Id="$($link.l2)" Ids="$($id.padCache),$($id.osmCache)" />
        <Link Id="$($link.l3)" Ids="$($id.osmBuilt),$($id.padBuilt)" />
        <Link Id="$($link.l4)" Ids="$($id.osmOut),$($id.consIn)" />
        <Link Id="$($link.l15)" Ids="$($id.consOut),$($id.mapLayers)" />
        <Link Id="$($link.l5)" Ids="$($id.padLon),$($id.mapLon)" />
        <Link Id="$($link.l6)" Ids="$($id.padLat),$($id.mapLat)" />
        <Link Id="$($link.l7)" Ids="$($id.padZoom),$($id.mapZoom)" />
        <Link Id="$($link.l8)" Ids="$($id.mapOut),$($id.infoMap)" />
        <Link Id="$($link.l9)" Ids="$($id.infoLon),$($id.padVpLon)" />
        <Link Id="$($link.l10)" Ids="$($id.infoLat),$($id.padVpLat)" />
        <Link Id="$($link.l11)" Ids="$($id.infoRes),$($id.padVpRes)" />
        <Link Id="$($link.l16)" Ids="$($id.mapOut),$($id.ctrMap)" />
        <Link Id="$($link.l17)" Ids="$($id.padNavLon),$($id.ctrLon)" />
        <Link Id="$($link.l18)" Ids="$($id.padNavLat),$($id.ctrLat)" />
        <Link Id="$($link.l19)" Ids="$($id.ctrOut),$($id.zmMap)" />
        <Link Id="$($link.l20)" Ids="$($id.padNavZoom),$($id.zmLevel)" />
        <Link Id="$($link.l12)" Ids="$($id.zmOut),$($id.skMap)" />
        <Link Id="$($link.l13)" Ids="$($id.padDiag),$($id.skDiag)" />
        <Link Id="$($link.l14)" Ids="$($id.skOut),$($id.rInput)" />
      </Patch>
    </Node>
  </Patch>
</Document>
"@

# Check the document before writing it. A .vl that fails to parse is not obviously broken from
# inside vvvv - it simply does not load - so every mistake caught here is a round trip saved.
foreach ($m in [regex]::Matches($xml, '(?s)<!--(.*?)-->')) {
    if ($m.Groups[1].Value -match '--') { throw "XML comment contains '--'" }
}
try { $null = [xml]$xml }
catch { throw "generated document is not well-formed XML: $($_.Exception.Message)" }

$dir = Split-Path $target -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

# UTF-8 *with* BOM. vvvv will not load the document without it.
[IO.File]::WriteAllText($target, $xml, (New-Object System.Text.UTF8Encoding($true)))
Write-Host "wrote $target"
