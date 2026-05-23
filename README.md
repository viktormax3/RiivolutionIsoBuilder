# Riivolution ISO Builder

Windows desktop tool for building standalone Wii disc images from Riivolution-style mods.

The app extracts a clean game image with Wiimm's tools, copies the selected mod files into the extracted filesystem, applies any configured DOL patches, edits the output game ID/name, and writes a new image in the selected format.

## Quick Start

From the repository root:

```powershell
dotnet run --project .\src\RiivolutionIsoBuilder.App\RiivolutionIsoBuilder.App.csproj
```

Typical workflow:

1. Put your game backup in `games`, or use `Elegir ISO` to select it manually.
2. Put the matching mod archive in `data/mods`.
3. Start the app and press `Buscar`.
4. Select the detected game and mod.
5. Confirm the output `ID6` and output format.
6. Press `Crear mod`.

Generated images are written to `output/<mod name> [ID6]/`.

You can also use `Elegir XML` to load a native Riivolution XML, or `Elegir GCT` to apply a standalone Ocarina `.gct` patch directly to the selected game.

## Requirements

- Windows
- .NET 8 SDK for development
- A legally obtained Wii game backup in `.iso`, `.wbfs`, `.ciso`, `.wdf`, or `.wia` format
- ZIP-compatible mod archives placed in `data/mods`

Wiimm's `wit.exe`, `wstrt.exe`, `titles.txt`, and required Cygwin DLLs are expected in `data/tools`. They are already present in this project layout.

## Supported Games and Mods

The catalog targets Super Mario Galaxy and Super Mario Galaxy 2:

- Super Mario Galaxy: `RMGE01`, `RMGP01`, `RMGJ01`
- Super Mario Galaxy 2: `SB4E01`, `SB4P01`, `SB4J01`

Registered mods are defined in [data/catalog/mods.json](data/catalog/mods.json). The current catalog includes:

- Kaizo Mario Galaxy
- SMG1 The Green Stars
- Neo Mario Galaxy
- SMG64 Holiday Special
- SMG2 The New Green Star
- SMG2 The Lost Levels
- Super Mayro Galaxy
- Super Mayro Galaxy Twoad

The repository includes patch files, banner resources, and Wiimm tool binaries needed by the builder. It does not include commercial game images or mod archives.

## Project Folders

```text
data/
  banner/      Optional custom banners copied into generated images.
  catalog/     Game/mod metadata used by the UI.
  gct/         GCT patches used by catalog entries.
  mods/        Local mod archives. Put ZIP packages here.
  tools/       wit, wstrt, titles.txt, and runtime DLLs.
  xml/         Preprocessed XML patches for wit DOLPATCH.
games/         Recommended place for input game backups.
output/        Generated images.
work/          Temporary extraction/build folder.
docs/          Notes about the Riivolution XML interpreter.
src/           WinForms app and probe utility.
```

The app scans `games` first. It can also find images selected manually with `Elegir ISO`.

## Output ID and Save Data

The output ID is intentionally changed from the original game ID. Riivolution can redirect save data at runtime, but a rebuilt standalone image cannot rely on Riivolution's save redirection. Giving the output a distinct ID lets the generated image use its own save slot instead of colliding with the original game.

For catalog mods, the ID is usually:

```text
<mod prefix><original region/maker suffix>
```

For example, a PAL SMG2 image `SB4P01` with Neo Mario Galaxy becomes `NMGP01`.

## Patch Modes

Catalog entries can use one of three patch modes:

- `None`: copy mod files only.
- `Gct`: apply a `.gct` file to `sys/main.dol` with `wstrt patch --add-sect`.
- `Xml`: apply a preprocessed XML patch with `wit DOLPATCH`.

The XML files in `data/xml` are not full Riivolution XML files. They are reduced patch files prepared for `wit DOLPATCH`.

Standalone GCT files do not need a catalog entry. Select the game, press `Elegir GCT`, choose the file, confirm the generated `ID6`, and build.

## Native Riivolution XML

