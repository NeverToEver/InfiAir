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
| 40 | `player.aim_assist.input.magnet_input_min` | `_magnet_input_min` |
| 41 | `player.aim_assist.input.magnet_input_full` | `_magnet_input_full` |
| 42 | `player.aim_assist.falloff.peak` | `_falloff_peak` |
| 43 | `player.aim_assist.falloff.end` | `_falloff_end` |
| 44 | `player.aim_assist.falloff.min` | `_falloff_min` |

### `scripts/balance_service.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 37 | `enemies.hp_ramp_factor` | `0.25` |
| 38 | `enemies.damage_ramp_factor` | `0.20` |

### `scripts/boss.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 338 | `boss.hp_mults` | `[1.3, 0.7, 1.6, 1.2]` |
| 348 | `boss.hp_base` | `HP_BASE` |
| 478 | `boss.enter_speed` | `ENTER_SPEED` |
| 479 | `boss.fight_y` | `FIGHT_Y` |
| 480 | `boss.strafe_min_x` | `STRAFE_MIN_X` |
| 481 | `boss.strafe_max_x` | `STRAFE_MAX_X` |
| 482 | `boss.phase2_hp_ratio` | `PHASE2_HP_RATIO` |
| 483 | `boss.enrage.hp_ratio` | `ENRAGE_HP_RATIO` |
| 484 | `boss.enrage.rate_mult` | `ENRAGE_RATE_MULT` |
| 485 | `boss.enrage.speed_mult` | `ENRAGE_SPEED_MULT` |
| 486 | `boss.enrage.player_slow` | `ENRAGE_PLAYER_SLOW` |
| 487 | `boss.enrage.snapshot_lasers` | `ENRAGE_SNAPSHOT_LASERS` |
| 488 | `boss.enrage.snapshot_ring` | `ENRAGE_SNAPSHOT_RING` |
| 489 | `boss.enrage.laser_speed` | `ENRAGE_LASER_SPEED` |
| 490 | `boss.enrage.ring_speed` | `ENRAGE_RING_SPEED` |
| 491 | `boss.enrage.duration` | `ENRAGE_DURATION` |
| 492 | `boss.enrage.transition_duration` | `ENRAGE_TRANSITION_DURATION` |
| 493 | `boss.enrage.attack_interval` | `ENRAGE_ATTACK_INTERVAL` |
| 494 | `boss.enrage.attack_windup` | `ENRAGE_ATTACK_WINDUP` |
| 495 | `boss.enrage.release_interval` | `ENRAGE_RELEASE_INTERVAL` |
| 496 | `boss.enrage.release_hold_duration` | `ENRAGE_RELEASE_HOLD_DURATION` |
| 497 | `boss.enrage.return_duration` | `ENRAGE_RETURN_DURATION` |
| 498 | `boss.enrage.path_radius_scale` | `ENRAGE_PATH_RADIUS_SCALE` |
| 500 | `boss.enrage.square_path_ratio` | `ENRAGE_SQUARE_PATH_RATIO` |
| 501 | `boss.enrage.release_laser_speed` | `ENRAGE_RELEASE_LASER_SPEED` |
| 502 | `boss.enrage.release_ring_speed` | `ENRAGE_RELEASE_RING_SPEED` |
| 504 | `boss.escape.time` | `ESCAPE_TIME` |
| 505 | `boss.escape.warning` | `ESCAPE_WARNING` |
| 506 | `boss.escape.drift` | `ESCAPE_DRIFT` |
| 507 | `boss.escape.start_speed` | `ESCAPE_START_SPEED` |
| 508 | `boss.escape.accel` | `ESCAPE_ACCEL` |
| 515 | `boss.escape.countdown_visible_from` | `ESCAPE_COUNTDOWN_FROM` |
| 516 | `boss.hp_base` | `HP_BASE` |
| 518 | `boss.strafe_speeds` | `STRAFE_SPEEDS` |
| 528 | `boss.fire_intervals` | `FIRE_INTERVALS` |
| 530 | `boss.fan_bullet_speed` | `FAN_BULLET_SPEED` |
| 531 | `boss.homing_bullet_speed` | `HOMING_BULLET_SPEED` |
| 532 | `boss.sniper_bullet_speed` | `SNIPER_BULLET_SPEED` |
| 533 | `boss.cross_bullet_speed` | `CROSS_BULLET_SPEED` |
| 534 | `boss.collision_damage` | `COLLISION_DAMAGE` |
| 535 | `buffs.slow_field.factor` | `SLOW_FIELD_FACTOR` |
| 536 | `boss.bullet_damage.fan` | `BULLET_DAMAGE_FAN` |
| 537 | `boss.bullet_damage.homing` | `BULLET_DAMAGE_HOMING` |
| 538 | `boss.bullet_damage.sniper` | `BULLET_DAMAGE_SNIPER` |
| 539 | `boss.bullet_damage.cross` | `BULLET_DAMAGE_CROSS` |
| 540 | `boss.bullet_damage.snapshot_laser` | `BULLET_DAMAGE_SNAPSHOT_LASER` |
| 541 | `boss.bullet_damage.snapshot_ring` | `BULLET_DAMAGE_SNAPSHOT_RING` |
| 542 | `boss.phases.phase_shift_duration` | `PHASE_SHIFT_DURATION` |
| 543 | `boss.phases.clear_on_shift` | `CLEAR_ON_SHIFT` |
| 544 | `boss.phases.transition_invincible` | `TRANSITION_INVINCIBLE` |
| 545 | `boss.phases.telegraph.sniper_aim` | `SNIPER_AIM_TIME` |
| 546 | `boss.phases.telegraph.sniper_track` | `SNIPER_TRACK_TIME` |
| 547 | `boss.phases.attacks.sniper3.burst_interval` | `SNIPER_BURST_INTERVAL` |
| 548 | `boss.phases.press_interval` | `PRESS_INTERVAL` |
| 549 | `boss.phases.press_depth` | `PRESS_DEPTH` |
| 550 | `boss.movement.type1_p2_strafe` | `TYPE1_P2_STRAFE` |
| 551 | `boss.movement.type1_p2_bob_amp` | `TYPE1_P2_BOB_AMP` |
| 552 | `boss.movement.type1_p2_bob_period` | `TYPE1_P2_BOB_PERIOD` |
| 553 | `boss.movement.type2_p2_dash_time` | `TYPE2_P2_DASH_TIME` |
| 554 | `boss.movement.type2_p2_rest_time` | `TYPE2_P2_REST_TIME` |
| 555 | `boss.movement.type3_p1_bob_min` | `TYPE3_P1_BOB_MIN` |
| 556 | `boss.movement.type3_p1_bob_max` | `TYPE3_P1_BOB_MAX` |
| 557 | `boss.movement.type3_p1_bob_period` | `TYPE3_P1_BOB_PERIOD` |
| 558 | `boss.movement.type3_p2_strafe` | `TYPE3_P2_STRAFE` |
| 559 | `boss.movement.type3_p2_bob_amp` | `TYPE3_P2_BOB_AMP` |
| 560 | `boss.movement.type3_p2_bob_period` | `TYPE3_P2_BOB_PERIOD` |
| 562 | `boss.phases.attacks.charged_cannon.charge` | `CANNON_CHARGE` |
| 563 | `boss.phases.attacks.charged_cannon.shots` | `CANNON_SHOTS` |
| 564 | `boss.phases.attacks.charged_cannon.interval` | `CANNON_INTERVAL` |
| 565 | `boss.phases.attacks.charged_cannon.bullet_speed` | `CANNON_BULLET_SPEED` |
| 566 | `boss.phases.attacks.charged_cannon.damage` | `CANNON_DAMAGE` |
| 567 | `boss.phases.attacks.charged_cannon.flash` | `CANNON_FLASH` |
| 568 | `boss.phases.attacks.dash_sweep.aim` | `SWEEP_AIM` |
| 569 | `boss.phases.attacks.dash_sweep.speed` | `SWEEP_SPEED` |
| 570 | `boss.phases.attacks.dash_sweep.drop_count` | `SWEEP_DROP_COUNT` |
| 571 | `boss.phases.attacks.dash_sweep.drop_speed` | `SWEEP_DROP_SPEED` |
| 572 | `boss.phases.attacks.dash_sweep.drop_damage` | `SWEEP_DROP_DAMAGE` |
| 573 | `boss.phases.attacks.dash_sweep.return_duration` | `SWEEP_RETURN_DURATION` |
| 574 | `boss.phases.attacks.minion_volley.count` | `VOLLEY_COUNT` |
| 575 | `boss.phases.attacks.minion_volley.delay` | `VOLLEY_DELAY` |
| 576 | `boss.phases.attacks.minion_volley.bullet_speed` | `VOLLEY_BULLET_SPEED` |
| 577 | `boss.phases.attacks.minion_volley.bullet_damage` | `VOLLEY_BULLET_DAMAGE` |
| 578 | `boss.phases.attacks.bullet_wall.count` | `WALL_COUNT` |
| 579 | `boss.phases.attacks.bullet_wall.bullet_speed` | `WALL_BULLET_SPEED` |
| 580 | `boss.phases.attacks.bullet_wall.damage` | `WALL_DAMAGE` |
| 581 | `boss.phases.attacks.bullet_wall.arc_deg` | `WALL_ARC_DEG` |
| 584 | `boss.enrage.type_1.ring_interval` | `E1_RING_INTERVAL` |
| 585 | `boss.enrage.type_1.ring_count` | `E1_RING_COUNT` |
| 586 | `boss.enrage.type_1.ring_speed` | `E1_RING_SPEED` |
| 587 | `boss.enrage.type_1.ring_precession_deg` | `E1_RING_PRECESSION_DEG` |
| 588 | `boss.enrage.type_1.salvo_charge` | `E1_SALVO_CHARGE` |
| 589 | `boss.enrage.type_1.salvo_count` | `E1_SALVO_COUNT` |
| 590 | `boss.enrage.type_1.salvo_speed` | `E1_SALVO_SPEED` |
| 591 | `boss.enrage.type_1.salvo_damage` | `E1_SALVO_DAMAGE` |
| 592 | `boss.enrage.type_2.point_count` | `E2_POINT_COUNT` |
| 593 | `boss.enrage.type_2.point_interval` | `E2_POINT_INTERVAL` |
| 594 | `boss.enrage.type_2.aim` | `E2_AIM` |
| 595 | `boss.enrage.type_2.sniper_speed` | `E2_SNIPER_SPEED` |
| 596 | `boss.enrage.type_2.sniper_damage` | `E2_SNIPER_DAMAGE` |
| 597 | `boss.enrage.type_2.release_ring_count` | `E2_RELEASE_RING_COUNT` |
| 598 | `boss.enrage.type_2.release_ring_speed` | `E2_RELEASE_RING_SPEED` |
| 599 | `boss.enrage.type_3.summon_interval` | `E3_SUMMON_INTERVAL` |
| 601 | `boss.phases.type3.summon_interval` | `_summon_interval` |
| 603 | `boss.enrage.type_3.summon_waves` | `E3_SUMMON_WAVES` |
| 604 | `boss.enrage.type_3.summon_count` | `E3_SUMMON_COUNT` |
| 605 | `boss.enrage.type_3.ring_interval` | `E3_RING_INTERVAL` |
| 606 | `boss.enrage.type_3.ring_count` | `E3_RING_COUNT` |
| 607 | `boss.enrage.type_3.ring_speed` | `E3_RING_SPEED` |
| 608 | `boss.enrage.type_3.release_ring_count` | `E3_RELEASE_RING_COUNT` |
| 609 | `boss.enrage.type_3.release_ring_speed` | `E3_RELEASE_RING_SPEED` |
| 611 | `boss.ring_burst.bullet_speed` | `RING_BURST_SPEED` |
| 612 | `boss.bullet_damage.ring` | `BULLET_DAMAGE_RING` |
| 613 | `boss.movement.type4.bob_amp` | `MOVE4_BOB_AMP` |
| 614 | `boss.movement.type4.bob_period` | `MOVE4_BOB_PERIOD` |
| 615 | `boss.enrage.type_4.ring_count` | `E4_RING_COUNT` |
| 616 | `boss.enrage.type_4.ring_interval` | `E4_RING_INTERVAL` |
| 617 | `boss.enrage.type_4.ring_speed` | `E4_RING_SPEED` |
| 618 | `boss.enrage.type_4.precession_deg` | `E4_PRECESSION_DEG` |
| 619 | `boss.enrage.type_4.release_ring_count` | `E4_RELEASE_RING_COUNT` |
| 620 | `boss.enrage.type_4.release_ring_speed` | `E4_RELEASE_RING_SPEED` |
| 622 | `boss.difficulty_scaling.interval_mult` | `DIFF_INTERVAL_MULT` |
| 623 | `boss.difficulty_scaling.speed_mult` | `DIFF_SPEED_MULT` |
| 624 | `boss.difficulty_scaling.counts` | `DIFF_COUNT_DELTAS` |
| 893 | `effects.shake.enrage` | `16.0` |
| 1047 | `effects.shake.enrage` | `16.0` |

