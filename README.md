# Klock

A wordless (numerals only), Bauhaus-inspired **analog clock screensaver for Windows 10 (1809+) and Windows 11**.
GPU-rendered, vector-crisp, with authentic quartz / mechanical / smooth movements, day and night dials with
glowing lume, burn-in protection, and a fully localised settings window.

**The look:** a full-bleed tonal dial (the whole screen is the dial colour, with a fine paper grain), large extended
hour numerals, a minute ring of dashes with small numerals (60, 05, 10...) standing on the radius, embossed pill
indices with an inset groove, and slim pointed hands with a recessed channel and a layered metal centre cap.
Ink is a deep tone of each dial's own hue. Everything, including the digit face, is drawn procedurally.
Prefer black for an OLED? *Settings > Display > Background > Custom colour* puts the dial on a disc over any colour.

> **Klock is a working title.** It lives in one constant (`AppInfo.Name`) plus the installer script. All dial
> artwork, digit shapes, the icon, names and copy are original and generated in code; no image or font assets are used.

```
Klock.scr   the screensaver (/s run, /c configure, /p preview pane)         ~78 MB, self-contained, uncompressed
Klock.Settings.exe   the settings window, with a live preview               ~74 MB, self-contained
Klock-Setup-<version>-<arch>.exe   installer (Inno Setup)
```

---

## Architecture

```
src/
  Klock.Core        net8.0           no UI, no GPU: everything testable
    Time/           MovementEngine (pure f(time) -> hand angles), SmoothClock (QPC phase-locked to wall time)
    Themes/         Rgba, DialTheme + DialCatalog (16 dials), LumeCatalog (14), ThemeResolver, SolarCalc, ThemeFader
    Burnin/         PixelShift (Lissajous drift, highlight breathing, lume cap)
    Settings/       KlockSettings, SettingsStore (HKCU + JSON mirror), FrameRatePolicy, schema migration
    Hosting/        LaunchArgs (/s /c /p parser), ExitInputDetector, SystemTheme
    Updates/        static-manifest update checker
    ClockController turns settings + time into ClockFrame (shared by saver, preview, capture)
  Klock.Renderer    net8.0-windows   Vortice (D3D11 + Direct2D 1.1)
    ClockPainter    procedural dial, cached static layers, live hands/shadows/lume bloom
    ClockView       one render thread per HWND, vsync pacing, device-loss recovery, WARP fallback
    FontLibrary     the numeral typefaces: embedded, unmodified .otf files loaded from memory via DirectWrite
    Glyphs          Klock's own extended monoline digits (built-in face and fallback)
    Native/         hand-written Win32: windows, monitors, power/session notifications
    Offscreen/Png/Gif/Icon writers    headless rendering for --capture, thumbnails and tests
    Audio/          synthesised tick (no audio samples at all)
  Klock.Screensaver  WinExe -> Klock.scr    ClockHost (multi-monitor), PreviewPaneHost, SettingsLauncher, dev commands
  Klock.Settings     WPF               custom-styled UI, Mica, RTL, HwndHost live preview, 10 languages
tests/Klock.Tests    xUnit: maths, movement, clock sync, themes, settings, updates, localisation, fonts,
                     and pixel-level rendering tests on the WARP software rasteriser
assets/fonts/        the four numeral fonts (unmodified; git-ignored, download from Fontshare to build) and their licences; klock.ico lives in assets/
tools/               gen-resx.ps1, strings/*.txt (translation sources), verify-clean-uninstall.ps1
installer/klock.iss  Inno Setup script
build.ps1            tests -> publish x64/arm64 -> (sign) -> installer
```

### Design decisions worth knowing

* **Movement is a pure function of time.** `MovementEngine.Evaluate(type, secondsOfDay)` has no state, so the
  hands are exact by construction: a Quartz tick is a damped-spring step response (peak at about 70 ms, roughly 10% overshoot, settled
  by 150 ms), and half a second after any tick the angle is `6 * second` to within 1e-4 degrees (unit-tested).
  The rendered pixels are tested too: hand axes are located in real frames and compared with the engine's angles.
* **`SmoothClock`** walks a monotonic (QPC) timeline and compares it with the wall clock every 5 s. Noise under 20 ms
  is ignored, small drift is slewed at 2% so it is invisible, and anything over 0.5 s (DST, manual change, resume
  from sleep, time-zone change) snaps. One instance is shared by all monitors so ticks stay in lock-step.
