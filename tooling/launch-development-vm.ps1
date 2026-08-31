[CmdletBinding()]
param([switch] $Status, [switch] $Launch)

$root = Split-Path -Parent $PSScriptRoot
$vbox = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
if (-not (Test-Path -LiteralPath $vbox)) { throw "VirtualBox tooling is not installed at $vbox." }
$vms = & $vbox list vms
if ([string]::IsNullOrWhiteSpace(($vms -join ''))) { throw 'No registered VirtualBox VM exists. A HavenOS development VM has not been created.' }
$havenVm = $vms | Where-Object { $_ -match 'HavenOS' }
if (-not $havenVm) { throw 'No registered HavenOS development VM exists. Refusing to launch another VM.' }
if ($Status) { $havenVm; exit 0 }
if (-not $Launch) { throw 'Pass -Launch only after verifying the selected HavenOS VM and snapshot.' }
throw 'Launch is intentionally blocked until vm/havenos-dev.json records the approved VM UUID and snapshot provenance.'
