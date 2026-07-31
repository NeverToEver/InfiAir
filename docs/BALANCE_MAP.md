# BALANCE_MAP — 数值位置地图

> 本文件由 `python3 scripts/tools/gen_balance_map.py` 扫描生成，请勿手改；
> 新增/改名数值键或调整 cfg() 调用后重新运行生成器。

## 怎么改数值

- 运行时数值的唯一来源是 `data/balance.json`；推荐用 `python3 scripts/tools/balance_editor.py` 在浏览器里编辑（改动高亮、类型校验、自动备份）。
- 脚本侧的 `GameState.cfg("键路径", 回退值)` 仅在 json 缺键/损坏时兜底；新增或调整数值按 AGENTS.md 约定保持 json 与回退值一致。
- 高频 `_process` 路径的数值在 `_ready()`/`_load_balance()` 一次缓存，不要每帧查。

## 静态 cfg() 调用点（按文件分组）

### `scripts/boss.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 310 | `boss.hp_base` | `HP_BASE` |
| 311 | `boss.hp_mults` | `[1.3, 0.7, 1.6]` |
| 342 | `boss.enter_speed` | `ENTER_SPEED` |
| 343 | `boss.fight_y` | `FIGHT_Y` |
| 344 | `boss.strafe_min_x` | `STRAFE_MIN_X` |
| 345 | `boss.strafe_max_x` | `STRAFE_MAX_X` |
| 346 | `boss.phase2_hp_ratio` | `PHASE2_HP_RATIO` |
| 347 | `boss.enrage.hp_ratio` | `ENRAGE_HP_RATIO` |
| 348 | `boss.enrage.rate_mult` | `ENRAGE_RATE_MULT` |
| 349 | `boss.enrage.speed_mult` | `ENRAGE_SPEED_MULT` |
| 350 | `boss.enrage.player_slow` | `ENRAGE_PLAYER_SLOW` |
| 351 | `boss.enrage.snapshot_lasers` | `ENRAGE_SNAPSHOT_LASERS` |
| 352 | `boss.enrage.snapshot_ring` | `ENRAGE_SNAPSHOT_RING` |
| 353 | `boss.enrage.laser_speed` | `ENRAGE_LASER_SPEED` |
| 354 | `boss.enrage.ring_speed` | `ENRAGE_RING_SPEED` |
| 355 | `boss.enrage.duration` | `ENRAGE_DURATION` |
| 356 | `boss.enrage.transition_duration` | `ENRAGE_TRANSITION_DURATION` |
| 357 | `boss.enrage.attack_interval` | `ENRAGE_ATTACK_INTERVAL` |
| 358 | `boss.enrage.attack_windup` | `ENRAGE_ATTACK_WINDUP` |
| 359 | `boss.enrage.release_interval` | `ENRAGE_RELEASE_INTERVAL` |
| 360 | `boss.enrage.release_hold_duration` | `ENRAGE_RELEASE_HOLD_DURATION` |
| 361 | `boss.enrage.return_duration` | `ENRAGE_RETURN_DURATION` |
| 362 | `boss.enrage.path_radius_scale` | `ENRAGE_PATH_RADIUS_SCALE` |
| 363 | `boss.enrage.square_path_ratio` | `ENRAGE_SQUARE_PATH_RATIO` |
| 364 | `boss.enrage.release_laser_speed` | `ENRAGE_RELEASE_LASER_SPEED` |
| 365 | `boss.enrage.release_ring_speed` | `ENRAGE_RELEASE_RING_SPEED` |
| 367 | `boss.escape.time` | `ESCAPE_TIME` |
| 368 | `boss.escape.warning` | `ESCAPE_WARNING` |
| 369 | `boss.escape.drift` | `ESCAPE_DRIFT` |
| 370 | `boss.escape.start_speed` | `ESCAPE_START_SPEED` |
| 371 | `boss.escape.accel` | `ESCAPE_ACCEL` |
| 372 | `boss.escape.countdown_visible_from` | `ESCAPE_COUNTDOWN_FROM` |
| 373 | `boss.hp_base` | `HP_BASE` |
| 374 | `boss.strafe_speeds` | `STRAFE_SPEEDS` |
| 375 | `boss.fire_intervals` | `FIRE_INTERVALS` |
| 376 | `boss.fan_bullet_speed` | `FAN_BULLET_SPEED` |
| 377 | `boss.homing_bullet_speed` | `HOMING_BULLET_SPEED` |
| 378 | `boss.sniper_bullet_speed` | `SNIPER_BULLET_SPEED` |
| 379 | `boss.cross_bullet_speed` | `CROSS_BULLET_SPEED` |
| 380 | `boss.collision_damage` | `COLLISION_DAMAGE` |
| 381 | `buffs.slow_field.factor` | `SLOW_FIELD_FACTOR` |
| 382 | `boss.bullet_damage.fan` | `BULLET_DAMAGE_FAN` |
| 383 | `boss.bullet_damage.homing` | `BULLET_DAMAGE_HOMING` |
| 384 | `boss.bullet_damage.sniper` | `BULLET_DAMAGE_SNIPER` |
| 385 | `boss.bullet_damage.cross` | `BULLET_DAMAGE_CROSS` |
| 386 | `boss.bullet_damage.snapshot_laser` | `BULLET_DAMAGE_SNAPSHOT_LASER` |
| 387 | `boss.bullet_damage.snapshot_ring` | `BULLET_DAMAGE_SNAPSHOT_RING` |
| 388 | `boss.phases.phase_shift_duration` | `PHASE_SHIFT_DURATION` |
| 389 | `boss.phases.telegraph.sniper_aim` | `SNIPER_AIM_TIME` |
| 390 | `boss.phases.telegraph.sniper_track` | `SNIPER_TRACK_TIME` |
| 391 | `boss.phases.press_interval` | `PRESS_INTERVAL` |
| 392 | `boss.phases.press_depth` | `PRESS_DEPTH` |
| 394 | `boss.phases.attacks.charged_cannon.charge` | `CANNON_CHARGE` |
| 395 | `boss.phases.attacks.charged_cannon.shots` | `CANNON_SHOTS` |
| 396 | `boss.phases.attacks.charged_cannon.interval` | `CANNON_INTERVAL` |
| 397 | `boss.phases.attacks.charged_cannon.bullet_speed` | `CANNON_BULLET_SPEED` |
| 398 | `boss.phases.attacks.charged_cannon.damage` | `CANNON_DAMAGE` |
| 399 | `boss.phases.attacks.charged_cannon.flash` | `CANNON_FLASH` |
| 400 | `boss.phases.attacks.dash_sweep.aim` | `SWEEP_AIM` |
| 401 | `boss.phases.attacks.dash_sweep.speed` | `SWEEP_SPEED` |
| 402 | `boss.phases.attacks.dash_sweep.drop_count` | `SWEEP_DROP_COUNT` |
| 403 | `boss.phases.attacks.dash_sweep.drop_speed` | `SWEEP_DROP_SPEED` |
| 404 | `boss.phases.attacks.dash_sweep.drop_damage` | `SWEEP_DROP_DAMAGE` |
| 405 | `boss.phases.attacks.dash_sweep.return_duration` | `SWEEP_RETURN_DURATION` |
| 406 | `boss.phases.attacks.minion_volley.count` | `VOLLEY_COUNT` |
| 407 | `boss.phases.attacks.minion_volley.delay` | `VOLLEY_DELAY` |
| 408 | `boss.phases.attacks.minion_volley.bullet_speed` | `VOLLEY_BULLET_SPEED` |
| 409 | `boss.phases.attacks.minion_volley.bullet_damage` | `VOLLEY_BULLET_DAMAGE` |
| 410 | `boss.phases.attacks.bullet_wall.count` | `WALL_COUNT` |
| 411 | `boss.phases.attacks.bullet_wall.bullet_speed` | `WALL_BULLET_SPEED` |
| 412 | `boss.phases.attacks.bullet_wall.damage` | `WALL_DAMAGE` |
| 413 | `boss.phases.attacks.bullet_wall.arc_deg` | `WALL_ARC_DEG` |
| 415 | `boss.enrage.type_1.ring_interval` | `E1_RING_INTERVAL` |
| 416 | `boss.enrage.type_1.ring_count` | `E1_RING_COUNT` |
| 417 | `boss.enrage.type_1.ring_speed` | `E1_RING_SPEED` |
| 418 | `boss.enrage.type_1.ring_precession_deg` | `E1_RING_PRECESSION_DEG` |
| 419 | `boss.enrage.type_1.salvo_charge` | `E1_SALVO_CHARGE` |
| 420 | `boss.enrage.type_1.salvo_count` | `E1_SALVO_COUNT` |
| 421 | `boss.enrage.type_1.salvo_speed` | `E1_SALVO_SPEED` |
| 422 | `boss.enrage.type_1.salvo_damage` | `E1_SALVO_DAMAGE` |
| 423 | `boss.enrage.type_2.point_count` | `E2_POINT_COUNT` |
| 424 | `boss.enrage.type_2.point_interval` | `E2_POINT_INTERVAL` |
| 425 | `boss.enrage.type_2.aim` | `E2_AIM` |
| 426 | `boss.enrage.type_2.sniper_speed` | `E2_SNIPER_SPEED` |
| 427 | `boss.enrage.type_2.sniper_damage` | `E2_SNIPER_DAMAGE` |
| 428 | `boss.enrage.type_2.release_ring_count` | `E2_RELEASE_RING_COUNT` |
| 429 | `boss.enrage.type_2.release_ring_speed` | `E2_RELEASE_RING_SPEED` |
| 430 | `boss.enrage.type_3.summon_interval` | `E3_SUMMON_INTERVAL` |
| 431 | `boss.enrage.type_3.summon_waves` | `E3_SUMMON_WAVES` |
| 432 | `boss.enrage.type_3.summon_count` | `E3_SUMMON_COUNT` |
| 433 | `boss.enrage.type_3.ring_interval` | `E3_RING_INTERVAL` |
| 434 | `boss.enrage.type_3.ring_count` | `E3_RING_COUNT` |
| 435 | `boss.enrage.type_3.ring_speed` | `E3_RING_SPEED` |
| 436 | `boss.enrage.type_3.release_ring_count` | `E3_RELEASE_RING_COUNT` |
| 437 | `boss.enrage.type_3.release_ring_speed` | `E3_RELEASE_RING_SPEED` |
| 439 | `boss.difficulty_scaling.interval_mult` | `DIFF_INTERVAL_MULT` |
| 440 | `boss.difficulty_scaling.speed_mult` | `DIFF_SPEED_MULT` |
| 441 | `boss.difficulty_scaling.counts` | `DIFF_COUNT_DELTAS` |
| 747 | `effects.shake.enrage` | `16.0` |
| 1454 | `effects.shake.enrage` | `16.0` |