* **Static art is cached.** The face, indices, rim, grain and minute track are rendered once at master size into
  day / night / specular layers (night has its lume glow baked in) and blitted with cubic filtering. Each frame
  draws only the hands, their soft shadows (`Shadow` effect) and the lume bloom (blur + additive composite).
* **One window, one device, one render thread per monitor.** `Present` blocks to that monitor's vsync, so mixed
  refresh rates just work. Frame caps are applied by presenting every Nth refresh (even cadence), not by sleeping.
* **Failure never strands the user.** Unhandled exceptions are logged to `%LOCALAPPDATA%\Klock\logs`, the cursor is
  restored and the process exits. A lost GPU device is rebuilt; no GPU falls back to WARP; a dead renderer ends the saver.
* **The `.scr` is a renamed exe** and is not elevated. `/c` launches `Klock.Settings.exe` from the folder recorded in
  `HKLM\Software\Klock\InstallDir`.

---

## Build, test, run

Requirements: Windows 10/11, .NET 8 SDK. (Installer: Inno Setup 6.3+.)

```powershell
./build.ps1                          # tests, x64 + arm64, installer if Inno Setup is present
./build.ps1 -Arch x64 -SkipTests     # fast x64 build
./build.ps1 -Sign                    # sign with KLOCK_SIGN_PFX / KLOCK_SIGN_PASSWORD (placeholders; no cert ships)
dotnet test                          # 160+ tests, including WARP pixel tests (no GPU needed)
# dev switches: --font panchang|clash|satoshi|melodrama|klock on --capture / --sheet
```

Outputs land in `dist/<arch>/stage` (`Klock.scr`, `Klock.Settings.exe`) and `dist/Klock-Setup-*.exe`.

### Trying it

| Command | What it does |
|---|---|
| `Klock.scr /s` | full-screen on every monitor; exits on key, click, wheel, or mouse movement over 10 px (first 500 ms ignored) |
| `Klock.scr /c` | open settings |
| `Klock.scr /p <HWND>` | render into a preview pane (what the Screen Saver dialog does) |
| `Klock.scr --preview [--fullscreen]` | windowed (or full-screen) demo; **Esc** exits |
| `Klock.scr --capture out.png [--dial ocean] [--night 1] [--time 10:08:35] [--size 1920x1080] [--solid]` | still image (`--solid` = dial on a black disc instead of full-bleed) |
| `Klock.scr --capture out.gif --move quartz --seconds 3 --fps 30` | animated GIF of the real movement |
| `Klock.scr --sheet sheet.png [--night 1]` | contact sheet of all 16 dials |
| `Klock.scr --icon klock.ico` | regenerate the app icon from the renderer |
| `Klock.scr --bench --seconds 8 [--fps 60] [--move mechanical]` | real window, then frame timing / CPU / memory report |
| `Klock.Settings.exe --lang ar` | preview a language without saving it (`--scroll 900` opens scrolled) |

Set `KLOCK_AUTOEXIT_SECONDS=6` to make `/s` exit by itself (for unattended QA scripts).

### Testing the screensaver contract

* **/s** - `$env:KLOCK_AUTOEXIT_SECONDS=10; .\Klock.scr /s`, or install and press *Preview* in Screen Saver Settings.
* **/p** - open *Settings > Personalisation > Lock screen > Screen saver settings*, select Klock: the small monitor shows it live.
* **/c** - press *Settings...* there.
* **Multi-monitor** - plug in a second display, run `/s`: each gets its own window and device. Hot-plug while running:
  windows are added, moved and removed (`WM_DISPLAYCHANGE`). Set *Display > Selected* to blank the others.
* **Mixed DPI / refresh** - each window renders at its own true pixel size and presents at its own vsync.
* **Pause** - turn the display off or lock the PC (`Win+L`): rendering stops; on unlock the clock resyncs.
* **Battery** - unplug: the cap drops to the battery limit (default 30 fps).

---

## Measured on the dev machine

AMD Radeon 780M, 144 Hz panel, 1920x1080 x 2 monitors, Release, Windows 11. `--bench`, 8 s windowed:

