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
| 38 | `player.aim_assist.input.magnet_input_min` | `_magnet_input_min` |
| 39 | `player.aim_assist.input.magnet_input_full` | `_magnet_input_full` |
| 40 | `player.aim_assist.falloff.peak` | `_falloff_peak` |
| 41 | `player.aim_assist.falloff.end` | `_falloff_end` |
| 42 | `player.aim_assist.falloff.min` | `_falloff_min` |

### `scripts/balance_service.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 24 | `enemies.hp_ramp_factor` | `0.12` |
| 25 | `enemies.damage_ramp_factor` | `0.08` |

### `scripts/boss.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 283 | `boss.hp_mults` | `[1.3, 0.7, 1.6]` |
| 291 | `boss.hp_base` | `HP_BASE` |
| 422 | `boss.enter_speed` | `ENTER_SPEED` |
| 423 | `boss.fight_y` | `FIGHT_Y` |
| 424 | `boss.strafe_min_x` | `STRAFE_MIN_X` |
| 425 | `boss.strafe_max_x` | `STRAFE_MAX_X` |
| 426 | `boss.phase2_hp_ratio` | `PHASE2_HP_RATIO` |
| 427 | `boss.enrage.hp_ratio` | `ENRAGE_HP_RATIO` |
| 428 | `boss.enrage.rate_mult` | `ENRAGE_RATE_MULT` |
| 429 | `boss.enrage.speed_mult` | `ENRAGE_SPEED_MULT` |
| 430 | `boss.enrage.player_slow` | `ENRAGE_PLAYER_SLOW` |
| 431 | `boss.enrage.snapshot_lasers` | `ENRAGE_SNAPSHOT_LASERS` |
| 432 | `boss.enrage.snapshot_ring` | `ENRAGE_SNAPSHOT_RING` |
| 433 | `boss.enrage.laser_speed` | `ENRAGE_LASER_SPEED` |
| 434 | `boss.enrage.ring_speed` | `ENRAGE_RING_SPEED` |
| 435 | `boss.enrage.duration` | `ENRAGE_DURATION` |
| 436 | `boss.enrage.transition_duration` | `ENRAGE_TRANSITION_DURATION` |
| 437 | `boss.enrage.attack_interval` | `ENRAGE_ATTACK_INTERVAL` |
| 438 | `boss.enrage.attack_windup` | `ENRAGE_ATTACK_WINDUP` |
| 439 | `boss.enrage.release_interval` | `ENRAGE_RELEASE_INTERVAL` |
| 440 | `boss.enrage.release_hold_duration` | `ENRAGE_RELEASE_HOLD_DURATION` |
| 441 | `boss.enrage.return_duration` | `ENRAGE_RETURN_DURATION` |
| 442 | `boss.enrage.path_radius_scale` | `ENRAGE_PATH_RADIUS_SCALE` |
| 444 | `boss.enrage.square_path_ratio` | `ENRAGE_SQUARE_PATH_RATIO` |
| 445 | `boss.enrage.release_laser_speed` | `ENRAGE_RELEASE_LASER_SPEED` |
| 446 | `boss.enrage.release_ring_speed` | `ENRAGE_RELEASE_RING_SPEED` |
| 448 | `boss.escape.time` | `ESCAPE_TIME` |
| 449 | `boss.escape.warning` | `ESCAPE_WARNING` |
| 450 | `boss.escape.drift` | `ESCAPE_DRIFT` |
| 451 | `boss.escape.start_speed` | `ESCAPE_START_SPEED` |
| 452 | `boss.escape.accel` | `ESCAPE_ACCEL` |
| 453 | `boss.escape.countdown_visible_from` | `ESCAPE_COUNTDOWN_FROM` |
| 454 | `boss.hp_base` | `HP_BASE` |
| 456 | `boss.strafe_speeds` | `STRAFE_SPEEDS` |
| 466 | `boss.fire_intervals` | `FIRE_INTERVALS` |
| 468 | `boss.fan_bullet_speed` | `FAN_BULLET_SPEED` |
| 469 | `boss.homing_bullet_speed` | `HOMING_BULLET_SPEED` |
| 470 | `boss.sniper_bullet_speed` | `SNIPER_BULLET_SPEED` |
| 471 | `boss.cross_bullet_speed` | `CROSS_BULLET_SPEED` |
| 472 | `boss.collision_damage` | `COLLISION_DAMAGE` |
| 473 | `buffs.slow_field.factor` | `SLOW_FIELD_FACTOR` |
| 474 | `boss.bullet_damage.fan` | `BULLET_DAMAGE_FAN` |
| 475 | `boss.bullet_damage.homing` | `BULLET_DAMAGE_HOMING` |
| 476 | `boss.bullet_damage.sniper` | `BULLET_DAMAGE_SNIPER` |
| 477 | `boss.bullet_damage.cross` | `BULLET_DAMAGE_CROSS` |
| 478 | `boss.bullet_damage.snapshot_laser` | `BULLET_DAMAGE_SNAPSHOT_LASER` |
| 479 | `boss.bullet_damage.snapshot_ring` | `BULLET_DAMAGE_SNAPSHOT_RING` |
| 480 | `boss.phases.phase_shift_duration` | `PHASE_SHIFT_DURATION` |
| 481 | `boss.phases.clear_on_shift` | `CLEAR_ON_SHIFT` |
| 482 | `boss.phases.transition_invincible` | `TRANSITION_INVINCIBLE` |
| 483 | `boss.phases.telegraph.sniper_aim` | `SNIPER_AIM_TIME` |
| 484 | `boss.phases.telegraph.sniper_track` | `SNIPER_TRACK_TIME` |
| 485 | `boss.phases.press_interval` | `PRESS_INTERVAL` |
| 486 | `boss.phases.press_depth` | `PRESS_DEPTH` |
| 487 | `boss.movement.type1_p2_strafe` | `TYPE1_P2_STRAFE` |
| 488 | `boss.movement.type1_p2_bob_amp` | `TYPE1_P2_BOB_AMP` |
| 489 | `boss.movement.type1_p2_bob_period` | `TYPE1_P2_BOB_PERIOD` |
| 490 | `boss.movement.type2_p2_dash_time` | `TYPE2_P2_DASH_TIME` |
| 491 | `boss.movement.type2_p2_rest_time` | `TYPE2_P2_REST_TIME` |
| 492 | `boss.movement.type3_p1_bob_min` | `TYPE3_P1_BOB_MIN` |
| 493 | `boss.movement.type3_p1_bob_max` | `TYPE3_P1_BOB_MAX` |
| 494 | `boss.movement.type3_p1_bob_period` | `TYPE3_P1_BOB_PERIOD` |
| 495 | `boss.movement.type3_p2_strafe` | `TYPE3_P2_STRAFE` |
| 496 | `boss.movement.type3_p2_bob_amp` | `TYPE3_P2_BOB_AMP` |
| 497 | `boss.movement.type3_p2_bob_period` | `TYPE3_P2_BOB_PERIOD` |
| 499 | `boss.phases.attacks.charged_cannon.charge` | `CANNON_CHARGE` |
| 500 | `boss.phases.attacks.charged_cannon.shots` | `CANNON_SHOTS` |
| 501 | `boss.phases.attacks.charged_cannon.interval` | `CANNON_INTERVAL` |
| 502 | `boss.phases.attacks.charged_cannon.bullet_speed` | `CANNON_BULLET_SPEED` |
| 503 | `boss.phases.attacks.charged_cannon.damage` | `CANNON_DAMAGE` |
| 504 | `boss.phases.attacks.charged_cannon.flash` | `CANNON_FLASH` |
| 505 | `boss.phases.attacks.dash_sweep.aim` | `SWEEP_AIM` |
| 506 | `boss.phases.attacks.dash_sweep.speed` | `SWEEP_SPEED` |
| 507 | `boss.phases.attacks.dash_sweep.drop_count` | `SWEEP_DROP_COUNT` |
| 508 | `boss.phases.attacks.dash_sweep.drop_speed` | `SWEEP_DROP_SPEED` |
| 509 | `boss.phases.attacks.dash_sweep.drop_damage` | `SWEEP_DROP_DAMAGE` |
| 510 | `boss.phases.attacks.dash_sweep.return_duration` | `SWEEP_RETURN_DURATION` |
| 511 | `boss.phases.attacks.minion_volley.count` | `VOLLEY_COUNT` |
| 512 | `boss.phases.attacks.minion_volley.delay` | `VOLLEY_DELAY` |
| 513 | `boss.phases.attacks.minion_volley.bullet_speed` | `VOLLEY_BULLET_SPEED` |
| 514 | `boss.phases.attacks.minion_volley.bullet_damage` | `VOLLEY_BULLET_DAMAGE` |
| 515 | `boss.phases.attacks.bullet_wall.count` | `WALL_COUNT` |
| 516 | `boss.phases.attacks.bullet_wall.bullet_speed` | `WALL_BULLET_SPEED` |
| 517 | `boss.phases.attacks.bullet_wall.damage` | `WALL_DAMAGE` |
| 518 | `boss.phases.attacks.bullet_wall.arc_deg` | `WALL_ARC_DEG` |
| 520 | `boss.enrage.type_1.ring_interval` | `E1_RING_INTERVAL` |
| 521 | `boss.enrage.type_1.ring_count` | `E1_RING_COUNT` |
| 522 | `boss.enrage.type_1.ring_speed` | `E1_RING_SPEED` |
| 523 | `boss.enrage.type_1.ring_precession_deg` | `E1_RING_PRECESSION_DEG` |
| 524 | `boss.enrage.type_1.salvo_charge` | `E1_SALVO_CHARGE` |
| 525 | `boss.enrage.type_1.salvo_count` | `E1_SALVO_COUNT` |
| 526 | `boss.enrage.type_1.salvo_speed` | `E1_SALVO_SPEED` |
| 527 | `boss.enrage.type_1.salvo_damage` | `E1_SALVO_DAMAGE` |
| 528 | `boss.enrage.type_2.point_count` | `E2_POINT_COUNT` |
| 529 | `boss.enrage.type_2.point_interval` | `E2_POINT_INTERVAL` |
| 530 | `boss.enrage.type_2.aim` | `E2_AIM` |
| 531 | `boss.enrage.type_2.sniper_speed` | `E2_SNIPER_SPEED` |
| 532 | `boss.enrage.type_2.sniper_damage` | `E2_SNIPER_DAMAGE` |
| 533 | `boss.enrage.type_2.release_ring_count` | `E2_RELEASE_RING_COUNT` |
| 534 | `boss.enrage.type_2.release_ring_speed` | `E2_RELEASE_RING_SPEED` |
| 535 | `boss.enrage.type_3.summon_interval` | `E3_SUMMON_INTERVAL` |
| 537 | `boss.phases.type3.summon_interval` | `_summon_interval` |
| 539 | `boss.enrage.type_3.summon_waves` | `E3_SUMMON_WAVES` |
| 540 | `boss.enrage.type_3.summon_count` | `E3_SUMMON_COUNT` |
| 541 | `boss.enrage.type_3.ring_interval` | `E3_RING_INTERVAL` |
| 542 | `boss.enrage.type_3.ring_count` | `E3_RING_COUNT` |
| 543 | `boss.enrage.type_3.ring_speed` | `E3_RING_SPEED` |
| 544 | `boss.enrage.type_3.release_ring_count` | `E3_RELEASE_RING_COUNT` |
| 545 | `boss.enrage.type_3.release_ring_speed` | `E3_RELEASE_RING_SPEED` |
| 547 | `boss.difficulty_scaling.interval_mult` | `DIFF_INTERVAL_MULT` |
| 548 | `boss.difficulty_scaling.speed_mult` | `DIFF_SPEED_MULT` |
| 549 | `boss.difficulty_scaling.counts` | `DIFF_COUNT_DELTAS` |
| 775 | `effects.shake.enrage` | `16.0` |
| 927 | `effects.shake.enrage` | `16.0` |

