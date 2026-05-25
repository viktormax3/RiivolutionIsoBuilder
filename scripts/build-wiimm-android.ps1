param(
    [string]$WiimmRoot = "wiimms",
    [string]$OutputDirectory = "data/tools/android-arm64",
    [ValidateSet("arm64-v8a", "armeabi-v7a", "x86_64", "x86")]
    [string]$Abi = "arm64-v8a",
    [int]$ApiLevel = 24,
    [string]$AndroidNdkDirectory = "",
    [string]$Make = "",
    [ValidateSet("App", "AllNoPng", "All")]
    [string]$BuildSet = "App",
    [switch]$SkipHostPrep
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Find-Ndk([string]$ExplicitPath) {
    $candidates = @()
    if ($ExplicitPath) {
        $candidates += $ExplicitPath
    }

    foreach ($name in "ANDROID_NDK_ROOT", "ANDROID_NDK_HOME") {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) {
            $candidates += $value
        }
    }

    foreach ($name in "ANDROID_SDK_ROOT", "ANDROID_HOME") {
        $sdk = [Environment]::GetEnvironmentVariable($name)
        if ($sdk) {
            $ndkRoot = Join-Path $sdk "ndk"
            if (Test-Path $ndkRoot) {
                $latest = Get-ChildItem -Path $ndkRoot -Directory |
                    Sort-Object Name -Descending |
                    Select-Object -First 1
                if ($latest) {
                    $candidates += $latest.FullName
                }
            }
        }
    }

    $repoSdk = Join-Path (Get-Location) ".android-sdk/ndk"
    if (Test-Path $repoSdk) {
        $latest = Get-ChildItem -Path $repoSdk -Directory |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if ($latest) {
            $candidates += $latest.FullName
        }
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path (Join-Path $candidate "toolchains/llvm/prebuilt"))) {
            return (Resolve-FullPath $candidate)
        }
    }

    throw "Android NDK not found. Set ANDROID_NDK_ROOT, ANDROID_NDK_HOME, or pass -AndroidNdkDirectory."
}

function Get-HostTag {
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        return "windows-x86_64"
    }

    if ($IsMacOS) {
        return "darwin-x86_64"
    }

    return "linux-x86_64"
}

function Find-GitUnixTools {
    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if (-not $git) {
        return $null
    }

    $gitRoot = Split-Path (Split-Path $git.Source -Parent) -Parent
    $unixTools = Join-Path $gitRoot "usr/bin"
    if (Test-Path (Join-Path $unixTools "bash.exe")) {
        return $unixTools
    }

    return $null
}

function Find-Msys2UnixTools {
    $localMsys = Join-Path (Get-Location) ".msys2/msys64/usr/bin"
    if (Test-Path (Join-Path $localMsys "bash.exe")) {
        return (Resolve-FullPath $localMsys)
    }

    foreach ($root in "C:/msys64/usr/bin", "C:/msys2/usr/bin") {
        if (Test-Path (Join-Path $root "bash.exe")) {
            return (Resolve-FullPath $root)
        }
    }

    return $null
}

function Find-Make([string]$ExplicitMake, [string]$NdkDirectory, [string]$HostTag) {
    if ($ExplicitMake) {
        return $ExplicitMake
    }

    $ndkMake = Join-Path $NdkDirectory "prebuilt/$HostTag/bin/make.exe"
    if (Test-Path $ndkMake) {
        return $ndkMake
    }

    $pathMake = Get-Command make.exe -ErrorAction SilentlyContinue
    if ($pathMake) {
        return $pathMake.Source
    }

    $pathMake = Get-Command make -ErrorAction SilentlyContinue
    if ($pathMake) {
        return $pathMake.Source
    }

    throw "GNU Make not found. Install make or pass -Make."
}

function Find-HostCompiler([string]$MsysTools, [string]$FallbackCc, [string]$FallbackCxx) {
    if ($MsysTools) {
        $gcc = Join-Path $MsysTools "gcc.exe"
        $gxx = Join-Path $MsysTools "g++.exe"
        if ((Test-Path $gcc) -and (Test-Path $gxx)) {
            return @{
                Cc = $gcc
                Cxx = $gxx
                UseShim = $false
            }
        }
    }

    return @{
        Cc = $FallbackCc
        Cxx = $FallbackCxx
        UseShim = $true
    }
}

