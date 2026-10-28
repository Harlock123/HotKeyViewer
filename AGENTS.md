# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers what the app does, why it reads both
the compositor and the config files, packaging, theming and HiDPI; this file covers how to build, test and change
it safely.

## What this is

A Linux desktop utility in C# on .NET 10 (`net10.0`) and Avalonia 12.1 that lists every Hyprland key binding in a
searchable window. It joins the live `hyprctl binds` output to the config files that defined each binding, and can
remove a binding or open its definition in an editor. It is built for Hyprland, with extras for Omarchy, and only
does anything useful on a Linux machine running Hyprland.

## Commands

```bash
dotnet build HotKeyViewer.sln                         # global.json: SDK 10.0.100, rollForward latestFeature
dotnet test HotKeyViewer.sln                          # xUnit 2; the window tests run on Avalonia.Headless
dotnet run --project src/HotKeyViewer -- --print      # list bindings as text, no window
./packaging/build.sh                                  # NativeAOT build into dist/aot/linux-x64 (needs clang)
./packaging/build.sh --single-file                    # one self-extracting file instead
```

Run options handled in `Program.cs`: `--help`/`-h`, `--toggle`, `--print`/`-p` and `--debug-keycodes`. The
executable is named `hotkeyviewer` (`AssemblyName`). `HOTKEYVIEWER_TEXT_SCALE` overrides the text scale.

## Layout

The README's own layout section lists every file. In short:

- `src/HotKeyViewer/Sources/`: readers. `HyprctlBindsReader` parses the plain-text `hyprctl binds`;
  `LuaConfigScanner` runs a Lua config against the stub API in `scan-lua-config.lua` (an embedded resource);
  `ConfConfigScanner` walks classic `hyprland.conf` files; `KeycodeResolver` names `code:N` keys.
- `src/HotKeyViewer/Services/`: `HotKeyCatalogBuilder` (in `HotKeyCatalog.cs`) merges live binds with their
  definitions. `BindingRemover` plans and applies removals. `ProcessRunner` runs external commands and never
  throws for a missing one. The rest handle theming, scaling, the editor, the single instance and the float rule.
- `src/HotKeyViewer/Models/`: `HotKey` and `KeyChord` (parsing and display of chords).
- `src/HotKeyViewer/ViewModels/` and `Views/`: one window, `MainWindow`, driven by `MainViewModel`.
- `tests/HotKeyViewer.Tests/`: parser, merge, filter, removal-planning, theme and keyboard tests.
  `KeyboardNavigationTests` drives the real `MainWindow` with key events through a shared
  `HeadlessUnitTestSession`.
- `packaging/`: `build.sh` (AOT or single file), `install.sh` (installs under `~/.local`) and the desktop entry.

## Rules that keep it working

- **Use the plain-text `hyprctl binds`, not `-j`.** The JSON form loses keycode binds. The README explains why.
- **External tools are optional.** `lua`, `xkbcli`, `gsettings` and `hyprctl` may all be missing. Every reader
  must fall back quietly, as `ProcessRunner` does; the stock-Hyprland behaviour is pinned by
  `VanillaHyprlandTests`.
- **Removal edits the user's real config.** `BindingRemover.ApplyAsync` backs up the file, edits it, asks Hyprland
  to reload, and restores the backup if the config is rejected. Tests only cover `Plan` and the text helpers;
  never call `ApplyAsync` from a test.
- **Tests write only to temp folders.** File-based tests use `Directory.CreateTempSubdirectory`. Keep new ones
  away from `~/.config/hypr`.
- **Test key handling through the window.** Routing bugs only show when real key events go through `MainWindow`,
  so add keyboard cases to `KeyboardNavigationTests`, not by calling handlers directly. Avalonia has one UI thread
  per test assembly, so use the shared session.
- **Stay AOT-safe.** `packaging/build.sh` publishes with `PublishAot`. Compiled bindings are on by default; avoid
  reflection-based code that trimming would break.
- **Internals are visible to the tests** (`InternalsVisibleTo`), so helpers can stay `internal`.

## Making a change

- Add or update tests with every change to parsing, merging, filtering or removal. `dotnet test` must pass.
- If the window changes, refresh the screenshots in `docs/` and check the README text that refers to them.
- Update `README.md` (key table, options, behaviour on non-Omarchy Hyprland) to match.
- `dist/` is ignored by git; don't commit builds.

There is no CI workflow; builds and releases are made locally with `packaging/build.sh`.
