# Compiles the UnityEngine-free core (Data + Services + Core) together with the test
# harness using the Roslyn compiler from VS2022, referencing Newtonsoft.Json 13, then runs it.
# The core now includes SQLite.cs (SQLite4Unity3d); the native sqlite3.dll is copied beside the
# exe so the harness can P/Invoke it (mirrors the Newtonsoft copy below).
$ErrorActionPreference = "Stop"

$csc = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
$fw  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$root = "f:\UnityProjects\PoemPoetry"
$harnessDir = Join-Path $root "Tools\TestHarness"
$lib = Join-Path $harnessDir "lib"
New-Item -ItemType Directory -Force -Path $lib | Out-Null

$newtonSrc = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\Newtonsoft.Json.dll"
$newton = Join-Path $lib "Newtonsoft.Json.dll"
Copy-Item $newtonSrc $newton -Force
# Also place it beside the exe so the runtime can resolve it.
Copy-Item $newtonSrc (Join-Path $harnessDir "Newtonsoft.Json.dll") -Force

# Native SQLite for the harness P/Invoke: copy the x64 Windows binary beside the exe.
$sqliteSrc = Join-Path $root "Assets\Plugins\x64\sqlite3.dll"
if (Test-Path $sqliteSrc) { Copy-Item $sqliteSrc (Join-Path $harnessDir "sqlite3.dll") -Force }
else { Write-Output "WARN: $sqliteSrc not found; SQLite tests will fail to load sqlite3" }

$sources = @()
$sources += Get-ChildItem (Join-Path $root "Assets\Scripts\Data")     -Recurse -Filter *.cs | ForEach-Object FullName
$sources += Get-ChildItem (Join-Path $root "Assets\Scripts\Services")  -Recurse -Filter *.cs | ForEach-Object FullName
$sources += Get-ChildItem (Join-Path $root "Assets\Scripts\Core")      -Recurse -Filter *.cs | ForEach-Object FullName
$sources += (Join-Path $harnessDir "Program.cs")

$rsp = Join-Path $harnessDir "sources.rsp"
$sources | ForEach-Object { '"' + $_ + '"' } | Set-Content -Path $rsp -Encoding ascii

$out = Join-Path $harnessDir "harness.exe"
if (Test-Path $out) { Remove-Item $out -Force }

& $csc /nologo /noconfig /nostdlib+ /target:exe /langversion:9.0 /codepage:65001 /out:"$out" `
    /r:"$newton" `
    /r:"$fw\mscorlib.dll" /r:"$fw\System.dll" /r:"$fw\System.Core.dll" `
    "@$rsp"

if ($LASTEXITCODE -ne 0) { Write-Output "COMPILE FAILED ($LASTEXITCODE)"; exit $LASTEXITCODE }
Write-Output "Compile OK -> $out`n"
& $out
exit $LASTEXITCODE