function New-HostCompilerShim([string]$ShimDirectory, [string]$HostCc, [string]$HostCxx) {
    New-Item -ItemType Directory -Force $ShimDirectory | Out-Null

    $gcc = @"
#!/usr/bin/env bash
exec "$HostCc" "`$@"
"@
    $gxx = @"
#!/usr/bin/env bash
exec "$HostCxx" "`$@"
"@

    Set-Content -Path (Join-Path $ShimDirectory "gcc") -Value $gcc -NoNewline
    Set-Content -Path (Join-Path $ShimDirectory "g++") -Value $gxx -NoNewline
}

function Get-TargetInfo([string]$Abi, [int]$ApiLevel) {
    switch ($Abi) {
        "arm64-v8a" {
            return @{
                Triple = "aarch64-linux-android"
                ClangPrefix = "aarch64-linux-android$ApiLevel"
                ToolFolder = "android-arm64"
            }
        }
        "armeabi-v7a" {
            return @{
                Triple = "arm-linux-androideabi"
                ClangPrefix = "armv7a-linux-androideabi$ApiLevel"
                ToolFolder = "android-arm"
            }
        }
        "x86_64" {
            return @{
                Triple = "x86_64-linux-android"
                ClangPrefix = "x86_64-linux-android$ApiLevel"
                ToolFolder = "android-x64"
            }
        }
        "x86" {
            return @{
                Triple = "i686-linux-android"
                ClangPrefix = "i686-linux-android$ApiLevel"
                ToolFolder = "android-x86"
            }
        }
    }
}

function Invoke-Checked {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    Write-Host "> $FilePath $($Arguments -join ' ')"
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_.ToString() }
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE"
    }
}

