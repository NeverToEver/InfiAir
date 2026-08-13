# BALANCE_MAP — 数值位置地图

> 本文件由 `python3 scripts/tools/gen_balance_map.py` 扫描生成，请勿手改；
> 新增/改名数值键或调整 cfg() 调用后重新运行生成器。

## 怎么改数值

- 运行时数值的唯一来源是 `data/balance.json`；推荐用 `python3 scripts/tools/balance_editor.py` 在浏览器里编辑（改动高亮、类型校验、自动备份）。
- 代码侧的 `GameState.Instance.Cfg("键路径", 回退值)` / `CfgFx.Float/Int("键路径", 回退值)` 仅在 json 缺键/损坏时兜底；新增或调整数值按 AGENTS.md 约定保持 json 与回退值一致。
- 高频 `_Process` 路径的数值在 `_Ready()`/`LoadBalance()` 一次缓存，不要每帧查。

## 静态 cfg() 调用点（按文件分组）

### `csharp/godot/AimFrameLayer.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 85 | `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| 86 | `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| 89 | `player.aim_assist.falloff.peak` | `_falloffPeak` |
| 90 | `player.aim_assist.falloff.end` | `_falloffEnd` |
| 91 | `player.aim_assist.falloff.min` | `_falloffMin` |

### `csharp/godot/BalanceService.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 45 | `enemies.hp_ramp_factor` | `0.25` |
| 46 | `enemies.damage_ramp_factor` | `0.20` |
| 48 | `enemies.move_strategies` | `new Godot.Collections.Dictionary(` |
| 50 | `enemies.speed_ramp_factor` | `0.1` |
| 51 | `player.aim_assist.mark_ratio` | `0.25` |
| 53 | `spawner.telegraph_duration` | `SpawnTelegraph.GetDefaultDuration(` |

