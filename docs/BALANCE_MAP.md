# BALANCE_MAP — 数值位置地图

> 本文件由 `python3 scripts/tools/gen_balance_map.py` 扫描生成，请勿手改；
> 新增/改名数值键或调整 cfg() 调用后重新运行生成器。

## 怎么改数值

- 运行时数值的唯一来源是 `data/balance.json`；推荐用 `python3 scripts/tools/balance_editor.py` 在浏览器里编辑（改动高亮、类型校验、自动备份）。
- 脚本侧的 `GameState.cfg("键路径", 回退值)` 仅在 json 缺键/损坏时兜底；新增或调整数值按 AGENTS.md 约定保持 json 与回退值一致。
- 高频 `_process` 路径的数值在 `_ready()`/`_load_balance()` 一次缓存，不要每帧查。

## 静态 cfg() 调用点（按文件分组）

### `scripts/aim_frame_layer.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 33 | `player.aim_assist.input.magnet_input_min` | `_magnet_input_min` |
| 34 | `player.aim_assist.input.magnet_input_full` | `_magnet_input_full` |
| 35 | `player.aim_assist.falloff.peak` | `_falloff_peak` |
| 36 | `player.aim_assist.falloff.end` | `_falloff_end` |
| 37 | `player.aim_assist.falloff.min` | `_falloff_min` |

### `scripts/balance_service.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 50 | `enemies.hp_ramp_factor` | `0.12` |
| 57 | `enemies.damage_ramp_factor` | `0.08` |