### `scripts/buff_select.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 66 | `buffs.explosive.unlock_boss_kills` | `3` |
| 233 | `buffs.extra_life.heal_on_pick` | `30` |
| 268 | `buffs.extra_life.heal_on_pick` | `30` |

### `scripts/bullet.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 143 | `buffs.explosive.radius_per_level` | `EXPLOSIVE_RADIUS` |
| 144 | `buffs.explosive.damage_per_level` | `EXPLOSIVE_DAMAGE` |
| 145 | `effects.bullet_visual_scale` | `VISUAL_SCALE` |
| 146 | `effects.enemy_bullet_visual_scale` | `ENEMY_VISUAL_SCALE` |
| 148 | `player.grace_period` | `GRACE_PERIOD` |
| 150 | `player.parry.reflect_speed_mult` | `REFLECT_SPEED_MULT` |
| 151 | `player.parry.reflect_damage_mult` | `REFLECT_DAMAGE_MULT` |

### `scripts/camera_shake.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 14 | `effects.shake.decay` | `DECAY` |

### `scripts/elite_turret_event.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 100 | `elite_turret_event.duration` | `DURATION` |
| 101 | `elite_turret_event.enter_time` | `ENTER_TIME` |
| 102 | `elite_turret_event.rise_time` | `RISE_TIME` |
| 103 | `elite_turret_event.boss_resume_delay` | `BOSS_RESUME_DELAY` |
| 104 | `elite_turret_event.turret_hp_base` | `TURRET_HP_BASE` |
| 107 | `elite_turret_event.turret_counts` | `TURRET_COUNTS` |
| 110 | `elite_turret_event.ammo_sequences` | `AMMO_SEQUENCES` |
| 114 | `elite_turret_event.fire_interval` | `[FIRE_INTERVAL.x, FIRE_INTERVAL.y]` |
| 119 | `elite_turret_event.weak_lock` | `WEAK_LOCK` |
| 120 | `elite_turret_event.reward_score` | `REWARD_SCORE` |
| 121 | `elite_turret_event.carrier.hover_y` | `HOVER_Y` |
| 122 | `elite_turret_event.cooldown` | `COOLDOWN` |
| 160 | `elite_turret_event.carrier.shake` | `4.0` |

