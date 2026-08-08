using System;
using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// M1 样板断言场景（C# 脚本化）：Starfield.cs 迁移验证——
/// 加载/实例化、_Ready 配置（view_world_rect 区域锚点+尺寸）、Warp 拉伸与衰减。
/// 模式与 GDScript 断言场景一致：_Ready 内断言 + GetTree().Quit(failures)；
/// 入口 try/catch 保证异常也走非零退出（#90753 教训）。
/// </summary>
public partial class StarfieldCsTest : Node
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
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget（约定 §Async）
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var script = GD.Load<Script>("res://csharp/godot/Starfield.cs");
            Check(script != null, "Starfield.cs 可加载");

            // C# 侧实例化直接用类（脚本资源路径加载由 test/csharp_call_test.gd 探针验证）
            var starfield = new Starfield()!; // Godot 生成构造器在 NRT 下视为可空，测试侧断言非空后取值
            Check(starfield != null, "Starfield 可 new");
            AddChild(starfield);
            await Coroutine.WaitSeconds(this, 0.05); // 等一帧让 _Ready 执行

            var gs = GetNode("/root/GameState");
            var view = gs.Call("view_world_rect").AsRect2();
            Check(starfield!.Origin() == view.Position, "Origin == view_world_rect.position");
            Check(starfield.AreaSize() == view.Size, "AreaSize == view_world_rect.size");
            Check(starfield.AreaSize().X > 0.0f && starfield.AreaSize().Y > 0.0f, "可见区域非零");

            starfield.Warp(5.0f);
            Check(Mathf.IsEqualApprox(starfield.WarpFactor, 5.0f), "Warp(5) 设置拉伸倍率");
            await Coroutine.WaitSeconds(this, 0.25);
            Check(starfield.WarpFactor < 5.0f && starfield.WarpFactor > 1.0f,
                $"拉伸倍率向 1 衰减（当前 {starfield.WarpFactor:0.000}）");
        }
        catch (Exception e)
        {
            _failures++;
            GD.PushError($"STARFIELD CS TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"STARFIELD CS TEST DONE, failures = {_failures}");
            GetTree().Quit(_failures);
        }
    }
}
