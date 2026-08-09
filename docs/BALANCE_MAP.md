# BALANCE_MAP — 数值位置地图

> 本文件由 `python3 scripts/tools/gen_balance_map.py` 扫描生成，请勿手改；
> 新增/改名数值键或调整 cfg() 调用后重新运行生成器。

## 怎么改数值

- 运行时数值的唯一来源是 `data/balance.json`；推荐用 `python3 scripts/tools/balance_editor.py` 在浏览器里编辑（改动高亮、类型校验、自动备份）。
- 脚本侧的 `GameState.cfg("键路径", 回退值)` 仅在 json 缺键/损坏时兜底；新增或调整数值按 AGENTS.md 约定保持 json 与回退值一致。
- 高频 `_process` 路径的数值在 `_ready()`/`_load_balance()` 一次缓存，不要每帧查。

## 静态 cfg() 调用点（按文件分组）

### `csharp/godot/AimFrameLayer.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 90 | `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| 91 | `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| 92 | `player.aim_assist.falloff.peak` | `_falloffPeak` |
| 93 | `player.aim_assist.falloff.end` | `_falloffEnd` |
| 94 | `player.aim_assist.falloff.min` | `_falloffMin` |

### `csharp/godot/Boss.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 395 | `boss.enter_speed` | `EnterSpeed` |
| 396 | `boss.fight_y` | `FightY` |
| 397 | `boss.strafe_min_x` | `StrafeMinX` |
| 398 | `boss.strafe_max_x` | `StrafeMaxX` |
| 399 | `boss.phase2_hp_ratio` | `Phase2HpRatio` |
| 400 | `boss.enrage.hp_ratio` | `EnrageHpRatio` |
| 401 | `boss.enrage.rate_mult` | `EnrageRateMult` |
| 402 | `boss.enrage.speed_mult` | `EnrageSpeedMult` |
| 403 | `boss.enrage.player_slow` | `EnragePlayerSlow` |
| 404 | `boss.enrage.snapshot_lasers` | `EnrageSnapshotLasers` |
| 405 | `boss.enrage.snapshot_ring` | `EnrageSnapshotRing` |
| 406 | `boss.enrage.laser_speed` | `EnrageLaserSpeed` |
| 407 | `boss.enrage.ring_speed` | `EnrageRingSpeed` |
| 408 | `boss.enrage.duration` | `EnrageDuration` |
| 409 | `boss.enrage.transition_duration` | `EnrageTransitionDuration` |
| 410 | `boss.enrage.attack_interval` | `EnrageAttackInterval` |
| 411 | `boss.enrage.attack_windup` | `EnrageAttackWindup` |
| 412 | `boss.enrage.release_interval` | `EnrageReleaseInterval` |
| 413 | `boss.enrage.release_hold_duration` | `EnrageReleaseHoldDuration` |
| 414 | `boss.enrage.return_duration` | `EnrageReturnDuration` |
| 415 | `boss.enrage.path_radius_scale` | `EnragePathRadiusScale` |
| 418 | `boss.enrage.square_path_ratio` | `EnrageSquarePathRatio` |
| 419 | `boss.enrage.release_laser_speed` | `EnrageReleaseLaserSpeed` |
| 420 | `boss.enrage.release_ring_speed` | `EnrageReleaseRingSpeed` |
| 422 | `boss.escape.time` | `EscapeTime` |
| 423 | `boss.escape.warning` | `EscapeWarning` |
| 424 | `boss.escape.drift` | `EscapeDrift` |
| 425 | `boss.escape.start_speed` | `EscapeStartSpeed` |
| 426 | `boss.escape.accel` | `EscapeAccel` |
| 438 | `boss.escape.countdown_visible_from` | `EscapeCountdownFrom` |
| 439 | `boss.hp_base` | `HpBase` |
| 441 | `boss.strafe_speeds` | `StrafeSpeeds` |
| 455 | `boss.fire_intervals` | `FireIntervals` |
| 459 | `boss.fan_bullet_speed` | `FanBulletSpeed` |
| 460 | `boss.homing_bullet_speed` | `HomingBulletSpeed` |
| 461 | `boss.sniper_bullet_speed` | `SniperBulletSpeed` |
| 462 | `boss.cross_bullet_speed` | `CrossBulletSpeed` |
| 463 | `boss.collision_damage` | `CollisionDamage` |
| 464 | `buffs.slow_field.factor` | `SlowFieldFactor` |
| 465 | `boss.bullet_damage.fan` | `BulletDamageFan` |
| 466 | `boss.bullet_damage.homing` | `BulletDamageHoming` |
| 467 | `boss.bullet_damage.sniper` | `BulletDamageSniper` |
| 468 | `boss.bullet_damage.cross` | `BulletDamageCross` |
| 469 | `boss.bullet_damage.snapshot_laser` | `BulletDamageSnapshotLaser` |
| 470 | `boss.bullet_damage.snapshot_ring` | `BulletDamageSnapshotRing` |
| 471 | `boss.phases.phase_shift_duration` | `PhaseShiftDuration` |
| 472 | `boss.phases.clear_on_shift` | `ClearOnShift` |
| 473 | `boss.phases.transition_invincible` | `TransitionInvincible` |
| 474 | `boss.phases.telegraph.sniper_aim` | `SniperAimTime` |
| 475 | `boss.phases.telegraph.sniper_track` | `SniperTrackTime` |
| 476 | `boss.phases.attacks.sniper3.burst_interval` | `SniperBurstInterval` |
| 477 | `boss.phases.press_interval` | `PressInterval` |
| 478 | `boss.phases.press_depth` | `PressDepth` |
| 479 | `boss.movement.type1_p2_strafe` | `Type1P2Strafe` |
| 480 | `boss.movement.type1_p2_bob_amp` | `Type1P2BobAmp` |
| 481 | `boss.movement.type1_p2_bob_period` | `Type1P2BobPeriod` |
| 482 | `boss.movement.type2_p2_dash_time` | `Type2P2DashTime` |
| 483 | `boss.movement.type2_p2_rest_time` | `Type2P2RestTime` |
| 484 | `boss.movement.type3_p1_bob_min` | `Type3P1BobMin` |
| 485 | `boss.movement.type3_p1_bob_max` | `Type3P1BobMax` |
| 486 | `boss.movement.type3_p1_bob_period` | `Type3P1BobPeriod` |
| 487 | `boss.movement.type3_p2_strafe` | `Type3P2Strafe` |
| 488 | `boss.movement.type3_p2_bob_amp` | `Type3P2BobAmp` |
| 489 | `boss.movement.type3_p2_bob_period` | `Type3P2BobPeriod` |
| 491 | `boss.phases.attacks.charged_cannon.charge` | `CannonCharge` |
| 492 | `boss.phases.attacks.charged_cannon.shots` | `CannonShots` |
| 493 | `boss.phases.attacks.charged_cannon.interval` | `CannonInterval` |
| 494 | `boss.phases.attacks.charged_cannon.bullet_speed` | `CannonBulletSpeed` |
| 495 | `boss.phases.attacks.charged_cannon.damage` | `CannonDamage` |
| 496 | `boss.phases.attacks.charged_cannon.flash` | `CannonFlash` |
| 497 | `boss.phases.attacks.dash_sweep.aim` | `SweepAim` |
| 498 | `boss.phases.attacks.dash_sweep.speed` | `SweepSpeed` |
| 499 | `boss.phases.attacks.dash_sweep.drop_count` | `SweepDropCount` |
| 500 | `boss.phases.attacks.dash_sweep.drop_speed` | `SweepDropSpeed` |
| 501 | `boss.phases.attacks.dash_sweep.drop_damage` | `SweepDropDamage` |
| 502 | `boss.phases.attacks.dash_sweep.return_duration` | `SweepReturnDuration` |
| 503 | `boss.phases.attacks.minion_volley.count` | `VolleyCount` |
| 504 | `boss.phases.attacks.minion_volley.delay` | `VolleyDelay` |
| 505 | `boss.phases.attacks.minion_volley.bullet_speed` | `VolleyBulletSpeed` |
| 506 | `boss.phases.attacks.minion_volley.bullet_damage` | `VolleyBulletDamage` |
| 507 | `boss.phases.attacks.bullet_wall.count` | `WallCount` |
| 508 | `boss.phases.attacks.bullet_wall.bullet_speed` | `WallBulletSpeed` |
| 509 | `boss.phases.attacks.bullet_wall.damage` | `WallDamage` |
| 510 | `boss.phases.attacks.bullet_wall.arc_deg` | `WallArcDeg` |
| 514 | `boss.enrage.type_1.ring_interval` | `E1RingInterval` |
| 515 | `boss.enrage.type_1.ring_count` | `E1RingCount` |
| 516 | `boss.enrage.type_1.ring_speed` | `E1RingSpeed` |
| 517 | `boss.enrage.type_1.ring_precession_deg` | `E1RingPrecessionDeg` |
| 519 | `boss.enrage.type_1.salvo_charge` | `E1SalvoCharge` |
| 520 | `boss.enrage.type_1.salvo_count` | `E1SalvoCount` |
| 521 | `boss.enrage.type_1.salvo_speed` | `E1SalvoSpeed` |
| 522 | `boss.enrage.type_1.salvo_damage` | `E1SalvoDamage` |
| 523 | `boss.enrage.type_2.point_count` | `E2PointCount` |
| 525 | `boss.enrage.type_2.point_interval` | `E2PointInterval` |
| 526 | `boss.enrage.type_2.aim` | `E2Aim` |
| 527 | `boss.enrage.type_2.sniper_speed` | `E2SniperSpeed` |
| 528 | `boss.enrage.type_2.sniper_damage` | `E2SniperDamage` |
| 529 | `boss.enrage.type_2.release_ring_count` | `E2ReleaseRingCount` |
| 530 | `boss.enrage.type_2.release_ring_speed` | `E2ReleaseRingSpeed` |
| 532 | `boss.enrage.type_3.summon_interval` | `E3SummonInterval` |
| 534 | `boss.phases.type3.summon_interval` | `_summonInterval` |
| 536 | `boss.enrage.type_3.summon_waves` | `E3SummonWaves` |
| 537 | `boss.enrage.type_3.summon_count` | `E3SummonCount` |
| 539 | `boss.enrage.type_3.ring_interval` | `E3RingInterval` |
| 540 | `boss.enrage.type_3.ring_count` | `E3RingCount` |
| 541 | `boss.enrage.type_3.ring_speed` | `E3RingSpeed` |
| 542 | `boss.enrage.type_3.release_ring_count` | `E3ReleaseRingCount` |
| 543 | `boss.enrage.type_3.release_ring_speed` | `E3ReleaseRingSpeed` |
| 545 | `boss.ring_burst.bullet_speed` | `RingBurstSpeed` |
| 546 | `boss.bullet_damage.ring` | `BulletDamageRing` |
| 547 | `boss.movement.type4.bob_amp` | `Move4BobAmp` |
| 548 | `boss.movement.type4.bob_period` | `Move4BobPeriod` |
| 549 | `boss.enrage.type_4.ring_count` | `E4RingCount` |
| 551 | `boss.enrage.type_4.ring_interval` | `E4RingInterval` |
| 552 | `boss.enrage.type_4.ring_speed` | `E4RingSpeed` |
| 553 | `boss.enrage.type_4.precession_deg` | `E4PrecessionDeg` |
| 554 | `boss.enrage.type_4.release_ring_count` | `E4ReleaseRingCount` |
| 555 | `boss.enrage.type_4.release_ring_speed` | `E4ReleaseRingSpeed` |
| 557 | `boss.difficulty_scaling.interval_mult` | `DiffIntervalMult` |
| 558 | `boss.difficulty_scaling.speed_mult` | `DiffSpeedMult` |
| 559 | `boss.difficulty_scaling.counts` | `DiffCountDeltas` |
| 591 | `boss.hp_mults` | `new Godot.Collections.Array { 1.3, 0.7, 1.6, 1.2 }` |
| 617 | `boss.hp_base` | `HpBase` |
| 1055 | `effects.shake.enrage` | `16.0` |
| 1366 | `effects.shake.enrage` | `16.0` |

