using System;
using System.Collections.Generic;
using System.Linq;
using InfiAir.Core.Machines;
using Xunit;

namespace InfiAir.Core.Tests.Machines;

/// <summary>
/// 机型性格指纹契约测试：六型 × 六轴逐格钉住（算式写反、刻度带改窄、两层乘区接错一条，
/// 都会让图与实机对不上——而图上什么都看不出来，只是「形状不对」），另钉住几何
/// （顶点朝向、半径比例、闭合顺序）与文案键。
///
/// 为什么逐格钉住而不是只判「六型两两不同」：两两不同只能证明图有差异，证明不了**差异是对的**——
/// 把火力算成「开火间隔 ÷ 攻击力」照样六型两两不同，但每型读出来的性格正好反了。
/// </summary>
public sealed class MachineProfileTests
{
    /// <summary>定稿取值（<c>DESIGN_BASELINE</c> §1.17 / §1.19 的两层乘区算出来的：倍数与归一强度）。
    /// 顺序同 <see cref="MachineProfileTable.DrawingOrder"/>。</summary>
    private static readonly Dictionary<string, double[]> Ratios = new(StringComparer.Ordinal)
    {
        ["standard"] = new[] { 1.000, 1.000, 1.000, 1.000, 1.000, 1.000 },
        ["peregrine"] = new[] { 0.900, 1.150, 1.000, 0.927, 1.176, 1.000 },
        ["sledge"] = new[] { 1.200, 0.900, 1.000, 1.000, 0.909, 1.149 },
        ["repeater"] = new[] { 1.176, 1.000, 0.893, 1.134, 1.000, 0.870 },
        ["bulwark"] = new[] { 0.893, 1.000, 1.176, 1.400, 0.909, 1.000 },
        ["colossus"] = new[] { 1.000, 0.880, 1.200, 0.894, 1.000, 1.220 },
    };

    private static readonly Dictionary<string, double[]> Strengths = new(StringComparer.Ordinal)
    {
        ["standard"] = new[] { 0.50, 0.50, 0.50, 0.50, 0.50, 0.50 },
        ["peregrine"] = new[] { 0.38, 0.69, 0.50, 0.41, 0.72, 0.50 },
        ["sledge"] = new[] { 0.75, 0.38, 0.50, 0.50, 0.39, 0.69 },
        ["repeater"] = new[] { 0.72, 0.50, 0.37, 0.67, 0.50, 0.34 },
        ["bulwark"] = new[] { 0.37, 0.50, 0.72, 1.00, 0.39, 0.50 },
        ["colossus"] = new[] { 0.50, 0.35, 0.75, 0.37, 0.50, 0.77 },
    };

    /// <summary>强度下限：任一条轴贴到圆心，形状就读不出「这一型弱在哪」——刻度带 [0.60, 1.40]
    /// 是照着当前取值域（0.870–1.400）定的，将来有型越界到 0.70 以下，这条会红，提醒重新定刻度带
    /// （而不是让一个轴悄悄贴到圆心）。实测最弱轴 ＝ 连弩续航 0.34。</summary>
    private const double MinLegibleStrength = 0.30;

    [Fact]
    public void EveryMachineAndAxisMatchesTheFinalizedRatios()
    {
        foreach (var spec in MachineRoster.All)
        {
            var mods = spec.Defaults;
            var kit = MachineKitTable.For(spec.Id);
            var expected = Ratios[spec.Id];
            for (var i = 0; i < MachineProfileTable.AxisCount; i++)
            {
                var axis = MachineProfileTable.DrawingOrder[i];
                Assert.Equal(expected[i], MachineProfileTable.Ratio(axis, mods, kit), 3);
            }
        }
    }

    [Fact]
    public void EveryMachineAndAxisMatchesTheFinalizedStrengths()
    {
        foreach (var spec in MachineRoster.All)
        {
            var mods = spec.Defaults;
            var kit = MachineKitTable.For(spec.Id);
            var expected = Strengths[spec.Id];
            for (var i = 0; i < MachineProfileTable.AxisCount; i++)
            {
                var axis = MachineProfileTable.DrawingOrder[i];
                var strength = MachineProfileTable.Strength(axis, mods, kit);
                Assert.Equal(expected[i], strength, 2);
                Assert.InRange(strength, 0.0, 1.0);
                Assert.True(
                    strength >= MinLegibleStrength,
                    $"{spec.Id} 的 {axis} 强度 {strength:F2} 低于可读下限 {MinLegibleStrength}：形状会贴到圆心");
            }
        }
    }

