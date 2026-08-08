using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 基地数据层测试：RP 经济、三常驻任务、天赋路线互斥、存档往返。
/// 只操作 GameState autoload，不加载 main 场景。
/// </summary>
public partial class BaseSystemTest : Node
{
    private int _failures;

    /// <summary>
    /// M7（2026-08-06 审计）：profile 快照还原——base_system 的高分榜段直写
    /// GameState.highscores + save_profile 清零落盘（L15 档案称已修与 git 事实不符），
    /// 备份/还原防本地 pre-login 最高分与高分榜被永久销毁。
    /// </summary>
    private Godot.Collections.Dictionary _profileBackup = new();

    /// <summary>2026-08-06 审计：键位快照还原（H02 段 rebind/reset 自动落盘，防开发者键位被重置）。</summary>
    private Godot.Collections.Dictionary _keyBackup = new();

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

    private void BackupProfile()
    {
        var gs = GetNode<GameState>("/root/GameState");
        _profileBackup = new Godot.Collections.Dictionary();
        foreach (var f in new[] { gs.PROFILE_PATH, gs.PROFILE_PATH + ".corrupt" })
        {
            var exists = Godot.FileAccess.FileExists(f);
            _profileBackup[f] = new Godot.Collections.Dictionary
            {
                ["exists"] = exists,
                ["content"] = exists ? Godot.FileAccess.GetFileAsString(f) : "",
            };
        }
    }

    private void RestoreProfile()
    {
        var gs = GetNode<GameState>("/root/GameState");
        foreach (var key in _profileBackup.Keys)
        {
            var f = key.AsString();
            var b = _profileBackup[key].AsGodotDictionary();
            if (b["exists"].AsBool())
            {
                var fh = Godot.FileAccess.Open(f, Godot.FileAccess.ModeFlags.Write);
                fh.StoreString(b["content"].AsString());
                fh.Close();
            }
            else if (Godot.FileAccess.FileExists(f))
            {
                DirAccess.RemoveAbsolute(f);
            }
        }
    }

    private void BackupKeys()
    {
        var gs = GetNode<GameState>("/root/GameState");
        _keyBackup = gs.KeyBindings.Duplicate(true);
    }

