# Klock manual QA checklist

Automated coverage (run `dotnet test`): movement maths and spring easing, clock slew/snap, themes and the day/night
fader, pixel-shift path, settings serialisation and migration, updates, embedded fonts (byte-identical) and each numeral face, all ten translations, argument
parsing, exit-input rules, frame pacing, and pixel-level rendering on the WARP software rasteriser.

Everything below needs eyes or hardware. Tick per row: **P** pass, **F** fail (note it), **-** not applicable.

## 1. Platform matrix

| # | Scenario | Steps | Expect | Win10 | Win11 |
|---|---|---|---|---|---|
| 1.1 | Install | Run Setup, accept defaults | `C:\Windows\System32\Klock.scr` exists; Screen Saver Settings opens with Klock selected | | |
| 1.2 | Upgrade | Install v(n), then v(n+1) | One entry in Apps; no duplicate files; settings and language kept | | |
| 1.3 | Uninstall | Uninstall; answer Yes to removing settings | `tools/verify-clean-uninstall.ps1 -IncludeUserData` reports clean | | |
| 1.4 | Uninstall while active | Make Klock the screensaver, uninstall | Screensaver is switched off, no "file not found" dialog | | |
| 1.5 | SmartScreen | Run a *signed* Setup | Publisher shown; no scary warning after reputation builds | | |
| 1.6 | ARM64 | Install the arm64 build on an ARM64 PC | Runs natively (Task Manager shows "Arm64") | | |

## 2. Display matrix

| # | Display | Expect |
|---|---|---|
| 2.1 | 1080p at 100% | Dial is 70% of height, crisp edges and indices |
| 2.2 | 4K at 150% / 200% | Same proportions; no blur; indices stay sharp |
| 2.3 | Ultrawide 21:9 / 32:9 | Dial centred and sized from the *short* edge |
| 2.4 | Portrait monitor | Dial sized from the short (horizontal) edge |
| 2.5 | Two monitors, mixed DPI (100% + 175%) | Both crisp; same time on both; ticks in lock-step |
| 2.6 | 60 Hz + 144 Hz together | Each paces to its own vsync; no tearing, no shared stutter |
| 2.7 | 144 Hz, Quartz/Mechanical | Smooth at full rate; tick overshoot visible and tiny |
| 2.8 | Hot-plug a monitor during `/s` | New window appears; unplug removes it; no crash |
| 2.9 | Change resolution / scale during `/s` | Clock re-lays out immediately |
| 2.10 | OLED, pure black background | Black pixels truly off; no panel glow around the dial |
| 2.11 | HDR enabled in Windows | Colours look normal (not blown out or dim) |

## 3. Screensaver behaviour

| # | Scenario | Expect |
|---|---|---|
| 3.1 | Idle timeout starts `/s` | Starts within a second; cursor hidden |
| 3.2 | Move mouse < 10 px right after start | Keeps running |
| 3.3 | Move mouse > 10 px / click / wheel / key | Exits at once on all monitors; cursor returns |
| 3.4 | Switch to another app (Alt+Tab) | Exits |
| 3.5 | "On resume, display logon screen" ticked | Lock screen appears on exit; Klock does not interfere |
| 3.6 | Display turns off (power plan) | Rendering pauses; GPU idles (check Task Manager) |
| 3.7 | Lock with Win+L | Paused while locked; correct time after unlock |
| 3.8 | Sleep then resume | Correct time immediately, no hands spinning |
| 3.9 | Change the clock / time zone / DST while running | Hands jump to the new time (no slow drift) |
| 3.10 | Start `/s` twice quickly | Second instance exits silently |
| 3.11 | Preview pane (`/p`) | Live clock in the dialog's monitor; process ends when the dialog closes (no orphan in Task Manager) |
| 3.12 | `--preview` | Windowed; Esc exits |
| 3.13 | Remote Desktop session | Renders (software path is acceptable); no black screen; exits on input |
| 3.14 | Laptop: unplug during `/s` | Frame rate drops to the battery cap within about 5 s |
| 3.15 | Kill the GPU driver (Win+Ctrl+Shift+B) during `/s` | Recovers within a second or two |

## 4. Look

| # | Check | Expect |
|---|---|---|
| 4.1 | All 16 dials (Settings > Dial) | Legible hands and indices; preview updates instantly |
| 4.2 | Night theme, all 14 lumes | Hands and indices glow in the chosen colour; no dark blocks past the lume |
| 4.3 | Switch Day <-> Night in Settings | About 1.5 s cross-fade, never a hard cut |
| 4.4 | Windows app mode Light -> Dark while `/s` runs | Fades to night |
| 4.5 | Schedule (custom + solar) | Switches at the set times; wraps midnight correctly |
| 4.6 | Classic <-> Compact | Smooth resize |
| 4.7 | Movement: Quartz / Mechanical / Smooth | Quartz ticks once a second with a tiny overshoot; Mechanical sweeps in 4 beats; Smooth is continuous |
| 4.8 | Tick sound on | Soft, quiet tick on the beat; off by default |
| 4.9 | Burn-in Standard / Extended / Off | Dial drifts imperceptibly (compare screenshots a few minutes apart); highlights breathe |
| 4.10 | Display > per-monitor offset | Clock moves; Reset restores |

## 5. Settings window

| # | Check | Expect |
|---|---|---|
| 5.1 | Change anything | Preview reflects it instantly; reopening the app shows it persisted |
| 5.2 | Keyboard only (Tab / arrows / Space) | Every control reachable, visible focus ring |
| 5.3 | Narrator | Controls announce their names and values |
| 5.4 | Windows high-contrast theme | UI switches to system colours, fully legible |
| 5.5 | Windows "animation effects" off | No UI animations |
| 5.6 | Light and Dark Windows themes | UI follows; Mica on Windows 11 |
| 5.7 | Each of the 10 languages | No truncated or overlapping text; no untranslated English |
| 5.8 | Arabic | Layout mirrored; hex colour and times stay left-to-right and readable |
| 5.9 | Small screen / 200% scale | Window fits the work area; everything scrolls |
| 5.10 | About > Check for updates, offline | Friendly message, no crash |
| 5.11 | Numerals: Panchang / Clash Display / Satoshi / Melodrama / Klock | Preview and `/s` switch instantly; digits stay centred in the dial and clear of the pills; minute numerals stay legible |
| 5.12 | Scrollbar | Slim thumb, swells on hover, accent colour while dragging; works with wheel, keyboard and the dropdown lists |

## 6. Performance (use `Klock.scr --bench --seconds 30`, plus Task Manager for a real `/s`)

| Target | Measured |
|---|---|
| CPU under about 2% at 60 fps | |
| Memory under 150 MB for the whole process | |
| p99 frame time within 10% of the refresh period | |
| Integrated GPU: a single modest workload (Task Manager > GPU engine) | |
| 10 min soak: memory flat, no growth | |