### `csharp/godot/BuffSelect.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 115 | `buffs.explosive.unlock_boss_kills` | `3` |
| 345 | `buffs.extra_life.heal_on_pick` | `30` |
| 382 | `buffs.extra_life.heal_on_pick` | `30` |

### `csharp/godot/Bullet.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 222 | `buffs.explosive.radius_per_level` | `ExplosiveRadius` |
| 223 | `buffs.explosive.damage_per_level` | `ExplosiveDamage` |
| 224 | `effects.bullet_visual_scale` | `VisualScale` |
| 226 | `effects.enemy_bullet_visual_scale` | `EnemyVisualScale` |
| 229 | `player.grace_period` | `GracePeriod` |
| 231 | `player.parry.reflect_speed_mult` | `ReflectSpeedMult` |
| 232 | `player.parry.reflect_damage_mult` | `ReflectDamageMult` |

### `csharp/godot/CameraShake.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 32 | `effects.shake.decay` | `_decay` |

### `csharp/godot/DirectionShiftEvent.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 23 | `fog_events.direction_shift.shift_interval` | `_interval` |
| 24 | `fog_events.direction_shift.hold_time` | `_hold` |

### `csharp/godot/EliteTurretEvent.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 102 | `elite_turret_event.duration` | `Duration` |
| 103 | `elite_turret_event.enter_time` | `EnterTime` |
| 104 | `elite_turret_event.rise_time` | `RiseTime` |
| 105 | `elite_turret_event.boss_resume_delay` | `BossResumeDelay` |
| 106 | `elite_turret_event.turret_hp_base` | `TurretHpBase` |
| 109 | `elite_turret_event.turret_counts` | `TurretCounts` |
| 115 | `elite_turret_event.ammo_sequences` | `AmmoSequences` |
| 122 | `elite_turret_event.fire_interval` | `new Godot.Collections.Array { FireInterval.X, FireInterva...` |
| 132 | `elite_turret_event.weak_lock` | `WeakLock` |
| 138 | `elite_turret_event.reward_score` | `RewardScore` |
| 139 | `elite_turret_event.carrier.hover_y` | `HoverY` |
| 140 | `elite_turret_event.cooldown` | `Cooldown` |
| 222 | `elite_turret_event.carrier.shake` | `4.0` |