### `scripts/buff_select.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 69 | `buffs.explosive.unlock_boss_kills` | `3` |
| 236 | `buffs.extra_life.heal_on_pick` | `30` |
| 272 | `buffs.extra_life.heal_on_pick` | `30` |

### `scripts/bullet.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 226 | `buffs.explosive.radius_per_level` | `EXPLOSIVE_RADIUS` |
| 227 | `buffs.explosive.damage_per_level` | `EXPLOSIVE_DAMAGE` |
| 228 | `effects.bullet_visual_scale` | `VISUAL_SCALE` |
| 229 | `effects.enemy_bullet_visual_scale` | `ENEMY_VISUAL_SCALE` |
| 231 | `player.grace_period` | `GRACE_PERIOD` |
| 233 | `player.parry.reflect_speed_mult` | `REFLECT_SPEED_MULT` |
| 234 | `player.parry.reflect_damage_mult` | `REFLECT_DAMAGE_MULT` |

### `scripts/camera_shake.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 14 | `effects.shake.decay` | `DECAY` |

### `scripts/direction_shift_event.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 18 | `fog_events.direction_shift.shift_interval` | `_interval` |
| 19 | `fog_events.direction_shift.hold_time` | `_hold` |

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
| 121 | `elite_turret_event.weak_lock` | `WEAK_LOCK` |
| 124 | `elite_turret_event.reward_score` | `REWARD_SCORE` |
| 125 | `elite_turret_event.carrier.hover_y` | `HOVER_Y` |
| 126 | `elite_turret_event.cooldown` | `COOLDOWN` |
| 172 | `elite_turret_event.carrier.shake` | `4.0` |