### `scripts/buff_select.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 129 | `buffs.explosive.unlock_boss_kills` | `3` |
| 280 | `buffs.extra_life.heal_on_pick` | `30` |

### `scripts/bullet.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 125 | `buffs.explosive.radius_per_level` | `EXPLOSIVE_RADIUS` |
| 126 | `buffs.explosive.damage_per_level` | `EXPLOSIVE_DAMAGE` |
| 127 | `effects.bullet_visual_scale` | `VISUAL_SCALE` |
| 128 | `effects.enemy_bullet_visual_scale` | `ENEMY_VISUAL_SCALE` |

### `scripts/camera_shake.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 12 | `effects.shake.decay` | `DECAY` |

### `scripts/elite_turret_event.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 51 | `elite_turret_event.duration` | `DURATION` |
| 52 | `elite_turret_event.enter_time` | `ENTER_TIME` |
| 53 | `elite_turret_event.rise_time` | `RISE_TIME` |
| 54 | `elite_turret_event.boss_resume_delay` | `BOSS_RESUME_DELAY` |
| 55 | `elite_turret_event.turret_hp_base` | `TURRET_HP_BASE` |
| 56 | `elite_turret_event.turret_counts` | `TURRET_COUNTS` |
| 57 | `elite_turret_event.fire_interval` | `[FIRE_INTERVAL.x, FIRE_INTERVAL.y]` |
| 59 | `elite_turret_event.weak_lock` | `WEAK_LOCK` |
| 60 | `elite_turret_event.ammo_sequences` | `AMMO_SEQUENCES` |
| 61 | `elite_turret_event.reward_score` | `REWARD_SCORE` |
| 62 | `elite_turret_event.carrier.hover_y` | `HOVER_Y` |
| 63 | `elite_turret_event.cooldown` | `COOLDOWN` |
| 101 | `elite_turret_event.carrier.shake` | `4.0` |

