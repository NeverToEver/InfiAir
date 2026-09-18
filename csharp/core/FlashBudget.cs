namespace InfiAir.Core;

/// <summary>全屏尺度脉冲源的标识（枚举顺序即 <see cref="FlashBudget.Sources"/> 的行序）。</summary>
public enum PulseId
{
    /// <summary>HUD 低燃料警戒脉冲。</summary>
    HudLowFuel,

    /// <summary>HUD 低血晕影脉动（MetaFX LOD 非 0 时的回退路径）。</summary>
    HudLowHpVignette,

    /// <summary>Meta 濒死心跳。</summary>
    MetaHeartbeat,

    /// <summary>Meta 濒死警告晕影。</summary>
    MetaDyingWarn,

    /// <summary>迷雾「精神错乱」全屏变色覆盖层。</summary>
    FogConfusion,

    /// <summary>背景星场亮星闪烁。</summary>
    StarfieldTwinkle,

    /// <summary>全屏慢呼吸（`effects.motion`：按危险度调频的整屏明暗起伏，由 WorldPostFx 一处驱动）。</summary>
    WorldBreath,
}

/// <summary>一次性闪光的标识（枚举顺序即 <see cref="FlashBudget.OneShots"/> 的行序）。</summary>
public enum OneShotFlashId
{
    /// <summary>能力槽（相位冲刺 / 弧光弹反）由未就绪转就绪瞬间的外扩脉冲。</summary>
    AbilityReadyPulse,

    /// <summary>血条掉段闪：Boss 掉血时指向刚被消耗的那一段。</summary>
    HealthBarSegment,

    /// <summary>Boss 阶段切换瞬间的整条血条提亮。</summary>
    BossPhase,
}

/// <summary>一次性闪光登记行：无频率可言（不进 <see cref="FlashBudget.Sources"/>），但同样受「减少闪光」约束。</summary>
/// <param name="Id">枚举下标即表序（单测钉住两者对齐，防插入行时错位）。</param>
/// <param name="SuppressedByReduceFlash">减少闪光下是否抑制——本表全部为真，加例外须同时改单测。</param>
/// <param name="Note">为什么这样登记（核对时比 Id 本身重要）：抑制后哪一半读数仍在。</param>
public readonly record struct OneShotFlash(
    OneShotFlashId Id,
    bool SuppressedByReduceFlash,
    string Note);

/// <summary>登记行：频率一律以 Hz 表达（周期型来源在表里换算后登记）。</summary>
/// <param name="Id">枚举下标即表序（单测钉住两者对齐，防插入行时错位）。</param>
/// <param name="Hz">该源的最高闪烁频率。</param>
/// <param name="ZeroUnderReduceFlash">减少闪光下振幅是否归零——本表全部为真，加例外须同时改单测。</param>
/// <param name="Origin">取值来源（`core` 常量 / `balance` 键），供人核对。</param>
/// <param name="BalanceKey">来源是 balance 时的键路径（空串表示核心常量）；单测按它回读 balance 交叉判定，防两处漂移。</param>
/// <param name="BalanceIsPeriod">balance 值是否为**周期**（秒）而非频率——是则单测按 1/周期 比较。</param>
/// <param name="Note">为什么这样登记（核对时比 Id 本身重要）。</param>
public readonly record struct PulseSource(
    PulseId Id,
    float Hz,
    bool ZeroUnderReduceFlash,
    string Origin,
    string BalanceKey,
    bool BalanceIsPeriod,
    string Note);

/// <summary>
/// 闪烁预算（无障碍）：把**全屏尺度**脉冲源的频率与「减少闪光」处置收成一份登记表，
/// 并给出生产与单测共用的准入判据。
///
/// 为什么有这一份（AGENTS §1 单源）：`DESIGN_BASELINE` §1.9.1 声称「闪烁频率全部低于
/// WCAG 2.3.1 阈值」，而改动前全库唯一能直接读到的频率常数是一条 `private const`
/// （`Hud.FuelPulseHz = 2.4f`）——改一行即超阈值，门禁、单测、探针却全绿：判据只活在散文里。
/// 现在频率要么取自本表（`core` 行），要么由单测**回读 balance 交叉判定**（`balance` 行），
/// 两半互补的断言是：全屏尺度项一律低于 <see cref="HzLimit"/>，且经 <see cref="Amplitude"/>
/// 在减少闪光下振幅为零——只判前者会让「一律照闪」混过，只判后者会让「一律不闪」混过。
///
/// 边界（判据只在这一面成立，不要外推）：
///   - 按 WCAG 2.3.1，阈值约束的是**大面积**闪烁，故精灵与局部 UI 的短时高频频闪不在本表——
///     玩家无敌帧闪烁、Boss 逃跑期机体闪烁、瞄准线高频抖动、编队炸弹弹头、刷怪预警圈、
///     激光辉光、轮盘开机物化频闪都属面积豁免之列（它们各自受不受「减少闪光」约束，见各自实现）。
///     **拍点脉冲同理不入本表**：每拍的脉冲一律下放局部元素（弹体尾部 / 舰体流光 / 仪表 /
///     亮星，各自屏占远低于 20%），面积判据不适用——本轮动效里只有**全屏呼吸**占屏够大，
///     故只有它进本表（判定线就在面积上，不在「是不是脉冲」上）。
///   - 标题屏的远景爆炸、光带与按键提示是「待人类裁定」条目的处置对象（见 `docs/ROADMAP.md`），
///     也不是本表的判据面。
///   - 表只登记**周期型**脉冲；一次性闪光（就绪脉冲、血条掉段闪、Boss 阶段闪）无频率可言，
///     不进频率表——但它们的减闪门控同样单源在本类（<see cref="AllowsOneShot"/>），
///     三个站点各自引它，判据面是「core 单测的两半互补 + 探针按正反两半各采样一次」。
/// </summary>
public static class FlashBudget
{
    /// <summary>全屏尺度闪烁的频率上限（Hz）：XAG 118 / WCAG 2.3.1 通用闪烁阈值的三条判据之一
    /// （另两条是面积与时长，本表只覆盖频率这一条），出处见 `docs/REFERENCES.md`。</summary>
    public const float HzLimit = 3.0f;