    /// <summary>基准型必须落在刻度带正中（＝基准环上）：标准型一偏离，六型共用的那张图的
    /// 「凸起 / 凹陷」基准就错了——而图看上去只是「标准型也不标准」。</summary>
    [Fact]
    public void StandardSitsExactlyOnTheBaselineRingForEveryAxis()
    {
        foreach (var axis in MachineProfileTable.DrawingOrder)
        {
            Assert.Equal(MachineProfileTable.BaselineRatio, MachineProfileTable.Ratio(axis, MachineModifiers.Baseline, MachineKit.Baseline), 9);
            Assert.Equal(MachineProfileTable.BaselineStrength, MachineProfileTable.Strength(axis, MachineModifiers.Baseline, MachineKit.Baseline), 9);
        }
    }

    /// <summary>每条轴都是「越大越有利」：把某型的强化侧单独接进来，强度必须**高于**基准。
    /// 这一条抓的是方向接反（例如火力写成开火间隔 ÷ 攻击力：强化轴会读出凹陷）。</summary>
    [Fact]
    public void EveryAxisPointsTheBeneficialWay()
    {
        // 强化侧：攻速、移速、血上限、弹反窗、冲刺冷却、耗油——逐轴单独喂一种「更好」的取值
        var cases = new (MachineProfileAxis Axis, MachineModifiers Mods, MachineKit Kit)[]
        {
            (MachineProfileAxis.Fire, new MachineModifiers(1, 1.5, 1, 1, 1), MachineKit.Baseline),        // 攻击力 ↑
            (MachineProfileAxis.Fire, new MachineModifiers(1, 1, 0.5, 1, 1), MachineKit.Baseline),        // 开火间隔 ↓
            (MachineProfileAxis.Mobility, new MachineModifiers(1.5, 1, 1, 1, 1), MachineKit.Baseline),
            (MachineProfileAxis.Armor, new MachineModifiers(1, 1, 1, 0.5, 1), MachineKit.Baseline),       // 受到伤害 ↓
            (MachineProfileAxis.Armor, new MachineModifiers(1, 1, 1, 1, 1.5), MachineKit.Baseline),       // 血上限 ↑
            (MachineProfileAxis.Parry, MachineModifiers.Baseline, MachineKit.Baseline with { ParryWindowMult = 1.4 }),
            (MachineProfileAxis.Parry, MachineModifiers.Baseline, MachineKit.Baseline with { ParryCooldownMult = 0.5 }),
            (MachineProfileAxis.Dash, MachineModifiers.Baseline, MachineKit.Baseline with { DashCooldownMult = 0.5 }),
            (MachineProfileAxis.Endurance, MachineModifiers.Baseline, MachineKit.Baseline with { FuelDrainMult = 0.5 }),
        };

        foreach (var (axis, mods, kit) in cases)
        {
            Assert.True(
                MachineProfileTable.Strength(axis, mods, kit) > MachineProfileTable.BaselineStrength,
                $"{axis} 的有利方向接反了：给了更有利的取值，强度却没有高过基准");
        }
    }

    /// <summary>弹反轴读的是**占空**而不是窗本身：窗与循环各自都能抬占空，但循环一变，
    /// 窗的边际价值跟着变——把这条钉住，免得有人「顺手」把弹反轴改成窗乘区（那样壁垒以外的机型
    /// 会读出偏高的弹反）。</summary>
    [Fact]
    public void ParryAxisReadsTheDutyCycleNotTheWindowAlone()
    {
        double Parry(MachineKit kit) => MachineProfileTable.Ratio(MachineProfileAxis.Parry, MachineModifiers.Baseline, kit);

        // 窗 ×1.4（循环不动）＝ 占空 ×1.4：这一档与窗乘区等价（壁垒就是这一档）
        Assert.Equal(1.4, Parry(MachineKit.Baseline with { ParryWindowMult = 1.4 }), 6);

        // 循环缩短抬占空、循环拉长压占空
        Assert.True(Parry(MachineKit.Baseline with { ParryCooldownMult = 0.75 }) > 1.2);
        Assert.True(Parry(MachineKit.Baseline with { ParryCooldownMult = 1.5 }) < 0.9);

        // 窗 ×1.4 **且** 循环 ×1.4：占空低于 1.4——多出来的循环把窗的收益吃掉了一部分，
        // 「窗乘区直接当弹反轴」在这里必然读错
        var mixed = Parry(MachineKit.Baseline with { ParryWindowMult = 1.4, ParryCooldownMult = 1.4 });
        Assert.Equal(5.32 / 5.0, mixed, 6);
        Assert.True(mixed < 1.4, $"循环变长没有吃掉窗的收益：{mixed} ≥ 1.4");
    }