### `scripts/enemy.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 131 | `enemies.hp_ramp_factor` | `HP_RAMP_FACTOR` |
| 146 | `enemies.speed_ramp_factor` | `SPEED_RAMP_FACTOR` |
| 154 | `player.aim_assist.mark_ratio` | `0.25` |
| 211 | `enemies.bullet_speed` | `ENEMY_BULLET_SPEED` |
| 212 | `enemies.spread_bullet_speed` | `SPREAD_BULLET_SPEED` |
| 213 | `enemies.laser_bullet_speed` | `LASER_BULLET_SPEED` |
| 214 | `enemies.bullet_damage.single` | `BULLET_DAMAGE_SINGLE` |
| 215 | `enemies.bullet_damage.spread` | `BULLET_DAMAGE_SPREAD` |
| 216 | `enemies.bullet_damage.laser` | `BULLET_DAMAGE_LASER` |
| 217 | `enemies.collision_damage` | `COLLISION_DAMAGE` |
| 218 | `buffs.slow_field.factor` | `SLOW_FIELD_FACTOR` |
| 219 | `enemies.spread_fan_step` | `SPREAD_FAN_STEP` |
| 220 | `enemies.lifetime` | `LIFETIME` |
| 221 | `enemies.exit_accel` | `EXIT_ACCEL` |
| 222 | `enemies.aggressive_chase_speed` | `AGGR_CHASE_SPEED` |
| 223 | `enemies.fire_interval` | `FIRE_INTERVAL` |
| 225 | `enemies.hover_band` | `[HOVER_BAND.x, HOVER_BAND.y]` |
| 230 | `enemies.hover_bob_amp` | `HOVER_BOB_AMP` |
| 231 | `enemies.hover_bob_freq` | `HOVER_BOB_FREQ` |
| 232 | `enemies.hover_sway_amp` | `HOVER_SWAY_AMP` |
| 233 | `enemies.hover_sway_freq` | `HOVER_SWAY_FREQ` |
| 234 | `enemies.spiral_drift_amp` | `SPIRAL_DRIFT_AMP` |
| 235 | `enemies.spiral_drift_freq` | `SPIRAL_DRIFT_FREQ` |
| 236 | `enemies.spiral_radius` | `SPIRAL_RADIUS` |
| 253 | `effects.shake.enemy_die` | `_shake_die_normal` |
| 254 | `effects.shake.elite_die` | `_shake_die_elite` |

