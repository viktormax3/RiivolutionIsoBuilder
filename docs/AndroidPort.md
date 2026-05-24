# Android Port

The Android app is a platform host for the shared Avalonia UI and the same `RiivolutionIsoBuilder.Core` engine used by desktop.

## Current Shape

- `RiivolutionIsoBuilder.UI`: shared Avalonia app, views, styles, and workflow logic.
- `RiivolutionIsoBuilder.Avalonia`: desktop host using `Avalonia.Desktop`.
- `RiivolutionIsoBuilder.Android`: Android host using `Avalonia.Android`.
- `RiivolutionIsoBuilder.Core`: portable builder logic and Wiimm toolchain abstraction.

The Android project is intentionally present in the solution but excluded from normal solution builds. This keeps desktop CI and developer builds working on machines that do not have the Android workload installed.

## Requirements

- .NET 8 SDK.
- Android workload:

```powershell
dotnet workload restore .\src\RiivolutionIsoBuilder.Android\RiivolutionIsoBuilder.Android.csproj
```

or:

```powershell
dotnet workload install android
```

- Android SDK and JDK 11 or newer.
- Android-compatible `wit` and `wstrt` binaries for the device architecture.

Avalonia's Android documentation recommends a separate Android project with a `MainActivity` inheriting from `AvaloniaMainActivity`. That is the structure used here.

## Build

```powershell
.\scripts\build-android.ps1
```

GitHub Actions builds Android through `.github/workflows/build-android.yml`. The workflow installs Temurin JDK 17, installs the .NET Android workload, uses the runner-provided Android SDK, publishes a Release APK, and uploads it as an artifact.

If the Android workload is installed but the Android SDK or JDK are missing, install them into repo-local ignored folders:

```powershell
.\scripts\build-android.ps1 -InstallDependencies
```

To use an existing SDK:

```powershell
.\scripts\build-android.ps1 -AndroidSdkDirectory "C:\Users\<you>\AppData\Local\Android\Sdk"
```

To use an existing JDK:

```powershell
.\scripts\build-android.ps1 -JavaSdkDirectory "C:\Program Files\Microsoft\jdk-17"
```

For a release APK:

```powershell
.\scripts\build-android.ps1 -Configuration Release -Publish
```

Unsigned APK/AAB files are copied into `artifacts` when present.

## Native Wiimm Tools

`wit` and `wstrt` come from different Wiimm projects:

- `wit`: `wiimms-iso-tools`
- `wstrt`: `wiimms-szs-tools`

The source tree is intentionally kept outside git under ignored `wiimms/`. A local experimental build script can cross-compile Android binaries with the Android NDK:

```powershell
.\scripts\build-wiimm-android.ps1
```

By default it expects:

```text
wiimms/
  wiimms-iso-tools-master/
  wiimms-szs-tools-master/
```

and writes:

```text
data/tools/android-arm64/
  wit
  wstrt
```

Those binaries are packaged into the Android APK as assets. On first launch, the Android host copies bundled `data` files into the app-local `RiivolutionIsoBuilder/data` folder and marks `wit`/`wstrt` executable.

The script uses `ANDROID_NDK_ROOT`, `ANDROID_NDK_HOME`, or the newest NDK under `ANDROID_SDK_ROOT/ndk`. A custom NDK path can be passed explicitly:

```powershell
.\scripts\build-wiimm-android.ps1 -AndroidNdkDirectory "C:\Android\Sdk\ndk\27.0.12077973"
```

If the repo-local SDK was created with `build-android.ps1 -InstallDependencies`, install an NDK beside it before running the Wiimm build:

```powershell
.\.android-sdk\cmdline-tools\11.0\bin\sdkmanager.bat --sdk_root=.\.android-sdk "ndk;27.0.12077973"
```

On Windows, the script also expects a small Unix-like host toolchain. The verified local setup uses ignored repo-local MSYS2 under `.msys2/msys64` with `gcc`, `make`, and `ncurses-devel` available. The Android target binaries are still built by NDK clang; MSYS2 is only used to run Wiimm's host-side generation steps.

Other ABIs can be targeted if needed:

```powershell
.\scripts\build-wiimm-android.ps1 -Abi armeabi-v7a -OutputDirectory data/tools/android-arm
```

The Wiimm makefiles generate and run a small host helper named `gen-ui`. The script prepares generated UI files where needed, removes stale object files, patches Android's `funopen` path for Wiimm's line-buffer helper, avoids an Android `siginfo.h` macro collision, removes terminal-only `ncurses/tinfo` linker dependencies and terminal color probing for Android, and then cross-compiles only the final Android tools with the NDK clang toolchain. API 24 is the default target because Android exposes `funopen` there.

`wstrt` is built as an Android-only single-tool target so it does not pull in SZS image tooling that requires libpng. That keeps the first Android backend focused on the commands used by Riivolution ISO Builder.

## Runtime Layout

Android uses a writable app-local root instead of assuming `data` sits next to the executable:

```text
<app local data>/RiivolutionIsoBuilder/
  data/
    banner/
    catalog/
    gct/
    mods/
    tools/
    xml/
  games/
  output/
  work/
```

Tool discovery remains the same conceptually:

- `data/tools/android-arm64`
- `data/tools/android-arm`
- `data/tools/android`
- `RIIVOLUTION_ISO_BUILDER_TOOLS`

The app can open a game image manually through the Avalonia storage picker. Automatic scanning is limited by Android storage permissions and by where the user places files.

## Toolchain Notes

The current backend still executes external `wit` and `wstrt` binaries. That is the fastest path to validate Android feasibility, but it has constraints:

- Android binaries must be executable on the target ABI.
- Linux desktop binaries such as `linux-x64` should not be treated as app-compatible Android binaries. Termux, proot, emulation, or another compatibility layer can make some binaries appear to work, but the APK should target native `android-arm64` or `android-arm` tools.
- Binaries copied into app data may need executable permissions, and some Android versions/devices restrict executing files from writable app storage. If that happens, the safer packaging path is to ship supported native binaries as app native libraries or move the toolchain behind a native library backend.
- Files selected through Android storage providers must expose a local path for the current core engine.
- Large ISO/WBFS processing needs enough free app-accessible storage.
- A future backend may use native libraries or partial managed implementations for operations now delegated to Wiimm tools.

## Verified So Far

- Desktop solution builds without requiring Android workload.
- Android project is isolated and detected by `dotnet`.
- Native `wit` and `wstrt` build locally as `ELF64` `AArch64` binaries and are copied to `data/tools/android-arm64`.
- Debug Android APK builds locally and contains `assets/data/tools/android-arm64/wit`, `assets/data/tools/android-arm64/wstrt`, and `assets/data/tools/titles.txt`.
- Android build currently stops with `NETSDK1147` on machines without the Android workload, which is expected.
- If the workload exists but the SDK/JDK does not, MSBuild reports `XA5300`; use `-InstallDependencies` or pass `-AndroidSdkDirectory` and `-JavaSdkDirectory`.

## References

- Avalonia Android deployment: https://docs.avaloniaui.net/docs/deployment/android
- Avalonia cross-platform solution setup: https://docs.avaloniaui.net/docs/app-development/cross-platform-solution-setup
- Avalonia Android platform guide: https://docs.avaloniaui.net/docs/platform-specific-guides/android
