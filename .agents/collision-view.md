# Collision, Damage & View

## Overview

Collision layers/masks, hit detection, bullet/explosion conventions, camera & viewport math, mouse lock. Applies to combat entities, effects, and anything doing screen-space math.

## Rules

- Layers: 1=`player`, 2=`player_bullet`, 3=`enemy` (incl. boss), 4=`enemy_bullet`. Player bullets resolve vs `enemy` group; enemy bullets/entities vs `player_hitbox` group.
- Player hit only via `Player/Hitbox` Area2D (design r=7 × world_scale → runtime 2.8). Body circle r=22 has no collision use (mask 0) — never use for hit detection.
- Bullets: `scenes/bullet.tscn`, faction in `setup()`; enemy visual scale `effects.enemy_bullet_visual_scale`, player `effects.bullet_visual_scale` (design × world_scale); `Bullet.homing_target` supported (reset in `activate()`). Explosions via `Explosion.spawn_at()`, not ad-hoc particle setups.
- Zoom & window size: independent profile settings. Camera fixed at (960, 540), zoom only; all edge/offscreen/spawn/visibility math via `GameState.view_world_rect()`, never hardcoded 0..1920/0..1080.
- Mouse lock (profile `mouse_lock`, default on): `scripts/mouse_trap.gd` (on Main, `PROCESS_MODE_ALWAYS`) warps mouse inside via `Input.warp_mouse()` while crosshair active (unpaused + cursor hidden) + window focused (`mouse_exited` + per-frame `_process`). Released in non-crosshair states (pause/buff/base/results/cutscene/start) and on focus loss (mouse can leave window, e.g. close via title bar).
