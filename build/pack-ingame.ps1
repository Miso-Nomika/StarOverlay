# Builds artifacts\ingame-package: the loader (BepInEx), our in-game agent, the font and the Unity base libraries.
# Usage:
#   build\pack-ingame.ps1 -NoAgent            first run on a PC: loader only, so the game can generate its interop assemblies
#   build\collect-interop.ps1 -Game <folder>  copies those assemblies to artifacts\ingame-refs (needed to compile the agent)
#   build\pack-ingame.ps1                     full package with the agent
param([switch]$NoAgent, [switch]$Dev) # -Dev: a development build with the tools of src\StarOverlay.Ingame\DevTools.cs (never for a release)
$ErrorActionPreference = 'Stop'
$userDotnet = Join-Path $env:USERPROFILE '.dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) { $env:PATH = "$userDotnet;$env:PATH"; $env:DOTNET_ROOT = $userDotnet } # a per-user SDK wins over a runtime-only install
$root = Split-Path -Parent $PSScriptRoot
$art = Join-Path $root 'artifacts'
$downloads = Join-Path $art 'downloads'
$package = Join-Path $art 'ingame-package'
New-Item -ItemType Directory -Force $downloads | Out-Null

# --- BepInEx 6 IL2CPP, pinned build and hash
$bepZip = Join-Path $downloads 'BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793.zip'
$bepUrl = 'https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip'
$bepHash = '5B63D7D0E92A2448CCD13B738FF95FF1CC1026C6D718555D4DF8E6C824953AC4'
if (-not (Test-Path $bepZip)) { Invoke-WebRequest -UseBasicParsing $bepUrl -OutFile $bepZip }
if ((Get-FileHash $bepZip -Algorithm SHA256).Hash -ne $bepHash) { Remove-Item $bepZip; throw 'BepInEx download does not match the pinned hash' }

# --- Unity base libraries for the game's Unity version (so the first start needs no internet)
$libsZip = Join-Path $downloads 'unity-libs-2022.3.59.zip'
if (-not (Test-Path $libsZip)) { Invoke-WebRequest -UseBasicParsing 'https://unity.bepinex.dev/libraries/2022.3.59.zip' -OutFile $libsZip }

if (Test-Path $package) { Remove-Item $package -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $package 'bepinex'), (Join-Path $package 'agent'), (Join-Path $package 'fonts'), (Join-Path $package 'icons'), (Join-Path $package 'unity-libs') | Out-Null
Expand-Archive $bepZip -DestinationPath (Join-Path $package 'bepinex') -Force
Copy-Item $libsZip (Join-Path $package 'unity-libs\2022.3.59.zip')
$fontDir = Join-Path $root 'src\StarOverlay.App\Assets\Fonts'
Copy-Item (Join-Path $fontDir 'PretendardJP-Bold.otf'), (Join-Path $fontDir 'PretendardJP-LICENSE.txt') (Join-Path $package 'fonts')
Copy-Item (Join-Path $root 'src\StarOverlay.Ingame\Icons\*.png') (Join-Path $package 'icons')

$agentHash = 'noagent'
if (-not $NoAgent) {
    $devFlag = if ($Dev) { '-p:DevTools=true' } else { '-p:DevTools=false' }
    dotnet build (Join-Path $root 'src\StarOverlay.Ingame\StarOverlay.Ingame.csproj') -c Release $devFlag -o (Join-Path $art 'agent-build')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item (Join-Path $art 'agent-build\StarOverlay.Ingame.dll'), (Join-Path $art 'agent-build\StarOverlay.Ingame.Protocol.dll') (Join-Path $package 'agent')
    # the names and places of the game's picture files, read by the agent (the Steam client has no list of its own to read)
    Copy-Item (Join-Path $root 'src\StarOverlay.Ingame\Data\addresses.txt') (Join-Path $package 'agent')
    $agentHash = (Get-FileHash (Join-Path $package 'agent\StarOverlay.Ingame.dll') -Algorithm SHA256).Hash.Substring(0, 6) + (Get-FileHash (Join-Path $package 'agent\addresses.txt') -Algorithm SHA256).Hash.Substring(0, 6)
}
"be793-$agentHash" | Set-Content -Encoding ASCII (Join-Path $package 'package.id')
Write-Host "package: $package  id: $(Get-Content (Join-Path $package 'package.id'))"
