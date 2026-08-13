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
# Authors help\VL.Mapsui\HowTo Drive the map with the mouse.vl.
#
# Emitted whole, every time. Every node reference is copied from a document vvvv itself wrote:
# Console and Group from VL.Skia's own patches, MouseState from CoreLibBasics as used in
# HowTo React On Mouse Async2.vl, Vector (Split) from VL.Audio.UI.vl, Cons from VL.Audio.HDE.vl.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$target   = Join-Path $RepoRoot 'help\VL.Mapsui\HowTo Drive the map with the mouse.vl'

$keys = @(
    'doc','patch','canvas','app','inPatch','inCanvas','pCreate','pUpdate','procDef','frag1','frag2',
    'depCoreLib','depSkia','depMapsui',
    'padNote','padEnabled','padCache','padDiag','padColor','padWheel','padPos','padPressed',
    'nOsm','osmEnabled','osmCache','osmBuilt','osmOut','padBuilt',
    'nCons','consIn','consIn2','consOut',
    'nMap','mapLayers','mapOut',
    'nConsole','conOut','conMouse','conKeyboard','conTouch','conNotif',
    'nMouse','msDev','msWorld','msProj','msPos','msLeft','msMiddle','msRight','msNorm','msWheel','msClient','msSender',
    'nSplit','spIn','spX','spY',
    'nDrag','dgMap','dgX','dgY','dgOn','dgOut',
    'nFrameDiff','fdIn','fdOut','padDelta',
    'nWheel','whMap','whX','whY','whDelta','whNotch','whOut',
    'nZin','ziMap','ziTrig','ziOut','nZout','zoMap','zoTrig','zoOut','padZin','padZout',
    'nSkia','skMap','skDiag','skOut',
    'nGroup','grIn','grIn2','grOut',
    'nR','rBounds','rBound','rInput','rColor','rClear','rSpace','rCursor','rVSync','rEnabled','rForm','rClient','rTime'
)
$linkKeys = 1..30 | ForEach-Object { "l$_" }

$ids = @(& (Join-Path $PSScriptRoot 'New-VLId.ps1') -Count ($keys.Count + $linkKeys.Count))
$id = @{}; $i = 0
foreach ($k in $keys)     { $id[$k]   = $ids[$i]; $i++ }
$link = @{}
foreach ($k in $linkKeys) { $link[$k] = $ids[$i]; $i++ }

