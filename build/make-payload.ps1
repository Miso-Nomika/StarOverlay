# Builds artifacts\payload (and artifacts\release\payload.zip): everything the installer puts on a PC.
#   host\     the background service (self-contained, no .NET needed on the PC)
#   capture\  the receiving helper (needs administrator once)
#   ingame\   BepInEx loader + our in-game agent + font + icons + Unity libraries
#   plugins\  the standard plugins (dps.meter, chat, party.join, buff.tracker)
# Needs: the game's interop assemblies in artifacts\ingame-refs (build\collect-interop.ps1 on a PC with the game, once).
param([string]$Version = '0.4.9', [switch]$Dev)
$ErrorActionPreference = 'Stop'
$userDotnet = Join-Path $env:USERPROFILE '.dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) { $env:PATH = "$userDotnet;$env:PATH"; $env:DOTNET_ROOT = $userDotnet } # a per-user SDK wins over a runtime-only install
$root = Split-Path -Parent $PSScriptRoot
$art = Join-Path $root 'artifacts'
$payload = Join-Path $art 'payload'
$release = Join-Path $art 'release'
function Run($what) { & $what; if ($LASTEXITCODE -ne 0) { throw "failed: $what" } }

if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force $payload, $release | Out-Null

Write-Host '== in-game package'
Run { & (Join-Path $PSScriptRoot 'pack-ingame.ps1') -Dev:$Dev }
Copy-Item (Join-Path $art 'ingame-package') (Join-Path $payload 'ingame') -Recurse

Write-Host '== receiving helper'
Run { & (Join-Path $PSScriptRoot 'publish-capture.ps1') }
New-Item -ItemType Directory -Force (Join-Path $payload 'capture') | Out-Null
Copy-Item (Join-Path $art 'capture\StarOverlay.Capture.exe') (Join-Path $payload 'capture')

Write-Host '== background service'
Run { dotnet publish (Join-Path $root 'src\StarOverlay.Host\StarOverlay.Host.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=none -p:Version=$Version -o (Join-Path $payload 'host') }

# what the service never loads: the runtime's debugger and dump tools, the pdb reader (the build has no pdb) and the WinForms form designer
foreach ($unused in 'mscordbi.dll', 'mscordaccore_amd64_amd64_*.dll', 'createdump.exe', 'Microsoft.DiaSymReader.Native.amd64.dll', 'System.Windows.Forms.Design.dll', 'System.Windows.Forms.Design.Editors.dll') {
    Get-ChildItem (Join-Path $payload 'host') -Filter $unused -ErrorAction SilentlyContinue | Remove-Item -Force
}

Write-Host '== standard plugins'
$plugins = @(
    @{ Id = 'dps.meter'; Project = 'StarOverlay.DpsMeter'; Assets = 'samples\StarOverlay.DpsMeter\assets' },
    @{ Id = 'chat'; Project = 'StarOverlay.Chat'; Assets = $null },
    @{ Id = 'party.join'; Project = 'StarOverlay.PartyJoin'; Assets = $null },
    @{ Id = 'buff.tracker'; Project = 'StarOverlay.BuffTracker'; Assets = $null },
    @{ Id = 'skill.repeat'; Project = 'StarOverlay.SkillRepeat'; Assets = $null }
)
foreach ($plugin in $plugins) {
    $out = Join-Path $art ('plugin-build\' + $plugin.Project)
    Run { dotnet build (Join-Path $root "samples\$($plugin.Project)\$($plugin.Project).csproj") -c Release -p:DebugType=none -p:Version=$Version -o $out }
    $target = Join-Path $payload ('plugins\' + $plugin.Id)
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $out ($plugin.Project + '.dll')) $target
    $assets = if ($plugin.Assets) { Join-Path $root $plugin.Assets } else { Join-Path $out 'assets' }
    if (Test-Path $assets) { Copy-Item $assets (Join-Path $target 'assets') -Recurse }
}

# the settings a new user starts with (copied at install only where the user has no file yet)
New-Item -ItemType Directory -Force (Join-Path $payload 'config-defaults') | Out-Null
Copy-Item (Join-Path $root 'build\defaults\config\*.json') (Join-Path $payload 'config-defaults')

# one id per build: the version and a short hash of what changes (the agent and the service)
$agent = (Get-FileHash (Join-Path $payload 'ingame\agent\StarOverlay.Ingame.dll') -Algorithm SHA256).Hash.Substring(0, 6)
# every program file of the service counts: most of its logic lives in StarOverlay.Game.dll and StarOverlay.Framework.dll, not in the Host exe
$hostFiles = Get-ChildItem (Join-Path $payload 'host') -Filter 'StarOverlay.*.dll' | Sort-Object Name
$hostFiles += Get-Item (Join-Path $payload 'host\StarOverlay.Host.dll')
$host_ = ((($hostFiles | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash }) -join '') | ForEach-Object {
    $sha = [System.Security.Cryptography.SHA256]::Create(); ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($_))) -replace '-', '').Substring(0, 6) })
# the plugins and their pictures count too, by content (a same-length text change must still be installed as a new build)
$pluginRoot = Join-Path $payload 'plugins'
$plugins_ = ((Get-ChildItem $pluginRoot -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName.Substring($pluginRoot.Length) + ':' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash }) -join '|') | ForEach-Object {
    $sha = [System.Security.Cryptography.SHA256]::Create(); ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($_))) -replace '-', '').Substring(0, 6) }
"$Version-$agent$host_$plugins_" | Set-Content -Encoding ASCII (Join-Path $payload 'payload.id')

Write-Host '== payload.zip'
$zip = Join-Path $release 'payload.zip'
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ("payload: {0}  id: {1}  zip: {2:N1} MB" -f $payload, (Get-Content (Join-Path $payload 'payload.id')), ((Get-Item $zip).Length / 1MB))
