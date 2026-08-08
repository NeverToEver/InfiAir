using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 精英炮塔事件测试（docs/ELITE_TURRET_EVENT.md）：
/// 场景1 成功流：航母入场 → 炮塔升起 → 30s 倒计时 → 弱锁定开火 → 三节点台词 →
///   全歼 +500 基础分（中难度 ×2）→ 受创撤离 → Boss 解冻。
/// 场景2 互斥：事件期间 Boss 触发被冻结（_boss_pending 记一次不累积），
///   BOSS_DELAY 结束补触发一次且仅一次。
/// 场景3 失败流：倒计时归零仍有炮台存活 → 撤退台词 + 无奖励 + 炮塔收回 + 回 IDLE 进冷却。
/// 场景4 返航中止：TURRET_ACTIVE 中 _start_homecoming → abort 清炮塔/隐藏事件条/恢复波次/
///   航母完整撤离 → 继续出击注册表清场 → BOSS_DELAY 后回 IDLE 且 Boss 解冻。
/// </summary>
public partial class EliteTurretEventTest : Node
{
    private int _failures;
    private Main _main = null!;

    private void Check(bool cond, string label)
    {
        if (cond)
        {
            GD.Print("[PASS] " + label);
        }
        else
        {
            _failures++;
            GD.PushError("[FAIL] " + label);
        }
    }

