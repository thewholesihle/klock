# Klock

A wordless analog clock screensaver for **Windows 10 (1809+) and Windows 11**. The whole screen is the dial: one
tone with a fine paper grain, large numerals, embossed pill indices and slim pointed hands. GPU-rendered and
vector-crisp, with day and night dials, glowing lume and no telemetry.

[**Download the latest release**](https://github.com/OWNER/klock/releases/latest) (x64 and ARM64 installers).
Run the installer; Screen Saver Settings opens with Klock pre-selected. It never switches your screensaver silently.

## Features

* **16 dials**, each with a matching night look, and **14 lume colours** for the glowing hands and indices.
* **Three movements:** quartz (a tick each second), mechanical (swing and settle) and smooth (continuous sweep).
  Hand positions are a pure function of the time, so they are exact by construction.
* **Numerals** in Panchang, Clash Display, Satoshi, Melodrama or Klock's built-in monoline digits.
* **Day and night** by Windows theme, by sunrise and sunset, or on a schedule.
* **Burn-in protection** for OLED: the face drifts a few pixels and highlights breathe.
* **Every monitor**, each at its own DPI and refresh rate. Pauses when the display is off and drops its frame rate on battery.
* **10 languages** including right-to-left: English, Français, Deutsch, Español, Italiano, 日本語, 简体中文, Русский, Türkçe, العربية.
* **Private:** the only network request is an optional update check (one GET of a static JSON file; nothing about you is sent).

## Build

Requirements: Windows 10/11, the .NET 8 SDK, and Inno Setup 6.3+ for the installer.

```powershell
./build.ps1                          # tests, x64 + arm64 builds, installer if Inno Setup is present
./build.ps1 -Arch x64 -SkipTests     # fast x64 build
./build.ps1 -Sign                    # sign with KLOCK_SIGN_PFX / KLOCK_SIGN_PASSWORD
dotnet test                          # unit tests plus pixel-level rendering tests (WARP, no GPU needed)
```

Output lands in `dist/<arch>/stage` (`Klock.scr`, `Klock.Settings.exe`) and `dist/Klock-Setup-*.exe`, plus
`dist/latest.json`, the update manifest.

**Fonts:** the four numeral typefaces are not in this repository (their licence forbids redistribution). Download
Panchang Extrabold, Clash Display Semibold, Satoshi Black and Melodrama Bold from [Fontshare](https://www.fontshare.com)
and place the `.otf` files in `assets/fonts/` with the names listed in `THIRD-PARTY-NOTICES.md` before building.

### Try it

| Command | What it does |
|---|---|
| `Klock.scr /s` | full-screen on every monitor; any key, click or mouse movement exits |
| `Klock.scr /c` | open settings |
| `Klock.scr --preview` | windowed demo (Esc exits) |
| `Klock.scr --capture out.png [--dial ocean] [--night 1] [--time 10:08:35]` | render a still image |
| `Klock.scr --sheet sheet.png` | contact sheet of all 16 dials |
| `Klock.Settings.exe --lang ar` | preview a language without saving it |

More checks are in [docs/QA-CHECKLIST.md](docs/QA-CHECKLIST.md).

## Project layout

```
src/
  Klock.Core         movement maths, themes, settings, update checker (no UI, no GPU)
  Klock.Renderer     Direct3D 11 / Direct2D drawing via Vortice, one render thread per monitor
  Klock.Screensaver  the .scr host (/s /c /p), multi-monitor, dev commands
  Klock.Settings     WPF settings window with live preview
tests/Klock.Tests    xUnit, including rendering tests on the WARP software rasteriser
tools/               string sources (tools/strings) and resx generator
installer/klock.iss  Inno Setup script
website/             static landing page (see website/README.md)
```

Klock is a working title: it lives in `AppInfo.Name` and the installer script.

## Extending

**A dial** is one row in `src/Klock.Core/Themes/DialTheme.cs`:

```csharp
D("ember", "#E9A15B", "#C77C34",   // face: centre -> edge
           "#4A2A08",              // ink: numerals and minute dashes
           "#FFF9F0",              // hands and pill indices
           "#4A2A08",              // centre cap dot
           dim: 0.86f),            // how far the face fades toward black at night
```

Add `dial.ember=Ember` to every file in `tools/strings/` (a test fails otherwise) and run `tools/gen-resx.ps1`
(or `build.ps1`). Lume colours are one row in `LumeCatalog`.

**A language:** add `tools/strings/<culture>.txt` with every key; a test checks key and `{0}` placeholder parity.

## Releases and updates

Releases are hosted on GitHub. After `build.ps1`, create a release tagged `v<version>` and attach both
`Klock-Setup-*.exe` files and `dist/latest.json`. The app reads
`https://github.com/OWNER/klock/releases/latest/download/latest.json`. Set the real repository in
`AppInfo.GitHubRepo` (`src/Klock.Core/AppInfo.cs`) first.

## Fonts and licences

The numeral typefaces are by the Indian Type Foundry, used under the **ITF Free Font License 2.0**
(`assets/fonts/*-FFL.txt`). They are embedded byte-for-byte unmodified, loaded from memory by DirectWrite and never
written to disk or redistributed as font files. If a font fails to load, Klock falls back to its built-in digits.
Keep the font files out of public repositories, and ask ITF to confirm the licence covers a numeral picker before
publishing. Library notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Known limitations

* Output is 8-bit sRGB; wide-colour (P3/scRGB) rendering is not implemented.
* Not yet tested on real ARM64 hardware, RDP sessions, OLED panels or a laptop on battery.
* Installer languages are limited to those Inno Setup ships (no Arabic or Chinese); the app itself has all ten.
* Builds are unsigned until a certificate is configured, so SmartScreen may warn on first run.
