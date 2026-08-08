using Godot;

namespace InfiAir.Tests;

/// <summary>
/// C# 互操作断言场景（M7c 迁移）：加载 C# 绑定类（BalanceInterop）→ 调用其解析
/// → 验证脚本资源加载路径 + InfiAir.Core 纯逻辑在引擎环境内可用。
/// 生产运行时链路零改动（GameState.cfg() 不受影响）。
/// </summary>
public partial class CSharpInteropTest : Node
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

            // 1. C# 类可加载（.NET 版引擎 + 主程序集内类型）
            var cls = GD.Load("res://csharp/godot/BalanceInterop.cs");
            Check(cls != null, "C# 脚本资源可加载");
            Check(cls is Script, "C# 脚本资源类型为 Script");

            // 2. 实例化并跨语言调用
            var interop = new BalanceInterop();
            var res = interop.ParseBalance(gs.BALANCE_PATH);

            // 3. 解析真实 data/balance.json 的抽查值（与 balance_test.gd 同源）
            Check(res.GetValueOrDefault("ok", false).AsBool(), "balance.json 解析成功");
            Check(res.GetValueOrDefault("error", "?").AsString() == "", "无解析错误");
            Check(res.GetValueOrDefault("version", -1).AsInt32() == 2, "version = 2");
            Check(res.GetValueOrDefault("max_speed", -1).AsInt32() == 420, "player.max_speed = 420");
            Check(res.GetValueOrDefault("mag_cells", -1).AsInt32() == 10, "mothership.mag_cells = 10");

            // 4. 损坏输入 → ok=false 且带错误信息（对齐"损坏回退"语义）
            var broken = interop.ParseBalance("res://test/does_not_exist.json");
            Check(!broken.GetValueOrDefault("ok", true).AsBool(), "缺失文件 → ok=false");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"CSHARP_INTEROP TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"CSHARP_INTEROP TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
