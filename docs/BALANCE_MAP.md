# BALANCE_MAP — 数值位置地图

> 本文件由 `python3 scripts/tools/gen_balance_map.py` 扫描生成，请勿手改；
> 新增/改名数值键或调整 cfg() 调用后重新运行生成器。

## 怎么改数值

- 运行时数值的唯一来源是 `data/balance.json`；推荐用 `python3 scripts/tools/balance_editor.py` 在浏览器里编辑（改动高亮、类型校验、自动备份）。
- 代码侧的 `GameState.Instance.Cfg("键路径", 回退值)` / `CfgFx.Float/Int("键路径", 回退值)` 仅在 json 缺键/损坏时兜底；新增或调整数值按 AGENTS.md 约定保持 json 与回退值一致。
- 高频 `_Process` 路径的数值在 `_Ready()`/`LoadBalance()` 一次缓存，不要每帧查。

## 静态 cfg() 调用点（按文件分组）

### `csharp\godot\AimFrameLayer.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| `player.aim_assist.falloff.peak` | `_falloffPeak` |
| `player.aim_assist.falloff.end` | `_falloffEnd` |
| `player.aim_assist.falloff.min` | `_falloffMin` |

### `csharp\godot\BalanceService.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `enemies.hp_ramp_factor` | `0.25` |
| `enemies.damage_ramp_factor` | `0.20` |
| `enemies.move_strategies` | `new Godot.Collections.Dictionary(` |
| `enemies.speed_ramp_factor` | `0.1` |
| `player.aim_assist.mark_ratio` | `0.25` |
| `spawner.telegraph_duration` | `SpawnTelegraph.GetDefaultDuration(` |

