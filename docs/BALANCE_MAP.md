# BALANCE_MAP — 数值位置地图

> 本文件由 `python3 scripts/tools/gen_balance_map.py` 扫描生成，请勿手改；
> 新增/改名数值键或调整 cfg() 调用后重新运行生成器。

## 怎么改数值

- 运行时数值的唯一来源是 `data/balance.json`；推荐用 `python3 scripts/tools/balance_editor.py` 在浏览器里编辑（改动高亮、类型校验、自动备份）。
- 代码侧的 `GameState.Instance.Cfg("键路径", 回退值)` 仅在 json 缺键/损坏时兜底；新增或调整数值按 AGENTS.md 约定保持 json 与回退值一致。
- 高频 `_Process` 路径的数值在 `_Ready()`/`LoadBalance()` 一次缓存，不要每帧查。

## 静态 cfg() 调用点（按文件分组）

### `csharp/godot/AimFrameLayer.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 90 | `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| 91 | `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| 92 | `player.aim_assist.falloff.peak` | `_falloffPeak` |
| 93 | `player.aim_assist.falloff.end` | `_falloffEnd` |
| 94 | `player.aim_assist.falloff.min` | `_falloffMin` |

### `csharp/godot/BalanceService.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 37 | `enemies.hp_ramp_factor` | `0.25` |
| 38 | `enemies.damage_ramp_factor` | `0.20` |