### `scripts/enemy.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 132 | `enemies.hp_ramp_factor` | `HP_RAMP_FACTOR` |
| 150 | `player.aim_assist.mark_ratio` | `0.25` |
| 178 | `enemies.bullet_speed` | `ENEMY_BULLET_SPEED` |
| 179 | `enemies.spread_bullet_speed` | `SPREAD_BULLET_SPEED` |
| 180 | `enemies.laser_bullet_speed` | `LASER_BULLET_SPEED` |
| 181 | `enemies.bullet_damage.single` | `BULLET_DAMAGE_SINGLE` |
| 182 | `enemies.bullet_damage.spread` | `BULLET_DAMAGE_SPREAD` |
| 183 | `enemies.bullet_damage.laser` | `BULLET_DAMAGE_LASER` |
| 184 | `enemies.collision_damage` | `COLLISION_DAMAGE` |
| 185 | `buffs.slow_field.factor` | `SLOW_FIELD_FACTOR` |
| 186 | `enemies.spread_fan_step` | `SPREAD_FAN_STEP` |
| 187 | `enemies.lifetime` | `LIFETIME` |
| 188 | `enemies.exit_accel` | `EXIT_ACCEL` |
| 189 | `enemies.aggressive_chase_speed` | `AGGR_CHASE_SPEED` |
| 190 | `enemies.fire_interval` | `FIRE_INTERVAL` |
| 191 | `enemies.hover_band` | `[HOVER_BAND.x, HOVER_BAND.y]` |
| 193 | `enemies.hover_bob_amp` | `HOVER_BOB_AMP` |
| 194 | `enemies.hover_bob_freq` | `HOVER_BOB_FREQ` |
| 195 | `enemies.hover_sway_amp` | `HOVER_SWAY_AMP` |
| 196 | `enemies.hover_sway_freq` | `HOVER_SWAY_FREQ` |
| 197 | `enemies.spiral_drift_amp` | `SPIRAL_DRIFT_AMP` |
| 198 | `enemies.spiral_drift_freq` | `SPIRAL_DRIFT_FREQ` |
| 199 | `enemies.spiral_radius` | `SPIRAL_RADIUS` |
| 544 | `effects.shake.elite_die` | `9.0` |
| 544 | `effects.shake.enemy_die` | `5.0` |

