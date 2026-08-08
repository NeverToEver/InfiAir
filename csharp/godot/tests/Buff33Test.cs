using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 3.3 战斗补全测试（Buff/母舰/放弃，M7c 迁移）：laser_beam 光束、mothership_recall 冷却减半、
/// boost_recovery 恢复提升、explosive 抽卡 gating、长按 K 放弃出击。
/// </summary>
public partial class Buff33Test : Node
{
    private int _failures;

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

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var gs = GetNode<GameState>("/root/GameState");
        try
        {
            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            // give_up 输入映射由 project.godot 提供；缺失时运行时补齐（不影响断言语义）
            // R07：补齐动作记录标记，收尾还原（L 系列测试登记遗留——原实现 add 后不清理）
            var addedGiveUp = false;
            if (!InputMap.HasAction("give_up"))
            {
                InputMap.AddAction("give_up");
                addedGiveUp = true;
            }
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            var main = GetNode<Main>("Main");
            // 开场面板自显即暂停（冻结背景），先关闭解除
            var player = GetNode<Player>("Main/Player");
            var hud = GetNode<Hud>("Main/HUD");
            var buffUi = GetNode<BuffSelect>("Main/BuffUI");
            var spawner = GetNode<Spawner>("Main/Spawner");
            // 全自动开火会干扰激光断言，测试全程禁用
            player.SetAutoFire(false);
            player.SetInvincible(999.0f);
            spawner.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var child in main.GetChildren())
            {
                if (child is Enemy || child.HasMethod("TryGraze"))
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 1. Buff 候选池：新 buff 入池 + explosive gating（boss_kills>=3 才解锁）
            gs.BossKills = 0;
            var ids = new Godot.Collections.Array<StringName>();
            foreach (var b in buffUi.AvailableBuffs())
            {
                ids.Add(b["id"].AsStringName());
            }
            Check(ids.Contains("laser_beam"), "laser_beam 入抽卡候选池");
            Check(ids.Contains("mothership_recall"), "mothership_recall 入抽卡候选池");
            Check(ids.Contains("boost_recovery"), "boost_recovery 入抽卡候选池");
            Check(!ids.Contains("explosive"), "boss_kills<3 时 explosive 不入候选池");
            gs.BossKills = 3;
            ids.Clear();
            foreach (var b in buffUi.AvailableBuffs())
            {
                ids.Add(b["id"].AsStringName());
            }
            Check(ids.Contains("explosive"), "boss_kills>=3 时 explosive 入候选池");
            gs.BossKills = 0;

            // 2. laser_beam：无 buff 不触发；获得后触发光束并穿透伤害直线上 2 个敌人
            var laser = player.GetNode<LaserWeapon>("LaserWeapon");
            await Coroutine.WaitSeconds(this, 0.4);
            Check(!laser.Active(), "无 laser_beam buff 时激光不触发");
            gs.AddBuff("laser_beam");
            // 沿瞄准线布置两个静止高血敌人（不被击毁，避免得分里程碑干扰）
            var aim = (player.GetGlobalMousePosition() - player.GlobalPosition).Normalized();
            if (aim.Length() < 0.5f)
            {
                aim = Vector2.Up;
            }
            var enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");
            var e1 = enemyScene.Instantiate<Enemy>();
            e1.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            e1.Hp = 9999;
            e1.Speed = 0.0f;
            e1.CanShoot = false;
            e1.Position = player.GlobalPosition + aim * 300.0f;
            main.AddChild(e1);
            var e2 = enemyScene.Instantiate<Enemy>();
            e2.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            e2.Hp = 9999;
            e2.Speed = 0.0f;
            e2.CanShoot = false;
            e2.Position = player.GlobalPosition + aim * 600.0f;
            main.AddChild(e2);
            var wait = 0.0;
            while (!laser.Active() && wait < 2.0)
            {
                await Coroutine.WaitSeconds(this, 0.1);
                wait += 0.1;
            }
            Check(laser.Active(), "获得 laser_beam 后触发光束");
            Check(laser.Beam().Visible, "光束视觉可见");
            await Coroutine.WaitSeconds(this, 0.5);
            Check(GodotObject.IsInstanceValid(e1) && e1.Hp < 9999, "光束对直线上敌人 1 造成伤害");
            Check(GodotObject.IsInstanceValid(e2) && e2.Hp < 9999, "光束穿透对直线上敌人 2 造成伤害");
            // 3s 持续结束后进入约 8s 冷却
            await Coroutine.WaitSeconds(this, 2.8);
            Check(!laser.Active(), "3 秒后光束结束");
            Check(laser.Cooldown() > 6.0f, "光束结束后进入约 8s 冷却");
            // 冷却结束可再次触发（测试直接缩短冷却，不真等 8s）
            laser.SetCooldown(0.05f);
            await Coroutine.WaitSeconds(this, 0.3);
            Check(laser.Active(), "冷却结束后激光可再次触发");
            laser.SetActiveTime(0.01f);
            await Coroutine.WaitSeconds(this, 0.2);
            if (GodotObject.IsInstanceValid(e1))
            {
                e1.QueueFree();
            }
            if (GodotObject.IsInstanceValid(e2))
            {
                e2.QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 3. mothership_recall：每层母舰冷却 ×0.5（基础 60s→30s→15s）
            main.OnMothershipDeparted(60.0f);
            Check(Mathf.IsEqualApprox(main.DockCooldown(), 60.0f), "无 recall 时母舰冷却 60s");
            gs.AddBuff("mothership_recall");
            main.OnMothershipDeparted(60.0f);
            Check(Mathf.IsEqualApprox(main.DockCooldown(), 30.0f), "recall 1 层母舰冷却 30s");
            gs.AddBuff("mothership_recall");
            main.OnMothershipDeparted(60.0f);
            Check(Mathf.IsEqualApprox(main.DockCooldown(), 15.0f), "recall 2 层母舰冷却 15s");
            Check(main.DockStatusText().Contains("母舰冷却"), "母舰状态文本联动冷却值");
            main.SetDockCooldown(0.0f);

            // 4. boost_recovery：恢复速率每层 ×1.5（乘算），且实际回油生效
            Check(Mathf.IsEqualApprox(player.FuelRegenRate(), 20.0f), "无 buff 燃料恢复 20/s");
            gs.AddBuff("boost_recovery");
            Check(Mathf.IsEqualApprox(player.FuelRegenRate(), 30.0f), "boost_recovery 1 层恢复 ×1.5");
            gs.AddBuff("boost_recovery");
            Check(Mathf.IsEqualApprox(player.FuelRegenRate(), 45.0f), "boost_recovery 2 层乘算 ×2.25");
            player.SetFuel(50.0f);
            await Coroutine.WaitSeconds(this, 1.0);
            Check(player.FuelAmount() > 80.0f, "提升后的恢复速率实际生效（1s 回 45）");

            // 5. 长按 K 放弃出击：蓄力可取消，蓄满 3s 自毁进死亡结算
            // 制造敌弹供死亡回放录制（B 梯队：回放重演死因片段；蓄力 3.3s 期间 main._process
            // 持续采样填充环形缓冲）
            var eb = GD.Load<PackedScene>("res://scenes/bullet.tscn").Instantiate<Bullet>();
            eb.Setup(Vector2.Down, 200.0f, 1, false);
            eb.Position = new Vector2(960.0f, 300.0f);
            main.AddChild(eb);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ActionPress("give_up");
            await Coroutine.WaitSeconds(this, 1.0);
            Check(main.GiveUpCharge() > 0.0f, "K 蓄力进行中");
            Check(hud.GiveUpLabel().Visible, "HUD 显示放弃蓄力进度");
            Input.ActionRelease("give_up");
            await Coroutine.WaitSeconds(this, 0.2);
            Check(main.GiveUpCharge() == 0.0f && !hud.GiveUpLabel().Visible, "松开 K 取消蓄力");
            Check(gs.Health == gs.MaxHealth(), "取消蓄力未自毁");
            Input.ActionPress("give_up");
            await Coroutine.WaitSeconds(this, 3.3);
            Input.ActionRelease("give_up");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(gs.Health == 0.0, "长按 K 3s 自毁");
            Check(player.IsDead(), "自毁后玩家死亡");
            Check(GetNode<GameOverUi>("Main/GameOverUI").Visible, "自毁进入死亡结算面板");
            Check(GetTree().Paused, "结算时游戏暂停");
            // B 梯队（fair plan §8）：死亡回放演出已挂树（幽灵弹幕重放死因；process_mode=ALWAYS
            // 暂停树中照常播放，播完自毁）
            Node? replayNode = null;
            foreach (var child in main.GetChildren())
            {
                if (child is DeathReplayPlayer)
                {
                    replayNode = child;
                    break;
                }
            }
            Check(replayNode != null, "死亡回放演出已启动");
            if (replayNode != null)
            {
                await ToSignal(GetTree().CreateTimer(0.2, true, false, true),
                    SceneTreeTimer.SignalName.Timeout);  // process_always：暂停树中计时
                Check(GodotObject.IsInstanceValid(replayNode), "回放演出播放中（0.2s 后未结束）");
            }

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            // R07：还原运行时补齐的输入动作（与补齐对称）
            if (addedGiveUp)
            {
                InputMap.EraseAction("give_up");
            }
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BUFF33 TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BUFF33 TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
