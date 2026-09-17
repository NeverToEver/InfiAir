using System;
using System.Text.RegularExpressions;
using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>HUD 版式契约：各板块的盒由 core 的常量推出，判据是盒之间的关系（良构 / 相交 / 包含 /
/// 顺序），不是另一份硬编码期望值——改任一尺寸常量，判据跟着动。原实现把落位写成 <c>Hud</c> 里的
/// 裸字面量，改一处（面板宽、槽位间距、行高）不会有任何自动判定发红：这类布局错在无头冒烟里既不崩
/// 也不报错，截图探针又只兜渲染整体坏掉，故判据落在这里。</summary>
public sealed class HudLayoutTests
{
    [Fact]
    public void BossCountdown_DoesNotOverlapPlate()
    {
        Assert.False(HudLayout.BoxesOverlap(
            HudLayout.BossCountdownTop,
            HudLayout.BossCountdownLineHeight,
            HudLayout.BossPlateTop,
            HudLayout.BossPlateHeight));
        // 顶位必须在背板下缘之外（相切也不许：读数贴框读成背板的一部分）
        Assert.True(
            HudLayout.BossCountdownTop >= HudLayout.BossPlateBottom,
            $"倒计时顶 {HudLayout.BossCountdownTop} 未越出背板下缘 {HudLayout.BossPlateBottom}");
        // 盒层面同一条：倒计时盒与背板、血条带都不相交
        Assert.False(HudLayout.Intersects(HudLayout.BossCountdownBox, HudLayout.BossPlateBox));
        Assert.False(HudLayout.Intersects(HudLayout.BossCountdownBox, HudLayout.BossHealthBarBox));
    }

    [Fact]
    public void BoxesOverlap_DetectsIntersectionAndIgnoresTouching()
    {
        // 判据自身要能失败：部分相交/包含判相交，相切与分离判不相交
        Assert.True(HudLayout.BoxesOverlap(0.0f, 10.0f, 5.0f, 10.0f));
        Assert.True(HudLayout.BoxesOverlap(5.0f, 10.0f, 0.0f, 10.0f));
        Assert.True(HudLayout.BoxesOverlap(0.0f, 100.0f, 40.0f, 10.0f));
        Assert.False(HudLayout.BoxesOverlap(0.0f, 10.0f, 10.0f, 10.0f));
        Assert.False(HudLayout.BoxesOverlap(10.0f, 10.0f, 0.0f, 10.0f));
        // 回归形态（原实现的裸值：倒计时顶 78、行盒 30、背板 4..92）必须判相交——
        // 判据抓不到它要抓的那一版就等于没判
        Assert.True(HudLayout.BoxesOverlap(78.0f, 30.0f, 4.0f, 88.0f));
    }

    [Fact]
    public void BoxesHelpers_DetectIntrusionAndDegenerateBoxes()
    {
        var panel = new HudLayout.AnchoredBox(0.0f, 0.0f, 100.0f, 50.0f);
        Assert.True(HudLayout.IsWellFormed(panel));
        // 判据自身要能失败：越界一个方向即不算包含；相切算包含；相交判相交（共边不算）
        Assert.True(HudLayout.Contains(panel, new HudLayout.AnchoredBox(0.0f, 0.0f, 100.0f, 50.0f)));
        Assert.False(HudLayout.Contains(panel, new HudLayout.AnchoredBox(-1.0f, 0.0f, 100.0f, 50.0f)));
        Assert.False(HudLayout.Contains(panel, new HudLayout.AnchoredBox(0.0f, 0.0f, 101.0f, 50.0f)));
        Assert.False(HudLayout.Contains(panel, new HudLayout.AnchoredBox(0.0f, -1.0f, 100.0f, 50.0f)));
        Assert.False(HudLayout.Contains(panel, new HudLayout.AnchoredBox(0.0f, 0.0f, 100.0f, 51.0f)));
        Assert.True(HudLayout.Intersects(panel, new HudLayout.AnchoredBox(50.0f, 25.0f, 60.0f, 30.0f)));
        Assert.False(HudLayout.Intersects(panel, new HudLayout.AnchoredBox(100.0f, 0.0f, 120.0f, 50.0f)));
        Assert.False(HudLayout.Intersects(panel, new HudLayout.AnchoredBox(0.0f, 50.0f, 100.0f, 70.0f)));
        // 负宽/负高的盒会让相交与包含静默通过 —— 良构判据是它们的前置关
        Assert.False(HudLayout.IsWellFormed(new HudLayout.AnchoredBox(58.0f, -104.0f, 24.0f, -52.0f)));
        Assert.False(HudLayout.IsWellFormed(new HudLayout.AnchoredBox(24.0f, -52.0f, 58.0f, -104.0f)));
    }