### `csharp/godot/Boss.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 436 | `boss.strafe_speeds` | `StrafeSpeeds` |
| 452 | `boss.fire_intervals` | `FireIntervals` |
| 476 | `boss.phases.clear_on_shift` | `ClearOnShift` |
| 564 | `boss.difficulty_scaling.interval_mult` | `DiffIntervalMult` |
| 569 | `boss.difficulty_scaling.speed_mult` | `DiffSpeedMult` |
| 574 | `boss.difficulty_scaling.counts` | `DiffCountDeltas` |
| 608 | `boss.hp_mults` | `new Godot.Collections.Array { 1.3, 0.7, 1.6, 1.2 }` |
| 634 | `boss.hp_base` | `HpBase` |
| 1047 | `effects.shake.enrage` | `16.0` |
| 1344 | `effects.shake.enrage` | `16.0` |
| 381 | `boss.enter_speed` | `EnterSpeed` |
| 382 | `boss.fight_y` | `FightY` |
| 383 | `boss.strafe_min_x` | `StrafeMinX` |
| 384 | `boss.strafe_max_x` | `StrafeMaxX` |
| 386 | `boss.phase2_hp_ratio` | `Phase2HpRatio` |
| 387 | `boss.enrage.hp_ratio` | `EnrageHpRatio` |
| 394 | `boss.enrage.rate_mult` | `EnrageRateMult` |
| 395 | `boss.enrage.speed_mult` | `EnrageSpeedMult` |
| 396 | `boss.enrage.player_slow` | `EnragePlayerSlow` |
| 397 | `boss.enrage.snapshot_lasers` | `EnrageSnapshotLasers` |
| 398 | `boss.enrage.snapshot_ring` | `EnrageSnapshotRing` |
| 399 | `boss.enrage.laser_speed` | `EnrageLaserSpeed` |
| 400 | `boss.enrage.ring_speed` | `EnrageRingSpeed` |
| 403 | `boss.enrage.duration` | `EnrageDuration` |
| 404 | `boss.enrage.transition_duration` | `EnrageTransitionDuration` |
| 408 | `boss.enrage.attack_interval` | `EnrageAttackInterval` |
| 409 | `boss.enrage.attack_windup` | `EnrageAttackWindup` |
| 410 | `boss.enrage.release_interval` | `EnrageReleaseInterval` |
| 413 | `boss.enrage.release_hold_duration` | `EnrageReleaseHoldDuration` |
| 414 | `boss.enrage.return_duration` | `EnrageReturnDuration` |
| 415 | `boss.enrage.path_radius_scale` | `EnragePathRadiusScale` |
| 417 | `boss.enrage.square_path_ratio` | `EnrageSquarePathRatio` |
| 418 | `boss.enrage.release_laser_speed` | `EnrageReleaseLaserSpeed` |
| 419 | `boss.enrage.release_ring_speed` | `EnrageReleaseRingSpeed` |
| 421 | `boss.escape.time` | `EscapeTime` |
| 422 | `boss.escape.warning` | `EscapeWarning` |
| 423 | `boss.escape.drift` | `EscapeDrift` |
| 424 | `boss.escape.start_speed` | `EscapeStartSpeed` |
| 425 | `boss.escape.accel` | `EscapeAccel` |
| 433 | `boss.escape.countdown_visible_from` | `EscapeCountdownFrom` |
| 434 | `boss.hp_base` | `HpBase` |
| 460 | `boss.fan_bullet_speed` | `FanBulletSpeed` |
| 461 | `boss.homing_bullet_speed` | `HomingBulletSpeed` |
| 462 | `boss.sniper_bullet_speed` | `SniperBulletSpeed` |
| 463 | `boss.cross_bullet_speed` | `CrossBulletSpeed` |
| 464 | `boss.collision_damage` | `CollisionDamage` |
| 468 | `buffs.slow_field.factor` | `SlowFieldFactor` |
| 469 | `boss.bullet_damage.fan` | `BulletDamageFan` |
| 470 | `boss.bullet_damage.homing` | `BulletDamageHoming` |
| 471 | `boss.bullet_damage.sniper` | `BulletDamageSniper` |
| 472 | `boss.bullet_damage.cross` | `BulletDamageCross` |
| 473 | `boss.bullet_damage.snapshot_laser` | `BulletDamageSnapshotLaser` |
| 474 | `boss.bullet_damage.snapshot_ring` | `BulletDamageSnapshotRing` |
| 475 | `boss.phases.phase_shift_duration` | `PhaseShiftDuration` |
| 477 | `boss.phases.transition_invincible` | `TransitionInvincible` |
| 478 | `boss.phases.telegraph.sniper_aim` | `SniperAimTime` |
| 479 | `boss.phases.telegraph.sniper_track` | `SniperTrackTime` |
| 480 | `boss.phases.attacks.sniper3.burst_interval` | `SniperBurstInterval` |
| 481 | `boss.phases.press_interval` | `PressInterval` |
| 482 | `boss.phases.press_depth` | `PressDepth` |
| 483 | `boss.movement.type1_p2_strafe` | `Type1P2Strafe` |
| 484 | `boss.movement.type1_p2_bob_amp` | `Type1P2BobAmp` |
| 485 | `boss.movement.type1_p2_bob_period` | `Type1P2BobPeriod` |
| 486 | `boss.movement.type2_p2_dash_time` | `Type2P2DashTime` |
| 487 | `boss.movement.type2_p2_rest_time` | `Type2P2RestTime` |
| 488 | `boss.movement.type3_p1_bob_min` | `Type3P1BobMin` |
| 489 | `boss.movement.type3_p1_bob_max` | `Type3P1BobMax` |
| 490 | `boss.movement.type3_p1_bob_period` | `Type3P1BobPeriod` |
| 491 | `boss.movement.type3_p2_strafe` | `Type3P2Strafe` |
| 492 | `boss.movement.type3_p2_bob_amp` | `Type3P2BobAmp` |
| 493 | `boss.movement.type3_p2_bob_period` | `Type3P2BobPeriod` |
| 495 | `boss.phases.attacks.charged_cannon.charge` | `CannonCharge` |
| 496 | `boss.phases.attacks.charged_cannon.shots` | `CannonShots` |
| 497 | `boss.phases.attacks.charged_cannon.interval` | `CannonInterval` |
| 498 | `boss.phases.attacks.charged_cannon.bullet_speed` | `CannonBulletSpeed` |
| 499 | `boss.phases.attacks.charged_cannon.damage` | `CannonDamage` |
| 500 | `boss.phases.attacks.charged_cannon.flash` | `CannonFlash` |
| 501 | `boss.phases.attacks.dash_sweep.aim` | `SweepAim` |
| 502 | `boss.phases.attacks.dash_sweep.speed` | `SweepSpeed` |
| 503 | `boss.phases.attacks.dash_sweep.drop_count` | `SweepDropCount` |
| 504 | `boss.phases.attacks.dash_sweep.drop_speed` | `SweepDropSpeed` |
| 505 | `boss.phases.attacks.dash_sweep.drop_damage` | `SweepDropDamage` |
| 508 | `boss.phases.attacks.dash_sweep.return_duration` | `SweepReturnDuration` |
| 509 | `boss.phases.attacks.minion_volley.count` | `VolleyCount` |
| 510 | `boss.phases.attacks.minion_volley.delay` | `VolleyDelay` |
| 511 | `boss.phases.attacks.minion_volley.bullet_speed` | `VolleyBulletSpeed` |
| 512 | `boss.phases.attacks.minion_volley.bullet_damage` | `VolleyBulletDamage` |
| 513 | `boss.phases.attacks.bullet_wall.count` | `WallCount` |
| 514 | `boss.phases.attacks.bullet_wall.bullet_speed` | `WallBulletSpeed` |
| 515 | `boss.phases.attacks.bullet_wall.damage` | `WallDamage` |
| 516 | `boss.phases.attacks.bullet_wall.arc_deg` | `WallArcDeg` |
| 519 | `boss.enrage.type_1.ring_interval` | `E1RingInterval` |
| 520 | `boss.enrage.type_1.ring_count` | `E1RingCount` |
| 521 | `boss.enrage.type_1.ring_speed` | `E1RingSpeed` |
| 522 | `boss.enrage.type_1.ring_precession_deg` | `E1RingPrecessionDeg` |
| 523 | `boss.enrage.type_1.salvo_charge` | `E1SalvoCharge` |
| 524 | `boss.enrage.type_1.salvo_count` | `E1SalvoCount` |
| 525 | `boss.enrage.type_1.salvo_speed` | `E1SalvoSpeed` |
| 526 | `boss.enrage.type_1.salvo_damage` | `E1SalvoDamage` |
| 529 | `boss.enrage.type_2.point_count` | `E2PointCount` |
| 530 | `boss.enrage.type_2.point_interval` | `E2PointInterval` |
| 531 | `boss.enrage.type_2.aim` | `E2Aim` |
| 532 | `boss.enrage.type_2.sniper_speed` | `E2SniperSpeed` |
| 533 | `boss.enrage.type_2.sniper_damage` | `E2SniperDamage` |
| 534 | `boss.enrage.type_2.release_ring_count` | `E2ReleaseRingCount` |
| 535 | `boss.enrage.type_2.release_ring_speed` | `E2ReleaseRingSpeed` |
| 536 | `boss.enrage.type_3.summon_interval` | `E3SummonInterval` |
| 540 | `boss.phases.type3.summon_interval` | `_summonInterval` |
| 542 | `boss.enrage.type_3.summon_waves` | `E3SummonWaves` |
| 543 | `boss.enrage.type_3.summon_count` | `E3SummonCount` |
| 544 | `boss.enrage.type_3.ring_interval` | `E3RingInterval` |
| 545 | `boss.enrage.type_3.ring_count` | `E3RingCount` |
| 546 | `boss.enrage.type_3.ring_speed` | `E3RingSpeed` |
| 547 | `boss.enrage.type_3.release_ring_count` | `E3ReleaseRingCount` |
| 548 | `boss.enrage.type_3.release_ring_speed` | `E3ReleaseRingSpeed` |
| 550 | `boss.ring_burst.bullet_speed` | `RingBurstSpeed` |
| 551 | `boss.bullet_damage.ring` | `BulletDamageRing` |
| 552 | `boss.movement.type4.bob_amp` | `Move4BobAmp` |
| 554 | `boss.movement.type4.bob_period` | `Move4BobPeriod` |
| 555 | `boss.enrage.type_4.ring_count` | `E4RingCount` |
| 556 | `boss.enrage.type_4.ring_interval` | `E4RingInterval` |
| 557 | `boss.enrage.type_4.ring_speed` | `E4RingSpeed` |
| 558 | `boss.enrage.type_4.precession_deg` | `E4PrecessionDeg` |
| 559 | `boss.enrage.type_4.release_ring_count` | `E4ReleaseRingCount` |
| 560 | `boss.enrage.type_4.release_ring_speed` | `E4ReleaseRingSpeed` |