### `scripts/enemy.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 145 | `enemies.hp_ramp_factor` | `HP_RAMP_FACTOR` |
| 160 | `enemies.speed_ramp_factor` | `SPEED_RAMP_FACTOR` |
| 168 | `player.aim_assist.mark_ratio` | `0.25` |
| 247 | `enemies.bullet_speed` | `ENEMY_BULLET_SPEED` |
| 248 | `enemies.spread_bullet_speed` | `SPREAD_BULLET_SPEED` |
| 249 | `enemies.laser_bullet_speed` | `LASER_BULLET_SPEED` |
| 250 | `enemies.bullet_damage.single` | `BULLET_DAMAGE_SINGLE` |
| 251 | `enemies.bullet_damage.spread` | `BULLET_DAMAGE_SPREAD` |
| 252 | `enemies.bullet_damage.laser` | `BULLET_DAMAGE_LASER` |
| 253 | `enemies.collision_damage` | `COLLISION_DAMAGE` |
| 254 | `buffs.slow_field.factor` | `SLOW_FIELD_FACTOR` |
| 255 | `enemies.spread_fan_step` | `SPREAD_FAN_STEP` |
| 256 | `enemies.lifetime` | `LIFETIME` |
| 257 | `enemies.exit_accel` | `EXIT_ACCEL` |
| 258 | `enemies.aggressive_chase_speed` | `AGGR_CHASE_SPEED` |
| 259 | `enemies.fire_interval` | `FIRE_INTERVAL` |
| 261 | `enemies.hover_band` | `[HOVER_BAND.x, HOVER_BAND.y]` |
| 266 | `enemies.hover_bob_amp` | `HOVER_BOB_AMP` |
| 267 | `enemies.hover_bob_freq` | `HOVER_BOB_FREQ` |
| 268 | `enemies.hover_sway_amp` | `HOVER_SWAY_AMP` |
| 269 | `enemies.hover_sway_freq` | `HOVER_SWAY_FREQ` |
| 270 | `enemies.spiral_drift_amp` | `SPIRAL_DRIFT_AMP` |
| 271 | `enemies.spiral_drift_freq` | `SPIRAL_DRIFT_FREQ` |
| 272 | `enemies.spiral_radius` | `SPIRAL_RADIUS` |
| 289 | `effects.shake.enemy_die` | `_shake_die_normal` |
| 290 | `effects.shake.elite_die` | `_shake_die_elite` |
| 319 | `enemies.move_strategies` | `{}` |