### `scripts/explosion.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 23 | `effects.explosion.pool_cap` | `POOL_CAP` |
| 31 | `effects.explosion_visual_scale` | `1.6` |
| 50 | `effects.shake.boss_seq_initial` | `20.0` |
| 63 | `effects.shake.boss_seq_step` | `8.0` |
| 74 | `effects.shake.boss_seq_final` | `24.0` |
| 82 | `effects.explosion.amount` | `24` |
| 102 | `effects.explosion.debris_amount` | `10` |

### `scripts/formation_bomb.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 98 | `effects.shake.enemy_die` | `5.0` |

### `scripts/formation_craft.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 47 | `effects.shake.enemy_die` | `_shake_die` |

### `scripts/formation_strike_event.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 67 | `formation_strike_event.min_score` | `MIN_SCORE` |
| 68 | `formation_strike_event.cooldown` | `COOLDOWN` |
| 69 | `formation_strike_event.craft_counts` | `CRAFT_COUNTS` |
| 70 | `formation_strike_event.craft_hp_base` | `CRAFT_HP_BASE` |
| 71 | `formation_strike_event.craft_score` | `CRAFT_SCORE` |
| 72 | `formation_strike_event.approach_speed` | `APPROACH_SPEED` |
| 73 | `formation_strike_event.approach_y` | `APPROACH_Y` |
| 74 | `formation_strike_event.turn_time` | `TURN_TIME` |
| 75 | `formation_strike_event.run_speed` | `RUN_SPEED` |
| 76 | `formation_strike_event.bomb_interval` | `BOMB_INTERVAL` |
| 77 | `formation_strike_event.bombs_per_craft` | `BOMBS_PER_CRAFT` |
| 78 | `formation_strike_event.bomb_fall_speed` | `BOMB_FALL_SPEED` |
| 79 | `formation_strike_event.bomb_fuse` | `BOMB_FUSE` |
| 80 | `formation_strike_event.bomb_damage` | `BOMB_DAMAGE` |
| 81 | `formation_strike_event.bomb_radius` | `BOMB_RADIUS` |
| 82 | `formation_strike_event.reward_all_clear` | `REWARD_ALL_CLEAR` |