### `csharp/godot/BuffSelect.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 142 | `buffs.explosive.unlock_boss_kills` | `3` |
| 164 | `buffs.dynamic_weight` | `new Godot.Collections.Dictionary(` |
| 487 | `buffs.extra_life.heal_on_pick` | `30` |
| 538 | `buffs.extra_life.heal_on_pick` | `30` |

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
| 100 | `elite_turret_event.duration` | `Duration` |
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
| 129 | `enemies.hover_band` | `new Godot.Collections.Array { HoverBand.X, HoverBand.Y }` |
| 113 | `enemies.bullet_speed` | `EnemyBulletSpeed` |
| 114 | `enemies.spread_bullet_speed` | `SpreadBulletSpeed` |
| 115 | `enemies.laser_bullet_speed` | `LaserBulletSpeed` |
| 116 | `enemies.bullet_damage.single` | `BulletDamageSingle` |
| 117 | `enemies.bullet_damage.spread` | `BulletDamageSpread` |
| 118 | `enemies.bullet_damage.laser` | `BulletDamageLaser` |
| 119 | `enemies.collision_damage` | `CollisionDamage` |
| 121 | `buffs.slow_field.factor` | `SlowFieldFactor` |
| 122 | `enemies.spread_fan_step` | `SpreadFanStep` |
| 124 | `enemies.lifetime` | `Lifetime` |
| 126 | `enemies.exit_accel` | `ExitAccel` |
| 127 | `enemies.aggressive_chase_speed` | `AggrChaseSpeed` |
| 141 | `enemies.hover_bob_amp` | `HoverBobAmp` |
| 142 | `enemies.hover_bob_freq` | `HoverBobFreq` |
| 143 | `enemies.hover_sway_amp` | `HoverSwayAmp` |
| 144 | `enemies.hover_sway_freq` | `HoverSwayFreq` |
| 145 | `enemies.spiral_drift_amp` | `SpiralDriftAmp` |
| 146 | `enemies.spiral_drift_freq` | `SpiralDriftFreq` |
| 147 | `enemies.spiral_radius` | `SpiralRadius` |
| 168 | `effects.shake.enemy_die` | `_shakeDieNormal` |
| 169 | `effects.shake.elite_die` | `_shakeDieElite` |

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
| 31 | `fog_events.fake_enemies.count` | `_count` |
| 33 | `fog_events.fake_enemies.spawn_interval` | `_spawnInterval` |

