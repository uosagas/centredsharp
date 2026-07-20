# Builds the UO-Sagas .sag-aware ClassicUO DLLs and copies them into lib/dotnet.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\cuo-sag\build.ps1
#         (add -SkipPatch to build pristine upstream DLLs for parity testing)

param(
    [switch]$SkipPatch
)

$ErrorActionPreference = "Stop"

# Mainline ClassicUO commit the CentrED DLLs are pinned to.
$CuoCommit = "48ba0f5c242b136b969795f1f7054f9640d4ec86"
$CuoRepo = "https://github.com/ClassicUO/ClassicUO.git"

$KitDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $KitDir "..\..")).Path
$WorkDir = Join-Path $KitDir ".work"

if (-not (Test-Path (Join-Path $WorkDir ".git"))) {
    git clone $CuoRepo $WorkDir
    if ($LASTEXITCODE -ne 0) { throw "git clone failed" }
}

git -C $WorkDir checkout --force $CuoCommit
if ($LASTEXITCODE -ne 0) { throw "git checkout $CuoCommit failed" }
git -C $WorkDir clean -fd -e bin -e obj | Out-Null

git -C $WorkDir submodule update --init --recursive --depth 1 external/FNA external/MP3Sharp external/FileEmbed
if ($LASTEXITCODE -ne 0) { throw "git submodule update failed" }

if (-not $SkipPatch) {
    Copy-Item (Join-Path $KitDir "patches\SagCrypto.cs") (Join-Path $WorkDir "src\ClassicUO.IO\SagCrypto.cs") -Force
    git -C $WorkDir apply --whitespace=nowarn (Join-Path $KitDir "patches\sag-support.patch")
    if ($LASTEXITCODE -ne 0) { throw "git apply failed" }
}

foreach ($project in "ClassicUO.Assets", "ClassicUO.Renderer") {
    dotnet build (Join-Path $WorkDir "src\$project\$project.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build $project failed" }
}

$BinDir = Join-Path $WorkDir "src\ClassicUO.Renderer\bin\Release\net9.0"
$LibDir = Join-Path $RepoRoot "lib\dotnet"

foreach ($dll in "ClassicUO.Assets.dll", "ClassicUO.IO.dll", "ClassicUO.Renderer.dll", "ClassicUO.Utility.dll") {
    Copy-Item (Join-Path $BinDir $dll) $LibDir -Force
    Write-Host "Updated $LibDir\$dll"
}

Write-Host "Done. ClassicUO $CuoCommit$(if (-not $SkipPatch) { ' + sag patch' })"