### `csharp/godot/Enemy.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 124 | `enemies.bullet_speed` | `EnemyBulletSpeed` |
| 125 | `enemies.spread_bullet_speed` | `SpreadBulletSpeed` |
| 126 | `enemies.laser_bullet_speed` | `LaserBulletSpeed` |
| 127 | `enemies.bullet_damage.single` | `BulletDamageSingle` |
| 128 | `enemies.bullet_damage.spread` | `BulletDamageSpread` |
| 129 | `enemies.bullet_damage.laser` | `BulletDamageLaser` |
| 130 | `enemies.collision_damage` | `CollisionDamage` |
| 131 | `buffs.slow_field.factor` | `SlowFieldFactor` |
| 132 | `enemies.spread_fan_step` | `SpreadFanStep` |
| 133 | `enemies.lifetime` | `Lifetime` |
| 134 | `enemies.exit_accel` | `ExitAccel` |
| 135 | `enemies.aggressive_chase_speed` | `AggrChaseSpeed` |
| 136 | `enemies.fire_interval` | `FireInterval` |
| 138 | `enemies.hover_band` | `new Godot.Collections.Array { HoverBand.X, HoverBand.Y }` |
| 148 | `enemies.hover_bob_amp` | `HoverBobAmp` |
| 149 | `enemies.hover_bob_freq` | `HoverBobFreq` |
| 150 | `enemies.hover_sway_amp` | `HoverSwayAmp` |
| 151 | `enemies.hover_sway_freq` | `HoverSwayFreq` |
| 152 | `enemies.spiral_drift_amp` | `SpiralDriftAmp` |
| 153 | `enemies.spiral_drift_freq` | `SpiralDriftFreq` |
| 154 | `enemies.spiral_radius` | `SpiralRadius` |
| 173 | `effects.shake.enemy_die` | `_shakeDieNormal` |
| 174 | `effects.shake.elite_die` | `_shakeDieElite` |
| 226 | `enemies.hp_ramp_factor` | `HpRampFactor` |
| 241 | `enemies.speed_ramp_factor` | `SpeedRampFactor` |
| 246 | `player.aim_assist.mark_ratio` | `0.25` |
| 570 | `enemies.move_strategies` | `new Godot.Collections.Dictionary(` |

### `csharp/godot/Explosion.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 62 | `effects.explosion.pool_cap` | `PoolCap` |
| 81 | `effects.explosion_visual_scale` | `1.6` |
| 109 | `effects.shake.boss_seq_initial` | `20.0` |
| 122 | `effects.shake.boss_seq_step` | `8.0` |
| 140 | `effects.shake.boss_seq_final` | `24.0` |
| 149 | `effects.explosion.amount` | `24` |
| 172 | `effects.explosion.debris_amount` | `10` |

