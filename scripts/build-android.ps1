param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$OutputRoot = "artifacts",

    [string]$AndroidSdkDirectory,

    [string]$JavaSdkDirectory,

    [switch]$InstallDependencies,

    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/RiivolutionIsoBuilder.Android/RiivolutionIsoBuilder.Android.csproj"

if ([string]::IsNullOrWhiteSpace($AndroidSdkDirectory)) {
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_SDK_ROOT)) {
        $AndroidSdkDirectory = $env:ANDROID_SDK_ROOT
    } elseif (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
        $AndroidSdkDirectory = $env:ANDROID_HOME
    } else {
        $AndroidSdkDirectory = Join-Path $repoRoot ".android-sdk"
    }
}

if ([string]::IsNullOrWhiteSpace($JavaSdkDirectory)) {
    if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
        $JavaSdkDirectory = $env:JAVA_HOME
    } else {
        $JavaSdkDirectory = Join-Path $repoRoot ".android-jdk"
    }
}

$msbuildProperties = @(
    "-p:AndroidSdkDirectory=$AndroidSdkDirectory",
    "-p:JavaSdkDirectory=$JavaSdkDirectory"
)

if ($InstallDependencies) {
    New-Item -ItemType Directory -Force $AndroidSdkDirectory | Out-Null
    New-Item -ItemType Directory -Force $JavaSdkDirectory | Out-Null
} elseif (-not (Test-Path $AndroidSdkDirectory)) {
    throw "Android SDK not found at '$AndroidSdkDirectory'. Run scripts/build-android.ps1 -InstallDependencies, pass -AndroidSdkDirectory, or set ANDROID_SDK_ROOT."
} elseif (-not (Test-Path (Join-Path $JavaSdkDirectory "bin\javac.exe"))) {
    throw "Java SDK not found at '$JavaSdkDirectory'. Run scripts/build-android.ps1 -InstallDependencies, pass -JavaSdkDirectory, or set JAVA_HOME."
}

function Invoke-DotNet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE"
    }
}

if ($Publish) {
    $args = @("publish", $project, "--configuration", $Configuration, "--framework", "net8.0-android", "-p:AndroidPackageFormats=apk") + $msbuildProperties
    Invoke-DotNet $args
} else {
    if ($InstallDependencies) {
        $installArgs = @("build", $project, "--configuration", $Configuration, "--framework", "net8.0-android", "-t:InstallAndroidDependencies", "-p:AcceptAndroidSDKLicenses=true") + $msbuildProperties
        Invoke-DotNet $installArgs
    }

    $buildArgs = @("build", $project, "--configuration", $Configuration, "--framework", "net8.0-android") + $msbuildProperties
    Invoke-DotNet $buildArgs
}

$publishRoot = Join-Path $repoRoot "src/RiivolutionIsoBuilder.Android/bin/$Configuration/net8.0-android"
$targetRoot = Join-Path $repoRoot $OutputRoot
New-Item -ItemType Directory -Force $targetRoot | Out-Null

Get-ChildItem -Path $publishRoot -Recurse -Include *.apk,*.aab -ErrorAction SilentlyContinue |
    Copy-Item -Destination $targetRoot -Force
