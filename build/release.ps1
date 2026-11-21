# Builds the one file users download: artifacts\release\StarOverlay.exe (self-contained, no .NET needed, the install files inside).
#   build\release.ps1 -Version 0.4.6
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
$userDotnet = Join-Path $env:USERPROFILE '.dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) { $env:PATH = "$userDotnet;$env:PATH"; $env:DOTNET_ROOT = $userDotnet } # a per-user SDK wins over a runtime-only install
$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root 'artifacts\release'

& (Join-Path $PSScriptRoot 'make-payload.ps1') -Version $Version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== StarOverlay-$Version.exe"
$out = Join-Path $release 'app'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\StarOverlay.App\StarOverlay.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:EmbedPayload=true -p:DebugType=none -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$name = "StarOverlay-$Version.exe"
# earlier builds (any version) are removed: only this one stays
Get-ChildItem $release -Filter 'StarOverlay*.exe*' -File -ErrorAction SilentlyContinue | Remove-Item -Force
$exe = Join-Path $release $name
Copy-Item (Join-Path $out 'StarOverlay.App.exe') $exe -Force
(Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant() + "  $name" | Set-Content -Encoding ASCII ($exe + '.sha256')
# leave only this build's exe: the intermediate folders (hundreds of MB) are rebuilt by the next release anyway
foreach ($leftover in @((Join-Path $release 'app'), (Join-Path $release 'payload.zip'), (Join-Path $root 'artifacts\payload'), (Join-Path $root 'artifacts\plugin-build'))) {
    if (Test-Path $leftover) { Remove-Item $leftover -Recurse -Force }
}
Write-Host ("done: {0}  {1:N1} MB" -f $exe, ((Get-Item $exe).Length / 1MB))
