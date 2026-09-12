[CmdletBinding()]
param(
    [string] $PackageDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $root 'artifacts\packages'
}
$llamaName = 'haven-llamacpp-runtime_0.4.0+haven0.1_amd64.deb'
$llamaHash = '13ea16e4ffa92d1e4b2f31a1a32ba00dc8802ec9077733d7db96a68dad92142d'

if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
    throw "Release staging directory is missing: $PackageDirectory"
}

$llamaPath = Join-Path $PackageDirectory $llamaName
if (-not (Test-Path -LiteralPath $llamaPath -PathType Leaf)) {
    throw "Required READY cohort artifact is missing: $llamaName"
}

$actualHash = (Get-FileHash -LiteralPath $llamaPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $llamaHash) {
    throw "llama.cpp artifact hash mismatch: expected $llamaHash, got $actualHash"
}

Write-Output "Release staging contract passed: $llamaName ($actualHash)"