### `scripts/event_manager.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 99 | `fog_events.enabled` | `FOG_ENABLED` |
| 100 | `fog_events.trigger_chance` | `FOG_TRIGGER_CHANCE` |
| 101 | `fog_events.check_interval` | `FOG_CHECK_INTERVAL` |
| 102 | `fog_events.min_interval` | `FOG_MIN_INTERVAL` |
| 103 | `fog_events.first_delay` | `FOG_FIRST_DELAY` |
| 104 | `fog_events.weights` | `FOG_WEIGHTS` |
| 107 | `fog_events.durations` | `FOG_EVENT_DURATIONS` |
| 114 | `elite_turret_event.trigger_interval` | `45.0` |
| 115 | `elite_turret_event.trigger_chance` | `0.35` |
| 116 | `elite_turret_event.min_score` | `800` |
| 120 | `formation_strike_event.trigger_interval` | `40.0` |
| 121 | `formation_strike_event.trigger_chance` | `0.30` |
| 122 | `formation_strike_event.min_score` | `500` |

### `scripts/explosion.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 53 | `effects.explosion.pool_cap` | `POOL_CAP` |
| 61 | `effects.explosion_visual_scale` | `1.6` |
| 80 | `effects.shake.boss_seq_initial` | `20.0` |
| 93 | `effects.shake.boss_seq_step` | `8.0` |
| 104 | `effects.shake.boss_seq_final` | `24.0` |
| 112 | `effects.explosion.amount` | `24` |
| 132 | `effects.explosion.debris_amount` | `10` |

### `scripts/fake_enemies_event.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 23 | `fog_events.fake_enemies.count` | `_count` |
| 24 | `fog_events.fake_enemies.spawn_interval` | `_spawn_interval` |

### `scripts/formation_bomb.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 98 | `effects.shake.enemy_die` | `5.0` |