function Clear-CrossObjects([string]$ProjectDirectory) {
    Get-ChildItem -Path $ProjectDirectory -Recurse -Include *.o,*.d -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

function Patch-AndroidFunopen([string]$ProjectDirectory) {
    $file = Join-Path $ProjectDirectory "dclib/dclib-file.c"
    if (-not (Test-Path $file)) {
        throw "Expected dclib file not found: $file"
    }

    $text = Get-Content -Raw $file

    $anchor = "LineBuffer_t * OpenLineBuffer"
    $helpers = @"
#ifdef __ANDROID__
static int AndroidWriteLineBuffer ( void *cookie, const char *buf, int size )
{
    return (int)WriteLineBuffer((LineBuffer_t*)cookie,buf,size);
}

static int AndroidCloseLineBuffer ( void *cookie )
{
    return CloseLineBuffer((LineBuffer_t*)cookie);
}

#endif

"@
    if (-not $text.Contains("AndroidWriteLineBuffer")) {
        $text = $text.Replace($anchor, $helpers + $anchor)
    }

    if ($text.Contains("lb->fp = funopen(lb,0,AndroidWriteLineBuffer,0,AndroidCloseLineBuffer);")) {
        Set-Content -Path $file -Value $text -NoNewline
        return
    }

    $new = @"
#ifdef __ANDROID__
    lb->fp = funopen(lb,0,AndroidWriteLineBuffer,0,AndroidCloseLineBuffer);
#else
    static cookie_io_functions_t funcs =
    {
	0, // read
	(cookie_write_function_t*)WriteLineBuffer,
	0, // seek
	(cookie_close_function_t*)CloseLineBuffer
    };
    lb->fp = fopencookie(lb,"wb",funcs);
#endif
"@

    $pattern = '(?s)\s*static\s+cookie_io_functions_t\s+funcs\s*=\s*\{\s*0,\s*// read\s*\(cookie_write_function_t\*\)WriteLineBuffer,\s*0,\s*// seek\s*\(cookie_close_function_t\*\)CloseLineBuffer\s*\};\s*lb->fp\s*=\s*fopencookie\(lb,"wb",funcs\);'
    $patched = [regex]::Replace($text, $pattern, "`r`n$new", 1)
    if ($patched -eq $text) {
        throw "Could not patch fopencookie block in $file"
    }

    Set-Content -Path $file -Value $patched -NoNewline
}

function Patch-AndroidSignalMacroConflict([string]$ProjectDirectory) {
    $file = Join-Path $ProjectDirectory "dclib/dclib-tables.c"
    if (-not (Test-Path $file)) {
        throw "Expected dclib tables file not found: $file"
    }

    $text = Get-Content -Raw $file
    if (-not $text.Contains("si_ptr")) {
        return
    }

    $text = $text.Replace("si_ptr", "sizeof_info_ptr")
    Set-Content -Path $file -Value $text -NoNewline
}

function Patch-AndroidTerminalLibraries([string]$ProjectDirectory) {
    $file = Join-Path $ProjectDirectory "Makefile"
    if (-not (Test-Path $file)) {
        throw "Expected Makefile not found: $file"
    }

    $text = Get-Content -Raw $file
    if ($text.Contains("RIIVOLUTION_ANDROID_NO_NCURSES")) {
        $text = $text.Replace("LIBS`t`t+= `$(LIBPNG) -lm `$(XLIBS)", "LIBS`t`t+= -lm `$(XLIBS)")
        Set-Content -Path $file -Value $text -NoNewline
        return
    }

    $wiitPattern = "LIBS`t`t+= -lm -lncurses `$(XLIBS)"
    $wiitReplacement = @"
ifeq (`$(findstring android,`$(SYSTEM2)),android)
 # RIIVOLUTION_ANDROID_NO_NCURSES: Android NDK has no ncurses/tinfo.
 LIBS		+= -lm `$(XLIBS)
else
 LIBS		+= -lm -lncurses `$(XLIBS)
endif
"@

    $szsPattern = "LIBS`t`t+= `$(LIBPNG) -lm -lncurses `$(XLIBS)"
    $szsReplacement = @"
ifeq (`$(findstring android,`$(SYSTEM2)),android)
 # RIIVOLUTION_ANDROID_NO_NCURSES: Android NDK has no ncurses/tinfo.
 LIBS		+= -lm `$(XLIBS)
else
 LIBS		+= `$(LIBPNG) -lm -lncurses `$(XLIBS)
endif
"@

    if ($text.Contains($wiitPattern)) {
        $text = $text.Replace($wiitPattern, $wiitReplacement)
    } elseif ($text.Contains($szsPattern)) {
        $text = $text.Replace($szsPattern, $szsReplacement)
    } else {
        throw "Could not find ncurses linker line in $file"
    }

    Set-Content -Path $file -Value $text -NoNewline
}

function Patch-AndroidTerminalColorDetection([string]$ProjectDirectory) {
    $file = Join-Path $ProjectDirectory "dclib/dclib-color.c"
    if (-not (Test-Path $file)) {
        throw "Expected dclib color file not found: $file"
    }

    $text = Get-Content -Raw $file
    if ($text.Contains("RIIVOLUTION_ANDROID_NO_TERMINFO")) {
        return
    }

    $text = [regex]::Replace(
        $text,
        'int setupterm\(char \*term, int fildes, int \*errret\);\r?\nint tigetnum\(char \*capname\);',
        "#ifndef __ANDROID__`nint setupterm(char *term, int fildes, int *errret);`nint tigetnum(char *capname);`n#endif",
        1)

    $pattern = '(?s)if \( auto_mode == COLMD_AUTO \s*\)\s*\{\s*char \*term = getenv\("TERM"\);.*?#endif\s*\r?\n\s*\}'
    $replacement = @"
if ( auto_mode == COLMD_AUTO )
    {
     #ifdef __ANDROID__
	// RIIVOLUTION_ANDROID_NO_TERMINFO: Android has no ncurses/terminfo.
	auto_mode = force_on ? COLMD_8_COLORS : COLMD_OFF;
     #else
	char *term = getenv("TERM");

     #ifdef __CYGWIN__
	if (!term)
	    term = "cygwin";
     #else
	if (!term)
	    term = "vt100";
     #endif

	int error;
	setupterm(term,1,&error);
	const int ncol = tigetnum("colors");

	auto_mode = ncol >= 256 ? COLMD_256_COLORS
		  : ncol >=   8 ? COLMD_8_COLORS
				: COLMD_OFF;
	if ( auto_mode == COLMD_OFF && !strcmp(term,"cygwin") )
	    auto_mode = COLMD_8_COLORS;

     #ifdef TEST
	fprintf(stderr,">>> GetColorSetAuto(%d) => \"%s\" n=%d => %d [%s]\n",
		force_on, term, ncol,
		auto_mode, GetColorModeName(auto_mode,0) );
     #endif
     #endif
    }
