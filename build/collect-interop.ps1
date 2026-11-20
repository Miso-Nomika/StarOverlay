# Copies the assemblies BepInEx generated on a game run (<game>\BepInEx\interop) to artifacts\ingame-refs,
# and the loader's core assemblies from the package. The agent project compiles against these.
param([Parameter(Mandatory)][string]$Game)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$refs = Join-Path $root 'artifacts\ingame-refs'
$interop = Join-Path $Game 'BepInEx\interop'
if (-not (Test-Path $interop)) { throw "No generated assemblies yet: start the game once with the loader installed ($interop)" }
New-Item -ItemType Directory -Force (Join-Path $refs 'interop'), (Join-Path $refs 'core') | Out-Null
$names = 'Il2Cppmscorlib', 'UnityEngine', 'UnityEngine.CoreModule', 'UnityEngine.UI', 'UnityEngine.UIModule', 'UnityEngine.TextCoreFontEngineModule',
         'UnityEngine.TextRenderingModule', 'UnityEngine.InputLegacyModule', 'UnityEngine.ImageConversionModule', 'Unity.TextMeshPro', 'Assembly-CSharp', 'Panda.Script', 'Panda.AOT.SDKBridge'
$ZRpc = 'ZRpc', 'ZNet'
foreach ($n in $names + $ZRpc) { $p = Join-Path $interop "$n.dll"; if (Test-Path $p) { Copy-Item $p (Join-Path $refs 'interop') -Force } }
Copy-Item (Join-Path $Game 'BepInEx\core\*.dll') (Join-Path $refs 'core') -Force
Write-Host "refs: $refs ($((Get-ChildItem (Join-Path $refs 'interop')).Count) interop, $((Get-ChildItem (Join-Path $refs 'core')).Count) core)"