### `scripts/hud.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 102 | `effects.hud_poll_interval` | `POLL_INTERVAL` |
| 103 | `hud.boss_bar_segments` | `BOSS_BAR_SEGMENTS` |
| 104 | `effects.hit_flash.alpha` | `HIT_FLASH_ALPHA` |
| 105 | `effects.hit_flash.time` | `HIT_FLASH_TIME` |
| 106 | `effects.low_hp.ratio` | `LOW_HP_RATIO` |
| 107 | `effects.low_hp.pulse_min` | `LOW_HP_PULSE_MIN` |
| 108 | `effects.low_hp.pulse_max` | `LOW_HP_PULSE_MAX` |
| 109 | `effects.low_hp.pulse_period` | `LOW_HP_PULSE_PERIOD` |

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
| 72 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 73 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 74 | `effects.give_up_hold_time` | `GIVE_UP_HOLD_TIME` |
| 75 | `boss.enrage.slow_scale` | `ENRAGE_SLOW_SCALE` |
| 76 | `boss.enrage.bullet_time` | `ENRAGE_BULLET_TIME` |
| 77 | `boss.enrage.ramp_time` | `ENRAGE_RAMP_TIME` |
| 635 | `effects.mothership_summon.shake_gate` | `6.0` |
| 646 | `buffs.mothership_recall.cooldown_factor` | `0.5` |
| 686 | `mothership.depart_cooldown` | `60.0` |

### `scripts/meta_health_fx.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 155 | `effects.meta_health.crack.density` | `DENSITY_CAPS.duplicate(` |
| 166 | `effects.meta_health.lod` | `0` |
| 167 | `effects.meta_health.pulse.scale` | `2.5` |
| 168 | `effects.meta_health.pulse.min` | `0.15` |
| 169 | `effects.meta_health.pulse.decay_tau` | `0.09` |
| 170 | `effects.meta_health.chromatic.base` | `0.006` |
| 171 | `effects.meta_health.chromatic.peak` | `0.014` |
| 172 | `effects.meta_health.blur.strength` | `0.6` |
| 173 | `effects.meta_health.ripple.duration` | `0.4` |
| 174 | `effects.meta_health.ripple.alpha` | `0.8` |
| 175 | `effects.meta_health.crack.exponent` | `1.6` |
| 176 | `effects.meta_health.crack.spread_min` | `0.10` |
| 177 | `effects.meta_health.crack.edge_softness` | `0.08` |
| 178 | `effects.meta_health.crack.width` | `0.03` |
| 179 | `effects.meta_health.crack.glow` | `0.8` |
| 180 | `effects.meta_health.crack.heal_jitter` | `0.35` |
| 181 | `effects.meta_health.crack.grow_overshoot` | `0.08` |
| 182 | `effects.meta_health.crack.grow_time` | `0.6` |
| 184 | `effects.meta_health.desat.max` | `0.35` |
| 185 | `effects.meta_health.desat.exponent` | `2.0` |
| 186 | `effects.meta_health.vignette.max_alpha` | `0.5` |
| 187 | `effects.meta_health.vignette.inner` | `0.62` |
| 188 | `effects.meta_health.vignette.dying_shrink` | `0.06` |
| 189 | `effects.meta_health.dying.threshold` | `0.2` |
| 190 | `effects.meta_health.dying.heart_min_hz` | `1.0` |
| 191 | `effects.meta_health.dying.heart_max_hz` | `1.2` |
| 192 | `effects.meta_health.dying.breath` | `0.015` |
| 193 | `effects.meta_health.dying.jitter_px` | `2.0` |
| 194 | `effects.meta_health.dying.warn_hz` | `2.5` |
| 195 | `effects.meta_health.dying.fade` | `0.3` |
| 196 | `effects.meta_health.smooth.down_tau` | `0.10` |
| 197 | `effects.meta_health.smooth.up_tau` | `0.80` |
| 198 | `effects.meta_health.adapt.interval` | `0.25` |
| 199 | `effects.meta_health.adapt.min` | `0.8` |
| 200 | `effects.meta_health.adapt.max` | `1.3` |
| 201 | `effects.meta_health.adapt.bullet_weight` | `0.002` |
| 202 | `effects.meta_health.adapt.explosion_weight` | `0.15` |
| 203 | `effects.meta_health.reduce_flash.chromatic_scale` | `0.4` |