### `csharp\godot\Boss.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `boss.strafe_speeds` | `StrafeSpeeds` |
| `boss.fire_intervals` | `FireIntervals` |
| `boss.phases.clear_on_shift` | `ClearOnShift` |
| `boss.difficulty_scaling.interval_mult` | `DiffIntervalMult` |
| `boss.difficulty_scaling.speed_mult` | `DiffSpeedMult` |
| `boss.difficulty_scaling.counts` | `DiffCountDeltas` |
| `boss.hp_mults` | `new Godot.Collections.Array { 1.3, 0.7, 1.6, 1.2 }` |
| `boss.hp_base` | `HpBase` |
| `effects.shake.enrage` | `16.0` |
| `effects.shake.enrage` | `16.0` |
| `boss.enter_speed` | `EnterSpeed` |
| `boss.fight_y` | `FightY` |
| `boss.strafe_min_x` | `StrafeMinX` |
| `boss.strafe_max_x` | `StrafeMaxX` |
| `boss.phase2_hp_ratio` | `Phase2HpRatio` |
| `boss.enrage.hp_ratio` | `EnrageHpRatio` |
| `boss.enrage.rate_mult` | `EnrageRateMult` |
| `boss.enrage.speed_mult` | `EnrageSpeedMult` |
| `boss.enrage.player_slow` | `EnragePlayerSlow` |
| `boss.enrage.snapshot_lasers` | `EnrageSnapshotLasers` |
| `boss.enrage.snapshot_ring` | `EnrageSnapshotRing` |
| `boss.enrage.laser_speed` | `EnrageLaserSpeed` |
| `boss.enrage.ring_speed` | `EnrageRingSpeed` |
| `boss.enrage.duration` | `EnrageDuration` |
| `boss.enrage.transition_duration` | `EnrageTransitionDuration` |
| `boss.enrage.attack_interval` | `EnrageAttackInterval` |
| `boss.enrage.attack_windup` | `EnrageAttackWindup` |
| `boss.enrage.release_interval` | `EnrageReleaseInterval` |
| `boss.enrage.release_hold_duration` | `EnrageReleaseHoldDuration` |
| `boss.enrage.return_duration` | `EnrageReturnDuration` |
| `boss.enrage.path_radius_scale` | `EnragePathRadiusScale` |
| `boss.enrage.square_path_ratio` | `EnrageSquarePathRatio` |
| `boss.enrage.release_laser_speed` | `EnrageReleaseLaserSpeed` |
| `boss.enrage.release_ring_speed` | `EnrageReleaseRingSpeed` |
| `boss.escape.time` | `EscapeTime` |
| `boss.escape.warning` | `EscapeWarning` |
| `boss.escape.drift` | `EscapeDrift` |
| `boss.escape.start_speed` | `EscapeStartSpeed` |
| `boss.escape.accel` | `EscapeAccel` |
| `boss.escape.countdown_visible_from` | `EscapeCountdownFrom` |
| `boss.hp_base` | `HpBase` |
| `boss.fan_bullet_speed` | `FanBulletSpeed` |
| `boss.homing_bullet_speed` | `HomingBulletSpeed` |
| `boss.sniper_bullet_speed` | `SniperBulletSpeed` |
| `boss.cross_bullet_speed` | `CrossBulletSpeed` |
| `boss.collision_damage` | `CollisionDamage` |
| `buffs.slow_field.factor` | `SlowFieldFactor` |
| `boss.bullet_damage.fan` | `BulletDamageFan` |
| `boss.bullet_damage.homing` | `BulletDamageHoming` |
| `boss.bullet_damage.sniper` | `BulletDamageSniper` |
| `boss.bullet_damage.cross` | `BulletDamageCross` |
| `boss.bullet_damage.snapshot_laser` | `BulletDamageSnapshotLaser` |
| `boss.bullet_damage.snapshot_ring` | `BulletDamageSnapshotRing` |
| `boss.phases.phase_shift_duration` | `PhaseShiftDuration` |
| `boss.phases.transition_invincible` | `TransitionInvincible` |
| `boss.phases.telegraph.sniper_aim` | `SniperAimTime` |
| `boss.phases.telegraph.sniper_track` | `SniperTrackTime` |
| `boss.phases.attacks.sniper3.burst_interval` | `SniperBurstInterval` |
| `boss.phases.press_interval` | `PressInterval` |
| `boss.phases.press_depth` | `PressDepth` |
| `boss.movement.type1_p2_strafe` | `Type1P2Strafe` |
| `boss.movement.type1_p2_bob_amp` | `Type1P2BobAmp` |
| `boss.movement.type1_p2_bob_period` | `Type1P2BobPeriod` |
| `boss.movement.type2_p2_dash_time` | `Type2P2DashTime` |
| `boss.movement.type2_p2_rest_time` | `Type2P2RestTime` |
| `boss.movement.type3_p1_bob_min` | `Type3P1BobMin` |
| `boss.movement.type3_p1_bob_max` | `Type3P1BobMax` |
| `boss.movement.type3_p1_bob_period` | `Type3P1BobPeriod` |
| `boss.movement.type3_p2_strafe` | `Type3P2Strafe` |
| `boss.movement.type3_p2_bob_amp` | `Type3P2BobAmp` |
| `boss.movement.type3_p2_bob_period` | `Type3P2BobPeriod` |
| `boss.phases.attacks.charged_cannon.charge` | `CannonCharge` |
| `boss.phases.attacks.charged_cannon.shots` | `CannonShots` |
| `boss.phases.attacks.charged_cannon.interval` | `CannonInterval` |
| `boss.phases.attacks.charged_cannon.bullet_speed` | `CannonBulletSpeed` |
| `boss.phases.attacks.charged_cannon.damage` | `CannonDamage` |
| `boss.phases.attacks.charged_cannon.flash` | `CannonFlash` |
| `boss.phases.attacks.dash_sweep.aim` | `SweepAim` |
| `boss.phases.attacks.dash_sweep.speed` | `SweepSpeed` |
| `boss.phases.attacks.dash_sweep.drop_count` | `SweepDropCount` |
| `boss.phases.attacks.dash_sweep.drop_speed` | `SweepDropSpeed` |
| `boss.phases.attacks.dash_sweep.drop_damage` | `SweepDropDamage` |
| `boss.phases.attacks.dash_sweep.return_duration` | `SweepReturnDuration` |
| `boss.phases.attacks.minion_volley.count` | `VolleyCount` |
| `boss.phases.attacks.minion_volley.delay` | `VolleyDelay` |
| `boss.phases.attacks.minion_volley.bullet_speed` | `VolleyBulletSpeed` |
| `boss.phases.attacks.minion_volley.bullet_damage` | `VolleyBulletDamage` |
| `boss.phases.attacks.bullet_wall.count` | `WallCount` |
| `boss.phases.attacks.bullet_wall.bullet_speed` | `WallBulletSpeed` |
| `boss.phases.attacks.bullet_wall.damage` | `WallDamage` |
| `boss.phases.attacks.bullet_wall.arc_deg` | `WallArcDeg` |
| `boss.enrage.type_1.ring_interval` | `E1RingInterval` |
| `boss.enrage.type_1.ring_count` | `E1RingCount` |
| `boss.enrage.type_1.ring_speed` | `E1RingSpeed` |
| `boss.enrage.type_1.ring_precession_deg` | `E1RingPrecessionDeg` |
| `boss.enrage.type_1.salvo_charge` | `E1SalvoCharge` |
| `boss.enrage.type_1.salvo_count` | `E1SalvoCount` |
| `boss.enrage.type_1.salvo_speed` | `E1SalvoSpeed` |
| `boss.enrage.type_1.salvo_damage` | `E1SalvoDamage` |
| `boss.enrage.type_2.point_count` | `E2PointCount` |
| `boss.enrage.type_2.point_interval` | `E2PointInterval` |
| `boss.enrage.type_2.aim` | `E2Aim` |
| `boss.enrage.type_2.sniper_speed` | `E2SniperSpeed` |
| `boss.enrage.type_2.sniper_damage` | `E2SniperDamage` |
| `boss.enrage.type_2.release_ring_count` | `E2ReleaseRingCount` |
| `boss.enrage.type_2.release_ring_speed` | `E2ReleaseRingSpeed` |
| `boss.enrage.type_3.summon_interval` | `E3SummonInterval` |
| `boss.phases.type3.summon_interval` | `_summonInterval` |
| `boss.enrage.type_3.summon_waves` | `E3SummonWaves` |
| `boss.enrage.type_3.summon_count` | `E3SummonCount` |
| `boss.enrage.type_3.ring_interval` | `E3RingInterval` |
| `boss.enrage.type_3.ring_count` | `E3RingCount` |
| `boss.enrage.type_3.ring_speed` | `E3RingSpeed` |
| `boss.enrage.type_3.release_ring_count` | `E3ReleaseRingCount` |
| `boss.enrage.type_3.release_ring_speed` | `E3ReleaseRingSpeed` |
| `boss.ring_burst.bullet_speed` | `RingBurstSpeed` |
| `boss.bullet_damage.ring` | `BulletDamageRing` |
| `boss.movement.type4.bob_amp` | `Move4BobAmp` |
| `boss.movement.type4.bob_period` | `Move4BobPeriod` |
| `boss.enrage.type_4.ring_count` | `E4RingCount` |
| `boss.enrage.type_4.ring_interval` | `E4RingInterval` |
| `boss.enrage.type_4.ring_speed` | `E4RingSpeed` |
| `boss.enrage.type_4.precession_deg` | `E4PrecessionDeg` |
| `boss.enrage.type_4.release_ring_count` | `E4ReleaseRingCount` |
| `boss.enrage.type_4.release_ring_speed` | `E4ReleaseRingSpeed` |

