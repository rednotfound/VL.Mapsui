<#
.SYNOPSIS
    Generates VL document IDs in the exact format vvvv gamma uses.

.DESCRIPTION
    Every Id="..." attribute in a .vl document is a 22-character token.
    Format derived empirically from 21,823 unique IDs extracted from shipped
    vvvv packages (VL.PolyTools, VL.CoreLib, VL.Stride):

      - length          : exactly 22 characters
      - character 1     : [A-V]                  (22 possible values, zero exceptions)
      - characters 2-22 : [0-9A-Za-z]            (full 62-character alphabet)

    Hand-invented IDs of the wrong length are silently rejected by vvvv's
    deserializer, which is what broke VL.GIS v0.0.3 - v0.0.11.

.EXAMPLE
    .\tools\New-VLId.ps1 -Count 6
#>
param(
    [int]$Count = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$firstChars = [char[]]'ABCDEFGHIJKLMNOPQRSTUV'
$restChars  = [char[]]'0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'

function New-VLId {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $sb = [System.Text.StringBuilder]::new(22)
        [void]$sb.Append($firstChars[(Get-RandomIndex $rng $firstChars.Length)])
        for ($i = 1; $i -lt 22; $i++) {
            [void]$sb.Append($restChars[(Get-RandomIndex $rng $restChars.Length)])
        }
        $sb.ToString()
    }
    finally { $rng.Dispose() }
}

function Get-RandomIndex($rng, [int]$upperBound) {
    # Rejection sampling to keep the distribution uniform.
    $limit = [int]([Math]::Floor(256 / $upperBound)) * $upperBound
    $buf = [byte[]]::new(1)
    do {
        $rng.GetBytes($buf)
    } while ($buf[0] -ge $limit)
    return $buf[0] % $upperBound
}

1..$Count | ForEach-Object { New-VLId }
