# Collision, Damage & View

## Overview

Collision layers/masks, hit detection, bullet/explosion conventions, camera & viewport math, mouse lock. Applies to combat entities, effects, and anything doing screen-space math.

## Rules

- Layers: 1=`player`, 2=`player_bullet`, 3=`enemy` (incl. boss), 4=`enemy_bullet`. Player bullets resolve vs `enemy` group; enemy bullets/entities vs `player_hitbox` group.
- Player hit only via `Player/Hitbox` Area2D (design r=7 × world_scale → runtime 2.8). Body circle r=22 has no collision use (mask 0) — never use for hit detection.
- Ramming: enemy = event-driven `AreaEntered`/`AreaExited` overlap flags + O(1) guard re-roll while overlapping (P0-2; no per-frame `overlaps_area` polling); boss = `_bodyContact` contact flag + deliberate per-frame check (phase-gated, `csharp/godot/Boss.cs` `CheckBodyCollision()`). Pre-enrage boss HP floored at 30% (`Boss.cs` `EnrageHpRatio`, cfg `boss.enrage.hp_ratio`).
- Bullets: `scenes/bullet.tscn`, faction in `Setup()`; enemy visual scale `effects.enemy_bullet_visual_scale`, player `effects.bullet_visual_scale` (design × world_scale); `Bullet.HomingTarget` supported (reset in `Activate()`). Explosions via `Explosion.SpawnAt()`, not ad-hoc particle setups.
- Zoom & window size: independent profile settings. Camera fixed at (960, 540), zoom only; all edge/offscreen/spawn/visibility math via `GameState.ViewWorldRect()`, never hardcoded 0..1920/0..1080.
- Mouse lock (profile `mouse_lock`, default on): `csharp/godot/MouseTrap.cs` (on Main, `ProcessModeEnum.Always`) warps mouse inside via `Input.WarpMouse()` while crosshair active (unpaused + cursor hidden) + window focused (`MouseExited` + per-frame `_Process`). Released in non-crosshair states (pause/buff/base/results/cutscene/start) and on focus loss (mouse can leave window, e.g. close via title bar).