### `csharp\godot\BuffSelect.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `buffs.explosive.unlock_boss_kills` | `3` |
| `buffs.dynamic_weight` | `new Godot.Collections.Dictionary(` |
| `buffs.extra_life.heal_on_pick` | `30` |
| `buffs.extra_life.heal_on_pick` | `30` |

### `csharp\godot\Bullet.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `buffs.explosive.radius_per_level` | `ExplosiveRadius` |
| `buffs.explosive.damage_per_level` | `ExplosiveDamage` |
| `effects.bullet_visual_scale` | `VisualScale` |
| `effects.enemy_bullet_visual_scale` | `EnemyVisualScale` |
| `player.grace_period` | `GracePeriod` |
| `player.parry.reflect_speed_mult` | `ReflectSpeedMult` |
| `player.parry.reflect_damage_mult` | `ReflectDamageMult` |

### `csharp\godot\CameraShake.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.shake.decay` | `_decay` |

### `csharp\godot\DirectionShiftEvent.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `fog_events.direction_shift.shift_interval` | `_interval` |
| `fog_events.direction_shift.hold_time` | `_hold` |

### `csharp\godot\EliteTurretEvent.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `elite_turret_event.duration` | `Duration` |
| `elite_turret_event.enter_time` | `EnterTime` |
| `elite_turret_event.rise_time` | `RiseTime` |
| `elite_turret_event.boss_resume_delay` | `BossResumeDelay` |
| `elite_turret_event.turret_hp_base` | `TurretHpBase` |
| `elite_turret_event.turret_counts` | `TurretCounts` |
| `elite_turret_event.ammo_sequences` | `AmmoSequences` |
| `elite_turret_event.fire_interval` | `new Godot.Collections.Array { FireInterval.X, FireInterva...` |
| `elite_turret_event.weak_lock` | `WeakLock` |
| `elite_turret_event.reward_score` | `RewardScore` |
| `elite_turret_event.carrier.hover_y` | `HoverY` |
| `elite_turret_event.cooldown` | `Cooldown` |
| `elite_turret_event.carrier.shake` | `4.0` |