### `csharp/godot/FormationBomb.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 111 | `effects.shake.enemy_die` | `5.0` |

### `csharp/godot/FormationCraft.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 53 | `effects.shake.enemy_die` | `_shakeDie` |

### `csharp/godot/FormationStrikeEvent.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 91 | `formation_strike_event.craft_counts` | `CraftCounts` |
| 87 | `formation_strike_event.min_score` | `MinScore` |
| 88 | `formation_strike_event.cooldown` | `Cooldown` |
| 104 | `formation_strike_event.craft_hp_base` | `CraftHpBase` |
| 105 | `formation_strike_event.craft_score` | `CraftScore` |
| 108 | `formation_strike_event.approach_speed` | `ApproachSpeed` |
| 109 | `formation_strike_event.approach_y` | `ApproachY` |
| 112 | `formation_strike_event.turn_time` | `TurnTime` |
| 113 | `formation_strike_event.run_speed` | `RunSpeed` |
| 114 | `formation_strike_event.bomb_interval` | `BombInterval` |
| 116 | `formation_strike_event.bombs_per_craft` | `BombsPerCraft` |
| 117 | `formation_strike_event.bomb_fall_speed` | `BombFallSpeed` |
| 118 | `formation_strike_event.bomb_fuse` | `BombFuse` |
| 119 | `formation_strike_event.bomb_damage` | `BombDamage` |
| 120 | `formation_strike_event.bomb_radius` | `BombRadius` |
| 121 | `formation_strike_event.reward_all_clear` | `RewardAllClear` |

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

### `csharp/godot/GameState.State.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 65 | `world_scale` | `WorldScale` |
| 68 | `milestones.base` | `ScoreService.BuildMilestoneBase(` |
| 89 | `milestones.cycle_mult` | `_score.MilestoneCycleMult` |
| 93 | `progression.per_boss_kill` | `0.6` |
| 94 | `progression.per_ten_minutes` | `1.5` |
| 95 | `progression.time_step_seconds` | `30.0` |
| 98 | `difficulty` | `new Godot.Collections.Dictionary(` |
| 107 | `dda.duration` | `DDA_DURATION` |
| 108 | `dda.factor` | `DDA_FACTOR` |
| 115 | `scoring.combo.window` | `_score.ComboWindow` |
| 116 | `scoring.combo.step` | `_score.ComboStep` |
| 117 | `scoring.combo.max_mult` | `_score.ComboMaxMult` |
| 123 | `player.max_health` | `_combat.MaxHpBase` |
| 124 | `buffs.extra_life.max_hp_bonus` | `_combat.MaxHpBonus` |
| 125 | `buffs.lifesteal.max_hp_fraction` | `0.1` |
| 127 | `base_task.refresh_cost` | `REFRESH_COST` |
| 128 | `base_task.grant_per_visit` | `GRANT_PER_VISIT` |

