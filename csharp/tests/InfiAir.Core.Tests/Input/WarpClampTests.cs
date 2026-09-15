using InfiAir.Core.Input;
using Xunit;

namespace InfiAir.Core.Tests.Input;

/// <summary>鼠标 confine 的 warp 目标钳制契约：输入与输出都是**窗口相对坐标**。
/// Godot 4 的 Input.warp_mouse 以「当前聚焦窗口左上角」为原点（窗口相对），
/// 窗口的屏幕位置不属于本函数的坐标语义——把它叠进来会让光标被弹开窗口偏移量。</summary>
public sealed class WarpClampTests
{
    [Fact]
    public void Target_KeepsResultInsideWindowLocalRange()
    {
        // 两种窗口尺寸各自打满越界输入：输出必须落在 [1, size−1]（窗口相对坐标系内）
        foreach (var (w, h) in new[] { (1280.0f, 720.0f), (3840.0f, 2160.0f) })
        {
            var (x, y) = WarpClamp.Target(1.0e6f, -1.0e6f, w, h);
            Assert.InRange(x, WarpClamp.EdgeInset, w - WarpClamp.EdgeInset);
            Assert.InRange(y, WarpClamp.EdgeInset, h - WarpClamp.EdgeInset);
        }
    }

    [Fact]
    public void Target_HugeWindowAndOriginWindowShareTheSameSemantics()
    {
        // 窗口尺寸只决定钳制区间，不改变「窗口相对」这一坐标语义：同一窗口内位置在两种尺寸下
        // 都原样通过（既不乘也不加窗口的屏幕位置/尺寸）
        var small = WarpClamp.Target(300.0f, 200.0f, 1280.0f, 720.0f);
        var large = WarpClamp.Target(300.0f, 200.0f, 3840.0f, 2160.0f);
        Assert.Equal((300.0f, 200.0f), small);
        Assert.Equal(small, large);
    }

    [Fact]
    public void Target_ClampsToEdgeInset()
    {
        // 贴边即被钳到内侧 1px：贴 (0,0) 或 (size,size) 会被系统判为仍在窗外，形成 exited/warp 循环
        Assert.Equal((1.0f, 1.0f), WarpClamp.Target(0.0f, 0.0f, 1920.0f, 1080.0f));
        Assert.Equal((1919.0f, 1079.0f), WarpClamp.Target(1920.0f, 1080.0f, 1920.0f, 1080.0f));
    }

    [Fact]
    public void Target_DegenerateWindow_StaysAtInset()
    {
        // 尺寸 <2px 时不存在内侧区间：钉在 1，不得返回负值或越界值
        Assert.Equal((1.0f, 1.0f), WarpClamp.Target(0.0f, 0.0f, 1.0f, 1.0f));
        Assert.Equal((1.0f, 1.0f), WarpClamp.Target(0.0f, 0.0f, 0.5f, 0.0f));
    }

    [Fact]
    public void Target_NonFiniteInput_FallsBackToInset()
    {
        // NaN 会原样穿出 Math.Clamp 并被 warp 到未定义位置，必须回退边缘内侧
        Assert.Equal((1.0f, 1.0f), WarpClamp.Target(float.NaN, float.PositiveInfinity, 1280.0f, 720.0f));
    }
}
