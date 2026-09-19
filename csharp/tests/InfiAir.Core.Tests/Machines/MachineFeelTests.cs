using System;
using System.Collections.Generic;
using System.Text.Json;
using InfiAir.Core.Machines;
using Xunit;

namespace InfiAir.Core.Tests.Machines;

/// <summary>
/// 机型手感档案契约测试：把「六型两两不同 / 标准型＝基准 / 域收口 / 设计稿条目映射 /
/// 代码默认 vs <c>data/balance.json machines.*.feel.*</c>」钉住。
/// 手感档案是表现层参数集，坏法全部是静默的：两型撞车则机型性格读不出、
/// 两侧数值分叉则「改 json 不生效」或「回退值与定稿不符」，域外值则表现量级失控。
/// </summary>
public sealed class MachineFeelTests
{
    [Fact]
    public void StandardAndUnknownIdsFallBackToBaseline()
    {
        Assert.Equal(MachineFeel.Baseline, MachineFeelTable.For(MachineRoster.StandardId));
        Assert.Equal(MachineFeel.Baseline, MachineFeelTable.For(null));
        Assert.Equal(MachineFeel.Baseline, MachineFeelTable.For(string.Empty));
        Assert.Equal(MachineFeel.Baseline, MachineFeelTable.For("not-a-machine"));

        // 基准档案必须是「零差异」：全部乘区 1.0、新增量 0、档位与开关保持既有口径
        var b = MachineFeel.Baseline;
        Assert.Equal(1.0, b.DashPopScaleMult);
        Assert.Equal(1.0, b.SwayMult);
        Assert.Equal(1.0, b.BankMult);
        Assert.Equal(1.0, b.VortexThresholdMult);
        Assert.Equal(1.0, b.RecoilPxMult);
        Assert.Equal(1.0, b.HitImpactMult);
        Assert.Equal(0.0, b.DirectShake);
        Assert.Equal(1.0, b.LagPxMult);
        Assert.Equal(1.0, b.FireLightMult);
        Assert.Equal(0.0, b.FireJitterPx);
        Assert.Equal(1.0, b.HitSquashMult);
        Assert.Equal(1.0, b.ParryGlowMult);
        Assert.Equal(1.0, b.BobPxMult);
        Assert.Equal(1.0, b.AfterimageLifeMult);
        Assert.Equal(1.0, b.AfterimageRateMult);
        Assert.Equal(0.0, b.DashSpeedline);
        Assert.Equal(1.0, b.MuzzleGlowMult);
        Assert.Equal(1.0, b.HitSparkMult);
        Assert.Equal(0.0, b.TracerLenPx);
        Assert.Equal(1.0, b.DamageFrameMult);
        Assert.Equal(MachineFeel.SmokeHeavyLevel, b.SmokeMinLevel);
        Assert.False(b.Deflect);
    }

    /// <summary>六型两两不同：任何两型档案撞车，那一对机型在游戏内就读不出性格差
    /// （可读性判据「差异小到读不出＝白做」的结构化形式）。</summary>
    [Fact]
    public void SixMachinesArePairwiseDistinct()
    {
        var feels = new List<MachineFeel>();
        foreach (var spec in MachineRoster.All)
        {
            var feel = MachineFeelTable.For(spec.Id);
            Assert.Equal(feel, MachineFeelTable.Sanitize(feel)); // 默认表全落在域内：收口后不变
            feels.Add(feel);
        }

        for (var i = 0; i < feels.Count; i++)
        {
            for (var j = i + 1; j < feels.Count; j++)
            {
                Assert.NotEqual(feels[i], feels[j]);
            }
        }
    }

