using InfiAir.Core.Text;
using Xunit;

namespace InfiAir.Core.Tests.Text;

/// <summary>GdFormat 契约测试：全部格式符与兜底分支。
/// 玩家可见文案直接吃它的输出——键名写错门禁能抓，但 % 序列渲染坏
/// （精度守卫误判、越界抛异常、%% 吃掉参数）没有任何门禁跑得到。</summary>
public sealed class GdFormatTests
{
    [Fact]
    public void Format_PercentS_InsertsToString()
    {
        Assert.Equal("编队机 3/7", GdFormat.Format("编队机 %s/%s", 3, 7));
    }

    [Fact]
    public void Format_PercentD_RoundsToNearestEven()
    {
        // Convert.ToInt64 语义（对 GDScript int() 的近似）：四舍六入五取偶，非向零截断
        Assert.Equal("x=-5", GdFormat.Format("x=%d", -4.9));
    }

    [Fact]
    public void Format_PercentD_OverflowFallsBackToQuestionMark()
    {
        // ±Infinity / 超 long 域 double：Convert.ToInt64 抛 OverflowException，
        // 配置驱动的参数可达——必须兜成 "?" 而非整帧炸掉
        Assert.Equal("? ?", GdFormat.Format("%d %d", double.PositiveInfinity, 1e30));
    }

    [Fact]
    public void Format_PercentD_NonNumericThrows()
    {
        // 类型错误按既定契约照抛不吞，对齐 GDScript 报错语义
        Assert.Throws<FormatException>(() => GdFormat.Format("%d", "abc"));
    }

    [Fact]
    public void Format_PercentF_FixedSixDecimals()
    {
        Assert.Equal("1.500000", GdFormat.Format("%f", 1.5));
    }

    [Fact]
    public void Format_PrecisionF_RoundsToNDigits()
    {
        Assert.Equal("难度 x1.50 · 中", GdFormat.Format("难度 x%.2f · 中", 1.5));
    }

    [Fact]
    public void Format_PrecisionF_EmptyPrecisionIsZero()
    {
        Assert.Equal("2", GdFormat.Format("%.f", 1.6));
    }

    [Fact]
    public void Format_PrecisionF_ExcessivePrecisionPreservedVerbatim()
    {
        // 精度上限 99 守卫：非法精度按未知 spec 原样保留，不得 Parse 抛异常或巨额分配
        Assert.Equal("%.100f", GdFormat.Format("%.100f", 1.5));
    }

    [Fact]
    public void Format_PercentPercent_LiteralAndConsumesNoArg()
    {
        Assert.Equal("100% 命中 5", GdFormat.Format("100%% 命中 %d", 5L));
    }

    [Fact]
    public void Format_UnknownSpec_PreservedVerbatimAndConsumesNoArg()
    {
        Assert.Equal("%x 5", GdFormat.Format("%x %d", 5L));
    }

    [Fact]
    public void Format_MissingArgs_FallbackQuestionMarkNoThrow()
    {
        Assert.Equal("? ? ? ?", GdFormat.Format("%s %d %f %.2f"));
    }

    [Fact]
    public void Format_TrailingPercent_Literal()
    {
        Assert.Equal("end%", GdFormat.Format("end%"));
    }

    [Fact]
    public void Format_PercentDAfterPrecisionDot_StillWorks()
    {
        // 位数扫描从 '.' 之后起——锚 UI_DIFF_FMT 实际用法的回归位
        Assert.Equal("倍率 2 档", GdFormat.Format("倍率 %.0f 档", 2.4));
    }
}
