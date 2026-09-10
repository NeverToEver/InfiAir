# InfiAir

[![CI](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml/badge.svg)](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml)

**English** | [简体中文](README.md)

A single-player 2D top-down shoot-'em-up (danmaku / bullet hell). Pure endless arcade: no accounts, no leaderboards, no meta progression — boot it up and fly. Player growth is bounded, enemy pressure is not, and the curve always ends in your death. How long can you last?

| | |
| --- | --- |
| Engine | Godot 4.6 .NET — 100% C# (zero GDScript), GL Compatibility, 1920×1080 |
| Platforms | Windows / Linux / macOS, gamepad & touch (virtual sticks) |
| UI languages | 简体中文 / English (switchable in game) |
| License | MIT |

## Highlights

- **Run checkpoint saves** — progress is written automatically when you dock at base or choose "save & exit"; dying or abandoning a run deletes the save — checkpoints can be resumed, but never rolled back after death. "Continue last sortie" on the title screen restores the whole run (score, difficulty, talents, augments, missions); the battlefield restarts from the next wave.
- **Talent cache tree** — milestones and boss kills mint talent points into a cache pool (last in, first out, with overflow decay). Charge open the panel and build freely: 35 nodes across 4 categories, with faction mutex, focus penalty, route contracts and overcharge pulling against each other — no build is a free lunch.
- **Precise bullet-hell feel** — auto-fire with combo scoring (3 s window, up to ×2), graze scoring, and a 360° parry that reflects enemy bullets back at their owners. Your hitbox is a 2.8 px core; grace frames and exit-trajectory settlement guarantee a direct hit always lands and only grazes get forgiven.
- **Boss rotation & encounter events** — 4 bosses with phased patterns and enrage; fail to kill one within 50 s and it flees. Elite turrets, formation strikes and four interference fogs interleave under a strict priority chain — never two at once.
- **Mothership & the Dawn Station** — charge-summon the mothership for fire support and a hangar pod, then return to the Dawn Station to rest: hangar, supply, contracts and mission rotation — and sortie again.
- **Tactical-amber presentation** — deep charcoal-blue base with amber interaction accents and cyan data channels; hand-written screen-space post-processing (bloom / grade / vignette / grain) fills in what GL Compatibility lacks; heavy damage cracks the screen and sinks your heartbeat into it, with a flash-reduction toggle for accessibility.
- **Window & performance options** — windowed / borderless fullscreen; render resolution tiers from 720p up to 4K (filtered to your monitor), free window resizing that remembers custom sizes; 3-step view zoom; a six-tier FPS cap (60–240) plus vsync.
- **The inevitable-death curve** — difficulty climbs without bound with boss kills and time; three difficulty tiers set your score multiplier and pacing; every number lives in a single `data/balance.json`.

## Controls

| Input | Action |
| --- | --- |
| WASD / Arrows | Move (Ctrl fine adjust, Shift boost) |
| Mouse / Right stick | Aim (crosshair is pixel-bound to your cursor) |
| Space | Dash |
| F / LT | Parry (reflect enemy bullets 360°) |
| G (hold to charge) | Talent panel |
| H (hold to charge) | Summon mothership |
| B (hold to charge) | Return to base |
| R | Restart run (pause screen) |
| Esc | Back / Pause |

Title screen: **any key** for a new run · **C** continue last sortie · **T** tutorial. Menus use a left-edge radial dial — rotate with arrows/stick, press to confirm.

## Running

**Prebuilt builds**: grab a package from [Releases](https://github.com/NeverToEver/InfiAir/releases) (Windows zip / Linux tar.gz); the [.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0) is required.

**From source**: you need the [Godot 4.6+ .NET edition](https://godotengine.org/download) (the standard build cannot open this project); building the C# assembly requires the .NET 8 SDK.

- Windows: double-click `run.bat`
- Linux / macOS: `./run.sh` (`--editor` opens the editor)

The launcher scripts auto-detect the engine (`godot-mono` → `godot` → `godot4` → common install locations).

## Development

```
csharp/core/     Pure logic: talent tree, progression curves, balance models (Godot-free)
csharp/godot/    Godot binding layer: scene scripts, UI, services
scenes/          Scenes: main / title / tutorial
data/            balance.json (single source of numbers) · translations.csv (zh/en)
assets/          sprites / audio / fonts / shaders
packaging/       Release packaging assets
scripts/         CI gates and tool scripts
docs/            Design baseline (DESIGN_BASELINE.md) · direction & debt (ROADMAP.md)
```

- Tune gameplay numbers in `data/balance.json` only; every color token lives in `csharp/godot/UITheme.cs`.
- Design intent: [DESIGN_BASELINE](docs/DESIGN_BASELINE.md) · direction / debt / decision log: [ROADMAP](docs/ROADMAP.md) · **pre-commit verification gates: [AGENTS.md](AGENTS.md)**.

## License

[MIT](LICENSE). All code, sprites, audio and shaders are original (procedurally generated); the only third-party asset is the [Noto Sans SC](assets/fonts/NotoSansSC.ttf) font (SIL OFL 1.1) — see [NOTICE](NOTICE).