    private void RestoreKeys()
    {
        var gs = GetNode<GameState>("/root/GameState");
        gs.KeyBindings = _keyBackup.Duplicate(true);
        gs.ApplyKeyBindings();
        gs.SaveProfile();
    }

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget
        _ = RunAsync();
    }

    private async System.Threading.Tasks.Task RunAsync()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            // M7：profile 快照（须在任何覆写/落盘前捕获原始 pre-login 最高分与高分榜）
            BackupProfile();
            // 键位快照（H02 改键段自动落盘）
            BackupKeys();
            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            gs.ResetRun();

            // 1. 初始状态
            Check(gs.Rp == 0, "初始 RP 为 0");
            Check(gs.MissionProgress("kill_5") == 0, "初始任务进度为 0");
            Check(!gs.IsMissionDone("boss_1"), "初始任务未完成");

            // 2. Boss 击杀 +5RP，并推进 boss_1 任务
            gs.AddBossKill();
            Check(gs.Rp == 5, "Boss 击杀 +5RP");
            Check(gs.IsMissionDone("boss_1"), "boss_1 任务完成");
            Check(gs.MissionProgress("boss_1") == 1, "boss_1 进度为 1");

            // 3. 领取奖励 + 重复领奖拒绝
            Check(gs.ClaimMission("boss_1"), "领取 boss_1 奖励成功");
            Check(gs.Rp == 8, "任务奖励 +3RP 入账");
            Check(!gs.ClaimMission("boss_1"), "重复领奖被拒绝");
            Check(gs.Rp == 8, "重复领奖不重复入账");
            Check(!gs.ClaimMission("kill_5"), "未完成任务不能领奖");

            // 4. kill_5：击杀计数到 5
            for (var i = 0; i < 5; i++)
            {
                gs.AddKill();
            }

            Check(gs.MissionProgress("kill_5") == 5, "kill_5 进度追踪击杀数");
            Check(gs.IsMissionDone("kill_5"), "kill_5 任务完成");
            Check(gs.ClaimMission("kill_5"), "领取 kill_5 奖励成功");
            Check(gs.Rp == 11, "RP 累计正确");

            // 5. survive_180：对局存活秒数（用真实时间等待跨过 180s 阈值）
            gs.RunTime = 179.9;
            await Coroutine.WaitSeconds(this, 0.3);
            Check(gs.MissionProgress("survive_180") >= 180, "survive_180 进度按存活秒数推进");
            Check(gs.IsMissionDone("survive_180"), "survive_180 任务完成");
            Check(gs.ClaimMission("survive_180"), "领取 survive_180 奖励成功");
            Check(gs.Rp == 14, "三任务 RP 全部入账");

            // 6. spend_rp 余额校验
            Check(!gs.SpendRp(99), "余额不足 spend_rp 返回 false");
            Check(gs.Rp == 14, "余额不足不扣减");
            Check(gs.SpendRp(gs.RP_REPAIR_COST), "维修消费 2RP 成功");
            Check(gs.Rp == 12, "消费后余额正确");

            // 7. 天赋路线：合并层数 + 锁定未选 buff
            gs.AddBuff("spread_shot");
            gs.AddBuff("spread_shot");
            gs.AddBuff("laser_beam");
            Check(!gs.ChooseRoute("offense", "phase_dash"), "不属于该线的 buff 被拒绝");
            Check(!gs.ChooseRoute("bad_line", "spread_shot"), "非法路线名被拒绝");
            Check(!gs.ChooseRoute("mobility", "phase_dash"), "零层数路线被拒绝");
            Check(gs.ChooseRoute("offense", "spread_shot"), "路线选择成功");
            Check(gs.BuffCount("spread_shot") == 3, "同线层数合并到所选 buff");
            Check(gs.BuffCount("laser_beam") == 0, "未选 buff 层数清零");
            Check(gs.IsBuffLocked("laser_beam"), "未选 buff 被锁定");
            Check(!gs.IsBuffLocked("spread_shot"), "所选 buff 不锁定");
            Check(!gs.IsBuffLocked("phase_dash"), "未选路线的线不锁定");
            Check(gs.ChosenRoutes["offense"].AsStringName() == new StringName("spread_shot"), "路线选择已记录");

            // 8. 存档往返：rp / 路线 / 任务进度全保留
            gs.SaveRun(50.0, gs.RunTime);
            var savedRp = gs.Rp;
            gs.Rp = 0;
            gs.Buffs.Clear();
            gs.ResetMissions();
            gs.ChosenRoutes.Clear();
            gs.LockedRoutes.Clear();
            gs.ApplyRunSave(gs.LoadRunData());
            Check(gs.Rp == savedRp, "存档恢复 RP");
            Check(gs.MissionProgress("kill_5") == 5, "存档恢复任务进度");
            Check(gs.IsMissionClaimed("boss_1"), "存档恢复任务已领取标记");
            Check(!gs.ClaimMission("boss_1"), "恢复后已领取任务仍拒绝重复领奖");
            Check(gs.BuffCount("spread_shot") == 3, "存档恢复合并后的层数");
            Check(gs.ChosenRoutes["offense"].AsStringName() == new StringName("spread_shot"), "存档恢复路线选择");
            Check(gs.IsBuffLocked("laser_beam"), "存档恢复锁定 buff");
            Check(gs.MissionProgress("survive_180") >= 180, "存档恢复存活进度");

            // 9. reset_run 清零新状态
            gs.ResetRun();
            Check(gs.Rp == 0, "reset_run 清零 RP");
            Check(gs.MissionProgress("boss_1") == 0, "reset_run 清零任务进度");
            Check(!gs.IsMissionClaimed("boss_1"), "reset_run 清零领取标记");
            Check(gs.ChosenRoutes.Count == 0 && gs.LockedRoutes.Count == 0, "reset_run 清零路线");
            Check(!gs.IsBuffLocked("laser_beam"), "reset_run 解除锁定");

            // 9b. A 审计：SaveManager 原子写——save 后正本存在、数据正确、重复 save（覆盖）不丢
            var sm = new SaveManager();
            var testPath = "user://audit_save_test.json";
            sm.Delete(testPath);
            Check(sm.Save(testPath, new Godot.Collections.Dictionary { ["version"] = 2, ["score"] = 500 }), "A审计：save 成功");
            Check(sm.Exists(testPath), "A审计：save 后正本存在（rename 成功，非孤立 tmp）");
            var loaded = sm.Load(testPath);
            Check(loaded.GetValueOrDefault("score", -1).AsInt32() == 500, "A审计：save/load 数据正确（500）");
            // 覆盖写（原实现先删正本再 rename 致 rename 失败丢数据；修复后原子覆盖）
            Check(sm.Save(testPath, new Godot.Collections.Dictionary { ["version"] = 2, ["score"] = 999 }), "A审计：覆盖 save 成功");
            loaded = sm.Load(testPath);
            Check(loaded.GetValueOrDefault("score", -1).AsInt32() == 999, "A审计：覆盖后数据正确（999）");
            // 损坏隔离不影响正本
            sm.Delete(testPath);

            // 10. 本地高分榜（P0-3）：排序 / 同分排后 / 上限截断 / 持久化往返
            gs.Highscores.Clear();
            gs.SaveProfile();
            Check(gs.SubmitHighscore(0) == 0, "高分榜：0 分不入榜");
            Check(gs.SubmitHighscore(100) == 1, "高分榜：首条排第 1");
            Check(gs.SubmitHighscore(50) == 2, "高分榜：低分排第 2");
            Check(gs.SubmitHighscore(80) == 2, "高分榜：中间分插入第 2");
            Check(gs.SubmitHighscore(100) == 2, "高分榜：同分新条目排后");
            Check(gs.Highscores.Count == 4, "高分榜：条目数正确");
            Check(gs.Highscores[0]["score"].AsInt32() == 100, "高分榜：榜首为最高分");
            Check(gs.Highscores[1]["score"].AsInt32() == 100, "高分榜：同分按先到先得排前");
            Check(gs.HighscoresText(3) == "1. 100\n2. 100\n3. 80", "高分榜：榜单文本 Top3");
            for (var i = 0; i < 100; i++)
            {
                gs.SubmitHighscore(200 - i);
            }

            Check(gs.Highscores.Count == gs.HIGHSCORE_LIMIT, "高分榜：上限截断");
            Check(gs.SubmitHighscore(1) == 0, "高分榜：超出上限的分数不入榜");
            var firstScore = gs.Highscores[0]["score"].AsInt32();
            Check(firstScore == 200, "高分榜：截断后榜首不变");
            gs.SaveProfile();
            gs.LoadProfile();
            Check(gs.Highscores.Count == gs.HIGHSCORE_LIMIT, "高分榜：持久化往返条目数一致");
            Check(gs.Highscores[0]["score"].AsInt32() == firstScore, "高分榜：持久化往返榜首一致");
            gs.Highscores.Clear();
            gs.SaveProfile();

            // 11. 手柄默认绑定（P0-1 竞品调研）：运行时装配 + 右摇杆四向动作（H01 修正）
            Check(
                InputMap.HasAction("aim_left")
                && InputMap.HasAction("aim_right")
                && InputMap.HasAction("aim_up")
                && InputMap.HasAction("aim_down"),
                "H01：右摇杆四向瞄准动作已注册");
            var aimEvents = InputMap.ActionGetEvents(new StringName("aim_right"));
            var hasAimAxis = false;
            foreach (var ev in aimEvents)
            {
                if (ev is InputEventJoypadMotion jm && (int)jm.Axis == 2 && jm.AxisValue == 1.0f)
                {
                    hasAimAxis = true;
                }
            }

            Check(hasAimAxis, "H01：右摇杆动作含正确轴事件（axis 2/+1）");
            var hasMoveJoy = false;
            foreach (var ev in InputMap.ActionGetEvents(new StringName("move_up")))
            {
                if (ev is InputEventJoypadMotion)
                {
                    hasMoveJoy = true;
                }
            }

            Check(hasMoveJoy, "P0-1：移动动作含手柄摇杆绑定");
            var hasDashJoy = false;
            foreach (var ev in InputMap.ActionGetEvents(new StringName("dash")))
            {
                if (ev is InputEventJoypadButton)
                {
                    hasDashJoy = true;
                }
            }

            Check(hasDashJoy, "P0-1：动作键含手柄按钮绑定");

            // H02（健壮性审核）：改键只擦除键盘事件，手柄事件保留
            gs.RebindAction(new StringName("dash"), (int)Key.M);
            var dashEventsAfter = InputMap.ActionGetEvents(new StringName("dash"));
            var dashJoyKept = false;
            foreach (var ev in dashEventsAfter)
            {
                if (ev is InputEventJoypadButton)
                {
                    dashJoyKept = true;
                }
            }

            Check(dashJoyKept, "H02：改键后手柄事件保留");
            gs.ResetKeyBindings();
            var dashEventsReset = InputMap.ActionGetEvents(new StringName("dash"));
            var dashJoyReset = false;
            foreach (var ev in dashEventsReset)
            {
                if (ev is InputEventJoypadButton)
                {
                    dashJoyReset = true;
                }
            }

            Check(dashJoyReset, "H02：重置键位后手柄事件保留");

            // 12. 手柄设置（P0-1 设置页）：默认值 / setter 应用死区 / 持久化往返
            Check(gs.JoyDeadzone == 0.5 && gs.JoyAimSpeed >= 200.0, "P0-1：手柄设置默认值（死区 0.5 / 灵敏度≥200）");
            gs.SetJoyDeadzone(0.7);
            Check(Mathf.IsEqualApprox(InputMap.ActionGetDeadzone(new StringName("move_up")), 0.7f), "P0-1：死区 setter 应用至 InputMap");
            gs.SetJoyAimSpeed(1800.0);
            gs.SaveProfile();
            gs.LoadProfile();
            Check(gs.JoyAimSpeed == 1800.0, "P0-1：瞄准灵敏度持久化往返");
            Check(gs.JoyDeadzone == 0.7, "P0-1：死区持久化往返");
            gs.SetJoyDeadzone(0.5);
            gs.SetJoyAimSpeed(gs.Cfg("player.aim_assist.joy_speed", 1400.0).AsDouble());
            gs.SaveProfile();  // K06：setter 不再自动写盘，收尾恢复默认值须显式落盘（否则 profile 留存 0.7/1800 污染后续场景）

            // 13. PS 布局适配（P0-1 延伸）：GUID 判定纯函数 + 按钮标签映射（默认 Xbox / 切 PS）
            Check(gs.IsPsGuid("030000004c050000c405000000010000"), "P0-1：Sony GUID 判定（vendor 054c）");
            Check(!gs.IsPsGuid("030000005e0400008e02000000010000"), "P0-1：非 Sony GUID 不误判");
            Check(gs.JoyButtonLabel(0) == "A" && gs.JoyButtonLabel(5) == "RB", "P0-1：Xbox 布局标签映射");
            var savedLayout = gs.JoyLayout;
            gs.JoyLayout = new StringName("ps");
            Check(gs.JoyButtonLabel(0) == "✕" && gs.JoyButtonLabel(4) == "L1", "P0-1：PS 布局标签映射（✕/L1）");
            gs.JoyLayout = savedLayout;

            gs.DeleteSave();
            // M7：还原原始 profile（最高分/高分榜/设置项），防本地数据被清零
            RestoreProfile();
            // 还原用户自定义键位（H02 改键段已把测试键位落盘）
            RestoreKeys();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BASE SYSTEM TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BASE SYSTEM TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
