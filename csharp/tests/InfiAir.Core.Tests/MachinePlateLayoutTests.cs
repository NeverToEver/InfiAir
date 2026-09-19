using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// 机型轮盘铭牌版式契约：判据是**关系**（板顶不压机体、板底不压 note 与提示行、图表盒 + 文本区
/// 装得进板内、文本区装得下最长一行并留余量、板心对齐机体停驻位），不是另一份硬编码期望值——
/// 改任一尺寸常量，判据跟着动。
/// 版式错（铭牌与机体错开、板压住机尾或压住底部两行、图盒挤出板外）在无头冒烟里既不崩也不报错，
/// 故判据落在这里（与 <see cref="HudLayoutTests"/> 同款理由）。
/// </summary>
public sealed class MachinePlateLayoutTests
{
    /// <summary>板顶不得压到机体：板往上长到**机体实心像素的下缘 + 间距**为止，再高就切到机尾。
    /// 口径是实心像素（alpha ≥ 200）而不是贴图画布下缘——画布最下面十几行只剩尾焰余辉
    /// （实测最大 alpha 18），板压住它读不出来；而机体本身被切掉一眼就看得见。</summary>
    [Fact]
    public void PlateTopClearsTheMachineHull()
    {
        var clearance = MachinePlateLayout.PlateTop - MachinePlateLayout.ShipHullBottomY;
        Assert.True(
            clearance >= MachinePlateLayout.ShipGap,
            $"板顶 {MachinePlateLayout.PlateTop} 距机体实心下缘 {MachinePlateLayout.ShipHullBottomY} 只有 {clearance}，"
                + $"低于要求的 {MachinePlateLayout.ShipGap}——机体上移或板加高都会重新压上来");

        // 实心像素的下缘必须真的落在贴图画布之内（量错行就会得到一条假绿：机体其实更长）
        Assert.InRange(MachinePlateLayout.ShipHullBottomPx, 0.0, MachinePlateLayout.ShipSpritePx);
        Assert.True(
            MachinePlateLayout.ShipHullBottomY < MachinePlateLayout.ShipRestY + MachinePlateLayout.ShipSpritePx * MachinePlateLayout.ShipDisplayScale / 2.0,
            "实心像素下缘不该到贴图画布之外");
    }

    /// <summary>机体（含机首）不得压到页题：页题在最上面，机体上移是给铭牌让空间，但让到题字上就是
    /// 两行字压在一起——同样不报错。</summary>
    [Fact]
    public void MachineHullClearsThePageTitle()
    {
        var titleBottom = MachinePlateLayout.TitleY + MachinePlateLayout.TitleRowPx;
        var clearance = MachinePlateLayout.ShipHullTopY - titleBottom;
        Assert.True(
            clearance >= MachinePlateLayout.ShipGap,
            $"机体实心上缘 {MachinePlateLayout.ShipHullTopY} 距页题下缘 {titleBottom} 只有 {clearance}，"
                + $"低于要求的 {MachinePlateLayout.ShipGap}——机首会横穿题字");
    }

    /// <summary>板底之下那两行（note / 操作提示）不得被板压住，提示行也不得出屏。
    /// 这两行是玩家读到「选定即保存」与操作方式的唯一地方，被压住不报错、只是读不到。</summary>
    [Fact]
    public void PlateBottomClearsTheNoteAndHintLines()
    {
        Assert.True(
            MachinePlateLayout.NoteY >= MachinePlateLayout.PlateBottom + MachinePlateLayout.NoteGap,
            $"note {MachinePlateLayout.NoteY} 距板底 {MachinePlateLayout.PlateBottom} 不足 {MachinePlateLayout.NoteGap}");

        var noteBottom = MachinePlateLayout.NoteY + MachinePlateLayout.NoteRowPx * MachinePlateLayout.NoteMaxLines;
        Assert.True(
            MachinePlateLayout.HintY >= noteBottom + MachinePlateLayout.NoteGap,
            $"提示行 {MachinePlateLayout.HintY} 距 note 下缘 {noteBottom} 不足 {MachinePlateLayout.NoteGap}");

        var hintBottom = MachinePlateLayout.HintY + MachinePlateLayout.HintRowPx;
        Assert.True(
            hintBottom + MachinePlateLayout.MinBottomMargin <= MachinePlateLayout.ScreenHeight,
            $"提示行下缘 {hintBottom} 距屏幕下缘只剩 {MachinePlateLayout.ScreenHeight - hintBottom}，"
                + $"低于要求的 {MachinePlateLayout.MinBottomMargin}（板加高会把它一路顶下去）");
    }

