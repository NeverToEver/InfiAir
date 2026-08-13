using Godot;

namespace InfiAir.Tests;

/// <summary>AutoplayTest 战斗行为（partial 拆分自 AutoplayTest.cs，纯搬移零行为变化）。</summary>
public partial class AutoplayTest : Node
{
    // ---------------- bot 行为 ----------------

    /// <summary>随机游走 + 远离密集敌弹/敌机的简单规避</summary>
    private void UpdateMovement(long now)
    {
        if (now < _nextMoveDecision)
        {
            return;
        }
        _nextMoveDecision = now + MoveDecisionMs;
        var player = _player!;
        var playerPos = player.Position;  // 规避遍历每 child 复用，减少 Position 属性重复读取
        var view = _gs.ViewWorldRect();
        if (playerPos.DistanceTo(_moveTarget) < 80.0f || GD.Randf() < 0.1f)
        {
            _moveTarget = new Vector2(
                (float)GD.RandRange(view.Position.X + 100.0f, view.End.X - 100.0f),
                (float)GD.RandRange(view.Position.Y + 100.0f, view.End.Y - 100.0f));
        }
        var steer = _moveTarget - playerPos;
        steer = steer.Length() > 60.0f ? steer.Normalized() : Vector2.Zero;
        // 规避：240px 内敌弹/编队炸弹（同权重）+ 160px 内敌机的反加权和。
        // 擦弹诱导窗（_grazeSeekUntil>0）：敌弹改「60px 内硬规避 + 60~260px 拉向擦弹带」，
        // 诱导近失弹触发擦弹计分；编队炸弹始终硬规避（爆炸半径大，不值得擦）。
        var dodge = Vector2.Zero;
        bool graze = _grazeSeekUntil > 0;
        foreach (var child in _main!.GetChildren())
        {
            if (child is Bullet b)
            {
                if (!b.IsPlayerBullet)
                {
                    float d = playerPos.DistanceTo(b.Position);
                    if (d < 1.0f)
                    {
                        continue;
                    }
                    if (graze)
                    {
                        if (d < 60.0f)
                        {
                            dodge += (playerPos - b.Position) / d * (1.0f - d / 60.0f) * 3.0f;
                        }
                        else if (d < 260.0f)
                        {
                            dodge += (b.Position - playerPos) / d * (1.0f - (d - 60.0f) / 200.0f) * 1.5f;
                        }
                    }
                    else if (d < 240.0f)
                    {
                        dodge += (playerPos - b.Position) / d * (1.0f - d / 240.0f) * 2.0f;
                    }
                }
                continue;
            }
            if (child is FormationBomb fb)
            {
                float d = playerPos.DistanceTo(fb.Position);
                if (d < 240.0f && d > 1.0f)
                {
                    dodge += (playerPos - fb.Position) / d * (1.0f - d / 240.0f) * 2.0f;
                }
            }
        }
        foreach (var e in _gs.Enemies)
        {
            var n = e as Node2D;
            if (n == null)
            {
                continue;
            }
            float d = playerPos.DistanceTo(n.GlobalPosition);
            if (d < 160.0f && d > 1.0f)
            {
                dodge += (playerPos - n.GlobalPosition) / d * (1.0f - d / 160.0f) * 3.0f;
            }
        }
        steer += dodge;
        SetMoveActions(steer);
    }

    private void SetMoveActions(Vector2 steer)
    {
        var want = new Godot.Collections.Dictionary
        {
            ["move_right"] = steer.X > 0.35f,
            ["move_left"] = steer.X < -0.35f,
            ["move_down"] = steer.Y > 0.35f,
            ["move_up"] = steer.Y < -0.35f,
        };
        foreach (var kv in want)
        {
            if (kv.Value.AsBool())
            {
                Input.ActionPress(kv.Key.AsStringName());
            }
            else
            {
                Input.ActionRelease(kv.Key.AsStringName());
            }
        }
    }

    private void UpdateAim(long now)
    {
        if (now < _nextAim)
        {
            return;
        }
        _nextAim = now + AimIntervalMs;
        var player = _player!;
        var target = Vector2.Zero;
        float bestSq = float.PositiveInfinity;
        foreach (var e in _gs.Enemies)
        {
            var n = e as Node2D;
            if (n == null)
            {
                continue;
            }
            float dSq = player.Position.DistanceSquaredTo(n.GlobalPosition);
            if (dSq < bestSq)
            {
                bestSq = dSq;
                target = n.GlobalPosition;
            }
        }
        if (bestSq == float.PositiveInfinity)
        {
            target = player.Position + new Vector2((float)GD.RandRange(-300.0f, 300.0f), -400.0f);
        }
        // 世界坐标 → canvas → 屏幕（无头模式 warp_mouse 无效，合成鼠标移动事件）
        var canvasPos = _main!.GetCanvasTransform() * target;
        var win = GetTree().Root.GetScreenTransform() * canvasPos;
        var mev = new InputEventMouseMotion { Position = win, GlobalPosition = win };
        Input.ParseInputEvent(mev);
    }

