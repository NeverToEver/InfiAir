using Godot;

namespace InfiAir.Tests;

/// <summary>
/// P0-1 断言场景：SaveStoreInterop（C# 绑定壳）——save/load 往返、原子覆盖、
/// 损坏隔离（.corrupt + corrupt 标记）、缺失三态；并经 SaveManager 壳验证生产转发路径。
/// M7c：自 test/save_store_interop_test.gd 迁移（同步 Run，无等待逻辑）。
/// </summary>
public partial class SaveStoreInteropTest : Node
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
        Run();
    }

    private void Run()
    {
        try
        {
            // 0. C# 绑定壳可加载并实例化
            var script = GD.Load<Script>("res://csharp/godot/SaveStoreInterop.cs");
            Check(script != null, "SaveStoreInterop 脚本资源可加载");
            var interop = new SaveStoreInterop()!;
            var path = "user://save_store_interop_test.json";
            interop.Delete(path);
            interop.Delete(path + ".corrupt");
            interop.Delete(path + ".tmp");

            // 1. save/load 往返（含嵌套结构）
            Check(interop.Save(path, new Godot.Collections.Dictionary
            {
                ["version"] = 2,
                ["score"] = 500,
                ["nested"] = new Godot.Collections.Dictionary { ["k"] = "v" },
                ["arr"] = new Godot.Collections.Array { 1, 2.5, true, new Variant() },
            }), "save 成功");
            Check(interop.Exists(path), "save 后正本存在（非孤立 tmp）");
            Check(!Godot.FileAccess.FileExists(path + ".tmp"), "原子写不留 tmp");
            var loaded = interop.Load(path);
            var corrupt = loaded.GetValueOrDefault("corrupt", new Variant());
            Check(corrupt.VariantType == Variant.Type.Bool && !corrupt.AsBool(), "load 无损坏标记");
            var data = loaded.GetValueOrDefault("data", new Variant());
            Check(data.VariantType == Variant.Type.Dictionary
                && data.AsGodotDictionary().GetValueOrDefault("score", -1).AsInt32() == 500, "load 数据正确");
            var nested = data.AsGodotDictionary().GetValueOrDefault("nested", new Variant());
            Check(nested.VariantType == Variant.Type.Dictionary
                && nested.AsGodotDictionary()["k"].AsString() == "v", "嵌套结构往返");

            // 2. 覆盖写（原子替换不丢数据）
            Check(interop.Save(path, new Godot.Collections.Dictionary { ["score"] = 999 }), "覆盖 save 成功");
            loaded = interop.Load(path);
            Check(loaded.GetValueOrDefault("data", new Variant()).AsGodotDictionary()
                .GetValueOrDefault("score", -1).AsInt32() == 999, "覆盖后数据正确");
            Check(loaded.GetValueOrDefault("data", new Variant()).AsGodotDictionary()
                .GetValueOrDefault("version", new Variant()).VariantType == Variant.Type.Nil, "覆盖后旧键消失");

            // 3. 缺失文件 → 空数据不置损坏
            interop.Delete(path);
            loaded = interop.Load(path);
            corrupt = loaded.GetValueOrDefault("corrupt", new Variant());
            Check(corrupt.VariantType == Variant.Type.Bool && !corrupt.AsBool(), "缺失文件不置损坏");
            Check(loaded.GetValueOrDefault("data", new Variant()).VariantType == Variant.Type.Nil, "缺失文件返回空数据");

            // 4. 损坏隔离（损坏 JSON → corrupt 标记 + .corrupt 备份 + 正本移除）
            var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            f.StoreString("{broken json");
            f.Close();
            loaded = interop.Load(path);
            Check(loaded.GetValueOrDefault("corrupt", new Variant()).AsBool(), "损坏文件置 corrupt 标记");
            Check(loaded.GetValueOrDefault("data", new Variant()).VariantType == Variant.Type.Nil, "损坏后按无存档处理");
            Check(Godot.FileAccess.FileExists(path + ".corrupt"), "损坏文件隔离为 .corrupt");
            Check(!Godot.FileAccess.FileExists(path), "隔离后正本消失");

            // 5. 非对象根 JSON 亦视为损坏（对齐 json.data is Dictionary 判定）
            f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            f.StoreString("[1, 2, 3]");
            f.Close();
            loaded = interop.Load(path);
            Check(loaded.GetValueOrDefault("corrupt", new Variant()).AsBool(), "数组根 JSON 亦隔离");
            Check(Godot.FileAccess.FileExists(path + ".corrupt"), "数组根 JSON 隔离出 .corrupt");

            // 6. SaveManager 壳（生产转发路径：GameState/base_system_test 同款调用）
            var sm = new SaveManager()!;
            Check(sm.Save(path, new Godot.Collections.Dictionary { ["score"] = 123 }), "SaveManager 壳 save 成功");
            Check(sm.Exists(path), "SaveManager 壳 exists 命中");
            Check(sm.Load(path).GetValueOrDefault("score", -1).AsInt32() == 123, "SaveManager 壳 load 正确");
            Check(!sm.LastWasCorrupt, "SaveManager 壳正常读不置损坏");
            f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            f.StoreString("garbage!!");
            f.Close();
            Check(sm.Load(path).Count == 0 && sm.LastWasCorrupt, "SaveManager 壳损坏标记透传");
            Check(sm.SanitizeNum("x", 1.0) == 1.0 && sm.SanitizeNum(5, 1.0) == 5.0, "sanitize_num 语义不变");

            sm.Delete(path);
            sm.Delete(path + ".corrupt");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"SAVE STORE INTEROP TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"SAVE STORE INTEROP TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