### `csharp/godot/Hud.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 151 | `effects.hud_poll_interval` | `_pollInterval` |
| 153 | `effects.hit_flash.alpha` | `_hitFlashAlpha` |
| 154 | `effects.hit_flash.time` | `_hitFlashTime` |
| 155 | `effects.low_hp.ratio` | `_lowHpRatio` |
| 156 | `effects.low_hp.pulse_min` | `_lowHpPulseMin` |
| 157 | `effects.low_hp.pulse_max` | `_lowHpPulseMax` |
| 158 | `effects.low_hp.pulse_period` | `_lowHpPulsePeriod` |

### `csharp/godot/LaserWeapon.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 80 | `buffs.laser_beam.duration` | `BeamDuration` |
| 81 | `buffs.laser_beam.cooldown` | `CooldownDuration` |
| 83 | `buffs.laser_beam.tick_interval` | `TickInterval` |
| 85 | `buffs.laser_beam.tick_damage` | `TickDamage` |
| 87 | `buffs.laser_beam.length` | `BeamLength` |
| 88 | `buffs.laser_beam.half_width` | `BeamHalfWidth` |
| 89 | `buffs.laser_beam.hit_radius` | `EnemyHitRadius` |

### `csharp/godot/Main.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 106 | `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| 107 | `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| 108 | `effects.give_up_hold_time` | `GIVE_UP_HOLD_TIME` |
| 109 | `boss.enrage.slow_scale` | `ENRAGE_SLOW_SCALE` |
| 110 | `boss.enrage.bullet_time` | `ENRAGE_BULLET_TIME` |
| 111 | `boss.enrage.ramp_time` | `ENRAGE_RAMP_TIME` |
| 880 | `effects.mothership_summon.shake_gate` | `6.0` |
| 893 | `buffs.mothership_recall.cooldown_factor` | `0.5` |
| 949 | `mothership.depart_cooldown` | `60.0` |

### `csharp/godot/MetaHealthFX.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 321 | `effects.meta_health.crack.density` | `def` |
| 358 | `effects.meta_health.lod` | `0` |
| 359 | `effects.meta_health.pulse.scale` | `2.5f` |
| 360 | `effects.meta_health.pulse.min` | `0.15f` |
| 362 | `effects.meta_health.pulse.decay_tau` | `0.09f` |
| 363 | `effects.meta_health.chromatic.base` | `0.006f` |
| 364 | `effects.meta_health.chromatic.peak` | `0.014f` |
| 365 | `effects.meta_health.blur.strength` | `0.6f` |
| 367 | `effects.meta_health.ripple.duration` | `0.4f` |
| 368 | `effects.meta_health.ripple.alpha` | `0.8f` |
| 369 | `effects.meta_health.crack.exponent` | `1.6f` |
| 370 | `effects.meta_health.crack.spread_min` | `0.10f` |
| 371 | `effects.meta_health.crack.edge_softness` | `0.08f` |
| 372 | `effects.meta_health.crack.width` | `0.03f` |
| 373 | `effects.meta_health.crack.glow` | `0.8f` |
| 374 | `effects.meta_health.crack.heal_jitter` | `0.35f` |
| 375 | `effects.meta_health.crack.grow_overshoot` | `0.08f` |
| 377 | `effects.meta_health.crack.grow_time` | `0.6f` |
| 379 | `effects.meta_health.desat.max` | `0.35f` |
| 380 | `effects.meta_health.desat.exponent` | `2.0f` |
| 381 | `effects.meta_health.vignette.max_alpha` | `0.5f` |
| 382 | `effects.meta_health.vignette.inner` | `0.62f` |
| 383 | `effects.meta_health.vignette.dying_shrink` | `0.06f` |
| 384 | `effects.meta_health.dying.threshold` | `0.2f` |
| 385 | `effects.meta_health.dying.heart_min_hz` | `1.0f` |
| 386 | `effects.meta_health.dying.heart_max_hz` | `1.2f` |
| 387 | `effects.meta_health.dying.breath` | `0.015f` |
| 388 | `effects.meta_health.dying.jitter_px` | `2.0f` |
| 389 | `effects.meta_health.dying.warn_hz` | `2.5f` |
| 391 | `effects.meta_health.dying.fade` | `0.3f` |
| 393 | `effects.meta_health.smooth.down_tau` | `0.10f` |
| 395 | `effects.meta_health.smooth.up_tau` | `0.80f` |
| 396 | `effects.meta_health.adapt.interval` | `0.25f` |
| 397 | `effects.meta_health.adapt.min` | `0.8f` |
| 398 | `effects.meta_health.adapt.max` | `1.3f` |
| 399 | `effects.meta_health.adapt.bullet_weight` | `0.002f` |
| 400 | `effects.meta_health.adapt.explosion_weight` | `0.15f` |
| 401 | `effects.meta_health.reduce_flash.chromatic_scale` | `0.4f` |

