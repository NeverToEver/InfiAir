using Godot;

namespace InfiAir;

/// <summary>
/// 测试退出辅助（M5 定位，2026-08-08）：Godot C# 快速测试在 quit() 后 .NET 运行时 shutdown
/// 时，未回收的 C# Godot 对象（RefCounted/Node 包装）被 finalize，触碰已关闭的 Godot native
/// → 退出 segfault（实测：main.tscn 2 帧退出 8/8 崩，延迟 1s 0/10 崩）。
/// 在引擎仍存活时显式 GC.Collect + 等待 finalizer，随后再 quit，退出时无残留 finalize。
/// 测试统一以 load("res://csharp/godot/TestExit.cs").Quit(_failures) 替代 get_tree().quit()。
/// </summary>
public partial class TestExit : RefCounted
{
    public static void Quit(int failures)
    {
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        var tree = (SceneTree?)Engine.GetMainLoop();
        tree?.Quit(failures);
    }
}