### `scripts/mothership.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 121 | `mothership.hover_y` | `HOVER_Y` |
| 122 | `mothership.release_invincible` | `RELEASE_INVINCIBLE` |
| 123 | `mothership.dock_tween_time` | `DOCK_TWEEN_TIME` |
| 124 | `mothership.dock_offset_y` | `DOCK_OFFSET_Y` |
| 125 | `mothership.resupply_delay` | `RESUPPLY_DELAY` |
| 126 | `mothership.release_time` | `RELEASE_TIME` |
| 127 | `mothership.release_drop` | `RELEASE_DROP` |
| 128 | `mothership.mag_cells` | `MAG_CELLS` |
| 129 | `mothership.mag_cell_time` | `MAG_CELL_TIME` |
| 130 | `mothership.mag_warn_cells` | `MAG_WARN_CELLS` |
| 131 | `mothership.warn_eject_delay` | `WARN_EJECT_DELAY` |
| 132 | `mothership.early_hold_time` | `EARLY_HOLD_TIME` |
| 133 | `mothership.early_max_discount` | `EARLY_MAX_DISCOUNT` |
| 134 | `mothership.early_prefill_max` | `EARLY_PREFILL_MAX` |
| 135 | `mothership.early_prefill_ratio` | `EARLY_PREFILL_RATIO` |
| 136 | `mothership.depart_cooldown` | `DEPART_COOLDOWN` |
| 137 | `mothership.depart_start_speed` | `DEPART_START_SPEED` |
| 138 | `mothership.depart_accel` | `DEPART_ACCEL` |
| 139 | `mothership.drive.accel` | `DRIVE_ACCEL` |
| 140 | `mothership.drive.max_speed` | `DRIVE_MAX_SPEED` |
| 144 | `mothership.drive.margin_x` | `DRIVE_MARGIN_X` |
| 145 | `mothership.drive.margin_top` | `DRIVE_MARGIN_TOP` |
| 146 | `mothership.drive.margin_bottom` | `DRIVE_MARGIN_BOTTOM` |
| 147 | `mothership.gatling.interval` | `GATLING_INTERVAL` |
| 148 | `mothership.gatling.bullet_speed` | `GATLING_BULLET_SPEED` |
| 149 | `mothership.gatling.damage` | `GATLING_DAMAGE` |
| 150 | `mothership.gatling.score_scale` | `GATLING_SCORE_SCALE` |
| 151 | `mothership.gatling.sweep_left_min` | `GATLING_SWEEP_LEFT_MIN` |
| 152 | `mothership.gatling.sweep_left_max` | `GATLING_SWEEP_LEFT_MAX` |
| 153 | `mothership.gatling.sweep_right_min` | `GATLING_SWEEP_RIGHT_MIN` |
| 154 | `mothership.gatling.sweep_right_max` | `GATLING_SWEEP_RIGHT_MAX` |
| 155 | `mothership.gatling.sweep_left_period` | `GATLING_SWEEP_LEFT_PERIOD` |
| 156 | `mothership.gatling.sweep_right_period` | `GATLING_SWEEP_RIGHT_PERIOD` |
| 157 | `mothership.gatling.sweep_right_phase` | `GATLING_SWEEP_RIGHT_PHASE` |
| 158 | `mothership.missile.interval` | `MISSILE_INTERVAL` |
| 159 | `mothership.missile.damage` | `MISSILE_DAMAGE` |
| 160 | `mothership.missile.speed` | `MISSILE_SPEED` |
| 161 | `mothership.missile.target_count` | `MISSILE_TARGET_COUNT` |
| 162 | `mothership.missile.splash_damage` | `MISSILE_SPLASH_DAMAGE` |
| 163 | `mothership.missile.splash_radius` | `MISSILE_SPLASH_RADIUS` |
| 164 | `effects.mothership_summon.warp_in_time` | `WARP_IN_TIME` |
| 165 | `effects.mothership_summon.warp_in_drop` | `WARP_IN_DROP` |
| 166 | `effects.mothership_summon.slow.radius` | `SLOW_RADIUS` |
| 167 | `effects.mothership_summon.slow.duration` | `SLOW_DURATION` |
| 168 | `effects.mothership_summon.slow.factor` | `SLOW_FACTOR` |
| 169 | `effects.mothership_summon.slow.ring_time` | `SLOW_RING_TIME` |
| 170 | `effects.mothership_summon.shake_slow` | `SHAKE_SLOW` |
| 300 | `effects.mothership_summon.warp_in_drop` | `WARP_IN_DROP` |
| 692 | `effects.shake.mothership` | `4.0` |

### `scripts/mothership_summon_window.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 50 | `effects.mothership_summon.window.open_time` | `OPEN_TIME` |
| 51 | `effects.mothership_summon.window.close_time` | `CLOSE_TIME` |
| 53 | `effects.mothership_summon.window.shot_durations` | `_shot_durations` |

### `scripts/orbital_strike.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 49 | `effects.orbital_strike.duration` | `DURATION` |
| 50 | `effects.orbital_strike.impact_at` | `IMPACT_AT` |
| 51 | `effects.orbital_strike.missile_from` | `MISSILE_FROM` |
| 52 | `effects.orbital_strike.reticle_radius` | `RETICLE_RADIUS` |
| 53 | `effects.orbital_strike.impact_y_ratio` | `IMPACT_Y_RATIO` |
| 73 | `effects.shake.boss_seq_final` | `24.0` |