### `scripts/formation_craft.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 46 | `effects.shake.enemy_die` | `_shake_die` |

### `scripts/formation_strike_event.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 66 | `formation_strike_event.min_score` | `MIN_SCORE` |
| 67 | `formation_strike_event.cooldown` | `COOLDOWN` |
| 70 | `formation_strike_event.craft_counts` | `CRAFT_COUNTS` |
| 73 | `formation_strike_event.craft_hp_base` | `CRAFT_HP_BASE` |
| 74 | `formation_strike_event.craft_score` | `CRAFT_SCORE` |
| 77 | `formation_strike_event.approach_speed` | `APPROACH_SPEED` |
| 78 | `formation_strike_event.approach_y` | `APPROACH_Y` |
| 79 | `formation_strike_event.turn_time` | `TURN_TIME` |
| 80 | `formation_strike_event.run_speed` | `RUN_SPEED` |
| 81 | `formation_strike_event.bomb_interval` | `BOMB_INTERVAL` |
| 82 | `formation_strike_event.bombs_per_craft` | `BOMBS_PER_CRAFT` |
| 83 | `formation_strike_event.bomb_fall_speed` | `BOMB_FALL_SPEED` |
| 84 | `formation_strike_event.bomb_fuse` | `BOMB_FUSE` |
| 85 | `formation_strike_event.bomb_damage` | `BOMB_DAMAGE` |
| 86 | `formation_strike_event.bomb_radius` | `BOMB_RADIUS` |
| 87 | `formation_strike_event.reward_all_clear` | `REWARD_ALL_CLEAR` |

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
| 74 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 75 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 76 | `effects.give_up_hold_time` | `GIVE_UP_HOLD_TIME` |
| 77 | `boss.enrage.slow_scale` | `ENRAGE_SLOW_SCALE` |
| 78 | `boss.enrage.bullet_time` | `ENRAGE_BULLET_TIME` |
| 79 | `boss.enrage.ramp_time` | `ENRAGE_RAMP_TIME` |
| 688 | `effects.mothership_summon.shake_gate` | `6.0` |
| 699 | `buffs.mothership_recall.cooldown_factor` | `0.5` |
| 741 | `mothership.depart_cooldown` | `60.0` |

### `scripts/meta_health_fx.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 158 | `effects.meta_health.crack.density` | `DENSITY_CAPS.duplicate(` |
| 169 | `effects.meta_health.lod` | `0` |
| 170 | `effects.meta_health.pulse.scale` | `2.5` |
| 171 | `effects.meta_health.pulse.min` | `0.15` |
| 172 | `effects.meta_health.pulse.decay_tau` | `0.09` |
| 173 | `effects.meta_health.chromatic.base` | `0.006` |
| 174 | `effects.meta_health.chromatic.peak` | `0.014` |
| 175 | `effects.meta_health.blur.strength` | `0.6` |
| 176 | `effects.meta_health.ripple.duration` | `0.4` |
| 177 | `effects.meta_health.ripple.alpha` | `0.8` |
| 178 | `effects.meta_health.crack.exponent` | `1.6` |
| 179 | `effects.meta_health.crack.spread_min` | `0.10` |
| 180 | `effects.meta_health.crack.edge_softness` | `0.08` |
| 181 | `effects.meta_health.crack.width` | `0.03` |
| 182 | `effects.meta_health.crack.glow` | `0.8` |
| 183 | `effects.meta_health.crack.heal_jitter` | `0.35` |
| 184 | `effects.meta_health.crack.grow_overshoot` | `0.08` |
| 185 | `effects.meta_health.crack.grow_time` | `0.6` |
| 187 | `effects.meta_health.desat.max` | `0.35` |
| 188 | `effects.meta_health.desat.exponent` | `2.0` |
| 189 | `effects.meta_health.vignette.max_alpha` | `0.5` |
| 190 | `effects.meta_health.vignette.inner` | `0.62` |
| 191 | `effects.meta_health.vignette.dying_shrink` | `0.06` |
| 192 | `effects.meta_health.dying.threshold` | `0.2` |
| 193 | `effects.meta_health.dying.heart_min_hz` | `1.0` |
| 194 | `effects.meta_health.dying.heart_max_hz` | `1.2` |
| 195 | `effects.meta_health.dying.breath` | `0.015` |
| 196 | `effects.meta_health.dying.jitter_px` | `2.0` |
| 197 | `effects.meta_health.dying.warn_hz` | `2.5` |
| 198 | `effects.meta_health.dying.fade` | `0.3` |
| 199 | `effects.meta_health.smooth.down_tau` | `0.10` |
| 200 | `effects.meta_health.smooth.up_tau` | `0.80` |
| 201 | `effects.meta_health.adapt.interval` | `0.25` |
| 202 | `effects.meta_health.adapt.min` | `0.8` |
| 203 | `effects.meta_health.adapt.max` | `1.3` |
| 204 | `effects.meta_health.adapt.bullet_weight` | `0.002` |
| 205 | `effects.meta_health.adapt.explosion_weight` | `0.15` |
| 206 | `effects.meta_health.reduce_flash.chromatic_scale` | `0.4` |

