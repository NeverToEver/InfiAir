# InfiAir

[![CI](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml/badge.svg)](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml)
[![Godot 4.6 .NET](https://img.shields.io/badge/Godot-4.6%20.NET-478CBF?logo=godotengine&logoColor=white)](https://godotengine.org/download)
[![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**English** | [简体中文](README.md)

A single-player 2D top-down shoot-'em-up (danmaku / bullet hell). Pure endless arcade: no accounts, no leaderboards, no meta progression — boot it up and fly. Player growth is bounded, enemy pressure is not, and the curve always ends in your death. How long can you last?

![InfiAir title screen](docs/images/title.webp)

| | |
| --- | --- |
| Engine | Godot 4.6 .NET — 100% C# (zero GDScript), GL Compatibility, 1920×1080 |
| Platforms | Windows / Linux prebuilt packages; macOS from source |
| UI languages | 简体中文 / English (switchable in game) |
| Version | 3.34 |
| License | [MIT](LICENSE) |

## Screenshots

| In-run combat | Boss encounter |
| --- | --- |
| ![In-run combat](docs/images/combat.webp) | ![Boss encounter](docs/images/boss.webp) |
| Talent cache tree | Dawn Station · base refit |
| ![Talent cache tree](docs/images/talent.webp) | ![Dawn Station base](docs/images/station.webp) |

## Highlights

- **Run checkpoint saves** — progress is written automatically when you dock at base or choose "save & exit"; dying or abandoning a run deletes the save — checkpoints can be resumed, but never rolled back after death. "Continue last sortie" on the title screen restores the whole run (score, difficulty, talents, augments, missions); the battlefield restarts from the next wave.
- **Talent cache tree** — milestones and boss kills mint talent points into a cache pool (last in, first out, with overflow decay). Charge open the panel and build freely: 27 nodes across 4 categories, with faction mutex, focus penalty, route contracts and overcharge pulling against each other — no build is a free lunch.
- **Precise bullet-hell feel** — manual fire on the left mouse button (hold to fire, or click to toggle) with combo scoring (3 s window, up to ×2), graze scoring, and a 360° parry that reflects enemy bullets back at their owners. Your hitbox is a 2.8 px core; grace frames and exit-trajectory settlement guarantee a direct hit always lands and only grazes get forgiven.
- **Boss rotation & encounter events** — 4 bosses with phased patterns and enrage; fail to kill one within 50 s and it flees. Elite turrets, formation strikes and four interference fogs interleave under a strict priority chain — never two at once.
- **Mothership & the Dawn Station** — charge-summon the mothership for fire support and a hangar pod, then return to the Dawn Station to refit: hangar, repair & resupply, route contracts and mission planning, spending RP on the way back out.
- **Tactical-amber presentation** — warm charcoal base with amber interaction accents; metal, hologram lines and text greys all sit in the same warm family (the old cool cyan is retired); hand-written screen-space post-processing (bloom / grade / vignette / grain) fills in what GL Compatibility lacks; heavy damage cracks the screen and sinks your heartbeat into it, with a flash-reduction toggle for accessibility.
- **Window & performance options** — windowed / borderless fullscreen; five render resolution tiers (720p / 900p / 1080p / 1440p / 4K, filtered to your monitor), free window resizing that remembers custom sizes; 3-step view zoom; a six-tier FPS cap (60 / 120 / 144 / 165 / 180 / 240) plus vsync.
- **The inevitable-death curve** — difficulty climbs without bound with boss kills and time; three difficulty tiers set your score multiplier (×1 / ×2 / ×3) and pacing; every number lives in a single `data/balance.json`.

## Controls

| Input | Action |
| --- | --- |
| WASD / Arrows | Move (Ctrl fine adjust, Shift boost) |
| Mouse / Right stick | Aim (crosshair is pixel-bound to your cursor) |
| Left mouse / RT | Fire (hold to fire; switch to click-to-toggle in Settings) |
| Space | Dash |
| F / LT | Parry (reflect enemy bullets 360°) |
| G (hold to charge) | Talent panel |
| H (hold to charge) | Summon mothership |
| B (hold to charge) | Return to base |
| R | Restart run (pause screen) |
| Esc | Back / Pause |

Title screen: **any key** for a new run · **C** continue last sortie · **T** tutorial. Menus use a left-edge radial dial — rotate with arrows/stick, press to confirm. Gamepad and touch (virtual sticks) are supported.

## Running

### Prebuilt packages

Grab a package from [Releases](https://github.com/NeverToEver/InfiAir/releases) (Windows zip / Linux tar.gz). **The .NET runtime ships inside the package — no separate install needed.** Run the executable directly, or use the bundled installer script:

- Windows: `install.bat` (installs to `%LOCALAPPDATA%\InfiAir` and adds a Start Menu entry; no admin required)
- Linux: `./install.sh` (installs to `~/.local/share/infiair` and writes a `.desktop` entry; no root required)

### From source

You need the [Godot 4.6+ .NET edition](https://godotengine.org/download) (the standard build cannot open this project); building the C# assembly also requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

- Windows: double-click `run.bat`
- Linux: `./run.sh` (`--editor` opens the editor)
- macOS: double-click `run.command` (`chmod +x run.command` first)

The launcher scripts auto-detect the engine (`godot-mono` → `godot` → `godot4` → `~/.local/bin` → macOS `Godot*.app`) and check its version (a warning is printed below 4.6).

### System requirements

- A desktop GPU with OpenGL 3.3 support (GL Compatibility backend) on Windows / Linux / macOS.
- Prebuilt packages bundle the .NET runtime; from source you need Godot 4.6+ .NET edition plus the .NET 8 SDK.

## Saves & settings

Both files live under Godot's `user://` directory and never interfere with each other:

| Platform | Directory |
| --- | --- |
| Windows | `%APPDATA%\Godot\app_userdata\InfiAir\` |
| Linux | `~/.local/share/godot/app_userdata/InfiAir/` |
| macOS | `~/Library/Application Support/Godot/app_userdata/InfiAir/` |

- `run.json` — this run's checkpoint (written when docking at base or on "save & exit"; deleted on death or abandon-and-restart).
- `settings.json` — every setting: language, difficulty, window & resolution, FPS cap, key bindings, accessibility options. Delete it to reset to defaults.
- `logs/` — runtime logs; check here first when a launch misbehaves.

## Troubleshooting

- **Godot standard build won't open the project**: the project contains C#, so the .NET edition is mandatory.
- **Instant crash on launch / black screen**: usually a GPU driver without OpenGL 3.3, or — when running from source — a failed C# build (run `dotnet build` first). See `logs/`.
- **Window ends up off-screen or the size is wrong**: delete `settings.json` to restore defaults, or pick another window resolution tier in the settings page.
- **Want to skip the intro cinematic**: Settings → Operation Mode → skip intro cinematic; any key during playback also skips it.
- **Sensitive to flashing**: the "reduce flashing" toggle in the settings page lowers post-processing intensity.

## Project layout

```
csharp/core/     Pure logic: talent tree, progression curves, balance & save models (Godot-free)
csharp/godot/    Godot binding layer: scene scripts, UI, services
scenes/          Scenes: main / title / tutorial / intro / boss, etc.
data/            balance.json (single source of numbers) · translations.csv (zh/en)
assets/          sprites / audio / fonts / shaders
packaging/       Install / uninstall scripts and desktop entries shipped in releases
scripts/ci/      CI gate scripts
scripts/tools/   Asset generators and balance editor (Python)
docs/            Design baseline · direction & debt · README screenshots
builds/          Export and packaging output (not tracked)
```

## Development

- **Tune gameplay numbers in `data/balance.json` only** (visual editing: `python3 scripts/tools/balance_editor.py`, stdlib-only); **every color token lives in `csharp/godot/UITheme.cs`**.
- **Assets are procedurally generated**: `scripts/tools/regenerate_all.sh` re-runs every generator in a fixed order (needs Python 3 + Pillow); its output should match the committed assets — an empty `git diff` afterwards means it does.
- **Build**: `dotnet build` (warnings are errors, `TreatWarningsAsErrors`).
- **Pre-commit verification gates and commit-message rules live in [AGENTS.md](AGENTS.md)**; CI runs the same gate set as the local workflow.
- **Packaging**: `./release.sh` exports Linux/Windows and packages them into `builds/release/`; `./release.sh --publish` additionally pushes the tag, creates the GitHub Release and uploads the assets.
- Design intent: [DESIGN_BASELINE](docs/DESIGN_BASELINE.md) · direction / debt / decision log: [ROADMAP](docs/ROADMAP.md).

## License

[MIT](LICENSE). All code, sprites, audio and shaders are original (procedurally generated); the only third-party asset is the [Noto Sans SC](assets/fonts/NotoSansSC.ttf) font (SIL OFL 1.1) — see [NOTICE](NOTICE).