### `csharp/godot/Boss.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 393 | `boss.enter_speed` | `EnterSpeed` |
| 394 | `boss.fight_y` | `FightY` |
| 395 | `boss.strafe_min_x` | `StrafeMinX` |
| 396 | `boss.strafe_max_x` | `StrafeMaxX` |
| 398 | `boss.phase2_hp_ratio` | `Phase2HpRatio` |
| 399 | `boss.enrage.hp_ratio` | `EnrageHpRatio` |
| 400 | `boss.enrage.rate_mult` | `EnrageRateMult` |
| 401 | `boss.enrage.speed_mult` | `EnrageSpeedMult` |
| 402 | `boss.enrage.player_slow` | `EnragePlayerSlow` |
| 403 | `boss.enrage.snapshot_lasers` | `EnrageSnapshotLasers` |
| 404 | `boss.enrage.snapshot_ring` | `EnrageSnapshotRing` |
| 405 | `boss.enrage.laser_speed` | `EnrageLaserSpeed` |
| 406 | `boss.enrage.ring_speed` | `EnrageRingSpeed` |
| 407 | `boss.enrage.duration` | `EnrageDuration` |
| 408 | `boss.enrage.transition_duration` | `EnrageTransitionDuration` |
| 409 | `boss.enrage.attack_interval` | `EnrageAttackInterval` |
| 410 | `boss.enrage.attack_windup` | `EnrageAttackWindup` |
| 411 | `boss.enrage.release_interval` | `EnrageReleaseInterval` |
| 412 | `boss.enrage.release_hold_duration` | `EnrageReleaseHoldDuration` |
| 413 | `boss.enrage.return_duration` | `EnrageReturnDuration` |
| 414 | `boss.enrage.path_radius_scale` | `EnragePathRadiusScale` |
| 417 | `boss.enrage.square_path_ratio` | `EnrageSquarePathRatio` |
| 418 | `boss.enrage.release_laser_speed` | `EnrageReleaseLaserSpeed` |
| 419 | `boss.enrage.release_ring_speed` | `EnrageReleaseRingSpeed` |
| 421 | `boss.escape.time` | `EscapeTime` |
| 422 | `boss.escape.warning` | `EscapeWarning` |
| 423 | `boss.escape.drift` | `EscapeDrift` |
| 424 | `boss.escape.start_speed` | `EscapeStartSpeed` |
| 425 | `boss.escape.accel` | `EscapeAccel` |
| 437 | `boss.escape.countdown_visible_from` | `EscapeCountdownFrom` |
| 438 | `boss.hp_base` | `HpBase` |
| 440 | `boss.strafe_speeds` | `StrafeSpeeds` |
| 454 | `boss.fire_intervals` | `FireIntervals` |
| 458 | `boss.fan_bullet_speed` | `FanBulletSpeed` |
| 459 | `boss.homing_bullet_speed` | `HomingBulletSpeed` |
| 460 | `boss.sniper_bullet_speed` | `SniperBulletSpeed` |
| 461 | `boss.cross_bullet_speed` | `CrossBulletSpeed` |
| 462 | `boss.collision_damage` | `CollisionDamage` |
| 463 | `buffs.slow_field.factor` | `SlowFieldFactor` |
| 464 | `boss.bullet_damage.fan` | `BulletDamageFan` |
| 465 | `boss.bullet_damage.homing` | `BulletDamageHoming` |
| 466 | `boss.bullet_damage.sniper` | `BulletDamageSniper` |
| 467 | `boss.bullet_damage.cross` | `BulletDamageCross` |
| 468 | `boss.bullet_damage.snapshot_laser` | `BulletDamageSnapshotLaser` |
| 469 | `boss.bullet_damage.snapshot_ring` | `BulletDamageSnapshotRing` |
| 470 | `boss.phases.phase_shift_duration` | `PhaseShiftDuration` |
| 471 | `boss.phases.clear_on_shift` | `ClearOnShift` |
| 472 | `boss.phases.transition_invincible` | `TransitionInvincible` |
| 473 | `boss.phases.telegraph.sniper_aim` | `SniperAimTime` |
| 474 | `boss.phases.telegraph.sniper_track` | `SniperTrackTime` |
| 475 | `boss.phases.attacks.sniper3.burst_interval` | `SniperBurstInterval` |
| 476 | `boss.phases.press_interval` | `PressInterval` |
| 477 | `boss.phases.press_depth` | `PressDepth` |
| 478 | `boss.movement.type1_p2_strafe` | `Type1P2Strafe` |
| 479 | `boss.movement.type1_p2_bob_amp` | `Type1P2BobAmp` |
| 480 | `boss.movement.type1_p2_bob_period` | `Type1P2BobPeriod` |
| 481 | `boss.movement.type2_p2_dash_time` | `Type2P2DashTime` |
| 482 | `boss.movement.type2_p2_rest_time` | `Type2P2RestTime` |
| 483 | `boss.movement.type3_p1_bob_min` | `Type3P1BobMin` |
| 484 | `boss.movement.type3_p1_bob_max` | `Type3P1BobMax` |
| 485 | `boss.movement.type3_p1_bob_period` | `Type3P1BobPeriod` |
| 486 | `boss.movement.type3_p2_strafe` | `Type3P2Strafe` |
| 487 | `boss.movement.type3_p2_bob_amp` | `Type3P2BobAmp` |
| 488 | `boss.movement.type3_p2_bob_period` | `Type3P2BobPeriod` |
| 490 | `boss.phases.attacks.charged_cannon.charge` | `CannonCharge` |
| 491 | `boss.phases.attacks.charged_cannon.shots` | `CannonShots` |
| 492 | `boss.phases.attacks.charged_cannon.interval` | `CannonInterval` |
| 493 | `boss.phases.attacks.charged_cannon.bullet_speed` | `CannonBulletSpeed` |
| 494 | `boss.phases.attacks.charged_cannon.damage` | `CannonDamage` |
| 495 | `boss.phases.attacks.charged_cannon.flash` | `CannonFlash` |
| 496 | `boss.phases.attacks.dash_sweep.aim` | `SweepAim` |
| 497 | `boss.phases.attacks.dash_sweep.speed` | `SweepSpeed` |
| 498 | `boss.phases.attacks.dash_sweep.drop_count` | `SweepDropCount` |
| 499 | `boss.phases.attacks.dash_sweep.drop_speed` | `SweepDropSpeed` |
| 500 | `boss.phases.attacks.dash_sweep.drop_damage` | `SweepDropDamage` |
| 501 | `boss.phases.attacks.dash_sweep.return_duration` | `SweepReturnDuration` |
| 502 | `boss.phases.attacks.minion_volley.count` | `VolleyCount` |
| 503 | `boss.phases.attacks.minion_volley.delay` | `VolleyDelay` |
| 504 | `boss.phases.attacks.minion_volley.bullet_speed` | `VolleyBulletSpeed` |
| 505 | `boss.phases.attacks.minion_volley.bullet_damage` | `VolleyBulletDamage` |
| 506 | `boss.phases.attacks.bullet_wall.count` | `WallCount` |
| 507 | `boss.phases.attacks.bullet_wall.bullet_speed` | `WallBulletSpeed` |
| 508 | `boss.phases.attacks.bullet_wall.damage` | `WallDamage` |
| 509 | `boss.phases.attacks.bullet_wall.arc_deg` | `WallArcDeg` |
| 513 | `boss.enrage.type_1.ring_interval` | `E1RingInterval` |
| 514 | `boss.enrage.type_1.ring_count` | `E1RingCount` |
| 515 | `boss.enrage.type_1.ring_speed` | `E1RingSpeed` |
| 516 | `boss.enrage.type_1.ring_precession_deg` | `E1RingPrecessionDeg` |
| 518 | `boss.enrage.type_1.salvo_charge` | `E1SalvoCharge` |
| 519 | `boss.enrage.type_1.salvo_count` | `E1SalvoCount` |
| 520 | `boss.enrage.type_1.salvo_speed` | `E1SalvoSpeed` |
| 521 | `boss.enrage.type_1.salvo_damage` | `E1SalvoDamage` |
| 522 | `boss.enrage.type_2.point_count` | `E2PointCount` |
| 524 | `boss.enrage.type_2.point_interval` | `E2PointInterval` |
| 525 | `boss.enrage.type_2.aim` | `E2Aim` |
| 526 | `boss.enrage.type_2.sniper_speed` | `E2SniperSpeed` |
| 527 | `boss.enrage.type_2.sniper_damage` | `E2SniperDamage` |
| 528 | `boss.enrage.type_2.release_ring_count` | `E2ReleaseRingCount` |
| 529 | `boss.enrage.type_2.release_ring_speed` | `E2ReleaseRingSpeed` |
| 531 | `boss.enrage.type_3.summon_interval` | `E3SummonInterval` |
| 533 | `boss.phases.type3.summon_interval` | `_summonInterval` |
| 535 | `boss.enrage.type_3.summon_waves` | `E3SummonWaves` |
| 536 | `boss.enrage.type_3.summon_count` | `E3SummonCount` |
| 538 | `boss.enrage.type_3.ring_interval` | `E3RingInterval` |
| 539 | `boss.enrage.type_3.ring_count` | `E3RingCount` |
| 540 | `boss.enrage.type_3.ring_speed` | `E3RingSpeed` |
| 541 | `boss.enrage.type_3.release_ring_count` | `E3ReleaseRingCount` |
| 542 | `boss.enrage.type_3.release_ring_speed` | `E3ReleaseRingSpeed` |
| 544 | `boss.ring_burst.bullet_speed` | `RingBurstSpeed` |
| 545 | `boss.bullet_damage.ring` | `BulletDamageRing` |
| 546 | `boss.movement.type4.bob_amp` | `Move4BobAmp` |
| 548 | `boss.movement.type4.bob_period` | `Move4BobPeriod` |
| 549 | `boss.enrage.type_4.ring_count` | `E4RingCount` |
| 551 | `boss.enrage.type_4.ring_interval` | `E4RingInterval` |
| 552 | `boss.enrage.type_4.ring_speed` | `E4RingSpeed` |
| 553 | `boss.enrage.type_4.precession_deg` | `E4PrecessionDeg` |
| 554 | `boss.enrage.type_4.release_ring_count` | `E4ReleaseRingCount` |
| 555 | `boss.enrage.type_4.release_ring_speed` | `E4ReleaseRingSpeed` |
| 559 | `boss.difficulty_scaling.interval_mult` | `DiffIntervalMult` |
| 564 | `boss.difficulty_scaling.speed_mult` | `DiffSpeedMult` |
| 569 | `boss.difficulty_scaling.counts` | `DiffCountDeltas` |
| 605 | `boss.hp_mults` | `new Godot.Collections.Array { 1.3, 0.7, 1.6, 1.2 }` |
| 631 | `boss.hp_base` | `HpBase` |
| 1037 | `effects.shake.enrage` | `16.0` |
| 1348 | `effects.shake.enrage` | `16.0` |

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
| 33 | `effects.shake.decay` | `_decay` |

