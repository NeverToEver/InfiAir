using InfiAir.Core.Text;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// GdFormat（Y 系列收敛，2026-08-09）单测：语义基准 = 原 Hud 标准版（唯一支持 %.Nf 与
/// %f 6 位小数、唯一被 %.2f 调用方 UI_DIFF_FMT 使用的实现）；差异点 = 越界兜底统一 "?"
/// （原 Hud 版抛 IndexOutOfRange 为缺陷，行为收紧——其余 6 份主流实现均为 "?"）。
/// </summary>
public sealed class GdFormatTests
{
    [Fact]
    public void Format_StringSpec_AppendsToString()
    {
        Assert.Equal("score: 100", GdFormat.Format("score: %s", 100));
        Assert.Equal("name: alice", GdFormat.Format("name: %s", "alice"));
    }

    [Fact]
    public void Format_IntSpec_ConvertsToInt64()
    {
        Assert.Equal("hp 50", GdFormat.Format("hp %d", 50));
        Assert.Equal("hp 50", GdFormat.Format("hp %d", 50L));
        Assert.Equal("hp 51", GdFormat.Format("hp %d", 50.9)); // Convert.ToInt64 四舍五入（GDScript int() 近似）
    }

    [Fact]
    public void Format_FloatSpec_FixedSixDigitsInvariant()
    {
        Assert.Equal("v 1.500000", GdFormat.Format("v %f", 1.5));
        Assert.Equal("v 3.000000", GdFormat.Format("v %f", 3));
        Assert.Equal("v -0.250000", GdFormat.Format("v %f", -0.25));
    }

    [Fact]
    public void Format_PrecisionFloatSpec_HudSemantics()
    {
        // UI_DIFF_FMT（Hud.cs）是 %.2f 全库唯一调用方——语义基准
        Assert.Equal("x 3.14", GdFormat.Format("x %.2f", 3.14159));
        Assert.Equal("x 3", GdFormat.Format("x %.0f", 3.4)); // %.0f ≈ F0
        Assert.Equal("x 3.142", GdFormat.Format("x %.3f", 3.14159));
    }

    [Fact]
    public void Format_PercentEscape_AndUnknownSpec()
    {
        Assert.Equal("100%", GdFormat.Format("%d%%", 100));
        Assert.Equal("a%qb", GdFormat.Format("a%qb")); // 未知 spec 原样保留
        Assert.Equal("50% off", GdFormat.Format("%d%% off", 50));
    }

    [Fact]
    public void Format_MultipleArgs_InOrder()
    {
        Assert.Equal("kills 3, time 12.5", GdFormat.Format("kills %d, time %s", 3, "12.5"));
        Assert.Equal("1 2 3", GdFormat.Format("%d %d %d", 1, 2, 3));
    }

    [Fact]
    public void Format_OutOfRangeArgs_UnifiedQuestionMark()
    {
        // 行为收紧点：原 Hud 版越界抛 IndexOutOfRange；统一后与主流 6 份实现一致输出 "?"
        Assert.Equal("v ?", GdFormat.Format("v %s"));
        Assert.Equal("v ?", GdFormat.Format("v %d"));
        Assert.Equal("v ?", GdFormat.Format("v %f"));
        Assert.Equal("v ?", GdFormat.Format("v %.2f"));
    }

    [Fact]
    public void Format_NonNumericArgForIntSpec_ThrowsLikeOldImplementations()
    {
        // 非越界的类型错误（%d 收到非数字）与原实现一致抛 FormatException——语义保持，不吞
        Assert.Throws<FormatException>(() => GdFormat.Format("a %d b", "x"));
    }

    [Fact]
    public void Format_PrecisionOverflow_PreservesSpecLiterally()
    {
        // 2026-08-10 健壮性审查：%.超长精度f 的 int.Parse 溢出 / 超大精度巨额分配按
        // 未知 spec 原样保留，不抛异常
        var huge = "%." + new string('9', 20) + "f"; // 20 位数字溢出 int 域
        Assert.Equal("x " + huge, GdFormat.Format("x " + huge, 1.5));
        Assert.Equal("x %.100f", GdFormat.Format("x %.100f", 1.5)); // 合法 int 但超上限 99 同口径
    }

    [Fact]
    public void Format_NoSpecs_Passthrough()
    {
        Assert.Equal("plain text", GdFormat.Format("plain text"));
        Assert.Equal("", GdFormat.Format(""));
    }
}