### `scripts/explosion.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 18 | `effects.explosion.pool_cap` | `POOL_CAP` |
| 24 | `effects.explosion_visual_scale` | `1.6` |
| 42 | `effects.shake.boss_seq_initial` | `20.0` |
| 55 | `effects.shake.boss_seq_step` | `8.0` |
| 67 | `effects.shake.boss_seq_final` | `24.0` |
| 72 | `effects.explosion.amount` | `24` |
| 92 | `effects.explosion.debris_amount` | `10` |

### `scripts/formation_bomb.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 85 | `effects.shake.enemy_die` | `5.0` |

### `scripts/formation_craft.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 60 | `effects.shake.enemy_die` | `5.0` |

### `scripts/formation_strike_event.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 61 | `formation_strike_event.min_score` | `MIN_SCORE` |
| 62 | `formation_strike_event.cooldown` | `COOLDOWN` |
| 63 | `formation_strike_event.craft_counts` | `CRAFT_COUNTS` |
| 64 | `formation_strike_event.craft_hp_base` | `CRAFT_HP_BASE` |
| 65 | `formation_strike_event.craft_score` | `CRAFT_SCORE` |
| 66 | `formation_strike_event.approach_speed` | `APPROACH_SPEED` |
| 67 | `formation_strike_event.approach_y` | `APPROACH_Y` |
| 68 | `formation_strike_event.turn_time` | `TURN_TIME` |
| 69 | `formation_strike_event.run_speed` | `RUN_SPEED` |
| 70 | `formation_strike_event.bomb_interval` | `BOMB_INTERVAL` |
| 71 | `formation_strike_event.bombs_per_craft` | `BOMBS_PER_CRAFT` |
| 72 | `formation_strike_event.bomb_fall_speed` | `BOMB_FALL_SPEED` |
| 73 | `formation_strike_event.bomb_fuse` | `BOMB_FUSE` |
| 74 | `formation_strike_event.bomb_damage` | `BOMB_DAMAGE` |
| 75 | `formation_strike_event.bomb_radius` | `BOMB_RADIUS` |
| 76 | `formation_strike_event.reward_all_clear` | `REWARD_ALL_CLEAR` |

### `scripts/hud.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 91 | `effects.hud_poll_interval` | `POLL_INTERVAL` |
| 92 | `effects.hit_flash.alpha` | `HIT_FLASH_ALPHA` |
| 93 | `effects.hit_flash.time` | `HIT_FLASH_TIME` |
| 94 | `effects.low_hp.ratio` | `LOW_HP_RATIO` |
| 95 | `effects.low_hp.pulse_min` | `LOW_HP_PULSE_MIN` |
| 96 | `effects.low_hp.pulse_max` | `LOW_HP_PULSE_MAX` |
| 97 | `effects.low_hp.pulse_period` | `LOW_HP_PULSE_PERIOD` |