    [Fact]
    public void BossHeader_PlateContainsHealthBandAndNameIsAbove()
    {
        Assert.True(HudLayout.IsWellFormed(HudLayout.BossPlateBox));
        Assert.True(HudLayout.Contains(HudLayout.BossPlateBox, HudLayout.BossHealthBarBox));
        // 血条带不许压住背板下缘（判据取盒的关系；背板改矮到装不下血条即红）
        Assert.True(
            HudLayout.BossHealthBarBox.OffsetBottom <= HudLayout.BossPlateBox.OffsetBottom,
            $"血条带底 {HudLayout.BossHealthBarBox.OffsetBottom} 越出背板底 {HudLayout.BossPlateBox.OffsetBottom}");
        // 名牌行在血条带之上（同宽同列：改一处必然带动另一处）
        Assert.True(HudLayout.BossNameRowTop < HudLayout.BossHealthBandTop);
        Assert.True(HudLayout.BossNameRowTop >= HudLayout.BossPlateTop);
        // 名牌行「盒」与血条带不许相交：名牌是有底衬的金属面板、自带上下内边距，预留量小于实际盒高
        // 时底衬会盖住血条上沿——玩家读到的血条高度就少一截。Hud 侧把该面板的纵向内边距清零，
        // 使实际盒高 == BossNameRowHeight（本断言的前提，漂移即红）。
        Assert.False(
            HudLayout.BoxesOverlap(
                HudLayout.BossNameRowTop, HudLayout.BossNameRowHeight,
                HudLayout.BossHealthBandTop, HudLayout.BossHealthBandHeight),
            $"名牌行盒（{HudLayout.BossNameRowTop}..{HudLayout.BossNameRowTop + HudLayout.BossNameRowHeight}）"
            + $"与血条带（{HudLayout.BossHealthBandTop}..{HudLayout.BossHealthBandBottom}）相交");
        Assert.Equal(-HudLayout.BossBarWidth * 0.5f, HudLayout.BossBarLeft);
        // 名牌是血条子节点：局部顶位与两带之差必须一致（换算写错会让名牌落进血条里）
        Assert.Equal(HudLayout.BossNameRowTop - HudLayout.BossHealthBandTop, HudLayout.BossNameRowTopInBar);
    }

    [Fact]
    public void InstrumentPanel_ItemsAreWellFormedAndInsidePanel()
    {
        Assert.True(HudLayout.IsWellFormed(HudLayout.InstrumentPanelBox));
        Assert.True(HudLayout.InstrumentPanelItems.Count >= 12, "面板构件枚举面被删空——判据会空转");
        foreach (var box in HudLayout.InstrumentPanelItems)
        {
            Assert.True(HudLayout.IsWellFormed(box), $"构件盒宽高非正（边界写反）：{box}");
            Assert.True(HudLayout.Contains(HudLayout.InstrumentPanelBox, box), $"构件越出面板：{box}");
        }
    }