    /// <summary>周期性冲刺：敌弹密集时优先触发（需已解锁 phase_dash）；狂暴/子弹时间期间更频繁</summary>
    private void UpdateDash(long now)
    {
        if (_dashReleaseAt > 0 && now >= _dashReleaseAt)
        {
            Input.ActionRelease("dash");
            _dashReleaseAt = 0;
        }
        if (now < _nextDashTry)
        {
            return;
        }
        var player = _player!;
        var main = _main!;
        bool enrageActive = (_boss != null && IsInstanceValid(_boss) && _boss.IsEnraged()) || main.BulletTime() > 0.0f || main.TimeScaleRamp() >= 0.0f;
        _nextDashTry = now + (enrageActive ? 250 : 500);
        if (!player.DashUnlocked() || player.DashCooldownRemaining() > 0.0f || player.IsDashing())
        {
            return;
        }
        double threat = 0.0;
        foreach (var child in main.GetChildren())
        {
            if (child is Bullet b && !b.IsPlayerBullet)
            {
                float d = player.Position.DistanceTo(b.Position);
                if (d < 240.0f)
                {
                    threat += 1.0 - d / 240.0;
                }
            }
        }
        double threshold = enrageActive ? 0.4 : 1.0;
        double idleChance = enrageActive ? 0.25 : 0.05;
        if (threat > threshold || GD.Randf() < idleChance)
        {
            Input.ActionPress("dash");
            _dashReleaseAt = now + 150;
        }
    }

    /// <summary>母舰：Boss 战或 HP 偏低时概率蓄力召唤（含蓄力主动取消探针）；
    /// 驻留驾驶一段时间（WASD 已被移动驱动复用）后提前离舰，或驻留到超时强制弹射</summary>
    private void UpdateDock(long now)
    {
        var main = _main!;
        if (main.IsHomecoming() || main.IsGameOver())
        {
            if (_dockHolding)
            {
                Input.ActionRelease("dock");
                _dockHolding = false;
            }
            return;
        }
        var ms = main.Mothership();
        if (_dockHolding)
        {
            if (ms != null)
            {
                Input.ActionRelease("dock");
                _dockHolding = false;
                _msSummons++;
                Log($"母舰召唤成功（第 {_msSummons} 次）");
            }
            else if (now >= _dockHoldUntil)
            {
                Input.ActionRelease("dock");
                _dockHolding = false;
                if (_dockCancelEpisode)
                {
                    _chargeCancels++;
                    Log($"母舰蓄力主动取消（第 {_chargeCancels} 次）");
                }
                else
                {
                    Log("母舰蓄力超时未召唤，松手");
                }
                _dockCancelEpisode = false;
            }
        }
        else if (ms == null && main.DockCooldown() <= 0.0f && now >= _nextDockConsider)
        {
            double hpRatio = _gs.Health / _gs.MaxHealth();
            if (_boss != null || hpRatio < 0.7)
            {
                float roll = GD.Randf();
                if (roll < 0.15f)
                {
                    // 蓄力取消探针：按住短时间后在蓄满前松手
                    Input.ActionPress("dock");
                    _dockHolding = true;
                    _dockCancelEpisode = true;
                    _dockHoldUntil = now + 300 + (long)(GD.Randi() % 600);
                    Log("开始蓄力召唤母舰（计划中途取消）");
                }
                else if (roll < 0.6f)
                {
                    Input.ActionPress("dock");
                    _dockHolding = true;
                    _dockCancelEpisode = false;
                    _dockHoldUntil = now + 8000;  // 蓄力 3s + 机库小窗 ~2.6s，留足余量
                    Log($"开始蓄力召唤母舰（boss={BoolStr(_boss != null)} hp={hpRatio * 100.0:0}%）");
                }
                else
                {
                    _nextDockConsider = now + 20000;
                }
            }
            else
            {
                _nextDockConsider = now + 10000;
            }
        }
        else if (ms != null && ms.GetState() < Mothership.State.STAY && GD.Randf() < 0.002f)
        {
            // 边界探针：非驻留态（降入/吸附/补给）乱按 H，应为无操作
            Input.ActionPress("dock");
            Input.ActionRelease("dock");
        }
        // 驻留驾驶一段时间后提前离舰；部分局驻留到超时强制弹射
        if (_earlyHolding)
        {
            if (ms == null || ms.GetState() >= Mothership.State.RELEASE || now >= _earlyHoldUntil)
            {
                Input.ActionRelease("dock");
                _earlyHolding = false;
                if (ms != null && ms.GetState() >= Mothership.State.RELEASE)
                {
                    _earlyLeaves++;
                    Log($"提前离舰（第 {_earlyLeaves} 次，弹匣 {ms.GetMagCells()} 格）");
                }
            }
        }
        else if (ms != null && ms.GetState() == Mothership.State.STAY)
        {
            if (_staySince == 0)
            {
                _staySince = now;
                _stayUntilEject = GD.Randf() < 0.35f;
                _earlyLeaveAt = now + (_stayUntilEject ? 60000 : 6000 + (long)(GD.Randi() % 8000));
                if (_stayUntilEject)
                {
                    Log("本次驻留等到超时强制弹射");
                }
            }
            else if (now >= _earlyLeaveAt)
            {
                Input.ActionPress("dock");
                _earlyHolding = true;
                _earlyHoldUntil = now + 4000;
            }
        }
        else
        {
            _staySince = 0;
        }
    }