### `scripts/boss.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 265 | `boss.hp_base` | `HP_BASE` |
| 266 | `boss.hp_mults` | `[1.3, 0.7, 1.6]` |
| 400 | `boss.enter_speed` | `ENTER_SPEED` |
| 401 | `boss.fight_y` | `FIGHT_Y` |
| 402 | `boss.strafe_min_x` | `STRAFE_MIN_X` |
| 403 | `boss.strafe_max_x` | `STRAFE_MAX_X` |
| 404 | `boss.phase2_hp_ratio` | `PHASE2_HP_RATIO` |
| 405 | `boss.enrage.hp_ratio` | `ENRAGE_HP_RATIO` |
| 406 | `boss.enrage.rate_mult` | `ENRAGE_RATE_MULT` |
| 407 | `boss.enrage.speed_mult` | `ENRAGE_SPEED_MULT` |
| 408 | `boss.enrage.player_slow` | `ENRAGE_PLAYER_SLOW` |
| 409 | `boss.enrage.snapshot_lasers` | `ENRAGE_SNAPSHOT_LASERS` |
| 410 | `boss.enrage.snapshot_ring` | `ENRAGE_SNAPSHOT_RING` |
| 411 | `boss.enrage.laser_speed` | `ENRAGE_LASER_SPEED` |
| 412 | `boss.enrage.ring_speed` | `ENRAGE_RING_SPEED` |
| 413 | `boss.enrage.duration` | `ENRAGE_DURATION` |
| 414 | `boss.enrage.transition_duration` | `ENRAGE_TRANSITION_DURATION` |
| 415 | `boss.enrage.attack_interval` | `ENRAGE_ATTACK_INTERVAL` |
| 416 | `boss.enrage.attack_windup` | `ENRAGE_ATTACK_WINDUP` |
| 417 | `boss.enrage.release_interval` | `ENRAGE_RELEASE_INTERVAL` |
| 418 | `boss.enrage.release_hold_duration` | `ENRAGE_RELEASE_HOLD_DURATION` |
| 419 | `boss.enrage.return_duration` | `ENRAGE_RETURN_DURATION` |
| 420 | `boss.enrage.path_radius_scale` | `ENRAGE_PATH_RADIUS_SCALE` |
| 421 | `boss.enrage.square_path_ratio` | `ENRAGE_SQUARE_PATH_RATIO` |
| 422 | `boss.enrage.release_laser_speed` | `ENRAGE_RELEASE_LASER_SPEED` |
| 423 | `boss.enrage.release_ring_speed` | `ENRAGE_RELEASE_RING_SPEED` |
| 425 | `boss.escape.time` | `ESCAPE_TIME` |
| 426 | `boss.escape.warning` | `ESCAPE_WARNING` |
| 427 | `boss.escape.drift` | `ESCAPE_DRIFT` |
| 428 | `boss.escape.start_speed` | `ESCAPE_START_SPEED` |
| 429 | `boss.escape.accel` | `ESCAPE_ACCEL` |
| 430 | `boss.escape.countdown_visible_from` | `ESCAPE_COUNTDOWN_FROM` |
| 431 | `boss.hp_base` | `HP_BASE` |
| 433 | `boss.strafe_speeds` | `STRAFE_SPEEDS` |
| 442 | `boss.fire_intervals` | `FIRE_INTERVALS` |
| 443 | `boss.fan_bullet_speed` | `FAN_BULLET_SPEED` |
| 444 | `boss.homing_bullet_speed` | `HOMING_BULLET_SPEED` |
| 445 | `boss.sniper_bullet_speed` | `SNIPER_BULLET_SPEED` |
| 446 | `boss.cross_bullet_speed` | `CROSS_BULLET_SPEED` |
| 447 | `boss.collision_damage` | `COLLISION_DAMAGE` |
| 448 | `buffs.slow_field.factor` | `SLOW_FIELD_FACTOR` |
| 449 | `boss.bullet_damage.fan` | `BULLET_DAMAGE_FAN` |
| 450 | `boss.bullet_damage.homing` | `BULLET_DAMAGE_HOMING` |
| 451 | `boss.bullet_damage.sniper` | `BULLET_DAMAGE_SNIPER` |
| 452 | `boss.bullet_damage.cross` | `BULLET_DAMAGE_CROSS` |
| 453 | `boss.bullet_damage.snapshot_laser` | `BULLET_DAMAGE_SNAPSHOT_LASER` |
| 454 | `boss.bullet_damage.snapshot_ring` | `BULLET_DAMAGE_SNAPSHOT_RING` |
| 455 | `boss.phases.phase_shift_duration` | `PHASE_SHIFT_DURATION` |
| 456 | `boss.phases.telegraph.sniper_aim` | `SNIPER_AIM_TIME` |
| 457 | `boss.phases.telegraph.sniper_track` | `SNIPER_TRACK_TIME` |
| 458 | `boss.phases.press_interval` | `PRESS_INTERVAL` |
| 459 | `boss.phases.press_depth` | `PRESS_DEPTH` |
| 460 | `boss.movement.type1_p2_strafe` | `TYPE1_P2_STRAFE` |
| 461 | `boss.movement.type1_p2_bob_amp` | `TYPE1_P2_BOB_AMP` |
| 462 | `boss.movement.type1_p2_bob_period` | `TYPE1_P2_BOB_PERIOD` |
| 463 | `boss.movement.type2_p2_dash_time` | `TYPE2_P2_DASH_TIME` |
| 464 | `boss.movement.type2_p2_rest_time` | `TYPE2_P2_REST_TIME` |
| 465 | `boss.movement.type3_p1_bob_min` | `TYPE3_P1_BOB_MIN` |
| 466 | `boss.movement.type3_p1_bob_max` | `TYPE3_P1_BOB_MAX` |
| 467 | `boss.movement.type3_p1_bob_period` | `TYPE3_P1_BOB_PERIOD` |
| 468 | `boss.movement.type3_p2_strafe` | `TYPE3_P2_STRAFE` |
| 469 | `boss.movement.type3_p2_bob_amp` | `TYPE3_P2_BOB_AMP` |
| 470 | `boss.movement.type3_p2_bob_period` | `TYPE3_P2_BOB_PERIOD` |
| 472 | `boss.phases.attacks.charged_cannon.charge` | `CANNON_CHARGE` |
| 473 | `boss.phases.attacks.charged_cannon.shots` | `CANNON_SHOTS` |
| 474 | `boss.phases.attacks.charged_cannon.interval` | `CANNON_INTERVAL` |
| 475 | `boss.phases.attacks.charged_cannon.bullet_speed` | `CANNON_BULLET_SPEED` |
| 476 | `boss.phases.attacks.charged_cannon.damage` | `CANNON_DAMAGE` |
| 477 | `boss.phases.attacks.charged_cannon.flash` | `CANNON_FLASH` |
| 478 | `boss.phases.attacks.dash_sweep.aim` | `SWEEP_AIM` |
| 479 | `boss.phases.attacks.dash_sweep.speed` | `SWEEP_SPEED` |
| 480 | `boss.phases.attacks.dash_sweep.drop_count` | `SWEEP_DROP_COUNT` |
| 481 | `boss.phases.attacks.dash_sweep.drop_speed` | `SWEEP_DROP_SPEED` |
| 482 | `boss.phases.attacks.dash_sweep.drop_damage` | `SWEEP_DROP_DAMAGE` |
| 483 | `boss.phases.attacks.dash_sweep.return_duration` | `SWEEP_RETURN_DURATION` |
| 484 | `boss.phases.attacks.minion_volley.count` | `VOLLEY_COUNT` |
| 485 | `boss.phases.attacks.minion_volley.delay` | `VOLLEY_DELAY` |
| 486 | `boss.phases.attacks.minion_volley.bullet_speed` | `VOLLEY_BULLET_SPEED` |
| 487 | `boss.phases.attacks.minion_volley.bullet_damage` | `VOLLEY_BULLET_DAMAGE` |
| 488 | `boss.phases.attacks.bullet_wall.count` | `WALL_COUNT` |
| 489 | `boss.phases.attacks.bullet_wall.bullet_speed` | `WALL_BULLET_SPEED` |
| 490 | `boss.phases.attacks.bullet_wall.damage` | `WALL_DAMAGE` |
| 491 | `boss.phases.attacks.bullet_wall.arc_deg` | `WALL_ARC_DEG` |
| 493 | `boss.enrage.type_1.ring_interval` | `E1_RING_INTERVAL` |
| 494 | `boss.enrage.type_1.ring_count` | `E1_RING_COUNT` |
| 495 | `boss.enrage.type_1.ring_speed` | `E1_RING_SPEED` |
| 496 | `boss.enrage.type_1.ring_precession_deg` | `E1_RING_PRECESSION_DEG` |
| 497 | `boss.enrage.type_1.salvo_charge` | `E1_SALVO_CHARGE` |
| 498 | `boss.enrage.type_1.salvo_count` | `E1_SALVO_COUNT` |
| 499 | `boss.enrage.type_1.salvo_speed` | `E1_SALVO_SPEED` |
| 500 | `boss.enrage.type_1.salvo_damage` | `E1_SALVO_DAMAGE` |
| 501 | `boss.enrage.type_2.point_count` | `E2_POINT_COUNT` |
| 502 | `boss.enrage.type_2.point_interval` | `E2_POINT_INTERVAL` |
| 503 | `boss.enrage.type_2.aim` | `E2_AIM` |
| 504 | `boss.enrage.type_2.sniper_speed` | `E2_SNIPER_SPEED` |
| 505 | `boss.enrage.type_2.sniper_damage` | `E2_SNIPER_DAMAGE` |
| 506 | `boss.enrage.type_2.release_ring_count` | `E2_RELEASE_RING_COUNT` |
| 507 | `boss.enrage.type_2.release_ring_speed` | `E2_RELEASE_RING_SPEED` |
| 508 | `boss.enrage.type_3.summon_interval` | `E3_SUMMON_INTERVAL` |
| 509 | `boss.enrage.type_3.summon_waves` | `E3_SUMMON_WAVES` |
| 510 | `boss.enrage.type_3.summon_count` | `E3_SUMMON_COUNT` |
| 511 | `boss.enrage.type_3.ring_interval` | `E3_RING_INTERVAL` |
| 512 | `boss.enrage.type_3.ring_count` | `E3_RING_COUNT` |
| 513 | `boss.enrage.type_3.ring_speed` | `E3_RING_SPEED` |
| 514 | `boss.enrage.type_3.release_ring_count` | `E3_RELEASE_RING_COUNT` |
| 515 | `boss.enrage.type_3.release_ring_speed` | `E3_RELEASE_RING_SPEED` |
| 517 | `boss.difficulty_scaling.interval_mult` | `DIFF_INTERVAL_MULT` |
| 518 | `boss.difficulty_scaling.speed_mult` | `DIFF_SPEED_MULT` |
| 519 | `boss.difficulty_scaling.counts` | `DIFF_COUNT_DELTAS` |
| 748 | `effects.shake.enrage` | `16.0` |
| 880 | `effects.shake.enrage` | `16.0` |