### `csharp\godot\Enemy.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `enemies.hover_band` | `new Godot.Collections.Array { HoverBand.X, HoverBand.Y }` |
| `enemies.bullet_speed` | `EnemyBulletSpeed` |
| `enemies.spread_bullet_speed` | `SpreadBulletSpeed` |
| `enemies.laser_bullet_speed` | `LaserBulletSpeed` |
| `enemies.bullet_damage.single` | `BulletDamageSingle` |
| `enemies.bullet_damage.spread` | `BulletDamageSpread` |
| `enemies.bullet_damage.laser` | `BulletDamageLaser` |
| `enemies.collision_damage` | `CollisionDamage` |
| `buffs.slow_field.factor` | `SlowFieldFactor` |
| `enemies.spread_fan_step` | `SpreadFanStep` |
| `enemies.lifetime` | `Lifetime` |
| `enemies.exit_accel` | `ExitAccel` |
| `enemies.aggressive_chase_speed` | `AggrChaseSpeed` |
| `enemies.hover_bob_amp` | `HoverBobAmp` |
| `enemies.hover_bob_freq` | `HoverBobFreq` |
| `enemies.hover_sway_amp` | `HoverSwayAmp` |
| `enemies.hover_sway_freq` | `HoverSwayFreq` |
| `enemies.spiral_drift_amp` | `SpiralDriftAmp` |
| `enemies.spiral_drift_freq` | `SpiralDriftFreq` |
| `enemies.spiral_radius` | `SpiralRadius` |
| `effects.shake.enemy_die` | `_shakeDieNormal` |
| `effects.shake.elite_die` | `_shakeDieElite` |

### `csharp\godot\Explosion.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.explosion.pool_cap` | `PoolCap` |
| `effects.explosion_visual_scale` | `1.6` |
| `effects.shake.boss_seq_initial` | `20.0` |
| `effects.shake.boss_seq_step` | `8.0` |
| `effects.shake.boss_seq_final` | `24.0` |
| `effects.explosion.amount` | `24` |
| `effects.explosion.debris_amount` | `10` |

### `csharp\godot\FakeEnemiesEvent.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `fog_events.fake_enemies.count` | `_count` |
| `fog_events.fake_enemies.spawn_interval` | `_spawnInterval` |

### `csharp\godot\FormationBomb.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.shake.enemy_die` | `5.0` |

### `csharp\godot\FormationCraft.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.shake.enemy_die` | `_shakeDie` |

### `csharp\godot\FormationStrikeEvent.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `formation_strike_event.craft_counts` | `CraftCounts` |
| `formation_strike_event.min_score` | `MinScore` |
| `formation_strike_event.cooldown` | `Cooldown` |
| `formation_strike_event.craft_hp_base` | `CraftHpBase` |
| `formation_strike_event.craft_score` | `CraftScore` |
| `formation_strike_event.approach_speed` | `ApproachSpeed` |
| `formation_strike_event.approach_y` | `ApproachY` |
| `formation_strike_event.turn_time` | `TurnTime` |
| `formation_strike_event.run_speed` | `RunSpeed` |
| `formation_strike_event.bomb_interval` | `BombInterval` |
| `formation_strike_event.bombs_per_craft` | `BombsPerCraft` |
| `formation_strike_event.bomb_fall_speed` | `BombFallSpeed` |
| `formation_strike_event.bomb_fuse` | `BombFuse` |
| `formation_strike_event.bomb_damage` | `BombDamage` |
| `formation_strike_event.bomb_radius` | `BombRadius` |
| `formation_strike_event.reward_all_clear` | `RewardAllClear` |

### `csharp\godot\GameEventManager.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `fog_events.enabled` | `FOG_ENABLED` |
| `fog_events.trigger_chance` | `FOG_TRIGGER_CHANCE` |
| `fog_events.check_interval` | `FOG_CHECK_INTERVAL` |
| `fog_events.min_interval` | `FOG_MIN_INTERVAL` |
| `fog_events.first_delay` | `FOG_FIRST_DELAY` |
| `fog_events.weights` | `FOG_WEIGHTS` |
| `fog_events.durations` | `FOG_EVENT_DURATIONS` |
| `elite_turret_event.trigger_interval` | `45.0` |
| `elite_turret_event.trigger_chance` | `0.35` |
| `elite_turret_event.min_score` | `800` |
| `formation_strike_event.trigger_interval` | `40.0` |
| `formation_strike_event.trigger_chance` | `0.30` |
| `formation_strike_event.min_score` | `500` |

