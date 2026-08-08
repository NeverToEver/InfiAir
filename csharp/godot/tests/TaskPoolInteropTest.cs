using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 2026-08-07 断言场景：TaskPoolInterop（C# 绑定壳）——经互操作层无放回
/// 抽取任务定义，验证 InfiAir.Core.Missions.TaskPool 语义在引擎环境内可用；
/// 生产链路已接入（scripts/task_pool.gd 转发，性质断言同 base_task_refresh_test 第 8–9 节）。
/// M7c：自 test/task_pool_interop_test.gd 迁移（同步 Run，无等待逻辑）。
/// </summary>
public partial class TaskPoolInteropTest : Node
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

    private bool AllDistinct(Godot.Collections.Array defs)
    {
        var seen = new Godot.Collections.Dictionary();
        foreach (var def in defs)
        {
            var id = def.AsGodotDictionary()["id"];
            if (seen.ContainsKey(id))
            {
                return false;
            }
            seen[id] = true;
        }
        return true;
    }

    public override void _Ready()
    {
        Run();
    }

    private void Run()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");

            // 1. C# 绑定壳可加载并实例化
            var script = GD.Load<Script>("res://csharp/godot/TaskPoolInterop.cs");
            Check(script != null, "TaskPoolInterop 脚本资源可加载");
            var interop = new TaskPoolInterop()!;
            interop.SetDefs(new Godot.Collections.Array(gs.MISSION_POOL.Select(d => (Variant)d)));

            // 2. 池满抽取：9 项全部返回且互不重复
            var batch1 = interop.Draw(9, new Godot.Collections.Array());
            Check(batch1.Count == 9, "池满抽取 9 项全部返回");
            Check(AllDistinct(batch1), "单批无放回（9 项互不重复）");

            // 3. 耗尽后自动重洗续抽
            var batch2 = interop.Draw(3, new Godot.Collections.Array());
            Check(batch2.Count == 3, "耗尽后自动重洗续抽");

            // 4. 排除项跳过 / 全池排除安全空
            var tiny = new TaskPoolInterop()!;
            tiny.SetDefs(new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["id"] = new StringName("a"), ["goal"] = 1, ["kind"] = new StringName("kill") },
                new Godot.Collections.Dictionary { ["id"] = new StringName("b"), ["goal"] = 1, ["kind"] = new StringName("kill") },
            });
            var excl = tiny.Draw(2, new Godot.Collections.Array { new StringName("a") });
            Check(excl.Count == 1 && excl[0].AsGodotDictionary()["id"].AsStringName() == new StringName("b"), "排除项被跳过");
            Check(tiny.Draw(2, new Godot.Collections.Array { new StringName("a"), new StringName("b") }).Count == 0,
                "排除覆盖全池时安全返回空");

            // 5. Q05：批次耗尽跨批补足——20 轮刷新槽位恒 = MISSION_SLOTS（与序列无关的性质断言）
            var interopQ05 = new TaskPoolInterop()!;
            interopQ05.SetDefs(new Godot.Collections.Array(gs.MISSION_POOL.Select(d => (Variant)d)));
            var inField = new Godot.Collections.Array();
            var allFull = true;
            for (var i = 0; i < 20; i++)
            {
                var d = interopQ05.Draw(gs.MISSION_SLOTS, inField);
                if (d.Count != gs.MISSION_SLOTS)
                {
                    allFull = false;
                }
                inField = new Godot.Collections.Array();
                foreach (var def in d)
                {
                    inField.Add(def.AsGodotDictionary()["id"]);
                }
            }
            Check(allFull, $"Q05：20 轮刷新槽位恒 = {gs.MISSION_SLOTS}");

            // 6. 返回原始任务定义引用（id 身份映射：调用方读 def["id"]/["goal"] 不受影响）
            var refDef = gs.MISSION_POOL[1];
            var drawn = interop.Draw(9, new Godot.Collections.Array());
            var foundIdentity = false;
            // Godot 4.6.2 C# 无 is_same 等价公开 API（Variant 对 Dictionary 为内容比较）：
            // 以共享底层数据的可观测副作用等价断言——写入抽取到的定义，若为池内原对象
            // 则 MISSION_POOL 侧同步可见；断言后立即还原，防污染后续小节与任务池。
            var probeKey = new StringName("_identity_probe");
            foreach (var def in drawn)
            {
                var defDict = def.AsGodotDictionary();
                if (defDict["id"].AsStringName() == refDef["id"].AsStringName())
                {
                    defDict[probeKey] = true;
                    foundIdentity = refDef.ContainsKey(probeKey);
                    refDef.Remove(probeKey);
                    break;
                }
            }
            Check(foundIdentity, "返回池内原始定义对象（is_same 引用同一）");

            // 7. 生产链路转发一致（task_pool.gd 已切 C# 壳；GameState.MISSION_POOL 走真实数据）
            var pool = new TaskPool();
            pool.defs = new Godot.Collections.Array(gs.MISSION_POOL.Select(d => (Variant)d));
            var prod = pool.Draw(9);
            Check(prod.Count == 9 && AllDistinct(new Godot.Collections.Array(prod.Select(d => (Variant)d))),
                "生产链路 TaskPool.draw(9) 全量无重复");
            var prodExcl = pool.Draw(2, new Godot.Collections.Array<StringName>
            {
                new StringName("kill_5"), new StringName("kill_15"), new StringName("kill_30"),
                new StringName("survive_60"), new StringName("survive_180"), new StringName("survive_300"),
                new StringName("boss_1"),
            });
            Check(prodExcl.Count == 2, "生产链路排除 7 项后可用 2 项");
            var allExcl = pool.Draw(3, new Godot.Collections.Array<StringName>
            {
                new StringName("kill_5"), new StringName("kill_15"), new StringName("kill_30"),
                new StringName("survive_60"), new StringName("survive_180"), new StringName("survive_300"),
                new StringName("boss_1"), new StringName("boss_2"), new StringName("boss_3"),
            });
            Check(allExcl.Count == 0, "生产链路全池排除返回空（不死循环）");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"TASK POOL INTEROP TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"TASK POOL INTEROP TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
