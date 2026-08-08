using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 教程流程测试（M7c 迁移）：6 阶段推进、锁血、对接、返航开基地、狂暴过关、Esc 退出、profile 写入。
/// 收尾沿用 GDScript 原语义：注入 ui_cancel 触发场景切换（本节点随后被释放），
/// 用绑定 SceneTree 的定时器延迟 1s 后 TestExit.Quit（避免在已释放节点上 await）。
/// </summary>
public partial class TutorialTest : Node
{
    private readonly PackedScene _tutorialScene = GD.Load<PackedScene>("res://scenes/tutorial.tscn");

    private int _failures;
    private bool _quitScheduled;

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
            // 清理持久化状态
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.TutorialDone = false;
            gs.SaveProfile();

            // ---------- 软锁路径 a：玩家死亡 → 显示任务失败提示（独立实例，不影响主流程） ----------
            var tutA = _tutorialScene.Instantiate<Tutorial>();
            AddChild(tutA);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var playerA = tutA.GetNode<Player>("Player");
            playerA.SetAutoFire(false);
            playerA.SetInvincible(0.0f);
            playerA.SetLastHitFrame(-1);
            playerA.TakeDamage(9999.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(playerA.IsDead(), "死亡路径：玩家已死亡");
            Check(tutA.Failed(), "死亡路径：教程进入失败态");
            Check(tutA.TitleLabel().Text == Tr("TUT_FAIL_TITLE"), "死亡路径：标题显示任务失败（tr 命中）");
            Check(tutA.ObjectiveLabel().Text == Tr("TUT_FAIL_DESC"), "死亡路径：提示 Esc 退出（tr 命中）");
            tutA.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ---------- 主流程：6 阶段推进 ----------
            AddChild(_tutorialScene.Instantiate<Tutorial>());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var tut = GetNode<Tutorial>("Tutorial");
            var player = tut.GetNode<Player>("Player");
            player.SetAutoFire(false);

            // 阶段 1：3 个静止靶机
            Check(tut.Stage() == 0, "教程进入阶段 1");
            var targets = new Godot.Collections.Array<Enemy>();
            foreach (var c in tut.GetChildren())
            {
                if (c is Enemy enemy)
                {
                    targets.Add(enemy);
                }
            }
            Check(targets.Count == 3, "阶段 1 生成 3 个靶机");
            foreach (var t in targets)
            {
                t.TakeDamage(9999);
            }
            await Coroutine.WaitSeconds(this, 1.3);
            Check(tut.Stage() == 1, "阶段 1 → 2（击杀 3 靶过关）");

            // 阶段 2：加速 ×2 + 冲刺 ×2
            Check(gs.BuffCount("phase_dash") == 1, "阶段 2 发放 phase_dash");
            for (int i = 0; i < 2; i++)
            {
                Input.ActionPress("boost");
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                Input.ActionRelease("boost");
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
            for (int i = 0; i < 2; i++)
            {
                player.SetDashCooldown(0.0f);  // 绕过 4s 冲刺冷却，缩短测试
                Input.ActionPress("dash");
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                Input.ActionRelease("dash");
                await Coroutine.WaitSeconds(this, 0.4);
            }
            Check(tut.BoostCount() == 2 && tut.DashCount() == 2, "阶段 2 输入计数");
            await Coroutine.WaitSeconds(this, 1.3);
            Check(tut.Stage() == 2, "阶段 2 → 3");

            // 阶段 3：5 敌 + 锁血不死
            var enemies = new Godot.Collections.Array<Enemy>();
            foreach (var c in tut.GetChildren())
            {
                if (c is Enemy enemy)
                {
                    enemies.Add(enemy);
                }
            }
            Check(enemies.Count == 5, "阶段 3 刷 5 只敌机");
            for (int i = 0; i < 3; i++)
            {
                player.SetInvincible(0.0f);
                player.SetLastHitFrame(-1);
                player.TakeDamage(10.0f);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
            Check(gs.Health > 0.0 && !player.IsDead(), "战斗阶段锁血不死");
            // 补刷兜底：先击杀 2 只，其余 3 只未计击杀直接离场（模拟飞出屏幕自毁）→ 自动补足剩余 3 只
            enemies[0].TakeDamage(9999);
            enemies[1].TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            for (int i = 2; i < 5; i++)
            {
                enemies[i].QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(tut.StageKills() == 2, "阶段 3 已计 2 杀（离场不虚增）");
            enemies.Clear();
            foreach (var c in tut.GetChildren())
            {
                if (c is Enemy enemy)
                {
                    enemies.Add(enemy);
                }
            }
            Check(enemies.Count == 3, "阶段 3 敌机离场后自动补足剩余 3 只");
            Check(tut.Stage() == 2, "补刷后仍在阶段 3");
            foreach (var e in enemies)
            {
                e.TakeDamage(9999);
            }
            await Coroutine.WaitSeconds(this, 1.3);
            Check(tut.Stage() == 3, "阶段 3 → 4");

            // 阶段 4：长按 H 蓄力召唤母舰（对齐正局 dock 键），穿梭入场后自动对接（弹匣加速消耗到自动释放）
            Check(tut.Mothership() == null, "阶段 4 进场时母舰未召唤（需长按 H）");
            Input.ActionPress("dock");
            await Coroutine.WaitSeconds(this, tut.DOCK_CHARGE_TIME + 0.3);
            Input.ActionRelease("dock");
            Check(tut.Mothership() != null, "阶段 4 蓄力完成后母舰已召唤");
            var ms = tut.Mothership()!;
            ms.SetStateTimer(ms.WARP_IN_TIME);  // 快进穿梭入场，到位触发自动对接
            ms.SetMagCells(1);  // 加速演示：1 格弹匣 2s 后自动释放
            await Coroutine.WaitSeconds(this, 6.0);
            Check(tut.Stage() == 4, "对接完成 → 阶段 5");

            // 阶段 5：长按 B 打开基地
            Input.ActionPress("homecoming");
            await Coroutine.WaitSeconds(this, 1.8);
            Input.ActionRelease("homecoming");
            await Coroutine.WaitSeconds(this, 0.3);
            Check(tut.BaseUi() != null && GetTree().Paused, "返航打开基地界面");
            await Coroutine.WaitSeconds(this, 1.5);
            Check(tut.Stage() == 5, "基地关闭 → 阶段 6");
            Check(!GetTree().Paused, "基地关闭后恢复");

            // 阶段 6：Boss 狂暴即过关
            Check(tut.Boss() != null, "阶段 6 Boss 已生成");
            var boss = tut.Boss()!;
            // 软锁路径 b：Boss 未狂暴逃跑离场 → 重置阶段 6 重刷 Boss
            boss.BeginEscape();
            boss.Position = new Vector2(boss.Position.X, -300.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(!GodotObject.IsInstanceValid(boss), "阶段 6：未狂暴 Boss 逃跑离场");
            Check(tut.Stage() == 5 && !tut.Finished(), "阶段 6：逃跑后仍在阶段 6 未过关");
            Check(tut.Boss() != null && GodotObject.IsInstanceValid(tut.Boss()) && tut.Boss() != boss,
                "阶段 6：逃跑后重置重刷 Boss");
            boss = tut.Boss()!;
            boss.TakeDamage((int)(boss.MaxHp * 0.75f));
            await Coroutine.WaitSeconds(this, 0.3);
            Check(tut.Finished(), "Boss 狂暴触发即过关");
            Check(tut.CompletePanel() != null, "教程完成面板显示");
            Check(gs.TutorialDone, "tutorial_done 已写 profile");
            Check(Engine.TimeScale == 1.0f, "time_scale 正常");

            // K16：教程通关写盘的持久化状态收尾恢复（TESTING.md 约定：结束时清理自己创建的持久化状态；
            // 测试开头已把 tutorial_done 置 false，此处恢复 false 不污染用户 profile）
            gs.TutorialDone = false;
            gs.SaveProfile();

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();

            GD.Print($"TUTORIAL TEST DONE, failures = {_failures}");
            // Esc 退出：触发场景切换（本节点随后被释放），用绑定 SceneTree 的定时器收尾
            // C31：注入 ui_cancel 动作走公开输入路径，不直调私有 _exit_tutorial
            var cancel = new InputEventAction();
            cancel.Action = "ui_cancel";
            cancel.Pressed = true;
            Input.ParseInputEvent(cancel);
            var fails = _failures;
            var timer = GetTree().CreateTimer(1.0);
            timer.Timeout += () => TestExit.Quit(fails);
            _quitScheduled = true;
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"TUTORIAL TEST 异常: {e}");
        }
        finally
        {
            if (!_quitScheduled)
            {
                GD.Print($"TUTORIAL TEST DONE, failures = {_failures}");
                TestExit.Quit(_failures);
            }
        }
    }
}