### `scripts/buff_select.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 65 | `buffs.explosive.unlock_boss_kills` | `3` |
| 227 | `buffs.extra_life.heal_on_pick` | `30` |
| 260 | `buffs.extra_life.heal_on_pick` | `30` |

### `scripts/bullet.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 130 | `buffs.explosive.radius_per_level` | `EXPLOSIVE_RADIUS` |
| 131 | `buffs.explosive.damage_per_level` | `EXPLOSIVE_DAMAGE` |
| 132 | `effects.bullet_visual_scale` | `VISUAL_SCALE` |
| 133 | `effects.enemy_bullet_visual_scale` | `ENEMY_VISUAL_SCALE` |

### `scripts/camera_shake.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 14 | `effects.shake.decay` | `DECAY` |

### `scripts/elite_turret_event.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 95 | `elite_turret_event.duration` | `DURATION` |
| 96 | `elite_turret_event.enter_time` | `ENTER_TIME` |
| 97 | `elite_turret_event.rise_time` | `RISE_TIME` |
| 98 | `elite_turret_event.boss_resume_delay` | `BOSS_RESUME_DELAY` |
| 99 | `elite_turret_event.turret_hp_base` | `TURRET_HP_BASE` |
| 100 | `elite_turret_event.turret_counts` | `TURRET_COUNTS` |
| 101 | `elite_turret_event.fire_interval` | `[FIRE_INTERVAL.x, FIRE_INTERVAL.y]` |
| 103 | `elite_turret_event.weak_lock` | `WEAK_LOCK` |
| 104 | `elite_turret_event.ammo_sequences` | `AMMO_SEQUENCES` |
| 105 | `elite_turret_event.reward_score` | `REWARD_SCORE` |
| 106 | `elite_turret_event.carrier.hover_y` | `HOVER_Y` |
| 107 | `elite_turret_event.cooldown` | `COOLDOWN` |
| 145 | `elite_turret_event.carrier.shake` | `4.0` |