"@

    $patched = [regex]::Replace($text, $pattern, $replacement, 1)
    if ($patched -eq $text) {
        throw "Could not patch terminal color detection in $file"
    }

    Set-Content -Path $file -Value $patched -NoNewline
}

function Patch-AndroidSingleToolDependency([string]$ProjectDirectory) {
    $file = Join-Path $ProjectDirectory "Makefile"
    if (-not (Test-Path $file)) {
        throw "Expected Makefile not found: $file"
    }

    $text = Get-Content -Raw $file
    if ($text.Contains("RIIVOLUTION_ANDROID_ONLY_TOOL")) {
        return
    }

    $blockWithWrapper = @"
TOBJ_NO_WRAPPER	= `$(filter-out `$(WRAPPER_OBJ), `$(TOBJ_ALL) )
ifneq (`$(ANDROID_ONLY_TOOL),)
 # RIIVOLUTION_ANDROID_ONLY_TOOL: avoid Android-only builds pulling every tool.
 TOBJ_ALL	= `$(TOBJ_`$(ANDROID_ONLY_TOOL))
 TOBJ_NO_WRAPPER	= `$(filter-out `$(WRAPPER_OBJ), `$(TOBJ_ALL) )
endif
"@

    if ($text.Contains("TOBJ_NO_WRAPPER`t= `$(filter-out `$(WRAPPER_OBJ), `$(TOBJ_ALL) )")) {
        $text = $text.Replace(
            "TOBJ_NO_WRAPPER`t= `$(filter-out `$(WRAPPER_OBJ), `$(TOBJ_ALL) )",
            $blockWithWrapper)
    } else {
        $block = @"
`$0
ifneq (`$(ANDROID_ONLY_TOOL),)
 # RIIVOLUTION_ANDROID_ONLY_TOOL: avoid Android-only builds pulling every tool.
 TOBJ_ALL	:= `$(TOBJ_`$(ANDROID_ONLY_TOOL))
endif
"@
        $patched = [regex]::Replace($text, '(?m)^TOBJ_ALL\s*:=.*$', $block, 1)
        if ($patched -eq $text) {
            throw "Could not patch single-tool dependency mode in $file"
        }
        $text = $patched
    }

    Set-Content -Path $file -Value $text -NoNewline
}

function Patch-AndroidFortifyPercentN([string]$ProjectDirectory) {
    $sfFile = Join-Path $ProjectDirectory "src/lib-sf.c"
    if (Test-Path $sfFile) {
        $text = Get-Content -Raw $sfFile
        $replacements = @{
            'snprintf(fbuf,sizeof(fbuf),"%u%n",slot,&fw);' = 'fw = snprintf(fbuf,sizeof(fbuf),"%u",slot);'
            'printf("%*s%3d%% %s in %s (%3.1f MiB/sec)  %n\r",' = 'wd = printf("%*s%3d%% %s in %s (%3.1f MiB/sec)  \r",'
            'printf("%*s%3d%% %s in %s (%3.1f MiB/sec) -> ETA %s   %n\r",' = 'wd = printf("%*s%3d%% %s in %s (%3.1f MiB/sec) -> ETA %s   \r",'
            '(double)total * 1000 / MiB / elapsed, &wd );' = '(double)total * 1000 / MiB / elapsed );'
            '(double)total * 1000 / MiB / elapsed, time2, &wd );' = '(double)total * 1000 / MiB / elapsed, time2 );'
        }
        foreach ($entry in $replacements.GetEnumerator()) {
            $text = $text.Replace($entry.Key, $entry.Value)
        }
        Set-Content -Path $sfFile -Value $text -NoNewline
    }

    $numericFile = Join-Path $ProjectDirectory "dclib/dclib-numeric.c"
    if (Test-Path $numericFile) {
        $text = Get-Content -Raw $numericFile
        $old = @"
    char linesep[200];
    int pre_pos;
    snprintf(linesep,sizeof(linesep),"%s%n%*s%s",
		eol,
		&pre_pos,
		indent, "",
		prefix ? prefix : ""
		);
"@
        $new = @"
    char linesep[200];
    int pre_pos = strlen(eol);
    snprintf(linesep,sizeof(linesep),"%s%*s%s",
		eol,
		indent, "",
		prefix ? prefix : ""
		);
"@
        if ($text.Contains($old)) {
            $text = $text.Replace($old, $new)
            Set-Content -Path $numericFile -Value $text -NoNewline
        }
    }

    $isoFile = Join-Path $ProjectDirectory "src/iso-interface.c"
    if (Test-Path $isoFile) {
        $text = Get-Content -Raw $isoFile
        $text = $text.Replace(
            'printf("%*s%-7.7s %s %n%-7s %6.6s %s\n",',
            'ver->info_indent = ver->indent + 7 + 1 + strlen(count_buf) + 1;' + "`r`n`t" + 'printf("%*s%-7.7s %s %-7s %6.6s %s\n",')
        $text = $text.Replace(
            'TRACE("%*s%-7.7s %s %n%-7s %6.6s %s\n",',
            'TRACE("%*s%-7.7s %s %-7s %6.6s %s\n",')
        $text = $text.Replace(
            'msg, count_buf, &ver->info_indent,',
            'msg, count_buf,')
        Set-Content -Path $isoFile -Value $text -NoNewline
    }

    $witFile = Join-Path $ProjectDirectory "src/wit.c"
    if (Test-Path $witFile) {
        $text = Get-Content -Raw $witFile
        $old = @"
	    printf("ID6   %s %*s Reg.  %n%d discs (%s)%n\n",
		    pt.head, size_fw, wd_get_size_unit(column_unit,"?"),
		    &n1, wlist.used,
		    wd_print_size(0,0,total_size,false,total_unit), &n2 );
"@
        $new = @"
	    printf("ID6   %s %*s Reg.  %d discs (%s)\n",
		    pt.head, size_fw, wd_get_size_unit(column_unit,"?"),
		    wlist.used,
		    wd_print_size(0,0,total_size,false,total_unit) );
	    n1 = n2 = 0;
"@
        $text = $text.Replace($old, $new)

        $old = @"
	    printf("ID6    %s %n%d disc%s (%s)%n\n",
		    pt.head, &n1,
		    wlist.used, wlist.used == 1 ? "" : "s",
		    wd_print_size(0,0,total_size,false,total_unit), &n2 );
"@
        $new = @"
	    printf("ID6    %s %d disc%s (%s)\n",
		    pt.head,
		    wlist.used, wlist.used == 1 ? "" : "s",
		    wd_print_size(0,0,total_size,false,total_unit) );
	    n1 = n2 = 0;
"@
        $text = $text.Replace($old, $new)
        Set-Content -Path $witFile -Value $text -NoNewline
    }
}