### `csharp\godot\GameState.State.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `world_scale` | `WorldScale` |
| `milestones.base` | `ScoreService.BuildMilestoneBase(` |
| `milestones.cycle_mult` | `_score.MilestoneCycleMult` |
| `progression.per_boss_kill` | `0.6` |
| `progression.per_ten_minutes` | `1.5` |
| `progression.time_step_seconds` | `30.0` |
| `difficulty` | `new Godot.Collections.Dictionary(` |
| `dda.duration` | `DDA_DURATION` |
| `dda.factor` | `DDA_FACTOR` |
| `scoring.combo.window` | `_score.ComboWindow` |
| `scoring.combo.step` | `_score.ComboStep` |
| `scoring.combo.max_mult` | `_score.ComboMaxMult` |
| `player.max_health` | `_combat.MaxHpBase` |
| `buffs.extra_life.max_hp_bonus` | `_combat.MaxHpBonus` |
| `buffs.lifesteal.max_hp_fraction` | `0.1` |
| `base_task.refresh_cost` | `REFRESH_COST` |
| `base_task.grant_per_visit` | `GRANT_PER_VISIT` |

### `csharp\godot\Hud.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.hud_poll_interval` | `_pollInterval` |
| `effects.hit_flash.alpha` | `_hitFlashAlpha` |
| `effects.hit_flash.time` | `_hitFlashTime` |
| `effects.low_hp.ratio` | `_lowHpRatio` |
| `effects.low_hp.pulse_min` | `_lowHpPulseMin` |
| `effects.low_hp.pulse_max` | `_lowHpPulseMax` |
| `effects.low_hp.pulse_period` | `_lowHpPulsePeriod` |

### `csharp\godot\LaserWeapon.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `buffs.laser_beam.duration` | `BeamDuration` |
| `buffs.laser_beam.cooldown` | `CooldownDuration` |
| `buffs.laser_beam.tick_interval` | `TickInterval` |
| `buffs.laser_beam.tick_damage` | `TickDamage` |
| `buffs.laser_beam.length` | `BeamLength` |
| `buffs.laser_beam.half_width` | `BeamHalfWidth` |
| `buffs.laser_beam.hit_radius` | `EnemyHitRadius` |

### `csharp\godot\Main.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `mothership.dock_charge_time` | `DOCK_CHARGE_TIME` |
| `effects.home_charge_time` | `HOME_CHARGE_TIME` |
| `effects.give_up_hold_time` | `GIVE_UP_HOLD_TIME` |
| `boss.enrage.slow_scale` | `ENRAGE_SLOW_SCALE` |
| `boss.enrage.bullet_time` | `ENRAGE_BULLET_TIME` |
| `boss.enrage.ramp_time` | `ENRAGE_RAMP_TIME` |
| `effects.mothership_summon.shake_gate` | `6.0` |
| `buffs.mothership_recall.cooldown_factor` | `0.5` |
| `mothership.depart_cooldown` | `60.0` |

### `csharp\godot\MetaHealthFX.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.meta_health.crack.density` | `def` |
| `effects.meta_health.lod` | `0` |
| `effects.meta_health.pulse.scale` | `2.5f` |
| `effects.meta_health.pulse.min` | `0.15f` |
| `effects.meta_health.pulse.decay_tau` | `0.09f` |
| `effects.meta_health.chromatic.base` | `0.006f` |
| `effects.meta_health.chromatic.peak` | `0.014f` |
| `effects.meta_health.blur.strength` | `0.6f` |
| `effects.meta_health.ripple.duration` | `0.4f` |
| `effects.meta_health.ripple.alpha` | `0.8f` |
| `effects.meta_health.crack.exponent` | `1.6f` |
| `effects.meta_health.crack.spread_min` | `0.10f` |
| `effects.meta_health.crack.edge_softness` | `0.08f` |
| `effects.meta_health.crack.width` | `0.03f` |
| `effects.meta_health.crack.glow` | `0.8f` |
| `effects.meta_health.crack.heal_jitter` | `0.35f` |
| `effects.meta_health.crack.grow_overshoot` | `0.08f` |
| `effects.meta_health.crack.grow_time` | `0.6f` |
| `effects.meta_health.desat.max` | `0.35f` |
| `effects.meta_health.desat.exponent` | `2.0f` |
| `effects.meta_health.vignette.max_alpha` | `0.5f` |
| `effects.meta_health.vignette.inner` | `0.62f` |
| `effects.meta_health.vignette.dying_shrink` | `0.06f` |
| `effects.meta_health.dying.threshold` | `0.2f` |
| `effects.meta_health.dying.heart_min_hz` | `1.0f` |
| `effects.meta_health.dying.heart_max_hz` | `1.2f` |
| `effects.meta_health.dying.breath` | `0.015f` |
| `effects.meta_health.dying.jitter_px` | `2.0f` |
| `effects.meta_health.dying.warn_hz` | `2.5f` |
| `effects.meta_health.dying.fade` | `0.3f` |
| `effects.meta_health.smooth.down_tau` | `0.10f` |
| `effects.meta_health.smooth.up_tau` | `0.80f` |
| `effects.meta_health.adapt.interval` | `0.25f` |
| `effects.meta_health.adapt.min` | `0.8f` |
| `effects.meta_health.adapt.max` | `1.3f` |
| `effects.meta_health.adapt.bullet_weight` | `0.002f` |
| `effects.meta_health.adapt.explosion_weight` | `0.15f` |
| `effects.meta_health.reduce_flash.chromatic_scale` | `0.4f` |

