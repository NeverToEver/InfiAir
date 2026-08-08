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
| 86 | `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| 87 | `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| 88 | `player.aim_assist.falloff.peak` | `_falloffPeak` |
| 89 | `player.aim_assist.falloff.end` | `_falloffEnd` |
| 90 | `player.aim_assist.falloff.min` | `_falloffMin` |

### `csharp/godot/Boss.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 392 | `boss.enter_speed` | `EnterSpeed` |
| 393 | `boss.fight_y` | `FightY` |
| 394 | `boss.strafe_min_x` | `StrafeMinX` |
| 395 | `boss.strafe_max_x` | `StrafeMaxX` |
| 396 | `boss.phase2_hp_ratio` | `Phase2HpRatio` |
| 397 | `boss.enrage.hp_ratio` | `EnrageHpRatio` |
| 398 | `boss.enrage.rate_mult` | `EnrageRateMult` |
| 399 | `boss.enrage.speed_mult` | `EnrageSpeedMult` |
| 400 | `boss.enrage.player_slow` | `EnragePlayerSlow` |
| 401 | `boss.enrage.snapshot_lasers` | `EnrageSnapshotLasers` |
| 402 | `boss.enrage.snapshot_ring` | `EnrageSnapshotRing` |
| 403 | `boss.enrage.laser_speed` | `EnrageLaserSpeed` |
| 404 | `boss.enrage.ring_speed` | `EnrageRingSpeed` |
| 405 | `boss.enrage.duration` | `EnrageDuration` |
| 406 | `boss.enrage.transition_duration` | `EnrageTransitionDuration` |
| 407 | `boss.enrage.attack_interval` | `EnrageAttackInterval` |
| 408 | `boss.enrage.attack_windup` | `EnrageAttackWindup` |
| 409 | `boss.enrage.release_interval` | `EnrageReleaseInterval` |
| 410 | `boss.enrage.release_hold_duration` | `EnrageReleaseHoldDuration` |
| 411 | `boss.enrage.return_duration` | `EnrageReturnDuration` |
| 412 | `boss.enrage.path_radius_scale` | `EnragePathRadiusScale` |
| 415 | `boss.enrage.square_path_ratio` | `EnrageSquarePathRatio` |
| 416 | `boss.enrage.release_laser_speed` | `EnrageReleaseLaserSpeed` |
| 417 | `boss.enrage.release_ring_speed` | `EnrageReleaseRingSpeed` |
| 419 | `boss.escape.time` | `EscapeTime` |
| 420 | `boss.escape.warning` | `EscapeWarning` |
| 421 | `boss.escape.drift` | `EscapeDrift` |
| 422 | `boss.escape.start_speed` | `EscapeStartSpeed` |
| 423 | `boss.escape.accel` | `EscapeAccel` |
| 435 | `boss.escape.countdown_visible_from` | `EscapeCountdownFrom` |
| 436 | `boss.hp_base` | `HpBase` |
| 438 | `boss.strafe_speeds` | `StrafeSpeeds` |
| 452 | `boss.fire_intervals` | `FireIntervals` |
| 456 | `boss.fan_bullet_speed` | `FanBulletSpeed` |
| 457 | `boss.homing_bullet_speed` | `HomingBulletSpeed` |
| 458 | `boss.sniper_bullet_speed` | `SniperBulletSpeed` |
| 459 | `boss.cross_bullet_speed` | `CrossBulletSpeed` |
| 460 | `boss.collision_damage` | `CollisionDamage` |
| 461 | `buffs.slow_field.factor` | `SlowFieldFactor` |
| 462 | `boss.bullet_damage.fan` | `BulletDamageFan` |
| 463 | `boss.bullet_damage.homing` | `BulletDamageHoming` |
| 464 | `boss.bullet_damage.sniper` | `BulletDamageSniper` |
| 465 | `boss.bullet_damage.cross` | `BulletDamageCross` |
| 466 | `boss.bullet_damage.snapshot_laser` | `BulletDamageSnapshotLaser` |
| 467 | `boss.bullet_damage.snapshot_ring` | `BulletDamageSnapshotRing` |
| 468 | `boss.phases.phase_shift_duration` | `PhaseShiftDuration` |
| 469 | `boss.phases.clear_on_shift` | `ClearOnShift` |
| 470 | `boss.phases.transition_invincible` | `TransitionInvincible` |
| 471 | `boss.phases.telegraph.sniper_aim` | `SniperAimTime` |
| 472 | `boss.phases.telegraph.sniper_track` | `SniperTrackTime` |
| 473 | `boss.phases.attacks.sniper3.burst_interval` | `SniperBurstInterval` |
| 474 | `boss.phases.press_interval` | `PressInterval` |
| 475 | `boss.phases.press_depth` | `PressDepth` |
| 476 | `boss.movement.type1_p2_strafe` | `Type1P2Strafe` |
| 477 | `boss.movement.type1_p2_bob_amp` | `Type1P2BobAmp` |
| 478 | `boss.movement.type1_p2_bob_period` | `Type1P2BobPeriod` |
| 479 | `boss.movement.type2_p2_dash_time` | `Type2P2DashTime` |
| 480 | `boss.movement.type2_p2_rest_time` | `Type2P2RestTime` |
| 481 | `boss.movement.type3_p1_bob_min` | `Type3P1BobMin` |
| 482 | `boss.movement.type3_p1_bob_max` | `Type3P1BobMax` |
| 483 | `boss.movement.type3_p1_bob_period` | `Type3P1BobPeriod` |
| 484 | `boss.movement.type3_p2_strafe` | `Type3P2Strafe` |
| 485 | `boss.movement.type3_p2_bob_amp` | `Type3P2BobAmp` |
| 486 | `boss.movement.type3_p2_bob_period` | `Type3P2BobPeriod` |
| 488 | `boss.phases.attacks.charged_cannon.charge` | `CannonCharge` |
| 489 | `boss.phases.attacks.charged_cannon.shots` | `CannonShots` |
| 490 | `boss.phases.attacks.charged_cannon.interval` | `CannonInterval` |
| 491 | `boss.phases.attacks.charged_cannon.bullet_speed` | `CannonBulletSpeed` |
| 492 | `boss.phases.attacks.charged_cannon.damage` | `CannonDamage` |
| 493 | `boss.phases.attacks.charged_cannon.flash` | `CannonFlash` |
| 494 | `boss.phases.attacks.dash_sweep.aim` | `SweepAim` |
| 495 | `boss.phases.attacks.dash_sweep.speed` | `SweepSpeed` |
| 496 | `boss.phases.attacks.dash_sweep.drop_count` | `SweepDropCount` |
| 497 | `boss.phases.attacks.dash_sweep.drop_speed` | `SweepDropSpeed` |
| 498 | `boss.phases.attacks.dash_sweep.drop_damage` | `SweepDropDamage` |
| 499 | `boss.phases.attacks.dash_sweep.return_duration` | `SweepReturnDuration` |
| 500 | `boss.phases.attacks.minion_volley.count` | `VolleyCount` |
| 501 | `boss.phases.attacks.minion_volley.delay` | `VolleyDelay` |
| 502 | `boss.phases.attacks.minion_volley.bullet_speed` | `VolleyBulletSpeed` |
| 503 | `boss.phases.attacks.minion_volley.bullet_damage` | `VolleyBulletDamage` |
| 504 | `boss.phases.attacks.bullet_wall.count` | `WallCount` |
| 505 | `boss.phases.attacks.bullet_wall.bullet_speed` | `WallBulletSpeed` |
| 506 | `boss.phases.attacks.bullet_wall.damage` | `WallDamage` |
| 507 | `boss.phases.attacks.bullet_wall.arc_deg` | `WallArcDeg` |
| 511 | `boss.enrage.type_1.ring_interval` | `E1RingInterval` |
| 512 | `boss.enrage.type_1.ring_count` | `E1RingCount` |
| 513 | `boss.enrage.type_1.ring_speed` | `E1RingSpeed` |
| 514 | `boss.enrage.type_1.ring_precession_deg` | `E1RingPrecessionDeg` |
| 516 | `boss.enrage.type_1.salvo_charge` | `E1SalvoCharge` |
| 517 | `boss.enrage.type_1.salvo_count` | `E1SalvoCount` |
| 518 | `boss.enrage.type_1.salvo_speed` | `E1SalvoSpeed` |
| 519 | `boss.enrage.type_1.salvo_damage` | `E1SalvoDamage` |
| 520 | `boss.enrage.type_2.point_count` | `E2PointCount` |
| 522 | `boss.enrage.type_2.point_interval` | `E2PointInterval` |
| 523 | `boss.enrage.type_2.aim` | `E2Aim` |
| 524 | `boss.enrage.type_2.sniper_speed` | `E2SniperSpeed` |
| 525 | `boss.enrage.type_2.sniper_damage` | `E2SniperDamage` |
| 526 | `boss.enrage.type_2.release_ring_count` | `E2ReleaseRingCount` |
| 527 | `boss.enrage.type_2.release_ring_speed` | `E2ReleaseRingSpeed` |
| 529 | `boss.enrage.type_3.summon_interval` | `E3SummonInterval` |
| 531 | `boss.phases.type3.summon_interval` | `_summonInterval` |
| 533 | `boss.enrage.type_3.summon_waves` | `E3SummonWaves` |
| 534 | `boss.enrage.type_3.summon_count` | `E3SummonCount` |
| 536 | `boss.enrage.type_3.ring_interval` | `E3RingInterval` |
| 537 | `boss.enrage.type_3.ring_count` | `E3RingCount` |
| 538 | `boss.enrage.type_3.ring_speed` | `E3RingSpeed` |
| 539 | `boss.enrage.type_3.release_ring_count` | `E3ReleaseRingCount` |
| 540 | `boss.enrage.type_3.release_ring_speed` | `E3ReleaseRingSpeed` |
| 542 | `boss.ring_burst.bullet_speed` | `RingBurstSpeed` |
| 543 | `boss.bullet_damage.ring` | `BulletDamageRing` |
| 544 | `boss.movement.type4.bob_amp` | `Move4BobAmp` |
| 545 | `boss.movement.type4.bob_period` | `Move4BobPeriod` |
| 546 | `boss.enrage.type_4.ring_count` | `E4RingCount` |
| 548 | `boss.enrage.type_4.ring_interval` | `E4RingInterval` |
| 549 | `boss.enrage.type_4.ring_speed` | `E4RingSpeed` |
| 550 | `boss.enrage.type_4.precession_deg` | `E4PrecessionDeg` |
| 551 | `boss.enrage.type_4.release_ring_count` | `E4ReleaseRingCount` |
| 552 | `boss.enrage.type_4.release_ring_speed` | `E4ReleaseRingSpeed` |
| 554 | `boss.difficulty_scaling.interval_mult` | `DiffIntervalMult` |
| 555 | `boss.difficulty_scaling.speed_mult` | `DiffSpeedMult` |
| 556 | `boss.difficulty_scaling.counts` | `DiffCountDeltas` |
| 588 | `boss.hp_mults` | `new Godot.Collections.Array { 1.3, 0.7, 1.6, 1.2 }` |
| 614 | `boss.hp_base` | `HpBase` |
| 1061 | `effects.shake.enrage` | `16.0` |
| 1372 | `effects.shake.enrage` | `16.0` |