### `csharp/godot/MetaService.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 83 | `meta.points.score_divisor` | `_metaScoreDivisor` |
| 84 | `meta.points.boss_kill_bonus` | `_metaBossKillBonus` |
| 85 | `meta.points.mission_bonus` | `_metaMissionBonus` |
| 87 | `meta.upgrades` | `new Godot.Collections.Dictionary(` |

### `csharp/godot/Mothership.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 226 | `mothership.hover_y` | `HoverY` |
| 227 | `mothership.release_invincible` | `ReleaseInvincible` |
| 228 | `mothership.dock_tween_time` | `DockTweenTime` |
| 229 | `mothership.dock_offset_y` | `DockOffsetY` |
| 231 | `mothership.resupply_delay` | `ResupplyDelay` |
| 232 | `mothership.release_time` | `ReleaseTime` |
| 233 | `mothership.release_drop` | `ReleaseDrop` |
| 237 | `mothership.mag_cells` | `MagCells` |
| 240 | `mothership.mag_cell_time` | `MagCellTime` |
| 241 | `mothership.mag_warn_cells` | `MagWarnCells` |
| 242 | `mothership.warn_eject_delay` | `WarnEjectDelay` |
| 245 | `mothership.early_hold_time` | `EarlyHoldTime` |
| 246 | `mothership.early_max_discount` | `EarlyMaxDiscount` |
| 247 | `mothership.early_prefill_max` | `EarlyPrefillMax` |
| 248 | `mothership.early_prefill_ratio` | `EarlyPrefillRatio` |
| 249 | `mothership.depart_cooldown` | `DepartCooldown` |
| 250 | `mothership.depart_start_speed` | `DepartStartSpeed` |
| 251 | `mothership.depart_accel` | `DepartAccel` |
| 252 | `mothership.drive.accel` | `DriveAccel` |
| 253 | `mothership.drive.max_speed` | `DriveMaxSpeed` |
| 257 | `mothership.drive.margin_x` | `DriveMarginX` |
| 259 | `mothership.drive.margin_top` | `DriveMarginTop` |
| 261 | `mothership.drive.margin_bottom` | `DriveMarginBottom` |
| 264 | `mothership.upgrade.threshold` | `_upgradeThreshold` |
| 265 | `mothership.upgrade.damage_mult` | `_upgradeDamageMult` |
| 266 | `mothership.upgrade.interval_mult` | `_upgradeIntervalMult` |
| 267 | `mothership.gatling.interval` | `GatlingInterval` |
| 268 | `mothership.gatling.bullet_speed` | `GatlingBulletSpeed` |
| 269 | `mothership.gatling.damage` | `GatlingDamage` |
| 270 | `mothership.gatling.score_scale` | `GatlingScoreScale` |
| 271 | `mothership.gatling.sweep_left_min` | `GatlingSweepLeftMin` |
| 272 | `mothership.gatling.sweep_left_max` | `GatlingSweepLeftMax` |
| 273 | `mothership.gatling.sweep_right_min` | `GatlingSweepRightMin` |
| 274 | `mothership.gatling.sweep_right_max` | `GatlingSweepRightMax` |
| 278 | `mothership.gatling.sweep_left_period` | `GatlingSweepLeftPeriod` |
| 280 | `mothership.gatling.sweep_right_period` | `GatlingSweepRightPeriod` |
| 282 | `mothership.gatling.sweep_right_phase` | `GatlingSweepRightPhase` |
| 284 | `mothership.missile.interval` | `MissileInterval` |
| 285 | `mothership.missile.damage` | `MissileDamage` |
| 286 | `mothership.missile.speed` | `MissileSpeed` |
| 287 | `mothership.missile.target_count` | `MissileTargetCount` |
| 288 | `mothership.missile.splash_damage` | `MissileSplashDamage` |
| 289 | `mothership.missile.splash_radius` | `MissileSplashRadius` |
| 292 | `effects.mothership_summon.warp_in_time` | `WarpInTime` |
| 293 | `effects.mothership_summon.warp_in_drop` | `WarpInDrop` |
| 295 | `effects.mothership_summon.slow.radius` | `SlowRadius` |
| 296 | `effects.mothership_summon.slow.duration` | `SlowDuration` |
| 297 | `effects.mothership_summon.slow.factor` | `SlowFactor` |
| 298 | `effects.mothership_summon.slow.ring_time` | `SlowRingTime` |
| 299 | `effects.mothership_summon.shake_slow` | `ShakeSlow` |
| 403 | `effects.mothership_summon.warp_in_drop` | `WarpInDrop` |
| 1059 | `effects.shake.mothership` | `4.0` |