    [Fact]
    public void InstrumentPanel_ItemsDoNotOverlap()
    {
        var items = HudLayout.InstrumentPanelItems;
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                Assert.False(
                    HudLayout.Intersects(items[i], items[j]),
                    $"面板内构件互压：{items[i]} × {items[j]}");
            }
        }
    }

    [Fact]
    public void InstrumentRows_AreSeparatedByDivider()
    {
        var topBottom = float.MinValue;
        foreach (var box in HudLayout.InstrumentTopRowItems)
        {
            topBottom = Math.Max(topBottom, box.OffsetBottom);
        }

        var bottomTop = float.MaxValue;
        foreach (var box in HudLayout.InstrumentBottomRowItems)
        {
            bottomTop = Math.Min(bottomTop, box.OffsetTop);
        }

        // 两排不许纵向重叠（挤在一起会读成一排）；分隔线必须落在两排之间的空档里
        Assert.True(topBottom <= bottomTop, $"上排底 {topBottom} 压住下排顶 {bottomTop}");
        var divider = HudLayout.InstrumentDividerBox;
        Assert.True(divider.OffsetTop >= topBottom, $"分隔线压上排：{divider}");
        Assert.True(divider.OffsetBottom <= bottomTop, $"分隔线压下排：{divider}");
        // 构件的分区不漏：上排 + 下排 = 面板内全部构件（除了分隔线自己）
        Assert.Equal(
            HudLayout.InstrumentPanelItems.Count,
            HudLayout.InstrumentTopRowItems.Count + HudLayout.InstrumentBottomRowItems.Count + 1);
    }

    [Fact]
    public void InstrumentGauges_ShareBaselineAndOrderLeftToRight()
    {
        // 三件仪表共用一条上下基线（同排）且按 x 顺序排开：后一件左缘 ≥ 前一件右缘 + 间距
        var fuel = HudLayout.FuelTankBox;
        var dash = HudLayout.SocketBox(0);
        var parry = HudLayout.SocketBox(1);
        Assert.Equal(fuel.OffsetTop, dash.OffsetTop);
        Assert.Equal(fuel.OffsetBottom, dash.OffsetBottom);
        Assert.Equal(dash.OffsetTop, parry.OffsetTop);
        Assert.Equal(dash.OffsetBottom, parry.OffsetBottom);
        Assert.True(fuel.OffsetRight <= dash.OffsetLeft, "燃料量槽与冲刺充能槽交叠");
        Assert.True(dash.OffsetRight <= parry.OffsetLeft, "冲刺与弹反充能槽交叠");
        // 小标题贴在各仪表正下方、宽度对齐
        foreach (var gauge in new[] { fuel, dash, parry })
        {
            var caption = HudLayout.CaptionBox(gauge);
            Assert.Equal(gauge.OffsetLeft, caption.OffsetLeft);
            Assert.Equal(gauge.OffsetRight, caption.OffsetRight);
            Assert.True(caption.OffsetTop >= gauge.OffsetBottom, $"小标题压到仪表上：{caption}");
        }
    }

    [Fact]
    public void CacheColumn_RowsAreRightAlignedAndSeparated()
    {
        Assert.True(HudLayout.CacheColumnItems.Count >= 4, "缓存列枚举面被删空——判据会空转");
        foreach (var box in HudLayout.CacheColumnItems)
        {
            Assert.True(HudLayout.IsWellFormed(box), $"缓存列行盒宽高非正：{box}");
            // 右对齐同一列且不越出右缘（越出即被视口切掉）
            Assert.Equal(HudLayout.CacheChipBox.OffsetRight, box.OffsetRight);
            Assert.True(box.OffsetRight <= 0.0f, $"缓存列越出右缘：{box}");
        }

        for (var i = 0; i < HudLayout.CacheColumnItems.Count; i++)
        {
            for (var j = i + 1; j < HudLayout.CacheColumnItems.Count; j++)
            {
                Assert.False(
                    HudLayout.Intersects(HudLayout.CacheColumnItems[i], HudLayout.CacheColumnItems[j]),
                    $"缓存列两行互压：{HudLayout.CacheColumnItems[i]} × {HudLayout.CacheColumnItems[j]}");
            }
        }

        // 芯片列整体在难度块之下（压上去会盖住难度读数）
        Assert.True(
            HudLayout.CacheChipBox.OffsetTop >= HudLayout.DifficultyPlateBox.OffsetBottom,
            "缓存芯片压到难度块上");
        // 难度读数落在背板之内
        Assert.True(HudLayout.Contains(HudLayout.DifficultyPlateBox, HudLayout.DifficultyLabelBox));
    }

    [Fact]
    public void BannerStack_WarningAndInfoDoNotOverlap()
    {
        var warning = HudLayout.WarningBannerBox;
        var info = HudLayout.InfoBannerBox;
        Assert.True(HudLayout.IsWellFormed(warning));
        Assert.True(HudLayout.IsWellFormed(info));
        // 两者可同屏（警告闪烁期间里程碑达成）：纵向必须分离且同宽同列
        Assert.False(HudLayout.Intersects(warning, info));
        Assert.True(warning.OffsetBottom <= info.OffsetTop, "信息横幅压到警告横幅上");
        Assert.Equal(warning.Width, info.Width);
        Assert.Equal(warning.OffsetLeft, info.OffsetLeft);
        Assert.Equal(warning.OffsetRight, info.OffsetRight);
    }

    [Fact]
    public void Hud_TakesHeaderAndBannerPositionsFromCore()
    {
        // 结构性判定：Hud 的落位必须引用 core 算式，不得再写一份字面量
        // （两处各写一份时改背板高不会带动倒计时位，正是要防的形态）
        var src = RepoFiles.Read("csharp/godot/Hud.cs");
        Assert.Contains("HudLayout.BossCountdownBox", src, StringComparison.Ordinal);
        Assert.Contains("HudLayout.BossPlateBox", src, StringComparison.Ordinal);
        Assert.Contains("HudLayout.BossNameRowHeight", src, StringComparison.Ordinal);
        Assert.Contains("HudLayout.BossNameRowTopInBar", src, StringComparison.Ordinal);
        Assert.Contains("HudLayout.BossBarLeft", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Hud_TakesPanelAndColumnPositionsFromCore()
    {
        // 同一判定的取值面广：面板/缓存列/横幅的裸像素若回到 Hud，下面的字面量与无参摆放调用即出现
        var src = RepoFiles.Read("csharp/godot/Hud.cs");
        foreach (var symbol in new[]
                 {
                     "HudLayout.InstrumentPanelBox",
                     "HudLayout.HpBarBox",
                     "HudLayout.LivesLabelBox",
                     "HudLayout.InstrumentDividerBox",
                     "HudLayout.FuelTankBox",
                     "HudLayout.SocketBox(0)",
                     "HudLayout.SocketBox(1)",
                     "HudLayout.MagStripBox",
                     "HudLayout.DockLampBox",
                     "HudLayout.DockTagBox",
                     "HudLayout.CacheChipBox",
                     "HudLayout.CacheTooltipBox",
                     "HudLayout.CacheHintBox",
                     "HudLayout.CacheGoalBox",
                     "HudLayout.DifficultyPlateBox",
                     "HudLayout.DifficultyLabelBox",
                     "HudLayout.WarningBannerBox",
                     "HudLayout.InfoBannerBox",
                 })
        {
            Assert.Contains(symbol, src, StringComparison.Ordinal);
        }

        // 曾经的落位字面量：再出现即说明算式被写回 Hud（版式又有了第二份拷贝）。
        // 只列这些像素值独有的形态，避免与无关代码里的同名数字误撞。
        foreach (var literal in new[]
                 {
                     "PlaceBottomLeft(_fuelTank, 24.0f",
                     "PlaceBottomLeft(_dockLamp, 300.0f",
                     "new Vector2(-192.0f, 118.0f)",
                     "new Vector2(-192.0f, 212.0f)",
                     "new Vector2(-360.0f, 178.0f)",
                     "new Vector2(-20.0f, 236.0f)",
                     "new Vector2(-300.0f, -34.0f)",
                     "new Vector2(-400.0f, 24.0f)",
                     "new Vector2(10.0f, -150.0f)",
                     "new Vector2(-300.0f, 140.0f)",
                     "new Vector2(-300.0f, 232.0f)",
                     "_bossBar.OffsetTop += 30.0f",
                 })
        {
            Assert.DoesNotContain(literal, src, StringComparison.Ordinal);
        }

        // 左下摆放的调用一律收盒：第二个实参出现数字即说明有人绕过 core 直接写像素
        // （`PlaceBottomLeft(foo, 24.0f, …)` 形态；取实参而不是整串，免得被 SocketBox(0) 的序号误伤）
        Assert.DoesNotMatch(new Regex(@"PlaceBottomLeft\([^,]*,\s*[-0-9]"), src);
        // 就地构造盒同样算第二份拷贝：盒里出现数字即说明落位写死在 Hud（`new HudLayout.AnchoredBox(24.0f, …)` 形态）
        Assert.DoesNotMatch(new Regex(@"new\s+HudLayout\.AnchoredBox\([^)]*\d"), src);
    }

    [Fact]
    public void ChargeBars_SlotsArePitchSpacedAndDoNotOverlap()
    {
        // 节距必须容得下整条盒：两条通道同时蓄力时（例如同时按住返航与天赋面板）条盒互压，
        // 玩家会把一条的进度读成另一条的——原实现五个手写槽位偏移的最后两对重叠 7px 与 27px。
        Assert.True(
            HudLayout.ChargeSlotPitch >= HudLayout.ChargeBarHeight,
            $"槽位节距 {HudLayout.ChargeSlotPitch} 容不下条盒高 {HudLayout.ChargeBarHeight}");

        const int channels = 5;
        for (var i = 0; i < channels; i++)
        {
            Assert.True(HudLayout.IsWellFormed(HudLayout.ChargeBarBox(i)));
        }

        for (var i = 0; i + 1 < channels; i++)
        {
            Assert.False(
                HudLayout.Intersects(HudLayout.ChargeBarBox(i), HudLayout.ChargeBarBox(i + 1)),
                $"第 {i} 与第 {i + 1} 条蓄力条盒相交");
            Assert.True(
                HudLayout.ChargeSlotY(i) > HudLayout.ChargeSlotY(i + 1),
                "槽位必须自下而上单调排开（索引 0 在最下）");
        }

        // 最下一条整体留在视野内（y 以底缘为 0、负值向上）
        Assert.True(HudLayout.ChargeSlotY(0) + HudLayout.ChargeBarHeight <= 0.0f);
    }
}