### `csharp/godot/DirectionShiftEvent.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 23 | `fog_events.direction_shift.shift_interval` | `_interval` |
| 24 | `fog_events.direction_shift.hold_time` | `_hold` |

### `csharp/godot/EliteTurretEvent.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 101 | `elite_turret_event.duration` | `Duration` |
| 102 | `elite_turret_event.enter_time` | `EnterTime` |
| 103 | `elite_turret_event.rise_time` | `RiseTime` |
| 104 | `elite_turret_event.boss_resume_delay` | `BossResumeDelay` |
| 105 | `elite_turret_event.turret_hp_base` | `TurretHpBase` |
| 108 | `elite_turret_event.turret_counts` | `TurretCounts` |
| 114 | `elite_turret_event.ammo_sequences` | `AmmoSequences` |
| 121 | `elite_turret_event.fire_interval` | `new Godot.Collections.Array { FireInterval.X, FireInterva...` |
| 131 | `elite_turret_event.weak_lock` | `WeakLock` |
| 137 | `elite_turret_event.reward_score` | `RewardScore` |
| 138 | `elite_turret_event.carrier.hover_y` | `HoverY` |
| 139 | `elite_turret_event.cooldown` | `Cooldown` |
| 221 | `elite_turret_event.carrier.shake` | `4.0` |

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
| 496 | `enemies.move_strategies` | `new Godot.Collections.Dictionary(` |