    [Fact]
    public void SanitizeClampsOutOfRangeAndNonFinite()
    {
        var over = MachineFeelTable.Sanitize(new MachineFeel(
            99.0, 99.0, 99.0, 99.0, 99.0, 99.0, 99.0, 99.0, 99.0, 99.0,
            99.0, 99.0, 99.0, 99.0, 99.0, 99.0, 99.0, 99.0, 99.0, 99.0,
            SmokeMinLevel: 7, DeflectRead: 0.4));
        Assert.Equal(MachineFeelTable.MultCeiling, over.DashPopScaleMult);
        Assert.Equal(MachineFeelTable.MultCeiling, over.SwayMult);
        Assert.Equal(12.0, over.DirectShake);
        Assert.Equal(4.0, over.FireJitterPx);
        Assert.Equal(48.0, over.TracerLenPx);
        Assert.Equal(1.0, over.DashSpeedline);
        Assert.Equal(2.0, over.DamageFrameMult);
        Assert.Equal(MachineFeel.SmokeHeavyLevel, over.SmokeMinLevel);
        Assert.False(over.Deflect); // 0.4 未过开关阈值：不生效

        var under = MachineFeelTable.Sanitize(new MachineFeel(
            -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0,
            -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0,
            SmokeMinLevel: 0, DeflectRead: 2.0));
        Assert.Equal(MachineFeelTable.MultFloor, under.DashPopScaleMult);
        Assert.Equal(0.0, under.DirectShake);
        Assert.Equal(0.0, under.FireJitterPx);
        Assert.Equal(0.0, under.TracerLenPx);
        Assert.Equal(0.5, under.DamageFrameMult);
        Assert.Equal(MachineFeel.SmokeLightLevel, under.SmokeMinLevel);
        Assert.True(under.Deflect); // 2.0 过阈值：开关收口为 1

        var nan = MachineFeelTable.Sanitize(BaselineWith(x => x with { SwayMult = double.NaN }));
        Assert.Equal(MachineFeel.Baseline, nan); // 任一非有限 → 整份回基准，不渗半个坏值
    }

    /// <summary>设计稿条目 → 档案字段的映射抽查：每型的差异化字段必须与设计稿方向一致
    /// （ROADMAP「设计稿 · 机型性格进游戏内」表格；改设计方向时这里是先红的那一环）。</summary>
    [Fact]
    public void DesignIntentPerMachineIsExpressed()
    {
        var peregrine = MachineFeelTable.For("peregrine");
        Assert.True(peregrine.DashPopScaleMult > 1.0); // ② 冲刺弹跳更大
        Assert.True(peregrine.SwayMult > 1.0);         // ② 转向跟随更「飘」
        Assert.True(peregrine.VortexThresholdMult < 1.0); // ② 翼尖涡流更早出现
        Assert.True(peregrine.AfterimageLifeMult > 1.0);  // ① 残影更长
        Assert.True(peregrine.AfterimageRateMult > 1.0);  // ① 残影更密
        Assert.True(peregrine.DashSpeedline > 0.0);       // ① 冲刺速度线（标准型 0 ＝ 旧体验不变）

        var sledge = MachineFeelTable.For("sledge");
        Assert.True(sledge.RecoilPxMult > 1.0);  // ② 开火后坐位移更大
        Assert.True(sledge.HitImpactMult > 1.0); // ② 命中顿帧更强
        Assert.True(sledge.DirectShake > 0.0);   // ② 直击震动（基准 0 ＝ 旧体验不变）
        Assert.True(sledge.LagPxMult > 1.0);     // ② 加减速更「沉」
        Assert.True(sledge.MuzzleGlowMult > 1.0); // ① 炮口冲击环更大
        Assert.True(sledge.HitSparkMult > 1.0);   // ① 弹着火花更重

        var repeater = MachineFeelTable.For("repeater");
        Assert.True(repeater.TracerLenPx > 0.0);  // ① 弹道曳光成串
        Assert.True(repeater.FireLightMult > 1.0); // ② 开火机身光更密
        Assert.True(repeater.FireJitterPx > 0.0);  // ② 连射机身微抖
        Assert.True(repeater.MuzzleGlowMult > 1.0); // ① 枪口闪更密（射速差由乘区另给）

        var bulwark = MachineFeelTable.For("bulwark");
        Assert.True(bulwark.Deflect);               // ① 受击读作「被弹开」
        Assert.True(bulwark.DamageFrameMult < 1.0); // ① 损伤帧更晚
        Assert.True(bulwark.HitSquashMult < 1.0);   // ② 受击回弹更小
        Assert.True(bulwark.ParryGlowMult > 1.0);   // ② 弹反环更亮
        Assert.True(bulwark.SwayMult < 1.0 && bulwark.BankMult < 1.0); // ② 机动更「钝」

        var colossus = MachineFeelTable.For("colossus");
        Assert.Equal(MachineFeel.SmokeLightLevel, colossus.SmokeMinLevel); // ① 损伤烟更早（轻伤档）
        Assert.True(colossus.DamageFrameMult > 1.0); // ① 损伤帧更早（甲上读出伤痕）
        Assert.True(colossus.LagPxMult > 1.0);       // ② 加速更「沉」
        Assert.True(colossus.HitSquashMult < 1.0);   // ② 受击压缩更小
        Assert.True(colossus.BobPxMult < 1.0);       // ② 悬停更「稳」
    }