### `scripts/enemy.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 132 | `enemies.hp_ramp_factor` | `HP_RAMP_FACTOR` |
| 142 | `enemies.speed_ramp_factor` | `SPEED_RAMP_FACTOR` |
| 150 | `player.aim_assist.mark_ratio` | `0.25` |
| 204 | `enemies.bullet_speed` | `ENEMY_BULLET_SPEED` |
| 205 | `enemies.spread_bullet_speed` | `SPREAD_BULLET_SPEED` |
| 206 | `enemies.laser_bullet_speed` | `LASER_BULLET_SPEED` |
| 207 | `enemies.bullet_damage.single` | `BULLET_DAMAGE_SINGLE` |
| 208 | `enemies.bullet_damage.spread` | `BULLET_DAMAGE_SPREAD` |
| 209 | `enemies.bullet_damage.laser` | `BULLET_DAMAGE_LASER` |
| 210 | `enemies.collision_damage` | `COLLISION_DAMAGE` |
| 211 | `buffs.slow_field.factor` | `SLOW_FIELD_FACTOR` |
| 212 | `enemies.spread_fan_step` | `SPREAD_FAN_STEP` |
| 213 | `enemies.lifetime` | `LIFETIME` |
| 214 | `enemies.exit_accel` | `EXIT_ACCEL` |
| 215 | `enemies.aggressive_chase_speed` | `AGGR_CHASE_SPEED` |
| 216 | `enemies.fire_interval` | `FIRE_INTERVAL` |
| 217 | `enemies.hover_band` | `[HOVER_BAND.x, HOVER_BAND.y]` |
| 219 | `enemies.hover_bob_amp` | `HOVER_BOB_AMP` |
| 220 | `enemies.hover_bob_freq` | `HOVER_BOB_FREQ` |
| 221 | `enemies.hover_sway_amp` | `HOVER_SWAY_AMP` |
| 222 | `enemies.hover_sway_freq` | `HOVER_SWAY_FREQ` |
| 223 | `enemies.spiral_drift_amp` | `SPIRAL_DRIFT_AMP` |
| 224 | `enemies.spiral_drift_freq` | `SPIRAL_DRIFT_FREQ` |
| 225 | `enemies.spiral_radius` | `SPIRAL_RADIUS` |
| 501 | `effects.shake.elite_die` | `9.0` |
| 501 | `effects.shake.enemy_die` | `5.0` |

### `scripts/explosion.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 18 | `effects.explosion.pool_cap` | `POOL_CAP` |
| 24 | `effects.explosion_visual_scale` | `1.6` |
| 42 | `effects.shake.boss_seq_initial` | `20.0` |
| 55 | `effects.shake.boss_seq_step` | `8.0` |
| 67 | `effects.shake.boss_seq_final` | `24.0` |
| 75 | `effects.explosion.amount` | `24` |
| 95 | `effects.explosion.debris_amount` | `10` |

### `scripts/formation_bomb.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 90 | `effects.shake.enemy_die` | `5.0` |

### `scripts/formation_craft.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 60 | `effects.shake.enemy_die` | `5.0` |

### `scripts/formation_strike_event.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 62 | `formation_strike_event.min_score` | `MIN_SCORE` |
| 63 | `formation_strike_event.cooldown` | `COOLDOWN` |
| 64 | `formation_strike_event.craft_counts` | `CRAFT_COUNTS` |
| 65 | `formation_strike_event.craft_hp_base` | `CRAFT_HP_BASE` |
| 66 | `formation_strike_event.craft_score` | `CRAFT_SCORE` |
| 67 | `formation_strike_event.approach_speed` | `APPROACH_SPEED` |
| 68 | `formation_strike_event.approach_y` | `APPROACH_Y` |
| 69 | `formation_strike_event.turn_time` | `TURN_TIME` |
| 70 | `formation_strike_event.run_speed` | `RUN_SPEED` |
| 71 | `formation_strike_event.bomb_interval` | `BOMB_INTERVAL` |
| 72 | `formation_strike_event.bombs_per_craft` | `BOMBS_PER_CRAFT` |
| 73 | `formation_strike_event.bomb_fall_speed` | `BOMB_FALL_SPEED` |
| 74 | `formation_strike_event.bomb_fuse` | `BOMB_FUSE` |
| 75 | `formation_strike_event.bomb_damage` | `BOMB_DAMAGE` |
| 76 | `formation_strike_event.bomb_radius` | `BOMB_RADIUS` |
| 77 | `formation_strike_event.reward_all_clear` | `REWARD_ALL_CLEAR` |