### `scripts/mothership.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 128 | `mothership.hover_y` | `HOVER_Y` |
| 129 | `mothership.release_invincible` | `RELEASE_INVINCIBLE` |
| 130 | `mothership.dock_tween_time` | `DOCK_TWEEN_TIME` |
| 131 | `mothership.dock_offset_y` | `DOCK_OFFSET_Y` |
| 132 | `mothership.resupply_delay` | `RESUPPLY_DELAY` |
| 133 | `mothership.release_time` | `RELEASE_TIME` |
| 134 | `mothership.release_drop` | `RELEASE_DROP` |
| 135 | `mothership.mag_cells` | `MAG_CELLS` |
| 136 | `mothership.mag_cell_time` | `MAG_CELL_TIME` |
| 137 | `mothership.mag_warn_cells` | `MAG_WARN_CELLS` |
| 138 | `mothership.warn_eject_delay` | `WARN_EJECT_DELAY` |
| 139 | `mothership.early_hold_time` | `EARLY_HOLD_TIME` |
| 140 | `mothership.early_max_discount` | `EARLY_MAX_DISCOUNT` |
| 141 | `mothership.early_prefill_max` | `EARLY_PREFILL_MAX` |
| 142 | `mothership.early_prefill_ratio` | `EARLY_PREFILL_RATIO` |
| 143 | `mothership.depart_cooldown` | `DEPART_COOLDOWN` |
| 144 | `mothership.depart_start_speed` | `DEPART_START_SPEED` |
| 145 | `mothership.depart_accel` | `DEPART_ACCEL` |
| 146 | `mothership.drive.accel` | `DRIVE_ACCEL` |
| 147 | `mothership.drive.max_speed` | `DRIVE_MAX_SPEED` |
| 151 | `mothership.drive.margin_x` | `DRIVE_MARGIN_X` |
| 152 | `mothership.drive.margin_top` | `DRIVE_MARGIN_TOP` |
| 153 | `mothership.drive.margin_bottom` | `DRIVE_MARGIN_BOTTOM` |
| 155 | `mothership.upgrade.threshold` | `_upgrade_threshold` |
| 156 | `mothership.upgrade.damage_mult` | `_upgrade_damage_mult` |
| 157 | `mothership.upgrade.interval_mult` | `_upgrade_interval_mult` |
| 158 | `mothership.gatling.interval` | `GATLING_INTERVAL` |
| 159 | `mothership.gatling.bullet_speed` | `GATLING_BULLET_SPEED` |
| 160 | `mothership.gatling.damage` | `GATLING_DAMAGE` |
| 161 | `mothership.gatling.score_scale` | `GATLING_SCORE_SCALE` |
| 162 | `mothership.gatling.sweep_left_min` | `GATLING_SWEEP_LEFT_MIN` |
| 163 | `mothership.gatling.sweep_left_max` | `GATLING_SWEEP_LEFT_MAX` |
| 164 | `mothership.gatling.sweep_right_min` | `GATLING_SWEEP_RIGHT_MIN` |
| 165 | `mothership.gatling.sweep_right_max` | `GATLING_SWEEP_RIGHT_MAX` |
| 166 | `mothership.gatling.sweep_left_period` | `GATLING_SWEEP_LEFT_PERIOD` |
| 167 | `mothership.gatling.sweep_right_period` | `GATLING_SWEEP_RIGHT_PERIOD` |
| 168 | `mothership.gatling.sweep_right_phase` | `GATLING_SWEEP_RIGHT_PHASE` |
| 169 | `mothership.missile.interval` | `MISSILE_INTERVAL` |
| 170 | `mothership.missile.damage` | `MISSILE_DAMAGE` |
| 171 | `mothership.missile.speed` | `MISSILE_SPEED` |
| 172 | `mothership.missile.target_count` | `MISSILE_TARGET_COUNT` |
| 173 | `mothership.missile.splash_damage` | `MISSILE_SPLASH_DAMAGE` |
| 174 | `mothership.missile.splash_radius` | `MISSILE_SPLASH_RADIUS` |
| 175 | `effects.mothership_summon.warp_in_time` | `WARP_IN_TIME` |
| 176 | `effects.mothership_summon.warp_in_drop` | `WARP_IN_DROP` |
| 177 | `effects.mothership_summon.slow.radius` | `SLOW_RADIUS` |
| 178 | `effects.mothership_summon.slow.duration` | `SLOW_DURATION` |
| 179 | `effects.mothership_summon.slow.factor` | `SLOW_FACTOR` |
| 180 | `effects.mothership_summon.slow.ring_time` | `SLOW_RING_TIME` |
| 181 | `effects.mothership_summon.shake_slow` | `SHAKE_SLOW` |
| 311 | `effects.mothership_summon.warp_in_drop` | `WARP_IN_DROP` |
| 739 | `effects.shake.mothership` | `4.0` |

