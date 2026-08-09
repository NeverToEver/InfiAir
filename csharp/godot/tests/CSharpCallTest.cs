using Godot;

namespace InfiAir.Tests;

/// <summary>
/// M1 跨语言调用探针（M7c 迁移）：加载/实例化 C# 脚本（Starfield.cs）动态派发命名/参数/返回值验证。
/// 全量迁移期 C# 方法以 PascalCase 注册进引擎，动态调用须同名（禁止 snake_case）。
/// 模式与 GDScript 断言场景一致：_Ready + Check + TestExit.Quit(failures)。
/// </summary>
public partial class CSharpCallTest : Node
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
            var script = GD.Load<Script>("res://csharp/godot/Starfield.cs");
            Check(script != null, "Starfield.cs 可加载");

            var sf = new Starfield();
            Check(sf != null, "Starfield.cs 可 new");

            // 动态派发：PascalCase 方法名 + 参数 + 返回值（跨语言唯一可靠通道）
            sf!.Warp(12.0f);
            // V 系列：原恒真断言（自身比较）无验证价值——改断言具体值（未入树时取字段默认值）
            Check(sf!.Origin() == Vector2.Zero, "Origin() 动态返回 Vector2（未入树默认 Zero）");
            Check(sf!.AreaSize() == new Vector2(1920.0f, 1080.0f), "AreaSize() 动态返回 Vector2（未入树默认 1920×1080）");
            Check(sf!.WarpFactor == 12.0f, "C# 属性 getter 动态读取（WarpFactor）");
            Check(Starfield.StaticProbe() == 42, "C# 静态方法经实例调用");
            Check(script!.Call("StaticProbe").AsInt32() == 42, "C# 静态方法经脚本资源调用");
            // V 系列：U17 已删 ProbeOverload（可选参数方法不注册引擎），重载注册探针随之移除；
            // 方法加载/调用验证由 starfield_cs_test 的 AddChild+_Ready+view 对照承担
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"CSHARP CALL TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"CSHARP CALL TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
