param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/RiivolutionIsoBuilder.App/RiivolutionIsoBuilder.App.csproj"
$runtimeLabel = if ([string]::IsNullOrWhiteSpace($Runtime) -or $Runtime -eq "portable") { "portable" } else { $Runtime }
$publishDir = Join-Path $repoRoot "publish/$runtimeLabel"
$packageName = "RiivolutionIsoBuilder-$runtimeLabel"
$packageDir = Join-Path $repoRoot "$OutputRoot/$packageName-package"
$zipPath = Join-Path $repoRoot "$OutputRoot/$packageName.zip"

function Invoke-DotNet {
    dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE"
    }
}

Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $packageDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

$isPortable = $runtimeLabel -eq "portable"
$selfContained = if ($FrameworkDependent -or $isPortable) { "false" } else { "true" }

if ($isPortable) {
    Invoke-DotNet restore $project
    Invoke-DotNet publish $project `
        --configuration $Configuration `
        --self-contained false `
        --no-restore `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        --output $publishDir
} else {
    Invoke-DotNet restore $project --runtime $Runtime
    Invoke-DotNet publish $project `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained $selfContained `
        --no-restore `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        --output $publishDir
}

New-Item -ItemType Directory -Force $packageDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force

$packageDataDir = Join-Path $packageDir "data"
New-Item -ItemType Directory -Force $packageDataDir | Out-Null
foreach ($dataChild in @("banner", "catalog", "gct", "tools", "xml")) {
    $source = Join-Path $repoRoot "data/$dataChild"
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $packageDataDir $dataChild) -Recurse -Force
    }
}
New-Item -ItemType Directory -Force (Join-Path $packageDataDir "mods") | Out-Null
if (Test-Path -LiteralPath (Join-Path $repoRoot "data/mods/.gitkeep")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "data/mods/.gitkeep") -Destination (Join-Path $packageDataDir "mods/.gitkeep") -Force
}
New-Item -ItemType Directory -Force `
    (Join-Path $packageDir "games"), `
    (Join-Path $packageDir "output"), `
    (Join-Path $packageDir "work") | Out-Null

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $packageDir -Force

$notes = @"
Riivolution ISO Builder

1. Coloca tus backups .iso/.wbfs/.ciso/.wdf/.wia en la carpeta games, o usa el boton Elegir ISO.
2. Coloca archivos .zip de mods en data/mods y registralos en data/catalog/mods.json.
3. Ejecuta RiivolutionIsoBuilder.exe.

Este paquete incluye wit/wstrt y sus DLLs en data/tools.
"@

Set-Content -LiteralPath (Join-Path $packageDir "LEEME.txt") -Value $notes -Encoding UTF8

Compress-Archive -LiteralPath $packageDir -DestinationPath $zipPath -Force
Write-Host "Package: $zipPath"