### `csharp\godot\MetaService.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `meta.points.score_divisor` | `_metaScoreDivisor` |
| `meta.points.boss_kill_bonus` | `_metaBossKillBonus` |
| `meta.points.mission_bonus` | `_metaMissionBonus` |
| `meta.upgrades` | `new Godot.Collections.Dictionary(` |

### `csharp\godot\Mothership.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `mothership.hover_y` | `HoverY` |
| `mothership.release_invincible` | `ReleaseInvincible` |
| `mothership.dock_tween_time` | `DockTweenTime` |
| `mothership.dock_offset_y` | `DockOffsetY` |
| `mothership.resupply_delay` | `ResupplyDelay` |
| `mothership.release_time` | `ReleaseTime` |
| `mothership.release_drop` | `ReleaseDrop` |
| `mothership.mag_cells` | `MagCells` |
| `mothership.mag_cell_time` | `MagCellTime` |
| `mothership.mag_warn_cells` | `MagWarnCells` |
| `mothership.warn_eject_delay` | `WarnEjectDelay` |
| `mothership.early_hold_time` | `EarlyHoldTime` |
| `mothership.early_max_discount` | `EarlyMaxDiscount` |
| `mothership.early_prefill_max` | `EarlyPrefillMax` |
| `mothership.early_prefill_ratio` | `EarlyPrefillRatio` |
| `mothership.depart_cooldown` | `DepartCooldown` |
| `mothership.depart_start_speed` | `DepartStartSpeed` |
| `mothership.depart_accel` | `DepartAccel` |
| `mothership.drive.accel` | `DriveAccel` |
| `mothership.drive.max_speed` | `DriveMaxSpeed` |
| `mothership.drive.margin_x` | `DriveMarginX` |
| `mothership.drive.margin_top` | `DriveMarginTop` |
| `mothership.drive.margin_bottom` | `DriveMarginBottom` |
| `mothership.upgrade.threshold` | `_upgradeThreshold` |
| `mothership.upgrade.damage_mult` | `_upgradeDamageMult` |
| `mothership.upgrade.interval_mult` | `_upgradeIntervalMult` |
| `mothership.gatling.interval` | `GatlingInterval` |
| `mothership.gatling.bullet_speed` | `GatlingBulletSpeed` |
| `mothership.gatling.damage` | `GatlingDamage` |
| `mothership.gatling.score_scale` | `GatlingScoreScale` |
| `mothership.gatling.sweep_left_min` | `GatlingSweepLeftMin` |
| `mothership.gatling.sweep_left_max` | `GatlingSweepLeftMax` |
| `mothership.gatling.sweep_right_min` | `GatlingSweepRightMin` |
| `mothership.gatling.sweep_right_max` | `GatlingSweepRightMax` |
| `mothership.gatling.sweep_left_period` | `GatlingSweepLeftPeriod` |
| `mothership.gatling.sweep_right_period` | `GatlingSweepRightPeriod` |
| `mothership.gatling.sweep_right_phase` | `GatlingSweepRightPhase` |
| `mothership.missile.interval` | `MissileInterval` |
| `mothership.missile.damage` | `MissileDamage` |
| `mothership.missile.speed` | `MissileSpeed` |
| `mothership.missile.target_count` | `MissileTargetCount` |
| `mothership.missile.splash_damage` | `MissileSplashDamage` |
| `mothership.missile.splash_radius` | `MissileSplashRadius` |
| `effects.mothership_summon.warp_in_time` | `WarpInTime` |
| `effects.mothership_summon.warp_in_drop` | `WarpInDrop` |
| `effects.mothership_summon.slow.radius` | `SlowRadius` |
| `effects.mothership_summon.slow.duration` | `SlowDuration` |
| `effects.mothership_summon.slow.factor` | `SlowFactor` |
| `effects.mothership_summon.slow.ring_time` | `SlowRingTime` |
| `effects.mothership_summon.shake_slow` | `ShakeSlow` |
| `effects.mothership_summon.warp_in_drop` | `WarpInDrop` |
| `effects.shake.mothership` | `4.0` |