### `scripts/laser_weapon.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 30 | `buffs.laser_beam.duration` | `BEAM_DURATION` |
| 31 | `buffs.laser_beam.cooldown` | `COOLDOWN` |
| 32 | `buffs.laser_beam.tick_interval` | `TICK_INTERVAL` |
| 33 | `buffs.laser_beam.tick_damage` | `TICK_DAMAGE` |
| 34 | `buffs.laser_beam.length` | `BEAM_LENGTH` |
| 35 | `buffs.laser_beam.half_width` | `BEAM_HALF_WIDTH` |
| 36 | `buffs.laser_beam.hit_radius` | `ENEMY_HIT_RADIUS` |

### `scripts/main.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 68 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 69 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 70 | `effects.give_up_hold_time` | `GIVE_UP_HOLD_TIME` |
| 71 | `boss.enrage.slow_scale` | `ENRAGE_SLOW_SCALE` |
| 72 | `boss.enrage.bullet_time` | `ENRAGE_BULLET_TIME` |
| 73 | `boss.enrage.ramp_time` | `ENRAGE_RAMP_TIME` |
| 466 | `effects.mothership_summon.shake_gate` | `6.0` |
| 476 | `buffs.mothership_recall.cooldown_factor` | `0.5` |
| 504 | `mothership.depart_cooldown` | `60.0` |

### `scripts/meta_health_fx.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 100 | `effects.meta_health.lod` | `0` |
| 101 | `effects.meta_health.pulse.scale` | `2.5` |
| 102 | `effects.meta_health.pulse.min` | `0.15` |
| 103 | `effects.meta_health.pulse.decay_tau` | `0.09` |
| 104 | `effects.meta_health.chromatic.base` | `0.006` |
| 105 | `effects.meta_health.chromatic.peak` | `0.014` |
| 106 | `effects.meta_health.blur.strength` | `0.6` |
| 107 | `effects.meta_health.ripple.duration` | `0.4` |
| 108 | `effects.meta_health.ripple.alpha` | `0.8` |
| 109 | `effects.meta_health.crack.exponent` | `1.6` |
| 110 | `effects.meta_health.crack.spread_min` | `0.10` |
| 111 | `effects.meta_health.crack.edge_softness` | `0.08` |
| 112 | `effects.meta_health.crack.width` | `0.03` |
| 113 | `effects.meta_health.crack.glow` | `0.8` |
| 114 | `effects.meta_health.crack.heal_jitter` | `0.35` |
| 115 | `effects.meta_health.crack.grow_overshoot` | `0.08` |
| 116 | `effects.meta_health.crack.grow_time` | `0.6` |
| 117 | `effects.meta_health.crack.density` | `DENSITY_CAPS.duplicate(` |
| 118 | `effects.meta_health.desat.max` | `0.35` |
| 119 | `effects.meta_health.desat.exponent` | `2.0` |
| 120 | `effects.meta_health.vignette.max_alpha` | `0.5` |
| 121 | `effects.meta_health.vignette.inner` | `0.62` |
| 122 | `effects.meta_health.vignette.dying_shrink` | `0.06` |
| 123 | `effects.meta_health.dying.threshold` | `0.2` |
| 124 | `effects.meta_health.dying.heart_min_hz` | `1.0` |
| 125 | `effects.meta_health.dying.heart_max_hz` | `1.2` |
| 126 | `effects.meta_health.dying.breath` | `0.015` |
| 127 | `effects.meta_health.dying.jitter_px` | `2.0` |
| 128 | `effects.meta_health.dying.warn_hz` | `2.5` |
| 129 | `effects.meta_health.dying.fade` | `0.3` |
| 130 | `effects.meta_health.smooth.down_tau` | `0.10` |
| 131 | `effects.meta_health.smooth.up_tau` | `0.80` |
| 132 | `effects.meta_health.adapt.interval` | `0.25` |
| 133 | `effects.meta_health.adapt.min` | `0.8` |
| 134 | `effects.meta_health.adapt.max` | `1.3` |
| 135 | `effects.meta_health.adapt.bullet_weight` | `0.002` |
| 136 | `effects.meta_health.adapt.explosion_weight` | `0.15` |
| 137 | `effects.meta_health.reduce_flash.chromatic_scale` | `0.4` |