### `csharp/godot/BuffSelect.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 114 | `buffs.explosive.unlock_boss_kills` | `3` |
| 344 | `buffs.extra_life.heal_on_pick` | `30` |
| 381 | `buffs.extra_life.heal_on_pick` | `30` |

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
| 207 | `elite_turret_event.carrier.shake` | `4.0` |

### `csharp/godot/Enemy.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 123 | `enemies.bullet_speed` | `EnemyBulletSpeed` |
| 124 | `enemies.spread_bullet_speed` | `SpreadBulletSpeed` |
| 125 | `enemies.laser_bullet_speed` | `LaserBulletSpeed` |
| 126 | `enemies.bullet_damage.single` | `BulletDamageSingle` |
| 127 | `enemies.bullet_damage.spread` | `BulletDamageSpread` |
| 128 | `enemies.bullet_damage.laser` | `BulletDamageLaser` |
| 129 | `enemies.collision_damage` | `CollisionDamage` |
| 130 | `buffs.slow_field.factor` | `SlowFieldFactor` |
| 131 | `enemies.spread_fan_step` | `SpreadFanStep` |
| 132 | `enemies.lifetime` | `Lifetime` |
| 133 | `enemies.exit_accel` | `ExitAccel` |
| 134 | `enemies.aggressive_chase_speed` | `AggrChaseSpeed` |
| 135 | `enemies.fire_interval` | `FireInterval` |
| 137 | `enemies.hover_band` | `new Godot.Collections.Array { HoverBand.X, HoverBand.Y }` |
| 147 | `enemies.hover_bob_amp` | `HoverBobAmp` |
| 148 | `enemies.hover_bob_freq` | `HoverBobFreq` |
| 149 | `enemies.hover_sway_amp` | `HoverSwayAmp` |
| 150 | `enemies.hover_sway_freq` | `HoverSwayFreq` |
| 151 | `enemies.spiral_drift_amp` | `SpiralDriftAmp` |
| 152 | `enemies.spiral_drift_freq` | `SpiralDriftFreq` |
| 153 | `enemies.spiral_radius` | `SpiralRadius` |
| 172 | `effects.shake.enemy_die` | `_shakeDieNormal` |
| 173 | `effects.shake.elite_die` | `_shakeDieElite` |
| 225 | `enemies.hp_ramp_factor` | `HpRampFactor` |
| 240 | `enemies.speed_ramp_factor` | `SpeedRampFactor` |
| 245 | `player.aim_assist.mark_ratio` | `0.25` |
| 582 | `enemies.move_strategies` | `new Godot.Collections.Dictionary(` |