### `scripts/player.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 239 | `player.max_speed` | `MAX_SPEED` |
| 240 | `player.accel` | `ACCEL` |
| 241 | `player.decel` | `DECEL` |
| 242 | `player.boost_mult` | `BOOST_MULT` |
| 243 | `player.fine_move_mult` | `FINE_MOVE_MULT` |
| 244 | `player.base_fire_interval` | `BASE_FIRE_INTERVAL` |
| 245 | `player.bullet_speed` | `BULLET_SPEED` |
| 246 | `player.bullet_spread_deg` | `BULLET_SPREAD_DEG` |
| 247 | `player.bullet_damage` | `BULLET_DAMAGE` |
| 248 | `player.invincible_time` | `INVINCIBLE_TIME` |
| 249 | `player.spawn_invincible_time` | `SPAWN_INVINCIBLE_TIME` |
| 250 | `player.bullet_clear_radius` | `BULLET_CLEAR_RADIUS` |
| 251 | `player.entry.land_ratio` | `ENTRY_LAND_RATIO` |
| 252 | `player.entry.rush_time` | `ENTRY_RUSH_TIME` |
| 253 | `player.entry.retreat_speed` | `ENTRY_RETREAT_SPEED` |
| 254 | `player.entry.retreat_time` | `ENTRY_RETREAT_TIME` |
| 255 | `player.entry.invincible` | `ENTRY_INVINCIBLE` |
| 256 | `player.entry.spawn_clearance` | `ENTRY_SPAWN_CLEARANCE` |
| 257 | `player.entry.rush_hspeed_ratio` | `ENTRY_RUSH_HS_RATIO` |
| 258 | `buffs.armor.multiplier` | `ARMOR_MULT` |
| 259 | `buffs.evasion.chance` | `EVASION_CHANCE` |
| 260 | `buffs.regen.heal_per_sec` | `REGEN_PER_SEC` |
| 261 | `effects.shake.player_hit` | `SHAKE_HIT` |
| 263 | `player.fuel.max` | `fuel_max` |
| 265 | `player.fuel.drain` | `FUEL_DRAIN` |
| 266 | `player.fuel.regen` | `FUEL_REGEN` |
| 267 | `player.fuel.restart` | `FUEL_RESTART` |
| 268 | `player.dash.distance` | `DASH_DISTANCE` |
| 269 | `player.dash.time` | `DASH_TIME` |
| 270 | `player.dash.cooldown` | `DASH_COOLDOWN` |
| 271 | `player.dash.fuel_ratio` | `DASH_FUEL_RATIO` |
| 272 | `player.dash.afterimage_interval` | `AFTERIMAGE_INTERVAL` |
| 274 | `player.graze_radius` | `GRAZE_RADIUS` |
| 275 | `player.graze_score` | `GRAZE_SCORE` |
| 277 | `player.parry.arc_deg` | `PARRY_ARC_DEG` |
| 278 | `player.parry.radius` | `PARRY_RADIUS` |
| 280 | `player.parry.duration` | `0.8` |
| 281 | `player.parry.active_time` | `0.5` |
| 282 | `player.parry.cooldown` | `3.0` |
| 288 | `player.aim_assist.input.magnet_input_min` | `_magnet_input_min` |
| 289 | `player.aim_assist.input.magnet_input_full` | `_magnet_input_full` |
| 290 | `player.aim_assist.falloff.peak` | `_falloff_peak` |
| 291 | `player.aim_assist.falloff.end` | `_falloff_end` |
| 292 | `player.aim_assist.falloff.min` | `_falloff_min` |
| 518 | `buffs.rapid_fire.factor` | `_rapid_fire_factor` |
| 519 | `buffs.power_shot.factor` | `_power_shot_factor` |
| 520 | `buffs.spread_shot.max_stacks` | `_spread_max` |
| 521 | `buffs.piercing.max_stacks` | `_pierce_max` |
| 522 | `buffs.efficient_boost.factor` | `_efficient_factor` |
| 523 | `buffs.boost_recovery.factor` | `_boost_recovery_factor` |
| 524 | `player.dash.cooldown_stack_factor` | `_dash_cooldown_stack_factor` |
| 734 | `player.aim_assist.homing_time` | `HOMING_TIME` |

### `scripts/return_cinematic.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 62 | `effects.return_skip_grace` | `SKIP_GRACE` |