### `scripts/mothership_summon_window.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 50 | `effects.mothership_summon.window.open_time` | `OPEN_TIME` |
| 51 | `effects.mothership_summon.window.close_time` | `CLOSE_TIME` |
| 53 | `effects.mothership_summon.window.shot_durations` | `_shot_durations` |

### `scripts/orbital_strike.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 50 | `effects.orbital_strike.duration` | `DURATION` |
| 51 | `effects.orbital_strike.impact_at` | `IMPACT_AT` |
| 52 | `effects.orbital_strike.missile_from` | `MISSILE_FROM` |
| 53 | `effects.orbital_strike.reticle_radius` | `RETICLE_RADIUS` |
| 54 | `effects.orbital_strike.impact_y_ratio` | `IMPACT_Y_RATIO` |
| 73 | `effects.shake.boss_seq_final` | `24.0` |
| 83 | `effects.shake.boss_seq_final` | `24.0` |

### `scripts/player.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 251 | `player.max_speed` | `MAX_SPEED` |
| 252 | `player.accel` | `ACCEL` |
| 253 | `player.decel` | `DECEL` |
| 254 | `player.boost_mult` | `BOOST_MULT` |
| 255 | `player.fine_move_mult` | `FINE_MOVE_MULT` |
| 256 | `player.base_fire_interval` | `BASE_FIRE_INTERVAL` |
| 257 | `player.bullet_speed` | `BULLET_SPEED` |
| 258 | `buffs.crit_shot.chance` | `CRIT_CHANCE_BASE` |
| 259 | `buffs.crit_shot.multiplier` | `CRIT_MULTIPLIER` |
| 260 | `player.bullet_spread_deg` | `BULLET_SPREAD_DEG` |
| 261 | `player.bullet_damage` | `BULLET_DAMAGE` |
| 262 | `player.invincible_time` | `INVINCIBLE_TIME` |
| 263 | `player.spawn_invincible_time` | `SPAWN_INVINCIBLE_TIME` |
| 264 | `player.bullet_clear_radius` | `BULLET_CLEAR_RADIUS` |
| 265 | `player.entry.land_ratio` | `ENTRY_LAND_RATIO` |
| 266 | `player.entry.rush_time` | `ENTRY_RUSH_TIME` |
| 267 | `player.entry.retreat_speed` | `ENTRY_RETREAT_SPEED` |
| 268 | `player.entry.retreat_time` | `ENTRY_RETREAT_TIME` |
| 269 | `player.entry.invincible` | `ENTRY_INVINCIBLE` |
| 270 | `player.entry.spawn_clearance` | `ENTRY_SPAWN_CLEARANCE` |
| 271 | `player.entry.rush_hspeed_ratio` | `ENTRY_RUSH_HS_RATIO` |
| 272 | `buffs.armor.multiplier` | `ARMOR_MULT` |
| 273 | `buffs.evasion.chance` | `EVASION_CHANCE` |
| 274 | `buffs.regen.heal_per_sec` | `REGEN_PER_SEC` |
| 275 | `effects.shake.player_hit` | `SHAKE_HIT` |
| 277 | `player.fuel.max` | `fuel_max` |
| 279 | `player.fuel.drain` | `FUEL_DRAIN` |
| 280 | `player.fuel.regen` | `FUEL_REGEN` |
| 281 | `player.fuel.restart` | `FUEL_RESTART` |
| 282 | `player.dash.distance` | `DASH_DISTANCE` |
| 283 | `player.dash.time` | `DASH_TIME` |
| 284 | `player.dash.cooldown` | `DASH_COOLDOWN` |
| 285 | `player.dash.fuel_ratio` | `DASH_FUEL_RATIO` |
| 286 | `player.dash.afterimage_interval` | `AFTERIMAGE_INTERVAL` |
| 288 | `player.graze_radius` | `GRAZE_RADIUS` |
| 289 | `player.graze_score` | `GRAZE_SCORE` |
| 291 | `player.parry.arc_deg` | `PARRY_ARC_DEG` |
| 292 | `player.parry.radius` | `PARRY_RADIUS` |
| 294 | `player.parry.duration` | `0.8` |
| 295 | `player.parry.active_time` | `0.5` |
| 296 | `player.parry.cooldown` | `3.0` |
| 302 | `player.aim_assist.input.magnet_input_min` | `_magnet_input_min` |
| 303 | `player.aim_assist.input.magnet_input_full` | `_magnet_input_full` |
| 304 | `player.aim_assist.falloff.peak` | `_falloff_peak` |
| 305 | `player.aim_assist.falloff.end` | `_falloff_end` |
| 306 | `player.aim_assist.falloff.min` | `_falloff_min` |
| 473 | `fog_events.bullet_malfunction.jitter_deg` | `20.0` |
| 474 | `fog_events.bullet_malfunction.misfire_chance` | `0.15` |
| 475 | `fog_events.bullet_malfunction.interval_jitter` | `0.3` |
| 833 | `player.aim_assist.homing_time` | `HOMING_TIME` |
| 55 | `buffs.rapid_fire.factor` | `—` |
| 56 | `buffs.power_shot.factor` | `—` |
| 57 | `buffs.efficient_boost.factor` | `—` |
| 58 | `buffs.boost_recovery.factor` | `—` |
| 59 | `player.dash.cooldown_stack_factor` | `—` |
| 60 | `buffs.spread_shot.max_stacks` | `—` |
| 61 | `buffs.piercing.max_stacks` | `—` |
| 63 | `buffs.bullet_speed.factor` | `—` |

