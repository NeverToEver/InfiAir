using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using InfiAir.Core.Machines;
using Xunit;

namespace InfiAir.Core.Tests.Machines;

/// <summary>
/// 机型能力档案契约测试：把「域与回退 / 六型两两不同 / 异轴且分属不同能力 / 组合与加成行两两不同 /
/// 方向与可读单位 / 亚一层上界 / 弹反窗不下探 / 设计条目映射 / 代码默认 vs
/// <c>data/balance.json machines.*.kit.*</c> / 文案键中英双列」钉住。
/// 四条轴的坏法全部是静默的：两型撞车读不出性格、代价与强化落在同一能力上相抵成中性、
/// 越界值被钳成 1.0 会把代价抹平、两侧数值分叉则「改 json 不生效」或「回退值与定稿不符」。
/// </summary>
public sealed class MachineKitTests
{
    [Fact]
    public void StandardAndUnknownIdsFallBackToBaseline()
    {
        // 旧档、手改档、未来版本的新 id 都走这一条：读不出即基准（与 MachineRoster.ById 同白名单口径）
        Assert.Equal(MachineKit.Baseline, MachineKitTable.For(MachineRoster.StandardId));
        Assert.Equal(MachineKit.Baseline, MachineKitTable.For(null));
        Assert.Equal(MachineKit.Baseline, MachineKitTable.For(string.Empty));
        Assert.Equal(MachineKit.Baseline, MachineKitTable.For("not-a-machine"));
        Assert.Equal(MachineKit.Baseline, MachineKitTable.For("Standard")); // 大小写不是白名单的一部分

        // 基准档案必须是「零差异」：四条轴全 1.0——它是面板全部读数的参照物
        Assert.Equal(1.0, MachineKit.Baseline.ParryWindowMult);
        Assert.Equal(1.0, MachineKit.Baseline.ParryCooldownMult);
        Assert.Equal(1.0, MachineKit.Baseline.DashCooldownMult);
        Assert.Equal(1.0, MachineKit.Baseline.FuelDrainMult);
    }

    /// <summary>六型两两不同：任何两型档案撞车，那一对机型在能力层就读不出差异（可读性判据的结构化形式）。</summary>
    [Fact]
    public void SixMachinesArePairwiseDistinct()
    {
        var kits = new List<MachineKit>();
        foreach (var spec in MachineRoster.All)
        {
            var kit = MachineKitTable.For(spec.Id);
            Assert.Equal(kit, MachineKitTable.Sanitize(kit)); // 定稿值全落在域内：收口后不变
            kits.Add(kit);
        }

        Assert.Equal(MachineRoster.Count, kits.Count);
        for (var i = 0; i < kits.Count; i++)
        {
            for (var j = i + 1; j < kits.Count; j++)
            {
                Assert.NotEqual(kits[i], kits[j]);
            }
        }

        Assert.Equal(MachineKit.Baseline, MachineKitTable.For(MachineRoster.StandardId));
    }

    /// <summary>每型只动它宣称的两条轴：多动一条轴 = 面板不写、玩家读不到的隐形差异
    /// （数值层由 <c>MachineRosterTests.EverySpecialistMovesExactlyItsBonusAndPenaltyAxes</c> 钉同一条）。</summary>
    [Fact]
    public void KitMovesExactlyTheDeclaredBonusAndPenaltyAxes()
    {
        foreach (var spec in MachineRoster.All)
        {
            var moved = MovedAxes(MachineKitTable.For(spec.Id));

            if (spec.KitBonus == MachineAxis.None)
            {
                Assert.Equal(MachineAxis.None, spec.KitPenalty);
                Assert.Equal(MachineKit.Baseline, MachineKitTable.For(spec.Id));
                Assert.Empty(moved);
                continue;
            }

            Assert.NotEqual(MachineAxis.None, spec.KitPenalty);
            Assert.Equal(2, moved.Count);
            Assert.Contains(spec.KitBonus, moved);
            Assert.Contains(spec.KitPenalty, moved);
        }
    }