    /// <summary>真实时间等待（不受 time_scale 影响）</summary>
    private async Task WaitReal(double sec)
    {
        await ToSignal(GetTree().CreateTimer(sec, true, false, true), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>轮询等事件进入目标状态（最多 timeout 秒真实时间）</summary>
    private async Task<bool> WaitEventState(EliteTurretEvent eventNode, EliteTurretEvent.State pState, double timeout = 8.0)
    {
        var left = timeout;
        while (left > 0.0)
        {
            if (eventNode.GetState() == pState)
            {
                return true;
            }
            await WaitReal(0.1);
            left -= 0.1;
        }
        return eventNode.GetState() == pState;
    }

    private int CountBosses()
    {
        var n = 0;
        foreach (var child in _main.GetChildren())
        {
            if (child is Boss)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>启动一次压缩时长的事件（实例 var 覆盖，不动 balance.json）</summary>
    private void StartFastEvent(EliteTurretEvent eventNode)
    {
        eventNode.ENTER_TIME = 0.2f;
        eventNode.RISE_TIME = 0.2f;
        eventNode.BOSS_RESUME_DELAY = 0.3f;
        eventNode.FIRE_INTERVAL = new Vector2(0.3f, 0.4f);
        eventNode.SetCooldownLeft(0.0f);
        eventNode.Start();
    }

    /// <summary>击毁 n 座仍存活的炮台（返回实际击毁数）</summary>
    private int KillTurrets(EliteTurretEvent eventNode, int n)
    {
        var killed = 0;
        foreach (var turret in eventNode.Turrets().Duplicate())
        {
            if (killed >= n)
            {
                break;
            }
            if (GodotObject.IsInstanceValid(turret))
            {
                turret.TakeDamage(9999);
                killed++;
            }
        }
        return killed;
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            // 清理持久化状态，保证测试确定性
            var gs = GetNode<GameState>("/root/GameState");
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            gs.SetDifficulty(new StringName("medium"));
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            _main = GetNode<Main>("Main");
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);  // 禁用自动开火，炮台击杀全部走断言路径
            player.SetInvincible(999.0f);
            player.Position = new Vector2(960.0f, 800.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            var hud = GetNode<Hud>("Main/HUD");
            var eventNode = gs.Events.Event(new StringName("elite_turret")).AsGodotObject() as EliteTurretEvent;
            Check(eventNode != null, "初始化：事件编排节点已登记到 main");
            if (eventNode == null)
            {
                throw new System.InvalidOperationException("elite_turret 事件节点未登记（GDScript 原语义：null 调用报错计入失败）");
            }
            Check(spawner.EliteEvent() == eventNode, "初始化：spawner 持有事件引用（互斥钩子）");
            spawner.SetProcess(false);  // 场景1 手动驱动，保证确定性
            gs.Score = 0;

            // ================= 场景 1：成功流（全歼炮台） =================
            StartFastEvent(eventNode);
            Check(eventNode.GetState() == EliteTurretEvent.State.CARRIER_ENTER, "场景1：启动进入 CARRIER_ENTER");
            Check(spawner.BossFrozen(), "场景1：事件启动即冻结 Boss 调度");
            Check(spawner.WavesPaused(), "场景1：事件启动即暂停普通波次");
            Check(eventNode.Lines().Count == 3, "场景1：无放回抽取 3 句绑定台词");
            var seenLines = new Godot.Collections.Array<string>();
            var dupOk = true;
            foreach (var key in eventNode.Lines())
            {
                if (seenLines.Contains(key))
                {
                    dupOk = false;
                }
                seenLines.Add(key);
            }
            Check(dupOk, "场景1：3 句台词不重复");
            // 等升起完成进入 30s 倒计时
            Check(await WaitEventState(eventNode, EliteTurretEvent.State.TURRET_ACTIVE), "场景1：入场+升起后进入 TURRET_ACTIVE");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(eventNode.Turrets().Count == 4, "场景1：中难度 4 座炮台");
            Check(eventNode.Total() == 4, "场景1：炮台总数记录为 4");
            if (eventNode.Turrets().Count > 0)
            {
                var t0 = eventNode.Turrets()[0];
                Check(t0.MaxHp == 80, "场景1：单台血量 80（80×中难度×1.0）");
                Check(t0.Monitoring, "场景1：充能完毕后炮台可被攻击");
            }
            Check(hud.EventBox().Visible, "场景1：HUD 事件计时条显示");
            // 弱锁定开火：等几轮射击，验证弹药与追踪参数
            var homingOk = false;
            var firedOk = false;
            for (var i = 0; i < 40; i++)  // 最多 ~4s 真实时间
            {
                await WaitReal(0.1);
                foreach (var child in _main.GetChildren())
                {
                    if (child is Bullet bullet && !bullet.IsPlayerBullet)
                    {
                        firedOk = true;
                        if (bullet.HasMeta("bullet_type") && bullet.GetMeta("bullet_type").AsStringName() == new StringName("homing"))
                        {
                            if (Mathf.IsEqualApprox(bullet.HomingTurnRate, 1.5f) && Mathf.IsEqualApprox(bullet.HomingTime, 0.6f))
                            {
                                homingOk = true;
                            }
                        }
                    }
                }
                if (firedOk && homingOk)
                {
                    break;
                }
            }
            Check(firedOk, "场景1：炮台按独立节奏开火");
            Check(homingOk, "场景1：弱追踪弹转向速率 1.5 / 时限 0.6s");
            // 台词节点：⌈4/3⌉=2 → 第 1 句；⌈4×2/3⌉=3 → 第 2 句
            KillTurrets(eventNode, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(eventNode.LineStage() == 0, "场景1：摧毁 1 座未达 ⌈4/3⌉=2，台词未播");
            KillTurrets(eventNode, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(eventNode.LineStage() == 1, "场景1：摧毁 2 座（≥⌈总数/3⌉）播第 1 句");
            Check(eventNode.Comm()!.FullText() == Tr(eventNode.Lines()[0]), "场景1：第 1 句为绑定台词第 1 条");
            KillTurrets(eventNode, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(eventNode.LineStage() == 2, "场景1：摧毁 3 座（≥⌈总数×2/3⌉）播第 2 句");
            // 全歼 → 成功结算
            var score0 = gs.Score;
            KillTurrets(eventNode, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(eventNode.GetState() == EliteTurretEvent.State.CARRIER_EXIT, "场景1：全歼进入 CARRIER_EXIT");
            Check(gs.Score - score0 == 1000, "场景1：奖励 500×中难度倍率×2 = 1000 入账");
            Check(eventNode.Comm()!.FullText() == Tr(eventNode.Lines()[2]), "场景1：全歼播第 3 句绑定台词");
            Check(!hud.EventBox().Visible, "场景1：结算后事件计时条隐藏");
            Check(!spawner.WavesPaused(), "场景1：CARRIER_EXIT 起普通波次恢复");
            Check(eventNode.Turrets().Count == 0, "场景1：炮台清单已清空");
            // 航母受创撤离 → BOSS_DELAY → IDLE，Boss 解冻
            Check(await WaitEventState(eventNode, EliteTurretEvent.State.BOSS_DELAY, 10.0), "场景1：航母离场进入 BOSS_DELAY");
            Check(await WaitEventState(eventNode, EliteTurretEvent.State.IDLE, 5.0), "场景1：BOSS_DELAY 结束回 IDLE");
            Check(!spawner.BossFrozen(), "场景1：事件结束后 Boss 解冻");
            Check(eventNode.CooldownLeft() > 0.0f, "场景1：事件结束进入触发冷却");

            // ================= 场景 2：Boss 冻结/恢复（单次不累积） =================
            spawner.SetBossPending(false);
            spawner.SetNextBossScore(gs.Score);  // Boss 分数步进立即到期
            spawner.SetBossTimer(spawner.BOSS_MIN_INTERVAL);  // 越过最小间隔时间门（分数触发需同时满足）
            spawner.SetProcess(true);  // 恢复 spawner 主循环：pending 标记由它记录
            StartFastEvent(eventNode);
            await WaitReal(0.5);
            Check(spawner.BossPending(), "场景2：事件期间 Boss 到期只记 pending");
            Check(CountBosses() == 0, "场景2：事件期间 Boss 未触发（冻结）");
            await WaitReal(1.0);  // 多帧重复到期：覆盖为同一标记，不累积
            Check(spawner.BossPending(), "场景2：重复到期仍只有同一 pending 标记");
            Check(CountBosses() == 0, "场景2：重复到期仍无 Boss");
            // 快速全歼结束事件
            await WaitEventState(eventNode, EliteTurretEvent.State.TURRET_ACTIVE);
            KillTurrets(eventNode, 99);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(await WaitEventState(eventNode, EliteTurretEvent.State.IDLE, 12.0), "场景2：事件结束回 IDLE");
            // BOSS_DELAY 结束 → 立即补触发 Boss 一次（boss_warning 后 2s 降入）
            var bossSpawned = false;
            for (var i = 0; i < 40; i++)  // 最多 ~4s
            {
                await WaitReal(0.1);
                if (CountBosses() > 0)
                {
                    bossSpawned = true;
                    break;
                }
            }
            Check(bossSpawned, "场景2：冻结的 Boss 在 BOSS_DELAY 后补触发");
            Check(!spawner.BossPending(), "场景2：补触发后 pending 标记清除");
            await WaitReal(0.5);
            Check(CountBosses() == 1, "场景2：Boss 仅补触发一次（不累积）");
            // 清理：直接释放 Boss，避免击杀奖励干扰后续断言
            foreach (var child in _main.GetChildren())
            {
                if (child is Boss bossChild)
                {
                    bossChild.QueueFree();
                }
            }
            spawner.SetBossActive(false);
            spawner.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 场景 3：失败流（超时撤退） =================
            eventNode.SetCooldownLeft(0.0f);
            eventNode.DURATION = 0.8f;
            eventNode.Start();
            var rp1 = gs.Rp;
            Check(await WaitEventState(eventNode, EliteTurretEvent.State.TURRET_ACTIVE), "场景3：事件进入倒计时");
            Check(await WaitEventState(eventNode, EliteTurretEvent.State.CARRIER_EXIT, 5.0), "场景3：倒计时归零进入 CARRIER_EXIT");
            Check(eventNode.Comm()!.FullText() == Tr("ETQ_RETREAT"), "场景3：失败播放固定撤退台词");
            // 失败无奖励入账：RP 为事件奖励载体（炮台未击杀无击杀分）。
            // 注：玩家在弹幕中会自然擦弹得分（2026-08-03 机制二设计行为），score 不再恒等，
            // 故断言改为奖励载体 RP 不变
            Check(gs.Rp == rp1, "场景3：失败无奖励入账");
            var turretsGone = true;
            foreach (var turret in eventNode.Turrets())
            {
                if (GodotObject.IsInstanceValid(turret) && !turret.Ceased())
                {
                    turretsGone = false;
                }
            }
            Check(turretsGone, "场景3：存活炮台停火收回盖板");
            Check(await WaitEventState(eventNode, EliteTurretEvent.State.IDLE, 12.0), "场景3：航母完整撤离后回 IDLE");
            Check(!eventNode.CanTrigger(), "场景3：冷却期内不可再次触发");
            // L13：母舰在场期事件不触发（组查询互斥）
            eventNode.SetCooldownLeft(0.0f);
            var msProbe = new Node();
            AddChild(msProbe);
            msProbe.AddToGroup("mothership");
            Check(!eventNode.CanTrigger(), "场景3：母舰在场时不可触发");
            msProbe.RemoveFromGroup("mothership");
            msProbe.QueueFree();
            Check(eventNode.CanTrigger(), "场景3：母舰离场恢复可触发");

            // ================= 场景 4：返航中止（abort） =================
            eventNode.SetCooldownLeft(0.0f);
            eventNode.DURATION = 30.0f;  // 恢复倒计时（场景3 改过）
            StartFastEvent(eventNode);
            Check(await WaitEventState(eventNode, EliteTurretEvent.State.TURRET_ACTIVE), "场景4：事件进入倒计时");
            Check(hud.EventBox().Visible, "场景4：中止前 HUD 事件条显示");
            // 返航触发：elite 事件应被 abort（清炮塔、隐藏事件条、恢复波次、航母完整撤离）
            _main.StartHomecoming();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(eventNode.GetState() == EliteTurretEvent.State.CARRIER_EXIT, "场景4：返航中止事件进入 CARRIER_EXIT");
            Check(eventNode.Turrets().Count == 0, "场景4：在场炮塔清单已清");
            Check(!hud.EventBox().Visible, "场景4：中止后 HUD 事件条隐藏");
            Check(!spawner.WavesPaused(), "场景4：普通波次恢复");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var turretNodesLeft = 0;
            foreach (var child in _main.GetChildren())
            {
                if (child is TurretBattery)
                {
                    turretNodesLeft++;
                }
            }
            Check(turretNodesLeft == 0, "场景4：炮塔节点已释放（不走 died 计分）");
            // 过场越过 1.2s 输入宽限后跳过 → 基地 → 继续出击
            await WaitReal(1.4);
            _main.SkipReturn();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_main.BaseUi().Visible, "场景4：过场结束进入基地界面");
            _main.BaseUi().Resume();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 轨道打击动画（orbital_strike_test 专测本体）：缩短时轴，等待命中清场并播完
            if (_main.Strike() != null)
            {
                _main.Strike()!.DURATION = 0.5f;
            }
            var tStrike = 0.0;
            while (_main.Strike() != null && tStrike < 3.0)
            {
                await Coroutine.WaitSeconds(this, 0.1);
                tStrike += 0.1;
            }
            // 注册表驱动清场：非 Boss 实体（含事件/波次残留）全清
            var registryLeft = false;
            foreach (var e in gs.Enemies)
            {
                if (GodotObject.IsInstanceValid(e) && e is not Boss)
                {
                    registryLeft = true;
                }
            }
            Check(!registryLeft, "场景4：继续出击后注册表非 Boss 实体清空");
            // 航母完整撤离 → BOSS_DELAY → IDLE，Boss 解冻（沿用 _on_boss_delay_end）
            Check(await WaitEventState(eventNode, EliteTurretEvent.State.IDLE, 15.0), "场景4：航母撤离后回 IDLE");
            Check(!spawner.BossFrozen(), "场景4：Boss 冻结解除");
            Check(!spawner.BossPending(), "场景4：无遗留 pending 标记");

            Check(Mathf.IsEqualApprox(Engine.TimeScale, 1.0f), "收尾：time_scale = 1.0");
            await WaitReal(1.5);  // 让撤退 tween/爆炸粒子播完，避免退出时对象泄漏
            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"ELITE TURRET EVENT TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"ELITE TURRET EVENT TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