### `csharp/godot/FakeEnemiesEvent.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 29 | `fog_events.fake_enemies.count` | `_count` |
| 31 | `fog_events.fake_enemies.spawn_interval` | `_spawnInterval` |

### `csharp/godot/FormationBomb.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 127 | `effects.shake.enemy_die` | `5.0` |

### `csharp/godot/FormationCraft.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 53 | `effects.shake.enemy_die` | `_shakeDie` |

### `csharp/godot/FormationStrikeEvent.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 103 | `formation_strike_event.min_score` | `MinScore` |
| 104 | `formation_strike_event.cooldown` | `Cooldown` |
| 107 | `formation_strike_event.craft_counts` | `CraftCounts` |
| 113 | `formation_strike_event.craft_hp_base` | `CraftHpBase` |
| 114 | `formation_strike_event.craft_score` | `CraftScore` |
| 118 | `formation_strike_event.approach_speed` | `ApproachSpeed` |
| 119 | `formation_strike_event.approach_y` | `ApproachY` |
| 120 | `formation_strike_event.turn_time` | `TurnTime` |
| 121 | `formation_strike_event.run_speed` | `RunSpeed` |
| 122 | `formation_strike_event.bomb_interval` | `BombInterval` |
| 123 | `formation_strike_event.bombs_per_craft` | `BombsPerCraft` |
| 124 | `formation_strike_event.bomb_fall_speed` | `BombFallSpeed` |
| 125 | `formation_strike_event.bomb_fuse` | `BombFuse` |
| 126 | `formation_strike_event.bomb_damage` | `BombDamage` |
| 127 | `formation_strike_event.bomb_radius` | `BombRadius` |
| 128 | `formation_strike_event.reward_all_clear` | `RewardAllClear` |

### `csharp/godot/GameEventManager.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 119 | `fog_events.enabled` | `FOG_ENABLED` |
| 120 | `fog_events.trigger_chance` | `FOG_TRIGGER_CHANCE` |
| 123 | `fog_events.check_interval` | `FOG_CHECK_INTERVAL` |
| 124 | `fog_events.min_interval` | `FOG_MIN_INTERVAL` |
| 125 | `fog_events.first_delay` | `FOG_FIRST_DELAY` |
| 126 | `fog_events.weights` | `FOG_WEIGHTS` |
| 132 | `fog_events.durations` | `FOG_EVENT_DURATIONS` |
| 143 | `elite_turret_event.trigger_interval` | `45.0` |
| 145 | `elite_turret_event.trigger_chance` | `0.35` |
| 146 | `elite_turret_event.min_score` | `800` |
| 151 | `formation_strike_event.trigger_interval` | `40.0` |
| 153 | `formation_strike_event.trigger_chance` | `0.30` |
| 154 | `formation_strike_event.min_score` | `500` |

### `csharp/godot/GameState.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 219 | `world_scale` | `WorldScale` |
| 222 | `milestones.base` | `BuildMilestoneBase(` |
| 241 | `milestones.cycle_mult` | `MilestoneCycleMultValue` |
| 243 | `progression.per_boss_kill` | `0.6` |
| 244 | `progression.per_ten_minutes` | `1.5` |
| 245 | `progression.time_step_seconds` | `30.0` |
| 248 | `difficulty` | `new Godot.Collections.Dictionary(` |
| 257 | `dda.duration` | `DDA_DURATION` |
| 258 | `dda.factor` | `DDA_FACTOR` |
| 259 | `player.max_health` | `_maxHpBase` |
| 261 | `buffs.extra_life.max_hp_bonus` | `_maxHpBonus` |
| 263 | `buffs.lifesteal.max_hp_fraction` | `0.1` |
| 265 | `base_task.refresh_cost` | `REFRESH_COST` |
| 266 | `base_task.grant_per_visit` | `GRANT_PER_VISIT` |
| 1285 | `milestones.boss_kill_base` | `500.0` |
| 2423 | `player.aim_assist.joy_speed` | `JoyAimSpeed` |
| 219 | `world_scale` | `WorldScale` |
| 222 | `milestones.base` | `BuildMilestoneBase(` |
| 241 | `milestones.cycle_mult` | `MilestoneCycleMultValue` |
| 243 | `progression.per_boss_kill` | `0.6` |
| 244 | `progression.per_ten_minutes` | `1.5` |
| 245 | `progression.time_step_seconds` | `30.0` |
| 248 | `difficulty` | `new Godot.Collections.Dictionary(` |
| 257 | `dda.duration` | `DDA_DURATION` |
| 258 | `dda.factor` | `DDA_FACTOR` |
| 259 | `player.max_health` | `_maxHpBase` |
| 261 | `buffs.extra_life.max_hp_bonus` | `_maxHpBonus` |
| 263 | `buffs.lifesteal.max_hp_fraction` | `0.1` |
| 265 | `base_task.refresh_cost` | `REFRESH_COST` |
| 266 | `base_task.grant_per_visit` | `GRANT_PER_VISIT` |
| 1285 | `milestones.boss_kill_base` | `500.0` |
| 2423 | `player.aim_assist.joy_speed` | `JoyAimSpeed` |