### `scripts/hud.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 93 | `effects.hud_poll_interval` | `POLL_INTERVAL` |
| 94 | `effects.hit_flash.alpha` | `HIT_FLASH_ALPHA` |
| 95 | `effects.hit_flash.time` | `HIT_FLASH_TIME` |
| 96 | `effects.low_hp.ratio` | `LOW_HP_RATIO` |
| 97 | `effects.low_hp.pulse_min` | `LOW_HP_PULSE_MIN` |
| 98 | `effects.low_hp.pulse_max` | `LOW_HP_PULSE_MAX` |
| 99 | `effects.low_hp.pulse_period` | `LOW_HP_PULSE_PERIOD` |

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
| 69 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 70 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 71 | `effects.give_up_hold_time` | `GIVE_UP_HOLD_TIME` |
| 72 | `boss.enrage.slow_scale` | `ENRAGE_SLOW_SCALE` |
| 73 | `boss.enrage.bullet_time` | `ENRAGE_BULLET_TIME` |
| 74 | `boss.enrage.ramp_time` | `ENRAGE_RAMP_TIME` |
| 621 | `effects.mothership_summon.shake_gate` | `6.0` |
| 631 | `buffs.mothership_recall.cooldown_factor` | `0.5` |
| 666 | `mothership.depart_cooldown` | `60.0` |

### `scripts/meta_health_fx.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 151 | `effects.meta_health.lod` | `0` |
| 152 | `effects.meta_health.pulse.scale` | `2.5` |
| 153 | `effects.meta_health.pulse.min` | `0.15` |
| 154 | `effects.meta_health.pulse.decay_tau` | `0.09` |
| 155 | `effects.meta_health.chromatic.base` | `0.006` |
| 156 | `effects.meta_health.chromatic.peak` | `0.014` |
| 157 | `effects.meta_health.blur.strength` | `0.6` |
| 158 | `effects.meta_health.ripple.duration` | `0.4` |
| 159 | `effects.meta_health.ripple.alpha` | `0.8` |
| 160 | `effects.meta_health.crack.exponent` | `1.6` |
| 161 | `effects.meta_health.crack.spread_min` | `0.10` |
| 162 | `effects.meta_health.crack.edge_softness` | `0.08` |
| 163 | `effects.meta_health.crack.width` | `0.03` |
| 164 | `effects.meta_health.crack.glow` | `0.8` |
| 165 | `effects.meta_health.crack.heal_jitter` | `0.35` |
| 166 | `effects.meta_health.crack.grow_overshoot` | `0.08` |
| 167 | `effects.meta_health.crack.grow_time` | `0.6` |
| 168 | `effects.meta_health.crack.density` | `DENSITY_CAPS.duplicate(` |
| 169 | `effects.meta_health.desat.max` | `0.35` |
| 170 | `effects.meta_health.desat.exponent` | `2.0` |
| 171 | `effects.meta_health.vignette.max_alpha` | `0.5` |
| 172 | `effects.meta_health.vignette.inner` | `0.62` |
| 173 | `effects.meta_health.vignette.dying_shrink` | `0.06` |
| 174 | `effects.meta_health.dying.threshold` | `0.2` |
| 175 | `effects.meta_health.dying.heart_min_hz` | `1.0` |
| 176 | `effects.meta_health.dying.heart_max_hz` | `1.2` |
| 177 | `effects.meta_health.dying.breath` | `0.015` |
| 178 | `effects.meta_health.dying.jitter_px` | `2.0` |
| 179 | `effects.meta_health.dying.warn_hz` | `2.5` |
| 180 | `effects.meta_health.dying.fade` | `0.3` |
| 181 | `effects.meta_health.smooth.down_tau` | `0.10` |
| 182 | `effects.meta_health.smooth.up_tau` | `0.80` |
| 183 | `effects.meta_health.adapt.interval` | `0.25` |
| 184 | `effects.meta_health.adapt.min` | `0.8` |
| 185 | `effects.meta_health.adapt.max` | `1.3` |
| 186 | `effects.meta_health.adapt.bullet_weight` | `0.002` |
| 187 | `effects.meta_health.adapt.explosion_weight` | `0.15` |
| 188 | `effects.meta_health.reduce_flash.chromatic_scale` | `0.4` |