    /// <summary>刻度带钳制与非有限值回基准：越界取边界（不重排刻度带）、NaN / ±Inf 回基准 1.0
    /// ——NaN 顶点会让 Godot 整块多边形不画且不报错。两层乘区各有一组「全推到底」的取值：
    /// 火力 / 机动 / 装甲只吃数值层，弹反 / 突进 / 续航只吃能力层，两组分开喂才判得出是哪一层漏了收口。</summary>
    [Fact]
    public void OutOfBandClampsToEdgeAndNonFiniteFallsBackToBaseline()
    {
        // 顶格：数值层推到最大（攻速 / 移速 / 血上限 / 减伤），能力层也推到各自域内最有利的一侧
        var maxMods = new MachineModifiers(4.0, 4.0, 0.25, 0.25, 4.0);
        var maxKit = MachineKit.Baseline with
        {
            ParryWindowMult = 1.5,
            ParryCooldownMult = 0.4,
            DashCooldownMult = 0.4,
            FuelDrainMult = 0.4,
        };
        foreach (var axis in MachineProfileTable.DrawingOrder)
        {
            Assert.Equal(MachineProfileTable.ScaleMax, MachineProfileTable.Ratio(axis, maxMods, maxKit), 9);
            Assert.Equal(1.0, MachineProfileTable.Strength(axis, maxMods, maxKit), 9);
        }

        // 触底：数值层推到最差，能力层推到各自域内最不利的一侧
        var minMods = new MachineModifiers(0.25, 0.25, 4.0, 4.0, 0.25);
        var minKit = MachineKit.Baseline with
        {
            ParryWindowMult = 0.4,
            ParryCooldownMult = 2.0,
            DashCooldownMult = 2.0,
            FuelDrainMult = 2.0,
        };
        foreach (var axis in MachineProfileTable.DrawingOrder)
        {
            Assert.Equal(MachineProfileTable.ScaleMin, MachineProfileTable.Ratio(axis, minMods, minKit), 9);
            Assert.Equal(0.0, MachineProfileTable.Strength(axis, minMods, minKit), 9);
        }

        // NaN / ±Inf：逐轴回基准（不是回边界——回边界会把坏输入伪造成一个「贴顶」的读数）
        var nan = new MachineModifiers(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        var nanKit = MachineKit.Baseline with
        {
            ParryWindowMult = double.NaN,
            ParryCooldownMult = double.PositiveInfinity,
            DashCooldownMult = double.NaN,
            FuelDrainMult = double.NegativeInfinity,
        };
        foreach (var axis in MachineProfileTable.DrawingOrder)
        {
            Assert.Equal(MachineProfileTable.BaselineRatio, MachineProfileTable.Ratio(axis, nan, nanKit), 9);
            Assert.Equal(MachineProfileTable.BaselineStrength, MachineProfileTable.Strength(axis, nan, nanKit), 9);
        }
    }

    /// <summary>几何：索引 0 在正上方（屏幕坐标 y 向下）、顺时针每 60°，半径 ＝ 强度 × 半径。
    /// 这四条一错，六个轴名与六个顶点就整体转了 / 镜像了——图还是六边形，只是读的是别人的轴。</summary>
    [Fact]
    public void VertexGeometryStartsAtTheTopAndRunsClockwise()
    {
        const double cx = 100.0;
        const double cy = 100.0;
        const double radius = 40.0;

        var top = MachineProfileTable.Vertex(cx, cy, radius, 0, 1.0);
        Assert.Equal(cx, top.X, 9);
        Assert.Equal(cy - radius, top.Y, 9); // 正上方（y 越小越靠上）

        var next = MachineProfileTable.Vertex(cx, cy, radius, 1, 1.0); // +60°：右上
        Assert.Equal(cx + radius * Math.Sin(Math.PI / 3.0), next.X, 9);
        Assert.Equal(cy - radius * Math.Cos(Math.PI / 3.0), next.Y, 9);

        var bottom = MachineProfileTable.Vertex(cx, cy, radius, 3, 1.0); // +180°：正下方
        Assert.Equal(cx, bottom.X, 9);
        Assert.Equal(cy + radius, bottom.Y, 9);

        // 半径比例 ＝ 强度（含下限钳制与中心回退）
        var half = MachineProfileTable.Vertex(cx, cy, radius, 0, MachineProfileTable.BaselineStrength);
        Assert.Equal(cy - radius * MachineProfileTable.BaselineStrength, half.Y, 9);
        var clamped = MachineProfileTable.Vertex(cx, cy, radius, 0, 9.0);
        Assert.Equal(cy - radius, clamped.Y, 9);
        var nan = MachineProfileTable.Vertex(cx, cy, radius, 0, double.NaN);
        Assert.Equal(cy - radius * MachineProfileTable.BaselineStrength, nan.Y, 9);
        var centered = MachineProfileTable.Vertex(cx, cy, radius, 0, 0.0);
        Assert.Equal(cy, centered.Y, 9);
    }

    /// <summary>多边形与环：六点、顺序 ＝ 绘制顺序、环的六点同半径、多边形的第 i 点等于第 i 条轴的顶点。
    /// 另断言绕行方向恒定（相邻边的叉积同号）——半径各异的星形多边形靠这一条保证简单（不自交）；
    /// 自交的多边形在 Godot 里三角化失败、整块不画，而那是不报错的空白。</summary>
    [Fact]
    public void PolygonFollowsDrawingOrderAndNeverSelfIntersects()
    {
        const double cx = 0.0;
        const double cy = 0.0;
        const double radius = 50.0;

        foreach (var spec in MachineRoster.All)
        {
            var points = MachineProfileTable.Polygon(cx, cy, radius, spec.Defaults, MachineKitTable.For(spec.Id));
            Assert.Equal(MachineProfileTable.AxisCount, points.Length);

            for (var i = 0; i < points.Length; i++)
            {
                var axis = MachineProfileTable.DrawingOrder[i];
                var expected = MachineProfileTable.Vertex(
                    cx, cy, radius, i, MachineProfileTable.Strength(axis, spec.Defaults, MachineKitTable.For(spec.Id)));
                Assert.Equal(expected, points[i]);

                var next = points[(i + 1) % points.Length];
                var current = points[i];
                var cross = current.X * next.Y - current.Y * next.X; // 与中心的叉积（中心在原点）
                Assert.True(
                    cross > 0.0,
                    $"{spec.Id} 的第 {i} → {i + 1} 边绕行反向（叉积 {cross}）：相邻顶点错序会让多边形自交");
            }
        }

        var ring = MachineProfileTable.Ring(cx, cy, radius, MachineProfileTable.BaselineStrength);
        Assert.Equal(MachineProfileTable.AxisCount, ring.Length);
        foreach (var point in ring)
        {
            var distance = Math.Sqrt(point.X * point.X + point.Y * point.Y);
            Assert.Equal(radius * MachineProfileTable.BaselineStrength, distance, 9);
        }
    }

    /// <summary>六条轴的文案键：键族前缀、键名互不相同、且中英两列在 <c>data/translations.csv</c>
    /// 都存在（缺键玩家看到的是键名本身——而这是图的轴名，六个键名并排会读成一串乱码）。
    /// 表里键行不含 ASCII 逗号，故按行首匹配取源即可（整表解析在 MachineRosterTests / MachineKitTests
    /// 各有一份，此处不引第三份）。</summary>
    [Fact]
    public void TextKeysExistInBothLanguages()
    {
        var lines = RepoFiles.Read("data/translations.csv").Split('\n');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var axis in MachineProfileTable.DrawingOrder)
        {
            var key = MachineProfileTable.TextKey(axis);
            Assert.StartsWith("MACHINE_PROFILE_", key, StringComparison.Ordinal);
            Assert.True(seen.Add(key), $"文案键重复：{key}（两条轴会印同一个名字）");

            var line = lines.FirstOrDefault(l => l.StartsWith(key + ",", StringComparison.Ordinal));
            Assert.False(line == null, $"文案表缺键：{key}");
            var columns = line!.Split(',');
            Assert.True(columns.Length >= 3, $"{key} 的文案行不是三列：{line}");
            Assert.False(string.IsNullOrWhiteSpace(columns[1].Trim('"')), $"{key} 缺中文");
            Assert.False(string.IsNullOrWhiteSpace(columns[2].Trim('"')), $"{key} 缺英文");
        }
    }

    /// <summary>绘制顺序覆盖全部六条轴且不重不漏（顺序本身只影响排布，漏一条会画出一个五边形）。</summary>
    [Fact]
    public void DrawingOrderCoversEveryAxisExactlyOnce()
    {
        Assert.Equal(MachineProfileTable.AxisCount, MachineProfileTable.DrawingOrder.Count);
        Assert.Equal(
            MachineProfileTable.AxisCount,
            MachineProfileTable.DrawingOrder.Distinct().Count());
        for (var i = 0; i < MachineProfileTable.AxisCount; i++)
        {
            Assert.Equal(i, (int)MachineProfileTable.DrawingOrder[i]); // 顺序索引与枚举序一致，读代码时不必回头查表
        }
    }
}
