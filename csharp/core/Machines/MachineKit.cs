namespace InfiAir.Core.Machines;

/// <summary>
/// 机型**能力轴**（玩法层四条轴；每型一条强化 + 一条代价，标准型两条都是 <see cref="None"/>）。
///
/// 与 <see cref="MachineTrait"/> 同角色但不同层：那边是数值乘区（移速 / 攻击 / 射速 / 防守 / 血量），
/// 这边乘在**能力参数**上（弹反窗 / 弹反冷却 / 冲刺冷却 / 加速耗油）。
/// 轴与代价的分配口径见 <c>DESIGN_BASELINE</c> §1.19。
/// </summary>
public enum MachineAxis
{
    /// <summary>无能力轴（标准型的强化与代价都是它）。</summary>
    None,

    /// <summary>弹反激活窗（乘在 <c>player.parry.active_time</c>）。窗**大**有利。</summary>
    ParryWindow,

    /// <summary>弹反冷却（乘在 <c>player.parry.cooldown</c>）。循环**小**有利。</summary>
    ParryCooldown,

    /// <summary>冲刺冷却（乘在 <c>player.dash.cooldown</c>）。冷却**小**有利。</summary>
    DashCooldown,

    /// <summary>加速耗油（乘在 <c>player.fuel.drain</c>）。耗油**低**有利。</summary>
    FuelDrain,
}

/// <summary>
/// 机型能力档案（纯逻辑，零 Godot 依赖）：四条轴全是「乘在基准值上」的倍率，1.0 ＝ 基准 ＝ 标准型。
///
/// 与 <see cref="MachineFeel"/> 的分工：feel 只碰表现层，本档案只碰**能力参数**——
/// 四条轴各一处乘区，不动判定几何、动作时长、伤害与计分。
/// 量级锚：全部差异压在**亚一层增幅**之下（一层 <c>phase_dash</c> ×0.8 / <c>deflector</c> ×0.78 /
/// <c>efficient_boost</c> ×0.75），故「满投资」永远是支配读数（口径见 <c>DESIGN_BASELINE</c> §1.19）。
///
/// **四条轴的有利方向不是一个**（与 <see cref="MachineTraitText"/> 那处坑同源）：
/// 弹反窗**越大越有利**，其余三条**越小越有利**。方向判定只在
/// <see cref="MachineKitTable.IsBeneficial"/>，调用点不得自己比对大小。
/// </summary>
public readonly record struct MachineKit(
    double ParryWindowMult,
    double ParryCooldownMult,
    double DashCooldownMult,
    double FuelDrainMult)
{
    /// <summary>基准档案（标准型与「读不到配置」时的回退值）：四条轴全 1.0。
    /// 标准型一偏离 1.0，全表所有面板读数同时失真。</summary>
    public static readonly MachineKit Baseline = new(
        ParryWindowMult: 1.0,
        ParryCooldownMult: 1.0,
        DashCooldownMult: 1.0,
        FuelDrainMult: 1.0);
}

/// <summary>
/// 六型能力档案默认表 + 键名 + 域收口 + 轴元数据（结构同 <see cref="MachineFeelTable"/>：
/// **结构**（每型动哪两条轴、叫什么、域与回退）在此并配单测；**数值**单源在
/// <c>data/balance.json machines.&lt;id&gt;.kit.*</c>（缺键回退本表，与 <c>machines.*.feel.*</c> 同口径）。
///
/// 轴元数据（基准自然量 / 自然量百分比 / 有利方向 / 感知下限 / 可读单位）由**面板文案与账本判据共用**：
/// 两侧各算一遍必然分叉，而分叉不报错——面板印 3.4s、判据算的是 4.4s 这种坏法没有编译期信号。
/// </summary>
public static class MachineKitTable
{
    // ---- balance.json 键名（引擎层读配置用；键名只有这一处字符串）----

    public const string ParryWindowKey = "parry_window_mult";
    public const string ParryCooldownKey = "parry_cooldown_mult";
    public const string DashCooldownKey = "dash_cooldown_mult";
    public const string FuelDrainKey = "fuel_drain_mult";