### `scripts/mothership.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 116 | `mothership.hover_y` | `HOVER_Y` |
| 117 | `mothership.release_invincible` | `RELEASE_INVINCIBLE` |
| 118 | `mothership.dock_tween_time` | `DOCK_TWEEN_TIME` |
| 119 | `mothership.dock_offset_y` | `DOCK_OFFSET_Y` |
| 120 | `mothership.resupply_delay` | `RESUPPLY_DELAY` |
| 121 | `mothership.release_time` | `RELEASE_TIME` |
| 122 | `mothership.release_drop` | `RELEASE_DROP` |
| 123 | `mothership.mag_cells` | `MAG_CELLS` |
| 124 | `mothership.mag_cell_time` | `MAG_CELL_TIME` |
| 125 | `mothership.mag_warn_cells` | `MAG_WARN_CELLS` |
| 126 | `mothership.warn_eject_delay` | `WARN_EJECT_DELAY` |
| 127 | `mothership.early_hold_time` | `EARLY_HOLD_TIME` |
| 128 | `mothership.early_max_discount` | `EARLY_MAX_DISCOUNT` |
| 129 | `mothership.early_prefill_max` | `EARLY_PREFILL_MAX` |
| 130 | `mothership.early_prefill_ratio` | `EARLY_PREFILL_RATIO` |
| 131 | `mothership.depart_cooldown` | `DEPART_COOLDOWN` |
| 132 | `mothership.depart_start_speed` | `DEPART_START_SPEED` |
| 133 | `mothership.depart_accel` | `DEPART_ACCEL` |
| 134 | `mothership.drive.accel` | `DRIVE_ACCEL` |
| 135 | `mothership.drive.max_speed` | `DRIVE_MAX_SPEED` |
| 139 | `mothership.drive.margin_x` | `DRIVE_MARGIN_X` |
| 140 | `mothership.drive.margin_top` | `DRIVE_MARGIN_TOP` |
| 141 | `mothership.drive.margin_bottom` | `DRIVE_MARGIN_BOTTOM` |
| 142 | `mothership.gatling.interval` | `GATLING_INTERVAL` |
| 143 | `mothership.gatling.bullet_speed` | `GATLING_BULLET_SPEED` |
| 144 | `mothership.gatling.damage` | `GATLING_DAMAGE` |
| 145 | `mothership.gatling.score_scale` | `GATLING_SCORE_SCALE` |
| 146 | `mothership.gatling.sweep_left_min` | `GATLING_SWEEP_LEFT_MIN` |
| 147 | `mothership.gatling.sweep_left_max` | `GATLING_SWEEP_LEFT_MAX` |
| 148 | `mothership.gatling.sweep_right_min` | `GATLING_SWEEP_RIGHT_MIN` |
| 149 | `mothership.gatling.sweep_right_max` | `GATLING_SWEEP_RIGHT_MAX` |
| 150 | `mothership.gatling.sweep_left_period` | `GATLING_SWEEP_LEFT_PERIOD` |
| 151 | `mothership.gatling.sweep_right_period` | `GATLING_SWEEP_RIGHT_PERIOD` |
| 152 | `mothership.gatling.sweep_right_phase` | `GATLING_SWEEP_RIGHT_PHASE` |
| 153 | `mothership.missile.interval` | `MISSILE_INTERVAL` |
| 154 | `mothership.missile.damage` | `MISSILE_DAMAGE` |
| 155 | `mothership.missile.speed` | `MISSILE_SPEED` |
| 156 | `mothership.missile.target_count` | `MISSILE_TARGET_COUNT` |
| 157 | `mothership.missile.splash_damage` | `MISSILE_SPLASH_DAMAGE` |
| 158 | `mothership.missile.splash_radius` | `MISSILE_SPLASH_RADIUS` |
| 159 | `effects.mothership_summon.warp_in_time` | `WARP_IN_TIME` |
| 160 | `effects.mothership_summon.warp_in_drop` | `WARP_IN_DROP` |
| 161 | `effects.mothership_summon.slow.radius` | `SLOW_RADIUS` |
| 162 | `effects.mothership_summon.slow.duration` | `SLOW_DURATION` |
| 163 | `effects.mothership_summon.slow.factor` | `SLOW_FACTOR` |
| 164 | `effects.mothership_summon.slow.ring_time` | `SLOW_RING_TIME` |
| 165 | `effects.mothership_summon.shake_slow` | `SHAKE_SLOW` |
| 262 | `effects.mothership_summon.warp_in_drop` | `WARP_IN_DROP` |
| 621 | `effects.shake.mothership` | `4.0` |

### `scripts/mothership_summon_window.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 48 | `effects.mothership_summon.window.open_time` | `OPEN_TIME` |
| 49 | `effects.mothership_summon.window.close_time` | `CLOSE_TIME` |
| 50 | `effects.mothership_summon.window.shot_durations` | `_shot_durations` |