    /// <summary>代码默认 vs balance.json 对账（两侧分叉时 json 完整则看不出——只有
    /// 键缺失/损坏回退默认才读得出来，改一侧必须同时改另一侧，AGENTS §3）。</summary>
    [Fact]
    public void BalanceJsonAgreesWithRosterDefaults()
    {
        var root = JsonDocument.Parse(RepoFiles.Read("data/balance.json")).RootElement;
        Assert.True(root.TryGetProperty("machines", out var machines), "balance.json 缺 machines 分区");

        foreach (var spec in MachineRoster.All)
        {
            var expected = MachineFeelTable.For(spec.Id);
            var present = false;
            JsonElement feelNode = default;
            if (machines.TryGetProperty(spec.Id, out var node) && node.TryGetProperty("feel", out feelNode))
            {
                present = true;
            }

            if (spec.Id == MachineRoster.StandardId)
            {
                // 标准型不列键：手感档案无差异可写，列了反而暗示「标准型可调出手感」
                Assert.False(present, "标准型不应有 machines.standard.feel 分区");
                continue;
            }

            Assert.True(present, $"balance.json 缺 machines.{spec.Id}.feel 分区");

            // 只对账**列出的键**（与 machines.* 乘区同口径：默认值在 core、json 只列要覆盖的键），
            // 但列出的每个键都必须在白名单里——键名写错（如 sway_mul）会静默无效，正是这里要抓的
            var known = new HashSet<string>(MachineFeelTable.Keys, StringComparer.Ordinal);
            Assert.NotEmpty(feelNode.EnumerateObject());
            foreach (var property in feelNode.EnumerateObject())
            {
                Assert.True(known.Contains(property.Name), $"machines.{spec.Id}.feel.{property.Name} 不是已知手感键（拼写错误即死键）");
                var value = property.Value;
                var actual = property.Name == MachineFeelTable.SmokeMinLevelKey ? value.GetInt32() : value.GetDouble();
                Assert.Equal(GetField(expected, property.Name), actual);
            }
        }
    }

    private static double GetField(MachineFeel f, string key) => key switch
    {
        MachineFeelTable.DashPopScaleKey => f.DashPopScaleMult,
        MachineFeelTable.SwayKey => f.SwayMult,
        MachineFeelTable.BankKey => f.BankMult,
        MachineFeelTable.VortexThresholdKey => f.VortexThresholdMult,
        MachineFeelTable.RecoilPxKey => f.RecoilPxMult,
        MachineFeelTable.HitImpactKey => f.HitImpactMult,
        MachineFeelTable.DirectShakeKey => f.DirectShake,
        MachineFeelTable.LagPxKey => f.LagPxMult,
        MachineFeelTable.FireLightKey => f.FireLightMult,
        MachineFeelTable.FireJitterKey => f.FireJitterPx,
        MachineFeelTable.HitSquashKey => f.HitSquashMult,
        MachineFeelTable.ParryGlowKey => f.ParryGlowMult,
        MachineFeelTable.BobPxKey => f.BobPxMult,
        MachineFeelTable.AfterimageLifeKey => f.AfterimageLifeMult,
        MachineFeelTable.AfterimageRateKey => f.AfterimageRateMult,
        MachineFeelTable.DashSpeedlineKey => f.DashSpeedline,
        MachineFeelTable.MuzzleGlowKey => f.MuzzleGlowMult,
        MachineFeelTable.HitSparkKey => f.HitSparkMult,
        MachineFeelTable.TracerLenKey => f.TracerLenPx,
        MachineFeelTable.DamageFrameKey => f.DamageFrameMult,
        MachineFeelTable.SmokeMinLevelKey => f.SmokeMinLevel,
        MachineFeelTable.DeflectReadKey => f.DeflectRead,
        _ => throw new ArgumentException($"未知手感键：{key}"),
    };

    private static MachineFeel BaselineWith(Func<MachineFeel, MachineFeel> mutate) =>
        mutate(MachineFeel.Baseline);
}