### `csharp/godot/Explosion.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 62 | `effects.explosion.pool_cap` | `PoolCap` |
| 86 | `effects.explosion_visual_scale` | `1.6` |
| 114 | `effects.shake.boss_seq_initial` | `20.0` |
| 127 | `effects.shake.boss_seq_step` | `8.0` |
| 145 | `effects.shake.boss_seq_final` | `24.0` |
| 154 | `effects.explosion.amount` | `24` |
| 177 | `effects.explosion.debris_amount` | `10` |

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
| 131 | `fog_events.enabled` | `FOG_ENABLED` |
| 132 | `fog_events.trigger_chance` | `FOG_TRIGGER_CHANCE` |
| 135 | `fog_events.check_interval` | `FOG_CHECK_INTERVAL` |
| 136 | `fog_events.min_interval` | `FOG_MIN_INTERVAL` |
| 137 | `fog_events.first_delay` | `FOG_FIRST_DELAY` |
| 138 | `fog_events.weights` | `FOG_WEIGHTS` |
| 144 | `fog_events.durations` | `FOG_EVENT_DURATIONS` |
| 155 | `elite_turret_event.trigger_interval` | `45.0` |
| 157 | `elite_turret_event.trigger_chance` | `0.35` |
| 158 | `elite_turret_event.min_score` | `800` |
| 163 | `formation_strike_event.trigger_interval` | `40.0` |
| 165 | `formation_strike_event.trigger_chance` | `0.30` |
| 166 | `formation_strike_event.min_score` | `500` |