    [Fact]
    public void SanitizeClampsOutOfRangeAndNonFinite()
    {
        // 越界取**边界值**而不是回 1.0：回 1.0 会把 >1.0 的代价（重锤 / 巨像的耗油罚）静默抹平成「没有代价」
        var over = MachineKitTable.Sanitize(new MachineKit(2.0, 2.5, 3.0, 9.0));
        Assert.Equal(MachineKitTable.WindowMax, over.ParryWindowMult); // 1.5：再大也被 ParryTimeline 钳到流程时长
        Assert.Equal(MachineKitTable.MultMax, over.ParryCooldownMult);
        Assert.Equal(MachineKitTable.MultMax, over.DashCooldownMult);
        Assert.Equal(MachineKitTable.MultMax, over.FuelDrainMult);

        var under = MachineKitTable.Sanitize(new MachineKit(0.0, -1.0, 0.1, 0.39));
        Assert.Equal(MachineKitTable.WindowMin, under.ParryWindowMult);
        Assert.Equal(MachineKitTable.MultMin, under.ParryCooldownMult);
        Assert.Equal(MachineKitTable.MultMin, under.DashCooldownMult);
        Assert.Equal(MachineKitTable.MultMin, under.FuelDrainMult);

        // NaN / Inf 逐轴回 1.0（不钳：钳制函数不拦 NaN，渗进 HUD 与判定后一起 NaN 化），
        // 但同一份档案里合法的那几条不许被一起丢掉
        var nan = MachineKitTable.Sanitize(new MachineKit(double.NaN, 0.85, 1.10, 1.15));
        Assert.Equal(1.0, nan.ParryWindowMult);
        Assert.Equal(0.85, nan.ParryCooldownMult);
        Assert.Equal(1.10, nan.DashCooldownMult);
        Assert.Equal(1.15, nan.FuelDrainMult);

        var infinite = MachineKitTable.Sanitize(new MachineKit(double.PositiveInfinity, 1.0, double.NegativeInfinity, 1.0));
        Assert.Equal(MachineKit.Baseline, infinite);
    }

    /// <summary>每型的两条轴必须落在**不同能力**上（K1）：同一能力的另一条轴作代价会在共同尺度上相抵
    /// （弹反窗 ↑ + 弹反循环 ↓ 在「单位时间有效弹反次数」上实测读作中性），而面板仍显示一加一减。</summary>
    [Fact]
    public void BonusAndPenaltyAreDifferentAxes()
    {
        foreach (var spec in Specialists())
        {
            Assert.NotEqual(spec.KitBonus, spec.KitPenalty);
            Assert.NotEqual(CapabilityOf(spec.KitBonus), CapabilityOf(spec.KitPenalty));
        }
    }

    /// <summary>(强化轴, 代价轴) 组合两两不同：组合撞车的第二型在面板上与第一型逐字相同，
    /// 玩家答不出「哪台是哪台」。</summary>
    [Fact]
    public void AxisPairsArePairwiseDistinct()
    {
        var pairs = new List<(MachineAxis Bonus, MachineAxis Penalty)>();
        foreach (var spec in Specialists())
        {
            var pair = (spec.KitBonus, spec.KitPenalty);
            Assert.DoesNotContain(pair, pairs);
            pairs.Add(pair);
        }

        Assert.Equal(5, pairs.Count);
    }

    /// <summary>五型加成行的 (轴, 值) 对必须两两不同（K3）。四轴 × 五型是鸽笼（必有一条轴被两型强化），
    /// 重复只能落在**不同数值**上；值与轴都从 <c>balance.json</c> 读——数值表改了而这条不跟着看，
    /// 「两型加成行逐字相同」就只能在面板上被玩家发现。</summary>
    [Fact]
    public void BonusRowTextIsPairwiseDistinct()
    {
        var machines = BalanceMachines();
        var rows = new List<(MachineAxis Axis, double Value)>();
        foreach (var spec in Specialists())
        {
            var value = BalanceKitValue(machines, spec, spec.KitBonus);
            foreach (var (axis, seen) in rows)
            {
                Assert.False(axis == spec.KitBonus && Math.Abs(seen - value) < 1e-9,
                    $"{spec.Id} 的加成行与另一型逐字相同（{spec.KitBonus} {value}）");
            }

            rows.Add((spec.KitBonus, value));
        }

        Assert.Equal(5, rows.Count);
    }

