using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 轰炸编队事件测试（docs/FORMATION_STRIKE_EVENT.md 第 8 节）：
/// 场景1 触发门槛：Boss 激活 / 精英炮塔事件 active / 冷却中 / 分数不足 → can_trigger() false。
/// 场景2 状态推进：ENTER→TURN→BOMBING_RUN→EXIT→IDLE；战机注册；转向后才有炸弹；
///   投弹数 = 存活机 × bombs_per_craft（击坠机跳过）。
/// 场景3 炸弹：预警环存在且随引信收缩；引爆后节点释放；半径内玩家掉血（无敌不掉）。
/// 场景4 击坠：致死 → 注销注册表 + 得分；全歼 → 奖励分 + 提前 EXIT。
/// 场景5 打断：abort() → 实体清理、回 IDLE、冷却生效。
/// 场景6 无 Timer/节点残留；清理 user:// 持久化。
/// </summary>
public partial class FormationStrikeEventTest : Node
{
    private int _failures;
    private GameState _gs = null!;

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
        await Coroutine.WaitSeconds(this, sec);
    }

    /// <summary>轮询等事件进入目标状态（最多 timeout 秒真实时间）</summary>
    private async Task<bool> WaitEventState(FormationStrikeEvent evt, FormationStrikeEvent.State pState, double timeout = 8.0)
    {
        var left = timeout;
        while (left > 0.0)
        {
            if (evt.GetState() == pState)
            {
                return true;
            }
            await WaitReal(0.05);
            left -= 0.05;
        }
        return evt.GetState() == pState;
    }

    private int CountCrafts()
    {
        var n = 0;
        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is FormationCraft)
            {
                n++;
            }
        }
        return n;
    }

    private int CountBombs()
    {
        var n = 0;
        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is FormationBomb)
            {
                n++;
            }
        }
        return n;
    }

    private int CountRegisteredCrafts()
    {
        var n = 0;
        foreach (Node node in _gs.Enemies)
        {
            if (node is FormationCraft)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>启动一次压缩时长的事件（实例 var 覆盖，不动 balance.json）</summary>
    private void StartFastEvent(FormationStrikeEvent evt)
    {
        evt.ApproachSpeed = 2000.0f;  // 进场 ~0.2s
        evt.TurnTime = 0.3f;
        evt.RunSpeed = 400.0f;
        evt.BombInterval = 0.2f;
        evt.BombFuse = 3.0f;  // 炸弹存续到场景结束统一清理
        evt.SetCooldownLeft(0.0f);
        evt.Start();
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            _gs = GetNode<GameState>("/root/GameState");
            // 清理持久化状态，保证测试确定性
            _gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = _gs.HighScore;
            _gs.HighScore = 0;
            _gs.SaveProfile();
            _gs.SetDifficulty("medium");
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            _gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);  // 禁用自动开火，击杀全部走断言路径
            player.SetInvincible(999.0f);
            player.Position = new Vector2(960f, 800f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var main = GetNode<Main>("Main");
            var spawner = GetNode<Spawner>("Main/Spawner");
            var evtNode = main.Formation();
            Check(evtNode != null, "初始化：事件编排节点已登记到 main");
            var evt = (FormationStrikeEvent)evtNode!;
            Check(spawner.FormationEvent() == evt, "初始化：spawner 持有事件引用（优先级链钩子）");
            spawner.SetProcess(false);  // 全程手动驱动，保证确定性
            _gs.Score = 0;
            _gs.SetMilestoneOverride(999999);  // 防止得分跨越里程碑弹 Buff 三选一暂停树

            // ================= 场景 1：触发门槛 =================
            _gs.Score = 1000;
            evt.SetCooldownLeft(0.0f);
            Check(evt.CanTrigger(), "场景1：常态（分数达标/无 Boss/无精英事件/非冷却）可触发");
            spawner.SetBossActive(true);
            Check(!evt.CanTrigger(), "场景1：Boss 激活时不可触发");
            spawner.SetBossActive(false);
            var fakeEvent = new EliteTurretEvent();  // 不入树，仅置状态模拟精英事件 active
            fakeEvent.SetState(EliteTurretEvent.State.CARRIER_ENTER);
            var realEvent = spawner.EliteEvent();
            spawner.SetEliteEvent(fakeEvent);
            Check(!evt.CanTrigger(), "场景1：精英炮塔事件 active 时不可触发");
            spawner.SetEliteEvent(realEvent);
            fakeEvent.Free();
            evt.SetCooldownLeft(5.0f);
            Check(!evt.CanTrigger(), "场景1：冷却中不可触发");
            evt.SetCooldownLeft(0.0f);
            _gs.Score = evt.MinScore - 1;
            Check(!evt.CanTrigger(), "场景1：分数不足不可触发");
            _gs.Score = 1000;
            // L13：母舰在场期事件不触发（组查询互斥；节点释放自动退组）
            var msProbe = new Node();
            AddChild(msProbe);
            msProbe.AddToGroup("mothership");
            Check(!evt.CanTrigger(), "场景1：母舰在场时不可触发");
            msProbe.RemoveFromGroup("mothership");
            msProbe.QueueFree();
            Check(evt.CanTrigger(), "场景1：母舰离场恢复可触发");

            // ================= 场景 2：状态推进 + 投弹计数（击坠机跳过） =================
            StartFastEvent(evt);
            Check(evt.GetState() == FormationStrikeEvent.State.FORMATION_ENTER, "场景2：启动进入 FORMATION_ENTER");
            Check(spawner.WavesPaused(), "场景2：事件启动即暂停普通波次（占用波次槽）");
            Check(evt.GetCrafts().Count == 4, "场景2：中难度 4 架编队");
            Check(CountRegisteredCrafts() == 4, "场景2：战机注册 GameState.enemies");
            if (evt.GetCrafts().Count > 0)
            {
                Check(((FormationCraft)evt.GetCrafts()[0].AsGodotObject()).MaxHp == 60, "场景2：单机血量 60（60×中难度×1.0）");
            }
            Check(await WaitEventState(evt, FormationStrikeEvent.State.FORMATION_TURN), "场景2：靠近后进入 FORMATION_TURN");
            // 转航向期间击落 4 号僚机（投弹前）：其投弹序列应被跳过
            var wingman = (FormationCraft)evt.GetCrafts()[3].AsGodotObject();
            var score0 = _gs.Score;
            wingman.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_gs.Score - score0 == 400, "场景2：击坠得分 200×中难度倍率×2 = 400");
            Check(evt.AliveCount() == 3, "场景2：剩余 3 架");
            Check(CountBombs() == 0, "场景2：转向完成前无炸弹生成");
            Check(await WaitEventState(evt, FormationStrikeEvent.State.BOMBING_RUN), "场景2：转向后进入 BOMBING_RUN");
            Check(await WaitEventState(evt, FormationStrikeEvent.State.FORMATION_EXIT, 6.0), "场景2：投弹完毕进入 FORMATION_EXIT");
            Check(evt.DroppedCount() == 6, "场景2：投弹数 = 存活 3 机 × 2 枚 = 6（击坠机跳过）");
            Check(CountBombs() > 0, "场景2：引信未到时炸弹节点存续");
            Check(await WaitEventState(evt, FormationStrikeEvent.State.IDLE, 5.0), "场景2：离场结束回 IDLE");
            // M4：queue_free 帧末生效——IDLE 与清理同帧时计数会竞态，等一帧再断言清理（flake 加固）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!spawner.WavesPaused(), "场景2：事件结束恢复普通波次");
            Check(evt.CooldownLeft() > 0.0f, "场景2：事件结束进入触发冷却");
            Check(CountCrafts() == 0, "场景2：离场后战机节点清理");
            Check(CountRegisteredCrafts() == 0, "场景2：离场后注册表无残留");
            // 统一清理本场景遗留炸弹（引爆音/粒子自然播完）
            foreach (Node child in main.GetChildren())
            {
                if (child is FormationBomb)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 场景 3：炸弹引信/预警环/玩家伤害 =================
            player.Position = new Vector2(960f, 800f);
            player.SetInvincible(0.0f);
            _gs.Health = 100.0;
            var bomb = new FormationBomb();
            bomb.Setup(Vector2.Zero, 0.5f, 20, 120.0f);
            bomb.Position = player.Position;
            main.AddChild(bomb);
            // add_child 同步触发 _ready（警示环半径已置位），此处先于首个 _process 帧检查初值
            Check(bomb.Ring() != null && bomb.Ring().Visible, "场景3：炸弹带警示环");
            var ringR0 = bomb.Ring().Scale.X;
            Check(Mathf.IsEqualApprox(ringR0, 108.0f), "场景3：警示环初始半径 0.9×AoE = 108");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await WaitReal(0.25);
            Check(GodotObject.IsInstanceValid(bomb) && bomb.Ring().Scale.X < ringR0, "场景3：警示环随引信收缩");
            var hp0 = _gs.Health;
            await WaitReal(0.4);  // 越过引信 0.5s 引爆
            Check(!GodotObject.IsInstanceValid(bomb), "场景3：引爆后炸弹节点释放");
            Check(_gs.Health < hp0, "场景3：玩家站半径内引爆掉血");
            // 无敌不掉血（血量只可能因被动回血上升，不允许下降）
            player.SetInvincible(999.0f);
            var bomb2 = new FormationBomb();
            bomb2.Setup(Vector2.Zero, 0.3f, 20, 120.0f);
            bomb2.Position = player.Position;
            main.AddChild(bomb2);
            var hp1 = _gs.Health;
            await WaitReal(0.5);
            Check(!GodotObject.IsInstanceValid(bomb2), "场景3：第二枚炸弹已引爆释放");
            Check(_gs.Health >= hp1, "场景3：玩家无敌时引爆不掉血");

            // ================= 场景 4：全歼奖励 + 提前离场 =================
            StartFastEvent(evt);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(evt.GetState() == FormationStrikeEvent.State.FORMATION_ENTER, "场景4：事件再次启动");
            var score1 = _gs.Score;
            foreach (var child in evt.GetCrafts())
            {
                var craft = child.AsGodotObject() as FormationCraft;
                if (craft != null && GodotObject.IsInstanceValid(craft))
                {
                    craft.TakeDamage(9999);
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 4 机击坠 200×4 + 全歼 200 = 1000 基础分 ×中难度×2 = 2000
            Check(_gs.Score - score1 == 2000, "场景4：击坠分 + 全歼奖励入账（2000）");
            Check(evt.GetState() == FormationStrikeEvent.State.FORMATION_EXIT, "场景4：全歼立即提前离场");
            Check(CountRegisteredCrafts() == 0, "场景4：全歼后注册表无残留");
            Check(await WaitEventState(evt, FormationStrikeEvent.State.IDLE, 5.0), "场景4：提前离场后回 IDLE");

            // ================= 场景 5：abort 打断 =================
            StartFastEvent(evt);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(evt.IsActive(), "场景5：事件进行中");
            evt.Abort();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);  // queue_free 帧末生效后再断言清理
            Check(evt.GetState() == FormationStrikeEvent.State.IDLE, "场景5：abort 回 IDLE");
            Check(!spawner.WavesPaused(), "场景5：abort 恢复普通波次");
            Check(CountCrafts() == 0, "场景5：abort 清理全部战机实体");
            Check(CountRegisteredCrafts() == 0, "场景5：abort 后注册表无残留");
            Check(evt.CooldownLeft() > 0.0f, "场景5：abort 后冷却照计");
            Check(!evt.CanTrigger(), "场景5：冷却期内不可再次触发");

            // ================= 场景 6：无残留 =================
            await WaitReal(0.5);
            Check(evt.GetChildCount() == 1, "场景6：事件节点无 Timer 残留（仅通讯浮层）");
            Check(CountCrafts() == 0 && CountBombs() == 0, "场景6：Main 下无编队实体残留");
            Check(Mathf.IsEqualApprox(Engine.TimeScale, 1.0f), "收尾：time_scale = 1.0");
            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            _gs.HighScore = origHighScore;
            _gs.SaveProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"FORMATION STRIKE EVENT TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"FORMATION STRIKE EVENT TEST DONE, failures = {_failures}");
            Cleanup();
            TestExit.Quit(_failures);
        }
    }

    private void Cleanup()
    {
        try
        {
            _gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            GD.PushError($"FORMATION STRIKE EVENT TEST 清理异常: {e}");
        }
    }
}