function Complete-HostUiPrep([string]$ProjectDirectory) {
    $genUi = Join-Path $ProjectDirectory "gen-ui"
    $genUiExe = Join-Path $ProjectDirectory "gen-ui.exe"
    if ((Test-Path $genUiExe) -and -not (Test-Path $genUi)) {
        Copy-Item -Force $genUiExe $genUi
    }
    if (-not (Test-Path $genUi)) {
        New-Item -ItemType File -Path $genUi -Force | Out-Null
    }

    $uiDirectory = Join-Path $ProjectDirectory "src/ui"
    if (Test-Path $uiDirectory) {
        Get-ChildItem -Path $uiDirectory -File -Include "ui-*.c", "ui-*.h", "ui.def" |
            ForEach-Object { $_.LastWriteTime = DateTime.Now }
    }
}

function Build-WiimmTool {
    param(
        [string]$ProjectDirectory,
        [string]$HostUiTarget,
        [string]$ToolName,
        [string]$Cc,
        [string]$Cxx,
        [string]$Strip,
        [string]$HostCc,
        [string]$HostStrip,
        [string]$SystemName,
        [bool]$SkipHostPrep
    )

    if (-not (Test-Path $ProjectDirectory)) {
        throw "Wiimm project not found: $ProjectDirectory"
    }

    Patch-AndroidFunopen $ProjectDirectory
    Patch-AndroidSignalMacroConflict $ProjectDirectory
    Patch-AndroidTerminalLibraries $ProjectDirectory
    Patch-AndroidTerminalColorDetection $ProjectDirectory
    Patch-AndroidSingleToolDependency $ProjectDirectory
    Patch-AndroidFortifyPercentN $ProjectDirectory

    Clear-CrossObjects $ProjectDirectory

    if (-not $SkipHostPrep) {
        Remove-Item -Force -ErrorAction SilentlyContinue `
            (Join-Path $ProjectDirectory "gen-ui"),
            (Join-Path $ProjectDirectory "gen-ui.exe")

        Invoke-Checked $script:MakeTool @(
            "-C", $ProjectDirectory,
            $HostUiTarget,
            "CC=$HostCc",
            "STRIP=:",
            "ANDROID_ONLY_TOOL=$ToolName"
        )
        Complete-HostUiPrep $ProjectDirectory
        Clear-CrossObjects $ProjectDirectory
    } else {
        Complete-HostUiPrep $ProjectDirectory
    }

    Invoke-Checked $script:MakeTool @(
        "-C", $ProjectDirectory,
        $ToolName,
        "CC=$Cc",
        "CPP=$Cxx",
        "STRIP=:",
        "HELPER_TOOLS=",
        "SYSTEM=android",
        "SYSTEM_LINUX=0",
        "SYSTEM2=$SystemName",
        "ANDROID_ONLY_TOOL=$ToolName",
        "XFLAGS=-U_FORTIFY_SOURCE -D_FORTIFY_SOURCE=0",
        "STATIC=0"
    )

    $toolPath = Join-Path $ProjectDirectory $ToolName
    if (-not (Test-Path $toolPath)) {
        throw "Expected output not found: $toolPath"
    }

    Invoke-Checked $Strip @($toolPath)

    return $toolPath
}

function Build-WiimmTools {
    param(
        [string]$ProjectDirectory,
        [string]$HostUiTarget,
        [string[]]$ToolNames,
        [string]$Cc,
        [string]$Cxx,
        [string]$Strip,
        [hashtable]$HostCompiler,
        [string]$HostStrip,
        [string]$SystemName,
        [bool]$SkipHostPrep
    )

    $built = @{}
    $hostPrepared = $SkipHostPrep
    foreach ($toolName in $ToolNames) {
        $tool = Build-WiimmTool `
            -ProjectDirectory $ProjectDirectory `
            -HostUiTarget $HostUiTarget `
            -ToolName $toolName `
            -Cc $Cc `
            -Cxx $Cxx `
            -Strip $Strip `
            -HostCc $HostCompiler.Cc `
            -HostStrip $HostStrip `
            -SystemName $SystemName `
            -SkipHostPrep:$hostPrepared
        $built[$toolName] = $tool
        $hostPrepared = $true
    }

    return $built
}

function Copy-AndroidToolOutputs {
    param(
        [hashtable]$Tools,
        [string]$OutputDirectory
    )

    foreach ($name in $Tools.Keys | Sort-Object) {
        $source = $Tools[$name]
        Copy-Item -Force $source (Join-Path $OutputDirectory $name)
        Copy-Item -Force $source (Join-Path $OutputDirectory "lib$name.so")
    }
}

$wiimmRootFull = Resolve-FullPath $WiimmRoot
$outputFull = Resolve-FullPath $OutputDirectory
$ndk = Find-Ndk $AndroidNdkDirectory
$hostTag = Get-HostTag
$target = Get-TargetInfo $Abi $ApiLevel
$toolBin = Join-Path $ndk "toolchains/llvm/prebuilt/$hostTag/bin"
$prebuiltBin = Join-Path $ndk "prebuilt/$hostTag/bin"
$exe = if ($hostTag.StartsWith("windows")) { ".cmd" } else { "" }
$stripExe = if ($hostTag.StartsWith("windows")) { ".exe" } else { "" }

$cc = Join-Path $toolBin "$($target.ClangPrefix)-clang$exe"
$cxx = Join-Path $toolBin "$($target.ClangPrefix)-clang++$exe"
$strip = Join-Path $toolBin "llvm-strip$stripExe"
$hostCc = Join-Path $toolBin "clang$stripExe"
$hostCxx = Join-Path $toolBin "clang++$stripExe"
$hostStrip = Join-Path $toolBin "llvm-strip$stripExe"
$script:MakeTool = Find-Make $Make $ndk $hostTag
$msysTools = Find-Msys2UnixTools
$hostCompiler = Find-HostCompiler $msysTools $hostCc $hostCxx

$pathEntries = @(".")
if ($hostCompiler.UseShim) {
    $hostShim = Resolve-FullPath ".wiimm-build-host-bin"
    New-HostCompilerShim $hostShim $hostCompiler.Cc $hostCompiler.Cxx
    $pathEntries += $hostShim
}

$pathEntries += @($msysTools, $toolBin, $prebuiltBin)
$gitUnixTools = Find-GitUnixTools
if ($gitUnixTools) {
    $pathEntries += $gitUnixTools
}

$env:PATH = (($pathEntries | Where-Object { $_ -and (Test-Path $_) }) -join [System.IO.Path]::PathSeparator) + [System.IO.Path]::PathSeparator + $env:PATH

foreach ($tool in @($cc, $cxx, $strip, $hostCompiler.Cc, $hostCompiler.Cxx, $hostStrip, $script:MakeTool)) {
    if (-not (Test-Path $tool)) {
        throw "NDK tool not found: $tool"
    }
}

$witProject = Join-Path $wiimmRootFull "wiimms-iso-tools-master/project"
$szsProject = Join-Path $wiimmRootFull "wiimms-szs-tools-master/project"

New-Item -ItemType Directory -Force $outputFull | Out-Null

$witTools = if ($BuildSet -in @("AllNoPng", "All")) { @("wit", "wwt", "wdf") } else { @("wit") }
$szsTools = switch ($BuildSet) {
    "All" {
        @("wszst", "wbmgt", "wctct", "wimgt", "wkclt", "wkmpt", "wlect", "wmdlt", "wpatt", "wstrt")
    }
    "AllNoPng" {
        # Android NDK does not ship libpng. These tools avoid XOBJ_IMAGE/XOBJ_LIB paths
        # that include src/lib-image*.c and require png.h.
        @("wbmgt", "wkclt", "wmdlt", "wpatt", "wstrt")
    }
    default {
        @("wstrt")
    }
}

$builtWitTools = Build-WiimmTools `
    -ProjectDirectory $witProject `
    -HostUiTarget "ui" `
    -ToolNames $witTools `
    -Cc $cc `
    -Cxx $cxx `
    -Strip $strip `
    -HostCompiler $hostCompiler `
    -HostStrip $hostStrip `
    -SystemName $target.ToolFolder `
    -SkipHostPrep:$SkipHostPrep

$builtSzsTools = Build-WiimmTools `
    -ProjectDirectory $szsProject `
    -HostUiTarget "run-ui" `
    -ToolNames $szsTools `
    -Cc $cc `
    -Cxx $cxx `
    -Strip $strip `
    -HostCompiler $hostCompiler `
    -HostStrip $hostStrip `
    -SystemName $target.ToolFolder `
    -SkipHostPrep:$SkipHostPrep

Copy-AndroidToolOutputs $builtWitTools $outputFull
Copy-AndroidToolOutputs $builtSzsTools $outputFull

if (-not ($IsWindows -or $env:OS -eq "Windows_NT")) {
    $outputs = @()
    foreach ($toolName in @($witTools + $szsTools)) {
        $outputs += (Join-Path $outputFull $toolName)
        $outputs += (Join-Path $outputFull "lib$toolName.so")
    }
    Invoke-Checked "chmod" @("+x" + $outputs)
}

Write-Host "Android Wiimm $BuildSet tools copied to $outputFull"