### `csharp/godot/MothershipSummonWindow.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 88 | `effects.mothership_summon.window.open_time` | `OpenTime` |
| 89 | `effects.mothership_summon.window.close_time` | `CloseTime` |
| 91 | `effects.mothership_summon.window.shot_durations` | `Variant.From(_shotDurations` |

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
| 521 | `fog_events.bullet_malfunction.jitter_deg` | `20.0` |
| 522 | `fog_events.bullet_malfunction.misfire_chance` | `0.15` |
| 523 | `fog_events.bullet_malfunction.interval_jitter` | `0.3` |
| 937 | `player.aim_assist.homing_time` | `HomingTime` |
| 276 | `player.max_speed` | `MaxSpeed` |
| 277 | `player.accel` | `Accel` |
| 278 | `player.decel` | `Decel` |
| 279 | `player.boost_mult` | `BoostMult` |
| 280 | `player.fine_move_mult` | `FineMoveMult` |
| 282 | `player.base_fire_interval` | `BaseFireInterval` |
| 283 | `player.bullet_speed` | `BulletSpeed` |
| 285 | `buffs.crit_shot.chance` | `CritChanceBase` |
| 286 | `buffs.crit_shot.multiplier` | `CritMultiplier` |
| 287 | `player.bullet_spread_deg` | `BulletSpreadDeg` |
| 289 | `player.bullet_damage` | `BulletDamage` |
| 290 | `player.invincible_time` | `InvincibleTime` |
| 291 | `player.spawn_invincible_time` | `SpawnInvincibleTime` |
| 292 | `player.bullet_clear_radius` | `BulletClearRadius` |
| 294 | `player.entry.land_ratio` | `EntryLandRatio` |
| 295 | `player.entry.rush_time` | `EntryRushTime` |
| 296 | `player.entry.retreat_speed` | `EntryRetreatSpeed` |
| 297 | `player.entry.retreat_time` | `EntryRetreatTime` |
| 298 | `player.entry.invincible` | `EntryInvincible` |
| 299 | `player.entry.spawn_clearance` | `EntrySpawnClearance` |
| 300 | `player.entry.rush_hspeed_ratio` | `EntryRushHsRatio` |
| 303 | `buffs.armor.multiplier` | `ArmorMult` |
| 304 | `buffs.evasion.chance` | `EvasionChance` |
| 305 | `buffs.regen.heal_per_sec` | `RegenPerSec` |
| 306 | `effects.shake.player_hit` | `ShakeHit` |
| 310 | `player.fuel.max` | `FuelMax` |
| 313 | `player.fuel.drain` | `FuelDrain` |
| 314 | `player.fuel.regen` | `FuelRegen` |
| 315 | `player.fuel.restart` | `FuelRestart` |
| 317 | `player.dash.distance` | `DashDistance` |
| 319 | `player.dash.time` | `DashTime` |
| 323 | `player.dash.cooldown` | `DashCooldownMaxValue` |
| 324 | `player.dash.fuel_ratio` | `DashFuelRatio` |
| 325 | `player.dash.afterimage_interval` | `AfterimageInterval` |
| 327 | `player.graze_radius` | `GrazeRadius` |
| 328 | `player.graze_score` | `GrazeScore` |
| 330 | `player.parry.arc_deg` | `ParryArcDeg` |
| 331 | `player.parry.radius` | `ParryRadius` |
| 333 | `player.parry.duration` | `0.8f` |
| 334 | `player.parry.active_time` | `0.5f` |
| 335 | `player.parry.cooldown` | `3.0f` |
| 339 | `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| 340 | `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| 341 | `player.aim_assist.falloff.peak` | `_falloffPeak` |
| 342 | `player.aim_assist.falloff.end` | `_falloffEnd` |
| 343 | `player.aim_assist.falloff.min` | `_falloffMin` |
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
| 79 | `effects.return_skip_grace` | `SKIP_GRACE` |

