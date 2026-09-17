using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>轮盘调色板口径（读源码文本的结构性判定）：暗钢/暗面/槽底一律引用 <c>UITheme</c> token，
/// 不得再写裸色字面量。原实现漏了三处冷蓝裸值（面包屑环 / 内域暗面 / 图标槽底），
/// 与 DESIGN_BASELINE §2.1「数据青通道退役、全站收归暖族」冲突——冷青与琥珀主色互斥，
/// 而这类偏差在截图探针（只判非空白 + 页间互异）里读不出来，只能在此判形态。
/// 白/黑等中性裸值与 token 引用不参与判定。</summary>
public sealed class RadialWheelPaletteTests
{
    private static readonly Regex RawColor =
        new(@"new Color\(\s*([0-9.]+)f\s*,\s*([0-9.]+)f\s*,\s*([0-9.]+)f");

    [Fact]
    public void ColdBlueRawColorLiterals_AreGone()
    {
        var src = RepoFiles.Read("csharp/godot/RadialWheel.cs");
        foreach (Match m in RawColor.Matches(src))
        {
            var r = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var b = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            Assert.True(
                b <= r,
                $"第 {LineOf(src, m.Index)} 行出现冷色裸字面量（蓝通道 > 红通道）：{m.Value}——"
                + "冷青已退役，颜色须引用 UITheme token（或说明刻意冷色的理由）");
        }
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