### `csharp\godot\MothershipSummonWindow.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.mothership_summon.window.open_time` | `OpenTime` |
| `effects.mothership_summon.window.close_time` | `CloseTime` |
| `effects.mothership_summon.window.shot_durations` | `Variant.From(_shotDurations` |

### `csharp\godot\OrbitalStrike.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.orbital_strike.duration` | `DURATION` |
| `effects.orbital_strike.impact_at` | `IMPACT_AT` |
| `effects.orbital_strike.missile_from` | `MISSILE_FROM` |
| `effects.orbital_strike.reticle_radius` | `RETICLE_RADIUS` |
| `effects.orbital_strike.impact_y_ratio` | `IMPACT_Y_RATIO` |
| `effects.shake.boss_seq_final` | `24.0` |
| `effects.shake.boss_seq_final` | `24.0` |

### `csharp\godot\Player.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `fog_events.bullet_malfunction.jitter_deg` | `20.0` |
| `fog_events.bullet_malfunction.misfire_chance` | `0.15` |
| `fog_events.bullet_malfunction.interval_jitter` | `0.3` |
| `player.aim_assist.homing_time` | `HomingTime` |
| `player.max_speed` | `MaxSpeed` |
| `player.accel` | `Accel` |
| `player.decel` | `Decel` |
| `player.boost_mult` | `BoostMult` |
| `player.fine_move_mult` | `FineMoveMult` |
| `player.base_fire_interval` | `BaseFireInterval` |
| `player.bullet_speed` | `BulletSpeed` |
| `buffs.crit_shot.chance` | `CritChanceBase` |
| `buffs.crit_shot.multiplier` | `CritMultiplier` |
| `player.bullet_spread_deg` | `BulletSpreadDeg` |
| `player.bullet_damage` | `BulletDamage` |
| `player.invincible_time` | `InvincibleTime` |
| `player.spawn_invincible_time` | `SpawnInvincibleTime` |
| `player.bullet_clear_radius` | `BulletClearRadius` |
| `player.entry.land_ratio` | `EntryLandRatio` |
| `player.entry.rush_time` | `EntryRushTime` |
| `player.entry.retreat_speed` | `EntryRetreatSpeed` |
| `player.entry.retreat_time` | `EntryRetreatTime` |
| `player.entry.invincible` | `EntryInvincible` |
| `player.entry.spawn_clearance` | `EntrySpawnClearance` |
| `player.entry.rush_hspeed_ratio` | `EntryRushHsRatio` |
| `buffs.armor.multiplier` | `ArmorMult` |
| `buffs.evasion.chance` | `EvasionChance` |
| `buffs.regen.heal_per_sec` | `RegenPerSec` |
| `effects.shake.player_hit` | `ShakeHit` |
| `effects.player_damage_frame.light_ratio` | `_damageLightRatio` |
| `effects.player_damage_frame.heavy_ratio` | `_damageHeavyRatio` |
| `player.fuel.max` | `FuelMax` |
| `player.fuel.drain` | `FuelDrain` |
| `player.fuel.regen` | `FuelRegen` |
| `player.fuel.restart` | `FuelRestart` |
| `player.dash.distance` | `DashDistance` |
| `player.dash.time` | `DashTime` |
| `player.dash.cooldown` | `DashCooldownMaxValue` |
| `player.dash.fuel_ratio` | `DashFuelRatio` |
| `player.dash.afterimage_interval` | `AfterimageInterval` |
| `player.graze_radius` | `GrazeRadius` |
| `player.graze_score` | `GrazeScore` |
| `player.parry.arc_deg` | `ParryArcDeg` |
| `player.parry.radius` | `ParryRadius` |
| `player.parry.duration` | `0.8f` |
| `player.parry.active_time` | `0.5f` |
| `player.parry.cooldown` | `3.0f` |
| `player.aim_assist.input.magnet_input_min` | `_magnetInputMin` |
| `player.aim_assist.input.magnet_input_full` | `_magnetInputFull` |
| `player.aim_assist.falloff.peak` | `_falloffPeak` |
| `player.aim_assist.falloff.end` | `_falloffEnd` |
| `player.aim_assist.falloff.min` | `_falloffMin` |
| `buffs.rapid_fire.factor` | `—` |
| `buffs.power_shot.factor` | `—` |
| `buffs.efficient_boost.factor` | `—` |
| `buffs.boost_recovery.factor` | `—` |
| `player.dash.cooldown_stack_factor` | `—` |
| `buffs.spread_shot.max_stacks` | `—` |
| `buffs.piercing.max_stacks` | `—` |
| `buffs.bullet_speed.factor` | `—` |