### `scripts/mothership.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 114 | `mothership.hover_y` | `HOVER_Y` |
| 115 | `mothership.release_invincible` | `RELEASE_INVINCIBLE` |
| 116 | `mothership.dock_tween_time` | `DOCK_TWEEN_TIME` |
| 117 | `mothership.dock_offset_y` | `DOCK_OFFSET_Y` |
| 118 | `mothership.resupply_delay` | `RESUPPLY_DELAY` |
| 119 | `mothership.release_time` | `RELEASE_TIME` |
| 120 | `mothership.release_drop` | `RELEASE_DROP` |
| 121 | `mothership.mag_cells` | `MAG_CELLS` |
| 122 | `mothership.mag_cell_time` | `MAG_CELL_TIME` |
| 123 | `mothership.mag_warn_cells` | `MAG_WARN_CELLS` |
| 124 | `mothership.warn_eject_delay` | `WARN_EJECT_DELAY` |
| 125 | `mothership.early_hold_time` | `EARLY_HOLD_TIME` |
| 126 | `mothership.early_max_discount` | `EARLY_MAX_DISCOUNT` |
| 127 | `mothership.early_prefill_max` | `EARLY_PREFILL_MAX` |
| 128 | `mothership.early_prefill_ratio` | `EARLY_PREFILL_RATIO` |
| 129 | `mothership.depart_cooldown` | `DEPART_COOLDOWN` |
| 130 | `mothership.depart_start_speed` | `DEPART_START_SPEED` |
| 131 | `mothership.depart_accel` | `DEPART_ACCEL` |
| 132 | `mothership.drive.accel` | `DRIVE_ACCEL` |
| 133 | `mothership.drive.max_speed` | `DRIVE_MAX_SPEED` |
| 134 | `mothership.drive.margin_x` | `DRIVE_MARGIN_X` |
| 135 | `mothership.drive.margin_top` | `DRIVE_MARGIN_TOP` |
| 136 | `mothership.drive.margin_bottom` | `DRIVE_MARGIN_BOTTOM` |
| 137 | `mothership.gatling.interval` | `GATLING_INTERVAL` |
| 138 | `mothership.gatling.bullet_speed` | `GATLING_BULLET_SPEED` |
| 139 | `mothership.gatling.damage` | `GATLING_DAMAGE` |
| 140 | `mothership.gatling.score_scale` | `GATLING_SCORE_SCALE` |
| 141 | `mothership.gatling.sweep_left_min` | `GATLING_SWEEP_LEFT_MIN` |
| 142 | `mothership.gatling.sweep_left_max` | `GATLING_SWEEP_LEFT_MAX` |
| 143 | `mothership.gatling.sweep_right_min` | `GATLING_SWEEP_RIGHT_MIN` |
| 144 | `mothership.gatling.sweep_right_max` | `GATLING_SWEEP_RIGHT_MAX` |
| 145 | `mothership.gatling.sweep_left_period` | `GATLING_SWEEP_LEFT_PERIOD` |
| 146 | `mothership.gatling.sweep_right_period` | `GATLING_SWEEP_RIGHT_PERIOD` |
| 147 | `mothership.gatling.sweep_right_phase` | `GATLING_SWEEP_RIGHT_PHASE` |
| 148 | `mothership.missile.interval` | `MISSILE_INTERVAL` |
| 149 | `mothership.missile.damage` | `MISSILE_DAMAGE` |
| 150 | `mothership.missile.speed` | `MISSILE_SPEED` |
| 151 | `mothership.missile.target_count` | `MISSILE_TARGET_COUNT` |
| 152 | `mothership.missile.splash_damage` | `MISSILE_SPLASH_DAMAGE` |
| 153 | `mothership.missile.splash_radius` | `MISSILE_SPLASH_RADIUS` |
| 154 | `effects.mothership_summon.warp_in_time` | `WARP_IN_TIME` |
| 155 | `effects.mothership_summon.warp_in_drop` | `WARP_IN_DROP` |
| 156 | `effects.mothership_summon.slow.radius` | `SLOW_RADIUS` |
| 157 | `effects.mothership_summon.slow.duration` | `SLOW_DURATION` |
| 158 | `effects.mothership_summon.slow.factor` | `SLOW_FACTOR` |
| 159 | `effects.mothership_summon.slow.ring_time` | `SLOW_RING_TIME` |
| 160 | `effects.mothership_summon.shake_slow` | `SHAKE_SLOW` |
| 255 | `effects.mothership_summon.warp_in_drop` | `WARP_IN_DROP` |
| 580 | `effects.shake.mothership` | `4.0` |

