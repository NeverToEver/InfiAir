# Collision, Damage & View

## Overview
碰撞层与解析、命中检测、子弹/爆炸约定、相机与视口数学、鼠标锁；适用于战斗实体、特效及一切屏幕空间计算。

## Rules
- 碰撞层：1=`player`、2=`player_bullet`、3=`enemy`（含 boss）、4=`enemy_bullet`；玩家弹对 `enemy` 组、敌弹/敌实体对 `player_hitbox` 组解析。
- 玩家仅经 `Player/Hitbox` Area2D 受击（设计 r=7×world_scale→运行时 2.8）；Body r=22 无碰撞用途（mask 0），禁用于受击。Ramming：敌=事件驱动 `AreaEntered/AreaExited` 标志 + O(1) 重掷防抖（禁逐帧 `overlaps_area` 轮询）；Boss=`csharp/godot/Boss.cs` `CheckBodyCollision()` 阶段门控。狂暴前 Boss HP 保底 30%（`boss.enrage.hp_ratio`）。
- 子弹：`scenes/bullet.tscn`，`Setup()` 定阵营；`Bullet.HomingTarget` 于 `Activate()` 重置；爆炸统一 `Explosion.SpawnAt()`。
- 相机固定 (960,540) 仅缩放；边缘/离屏/生成/可见性计算一律 `GameState.ViewWorldRect()`，禁硬编码 0..1920/0..1080。鼠标锁（profile `mouse_lock`，默认开）：`MouseTrap.cs`（`ProcessModeEnum.Always`）准星态 `Input.WarpMouse()` 回拉，焦点丢失或非准星态释放。