    /// <summary>返航：血量低（或 Boss 战且半血以下）概率蓄力 B</summary>
    private void UpdateHomecoming(long now)
    {
        var main = _main!;
        if (main.IsHomecoming())
        {
            if (_homeHolding)
            {
                Input.ActionRelease("homecoming");
                _homeHolding = false;
                _homecomings++;
                Log($"返航触发（第 {_homecomings} 次）");
            }
            return;
        }
        if (_homeHolding)
        {
            if (now >= _homeHoldUntil)
            {
                Input.ActionRelease("homecoming");
                _homeHolding = false;
                Log("返航蓄力超时未触发，松手");
            }
            return;
        }
        if (now < _nextHomeConsider || main.IsGameOver())
        {
            return;
        }
        _nextHomeConsider = now + 8000;
        double hpRatio = _gs.Health / _gs.MaxHealth();
        bool want = hpRatio < 0.35 || (_boss != null && hpRatio < 0.6);
        if (want && GD.Randf() < 0.6f)
        {
            Input.ActionPress("homecoming");
            _homeHolding = true;
            _homeHoldUntil = now + 4000;
            Log($"开始蓄力返航（hp={hpRatio * 100.0:0}% boss={BoolStr(_boss != null)}）");
        }
    }

    /// <summary>弹反盾探针：附近有敌弹时按 parry（原 autoplay 完全未覆盖弹反子系统）。
    /// parry 是 IsActionJustPressed 触发——按住 60ms 生成下降沿，保证 Player._physics_process 看到边沿。</summary>
    private void UpdateParry(long now)
    {
        if (_parryReleaseAt > 0 && now >= _parryReleaseAt)
        {
            Input.ActionRelease("parry");
            _parryReleaseAt = 0;
        }
        if (now < _nextParryConsider)
        {
            return;
        }
        _nextParryConsider = now + 1200 + (long)(GD.Randi() % 1500);
        var player = _player!;
        if (player.ParryPhase() != PlayerParry.GetPhaseIdle() || player.ParryCooldownRemaining() > 0.0f)
        {
            return;
        }
        // 附近存在敌弹才触发（模拟真人看弹反时机；无弹时不做无意义触发）
        bool near = false;
        foreach (var child in _main!.GetChildren())
        {
            if (child is Bullet b && !b.IsPlayerBullet && player.Position.DistanceTo(b.Position) < 260.0f)
            {
                near = true;
                break;
            }
        }
        if (!near)
        {
            return;
        }
        Input.ActionPress("parry");
        _parryReleaseAt = now + 60;
        _parryCount++;
        Log($"弹反盾触发（第 {_parryCount} 次，能量 {player.ParryEnergyRatio() * 100.0:0}%）");
    }

    /// <summary>加速/燃油经济探针：非 toggle 模式下周期性按住 boost 消耗/回复燃油
    /// （原 autoplay 未覆盖 boost_recovery/efficient_boost 热路径与 FuelDrain/FuelRegen）。</summary>
    private void UpdateBoost(long now)
    {
        var player = _player!;
        if (_boostHolding)
        {
            if (now >= _boostHoldUntil || player.FuelAmount() <= 5.0f)
            {
                Input.ActionRelease("boost");
                _boostHolding = false;
                Log($"加速释放（fuel={player.FuelAmount():0}）");
            }
            return;
        }
        if (now < _nextBoostConsider)
        {
            return;
        }
        _nextBoostConsider = now + 2500 + (long)(GD.Randi() % 2500);
        // toggle 模式下按住无效（仅 JustPressed 切换，已由设置轮换覆盖）；燃油不足跳过
        if (_gs.ShiftToggleMode || player.FuelAmount() < 30.0f)
        {
            return;
        }
        Input.ActionPress("boost");
        _boostHolding = true;
        _boostHoldUntil = now + 700 + (long)(GD.Randi() % 900);
        _boostCount++;
        Log($"加速开启（第 {_boostCount} 次，fuel={player.FuelAmount():0}）");
    }