### `scripts/mothership_summon_window.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 48 | `effects.mothership_summon.window.open_time` | `OPEN_TIME` |
| 49 | `effects.mothership_summon.window.close_time` | `CLOSE_TIME` |
| 50 | `effects.mothership_summon.window.shot_durations` | `_shot_durations` |

### `scripts/orbital_strike.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 46 | `effects.orbital_strike.duration` | `DURATION` |
| 47 | `effects.orbital_strike.impact_at` | `IMPACT_AT` |
| 48 | `effects.orbital_strike.missile_from` | `MISSILE_FROM` |
| 49 | `effects.orbital_strike.reticle_radius` | `RETICLE_RADIUS` |
| 50 | `effects.orbital_strike.impact_y_ratio` | `IMPACT_Y_RATIO` |
| 70 | `effects.shake.boss_seq_final` | `24.0` |

### `scripts/player.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 108 | `player.max_speed` | `MAX_SPEED` |
| 109 | `player.accel` | `ACCEL` |
| 110 | `player.decel` | `DECEL` |
| 111 | `player.boost_mult` | `BOOST_MULT` |
| 112 | `player.fine_move_mult` | `FINE_MOVE_MULT` |
| 113 | `player.base_fire_interval` | `BASE_FIRE_INTERVAL` |
| 114 | `player.bullet_speed` | `BULLET_SPEED` |
| 115 | `player.bullet_spread_deg` | `BULLET_SPREAD_DEG` |
| 116 | `player.bullet_damage` | `BULLET_DAMAGE` |
| 117 | `player.invincible_time` | `INVINCIBLE_TIME` |
| 118 | `player.spawn_invincible_time` | `SPAWN_INVINCIBLE_TIME` |
| 119 | `player.bullet_clear_radius` | `BULLET_CLEAR_RADIUS` |
| 120 | `buffs.armor.multiplier` | `ARMOR_MULT` |
| 121 | `buffs.evasion.chance` | `EVASION_CHANCE` |
| 122 | `buffs.regen.heal_per_sec` | `REGEN_PER_SEC` |
| 123 | `effects.shake.player_hit` | `SHAKE_HIT` |
| 125 | `player.fuel.max` | `fuel_max` |
| 127 | `player.fuel.drain` | `FUEL_DRAIN` |
| 128 | `player.fuel.regen` | `FUEL_REGEN` |
| 129 | `player.fuel.restart` | `FUEL_RESTART` |
| 130 | `player.dash.distance` | `DASH_DISTANCE` |
| 131 | `player.dash.time` | `DASH_TIME` |
| 132 | `player.dash.cooldown` | `DASH_COOLDOWN` |
| 133 | `player.dash.fuel_ratio` | `DASH_FUEL_RATIO` |
| 134 | `player.dash.afterimage_interval` | `AFTERIMAGE_INTERVAL` |
| 236 | `buffs.rapid_fire.factor` | `0.75` |
| 241 | `buffs.power_shot.factor` | `1.25` |
| 259 | `player.dash.cooldown_stack_factor` | `0.8` |
| 273 | `buffs.efficient_boost.factor` | `0.75` |
| 278 | `buffs.boost_recovery.factor` | `1.5` |
| 426 | `player.aim_assist.homing_time` | `HOMING_TIME` |
| 465 | `buffs.spread_shot.max_stacks` | `3` |
| 466 | `buffs.piercing.max_stacks` | `2` |

### `scripts/return_cinematic.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 42 | `effects.return_skip_grace` | `SKIP_GRACE` |