### `scripts/orbital_strike.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 47 | `effects.orbital_strike.duration` | `DURATION` |
| 48 | `effects.orbital_strike.impact_at` | `IMPACT_AT` |
| 49 | `effects.orbital_strike.missile_from` | `MISSILE_FROM` |
| 50 | `effects.orbital_strike.reticle_radius` | `RETICLE_RADIUS` |
| 51 | `effects.orbital_strike.impact_y_ratio` | `IMPACT_Y_RATIO` |
| 71 | `effects.shake.boss_seq_final` | `24.0` |

### `scripts/player.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 186 | `player.max_speed` | `MAX_SPEED` |
| 187 | `player.accel` | `ACCEL` |
| 188 | `player.decel` | `DECEL` |
| 189 | `player.boost_mult` | `BOOST_MULT` |
| 190 | `player.fine_move_mult` | `FINE_MOVE_MULT` |
| 191 | `player.base_fire_interval` | `BASE_FIRE_INTERVAL` |
| 192 | `player.bullet_speed` | `BULLET_SPEED` |
| 193 | `player.bullet_spread_deg` | `BULLET_SPREAD_DEG` |
| 194 | `player.bullet_damage` | `BULLET_DAMAGE` |
| 195 | `player.invincible_time` | `INVINCIBLE_TIME` |
| 196 | `player.spawn_invincible_time` | `SPAWN_INVINCIBLE_TIME` |
| 197 | `player.bullet_clear_radius` | `BULLET_CLEAR_RADIUS` |
| 198 | `player.entry.land_ratio` | `ENTRY_LAND_RATIO` |
| 199 | `player.entry.rush_time` | `ENTRY_RUSH_TIME` |
| 200 | `player.entry.retreat_speed` | `ENTRY_RETREAT_SPEED` |
| 201 | `player.entry.retreat_time` | `ENTRY_RETREAT_TIME` |
| 202 | `player.entry.invincible` | `ENTRY_INVINCIBLE` |
| 203 | `player.entry.spawn_clearance` | `ENTRY_SPAWN_CLEARANCE` |
| 204 | `player.entry.rush_hspeed_ratio` | `ENTRY_RUSH_HS_RATIO` |
| 205 | `buffs.armor.multiplier` | `ARMOR_MULT` |
| 206 | `buffs.evasion.chance` | `EVASION_CHANCE` |
| 207 | `buffs.regen.heal_per_sec` | `REGEN_PER_SEC` |
| 208 | `effects.shake.player_hit` | `SHAKE_HIT` |
| 210 | `player.fuel.max` | `fuel_max` |
| 212 | `player.fuel.drain` | `FUEL_DRAIN` |
| 213 | `player.fuel.regen` | `FUEL_REGEN` |
| 214 | `player.fuel.restart` | `FUEL_RESTART` |
| 215 | `player.dash.distance` | `DASH_DISTANCE` |
| 216 | `player.dash.time` | `DASH_TIME` |
| 217 | `player.dash.cooldown` | `DASH_COOLDOWN` |
| 218 | `player.dash.fuel_ratio` | `DASH_FUEL_RATIO` |
| 219 | `player.dash.afterimage_interval` | `AFTERIMAGE_INTERVAL` |
| 224 | `player.aim_assist.input.magnet_input_min` | `_magnet_input_min` |
| 225 | `player.aim_assist.input.magnet_input_full` | `_magnet_input_full` |
| 226 | `player.aim_assist.falloff.peak` | `_falloff_peak` |
| 227 | `player.aim_assist.falloff.end` | `_falloff_end` |
| 228 | `player.aim_assist.falloff.min` | `_falloff_min` |
| 418 | `buffs.rapid_fire.factor` | `_rapid_fire_factor` |
| 419 | `buffs.power_shot.factor` | `_power_shot_factor` |
| 420 | `buffs.spread_shot.max_stacks` | `_spread_max` |
| 421 | `buffs.piercing.max_stacks` | `_pierce_max` |
| 422 | `buffs.efficient_boost.factor` | `_efficient_factor` |
| 423 | `buffs.boost_recovery.factor` | `_boost_recovery_factor` |
| 450 | `player.dash.cooldown_stack_factor` | `0.8` |
| 613 | `player.aim_assist.homing_time` | `HOMING_TIME` |

### `scripts/return_cinematic.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 63 | `effects.return_skip_grace` | `SKIP_GRACE` |