### `csharp/godot/Explosion.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 62 | `effects.explosion.pool_cap` | `PoolCap` |
| 77 | `effects.explosion_visual_scale` | `1.6` |
| 105 | `effects.shake.boss_seq_initial` | `20.0` |
| 118 | `effects.shake.boss_seq_step` | `8.0` |
| 136 | `effects.shake.boss_seq_final` | `24.0` |
| 145 | `effects.explosion.amount` | `24` |
| 168 | `effects.explosion.debris_amount` | `10` |

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
| 52 | `effects.shake.enemy_die` | `_shakeDie` |

### `csharp/godot/FormationStrikeEvent.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 110 | `formation_strike_event.min_score` | `MinScore` |
| 111 | `formation_strike_event.cooldown` | `Cooldown` |
| 114 | `formation_strike_event.craft_counts` | `CraftCounts` |
| 120 | `formation_strike_event.craft_hp_base` | `CraftHpBase` |
| 121 | `formation_strike_event.craft_score` | `CraftScore` |
| 125 | `formation_strike_event.approach_speed` | `ApproachSpeed` |
| 126 | `formation_strike_event.approach_y` | `ApproachY` |
| 127 | `formation_strike_event.turn_time` | `TurnTime` |
| 128 | `formation_strike_event.run_speed` | `RunSpeed` |
| 129 | `formation_strike_event.bomb_interval` | `BombInterval` |
| 130 | `formation_strike_event.bombs_per_craft` | `BombsPerCraft` |
| 131 | `formation_strike_event.bomb_fall_speed` | `BombFallSpeed` |
| 132 | `formation_strike_event.bomb_fuse` | `BombFuse` |
| 133 | `formation_strike_event.bomb_damage` | `BombDamage` |
| 134 | `formation_strike_event.bomb_radius` | `BombRadius` |
| 135 | `formation_strike_event.reward_all_clear` | `RewardAllClear` |

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
| 221 | `world_scale` | `WorldScale` |
| 224 | `milestones.base` | `BuildMilestoneBase(` |
| 243 | `milestones.cycle_mult` | `MilestoneCycleMultValue` |
| 245 | `progression.per_boss_kill` | `0.6` |
| 246 | `progression.per_ten_minutes` | `1.5` |
| 247 | `progression.time_step_seconds` | `30.0` |
| 250 | `difficulty` | `new Godot.Collections.Dictionary(` |
| 259 | `dda.duration` | `DDA_DURATION` |
| 260 | `dda.factor` | `DDA_FACTOR` |
| 261 | `player.max_health` | `_maxHpBase` |
| 263 | `buffs.extra_life.max_hp_bonus` | `_maxHpBonus` |
| 265 | `buffs.lifesteal.max_hp_fraction` | `0.1` |
| 267 | `base_task.refresh_cost` | `REFRESH_COST` |
| 268 | `base_task.grant_per_visit` | `GRANT_PER_VISIT` |
| 1287 | `milestones.boss_kill_base` | `500.0` |
| 2415 | `player.aim_assist.joy_speed` | `JoyAimSpeed` |
| 221 | `world_scale` | `WorldScale` |
| 224 | `milestones.base` | `BuildMilestoneBase(` |
| 243 | `milestones.cycle_mult` | `MilestoneCycleMultValue` |
| 245 | `progression.per_boss_kill` | `0.6` |
| 246 | `progression.per_ten_minutes` | `1.5` |
| 247 | `progression.time_step_seconds` | `30.0` |
| 250 | `difficulty` | `new Godot.Collections.Dictionary(` |
| 259 | `dda.duration` | `DDA_DURATION` |
| 260 | `dda.factor` | `DDA_FACTOR` |
| 261 | `player.max_health` | `_maxHpBase` |
| 263 | `buffs.extra_life.max_hp_bonus` | `_maxHpBonus` |
| 265 | `buffs.lifesteal.max_hp_fraction` | `0.1` |
| 267 | `base_task.refresh_cost` | `REFRESH_COST` |
| 268 | `base_task.grant_per_visit` | `GRANT_PER_VISIT` |
| 1287 | `milestones.boss_kill_base` | `500.0` |
| 2415 | `player.aim_assist.joy_speed` | `JoyAimSpeed` |

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
| 86 | `buffs.laser_beam.duration` | `BeamDuration` |
| 87 | `buffs.laser_beam.cooldown` | `CooldownDuration` |
| 88 | `buffs.laser_beam.tick_interval` | `TickInterval` |
| 89 | `buffs.laser_beam.tick_damage` | `TickDamage` |
| 90 | `buffs.laser_beam.length` | `BeamLength` |
| 91 | `buffs.laser_beam.half_width` | `BeamHalfWidth` |
| 92 | `buffs.laser_beam.hit_radius` | `EnemyHitRadius` |