    /// <summary>HUD 低燃料警戒脉冲频率（Hz）。</summary>
    public const float LowFuelHz = 2.4f;

    /// <summary>HUD 低血晕影脉动频率（Hz）：由 balance 的周期 1.2s 换算。</summary>
    public const float LowHpVignetteHz = 1.0f / 1.2f;

    /// <summary>迷雾「精神错乱」覆盖层呼吸频率（Hz）：生产以 1.0s 周期表达。</summary>
    public const float ConfusionHz = 1.0f;

    /// <summary>背景星场亮星闪烁频率（Hz）：生产以 2.1 rad/s 的正弦相位推进表达。</summary>
    public const float StarfieldTwinkleHz = 2.1f / 6.2831855f;

    /// <summary>登记表：行序与 <see cref="PulseId"/> 枚举一致（单测钉住），一行一个全屏尺度脉冲源。</summary>
    public static readonly PulseSource[] Sources =
    {
        new(Id: PulseId.HudLowFuel, Hz: LowFuelHz, ZeroUnderReduceFlash: true,
            Origin: "core", BalanceKey: "", BalanceIsPeriod: false,
            Note: "低燃料警戒的整槽亮度泵动；归零后静置全亮"),
        new(Id: PulseId.HudLowHpVignette, Hz: LowHpVignetteHz, ZeroUnderReduceFlash: true,
            Origin: "balance", BalanceKey: "effects.low_hp.pulse_period", BalanceIsPeriod: true,
            Note: "MetaFX 离场/回退时的低血晕影脉动；周期型来源在表里换算成 Hz"),
        new(Id: PulseId.MetaHeartbeat, Hz: 1.2f, ZeroUnderReduceFlash: true,
            Origin: "balance", BalanceKey: "effects.meta_health.dying.heart_max_hz", BalanceIsPeriod: false,
            Note: "濒死心跳包络的上界（伤害越高越快，下限 1.0Hz）"),
        new(Id: PulseId.MetaDyingWarn, Hz: 2.5f, ZeroUnderReduceFlash: true,
            Origin: "balance", BalanceKey: "effects.meta_health.dying.warn_hz", BalanceIsPeriod: false,
            Note: "濒死警告晕影：全屏尺度里最接近阈值的一项"),
        new(Id: PulseId.FogConfusion, Hz: ConfusionHz, ZeroUnderReduceFlash: true,
            Origin: "core", BalanceKey: "", BalanceIsPeriod: false,
            Note: "迷雾覆盖层的整屏明暗呼吸"),
        new(Id: PulseId.StarfieldTwinkle, Hz: StarfieldTwinkleHz, ZeroUnderReduceFlash: true,
            Origin: "core", BalanceKey: "", BalanceIsPeriod: false,
            Note: "背景星场亮星闪烁；归零取均值常亮，不改平均亮度"),
        new(Id: PulseId.WorldBreath, Hz: 1.2f, ZeroUnderReduceFlash: true,
            Origin: "balance", BalanceKey: "effects.motion.breath_hz_max", BalanceIsPeriod: false,
            Note: "全屏慢呼吸的频率上限（危险度越高越贴近它，下限见 breath_hz_min）；"
                + "峰谷亮度差另受 core Rhythm 的硬线钳制，拍点脉冲不入本表"),
    };

    /// <summary>登记行（下标即枚举值）。</summary>
    public static PulseSource Source(PulseId id) => Sources[(int)id];

    /// <summary>一次性闪光登记表：行序与 <see cref="OneShotFlashId"/> 枚举一致（单测钉住）。</summary>
    public static readonly OneShotFlash[] OneShots =
    {
        new(Id: OneShotFlashId.AbilityReadyPulse, SuppressedByReduceFlash: true,
            Note: "就绪仍以字形点亮与满环表达，抑制后读数不丢"),
        new(Id: OneShotFlashId.HealthBarSegment, SuppressedByReduceFlash: true,
            Note: "掉的是哪一段仍由残影段表达"),
        new(Id: OneShotFlashId.BossPhase, SuppressedByReduceFlash: true,
            Note: "名牌与阶段标签照常刷新，只停整条提亮"),
    };

    /// <summary>登记行（下标即枚举值）。</summary>
    public static OneShotFlash OneShot(OneShotFlashId id) => OneShots[(int)id];

    /// <summary>一次性闪光是否放行——生产的唯一判据口（三个站点各自引它，不留本地布尔副本）。
    /// 两半互补：只有「非减闪一律放行」会让写反的实现混过，只有「减闪下一律抑制」会让
    /// 「一律不闪」混过，故单测两半都判。</summary>
    public static bool AllowsOneShot(OneShotFlashId id, bool reduceFlash)
        => !reduceFlash || !OneShot(id).SuppressedByReduceFlash;

    /// <summary>振幅预算：减少闪光下，登记为「归零」的脉冲源一律返回 0，其余原样返回。
    /// 生产把各自的振幅经这里过一道——「开关认了、某处脉冲没认」这类静默残留就只剩一处可写。</summary>
    public static float Amplitude(float baseAmplitude, PulseId id, bool reduceFlash)
        => reduceFlash && Source(id).ZeroUnderReduceFlash ? 0.0f : baseAmplitude;
}