    /// <summary>自毁探针：低血时按概率长按 give_up 自毁进死亡结算（原 autoplay 未覆盖该死亡路径；
    /// 与自然死亡不同——走 Main.GiveUp 直接 LoseHealth(全部) + player.Die）。</summary>
    private void UpdateGiveUp(long now)
    {
        if (_giveUpHolding)
        {
            if (now >= _giveUpHoldUntil)
            {
                Input.ActionRelease("give_up");
                _giveUpHolding = false;
            }
            return;
        }
        if (now < _nextGiveUpConsider)
        {
            return;
        }
        _nextGiveUpConsider = now + 45000 + (long)(GD.Randi() % 30000);
        var main = _main!;
        if (_player == null || _player.IsDead() || main.IsHomecoming() || main.IsGameOver() || main.SummonWindow() != null)
        {
            return;
        }
        double hpRatio = _gs.Health / _gs.MaxHealth();
        if (hpRatio < 0.35 && GD.Randf() < 0.4f)
        {
            Input.ActionPress("give_up");
            _giveUpHolding = true;
            _giveUpHoldUntil = now + 3400; // GIVE_UP_HOLD_TIME=3s + 余量
            _giveUpProbes++;
            Log($"自毁探针触发（hp={hpRatio * 100.0:0}%，第 {_giveUpProbes} 次）");
        }
    }

    /// <summary>fine_move（Ctrl 微调）探针：非 toggle 模式下周期性按住 fine_move 减速（覆盖 Ctrl 微调热路径）。</summary>
    private void UpdateFineMove(long now)
    {
        if (_fineMoveHolding)
        {
            if (now >= _fineMoveHoldUntil)
            {
                Input.ActionRelease("fine_move");
                _fineMoveHolding = false;
            }
            return;
        }
        if (now < _nextFineMoveConsider)
        {
            return;
        }
        _nextFineMoveConsider = now + 3500 + (long)(GD.Randi() % 3000);
        // toggle 模式下按住无效（仅 JustPressed 切换，已由设置轮换覆盖）
        if (_gs.CtrlToggleMode)
        {
            return;
        }
        Input.ActionPress("fine_move");
        _fineMoveHolding = true;
        _fineMoveHoldUntil = now + 600 + (long)(GD.Randi() % 800);
        _fineMoveCount++;
        Log($"微调开启（第 {_fineMoveCount} 次）");
    }

    /// <summary>刻意擦弹诱导：周期性开启诱导窗——把 60~260px 带内的敌弹拉向擦弹带（而非纯规避），
    /// 60px 内保持硬规避保命；诱导窗结束回归普通规避。擦弹接触经 GrazeArea.AreaEntered 计数。</summary>
    private void UpdateGrazeSeek(long now)
    {
        if (now < _nextGrazeConsider)
        {
            return;
        }
        if (_grazeSeekUntil == 0)
        {
            _grazeSeekUntil = now + 2000 + (long)(GD.Randi() % 1000);
            Log($"擦弹诱导开启（持续 {_grazeSeekUntil - now}ms）");
        }
        else if (now >= _grazeSeekUntil)
        {
            _grazeSeekUntil = 0;
            _nextGrazeConsider = now + 5000 + (long)(GD.Randi() % 4000);
            Log($"擦弹诱导结束（累计擦弹接触 {_grazeCount}）");
        }
    }

    /// <summary>buff_panel（L 键）探针：周期性切换 Hud buff 滚动栏（直接调 ToggleBuffPanel，
    /// 覆盖展开/收起核心路径；原 autoplay 未覆盖该子系统）。</summary>
    private void UpdateBuffPanel(long now)
    {
        if (now < _nextBuffPanelConsider)
        {
            return;
        }
        _nextBuffPanelConsider = now + 10000 + (long)(GD.Randi() % 8000);
        var hud = _main!.Hud();
        hud.ToggleBuffPanel();
        _buffPanelToggles++;
        Log($"buff_panel 切换（第 {_buffPanelToggles} 次，切换后 open={BoolStr(hud.IsBuffPanelOpen())}）");
    }

    /// <summary>GrazeArea 进入事件（与 Player 自身 handler 并存）：计数敌弹擦弹接触，用于覆盖验证。</summary>
    private void OnGrazeAreaEntered(Area2D area)
    {
        if (area is Bullet b && !b.IsPlayerBullet)
        {
            _grazeCount++;
        }
    }

}