    /// <summary>方向判据：强化侧必须往有利方向偏、代价侧必须往不利方向偏。
    /// 写反（一条让机体更强的「代价」，或一条更弱还叫强化的轴）编译、冒烟全过，只有玩家读得出。</summary>
    [Fact]
    public void BonusPointsStrongWayAndPenaltyWeakWay()
    {
        foreach (var spec in Specialists())
        {
            var kit = MachineKitTable.For(spec.Id);
            Assert.True(MachineKitTable.IsBeneficial(spec.KitBonus, kit), $"{spec.Id} 的强化方向写反了（{spec.KitBonus}）");
            Assert.False(MachineKitTable.IsBeneficial(spec.KitPenalty, kit), $"{spec.Id} 的代价方向写反了（{spec.KitPenalty}）");
        }

        Assert.False(MachineKitTable.IsBeneficial(MachineAxis.None, MachineKit.Baseline));
    }

    /// <summary>两侧的可读单位都要 ≥ 1.00：不到 1 RU 只是账本差异、算不上取舍——
    /// 「可读的代价才有损失厌恶」，这是损失厌恶生效的前置条件。</summary>
    [Fact]
    public void ReadabilityUnitsReachTheFloor()
    {
        foreach (var spec in Specialists())
        {
            var kit = MachineKitTable.For(spec.Id);
            var bonus = MachineKitTable.ReadabilityUnits(spec.KitBonus, kit);
            var penalty = MachineKitTable.ReadabilityUnits(spec.KitPenalty, kit);
            Assert.True(bonus >= 1.0, $"{spec.Id} 的强化只有 {bonus:F2} RU（读不出＝白做）");
            Assert.True(penalty >= 1.0, $"{spec.Id} 的代价只有 {penalty:F2} RU（读不出＝这条代价不算取舍）");
        }
    }

    /// <summary>代价幅度带 [0.4, 0.9]（RU 口径，与数值层同一条）：代价必须小于强化
    /// （损失体会约为等量收益的两倍，1:1 的交换体感是亏的），又不能小到只是象征
    /// （象征性代价抵不住收益，能力预算不成立）。</summary>
    [Fact]
    public void PenaltyStaysInBandAndBelowTheBonus()
    {
        foreach (var spec in Specialists())
        {
            var kit = MachineKitTable.For(spec.Id);
            var bonus = MachineKitTable.ReadabilityUnits(spec.KitBonus, kit);
            var penalty = MachineKitTable.ReadabilityUnits(spec.KitPenalty, kit);
            Assert.True(bonus > 0.0, $"{spec.Id} 的强化没有可读变化");
            var ratio = penalty / bonus;
            Assert.True(ratio is >= 0.4 and <= 0.9, $"{spec.Id} 的代价/强化 RU 比 {ratio:F2} 越界（应在 0.4–0.9）");
        }
    }

    /// <summary>强化值不得越过对应增幅的一层（亚一层口径：一层 <c>phase_dash</c> ×0.8、
    /// <c>deflector</c> ×0.78、<c>efficient_boost</c> ×0.75），否则机型层会盖过局内成长。
    /// 弹反窗另有一条硬顶：设计上限 1.40（0.8s 流程里给前后摇留 0.1s），域上限 1.5 已是饱和死区边界。
    /// 改设计方向时这里是先红的那一环。</summary>
    [Fact]
    public void BonusStaysBelowOneAugmentLayer()
    {
        foreach (var spec in Specialists())
        {
            var kit = MachineKitTable.For(spec.Id);
            if (spec.KitBonus == MachineAxis.ParryWindow)
            {
                Assert.True(kit.ParryWindowMult <= 1.40, $"{spec.Id} 的弹反窗 ×{kit.ParryWindowMult} 越过设计上限 1.40（0.70s）");
                continue;
            }

            // 缩减类强化的下限（越靠近 1.0 越弱）：三条都高于对应增幅的一层，故「满投资」永远是支配读数
            var lowerBound = spec.KitBonus switch
            {
                MachineAxis.DashCooldown => 0.85,
                MachineAxis.ParryCooldown => 0.85,
                MachineAxis.FuelDrain => 0.82,
                _ => double.NaN,
            };
            Assert.False(double.IsNaN(lowerBound), $"{spec.Id} 的强化轴 {spec.KitBonus} 没有亚一层判据");
            var value = KitValue(kit, spec.KitBonus);
            Assert.True(value >= lowerBound, $"{spec.Id} 的 {spec.KitBonus} ×{value} 越过亚一层下限 {lowerBound}");
        }

        // 硬顶的来由：ParryTimeline 把激活时长钳到流程时长 0.8s，[1.6, 2.0] 是改了没反应的死区
        Assert.Equal(1.5, MachineKitTable.WindowMax);
    }