### `csharp\godot\ReturnCinematic.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.return_skip_grace` | `SKIP_GRACE` |

### `csharp\godot\ScoreService.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `milestones.boss_kill_base` | `500.0` |

### `csharp\godot\SettingsService.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `player.aim_assist.joy_speed` | `JoyAimSpeed` |

### `csharp\godot\Spawner.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `spawner.wave_interval_start` | `WAVE_INTERVAL_START` |
| `spawner.wave_interval_end` | `WAVE_INTERVAL_END` |
| `spawner.ramp_time` | `RAMP_TIME` |
| `spawner.interval_min` | `INTERVAL_MIN` |
| `spawner.boss_score_step` | `BOSS_SCORE_STEP` |
| `spawner.boss_min_interval` | `BOSS_MIN_INTERVAL` |
| `spawner.boss_time_limit` | `BOSS_TIME_LIMIT` |
| `spawner.difficulty_factor` | `DIFFICULTY_FACTOR` |
| `spawner.unlock_scores` | `UNLOCK_SCORES` |
| `spawner.wave_size_start` | `WAVE_SIZE_START` |
| `spawner.wave_size_end` | `WAVE_SIZE_END` |
| `spawner.special_gap_min` | `SPECIAL_GAP_MIN` |
| `spawner.special_gap_max` | `SPECIAL_GAP_MAX` |
| `spawner.rest_waves_after_kill` | `REST_WAVES_AFTER_KILL` |
| `spawner.elite_wave_size` | `ELITE_WAVE_SIZE` |
| `enemies.hover_band` | `new Godot.Collections.Array { _hoverBand.X, _hoverBand.Y }` |
| `enemies.types` | `new Godot.Collections.Array(` |
| `elites.types` | `new Godot.Collections.Array(` |
| `effects.shake.boss_warning` | `14.0` |

### `csharp\godot\Starfield.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.starfield.far_count` | `_farCount` |
| `effects.starfield.near_count` | `_nearCount` |
| `effects.starfield.far_speed` | `_farSpeed` |
| `effects.starfield.near_speed` | `_nearSpeed` |

### `csharp\godot\StrikeCarrier.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `elite_turret_event.carrier.retreat_start_speed` | `RetreatStartSpeed` |
| `elite_turret_event.carrier.retreat_accel` | `RetreatAccel` |

### `csharp\godot\TurretBattery.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `enemies.bullet_speed` | `SingleSpeed` |
| `enemies.spread_bullet_speed` | `SpreadSpeed` |
| `enemies.laser_bullet_speed` | `LaserSpeed` |
| `boss.homing_bullet_speed` | `HomingSpeed` |
| `boss.sniper_bullet_speed` | `SniperSpeed` |
| `enemies.spread_fan_step` | `SpreadFanStep` |
| `enemies.bullet_damage.single` | `DmgSingle` |
| `enemies.bullet_damage.spread` | `DmgSpread` |
| `enemies.bullet_damage.laser` | `DmgLaser` |
| `boss.bullet_damage.homing` | `DmgHoming` |
| `boss.bullet_damage.sniper` | `DmgSniper` |
| `effects.shake.enemy_die` | `_shakeDie` |

### `csharp\godot\Tutorial.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.home_charge_time` | `HomeChargeTime` |
| `mothership.dock_charge_time` | `DockChargeTime` |
| `tutorial.boss_hp` | `120.0` |
| `mothership.hover_y` | `270.0` |

### `csharp\godot\WarpGate.cs`

| json 键路径 | 脚本回退值 |
| --- | --- |
| `effects.mothership_summon.gate.open_time` | `OPEN_TIME` |
| `effects.mothership_summon.gate.close_time` | `CLOSE_TIME` |
| `effects.mothership_summon.gate.radius` | `RADIUS` |

## 动态拼接键前缀

- `boss.phases.type…`
- `buffs.…`
- `player.aim_assist.levels.…`

## json 中存在但脚本未静态引用的键

（经动态键或整段读取覆盖的不列出；剩下的请人工判断是否为死键）

- `version`

## 脚本引用但 json 缺失的键（走回退值，建议补进 json 或确认为有意兜底）