### `csharp/godot/ScoreService.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 138 | `milestones.boss_kill_base` | `500.0` |

### `csharp/godot/SettingsService.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 474 | `player.aim_assist.joy_speed` | `JoyAimSpeed` |

### `csharp/godot/Spawner.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 136 | `spawner.wave_interval_start` | `WAVE_INTERVAL_START` |
| 137 | `spawner.wave_interval_end` | `WAVE_INTERVAL_END` |
| 138 | `spawner.ramp_time` | `RAMP_TIME` |
| 139 | `spawner.interval_min` | `INTERVAL_MIN` |
| 142 | `spawner.boss_score_step` | `BOSS_SCORE_STEP` |
| 144 | `spawner.boss_min_interval` | `BOSS_MIN_INTERVAL` |
| 145 | `spawner.boss_time_limit` | `BOSS_TIME_LIMIT` |
| 146 | `spawner.difficulty_factor` | `DIFFICULTY_FACTOR` |
| 148 | `spawner.unlock_scores` | `UNLOCK_SCORES` |
| 169 | `spawner.wave_size_start` | `WAVE_SIZE_START` |
| 170 | `spawner.wave_size_end` | `WAVE_SIZE_END` |
| 171 | `spawner.special_gap_min` | `SPECIAL_GAP_MIN` |
| 172 | `spawner.special_gap_max` | `SPECIAL_GAP_MAX` |
| 174 | `spawner.rest_waves_after_kill` | `REST_WAVES_AFTER_KILL` |
| 177 | `spawner.elite_wave_size` | `ELITE_WAVE_SIZE` |
| 179 | `enemies.hover_band` | `new Godot.Collections.Array { _hoverBand.X, _hoverBand.Y }` |
| 191 | `enemies.types` | `new Godot.Collections.Array(` |
| 201 | `elites.types` | `new Godot.Collections.Array(` |
| 439 | `effects.shake.boss_warning` | `14.0` |

### `csharp/godot/Starfield.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 52 | `effects.starfield.far_count` | `_farCount` |
| 58 | `effects.starfield.near_count` | `_nearCount` |
| 64 | `effects.starfield.far_speed` | `_farSpeed` |
| 70 | `effects.starfield.near_speed` | `_nearSpeed` |

### `csharp/godot/StrikeCarrier.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 68 | `elite_turret_event.carrier.retreat_start_speed` | `RetreatStartSpeed` |
| 69 | `elite_turret_event.carrier.retreat_accel` | `RetreatAccel` |

### `csharp/godot/TurretBattery.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 104 | `enemies.bullet_speed` | `SingleSpeed` |
| 105 | `enemies.spread_bullet_speed` | `SpreadSpeed` |
| 106 | `enemies.laser_bullet_speed` | `LaserSpeed` |
| 107 | `boss.homing_bullet_speed` | `HomingSpeed` |
| 108 | `boss.sniper_bullet_speed` | `SniperSpeed` |
| 109 | `enemies.spread_fan_step` | `SpreadFanStep` |
| 110 | `enemies.bullet_damage.single` | `DmgSingle` |
| 111 | `enemies.bullet_damage.spread` | `DmgSpread` |
| 112 | `enemies.bullet_damage.laser` | `DmgLaser` |
| 113 | `boss.bullet_damage.homing` | `DmgHoming` |
| 114 | `boss.bullet_damage.sniper` | `DmgSniper` |
| 135 | `effects.shake.enemy_die` | `_shakeDie` |

### `csharp/godot/Tutorial.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 117 | `effects.home_charge_time` | `HomeChargeTime` |
| 118 | `mothership.dock_charge_time` | `DockChargeTime` |
| 242 | `tutorial.boss_hp` | `120.0` |
| 371 | `mothership.hover_y` | `270.0` |

### `csharp/godot/WarpGate.cs`

| 行 | json 键路径 | 脚本回退值 |
| --- | --- | --- |
| 48 | `effects.mothership_summon.gate.open_time` | `OPEN_TIME` |
| 49 | `effects.mothership_summon.gate.close_time` | `CLOSE_TIME` |
| 50 | `effects.mothership_summon.gate.radius` | `RADIUS` |

## 动态拼接键前缀

- `boss.phases.type…`
- `buffs.…`
- `player.aim_assist.levels.…`

## json 中存在但脚本未静态引用的键

（经动态键或整段读取覆盖的不列出；剩下的请人工判断是否为死键）

- `version`

## 脚本引用但 json 缺失的键（走回退值，建议补进 json 或确认为有意兜底）