$note = @'
Driving the map with the mouse. Nothing in VL.Mapsui decides this for you: the wiring below is a choice, and swapping the mouse for an LFO, an OSC message or a keyboard means changing these links and nothing else.&#xD;&#xA;&#xD;&#xA;Console is a layer, which is why it goes into the Group along with the map. Notifications travel through the Skia layer graph, so a node that wants the mouse has to sit in it. Its Mouse output feeds MouseState, and that gives Position, Left Pressed and WheelDelta.&#xD;&#xA;&#xD;&#xA;Drag takes a position and a Dragging gate. It remembers the previous frame itself, so there is no FrameDelay to wire, but it does not decide what counts as dragging - that is the Left Pressed link, and you could use the right button or a modifier instead.&#xD;&#xA;&#xD;&#xA;The wheel goes through FrameDifference, because Wheel State accumulates rather than reporting what just happened. Measured in vvvv 7.4: one notch moves the difference by exactly 1, which is why Notch Size defaults to 1. The raw Windows convention is 120 per notch, so if something else drives that pin, set it to match. Both numbers are on IOBoxes so this is visible rather than assumed - and it earned its keep, because a default of 120 meant a wheel that turned and a map that did not move, with nothing on screen to say why.&#xD;&#xA;&#xD;&#xA;Enabled starts OFF, as everywhere in this package: opening a document in vvvv runs it.
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

          <Pad Id="$($id.padNote)" Bounds="60,30,640,150" ShowValueBox="true" isIOBox="true" Value="$note">
            <p:TypeAnnotation>
              <Choice Kind="TypeFlag" Name="String" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:fontsize p:Type="Int32">11</p:fontsize>
              <p:stringtype p:Assembly="VL.Core" p:Type="VL.Core.StringType">Comment</p:stringtype>
            </p:ValueBoxSettings>
          </Pad>

          <Pad Id="$($id.padEnabled)" Comment="Enabled" Bounds="60,210,35,35" ShowValueBox="true" isIOBox="true" Value="false">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Toggle</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>
          <Pad Id="$($id.padCache)" Comment="Cache To Disk" Bounds="110,210,35,35" ShowValueBox="true" isIOBox="true" Value="true">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Toggle</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>

          <Node Bounds="60,270,150,19" Id="$($id.nOsm)">
            <p:NodeReference LastCategoryFullName="Mapsui.Layers" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="OpenStreetMap" />
            </p:NodeReference>
            <Pin Id="$($id.osmEnabled)" Name="Enabled" Kind="InputPin" />
            <Pin Id="$($id.osmCache)" Name="Cache To Disk" Kind="InputPin" />
            <Pin Id="$($id.osmBuilt)" Name="Layers Built" Kind="OutputPin" />
            <Pin Id="$($id.osmOut)" Name="Result" Kind="OutputPin" />
          </Node>
          <Pad Id="$($id.padBuilt)" Comment="Layers Built" Bounds="240,270,80,15" ShowValueBox="true" isIOBox="true" />

          <Node Bounds="60,310,25,19" Id="$($id.nCons)">
            <p:NodeReference LastCategoryFullName="Collections.Spread" LastDependency="VL.CoreLib.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="Cons" />
              <CategoryReference Kind="RecordType" Name="Spread" NeedsToBeDirectParent="true" />
            </p:NodeReference>
            <Pin Id="$($id.consIn)" Name="Input" Kind="InputPin" />
            <Pin Id="$($id.consIn2)" Name="Input 2" Kind="InputPin" />
            <Pin Id="$($id.consOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <Node Bounds="60,350,110,19" Id="$($id.nMap)">
            <p:NodeReference LastCategoryFullName="Mapsui" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="Map" />
            </p:NodeReference>
            <Pin Id="$($id.mapLayers)" Name="Layers" Kind="InputPin" />
            <Pin Id="$($id.mapOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <!--
            Console is a LAYER, which is why its Output goes into the Group below rather than
            nowhere. Notifications travel through the Skia layer graph, so anything that wants
            the mouse has to sit in that graph. This is the pattern VL.Skia's own help patches
            use, including the ones that also have a Renderer.
          -->
          <Node Bounds="420,210,90,19" Id="$($id.nConsole)">
            <p:NodeReference LastCategoryFullName="Graphics.Skia" LastDependency="VL.Skia.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="Console" />
            </p:NodeReference>
            <Pin Id="$($id.conOut)" Name="Output" Kind="OutputPin" />
            <Pin Id="$($id.conMouse)" Name="Mouse" Kind="OutputPin" />
            <Pin Id="$($id.conKeyboard)" Name="Keyboard" Kind="OutputPin" />
            <Pin Id="$($id.conNotif)" Name="Notifications" Kind="OutputPin" />
          </Node>

          <Node Bounds="420,250,185,19" Id="$($id.nMouse)">
            <p:NodeReference LastCategoryFullName="IO.Mouse" LastSymbolSource="CoreLibBasics.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="MouseState" />
            </p:NodeReference>
            <Pin Id="$($id.msDev)" Name="Mouse Device" Kind="InputPin" />
            <Pin Id="$($id.msPos)" Name="Position" Kind="OutputPin" />
            <Pin Id="$($id.msLeft)" Name="Left Pressed" Kind="OutputPin" />
            <Pin Id="$($id.msWheel)" Name="Wheel State" Kind="OutputPin" />
          </Node>

          <Node Bounds="420,300,46,19" Id="$($id.nSplit)">
            <p:NodeReference LastCategoryFullName="2D.Vector2" LastDependency="VL.CoreLib.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="Vector (Split)" />
              <CategoryReference Kind="Vector2Type" Name="Vector2" NeedsToBeDirectParent="true" />
            </p:NodeReference>
            <Pin Id="$($id.spIn)" Name="Input" Kind="StateInputPin" />
            <Pin Id="$($id.spX)" Name="X" Kind="OutputPin" />
            <Pin Id="$($id.spY)" Name="Y" Kind="OutputPin" />
          </Node>

          <!--
            Read these rather than trusting a paragraph. Position tells you which pixels the
            mouse reports; Wheel State accumulates, and Wheel This Frame below is what changed.
            One notch moves it by 1 in vvvv 7.4, which is what Notch Size on ZoomByWheel has to
            agree with. Getting that wrong is silent: the wheel turns and the map does not move.
          -->
          <Pad Id="$($id.padPos)" Comment="Mouse Position" Bounds="640,250,110,15" ShowValueBox="true" isIOBox="true" />
          <Pad Id="$($id.padPressed)" Comment="Left Pressed" Bounds="640,270,110,15" ShowValueBox="true" isIOBox="true" />
          <Pad Id="$($id.padWheel)" Comment="Wheel State" Bounds="640,290,110,15" ShowValueBox="true" isIOBox="true" />

          <!--
            Drag remembers the previous position itself, so there is no FrameDelay here. What it
            deliberately does not decide is what counts as dragging: that is the Left Pressed
            link, and a patch is free to use the right button, a modifier or a touch gesture.
          -->
          <Node Bounds="60,400,110,19" Id="$($id.nDrag)">
            <p:NodeReference LastCategoryFullName="Mapsui.Navigate" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="Drag" />
            </p:NodeReference>
            <Pin Id="$($id.dgMap)" Name="Map" Kind="InputPin" />
            <Pin Id="$($id.dgX)" Name="X" Kind="InputPin" />
            <Pin Id="$($id.dgY)" Name="Y" Kind="InputPin" />
            <Pin Id="$($id.dgOn)" Name="Dragging" Kind="InputPin" />
            <Pin Id="$($id.dgOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <!--
            Wheel State accumulates: it counts up and stays there. FrameDifference turns it into
            what changed this frame, which is what ZoomByWheel wants. This is how vvvv's own
            patches read that pin - it is the only place in the shipped documents where it is
            connected to anything.
          -->
          <Node Bounds="640,320,91,19" Id="$($id.nFrameDiff)">
            <p:NodeReference LastCategoryFullName="Animation.FrameBased" LastDependency="CoreLibBasics.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="FrameDifference" />
            </p:NodeReference>
            <Pin Id="$($id.fdIn)" Name="Value" Kind="InputPin" />
            <Pin Id="$($id.fdOut)" Name="Result" Kind="OutputPin" />
          </Node>
          <Pad Id="$($id.padDelta)" Comment="Wheel This Frame" Bounds="760,320,110,15" ShowValueBox="true" isIOBox="true" />

          <Node Bounds="200,400,150,19" Id="$($id.nWheel)">
            <p:NodeReference LastCategoryFullName="Mapsui.Navigate" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="ZoomByWheel" />
            </p:NodeReference>
            <Pin Id="$($id.whMap)" Name="Map" Kind="InputPin" />
            <Pin Id="$($id.whX)" Name="X" Kind="InputPin" />
            <Pin Id="$($id.whY)" Name="Y" Kind="InputPin" />
            <Pin Id="$($id.whDelta)" Name="Wheel Delta" Kind="InputPin" />
            <Pin Id="$($id.whNotch)" Name="Notch Size" Kind="InputPin" />
            <Pin Id="$($id.whOut)" Name="Output" Kind="OutputPin" />
          </Node>

          <!--
            Two bangs, because a wheel is not the only way to zoom and not everyone has one. The
            nodes watch the rising edge, so a toggle left switched on zooms once rather than on
            every frame - which would be a tile request per frame.
          -->
          <Pad Id="$($id.padZin)" Comment="Zoom In" Bounds="60,450,35,35" ShowValueBox="true" isIOBox="true" Value="false">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Bang</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>
          <Pad Id="$($id.padZout)" Comment="Zoom Out" Bounds="110,450,35,35" ShowValueBox="true" isIOBox="true" Value="false">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Bang</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>

          <Node Bounds="160,490,100,19" Id="$($id.nZin)">
            <p:NodeReference LastCategoryFullName="Mapsui.Navigate" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="ZoomIn" />
            </p:NodeReference>
            <Pin Id="$($id.ziMap)" Name="Map" Kind="InputPin" />
            <Pin Id="$($id.ziTrig)" Name="Trigger" Kind="InputPin" />
            <Pin Id="$($id.ziOut)" Name="Result" Kind="OutputPin" />
          </Node>
          <Node Bounds="290,490,105,19" Id="$($id.nZout)">
            <p:NodeReference LastCategoryFullName="Mapsui.Navigate" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="ZoomOut" />
            </p:NodeReference>
            <Pin Id="$($id.zoMap)" Name="Map" Kind="InputPin" />
            <Pin Id="$($id.zoTrig)" Name="Trigger" Kind="InputPin" />
            <Pin Id="$($id.zoOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <Pad Id="$($id.padDiag)" Comment="Diagnostics" Bounds="340,400,35,35" ShowValueBox="true" isIOBox="true" Value="true">
            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">
              <Choice Kind="ImmutableTypeFlag" Name="Boolean" />
            </p:TypeAnnotation>
            <p:ValueBoxSettings>
              <p:buttonmode p:Assembly="VL.UI.Forms" p:Type="VL.HDE.PatchEditor.Editors.ButtonModeEnum">Toggle</p:buttonmode>
            </p:ValueBoxSettings>
          </Pad>

          <Node Bounds="60,460,130,19" Id="$($id.nSkia)">
            <p:NodeReference LastCategoryFullName="Mapsui.Skia" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="ToSkiaLayer" />
            </p:NodeReference>
            <Pin Id="$($id.skMap)" Name="Map" Kind="InputPin" />
            <Pin Id="$($id.skDiag)" Name="Diagnostics" Kind="InputPin" />
            <Pin Id="$($id.skOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <Node Bounds="60,510,105,19" Id="$($id.nGroup)">
            <p:NodeReference LastCategoryFullName="Graphics.Skia" LastDependency="VL.Skia.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="Group" />
              <CategoryReference Kind="Category" Name="Skia" NeedsToBeDirectParent="true" />
            </p:NodeReference>
            <Pin Id="$($id.grIn)" Name="Input" Kind="InputPin" />
            <Pin Id="$($id.grIn2)" Name="Input 2" Kind="InputPin" />
            <Pin Id="$($id.grOut)" Name="Output" Kind="OutputPin" />
          </Node>

          <Pad Id="$($id.padColor)" Comment="Background" Bounds="240,510,143,20" ShowValueBox="true" isIOBox="true" Value="0.1, 0.1, 0.15, 1">
            <p:TypeAnnotation LastCategoryFullName="Color" LastDependency="VL.CoreLib.vl">
              <Choice Kind="TypeFlag" Name="RGBA" />
            </p:TypeAnnotation>
          </Pad>

          <Node Bounds="60,560,165,19" Id="$($id.nR)">
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
        <Link Id="$($link.l5)" Ids="$($id.consOut),$($id.mapLayers)" />
        <Link Id="$($link.l6)" Ids="$($id.conMouse),$($id.msDev)" />
        <Link Id="$($link.l7)" Ids="$($id.msPos),$($id.spIn)" />
        <Link Id="$($link.l8)" Ids="$($id.msPos),$($id.padPos)" />
        <Link Id="$($link.l9)" Ids="$($id.msLeft),$($id.padPressed)" />
        <Link Id="$($link.l10)" Ids="$($id.msWheel),$($id.padWheel)" />
        <Link Id="$($link.l11)" Ids="$($id.mapOut),$($id.dgMap)" />
        <Link Id="$($link.l12)" Ids="$($id.spX),$($id.dgX)" />
        <Link Id="$($link.l13)" Ids="$($id.spY),$($id.dgY)" />
        <Link Id="$($link.l14)" Ids="$($id.msLeft),$($id.dgOn)" />
        <Link Id="$($link.l15)" Ids="$($id.dgOut),$($id.whMap)" />
        <Link Id="$($link.l16)" Ids="$($id.spX),$($id.whX)" />
        <Link Id="$($link.l17)" Ids="$($id.spY),$($id.whY)" />
        <Link Id="$($link.l23)" Ids="$($id.msWheel),$($id.fdIn)" />
        <Link Id="$($link.l24)" Ids="$($id.fdOut),$($id.padDelta)" />
        <Link Id="$($link.l25)" Ids="$($id.fdOut),$($id.whDelta)" />
        <Link Id="$($link.l26)" Ids="$($id.whOut),$($id.ziMap)" />
        <Link Id="$($link.l27)" Ids="$($id.padZin),$($id.ziTrig)" />
        <Link Id="$($link.l28)" Ids="$($id.ziOut),$($id.zoMap)" />
        <Link Id="$($link.l29)" Ids="$($id.padZout),$($id.zoTrig)" />
        <Link Id="$($link.l18)" Ids="$($id.zoOut),$($id.skMap)" />
        <Link Id="$($link.l19)" Ids="$($id.padDiag),$($id.skDiag)" />
        <Link Id="$($link.l20)" Ids="$($id.skOut),$($id.grIn)" />
        <Link Id="$($link.l21)" Ids="$($id.conOut),$($id.grIn2)" />
        <Link Id="$($link.l22)" Ids="$($id.grOut),$($id.rInput)" />
      </Patch>
    </Node>
  </Patch>
</Document>
"@

foreach ($m in [regex]::Matches($xml, '(?s)<!--(.*?)-->')) {
    if ($m.Groups[1].Value -match '--') { throw "XML comment contains '--'" }
}
try { $null = [xml]$xml }
catch { throw "generated document is not well-formed XML: $($_.Exception.Message)" }

$dir = Split-Path $target -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[IO.File]::WriteAllText($target, $xml, (New-Object System.Text.UTF8Encoding($true)))
Write-Host "wrote $target"
