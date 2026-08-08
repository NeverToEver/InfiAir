using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 基地任务轮换测试（2026-08-05，docs/FOG_EVENTS.md §1）：
/// 初始手牌 / TaskPool 无放回抽取算法 / RefreshPoints 经济与点数校验 /
/// 刷新重抽（排除在场 id、保留已完成未领取）/ 按 kind 分发进度 / 存档往返 / reset_run 复位。
/// 只操作 GameState autoload 与 TaskPool，不加载 main 场景。
/// M7c：自 test/base_task_refresh_test.gd 迁移（B09）。
/// </summary>
public partial class BaseTaskRefreshTest : Node
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

    private Godot.Collections.Array<StringName> ActiveIds() => _gs.ActiveMissionIds();

    private bool IdsInPool(Godot.Collections.Array<StringName> ids)
    {
        foreach (var id in ids)
        {
            var found = false;
            foreach (var def in _gs.MISSION_POOL)
            {
                if (def["id"].AsStringName() == id)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                return false;
            }
        }
        return true;
    }

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget（约定 §Async）
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            _gs = GetNode<GameState>("/root/GameState");
            _gs.DeleteSave();
            _gs.ResetRun();

            // 1. 初始手牌：固定三任务（保持既有 id 语义）
            Check(_gs.RefreshPoints == 0, "初始刷新点数为 0");
            Check(ActiveIds().Count == 3, "初始在场任务数为 3");
            Check(
                _gs.Missions.ContainsKey(new StringName("kill_5"))
                    && _gs.Missions.ContainsKey(new StringName("survive_180"))
                    && _gs.Missions.ContainsKey(new StringName("boss_1")),
                "初始手牌为 kill_5/survive_180/boss_1"
            );
            Check(_gs.REFRESH_COST == 2 && _gs.GRANT_PER_VISIT == 1, "刷新经济档位：2 点/次刷新，1 点/次进基地（balance.json）");

            // 2. 点数校验：余额不足禁止刷新且不扣减
            Check(!_gs.CanRefreshMissions(), "初始点数不足，刷新资格为 false");
            Check(!_gs.RefreshMissions(), "点数不足 refresh_missions 返回 false");
            Check(_gs.RefreshPoints == 0, "点数不足刷新不扣减");
            _gs.GrantRefreshPoints();
            Check(_gs.RefreshPoints == 1, "进基地发放 1 刷新点");
            Check(!_gs.CanRefreshMissions(), "1 点仍不足（成本 2 点）");
            _gs.GrantRefreshPoints();
            Check(_gs.RefreshPoints == 2, "两次进基地累计 2 点");
            Check(_gs.CanRefreshMissions(), "点数足够，刷新资格为 true");

            // 3. 刷新重抽：消耗点数、槽位数保持 3、id 互异且来自任务池、排除在场 id
            var before = ActiveIds();
            Check(_gs.RefreshMissions(), "点数足够刷新成功");
            Check(_gs.RefreshPoints == 0, "刷新消耗 2 点");
            var after = ActiveIds();
            Check(after.Count == 3, "刷新后在场任务数仍为 3");
            Check(IdsInPool(after), "刷新后任务全部来自任务池");
            var seen = new Godot.Collections.Dictionary();
            var distinct = true;
            foreach (var id in after)
            {
                if (seen.ContainsKey(id))
                {
                    distinct = false;
                }
                seen[id] = true;
            }
            Check(distinct, "刷新后任务 id 互不重复");
            var allReplaced = true;
            foreach (var id in before)
            {
                if (after.Contains(id))
                {
                    allReplaced = false;
                }
            }
            Check(allReplaced, "刷新排除在场 id（无重复任务）");

            // 4. 按 kind 分发进度：kill/boss 类任务进度 = 击杀数/Boss 击杀数（轮换后 id 变化仍推进）
            // 构造确定性在场集合（绕过随机刷新），验证进度按 kind 分发、goal 各自生效
            _gs.ResetMissions();
            _gs.Missions = new Godot.Collections.Dictionary
            {
                [new StringName("kill_15")] = new Godot.Collections.Dictionary { ["progress"] = 0, ["claimed"] = false, ["goal"] = 15 },
                [new StringName("survive_60")] = new Godot.Collections.Dictionary { ["progress"] = 0, ["claimed"] = false, ["goal"] = 60 },
                [new StringName("boss_2")] = new Godot.Collections.Dictionary { ["progress"] = 0, ["claimed"] = false, ["goal"] = 2 },
            };
            _gs.AddKill();
            _gs.AddKill();
            _gs.AddKill();
            Check(_gs.MissionProgress("kill_15") == 3, "kill 类任务进度 = 击杀数（3/15）");
            Check(!_gs.IsMissionDone("kill_15"), "goal=15 的 kill 任务 3 杀未完成");
            _gs.AddKill();
            _gs.AddKill();
            Check(_gs.MissionProgress("kill_15") == 5, "kill 类任务进度随击杀推进（5/15）");
            _gs.RunTime = 61.0;
            // 真实时间等 GameState._process 推进整秒边界（ignore_time_scale，与原 4 参 create_timer 一致）
            await ToSignal(GetTree().CreateTimer(0.2, true, false, true), SceneTreeTimer.SignalName.Timeout);
            Check(_gs.IsMissionDone("survive_60"), "survive 任务按存活秒推进（61s ≥ goal 60）");
            _gs.AddBossKill();
            Check(_gs.MissionProgress("boss_2") == 1, "boss 类任务进度 = Boss 击杀数（1/2）");
            Check(!_gs.IsMissionDone("boss_2"), "goal=2 的 boss 任务 1 杀未完成");
            _gs.AddBossKill();
            Check(_gs.IsMissionDone("boss_2"), "goal=2 的 boss 任务 2 杀完成");
            Check(
                _gs.MissionGoal("kill_15") == 15
                    && _gs.MissionGoal("survive_300") == 300
                    && _gs.MissionGoal("boss_3") == 3,
                "任务池新 id 的 goal 正确"
            );

            // 5. 保留已完成未领取任务：刷新不吞待领奖励
            _gs.ResetRun();  // 清零击杀计数（第 4 节累计了 5 杀 2 Boss，避免进度断言串扰）
            _gs.AddKill();  // 5 次击杀（reset 后从 0 起）
            for (var i = 0; i < 4; i++)
            {
                _gs.AddKill();
            }
            Check(_gs.IsMissionDone("kill_5") && !_gs.IsMissionClaimed("kill_5"), "kill_5 已完成未领取");
            _gs.GrantRefreshPoints();
            _gs.GrantRefreshPoints();
            Check(_gs.RefreshMissions(), "刷新成功");
            Check(_gs.Missions.ContainsKey(new StringName("kill_5")), "已完成未领取任务保留");
            Check(_gs.MissionProgress("kill_5") == 5 && !_gs.IsMissionClaimed("kill_5"), "保留任务进度与领取标记不变");
            var otherCount = 0;
            foreach (var id in ActiveIds())
            {
                if (id != new StringName("kill_5"))
                {
                    otherCount++;
                }
            }
            Check(otherCount == 2, "其余两个槽位被重抽（槽位总数仍为 3）");

            // 6. 存档往返：refresh_points 与轮换后的任务集合保留
            _gs.SaveRun(50.0, _gs.RunTime);
            var savedIds = ActiveIds();
            var savedPoints = _gs.RefreshPoints;
            _gs.RefreshPoints = 0;
            _gs.ResetMissions();
            _gs.ApplyRunSave(_gs.LoadRunData());
            Check(_gs.RefreshPoints == savedPoints, "存档恢复刷新点数");
            var restoredIds = ActiveIds();
            var restoredOk = restoredIds.Count == savedIds.Count;
            foreach (var id in savedIds)
            {
                if (!restoredIds.Contains(id))
                {
                    restoredOk = false;
                }
            }
            Check(restoredOk, "存档恢复轮换后的任务集合");
            Check(_gs.MissionProgress("kill_5") == 5, "存档恢复轮换任务进度");
            // 恢复后 kind 进度仍可推进（轮换 id 不依赖初始手牌）
            var bossId = new StringName("");
            foreach (var id in restoredIds)
            {
                foreach (var def in _gs.MISSION_POOL)
                {
                    if (def["id"].AsStringName() == id && def["kind"].AsStringName() == new StringName("boss"))
                    {
                        bossId = id;
                    }
                }
            }
            if (bossId != new StringName(""))
            {
                _gs.AddBossKill();
                Check(_gs.MissionProgress(bossId) == 1, $"恢复后 boss 类任务进度按 kind 推进（{bossId}）");
            }

            // 7. reset_run 复位：刷新点数清零、任务回初始手牌
            _gs.ResetRun();
            Check(_gs.RefreshPoints == 0, "reset_run 清零刷新点数");
            Check(ActiveIds().Count == 3, "reset_run 任务槽位数复位");
            Check(
                _gs.Missions.ContainsKey(new StringName("kill_5"))
                    && _gs.Missions.ContainsKey(new StringName("survive_180"))
                    && _gs.Missions.ContainsKey(new StringName("boss_1")),
                "reset_run 回初始手牌"
            );

            // 8. TaskPool 算法单元测试：无放回（单批内不重复、跨批不连续重复）+ 排除项
            var pool = new TaskPool();
            pool.defs = new Godot.Collections.Array(_gs.MISSION_POOL.Select(d => (Variant)d));
            var batch1 = pool.Draw(9, new Godot.Collections.Array<StringName>());
            Check(batch1.Count == 9, "TaskPool：池满抽取 9 项全部返回");
            var b1Seen = new Godot.Collections.Dictionary();
            var b1Distinct = true;
            foreach (var def in batch1)
            {
                if (b1Seen.ContainsKey(def["id"]))
                {
                    b1Distinct = false;
                }
                b1Seen[def["id"]] = true;
            }
            Check(b1Distinct, "TaskPool：单批无放回（9 项互不重复）");
            var batch2 = pool.Draw(3, new Godot.Collections.Array<StringName>());
            Check(batch2.Count == 3, "TaskPool：耗尽后自动重洗续抽");
            var tinyPool = new TaskPool();
            tinyPool.defs = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["id"] = new StringName("a"), ["goal"] = 1, ["kind"] = new StringName("kill") },
                new Godot.Collections.Dictionary { ["id"] = new StringName("b"), ["goal"] = 1, ["kind"] = new StringName("kill") },
            };
            var excl = tinyPool.Draw(2, new Godot.Collections.Array<StringName> { new StringName("a") });
            Check(excl.Count == 1 && excl[0]["id"].AsStringName() == new StringName("b"), "TaskPool：排除项被跳过");
            var allExcl = tinyPool.Draw(2, new Godot.Collections.Array<StringName> { new StringName("a"), new StringName("b") });
            Check(allExcl.Count == 0, "TaskPool：排除覆盖全池时安全返回空（不死循环）");

            // 9. Q05（2026-08-05）：批次耗尽跨批补足——固定种子 20 轮刷新槽位恒 = MISSION_SLOTS
            // （原实现「本批已产出即 break」在排除在场任务时提前耗尽，模拟 14% 刷新不足额、99.3% 对局命中）
            GD.Seed(20260805);
            var poolQ05 = new TaskPool();
            poolQ05.defs = new Godot.Collections.Array(_gs.MISSION_POOL.Select(d => (Variant)d));
            var inField = new Godot.Collections.Array<StringName>();
            var q05AllFull = true;
            for (var i = 0; i < 20; i++)
            {
                var d = poolQ05.Draw(_gs.MISSION_SLOTS, inField);
                if (d.Count != _gs.MISSION_SLOTS)
                {
                    q05AllFull = false;
                }
                inField.Clear();
                foreach (var def in d)
                {
                    inField.Append(def["id"].AsStringName());
                }
            }
            Check(q05AllFull, $"Q05：20 轮刷新槽位恒 = {_gs.MISSION_SLOTS}（原实现不足额 1-2/3 槽）");
            GD.Seed(0);
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BASE TASK REFRESH TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BASE TASK REFRESH TEST DONE, failures = {_failures}");
            _gs?.DeleteSave();
            TestExit.Quit(_failures);
        }
    }
}
