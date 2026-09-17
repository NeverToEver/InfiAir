using InfiAir.Core.Input;
using Xunit;

namespace InfiAir.Core.Tests.Input;

/// <summary>StickShaper 契约测试：径向死区/重标定/指数曲线。移动与瞄准共用本入口，
/// 输出 NaN 会顺着 Velocity 灌进机体位置且 Mathf.Clamp 拦不住 NaN。</summary>
public sealed class StickShaperTests
{
    [Fact]
    public void Shape_ZeroInput_IsZeroEvenWithoutDeadzone()
    {
        // 底线用例：dz=0 时 0 < 0 不成立，无零向量拦截会走 0/0 归一化得 NaN
        var (x, y) = StickShaper.Shape(0.0f, 0.0f, 0.0f, 1.0f);
        Assert.Equal(0.0f, x);
        Assert.Equal(0.0f, y);
    }

    [Fact]
    public void Shape_InsideDeadzone_IsZero()
    {
        var (x, y) = StickShaper.Shape(0.1f, 0.0f, 0.2f, 1.0f);
        Assert.Equal(0.0f, x);
        Assert.Equal(0.0f, y);
    }

    [Fact]
    public void Shape_JustAboveDeadzone_StartsNearZero()
    {
        // 出死区从 0 连续爬升（重标定语义）——不允许跨死区瞬间速度跳变
        var (x, _) = StickShaper.Shape(0.21f, 0.0f, 0.2f, 1.0f);
        Assert.InRange(x, 0.0f, 0.05f);
    }

    [Fact]
    public void Shape_FullPush_ClampsToUnitLength()
    {
        // 物理推不满的手柄：长度 ≥ OuterDeadzone 即视为满行程
        var (x, y) = StickShaper.Shape(0.99f, 0.0f, 0.2f, 1.0f);
        Assert.Equal(1.0f, x, 3);
        Assert.Equal(0.0f, y, 3);
    }

    [Fact]
    public void Shape_PreservesDirection()
    {
        var (x, y) = StickShaper.Shape(3.0f, 4.0f, 0.2f, 1.0f);
        var len = MathF.Sqrt((x * x) + (y * y));
        Assert.Equal(1.0f, len, 3);
        Assert.Equal(0.6f, x / len, 3);
        Assert.Equal(0.8f, y / len, 3);
    }

    [Fact]
    public void Shape_ExpoCurve_BendsWithoutChangingFullTravel()
    {
        // expo>1 轻推更细、推满仍到满速；输出长度必须随行程单调不减
        var small = StickShaper.Shape(0.5f, 0.0f, 0.2f, 2.2f);
        var large = StickShaper.Shape(0.9f, 0.0f, 0.2f, 2.2f);
        var full = StickShaper.Shape(1.0f, 0.0f, 0.2f, 2.2f);

        var smallLen = MathF.Sqrt((small.X * small.X) + (small.Y * small.Y));
        var largeLen = MathF.Sqrt((large.X * large.X) + (large.Y * large.Y));
        var fullLen = MathF.Sqrt((full.X * full.X) + (full.Y * full.Y));

        Assert.True(smallLen < largeLen);
        Assert.Equal(1.0f, fullLen, 3);
    }

    [Fact]
    public void Shape_NegativeAxis_Works()
    {
        var (x, _) = StickShaper.Shape(-1.0f, 0.0f, 0.2f, 1.0f);
        Assert.Equal(-1.0f, x, 3);
    }

    [Fact]
    public void Shape_NonFiniteInput_IsZero()
    {
        // NaN/±∞ 会让归一化 x/len 得 NaN（∞/∞ 或 NaN/NaN），输出直接灌进机体位置。
        Assert.Equal((0.0f, 0.0f), StickShaper.Shape(float.NaN, 0.0f, 0.2f, 1.0f));
        Assert.Equal((0.0f, 0.0f), StickShaper.Shape(0.0f, float.NaN, 0.2f, 1.0f));
        Assert.Equal((0.0f, 0.0f), StickShaper.Shape(float.PositiveInfinity, 0.0f, 0.2f, 1.0f));
        Assert.Equal((0.0f, 0.0f), StickShaper.Shape(float.NegativeInfinity, 0.0f, 0.2f, 1.0f));
    }

    [Fact]
    public void Shape_NonFiniteTuning_FallsBackToSafeValues()
    {
        // 死区/指数来自设置（手改 settings.json 可写坏）：NaN 死区与 NaN 指数都会让输出为 NaN，
        // 须各自回退安全值（死区 0 = 不吞输入，指数 1 = 线性）。
        var (x, y) = StickShaper.Shape(0.5f, 0.0f, float.NaN, float.NaN);
        Assert.True(float.IsFinite(x) && float.IsFinite(y), $"非有限输出：({x}, {y})");
        Assert.Equal(0.5f / 0.98f, x, 3);
        Assert.Equal(0.0f, y);
    }

    [Fact]
    public void Shape_DeadzoneAtClampBoundary_DoesNotDivideByZero()
    {
        // 死区钳制的真正边界值：dz 触到 Outer−0.01 时 (Outer − dz) = 0.01 仍为正，
        // 满推行程必须到 1.0；钳制写成 Outer 会让分母为 0 → t=inf/NaN 灌进机体位置。
        var atBoundary = StickShaper.Shape(StickShaper.OuterDeadzone, 0.0f, StickShaper.OuterDeadzone - 0.01f, 1.0f);

        // 再大一点的死区被钳回边界值，行为与边界一致（不是「吞掉全部输入」）
        var aboveBoundary = StickShaper.Shape(StickShaper.OuterDeadzone, 0.0f, StickShaper.OuterDeadzone, 1.0f);

        Assert.True(float.IsFinite(atBoundary.X) && float.IsFinite(atBoundary.Y));
        Assert.Equal(1.0f, atBoundary.X, 3);
        Assert.Equal(1.0f, aboveBoundary.X, 3);

        // 恰好等于有效死区（0.97）的长度：dz 生效、t=0，不得放行成输出
        Assert.Equal((0.0f, 0.0f), StickShaper.Shape(0.97f, 0.0f, 0.97f, 1.0f));
    }
}
