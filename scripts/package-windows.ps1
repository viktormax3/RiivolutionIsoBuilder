param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/RiivolutionIsoBuilder.App/RiivolutionIsoBuilder.App.csproj"
$runtimeLabel = if ([string]::IsNullOrWhiteSpace($Runtime)) { "win-x64" } else { $Runtime }
$variant = if ($FrameworkDependent) { "framework-dependent" } else { "standalone" }
$publishDir = Join-Path $repoRoot "publish/$runtimeLabel-$variant"
$packageName = "RiivolutionIsoBuilder-$runtimeLabel-$variant"
$packageDir = Join-Path $repoRoot "$OutputRoot/$packageName"
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
New-Item -ItemType Directory -Force $publishDir, $packageDir | Out-Null

Invoke-DotNet restore $project --runtime $runtimeLabel --force

if ($FrameworkDependent) {
    Invoke-DotNet publish $project `
        --configuration $Configuration `
        --runtime $runtimeLabel `
        --self-contained false `
        --no-restore `
        "-p:PublishSingleFile=true" `
        "-p:PublishReadyToRun=false" `
        "-p:DebugType=None" `
        "-p:DebugSymbols=false" `
        --output $publishDir
} else {
    Invoke-DotNet publish $project `
        --configuration $Configuration `
        --runtime $runtimeLabel `
        --self-contained true `
        --no-restore `
        "-p:PublishSingleFile=true" `
        "-p:EnableCompressionInSingleFile=true" `
        "-p:IncludeNativeLibrariesForSelfExtract=true" `
        "-p:PublishReadyToRun=false" `
        "-p:PublishTrimmed=false" `
        "-p:DebugType=None" `
        "-p:DebugSymbols=false" `
        --output $publishDir
}

Get-ChildItem -LiteralPath $publishDir -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force

$packageDataDir = Join-Path $packageDir "data"
New-Item -ItemType Directory -Force $packageDataDir | Out-Null
foreach ($dataChild in @("banner", "catalog", "gct", "tools", "xml")) {
    $source = Join-Path $repoRoot "data/$dataChild"
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $packageDataDir $dataChild) -Recurse -Force
    }
}

New-Item -ItemType Directory -Force `
    (Join-Path $packageDataDir "mods"), `
    (Join-Path $packageDir "games"), `
    (Join-Path $packageDir "output"), `
    (Join-Path $packageDir "work") | Out-Null

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $packageDir -Force

$notes = @"
Riivolution ISO Builder

1. Put your .iso/.wbfs/.ciso/.wdf/.wia backups in the games folder, or choose one from the app.
2. Put mod .zip files in data/mods and register them in data/catalog/mods.json.
3. Run RiivolutionIsoBuilder.exe.

This package includes wit/wstrt and their DLLs in data/tools.
"@

Set-Content -LiteralPath (Join-Path $packageDir "LEEME.txt") -Value $notes -Encoding UTF8

Compress-Archive -LiteralPath $packageDir -DestinationPath $zipPath -Force
Write-Host "Package: $zipPath"