### `scripts/spawner.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 133 | `spawner.wave_interval_start` | `WAVE_INTERVAL_START` |
| 134 | `spawner.wave_interval_end` | `WAVE_INTERVAL_END` |
| 135 | `spawner.ramp_time` | `RAMP_TIME` |
| 136 | `spawner.boss_score_step` | `BOSS_SCORE_STEP` |
| 137 | `spawner.boss_min_interval` | `BOSS_MIN_INTERVAL` |
| 138 | `spawner.boss_time_limit` | `BOSS_TIME_LIMIT` |
| 139 | `spawner.difficulty_factor` | `DIFFICULTY_FACTOR` |
| 140 | `spawner.interval_min` | `INTERVAL_MIN` |
| 141 | `spawner.unlock_scores` | `UNLOCK_SCORES` |
| 142 | `spawner.wave_size_start` | `WAVE_SIZE_START` |
| 143 | `spawner.wave_size_end` | `WAVE_SIZE_END` |
| 144 | `spawner.special_gap_min` | `SPECIAL_GAP_MIN` |
| 145 | `spawner.special_gap_max` | `SPECIAL_GAP_MAX` |
| 146 | `spawner.rest_waves_after_kill` | `REST_WAVES_AFTER_KILL` |
| 147 | `spawner.elite_wave_size` | `ELITE_WAVE_SIZE` |
| 148 | `enemies.hover_band` | `[_hover_band.x, _hover_band.y]` |
| 150 | `elite_turret_event.min_score` | `ETV_MIN_SCORE` |
| 151 | `elite_turret_event.trigger_interval` | `ETV_TRIGGER_INTERVAL` |
| 152 | `elite_turret_event.trigger_chance` | `ETV_TRIGGER_CHANCE` |
| 153 | `formation_strike_event.trigger_interval` | `FS_TRIGGER_INTERVAL` |
| 154 | `formation_strike_event.trigger_chance` | `FS_TRIGGER_CHANCE` |
| 155 | `enemies.types` | `[]` |
| 158 | `elites.types` | `[]` |
| 361 | `spawner.telegraph_duration` | `SpawnTelegraph.DURATION` |
| 399 | `effects.shake.boss_warning` | `14.0` |

### `scripts/starfield.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 22 | `effects.starfield.far_count` | `FAR_COUNT` |
| 23 | `effects.starfield.near_count` | `NEAR_COUNT` |
| 24 | `effects.starfield.far_speed` | `FAR_SPEED` |
| 25 | `effects.starfield.near_speed` | `NEAR_SPEED` |

### `scripts/strike_carrier.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 48 | `elite_turret_event.carrier.retreat_start_speed` | `RETREAT_START_SPEED` |
| 49 | `elite_turret_event.carrier.retreat_accel` | `RETREAT_ACCEL` |

### `scripts/turret_battery.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 65 | `enemies.bullet_speed` | `SINGLE_SPEED` |
| 66 | `enemies.spread_bullet_speed` | `SPREAD_SPEED` |
| 67 | `enemies.laser_bullet_speed` | `LASER_SPEED` |
| 68 | `boss.homing_bullet_speed` | `HOMING_SPEED` |
| 69 | `boss.sniper_bullet_speed` | `SNIPER_SPEED` |
| 70 | `enemies.spread_fan_step` | `SPREAD_FAN_STEP` |
| 71 | `enemies.bullet_damage.single` | `DMG_SINGLE` |
| 72 | `enemies.bullet_damage.spread` | `DMG_SPREAD` |
| 73 | `enemies.bullet_damage.laser` | `DMG_LASER` |
| 74 | `boss.bullet_damage.homing` | `DMG_HOMING` |
| 75 | `boss.bullet_damage.sniper` | `DMG_SNIPER` |
| 204 | `effects.shake.enemy_die` | `5.0` |

### `scripts/tutorial.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 57 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 58 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 128 | `tutorial.boss_hp` | `120.0` |
| 199 | `mothership.hover_y` | `270.0` |

### `scripts/warp_gate.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 34 | `effects.mothership_summon.gate.open_time` | `OPEN_TIME` |
| 35 | `effects.mothership_summon.gate.close_time` | `CLOSE_TIME` |
| 36 | `effects.mothership_summon.gate.radius` | `RADIUS` |

### `autoload/game_state.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 90 | `world_scale` | `world_scale` |
| 91 | `milestones.base` | `MILESTONE_BASE.duplicate(` |
| 92 | `milestones.cycle_mult` | `MILESTONE_CYCLE_MULT` |
| 93 | `progression.per_boss_kill` | `0.5` |
| 94 | `progression.per_ten_minutes` | `1.0` |
| 95 | `progression.time_step_seconds` | `30.0` |
| 96 | `difficulty` | `{}` |
| 559 | `player.max_health` | `100.0` |
| 559 | `buffs.extra_life.max_hp_bonus` | `50` |
| 586 | `buffs.lifesteal.max_hp_fraction` | `0.1` |

## 动态拼接键前缀

- `boss.phases.type…`
- `buffs.…`
- `player.aim_assist.levels.…`

## json 中存在但脚本未静态引用的键

（经动态键或整段读取覆盖的不列出；剩下的请人工判断是否为死键）

- `enemies.damage_ramp_factor`
- `version`

## 脚本引用但 json 缺失的键（走回退值，建议补进 json 或确认为有意兜底）