### `csharp/godot/Hud.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 146 | `effects.hud_poll_interval` | `_pollInterval` |
| 147 | `hud.boss_bar_segments` | `_bossBarSegments` |
| 148 | `effects.hit_flash.alpha` | `_hitFlashAlpha` |
| 149 | `effects.hit_flash.time` | `_hitFlashTime` |
| 150 | `effects.low_hp.ratio` | `_lowHpRatio` |
| 151 | `effects.low_hp.pulse_min` | `_lowHpPulseMin` |
| 152 | `effects.low_hp.pulse_max` | `_lowHpPulseMax` |
| 153 | `effects.low_hp.pulse_period` | `_lowHpPulsePeriod` |

### `csharp/godot/LaserWeapon.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 84 | `buffs.laser_beam.duration` | `BeamDuration` |
| 85 | `buffs.laser_beam.cooldown` | `CooldownDuration` |
| 86 | `buffs.laser_beam.tick_interval` | `TickInterval` |
| 87 | `buffs.laser_beam.tick_damage` | `TickDamage` |
| 88 | `buffs.laser_beam.length` | `BeamLength` |
| 89 | `buffs.laser_beam.half_width` | `BeamHalfWidth` |
| 90 | `buffs.laser_beam.hit_radius` | `EnemyHitRadius` |

### `csharp/godot/Main.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 104 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 105 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 106 | `effects.give_up_hold_time` | `GIVE_UP_HOLD_TIME` |
| 107 | `boss.enrage.slow_scale` | `ENRAGE_SLOW_SCALE` |
| 108 | `boss.enrage.bullet_time` | `ENRAGE_BULLET_TIME` |
| 109 | `boss.enrage.ramp_time` | `ENRAGE_RAMP_TIME` |
| 867 | `effects.mothership_summon.shake_gate` | `6.0` |
| 880 | `buffs.mothership_recall.cooldown_factor` | `0.5` |
| 932 | `mothership.depart_cooldown` | `60.0` |

### `csharp/godot/MetaHealthFX.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 315 | `effects.meta_health.crack.density` | `def` |
| 352 | `effects.meta_health.lod` | `0` |

### `csharp/godot/Mothership.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 225 | `mothership.hover_y` | `HoverY` |
| 226 | `mothership.release_invincible` | `ReleaseInvincible` |
| 227 | `mothership.dock_tween_time` | `DockTweenTime` |
| 228 | `mothership.dock_offset_y` | `DockOffsetY` |
| 230 | `mothership.resupply_delay` | `ResupplyDelay` |
| 231 | `mothership.release_time` | `ReleaseTime` |
| 232 | `mothership.release_drop` | `ReleaseDrop` |
| 234 | `mothership.mag_cells` | `MagCells` |
| 235 | `mothership.mag_cell_time` | `MagCellTime` |
| 236 | `mothership.mag_warn_cells` | `MagWarnCells` |
| 237 | `mothership.warn_eject_delay` | `WarnEjectDelay` |
| 238 | `mothership.early_hold_time` | `EarlyHoldTime` |
| 239 | `mothership.early_max_discount` | `EarlyMaxDiscount` |
| 240 | `mothership.early_prefill_max` | `EarlyPrefillMax` |
| 241 | `mothership.early_prefill_ratio` | `EarlyPrefillRatio` |
| 242 | `mothership.depart_cooldown` | `DepartCooldown` |
| 243 | `mothership.depart_start_speed` | `DepartStartSpeed` |
| 244 | `mothership.depart_accel` | `DepartAccel` |
| 245 | `mothership.drive.accel` | `DriveAccel` |
| 246 | `mothership.drive.max_speed` | `DriveMaxSpeed` |
| 250 | `mothership.drive.margin_x` | `DriveMarginX` |
| 252 | `mothership.drive.margin_top` | `DriveMarginTop` |
| 254 | `mothership.drive.margin_bottom` | `DriveMarginBottom` |
| 257 | `mothership.upgrade.threshold` | `_upgradeThreshold` |
| 258 | `mothership.upgrade.damage_mult` | `_upgradeDamageMult` |
| 259 | `mothership.upgrade.interval_mult` | `_upgradeIntervalMult` |
| 260 | `mothership.gatling.interval` | `GatlingInterval` |
| 261 | `mothership.gatling.bullet_speed` | `GatlingBulletSpeed` |
| 262 | `mothership.gatling.damage` | `GatlingDamage` |
| 263 | `mothership.gatling.score_scale` | `GatlingScoreScale` |
| 264 | `mothership.gatling.sweep_left_min` | `GatlingSweepLeftMin` |
| 265 | `mothership.gatling.sweep_left_max` | `GatlingSweepLeftMax` |
| 266 | `mothership.gatling.sweep_right_min` | `GatlingSweepRightMin` |
| 267 | `mothership.gatling.sweep_right_max` | `GatlingSweepRightMax` |
| 268 | `mothership.gatling.sweep_left_period` | `GatlingSweepLeftPeriod` |
| 270 | `mothership.gatling.sweep_right_period` | `GatlingSweepRightPeriod` |
| 272 | `mothership.gatling.sweep_right_phase` | `GatlingSweepRightPhase` |
| 274 | `mothership.missile.interval` | `MissileInterval` |
| 275 | `mothership.missile.damage` | `MissileDamage` |
| 276 | `mothership.missile.speed` | `MissileSpeed` |
| 277 | `mothership.missile.target_count` | `MissileTargetCount` |
| 278 | `mothership.missile.splash_damage` | `MissileSplashDamage` |
| 279 | `mothership.missile.splash_radius` | `MissileSplashRadius` |
| 280 | `effects.mothership_summon.warp_in_time` | `WarpInTime` |
| 281 | `effects.mothership_summon.warp_in_drop` | `WarpInDrop` |
| 283 | `effects.mothership_summon.slow.radius` | `SlowRadius` |
| 284 | `effects.mothership_summon.slow.duration` | `SlowDuration` |
| 285 | `effects.mothership_summon.slow.factor` | `SlowFactor` |
| 286 | `effects.mothership_summon.slow.ring_time` | `SlowRingTime` |
| 287 | `effects.mothership_summon.shake_slow` | `ShakeSlow` |
| 391 | `effects.mothership_summon.warp_in_drop` | `WarpInDrop` |
| 1145 | `effects.shake.mothership` | `4.0` |