### `csharp/godot/Main.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 104 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 105 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 106 | `effects.give_up_hold_time` | `GIVE_UP_HOLD_TIME` |
| 107 | `boss.enrage.slow_scale` | `ENRAGE_SLOW_SCALE` |
| 108 | `boss.enrage.bullet_time` | `ENRAGE_BULLET_TIME` |
| 109 | `boss.enrage.ramp_time` | `ENRAGE_RAMP_TIME` |
| 829 | `effects.mothership_summon.shake_gate` | `6.0` |
| 842 | `buffs.mothership_recall.cooldown_factor` | `0.5` |
| 894 | `mothership.depart_cooldown` | `60.0` |

### `csharp/godot/MetaHealthFX.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 307 | `effects.meta_health.crack.density` | `def` |
| 344 | `effects.meta_health.lod` | `0` |

### `csharp/godot/Mothership.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 224 | `mothership.hover_y` | `HoverY` |
| 225 | `mothership.release_invincible` | `ReleaseInvincible` |
| 226 | `mothership.dock_tween_time` | `DockTweenTime` |
| 227 | `mothership.dock_offset_y` | `DockOffsetY` |
| 229 | `mothership.resupply_delay` | `ResupplyDelay` |
| 230 | `mothership.release_time` | `ReleaseTime` |
| 231 | `mothership.release_drop` | `ReleaseDrop` |
| 233 | `mothership.mag_cells` | `MagCells` |
| 234 | `mothership.mag_cell_time` | `MagCellTime` |
| 235 | `mothership.mag_warn_cells` | `MagWarnCells` |
| 236 | `mothership.warn_eject_delay` | `WarnEjectDelay` |
| 237 | `mothership.early_hold_time` | `EarlyHoldTime` |
| 238 | `mothership.early_max_discount` | `EarlyMaxDiscount` |
| 239 | `mothership.early_prefill_max` | `EarlyPrefillMax` |
| 240 | `mothership.early_prefill_ratio` | `EarlyPrefillRatio` |
| 241 | `mothership.depart_cooldown` | `DepartCooldown` |
| 242 | `mothership.depart_start_speed` | `DepartStartSpeed` |
| 243 | `mothership.depart_accel` | `DepartAccel` |
| 244 | `mothership.drive.accel` | `DriveAccel` |
| 245 | `mothership.drive.max_speed` | `DriveMaxSpeed` |
| 249 | `mothership.drive.margin_x` | `DriveMarginX` |
| 251 | `mothership.drive.margin_top` | `DriveMarginTop` |
| 253 | `mothership.drive.margin_bottom` | `DriveMarginBottom` |
| 256 | `mothership.upgrade.threshold` | `_upgradeThreshold` |
| 257 | `mothership.upgrade.damage_mult` | `_upgradeDamageMult` |
| 258 | `mothership.upgrade.interval_mult` | `_upgradeIntervalMult` |
| 259 | `mothership.gatling.interval` | `GatlingInterval` |
| 260 | `mothership.gatling.bullet_speed` | `GatlingBulletSpeed` |
| 261 | `mothership.gatling.damage` | `GatlingDamage` |
| 262 | `mothership.gatling.score_scale` | `GatlingScoreScale` |
| 263 | `mothership.gatling.sweep_left_min` | `GatlingSweepLeftMin` |
| 264 | `mothership.gatling.sweep_left_max` | `GatlingSweepLeftMax` |
| 265 | `mothership.gatling.sweep_right_min` | `GatlingSweepRightMin` |
| 266 | `mothership.gatling.sweep_right_max` | `GatlingSweepRightMax` |
| 267 | `mothership.gatling.sweep_left_period` | `GatlingSweepLeftPeriod` |
| 269 | `mothership.gatling.sweep_right_period` | `GatlingSweepRightPeriod` |
| 271 | `mothership.gatling.sweep_right_phase` | `GatlingSweepRightPhase` |
| 273 | `mothership.missile.interval` | `MissileInterval` |
| 274 | `mothership.missile.damage` | `MissileDamage` |
| 275 | `mothership.missile.speed` | `MissileSpeed` |
| 276 | `mothership.missile.target_count` | `MissileTargetCount` |
| 277 | `mothership.missile.splash_damage` | `MissileSplashDamage` |
| 278 | `mothership.missile.splash_radius` | `MissileSplashRadius` |
| 279 | `effects.mothership_summon.warp_in_time` | `WarpInTime` |
| 280 | `effects.mothership_summon.warp_in_drop` | `WarpInDrop` |
| 282 | `effects.mothership_summon.slow.radius` | `SlowRadius` |
| 283 | `effects.mothership_summon.slow.duration` | `SlowDuration` |
| 284 | `effects.mothership_summon.slow.factor` | `SlowFactor` |
| 285 | `effects.mothership_summon.slow.ring_time` | `SlowRingTime` |
| 286 | `effects.mothership_summon.shake_slow` | `ShakeSlow` |
| 390 | `effects.mothership_summon.warp_in_drop` | `WarpInDrop` |
| 1152 | `effects.shake.mothership` | `4.0` |

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
| 252 | `player.max_speed` | `MaxSpeed` |
| 253 | `player.accel` | `Accel` |
| 254 | `player.decel` | `Decel` |
| 255 | `player.boost_mult` | `BoostMult` |
| 256 | `player.fine_move_mult` | `FineMoveMult` |
| 257 | `player.base_fire_interval` | `BaseFireInterval` |
| 258 | `player.bullet_speed` | `BulletSpeed` |
| 259 | `buffs.crit_shot.chance` | `CritChanceBase` |
| 260 | `buffs.crit_shot.multiplier` | `CritMultiplier` |
| 261 | `player.bullet_spread_deg` | `BulletSpreadDeg` |
| 262 | `player.bullet_damage` | `BulletDamage` |
| 263 | `player.invincible_time` | `InvincibleTime` |
| 264 | `player.spawn_invincible_time` | `SpawnInvincibleTime` |
| 265 | `player.bullet_clear_radius` | `BulletClearRadius` |
| 266 | `player.entry.land_ratio` | `EntryLandRatio` |
| 267 | `player.entry.rush_time` | `EntryRushTime` |
| 268 | `player.entry.retreat_speed` | `EntryRetreatSpeed` |
| 269 | `player.entry.retreat_time` | `EntryRetreatTime` |
| 270 | `player.entry.invincible` | `EntryInvincible` |
| 271 | `player.entry.spawn_clearance` | `EntrySpawnClearance` |
| 272 | `player.entry.rush_hspeed_ratio` | `EntryRushHsRatio` |
| 273 | `buffs.armor.multiplier` | `ArmorMult` |
| 274 | `buffs.evasion.chance` | `EvasionChance` |
| 275 | `buffs.regen.heal_per_sec` | `RegenPerSec` |
| 276 | `effects.shake.player_hit` | `ShakeHit` |
| 278 | `player.fuel.max` | `FuelMax` |
| 280 | `player.fuel.drain` | `FuelDrain` |
| 281 | `player.fuel.regen` | `FuelRegen` |
| 282 | `player.fuel.restart` | `FuelRestart` |
| 283 | `player.dash.distance` | `DashDistance` |
| 284 | `player.dash.time` | `DashTime` |
| 285 | `player.dash.cooldown` | `DashCooldownMaxValue` |
| 286 | `player.dash.fuel_ratio` | `DashFuelRatio` |
| 287 | `player.dash.afterimage_interval` | `AfterimageInterval` |
| 288 | `player.graze_radius` | `GrazeRadius` |
| 289 | `player.graze_score` | `GrazeScore` |
| 290 | `player.parry.arc_deg` | `ParryArcDeg` |
| 291 | `player.parry.radius` | `ParryRadius` |
| 293 | `player.parry.duration` | `0.8` |
| 294 | `player.parry.active_time` | `0.5` |
| 295 | `player.parry.cooldown` | `3.0` |
| 298 | `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| 299 | `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| 300 | `player.aim_assist.falloff.peak` | `_falloffPeak` |
| 301 | `player.aim_assist.falloff.end` | `_falloffEnd` |
| 302 | `player.aim_assist.falloff.min` | `_falloffMin` |
| 452 | `fog_events.bullet_malfunction.jitter_deg` | `20.0` |
| 453 | `fog_events.bullet_malfunction.misfire_chance` | `0.15` |
| 454 | `fog_events.bullet_malfunction.interval_jitter` | `0.3` |
| 842 | `player.aim_assist.homing_time` | `HomingTime` |

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