### `csharp/godot/GameState.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 217 | `world_scale` | `WorldScale` |
| 220 | `milestones.base` | `BuildMilestoneBase(` |
| 239 | `milestones.cycle_mult` | `MilestoneCycleMultValue` |
| 241 | `progression.per_boss_kill` | `0.6` |
| 242 | `progression.per_ten_minutes` | `1.5` |
| 243 | `progression.time_step_seconds` | `30.0` |
| 246 | `difficulty` | `new Godot.Collections.Dictionary(` |
| 255 | `dda.duration` | `DDA_DURATION` |
| 256 | `dda.factor` | `DDA_FACTOR` |
| 257 | `player.max_health` | `_maxHpBase` |
| 259 | `buffs.extra_life.max_hp_bonus` | `_maxHpBonus` |
| 261 | `buffs.lifesteal.max_hp_fraction` | `0.1` |
| 263 | `base_task.refresh_cost` | `REFRESH_COST` |
| 264 | `base_task.grant_per_visit` | `GRANT_PER_VISIT` |
| 1282 | `milestones.boss_kill_base` | `500.0` |
| 2420 | `player.aim_assist.joy_speed` | `JoyAimSpeed` |

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
| 77 | `buffs.laser_beam.duration` | `BeamDuration` |
| 78 | `buffs.laser_beam.cooldown` | `CooldownDuration` |
| 80 | `buffs.laser_beam.tick_interval` | `TickInterval` |
| 81 | `buffs.laser_beam.tick_damage` | `TickDamage` |
| 82 | `buffs.laser_beam.length` | `BeamLength` |
| 83 | `buffs.laser_beam.half_width` | `BeamHalfWidth` |
| 84 | `buffs.laser_beam.hit_radius` | `EnemyHitRadius` |

### `csharp/godot/Main.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 106 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 107 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 108 | `effects.give_up_hold_time` | `GIVE_UP_HOLD_TIME` |
| 109 | `boss.enrage.slow_scale` | `ENRAGE_SLOW_SCALE` |
| 110 | `boss.enrage.bullet_time` | `ENRAGE_BULLET_TIME` |
| 111 | `boss.enrage.ramp_time` | `ENRAGE_RAMP_TIME` |
| 872 | `effects.mothership_summon.shake_gate` | `6.0` |
| 885 | `buffs.mothership_recall.cooldown_factor` | `0.5` |
| 937 | `mothership.depart_cooldown` | `60.0` |

