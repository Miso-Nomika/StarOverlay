# Publishes the raw-socket helper as a NativeAOT single exe to artifacts\capture (needs the Visual Studio C++ build tools).
$root = Split-Path -Parent $PSScriptRoot
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'   # vswhere.exe must be on PATH for the native linker
if (Test-Path $installer) { $env:PATH = "$env:PATH;$installer" }
dotnet publish (Join-Path $root 'src\StarOverlay.Capture\StarOverlay.Capture.csproj') -c Release -r win-x64 -o (Join-Path $root 'artifacts\capture')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Remove-Item (Join-Path $root 'artifacts\capture\*.pdb') -ErrorAction SilentlyContinue