    /// <summary>K2：弹反窗不得作为任何机型的代价轴。一次**失败**的弹反同样烧掉整个循环，
    /// 窄窗的单价极高且无处补偿（输入缓冲 0.1s 是全局的、不能按机型给），故全表不得出现窗下探。</summary>
    [Fact]
    public void ParryWindowIsNeverAPenalty()
    {
        foreach (var spec in MachineRoster.All)
        {
            Assert.NotEqual(MachineAxis.ParryWindow, spec.KitPenalty);
            Assert.True(MachineKitTable.For(spec.Id).ParryWindowMult >= 1.0, $"{spec.Id} 的弹反窗下探（K2 禁止反方向的机型）");
        }
    }

    /// <summary>§1.19 的六型表逐项映射：轴 + 乘区值 + 面板要印的自然量秒数。
    /// 改设计方向时这里是先红的那一环。</summary>
    [Fact]
    public void DesignIntentPerMachineIsExpressed()
    {
        AssertMachine("peregrine", MachineAxis.DashCooldown, 0.85, MachineAxis.ParryCooldown, 1.10);
        AssertMachine("sledge", MachineAxis.FuelDrain, 0.87, MachineAxis.DashCooldown, 1.10);
        AssertMachine("repeater", MachineAxis.ParryCooldown, 0.85, MachineAxis.FuelDrain, 1.15);
        AssertMachine("bulwark", MachineAxis.ParryWindow, 1.40, MachineAxis.DashCooldown, 1.10);
        AssertMachine("colossus", MachineAxis.FuelDrain, 0.82, MachineAxis.ParryCooldown, 1.15);

        // 基准自然量（面板读数的参照物）：弹反窗 0.50s / 循环 3.80s / 冲刺冷却 4.00s / 满箱加速 2.86s
        Assert.Equal(0.50, MachineKitTable.BaselineQuantity(MachineAxis.ParryWindow), 2);
        Assert.Equal(3.80, MachineKitTable.BaselineQuantity(MachineAxis.ParryCooldown), 2);
        Assert.Equal(4.00, MachineKitTable.BaselineQuantity(MachineAxis.DashCooldown), 2);
        Assert.Equal(2.86, MachineKitTable.BaselineQuantity(MachineAxis.FuelDrain), 2);

        // 面板印的是自然量秒数，四条轴的换算式各不相同（弹反循环＝流程＋冷却、耗油与续航成反比）
        Assert.Equal(3.40, Nat("peregrine", MachineAxis.DashCooldown), 2);
        Assert.Equal(4.10, Nat("peregrine", MachineAxis.ParryCooldown), 2);
        Assert.Equal(4.40, Nat("sledge", MachineAxis.DashCooldown), 2);
        Assert.Equal(3.28, Nat("sledge", MachineAxis.FuelDrain), 2);
        Assert.Equal(3.35, Nat("repeater", MachineAxis.ParryCooldown), 2);
        Assert.Equal(2.48, Nat("repeater", MachineAxis.FuelDrain), 2);
        Assert.Equal(0.70, Nat("bulwark", MachineAxis.ParryWindow), 2);
        Assert.Equal(4.40, Nat("bulwark", MachineAxis.DashCooldown), 2);
        Assert.Equal(3.48, Nat("colossus", MachineAxis.FuelDrain), 2);
        Assert.Equal(4.25, Nat("colossus", MachineAxis.ParryCooldown), 2);

        // 百分比由自然量反算（不是印乘区）：弹反循环 ×1.10 读作 +8%、耗油 ×0.82 读作续航 +22%——
        // 而后者的「+」是有利，前者是代价：符号是自然量方向、强弱看 IsBeneficial，两者不共用
        var bulwark = MachineKitTable.For("bulwark");
        Assert.Equal(40, MachineKitTable.Percent(MachineAxis.ParryWindow, bulwark));
        var peregrine = MachineKitTable.For("peregrine");
        Assert.Equal(8, MachineKitTable.Percent(MachineAxis.ParryCooldown, peregrine));
        Assert.False(MachineKitTable.IsBeneficial(MachineAxis.ParryCooldown, peregrine));
        var colossus = MachineKitTable.For("colossus");
        Assert.Equal(22, MachineKitTable.Percent(MachineAxis.FuelDrain, colossus));
        Assert.True(MachineKitTable.IsBeneficial(MachineAxis.FuelDrain, colossus));

        // 标准型：两条都是 None、四条轴全基准（它一偏离 1.0，全表所有数字同时失真）
        var standard = MachineRoster.ById(MachineRoster.StandardId);
        Assert.Equal(MachineAxis.None, standard.KitBonus);
        Assert.Equal(MachineAxis.None, standard.KitPenalty);
    }