### `csharp/godot/MetaHealthFX.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 315 | `effects.meta_health.crack.density` | `def` |
| 352 | `effects.meta_health.lod` | `0` |
| 353 | `effects.meta_health.pulse.scale` | `2.5f` |
| 354 | `effects.meta_health.pulse.min` | `0.15f` |
| 356 | `effects.meta_health.pulse.decay_tau` | `0.09f` |
| 357 | `effects.meta_health.chromatic.base` | `0.006f` |
| 358 | `effects.meta_health.chromatic.peak` | `0.014f` |
| 359 | `effects.meta_health.blur.strength` | `0.6f` |
| 361 | `effects.meta_health.ripple.duration` | `0.4f` |
| 362 | `effects.meta_health.ripple.alpha` | `0.8f` |
| 363 | `effects.meta_health.crack.exponent` | `1.6f` |
| 364 | `effects.meta_health.crack.spread_min` | `0.10f` |
| 365 | `effects.meta_health.crack.edge_softness` | `0.08f` |
| 366 | `effects.meta_health.crack.width` | `0.03f` |
| 367 | `effects.meta_health.crack.glow` | `0.8f` |
| 368 | `effects.meta_health.crack.heal_jitter` | `0.35f` |
| 369 | `effects.meta_health.crack.grow_overshoot` | `0.08f` |
| 371 | `effects.meta_health.crack.grow_time` | `0.6f` |
| 373 | `effects.meta_health.desat.max` | `0.35f` |
| 374 | `effects.meta_health.desat.exponent` | `2.0f` |
| 375 | `effects.meta_health.vignette.max_alpha` | `0.5f` |
| 376 | `effects.meta_health.vignette.inner` | `0.62f` |
| 377 | `effects.meta_health.vignette.dying_shrink` | `0.06f` |
| 378 | `effects.meta_health.dying.threshold` | `0.2f` |
| 379 | `effects.meta_health.dying.heart_min_hz` | `1.0f` |
| 380 | `effects.meta_health.dying.heart_max_hz` | `1.2f` |
| 381 | `effects.meta_health.dying.breath` | `0.015f` |
| 382 | `effects.meta_health.dying.jitter_px` | `2.0f` |
| 383 | `effects.meta_health.dying.warn_hz` | `2.5f` |
| 385 | `effects.meta_health.dying.fade` | `0.3f` |
| 387 | `effects.meta_health.smooth.down_tau` | `0.10f` |
| 389 | `effects.meta_health.smooth.up_tau` | `0.80f` |
| 390 | `effects.meta_health.adapt.interval` | `0.25f` |
| 391 | `effects.meta_health.adapt.min` | `0.8f` |
| 392 | `effects.meta_health.adapt.max` | `1.3f` |
| 393 | `effects.meta_health.adapt.bullet_weight` | `0.002f` |
| 394 | `effects.meta_health.adapt.explosion_weight` | `0.15f` |
| 395 | `effects.meta_health.reduce_flash.chromatic_scale` | `0.4f` |

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
| 1041 | `effects.shake.mothership` | `4.0` |

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
| 70 | `effects.orbital_strike.impact_at` | `IMPACT_AT` |
| 71 | `effects.orbital_strike.missile_from` | `MISSILE_FROM` |
| 72 | `effects.orbital_strike.reticle_radius` | `RETICLE_RADIUS` |
| 73 | `effects.orbital_strike.impact_y_ratio` | `IMPACT_Y_RATIO` |
| 95 | `effects.shake.boss_seq_final` | `24.0` |
| 108 | `effects.shake.boss_seq_final` | `24.0` |

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
| 309 | `player.dash.time` | `DashTime` |
| 310 | `player.dash.cooldown` | `DashCooldownMaxValue` |
| 311 | `player.dash.fuel_ratio` | `DashFuelRatio` |
| 312 | `player.dash.afterimage_interval` | `AfterimageInterval` |
| 313 | `player.graze_radius` | `GrazeRadius` |
| 314 | `player.graze_score` | `GrazeScore` |
| 315 | `player.parry.arc_deg` | `ParryArcDeg` |
| 316 | `player.parry.radius` | `ParryRadius` |
| 318 | `player.parry.duration` | `0.8` |
| 319 | `player.parry.active_time` | `0.5` |
| 320 | `player.parry.cooldown` | `3.0` |
| 323 | `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| 324 | `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| 325 | `player.aim_assist.falloff.peak` | `_falloffPeak` |
| 326 | `player.aim_assist.falloff.end` | `_falloffEnd` |
| 327 | `player.aim_assist.falloff.min` | `_falloffMin` |
| 477 | `fog_events.bullet_malfunction.jitter_deg` | `20.0` |
| 478 | `fog_events.bullet_malfunction.misfire_chance` | `0.15` |
| 479 | `fog_events.bullet_malfunction.interval_jitter` | `0.3` |
| 868 | `player.aim_assist.homing_time` | `HomingTime` |
| 82 | `buffs.rapid_fire.factor` | `—` |
| 83 | `buffs.power_shot.factor` | `—` |
| 84 | `buffs.efficient_boost.factor` | `—` |
| 85 | `buffs.boost_recovery.factor` | `—` |
| 86 | `player.dash.cooldown_stack_factor` | `—` |
| 87 | `buffs.spread_shot.max_stacks` | `—` |
| 88 | `buffs.piercing.max_stacks` | `—` |
| 90 | `buffs.bullet_speed.factor` | `—` |

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

## 动态拼接键前缀

- `boss.phases.type…`
- `buffs.…`
- `player.aim_assist.levels.…`

## json 中存在但脚本未静态引用的键

（经动态键或整段读取覆盖的不列出；剩下的请人工判断是否为死键）

- `version`

## 脚本引用但 json 缺失的键（走回退值，建议补进 json 或确认为有意兜底）