### `scripts/spawner.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 139 | `spawner.wave_interval_start` | `WAVE_INTERVAL_START` |
| 140 | `spawner.wave_interval_end` | `WAVE_INTERVAL_END` |
| 141 | `spawner.ramp_time` | `RAMP_TIME` |
| 142 | `spawner.boss_score_step` | `BOSS_SCORE_STEP` |
| 143 | `spawner.boss_min_interval` | `BOSS_MIN_INTERVAL` |
| 144 | `spawner.boss_time_limit` | `BOSS_TIME_LIMIT` |
| 145 | `spawner.difficulty_factor` | `DIFFICULTY_FACTOR` |
| 146 | `spawner.interval_min` | `INTERVAL_MIN` |
| 148 | `spawner.unlock_scores` | `UNLOCK_SCORES` |
| 154 | `spawner.wave_size_start` | `WAVE_SIZE_START` |
| 155 | `spawner.wave_size_end` | `WAVE_SIZE_END` |
| 156 | `spawner.special_gap_min` | `SPECIAL_GAP_MIN` |
| 157 | `spawner.special_gap_max` | `SPECIAL_GAP_MAX` |
| 158 | `spawner.rest_waves_after_kill` | `REST_WAVES_AFTER_KILL` |
| 159 | `spawner.elite_wave_size` | `ELITE_WAVE_SIZE` |
| 160 | `enemies.hover_band` | `[_hover_band.x, _hover_band.y]` |
| 162 | `elite_turret_event.min_score` | `ETV_MIN_SCORE` |
| 163 | `elite_turret_event.trigger_interval` | `ETV_TRIGGER_INTERVAL` |
| 164 | `elite_turret_event.trigger_chance` | `ETV_TRIGGER_CHANCE` |
| 165 | `formation_strike_event.trigger_interval` | `FS_TRIGGER_INTERVAL` |
| 166 | `formation_strike_event.trigger_chance` | `FS_TRIGGER_CHANCE` |
| 170 | `enemies.types` | `[]` |
| 173 | `elites.types` | `[]` |
| 467 | `spawner.telegraph_duration` | `SpawnTelegraph.DURATION` |
| 505 | `effects.shake.boss_warning` | `14.0` |

### `scripts/starfield.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 24 | `effects.starfield.far_count` | `FAR_COUNT` |
| 25 | `effects.starfield.near_count` | `NEAR_COUNT` |
| 26 | `effects.starfield.far_speed` | `FAR_SPEED` |
| 27 | `effects.starfield.near_speed` | `NEAR_SPEED` |

### `scripts/strike_carrier.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 48 | `elite_turret_event.carrier.retreat_start_speed` | `RETREAT_START_SPEED` |
| 49 | `elite_turret_event.carrier.retreat_accel` | `RETREAT_ACCEL` |

### `scripts/turret_battery.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 70 | `enemies.bullet_speed` | `SINGLE_SPEED` |
| 71 | `enemies.spread_bullet_speed` | `SPREAD_SPEED` |
| 72 | `enemies.laser_bullet_speed` | `LASER_SPEED` |
| 73 | `boss.homing_bullet_speed` | `HOMING_SPEED` |
| 74 | `boss.sniper_bullet_speed` | `SNIPER_SPEED` |
| 75 | `enemies.spread_fan_step` | `SPREAD_FAN_STEP` |
| 76 | `enemies.bullet_damage.single` | `DMG_SINGLE` |
| 77 | `enemies.bullet_damage.spread` | `DMG_SPREAD` |
| 78 | `enemies.bullet_damage.laser` | `DMG_LASER` |
| 79 | `boss.bullet_damage.homing` | `DMG_HOMING` |
| 80 | `boss.bullet_damage.sniper` | `DMG_SNIPER` |
| 209 | `effects.shake.enemy_die` | `5.0` |

### `scripts/tutorial.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 106 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 107 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 177 | `tutorial.boss_hp` | `120.0` |
| 248 | `mothership.hover_y` | `270.0` |

### `scripts/warp_gate.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 34 | `effects.mothership_summon.gate.open_time` | `OPEN_TIME` |
| 35 | `effects.mothership_summon.gate.close_time` | `CLOSE_TIME` |
| 36 | `effects.mothership_summon.gate.radius` | `RADIUS` |

### `autoload/game_state.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 97 | `world_scale` | `world_scale` |
| 100 | `milestones.base` | `MILESTONE_BASE.duplicate(` |
| 106 | `milestones.cycle_mult` | `MILESTONE_CYCLE_MULT` |
| 107 | `progression.per_boss_kill` | `0.5` |
| 108 | `progression.per_ten_minutes` | `1.0` |
| 109 | `progression.time_step_seconds` | `30.0` |
| 112 | `difficulty` | `{}` |
| 597 | `player.max_health` | `100.0` |
| 597 | `buffs.extra_life.max_hp_bonus` | `50` |
| 624 | `buffs.lifesteal.max_hp_fraction` | `0.1` |

## 动态拼接键前缀

- `boss.phases.type…`
- `buffs.…`
- `player.aim_assist.levels.…`

## json 中存在但脚本未静态引用的键

（经动态键或整段读取覆盖的不列出；剩下的请人工判断是否为死键）

- `version`

## 脚本引用但 json 缺失的键（走回退值，建议补进 json 或确认为有意兜底）