    /// <summary>全部键（对账与遍历用；顺序无关）。</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        ParryWindowKey, ParryCooldownKey, DashCooldownKey, FuelDrainKey,
    };

    /// <summary>机型能力档案的配置路径（<c>machines.&lt;id&gt;.kit.&lt;field&gt;</c>；标准型不列分区）。</summary>
    public static string ConfigKey(MachineSpec spec, string field) =>
        "machines." + spec.Id + ".kit." + field;

    /// <summary>轴 → <c>machines.*.kit.*</c> 的字段名（<see cref="MachineAxis.None"/> 没有字段，回空串）。</summary>
    public static string ConfigKeyOf(MachineAxis axis) => axis switch
    {
        MachineAxis.ParryWindow => ParryWindowKey,
        MachineAxis.ParryCooldown => ParryCooldownKey,
        MachineAxis.DashCooldown => DashCooldownKey,
        MachineAxis.FuelDrain => FuelDrainKey,
        _ => string.Empty,
    };

    /// <summary>
    /// 名册默认档案（顺序与 <see cref="MachineRoster.All"/> 对齐，按 id 对账）。
    /// 每型的两条非基准轴即 §1.19 的强化 / 代价取值，未列轴 ＝ 基准：
    /// - 游隼：冲刺冷却 ×0.85（更快）、弹反循环 ×1.10（更慢）；
    /// - 重锤：加速耗油 ×0.87（推得久）、冲刺冷却 ×1.10（换向慢）；
    /// - 连弩：弹反循环 ×0.85（循环快）、加速耗油 ×1.15（续航薄）；
    /// - 壁垒：弹反窗 ×1.40（墙）、冲刺冷却 ×1.10（机动更钝）；
    /// - 巨像：加速耗油 ×0.82（大油箱）、弹反循环 ×1.15（一切循环都慢）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, MachineKit> Defaults = new Dictionary<string, MachineKit>(
        StringComparer.Ordinal)
    {
        [MachineRoster.StandardId] = MachineKit.Baseline,
        ["peregrine"] = MachineKit.Baseline with { DashCooldownMult = 0.85, ParryCooldownMult = 1.10 },
        ["sledge"] = MachineKit.Baseline with { FuelDrainMult = 0.87, DashCooldownMult = 1.10 },
        ["repeater"] = MachineKit.Baseline with { ParryCooldownMult = 0.85, FuelDrainMult = 1.15 },
        ["bulwark"] = MachineKit.Baseline with { ParryWindowMult = 1.40, DashCooldownMult = 1.10 },
        ["colossus"] = MachineKit.Baseline with { FuelDrainMult = 0.82, ParryCooldownMult = 1.15 },
    };

    /// <summary>id → 档案；未知与空串回退基准（与 <see cref="MachineRoster.ById"/> 同白名单口径）。</summary>
    public static MachineKit For(string? id) =>
        id != null && Defaults.TryGetValue(id, out var kit) ? kit : MachineKit.Baseline;

    // ---- 域 ----

    /// <summary>弹反窗域下限。</summary>
    public const double WindowMin = 0.4;

    /// <summary>弹反窗域上限 = **硬顶**：<c>ParryTimeline</c> 把激活时长钳到流程时长
    /// （<c>player.parry.duration</c> 0.8s），乘区进了 [1.6, 2.0] 是死区（改了没反应），故这里就收口。
    /// 设计上限更低（1.40 ＝ 0.70s，给前后摇留 0.1s）。</summary>
    public const double WindowMax = 1.5;

    /// <summary>其余三条轴的域下限。</summary>
    public const double MultMin = 0.4;

    /// <summary>其余三条轴的域上限。</summary>
    public const double MultMax = 2.0;

    /// <summary>
    /// 域收口：弹反窗钳 <c>[0.4, 1.5]</c>、其余三条钳 <c>[0.4, 2.0]</c>，
    /// **越界取边界值，不是回 1.0**——回 1.0 会把 &gt;1.0 的代价（重锤 / 巨像的耗油罚）静默抹平成
    /// 「这型没有代价」，那正是本表要防的坏法。非有限值（NaN / ±Inf）才逐轴回 1.0：
    /// NaN 渗进弹反窗或耗油会一路静默污染 HUD 与判定，而逐轴回退不会把同一份档案里合法的那三条一起丢掉
    /// （与 <see cref="MachineFeelTable.Sanitize"/> 的整份回退不同：那边 22 个字段是一套表现参数集，缺一个就没意义）。
    /// </summary>
    public static MachineKit Sanitize(MachineKit kit) => new(
        Clamp(kit.ParryWindowMult, WindowMin, WindowMax),
        Clamp(kit.ParryCooldownMult, MultMin, MultMax),
        Clamp(kit.DashCooldownMult, MultMin, MultMax),
        Clamp(kit.FuelDrainMult, MultMin, MultMax));

    // ---- 轴元数据（基准自然量 / 自然量 / 百分比 / 方向 / 感知下限 / RU / 文案键）----

    // 能力参数本身的单源是 balance.json（player.parry.* / player.dash.cooldown / player.fuel.*），
    // 下列常量只是**面板读数的基准**：改那边的取值必须同批改这里，否则面板印的秒数与玩家实感分叉。

    /// <summary>弹反流程时长 <c>player.parry.duration</c>（0.8s）：弹反循环＝本值＋冷却，
    /// 也是激活窗的饱和点。</summary>
    public const double ParryDurationBase = 0.8;

    /// <summary>弹反窗基准 <c>player.parry.active_time</c>（0.5s）。</summary>
    public const double ParryWindowBase = 0.5;

    /// <summary>弹反冷却基准 <c>player.parry.cooldown</c>（3.0s）。</summary>
    public const double ParryCooldownBase = 3.0;

    /// <summary>弹反循环基准（0.8 ＋ 3.0 ＝ 3.8s）：玩家数的是「下一次能按」的时间，
    /// 不是冷却字段本身——冷却乘区 ×1.10 读作循环 +8%，按冷却算会读到 +10%。</summary>
    public const double ParryCycleBase = ParryDurationBase + ParryCooldownBase;

    /// <summary>冲刺冷却基准 <c>player.dash.cooldown</c>（4.0s）。</summary>
    public const double DashCooldownBase = 4.0;

    /// <summary>满箱燃料基准 <c>player.fuel.max</c>（100）。</summary>
    public const double FuelMax = 100.0;

    /// <summary>加速耗油基准 <c>player.fuel.drain</c>（每秒 35）。</summary>
    public const double FuelDrainBase = 35.0;

    /// <summary>满箱可持续加速时长基准（100 ÷ 35 ≈ 2.857s）：“加速耗油”的自然量。</summary>
    public const double FuelDurationBase = FuelMax / FuelDrainBase;

    /// <summary>该轴在基准档案下的自然量（秒；<see cref="MachineAxis.None"/> 为 0）。
    /// **自然量＝玩家读这条轴时心里的那个量**，四条各不相同：
    /// 弹反窗＝窗口秒数；弹反循环＝流程＋冷却；冲刺冷却＝冷却秒数；加速耗油＝满箱加速时长（100 ÷ 耗油）。</summary>
    public static double BaselineQuantity(MachineAxis axis) => axis switch
    {
        MachineAxis.ParryWindow => ParryWindowBase,
        MachineAxis.ParryCooldown => ParryCycleBase,
        MachineAxis.DashCooldown => DashCooldownBase,
        MachineAxis.FuelDrain => FuelDurationBase,
        _ => 0.0,
    };

    /// <summary>该轴在该档案下的自然量（秒）——面板直接印这个数（§6.4：能力项写自然量，不写百分比与符号）。
    /// 弹反循环与加速耗油都**不是**基准量的简单倍数（一个是加常数、一个是倒数），故消费点不得自己乘。</summary>
    public static double NaturalQuantity(MachineAxis axis, MachineKit kit) => axis switch
    {
        MachineAxis.ParryWindow => ParryWindowBase * kit.ParryWindowMult,
        MachineAxis.ParryCooldown => ParryDurationBase + ParryCooldownBase * kit.ParryCooldownMult,
        MachineAxis.DashCooldown => DashCooldownBase * kit.DashCooldownMult,
        // 耗油乘区与续航成反比（100 ÷ (35 × mult)）；mult ≤ 0 只在未收口的输入上出现，
        // 回 0 而不是 ±Inf：Inf 会一路渗进百分比与 RU 判据
        MachineAxis.FuelDrain => kit.FuelDrainMult > 0.0 ? FuelDurationBase / kit.FuelDrainMult : 0.0,
        _ => 0.0,
    };

    /// <summary>该轴在该档案下的**自然量**变化百分比（整数；符号＝自然量的增减方向，不是强弱方向）。
    /// 由自然量反算而不是印乘区：弹反循环 ×1.10 是循环 +8%、耗油 ×0.82 是续航 +22%。
    /// 强弱看 <see cref="IsBeneficial"/>，两者分开才不会为了显示好看把方向判定也一起拧过去。</summary>
    public static int Percent(MachineAxis axis, MachineKit kit)
    {
        var baseline = BaselineQuantity(axis);
        if (baseline <= 0.0)
        {
            return 0;
        }

        var change = NaturalQuantity(axis, kit) / baseline - 1.0;
        return (int)Math.Round(change * 100.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>该轴在该档案下是否朝**对玩家有利**的方向偏（强化侧必须真、代价侧必须假）。
    /// 窗大有利，循环 / 冷却 / 耗油小有利——与 <see cref="Percent"/> 的符号不是一回事，混成一个必有一头判反。</summary>
    public static bool IsBeneficial(MachineAxis axis, MachineKit kit) => axis switch
    {
        MachineAxis.ParryWindow => kit.ParryWindowMult > 1.0,
        MachineAxis.ParryCooldown => kit.ParryCooldownMult < 1.0,
        MachineAxis.DashCooldown => kit.DashCooldownMult < 1.0,
        MachineAxis.FuelDrain => kit.FuelDrainMult < 1.0,
        _ => false,
    };

    /// <summary>该轴的感知下限（秒）：1 RU ＝「这条轴上玩家刚好能读出来的一次差异」。
    /// **不是实测值**（`DESIGN_BASELINE` §1.19 的读数分级）：由「输入缓冲 0.1s」与「一次冲刺＋缓冲」的量级推断而来，
    /// 校准前是可调常量——改这一处即可，不必重排六型分配。</summary>
    public static double PerceptionFloor(MachineAxis axis) => axis switch
    {
        MachineAxis.ParryWindow => 0.15,
        MachineAxis.ParryCooldown => 0.30,
        MachineAxis.DashCooldown => 0.40,
        MachineAxis.FuelDrain => 0.30,
        _ => 0.0,
    };

    /// <summary>可读单位（RU）＝自然量变化量 ÷ 该轴感知下限。
    /// 1 RU 以下只是账本差异、算不上取舍——「可读的代价才有损失厌恶」。</summary>
    public static double ReadabilityUnits(MachineAxis axis, MachineKit kit)
    {
        var floor = PerceptionFloor(axis);
        if (floor <= 0.0)
        {
            return 0.0;
        }

        return Math.Abs(NaturalQuantity(axis, kit) - BaselineQuantity(axis)) / floor;
    }

    /// <summary>该轴的文案键（面板的加成行与代价行共用一份）。<see cref="MachineAxis.None"/> 复用标准型那行：
    /// 标准型没有能力项可写，也不该为此新造一条只显示「无」的键。</summary>
    public static string TextKey(MachineAxis axis) => axis switch
    {
        MachineAxis.ParryWindow => "MACHINE_KIT_PARRY_WINDOW",
        MachineAxis.ParryCooldown => "MACHINE_KIT_PARRY_COOLDOWN",
        MachineAxis.DashCooldown => "MACHINE_KIT_DASH_COOLDOWN",
        MachineAxis.FuelDrain => "MACHINE_KIT_FUEL_DRAIN",
        _ => "MACHINE_TRAIT_STANDARD",
    };

    private static double Clamp(double value, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : 1.0;
}