### `csharp/godot/MothershipSummonWindow.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 86 | `effects.mothership_summon.window.open_time` | `OpenTime` |
| 87 | `effects.mothership_summon.window.close_time` | `CloseTime` |
| 89 | `effects.mothership_summon.window.shot_durations` | `Variant.From(_shotDurations` |

### `csharp/godot/OrbitalStrike.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 68 | `effects.orbital_strike.duration` | `DURATION` |
| 69 | `effects.orbital_strike.impact_at` | `IMPACT_AT` |
| 70 | `effects.orbital_strike.missile_from` | `MISSILE_FROM` |
| 71 | `effects.orbital_strike.reticle_radius` | `RETICLE_RADIUS` |
| 72 | `effects.orbital_strike.impact_y_ratio` | `IMPACT_Y_RATIO` |
| 94 | `effects.shake.boss_seq_final` | `24.0` |
| 107 | `effects.shake.boss_seq_final` | `24.0` |

### `csharp/godot/Player.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 276 | `player.max_speed` | `MaxSpeed` |
| 277 | `player.accel` | `Accel` |
| 278 | `player.decel` | `Decel` |
| 279 | `player.boost_mult` | `BoostMult` |
| 280 | `player.fine_move_mult` | `FineMoveMult` |
| 281 | `player.base_fire_interval` | `BaseFireInterval` |
| 282 | `player.bullet_speed` | `BulletSpeed` |
| 283 | `buffs.crit_shot.chance` | `CritChanceBase` |
| 284 | `buffs.crit_shot.multiplier` | `CritMultiplier` |
| 285 | `player.bullet_spread_deg` | `BulletSpreadDeg` |
| 286 | `player.bullet_damage` | `BulletDamage` |
| 287 | `player.invincible_time` | `InvincibleTime` |
| 288 | `player.spawn_invincible_time` | `SpawnInvincibleTime` |
| 289 | `player.bullet_clear_radius` | `BulletClearRadius` |
| 290 | `player.entry.land_ratio` | `EntryLandRatio` |
| 291 | `player.entry.rush_time` | `EntryRushTime` |
| 292 | `player.entry.retreat_speed` | `EntryRetreatSpeed` |
| 293 | `player.entry.retreat_time` | `EntryRetreatTime` |
| 294 | `player.entry.invincible` | `EntryInvincible` |
| 295 | `player.entry.spawn_clearance` | `EntrySpawnClearance` |
| 296 | `player.entry.rush_hspeed_ratio` | `EntryRushHsRatio` |
| 297 | `buffs.armor.multiplier` | `ArmorMult` |
| 298 | `buffs.evasion.chance` | `EvasionChance` |
| 299 | `buffs.regen.heal_per_sec` | `RegenPerSec` |
| 300 | `effects.shake.player_hit` | `ShakeHit` |
| 302 | `player.fuel.max` | `FuelMax` |
| 304 | `player.fuel.drain` | `FuelDrain` |
| 305 | `player.fuel.regen` | `FuelRegen` |
| 306 | `player.fuel.restart` | `FuelRestart` |
| 307 | `player.dash.distance` | `DashDistance` |
| 308 | `player.dash.time` | `DashTime` |
| 309 | `player.dash.cooldown` | `DashCooldownMaxValue` |
| 310 | `player.dash.fuel_ratio` | `DashFuelRatio` |
| 311 | `player.dash.afterimage_interval` | `AfterimageInterval` |
| 312 | `player.graze_radius` | `GrazeRadius` |
| 313 | `player.graze_score` | `GrazeScore` |
| 314 | `player.parry.arc_deg` | `ParryArcDeg` |
| 315 | `player.parry.radius` | `ParryRadius` |
| 317 | `player.parry.duration` | `0.8` |
| 318 | `player.parry.active_time` | `0.5` |
| 319 | `player.parry.cooldown` | `3.0` |
| 322 | `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| 323 | `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| 324 | `player.aim_assist.falloff.peak` | `_falloffPeak` |
| 325 | `player.aim_assist.falloff.end` | `_falloffEnd` |
| 326 | `player.aim_assist.falloff.min` | `_falloffMin` |
| 476 | `fog_events.bullet_malfunction.jitter_deg` | `20.0` |
| 477 | `fog_events.bullet_malfunction.misfire_chance` | `0.15` |
| 478 | `fog_events.bullet_malfunction.interval_jitter` | `0.3` |
| 867 | `player.aim_assist.homing_time` | `HomingTime` |

### `csharp/godot/ReturnCinematic.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 80 | `effects.return_skip_grace` | `SKIP_GRACE` |