    /// <summary>代码默认 vs <c>data/balance.json machines.*.kit</c> 对账（AGENTS §3 清单项）：
    /// 两侧分叉时 json 完整即看不出——只有键缺失 / 损坏回退默认才读得出来。
    /// 列出的键还必须在白名单里（拼错如 <c>parry_windows_mult</c> 会被整键静默忽略，报错比生效更难遇到）。</summary>
    [Fact]
    public void BalanceJsonAgreesWithRosterDefaults()
    {
        var machines = BalanceMachines();
        var known = new HashSet<string>(MachineKitTable.Keys, StringComparer.Ordinal);

        foreach (var spec in MachineRoster.All)
        {
            var expected = MachineKitTable.For(spec.Id);
            var present = false;
            JsonElement kitNode = default;
            if (machines.TryGetProperty(spec.Id, out var node) && node.TryGetProperty("kit", out kitNode))
            {
                present = true;
            }

            if (spec.Id == MachineRoster.StandardId)
            {
                // 标准型不列分区：四条轴全基准，没有差异可写；列了反而暗示「标准型可以调出能力差异」
                Assert.False(present, "标准型不应有 machines.standard.kit 分区");
                continue;
            }

            Assert.True(present, $"balance.json 缺 machines.{spec.Id}.kit 分区");
            Assert.NotEmpty(kitNode.EnumerateObject());
            foreach (var property in kitNode.EnumerateObject())
            {
                Assert.True(known.Contains(property.Name), $"machines.{spec.Id}.kit.{property.Name} 不是已知能力键（拼写错误即死键）");
                Assert.Equal(FieldOf(expected, property.Name), property.Value.GetDouble(), 6);
            }

            // 两条差异轴必须在表里有键（与数值层同一条理由：调参界面按这些键取值，漏键不报错，
            // 只是「改了没反应」——而那要等玩家读不出性格才会发现）
            foreach (var axis in new[] { spec.KitBonus, spec.KitPenalty })
            {
                var field = MachineKitTable.ConfigKeyOf(axis);
                Assert.True(kitNode.TryGetProperty(field, out var value), $"balance.json 缺 machines.{spec.Id}.kit.{field}");
                Assert.Equal(FieldOf(expected, field), value.GetDouble(), 6);
            }
        }
    }

    /// <summary>四条轴的文案键必须在 <c>data/translations.csv</c> 中英两列存在：缺键玩家就会看到键名，
    /// 而轮盘铭牌是玩家唯一能读到机型能力差异的地方。</summary>
    [Fact]
    public void TextKeysExist()
    {
        var table = TranslationTable();
        foreach (var axis in new[] { MachineAxis.ParryWindow, MachineAxis.ParryCooldown, MachineAxis.DashCooldown, MachineAxis.FuelDrain })
        {
            var key = MachineKitTable.TextKey(axis);
            Assert.StartsWith("MACHINE_KIT_", key, StringComparison.Ordinal);
            Assert.True(table.TryGetValue(key, out var row), $"文案表缺键：{key}");
            Assert.False(string.IsNullOrWhiteSpace(row.Zh), $"{key} 缺中文");
            Assert.False(string.IsNullOrWhiteSpace(row.En), $"{key} 缺英文");
        }
    }

    // ---- 判据工具 ----

    /// <summary>五型特种型（标准型没有能力轴可比）。</summary>
    private static IEnumerable<MachineSpec> Specialists()
    {
        foreach (var spec in MachineRoster.All)
        {
            if (spec.KitBonus != MachineAxis.None)
            {
                yield return spec;
            }
        }
    }

    /// <summary>轴所属的**能力**（K1 判据的尺度）：弹反窗与弹反循环同属弹反——
    /// 一条能力的另一条轴作代价，两笔会在同一个尺度上相抵。</summary>
    private static string CapabilityOf(MachineAxis axis) => axis switch
    {
        MachineAxis.ParryWindow or MachineAxis.ParryCooldown => "parry",
        MachineAxis.DashCooldown => "dash",
        MachineAxis.FuelDrain => "fuel",
        _ => "none",
    };

