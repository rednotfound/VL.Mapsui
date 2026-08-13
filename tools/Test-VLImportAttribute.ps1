<#
.SYNOPSIS
    Checks whether an assembly carries one of VL.Core's import attributes.

.DESCRIPTION
    Forwarding a .dll from a package's .vl is not enough to get nodes. The assembly must
    also opt in via an assembly-level attribute from VL.Core.Import:

        [assembly: ImportAsIs(Namespace = "VL")]     all public types
        [assembly: ImportNamespace("...")]           one namespace
        [assembly: ImportType(typeof(X))]            one type

    Miss it and everything still "works": the package loads, compiles, packs and exports
    without a single warning -- the methods are just demoted to raw .NET reflection nodes
    that the NodeBrowser hides behind a dependency toggle. Searching a node by name finds
    nothing, which is indistinguishable from the package having failed to load.

    Reads metadata only; the assembly is never loaded, so its dependencies need not
    resolve.

.OUTPUTS
    The names of the import attributes found. Empty if none.

.EXAMPLE
    .\tools\Test-VLImportAttribute.ps1 -Path .\dist\VL.Mapsui\lib\net8.0\VL.Mapsui.dll
#>
param(
    [Parameter(Mandatory)][string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Reflection.Metadata

$stream = [IO.File]::OpenRead((Resolve-Path $Path).Path)
try {
    $pe = New-Object System.Reflection.PortableExecutable.PEReader($stream)
    try {
        $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($handle in $reader.GetAssemblyDefinition().GetCustomAttributes()) {
            $attr = $reader.GetCustomAttribute($handle)
            $ctor = $attr.Constructor
            if ($ctor.Kind -ne 'MemberReference') { continue }

            $parent = $reader.GetMemberReference($ctor).Parent
            if ($parent.Kind -ne 'TypeReference') { continue }

            $typeRef = $reader.GetTypeReference($parent)
            if ($reader.GetString($typeRef.Namespace) -ne 'VL.Core.Import') { continue }

            $name = $reader.GetString($typeRef.Name)
            if ($name -like 'Import*Attribute') { $name }
        }
    }
    finally { $pe.Dispose() }
}
finally { $stream.Dispose() }