### `csharp/godot/Spawner.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 136 | `spawner.wave_interval_start` | `WAVE_INTERVAL_START` |
| 137 | `spawner.wave_interval_end` | `WAVE_INTERVAL_END` |
| 138 | `spawner.ramp_time` | `RAMP_TIME` |
| 139 | `spawner.interval_min` | `INTERVAL_MIN` |
| 140 | `spawner.boss_score_step` | `BOSS_SCORE_STEP` |
| 141 | `spawner.boss_min_interval` | `BOSS_MIN_INTERVAL` |
| 142 | `spawner.boss_time_limit` | `BOSS_TIME_LIMIT` |
| 143 | `spawner.difficulty_factor` | `DIFFICULTY_FACTOR` |
| 145 | `spawner.unlock_scores` | `UNLOCK_SCORES` |
| 161 | `spawner.wave_size_start` | `WAVE_SIZE_START` |
| 162 | `spawner.wave_size_end` | `WAVE_SIZE_END` |
| 163 | `spawner.special_gap_min` | `SPECIAL_GAP_MIN` |
| 164 | `spawner.special_gap_max` | `SPECIAL_GAP_MAX` |
| 165 | `spawner.rest_waves_after_kill` | `REST_WAVES_AFTER_KILL` |
| 166 | `spawner.elite_wave_size` | `ELITE_WAVE_SIZE` |
| 168 | `enemies.hover_band` | `new Godot.Collections.Array { _hoverBand.X, _hoverBand.Y }` |
| 180 | `enemies.types` | `new Godot.Collections.Array(` |
| 190 | `elites.types` | `new Godot.Collections.Array(` |
| 367 | `spawner.telegraph_duration` | `SpawnTelegraph.GetDefaultDuration(` |
| 423 | `effects.shake.boss_warning` | `14.0` |

### `csharp/godot/Starfield.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 49 | `effects.starfield.far_count` | `_farCount` |
| 55 | `effects.starfield.near_count` | `_nearCount` |
| 61 | `effects.starfield.far_speed` | `_farSpeed` |
| 67 | `effects.starfield.near_speed` | `_nearSpeed` |

### `csharp/godot/StrikeCarrier.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 80 | `elite_turret_event.carrier.retreat_start_speed` | `RetreatStartSpeed` |
| 81 | `elite_turret_event.carrier.retreat_accel` | `RetreatAccel` |

### `csharp/godot/TurretBattery.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 101 | `enemies.bullet_speed` | `SingleSpeed` |
| 102 | `enemies.spread_bullet_speed` | `SpreadSpeed` |
| 103 | `enemies.laser_bullet_speed` | `LaserSpeed` |
| 104 | `boss.homing_bullet_speed` | `HomingSpeed` |
| 105 | `boss.sniper_bullet_speed` | `SniperSpeed` |
| 106 | `enemies.spread_fan_step` | `SpreadFanStep` |
| 107 | `enemies.bullet_damage.single` | `DmgSingle` |
| 108 | `enemies.bullet_damage.spread` | `DmgSpread` |
| 109 | `enemies.bullet_damage.laser` | `DmgLaser` |
| 110 | `boss.bullet_damage.homing` | `DmgHoming` |
| 111 | `boss.bullet_damage.sniper` | `DmgSniper` |
| 132 | `effects.shake.enemy_die` | `_shakeDie` |

### `csharp/godot/Tutorial.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 116 | `effects.home_charge_time` | `HomeChargeTime` |
| 117 | `mothership.dock_charge_time` | `DockChargeTime` |
| 248 | `tutorial.boss_hp` | `120.0` |
| 364 | `mothership.hover_y` | `270.0` |

### `csharp/godot/WarpGate.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 46 | `effects.mothership_summon.gate.open_time` | `OPEN_TIME` |
| 47 | `effects.mothership_summon.gate.close_time` | `CLOSE_TIME` |
| 48 | `effects.mothership_summon.gate.radius` | `RADIUS` |

### `csharp/godot/tests/BalanceTest.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 43 | `player.fuel.drain` | `0.0` |
| 46 | `mothership.depart_cooldown` | `0.0` |
| 47 | `mothership.mag_cells` | `0` |
| 48 | `mothership.missile.damage` | `0` |
| 49 | `boss.hp_mults` | `new Godot.Collections.Array(` |
| 50 | `boss.collision_damage` | `0` |
| 51 | `player.max_speed` | `0.0` |
| 52 | `player.max_health` | `0.0` |
| 53 | `player.bullet_damage` | `0` |
| 54 | `player.dash.cooldown` | `0.0` |
| 55 | `enemies.collision_damage` | `0` |
| 56 | `enemies.bullet_damage.laser` | `0` |
| 57 | `spawner.special_gap_max` | `0` |
| 58 | `buffs.slow_field.factor` | `0.0` |
| 59 | `buffs.explosive.damage_per_level` | `0` |
| 60 | `mothership.gatling.score_scale` | `0.0` |
| 61 | `player.aim_assist.input.magnet_input_min` | `0.0` |
| 62 | `player.aim_assist.falloff.peak` | `0.0` |
| 63 | `player.aim_assist.levels.high.magnet_range` | `0.0` |
| 71 | `player.fuel.drain` | `35.0` |
| 79 | `player.fuel.drain` | `0.0` |
| 80 | `player.max_speed` | `420.0` |
| 95 | `player.fuel.drain` | `0.0` |
| 98 | `difficulty` | `new Godot.Collections.Dictionary(` |

### `csharp/godot/tests/BaseSystemTest.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 306 | `player.aim_assist.joy_speed` | `1400.0` |

### `csharp/godot/tests/EncounterFlowContractTest.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 86 | `elite_turret_event.trigger_interval` | `45.0` |
| 87 | `formation_strike_event.trigger_interval` | `40.0` |

### `csharp/godot/tests/FogEventTest.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 114 | `fog_events.fake_enemies.count` | `5` |
| 234 | `fog_events.trigger_chance` | `0.35` |
| 292 | `fog_events.fake_enemies.count` | `5` |

### `csharp/godot/tests/MothershipUpgradeTest.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 68 | `mothership.upgrade.threshold` | `0` |
| 69 | `mothership.upgrade.damage_mult` | `0.0` |
| 70 | `mothership.upgrade.interval_mult` | `0.0` |

### `csharp/godot/tests/PathResolverInteropTest.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 88 | `player.fuel.drain` | `0.0` |
| 89 | `mothership.mag_cells` | `0` |
| 90 | `difficulty` | `new Godot.Collections.Dictionary(` |