    /// <summary>板内两件东西都得装得进内容框：文本区（三行）+ 指纹图盒（正方形，含轴标签留白）。
    /// 装不下时引擎侧不会报错——vbox 溢出板外，图与文案被切一半。</summary>
    [Fact]
    public void ChartBoxAndTextBoxFitInsideThePlate()
    {
        Assert.True(
            MachinePlateLayout.TextBoxHeight + MachinePlateLayout.ChartBox <= MachinePlateLayout.ContentHeight,
            $"文本区 {MachinePlateLayout.TextBoxHeight} + 图盒 {MachinePlateLayout.ChartBox} "
                + $"超出内容框 {MachinePlateLayout.ContentHeight}");

        Assert.True(
            MachinePlateLayout.ChartBox <= MachinePlateLayout.ContentWidth,
            $"图盒 {MachinePlateLayout.ChartBox} 宽过内容框 {MachinePlateLayout.ContentWidth}");

        // 半径必须是正数：图盒被两侧标签留白吃光时，图上什么都不剩（零点几个像素的六边形）
        Assert.True(
            MachinePlateLayout.ChartRadius > 24.0,
            $"指纹图半径 {MachinePlateLayout.ChartRadius} 过小——六边形与六个轴名会挤成一团");

        // 标签留白要装得下「字高 + 间隙」：留白不足时标签会画到板外（被切掉一半的字不报错）
        Assert.True(
            MachinePlateLayout.ChartLabelPad >= MachinePlateLayout.NoteRowPx,
            $"轴标签留白 {MachinePlateLayout.ChartLabelPad} 装不下一行标签（{MachinePlateLayout.NoteRowPx}）");
    }

    [Fact]
    public void ContentBoxFitsTheLongestRowWithMargin()
    {
        var margin = MachinePlateLayout.ContentWidth - MachinePlateLayout.LongestRowPx;
        Assert.True(
            margin >= MachinePlateLayout.MinRowMarginPx,
            $"内容框 {MachinePlateLayout.ContentWidth} 只比最长一行 {MachinePlateLayout.LongestRowPx} 宽 {margin}，"
                + $"低于要求的 {MachinePlateLayout.MinRowMarginPx}——再补一个字或字体度量微差就会折行");
    }

    /// <summary>板下的 note 必须装得下一行（<see cref="MachinePlateLayout.NoteMaxLines"/> ＝ 1）：
    /// 折成两行就会顶到提示行，而顶上去同样不报错。</summary>
    [Fact]
    public void NoteFitsOnOneLine()
    {
        var margin = MachinePlateLayout.NoteWidthPx - MachinePlateLayout.LongestNotePx;
        Assert.True(
            margin >= MachinePlateLayout.MinRowMarginPx,
            $"note 宽度 {MachinePlateLayout.NoteWidthPx} 只比最长一行 {MachinePlateLayout.LongestNotePx} 宽 {margin}，"
                + $"低于要求的 {MachinePlateLayout.MinRowMarginPx}——折行会顶到提示行");
        Assert.Equal(1, MachinePlateLayout.NoteMaxLines);
    }

    [Fact]
    public void PlateCenterStaysOnTheShipAnchor()
    {
        Assert.Equal(
            MachinePlateLayout.ShipCenterX,
            MachinePlateLayout.PlateLeft + MachinePlateLayout.PlateWidth / 2.0);
    }

    [Fact]
    public void PlateStaysInsideTheRightHalfOfTheScreen()
    {
        // 右区铭牌不得压到左缘轮盘（轮盘圆心在屏幕左侧之外，弧面最远伸到约 x=300）
        Assert.True(MachinePlateLayout.PlateLeft > 300.0, $"铭牌左缘 {MachinePlateLayout.PlateLeft} 已进入轮盘区");
        Assert.True(MachinePlateLayout.PlateLeft + MachinePlateLayout.PlateWidth < 1920.0, "铭牌右缘越出屏幕");

        // note 比板宽出一截（居中在板心），它同样不得伸进轮盘区或出屏
        var noteLeft = MachinePlateLayout.ShipCenterX - MachinePlateLayout.NoteWidthPx / 2.0;
        Assert.True(noteLeft > 300.0, $"note 左缘 {noteLeft} 已进入轮盘区");
        Assert.True(noteLeft + MachinePlateLayout.NoteWidthPx < 1920.0, "note 右缘越出屏幕");
    }
}
