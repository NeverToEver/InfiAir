using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>机型轮盘铭牌版式契约：判据是**关系**（内容框装得下最长一行并留余量、板心对齐机体停驻位），
/// 不是另一份硬编码期望值——改任一尺寸常量，判据跟着动。
/// 版式错（英文行折成两行把四行挤成五行、铭牌与机体左右错开）在无头冒烟里既不崩也不报错，
/// 故判据落在这里（与 <see cref="HudLayoutTests"/> 同款理由）。</summary>
public sealed class MachinePlateLayoutTests
{
    [Fact]
    public void ContentBoxFitsTheLongestRowWithMargin()
    {
        var margin = MachinePlateLayout.ContentWidth - MachinePlateLayout.LongestRowPx;
        Assert.True(
            margin >= MachinePlateLayout.MinRowMarginPx,
            $"内容框 {MachinePlateLayout.ContentWidth} 只比最长一行 {MachinePlateLayout.LongestRowPx} 宽 {margin}，"
                + $"低于要求的 {MachinePlateLayout.MinRowMarginPx}——再补一个字或字体度量微差就会折行");
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
    }
}
