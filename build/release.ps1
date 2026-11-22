# Builds the one file users download: artifacts\release\StarOverlay.exe (self-contained, no .NET needed, the install files inside).
#   build\release.ps1 -Version 0.4.9
param([Parameter(Mandatory = $true)][string]$Version, [switch]$Dev) # -Dev: a development build (tools of DevTools.cs, file name ends in -dev); never upload it
$ErrorActionPreference = 'Stop'
$userDotnet = Join-Path $env:USERPROFILE '.dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) { $env:PATH = "$userDotnet;$env:PATH"; $env:DOTNET_ROOT = $userDotnet } # a per-user SDK wins over a runtime-only install
$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root 'artifacts\release'

& (Join-Path $PSScriptRoot 'make-payload.ps1') -Version $Version -Dev:$Dev
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== StarOverlay-$Version.exe"
$out = Join-Path $release 'app'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\StarOverlay.App\StarOverlay.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:EmbedPayload=true -p:DebugType=none -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$name = if ($Dev) { "StarOverlay-$Version-dev.exe" } else { "StarOverlay-$Version.exe" }
# earlier builds (any version) are removed: only this one stays
Get-ChildItem $release -Filter 'StarOverlay*.exe*' -File -ErrorAction SilentlyContinue | Remove-Item -Force
$exe = Join-Path $release $name
Copy-Item (Join-Path $out 'StarOverlay.App.exe') $exe -Force
(Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant() + "  $name" | Set-Content -Encoding ASCII ($exe + '.sha256')
# leave only this build's exe: the intermediate folders (hundreds of MB) are rebuilt by the next release anyway
foreach ($leftover in @((Join-Path $release 'app'), (Join-Path $release 'payload.zip'), (Join-Path $root 'artifacts\payload'), (Join-Path $root 'artifacts\plugin-build'))) {
    if (Test-Path $leftover) { Remove-Item $leftover -Recurse -Force }
}
$kind = if ($Dev) { '[개발 빌드 - 배포 금지, 인게임 Lua 도구 포함]' } else { '[릴리즈 빌드 - 개발 도구 없음]' }
Write-Host ("done: {0}  {1:N1} MB  {2}" -f $exe, ((Get-Item $exe).Length / 1MB), $kind)