The GUI also has `Elegir XML` for loading a real Riivolution XML directly. This path is meant for mods that ship a `riivolution/<mod>.xml` plus a matching file tree.

Current behavior:

- Reads sections, options, choices, patch definitions, folders, files, savegame entries, and memory patch entries.
- Resolves built-in variables such as `{$__region}`, `{$__gameid}`, and `{$__maker}`.
- Shows a choices dialog for XML options, including `Disabled`, so multi-mod XML files can be built with the intended patch set.
- Treats Riivolution option defaults as 1-based. `0` or a missing default means `Disabled`.
- Copies mapped folders/files into the extracted game filesystem.
- Honors `create="true"` for native file/folder mappings. Without it, only files already present in the extracted game filesystem are replaced.
- Handles `recursive`, `resize`, `offset`, and `length` for native file/folder mappings where they can be represented on an extracted filesystem.
- Converts supported memory patches into a generated `wit DOLPATCH` XML.
- Resolves the XML root, each patch `root`, absolute external paths, and `valuefile` references separately.
- Passes `--source <mod-root>` so generated `valuefile` references can be resolved by `wit`.
- Filters memory patches with `original` values against the extracted `main.dol` before generating the patch XML.

Limitations:

- Savegame redirection is represented by changing the output ID, not by emulating Riivolution at runtime.
- Riivolution `macro`, multi-XML merging, and special `memory search` / `ocarina` behavior are not fully emulated.
- Some complex Riivolution packages may still need catalog-specific handling or preprocessed XML.

More implementation notes are in [docs/RiivolutionInterpreter.md](docs/RiivolutionInterpreter.md).

## Adding a Catalog Mod

1. Put the archive in `data/mods`, usually as `<ID>.zip`.
2. Add an entry in `data/catalog/mods.json`.
3. If needed, add a GCT patch to `data/gct` or a preprocessed DOLPATCH XML to `data/xml`.
4. If the mod has a custom banner, add `<bannerId>.bnr` and `<bannerId>.arc` to `data/banner`.

Useful catalog fields:

- `id`: short mod key.
- `displayName`: name shown in the UI and used in the output folder.
- `gameKey`: target game key from the catalog, such as `smg1` or `smg2`.
- `archive`: archive filename under `data/mods`.
- `extractedFolder`: folder expected after extracting the archive.
- `outputIdPrefix`: first three characters used for the generated ID6.
- `defaultPatch`: `None`, `Gct`, or `Xml`.
- `patchFile`: optional patch filename.
- `bannerId`: optional banner resource prefix.
- `unsupportedOutputIds`: output IDs that should be rejected.
- `patchOverrides`: per-output-ID patch mode overrides.

## Probe Utility

`RiivolutionIsoBuilder.RiivProbe` is a console helper for inspecting a native Riivolution XML and previewing the generated patch plan.

```powershell
dotnet run --project .\src\RiivolutionIsoBuilder.RiivProbe\RiivolutionIsoBuilder.RiivProbe.csproj -- ".\path\to\riivolution.xml" PAL SB4P01
```

It prints the detected sections/options, active patches, file mappings, memory patch counts, and a generated DOLPATCH XML preview.

## Packaging

Create a Windows package with:

```powershell
.\scripts\package-windows.ps1
```

By default this publishes a self-contained `win-x64` build, copies the required `data` folders, creates empty `games`, `output`, and `work` folders, and writes a ZIP under `artifacts`.

Useful options:

```powershell
.\scripts\package-windows.ps1 -Runtime portable -FrameworkDependent
.\scripts\package-windows.ps1 -Configuration Debug -OutputRoot artifacts-dev
```

## Development

Build the solution:

```powershell
dotnet build .\RiivolutionIsoBuilder.sln
```

Main projects:

- [src/RiivolutionIsoBuilder.App](src/RiivolutionIsoBuilder.App): WinForms builder.
- [src/RiivolutionIsoBuilder.RiivProbe](src/RiivolutionIsoBuilder.RiivProbe): console XML inspection tool.