    /// <summary>档案里该轴的乘区（轴与字段的映射只此一处，测试侧不另写一套）。</summary>
    private static double KitValue(MachineKit kit, MachineAxis axis) => axis switch
    {
        MachineAxis.ParryWindow => kit.ParryWindowMult,
        MachineAxis.ParryCooldown => kit.ParryCooldownMult,
        MachineAxis.DashCooldown => kit.DashCooldownMult,
        MachineAxis.FuelDrain => kit.FuelDrainMult,
        _ => throw new ArgumentException($"轴 {axis} 没有乘区"),
    };

    private static double Nat(string id, MachineAxis axis) =>
        MachineKitTable.NaturalQuantity(axis, MachineKitTable.For(id));

    /// <summary>与基准相比被改动的轴（顺序固定，便于断言）。</summary>
    private static List<MachineAxis> MovedAxes(MachineKit kit)
    {
        var moved = new List<MachineAxis>();
        if (kit.ParryWindowMult != 1.0) moved.Add(MachineAxis.ParryWindow);
        if (kit.ParryCooldownMult != 1.0) moved.Add(MachineAxis.ParryCooldown);
        if (kit.DashCooldownMult != 1.0) moved.Add(MachineAxis.DashCooldown);
        if (kit.FuelDrainMult != 1.0) moved.Add(MachineAxis.FuelDrain);
        return moved;
    }

    private static void AssertMachine(string id, MachineAxis bonusAxis, double bonus, MachineAxis penaltyAxis, double penalty)
    {
        var spec = MachineRoster.ById(id);
        Assert.Equal(id, spec.Id); // id 拼错会被静默归一成标准型：先钉住取到的确实是这台
        Assert.Equal(bonusAxis, spec.KitBonus);
        Assert.Equal(penaltyAxis, spec.KitPenalty);

        var kit = MachineKitTable.For(id);
        Assert.Equal(bonus, KitValue(kit, bonusAxis), 6);
        Assert.Equal(penalty, KitValue(kit, penaltyAxis), 6);
    }

    private static double FieldOf(MachineKit kit, string key) => key switch
    {
        MachineKitTable.ParryWindowKey => kit.ParryWindowMult,
        MachineKitTable.ParryCooldownKey => kit.ParryCooldownMult,
        MachineKitTable.DashCooldownKey => kit.DashCooldownMult,
        MachineKitTable.FuelDrainKey => kit.FuelDrainMult,
        _ => throw new ArgumentException($"未知能力键：{key}"),
    };

    private static JsonElement BalanceMachines()
    {
        var root = JsonDocument.Parse(RepoFiles.Read("data/balance.json")).RootElement;
        Assert.True(root.TryGetProperty("machines", out var machines), "balance.json 缺 machines 分区");
        return machines;
    }

    /// <summary>数值表里该型该轴的实际取值（缺失即显式失败：判据取不到源不得静默跳过）。</summary>
    private static double BalanceKitValue(JsonElement machines, MachineSpec spec, MachineAxis axis)
    {
        Assert.True(machines.TryGetProperty(spec.Id, out var node), $"balance.json 缺 machines.{spec.Id}");
        Assert.True(node.TryGetProperty("kit", out var kit), $"balance.json 缺 machines.{spec.Id}.kit 分区");
        var field = MachineKitTable.ConfigKeyOf(axis);
        Assert.True(kit.TryGetProperty(field, out var value), $"balance.json 缺 machines.{spec.Id}.kit.{field}");
        return value.GetDouble();
    }

    private readonly record struct CopyRow(string Zh, string En);

    /// <summary>读 `data/translations.csv`（RFC4180 子集：双引号包裹、字段内换行、`""` 转义）。
    /// 取不到即抛——取不到判据必须显式失败，不得静默跳过。</summary>
    private static Dictionary<string, CopyRow> TranslationTable()
    {
        var rows = new Dictionary<string, CopyRow>(StringComparer.Ordinal);
        foreach (var row in ParseCsv(RepoFiles.Read("data/translations.csv")))
        {
            if (row.Count >= 3 && row[0].Length > 0 && row[0] != "keys")
            {
                rows[row[0]] = new CopyRow(row[1], row[2]);
            }
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