| Setup | Frame time (p50 / p99 / max) | CPU (of 16 logical cores) | Memory (working set) |
|---|---|---|---|
| Uncapped (144 Hz) | 6.96 / 7.67 / 8.03 ms | 2.4% | ~100 MB, flat, 0 GCs |
| Cap 60 (presents every 3rd refresh = 48 fps) | 20.9 / 21.8 / 22.0 ms | 1.2% | ~102 MB |

Measured on the published self-contained `Klock.scr` (uncompressed on purpose: a compressed single-file bundle is inflated into RAM at launch and cost ~50 MB). Zero dropped frames; the first 20 frames (cache build) are excluded and measured separately.
Note: a 30 fps cap on a 144 Hz display lands at 36 fps, because DXGI allows a sync interval of at most 4.

---

## Adding a dial

A dial is one row of data in `src/Klock.Core/Themes/DialTheme.cs`:

```csharp
D("ember", "#E9A15B", "#C77C34",   // face: centre -> edge (a soft radial gradient across the whole screen)
           "#4A2A08",              // ink: numerals and minute dashes (a deep tone of the face hue)
           "#FFF9F0",              // hands and pill indices (a pale, tinted white)
           "#4A2A08",              // the dot at the centre of the cap
           dim: 0.86f),            // how far the face fades toward black at night
```

Then add `dial.ember=Ember` to **every** file in `tools/strings/` (the localisation test fails the build otherwise),
run `tools/gen-resx.ps1` (or `build.ps1`), and add it to the contrast test's expectations if needed. Night looks are
derived automatically from `NightDim` and the user's lume colour. Lume colours are one row in `LumeCatalog`.

## Localisation

English, Français, Deutsch, Español, Italiano, 日本語, 简体中文, Русский, Türkçe and العربية
(full RTL). Sources are `tools/strings/<culture>.txt` (`key=value`); `tools/gen-resx.ps1` produces the `.resx`
files. A test checks that every language has every key with matching `{0}` placeholders. The clock itself has no words.

## Numeral fonts

*Settings > Numerals* chooses the typeface for the hour **and** minute numbers: **Panchang** (Extrabold, the default),
**Clash Display** (Semibold), **Satoshi** (Black), **Melodrama** (Bold), or **Klock**, the built-in monoline digits. Each face is sized
separately for the hour numerals and the small minute ring so the weights match.

The four fonts come from Fontshare under the **ITF Free Font License 2.0** (`assets/fonts/*-FFL.txt`):

* They are embedded **byte-for-byte unmodified** (a test compares them with the originals) and loaded from memory by
  DirectWrite. They are never subsetted, converted or written to disk, because the licence forbids it.
* If a font fails to load (or DirectWrite font sets are unavailable) the clock falls back to the built-in digits.
* Embedding in a desktop application is permitted (section 01). Section 02 also forbids offering a font as a
  *selectable font for third-party users to create or customise their own content*. This switch only changes
  fixed digits and users cannot type anything, so it should be fine, but **ask ITF to confirm before you publish
  the app**. Also keep `assets/fonts/` out of any public repository (redistribution is not allowed).
* See `THIRD-PARTY-NOTICES.md`.

## Updates

One GET of a static JSON manifest (`{ "version", "url", "notes" }`, https only). No telemetry; nothing about the
user or machine is sent. Set `AppInfo.UpdateManifestUrl` and `SupportUrl`.

## Installer

`installer/klock.iss`: installs `Klock.scr` to System32, the settings app to Program Files, records `InstallDir`,
and opens Screen Saver Settings with Klock pre-selected (it never switches your screensaver silently). Upgrades
remove the previous files first; older settings schemas are reset on load while the language and update-check
timestamp survive. Uninstall removes everything, switches the screensaver off if it was Klock, and asks before
deleting your settings. After uninstalling, `tools/verify-clean-uninstall.ps1 [-IncludeUserData]` checks for leftovers.

## Known limitations / honest notes

* **Wide-colour (P3/scRGB) rendering is not implemented.** Output is 8-bit sRGB, which is safe on HDR displays
  (Windows maps SDR content), but it is not wide-gamut.
* The installer was written but **not compiled in the dev environment** (Inno Setup was not installed); build it with `build.ps1`.
* Installer UI languages are limited to those Inno Setup ships (no Arabic or Chinese); the app itself has all ten.
* Not yet exercised on real hardware: ARM64 execution, RDP sessions, OLED panels, and a laptop on battery. See
  `docs/QA-CHECKLIST.md`.
* Signing is a hook only: no certificate is included.


