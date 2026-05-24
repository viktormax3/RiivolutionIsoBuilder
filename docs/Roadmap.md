# Roadmap

This project is currently a Windows desktop app, but the long-term goal is a real cross-platform builder with a portable core and platform-specific packaging.

## 1. Core Extraction

Status: started.

The build engine, catalog loading, Riivolution XML parsing, patch planning, archive extraction, and external tool execution should live outside the UI layer.

Current direction:

- Keep reusable logic in `RiivolutionIsoBuilder.Core`.
- Keep WinForms-specific code in `RiivolutionIsoBuilder.App`.
- Keep `RiivolutionIsoBuilder.RiivProbe` as an internal console diagnostic tool.
- Make future UI clients consume the same core instead of reimplementing builder behavior.

## 2. Toolchain Abstraction

Status: started.

The current implementation directly calls Wiimm's `wit` and `wstrt` command-line tools. That is practical and reliable, but the core should depend on a small interface instead of hardcoded process calls.

Target operations:

- Inspect a Wii image.
- Extract the data partition to an editable filesystem.
- Rebuild an image from the edited filesystem.
- Edit output ID, TMD ID, and internal title.
- Apply DOLPATCH XML changes.
- Apply GCT patches.

The first backend is `WiimmToolchain`, which continues to run external `wit` and `wstrt` binaries behind an `IWiiToolchain` interface. Later backends may use platform-specific binaries, a native library, or a partial managed implementation.

## 3. Desktop Cross-Platform GUI

Status: started.

The current GUI uses WinForms and targets `net8.0-windows`, so it cannot become a real macOS/Linux app through packaging alone.

Preferred direction:

- Add an Avalonia desktop app.
- Reuse `RiivolutionIsoBuilder.Core`.
- Keep the existing WinForms app working until the Avalonia app reaches feature parity.
- Publish desktop builds for Windows first, then Linux and macOS after toolchain packaging is solved for each platform.

The initial `RiivolutionIsoBuilder.Avalonia` project can start, discover the project layout, scan compatible images, manually choose an ISO, list catalog mods, load native Riivolution XML files with option choices, load standalone GCT patches, suggest output IDs, run builds, and show logs. It is not feature-complete yet.

## 4. Platform Tool Packaging

Status: planned.

Windows releases currently bundle Wiimm tools under `data/tools`. Cross-platform releases need an equivalent strategy.

Possible approaches:

- Bundle `wit` and `wstrt` per runtime when redistribution and licensing are handled correctly.
- Let users point the app to an installed Wiimm tools directory.
- Use a hybrid model: bundled tools on Windows, detected tools on Linux/macOS during early testing.

Current tool discovery supports the bundled `data/tools` folder, platform-specific folders such as `data/tools/linux-x64` and `data/tools/osx-arm64`, legacy `Base/bin` layouts, and the `RIIVOLUTION_ISO_BUILDER_TOOLS` environment variable.

## 5. Android Feasibility

Status: started.

Android is technically interesting because Wiimm tools can be used from Linux-like environments such as Termux, but a normal graphical Android app has stricter file access, native binary, storage, and performance constraints.

Current direction:

- Keep shared Avalonia UI in `RiivolutionIsoBuilder.UI`.
- Keep `RiivolutionIsoBuilder.Avalonia` as the desktop host.
- Add `RiivolutionIsoBuilder.Android` as the Android host.
- Keep Android out of the default solution build until CI/dev machines have the Android workload.
- Use a writable app-local root on Android instead of relying on executable-relative `data`.
- Continue using external Wiimm tools first, then evaluate native-library or managed replacements.

The detailed Android plan is tracked in [AndroidPort.md](AndroidPort.md).