### `csharp/godot/StrikeCarrier.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 79 | `elite_turret_event.carrier.retreat_start_speed` | `RetreatStartSpeed` |
| 80 | `elite_turret_event.carrier.retreat_accel` | `RetreatAccel` |

### `csharp/godot/TurretBattery.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 100 | `enemies.bullet_speed` | `SingleSpeed` |
| 101 | `enemies.spread_bullet_speed` | `SpreadSpeed` |
| 102 | `enemies.laser_bullet_speed` | `LaserSpeed` |
| 103 | `boss.homing_bullet_speed` | `HomingSpeed` |
| 104 | `boss.sniper_bullet_speed` | `SniperSpeed` |
| 105 | `enemies.spread_fan_step` | `SpreadFanStep` |
| 106 | `enemies.bullet_damage.single` | `DmgSingle` |
| 107 | `enemies.bullet_damage.spread` | `DmgSpread` |
| 108 | `enemies.bullet_damage.laser` | `DmgLaser` |
| 109 | `boss.bullet_damage.homing` | `DmgHoming` |
| 110 | `boss.bullet_damage.sniper` | `DmgSniper` |
| 131 | `effects.shake.enemy_die` | `_shakeDie` |

### `csharp/godot/Tutorial.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 114 | `effects.home_charge_time` | `HomeChargeTime` |
| 115 | `mothership.dock_charge_time` | `DockChargeTime` |
| 225 | `tutorial.boss_hp` | `120.0` |
| 340 | `mothership.hover_y` | `270.0` |

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
- `effects.starfield.far_count`
- `effects.starfield.far_speed`
- `effects.starfield.near_count`
- `effects.starfield.near_speed`
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