### `scripts/return_cinematic.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 62 | `effects.return_skip_grace` | `SKIP_GRACE` |

### `scripts/spawner.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 203 | `spawner.wave_interval_start` | `WAVE_INTERVAL_START` |
| 204 | `spawner.wave_interval_end` | `WAVE_INTERVAL_END` |
| 205 | `spawner.ramp_time` | `RAMP_TIME` |
| 206 | `spawner.interval_min` | `INTERVAL_MIN` |
| 207 | `spawner.boss_score_step` | `BOSS_SCORE_STEP` |
| 208 | `spawner.boss_min_interval` | `BOSS_MIN_INTERVAL` |
| 209 | `spawner.boss_time_limit` | `BOSS_TIME_LIMIT` |
| 210 | `spawner.difficulty_factor` | `DIFFICULTY_FACTOR` |
| 212 | `spawner.unlock_scores` | `UNLOCK_SCORES` |
| 221 | `spawner.wave_size_start` | `WAVE_SIZE_START` |
| 222 | `spawner.wave_size_end` | `WAVE_SIZE_END` |
| 223 | `spawner.special_gap_min` | `SPECIAL_GAP_MIN` |
| 224 | `spawner.special_gap_max` | `SPECIAL_GAP_MAX` |
| 225 | `spawner.rest_waves_after_kill` | `REST_WAVES_AFTER_KILL` |
| 226 | `spawner.elite_wave_size` | `ELITE_WAVE_SIZE` |
| 228 | `enemies.hover_band` | `[_hover_band.x, _hover_band.y]` |
| 233 | `enemies.types` | `[]` |
| 236 | `elites.types` | `[]` |
| 545 | `spawner.telegraph_duration` | `SpawnTelegraph.DURATION` |
| 589 | `effects.shake.boss_warning` | `14.0` |

### `scripts/starfield.gd`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 44 | `effects.starfield.far_count` | `FAR_COUNT` |
| 47 | `effects.starfield.near_count` | `NEAR_COUNT` |
| 50 | `effects.starfield.far_speed` | `FAR_SPEED` |
| 51 | `effects.starfield.near_speed` | `NEAR_SPEED` |

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
| 141 | `world_scale` | `world_scale` |
| 144 | `milestones.base` | `MILESTONE_BASE.duplicate(` |
| 156 | `milestones.cycle_mult` | `MILESTONE_CYCLE_MULT` |
| 158 | `progression.per_boss_kill` | `0.6` |
| 159 | `progression.per_ten_minutes` | `1.5` |
| 160 | `progression.time_step_seconds` | `30.0` |
| 163 | `difficulty` | `{}` |
| 169 | `dda.duration` | `DDA_DURATION` |
| 170 | `dda.factor` | `DDA_FACTOR` |
| 171 | `player.max_health` | `_max_hp_base` |
| 173 | `buffs.extra_life.max_hp_bonus` | `_max_hp_bonus` |
| 175 | `buffs.lifesteal.max_hp_fraction` | `0.1` |
| 177 | `base_task.refresh_cost` | `REFRESH_COST` |
| 178 | `base_task.grant_per_visit` | `GRANT_PER_VISIT` |
| 957 | `milestones.boss_kill_base` | `500.0` |
| 1785 | `player.aim_assist.joy_speed` | `joy_aim_speed` |

## 动态拼接键前缀

- `boss.phases.type…`
- `buffs.…`
- `player.aim_assist.levels.…`

## json 中存在但脚本未静态引用的键

（经动态键或整段读取覆盖的不列出；剩下的请人工判断是否为死键）

- `version`

## 脚本引用但 json 缺失的键（走回退值，建议补进 json 或确认为有意兜底）