### `scripts/spawner.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 176 | `spawner.wave_interval_start` | `WAVE_INTERVAL_START` |
| 177 | `spawner.wave_interval_end` | `WAVE_INTERVAL_END` |
| 178 | `spawner.ramp_time` | `RAMP_TIME` |
| 179 | `spawner.boss_score_step` | `BOSS_SCORE_STEP` |
| 180 | `spawner.boss_min_interval` | `BOSS_MIN_INTERVAL` |
| 181 | `spawner.boss_time_limit` | `BOSS_TIME_LIMIT` |
| 182 | `spawner.difficulty_factor` | `DIFFICULTY_FACTOR` |
| 183 | `spawner.interval_min` | `INTERVAL_MIN` |
| 185 | `spawner.unlock_scores` | `UNLOCK_SCORES` |
| 191 | `spawner.wave_size_start` | `WAVE_SIZE_START` |
| 192 | `spawner.wave_size_end` | `WAVE_SIZE_END` |
| 193 | `spawner.special_gap_min` | `SPECIAL_GAP_MIN` |
| 194 | `spawner.special_gap_max` | `SPECIAL_GAP_MAX` |
| 195 | `spawner.rest_waves_after_kill` | `REST_WAVES_AFTER_KILL` |
| 196 | `spawner.elite_wave_size` | `ELITE_WAVE_SIZE` |
| 198 | `enemies.hover_band` | `[_hover_band.x, _hover_band.y]` |
| 201 | `elite_turret_event.min_score` | `ETV_MIN_SCORE` |
| 202 | `elite_turret_event.trigger_interval` | `ETV_TRIGGER_INTERVAL` |
| 203 | `elite_turret_event.trigger_chance` | `ETV_TRIGGER_CHANCE` |
| 204 | `formation_strike_event.trigger_interval` | `FS_TRIGGER_INTERVAL` |
| 205 | `formation_strike_event.trigger_chance` | `FS_TRIGGER_CHANCE` |
| 209 | `enemies.types` | `[]` |
| 212 | `elites.types` | `[]` |
| 506 | `spawner.telegraph_duration` | `SpawnTelegraph.DURATION` |
| 544 | `effects.shake.boss_warning` | `14.0` |

### `scripts/starfield.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 29 | `effects.starfield.far_count` | `FAR_COUNT` |
| 30 | `effects.starfield.near_count` | `NEAR_COUNT` |
| 31 | `effects.starfield.far_speed` | `FAR_SPEED` |
| 32 | `effects.starfield.near_speed` | `NEAR_SPEED` |

### `scripts/strike_carrier.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 48 | `elite_turret_event.carrier.retreat_start_speed` | `RETREAT_START_SPEED` |
| 49 | `elite_turret_event.carrier.retreat_accel` | `RETREAT_ACCEL` |

### `scripts/turret_battery.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 77 | `enemies.bullet_speed` | `SINGLE_SPEED` |
| 78 | `enemies.spread_bullet_speed` | `SPREAD_SPEED` |
| 79 | `enemies.laser_bullet_speed` | `LASER_SPEED` |
| 80 | `boss.homing_bullet_speed` | `HOMING_SPEED` |
| 81 | `boss.sniper_bullet_speed` | `SNIPER_SPEED` |
| 82 | `enemies.spread_fan_step` | `SPREAD_FAN_STEP` |
| 83 | `enemies.bullet_damage.single` | `DMG_SINGLE` |
| 84 | `enemies.bullet_damage.spread` | `DMG_SPREAD` |
| 85 | `enemies.bullet_damage.laser` | `DMG_LASER` |
| 86 | `boss.bullet_damage.homing` | `DMG_HOMING` |
| 87 | `boss.bullet_damage.sniper` | `DMG_SNIPER` |
| 102 | `effects.shake.enemy_die` | `_shake_die` |

### `scripts/tutorial.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 109 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 110 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 182 | `tutorial.boss_hp` | `120.0` |
| 254 | `mothership.hover_y` | `270.0` |

### `scripts/warp_gate.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 34 | `effects.mothership_summon.gate.open_time` | `OPEN_TIME` |
| 35 | `effects.mothership_summon.gate.close_time` | `CLOSE_TIME` |
| 36 | `effects.mothership_summon.gate.radius` | `RADIUS` |

### `autoload/game_state.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 752 | `milestones.boss_kill_base` | `500.0` |
| 123 | `world_scale` | `world_scale` |
| 126 | `milestones.base` | `MILESTONE_BASE.duplicate(` |
| 135 | `milestones.cycle_mult` | `MILESTONE_CYCLE_MULT` |
| 137 | `progression.per_boss_kill` | `0.5` |
| 138 | `progression.per_ten_minutes` | `1.0` |
| 139 | `progression.time_step_seconds` | `30.0` |
| 142 | `difficulty` | `{}` |
| 147 | `player.max_health` | `_max_hp_base` |
| 148 | `buffs.extra_life.max_hp_bonus` | `_max_hp_bonus` |
| 803 | `buffs.lifesteal.max_hp_fraction` | `0.1` |
| 1304 | `player.aim_assist.joy_speed` | `joy_aim_speed` |

## 动态拼接键前缀

- `boss.phases.type…`
- `buffs.…`
- `player.aim_assist.levels.…`

## json 中存在但脚本未静态引用的键

（经动态键或整段读取覆盖的不列出；剩下的请人工判断是否为死键）

- `version`

## 脚本引用但 json 缺失的键（走回退值，建议补进 json 或确认为有意兜底）