### `csharp/godot/tests/ViewZoomTest.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 364 | `effects.mothership_summon.warp_in_drop` | `260.0` |
| 366 | `mothership.hover_y` | `270.0` |

## 动态拼接键前缀


## json 中存在但脚本未静态引用的键

（经动态键或整段读取覆盖的不列出；剩下的请人工判断是否为死键）

- `boss.phases.type1.p1.[*].attack`
- `boss.phases.type1.p1.[*].interval`
- `boss.phases.type1.p1.[*].waves`
- `boss.phases.type1.p2.[*].attack`
- `boss.phases.type1.p2.[*].interval`
- `boss.phases.type1.p2.[*].waves`
- `boss.phases.type2.p1.[*].attack`
- `boss.phases.type2.p1.[*].interval`
- `boss.phases.type2.p1.[*].waves`
- `boss.phases.type2.p2.[*].attack`
- `boss.phases.type2.p2.[*].interval`
- `boss.phases.type2.p2.[*].waves`
- `boss.phases.type3.p1.[*].attack`
- `boss.phases.type3.p1.[*].duration`
- `boss.phases.type3.p1.[*].interval`
- `boss.phases.type3.p2.[*].attack`
- `boss.phases.type3.p2.[*].interval`
- `boss.phases.type3.p2.[*].waves`
- `boss.phases.type4.p1.[*].attack`
- `boss.phases.type4.p1.[*].interval`
- `boss.phases.type4.p1.[*].waves`
- `boss.phases.type4.p2.[*].attack`
- `boss.phases.type4.p2.[*].duration`
- `boss.phases.type4.p2.[*].interval`
- `boss.phases.type4.p2.[*].waves`
- `buffs.boost_recovery.factor`
- `buffs.bullet_speed.factor`
- `buffs.bullet_speed.max_stacks`
- `buffs.crit_shot.max_stacks`
- `buffs.efficient_boost.factor`
- `buffs.extra_life.max_stacks`
- `buffs.phase_dash.max_stacks`
- `buffs.piercing.max_stacks`
- `buffs.power_shot.factor`
- `buffs.power_shot.max_stacks`
- `buffs.rapid_fire.factor`
- `buffs.rapid_fire.max_stacks`
- `buffs.shield.max_stacks`
- `buffs.spread_shot.max_stacks`
- `effects.meta_health.adapt.bullet_weight`
- `effects.meta_health.adapt.explosion_weight`
- `effects.meta_health.adapt.interval`
- `effects.meta_health.adapt.max`
- `effects.meta_health.adapt.min`
- `effects.meta_health.blur.strength`
- `effects.meta_health.chromatic.base`
- `effects.meta_health.chromatic.peak`
- `effects.meta_health.crack.edge_softness`
- `effects.meta_health.crack.exponent`
- `effects.meta_health.crack.glow`
- `effects.meta_health.crack.grow_overshoot`
- `effects.meta_health.crack.grow_time`
- `effects.meta_health.crack.heal_jitter`
- `effects.meta_health.crack.spread_min`
- `effects.meta_health.crack.width`
- `effects.meta_health.desat.exponent`
- `effects.meta_health.desat.max`
- `effects.meta_health.dying.breath`
- `effects.meta_health.dying.fade`
- `effects.meta_health.dying.heart_max_hz`
- `effects.meta_health.dying.heart_min_hz`
- `effects.meta_health.dying.jitter_px`
- `effects.meta_health.dying.threshold`
- `effects.meta_health.dying.warn_hz`
- `effects.meta_health.pulse.decay_tau`
- `effects.meta_health.pulse.min`
- `effects.meta_health.pulse.scale`
- `effects.meta_health.reduce_flash.chromatic_scale`
- `effects.meta_health.ripple.alpha`
- `effects.meta_health.ripple.duration`
- `effects.meta_health.smooth.down_tau`
- `effects.meta_health.smooth.up_tau`
- `effects.meta_health.vignette.dying_shrink`
- `effects.meta_health.vignette.inner`
- `effects.meta_health.vignette.max_alpha`
- `enemies.damage_ramp_factor`
- `player.aim_assist.levels.high.cone_angle_deg`
- `player.aim_assist.levels.high.cone_strength`
- `player.aim_assist.levels.high.frame_pad`
- `player.aim_assist.levels.high.homing_turn_rate`
- `player.aim_assist.levels.high.magnet_max_speed`
- `player.aim_assist.levels.high.magnet_strength`
- `player.aim_assist.levels.high.stick_factor`
- `player.aim_assist.levels.low.cone_angle_deg`
- `player.aim_assist.levels.low.cone_strength`
- `player.aim_assist.levels.low.frame_pad`
- `player.aim_assist.levels.low.homing_turn_rate`
- `player.aim_assist.levels.low.magnet_max_speed`
- `player.aim_assist.levels.low.magnet_range`
- `player.aim_assist.levels.low.magnet_strength`
- `player.aim_assist.levels.low.stick_factor`
- `player.aim_assist.levels.medium.cone_angle_deg`
- `player.aim_assist.levels.medium.cone_strength`
- `player.aim_assist.levels.medium.frame_pad`
- `player.aim_assist.levels.medium.homing_turn_rate`
- `player.aim_assist.levels.medium.magnet_max_speed`
- `player.aim_assist.levels.medium.magnet_range`
- `player.aim_assist.levels.medium.magnet_strength`
- `player.aim_assist.levels.medium.stick_factor`
- `player.dash.cooldown_stack_factor`
- `version`

## 脚本引用但 json 缺失的键（走回退值，建议补进 json 或确认为有意兜底）

