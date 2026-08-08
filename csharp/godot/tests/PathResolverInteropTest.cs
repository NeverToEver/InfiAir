using Godot;

namespace InfiAir.Tests;

/// <summary>
/// P1-1 断言场景：PathResolverInterop（C# 绑定壳）——经互操作层解析配置路径，
/// 验证 InfiAir.Core.Config.PathResolver 语义（数值宽容/容器拷贝/缺键回退）在引擎环境内可用；
/// 生产链路已接入（BalanceService.cfg 转发，本场景直测绑定壳 + 真实 balance.json 抽查）。
/// </summary>
public partial class PathResolverInteropTest : Node
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
            var gs = GetNode<GameState>("/root/GameState");

            // 1. C# 绑定壳可加载并实例化
            var script = GD.Load<Script>("res://csharp/godot/PathResolverInterop.cs");
            Check(script != null, "PathResolverInterop 脚本资源可加载");
            PathResolverInterop interop = new()!; // Godot 生成构造器在 NRT 下视为可空，断言非空后取值

            // 2. 合成树：嵌套/数值宽容/容器拷贝/类型判定
            interop.SetData(new Godot.Collections.Dictionary
            {
                ["player"] = new Godot.Collections.Dictionary
                {
                    ["max_speed"] = 420,
                    ["fuel"] = new Godot.Collections.Dictionary { ["drain"] = 35.0 },
                },
                ["boss"] = new Godot.Collections.Dictionary
                {
                    ["hp_mults"] = new Godot.Collections.Array { 1.3, 1.5, 1.8, 2.2 },
                },
                ["flag"] = true,
                ["label"] = "str",
            });
            Check(interop.Resolve("player.max_speed", 0).AsInt32() == 420, "嵌套整型路径解析");
            Check(interop.Resolve("player.fuel.drain", 0).AsInt32() == 35, "float 节点 + int 默认 → 截断");
            Check(interop.Resolve("player.max_speed", 0.0).AsDouble() == 420.0, "int 节点 + float 默认 → 拓宽");
            Check(interop.Resolve("player.missing", 7).AsInt32() == 7, "缺键回退默认");
            Check(interop.Resolve("flag", false).AsBool(), "bool 节点原样返回");
            Check(interop.Resolve("label", "d").AsString() == "str", "string 节点原样返回");
            Check(interop.Resolve("label", new StringName("d")).AsStringName() == new StringName("d"),
                "StringName 默认与 String 节点类型不符 → 回退");
            var arr = interop.Resolve("boss.hp_mults", new Godot.Collections.Array());
            Check(arr.VariantType == Variant.Type.Array && arr.AsGodotArray().Count == 4, "Array 默认返回容器");
            if (arr.VariantType == Variant.Type.Array)
            {
                arr.AsGodotArray().Clear();  // 修改返回值不应影响配置树
            }
            Check(interop.Resolve("boss.hp_mults", new Godot.Collections.Array()).AsGodotArray().Count == 4,
                "容器拷贝隔离（清空不影响）");

            // 3. 真实 data/balance.json 抽查（与 balance_test.gd 同源值）
            var raw = Json.ParseString(Godot.FileAccess.GetFileAsString(gs.BALANCE_PATH));
            Check(raw.VariantType != Variant.Type.Nil && raw.VariantType == Variant.Type.Dictionary, "balance.json 可解析");
            if (raw.VariantType == Variant.Type.Dictionary)
            {
                interop.SetData(raw.AsGodotDictionary());
                Check(interop.Resolve("version", 0).AsInt32() == 2, "真实文件 version = 2");
                Check(interop.Resolve("player.max_speed", 0).AsInt32() == 420, "真实文件 player.max_speed = 420");
                Check(interop.Resolve("mothership.mag_cells", 0).AsInt32() == 10, "真实文件 mothership.mag_cells = 10");
                Check(interop.Resolve("mothership.missile.damage", 0).AsInt32() == 80, "真实文件 母舰导弹 80");
            }

            // 4. 与生产转发路径一致（BalanceService.cfg 已切 C# 壳）
            Check(gs.Cfg("player.fuel.drain", 0.0).AsDouble() == 35.0, "生产链路 cfg 转发正常（float）");
            Check(gs.Cfg("mothership.mag_cells", 0).AsInt32() == 10, "生产链路 cfg 转发正常（int）");
            var diffCfg = gs.Cfg("difficulty", new Godot.Collections.Dictionary());
            Check(diffCfg.VariantType == Variant.Type.Dictionary, "生产链路 cfg 返回 Dictionary");
            if (diffCfg.VariantType == Variant.Type.Dictionary)
            {
                diffCfg.AsGodotDictionary().Clear();
            }
            Check(gs.EnemyHpMultiplier() == 1.0, "生产链路 cfg 容器拷贝隔离");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"PATH RESOLVER INTEROP TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"PATH RESOLVER INTEROP TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
