namespace InfiAir.Core.Machines;

/// <summary>
/// 机型手感档案（纯逻辑，零 Godot 依赖）：把机型轮盘特性演出的动作语汇搬进游戏内的
/// 表现层 / 手感层参数集（ROADMAP「设计稿 · 机型性格进游戏内」的实验落地）。
///
/// 与 <see cref="MachineModifiers"/> 同一口径的反面是它**不碰任何判定与玩法数值**：
/// 全部字段只乘在机体表现层（§2.13 姿态 / §2.16 活性 / §2.17 机身反馈 / §2.18 形态层）
/// 与命中反馈（顿帧 / 直击震动 / 火花尺度）的既有参数上——碰撞圆、擦弹环、弹道、
/// 机动 / 射速 / 伤害乘区（那五条在 MachineModifiers）逐位不动。
///
/// 乘区语义：1.0 ＝ 基准 ＝ 标准型；两处例外——<see cref="FireJitterPx"/> /
/// <see cref="DirectShake"/> / <see cref="TracerLenPx"/> / <see cref="DashSpeedline"/> 是
/// 「基准为 0、机型给正值才出现」的新增量（标准型与既有画面逐位一致），
/// <see cref="SmokeMinLevel"/> / <see cref="DeflectRead"/> 是档位与开关。
/// </summary>
public readonly record struct MachineFeel(
    double DashPopScaleMult,
    double SwayMult,
    double BankMult,
    double VortexThresholdMult,
    double RecoilPxMult,
    double HitImpactMult,
    double DirectShake,
    double LagPxMult,
    double FireLightMult,
    double FireJitterPx,
    double HitSquashMult,
    double ParryGlowMult,
    double BobPxMult,
    double AfterimageLifeMult,
    double AfterimageRateMult,
    double DashSpeedline,
    double MuzzleGlowMult,
    double HitSparkMult,
    double TracerLenPx,
    double DamageFrameMult,
    int SmokeMinLevel,
    double DeflectRead)
{
    /// <summary>基准档案（标准型与「读不到配置」时的回退值）：全部乘区 1.0、
    /// 新增量 0、档位保持既有口径（损伤烟仅重伤档、无被弹开读数）。</summary>
    public static readonly MachineFeel Baseline = new(
        DashPopScaleMult: 1.0,
        SwayMult: 1.0,
        BankMult: 1.0,
        VortexThresholdMult: 1.0,
        RecoilPxMult: 1.0,
        HitImpactMult: 1.0,
        DirectShake: 0.0,
        LagPxMult: 1.0,
        FireLightMult: 1.0,
        FireJitterPx: 0.0,
        HitSquashMult: 1.0,
        ParryGlowMult: 1.0,
        BobPxMult: 1.0,
        AfterimageLifeMult: 1.0,
        AfterimageRateMult: 1.0,
        DashSpeedline: 0.0,
        MuzzleGlowMult: 1.0,
        HitSparkMult: 1.0,
        TracerLenPx: 0.0,
        DamageFrameMult: 1.0,
        SmokeMinLevel: SmokeHeavyLevel,
        DeflectRead: 0.0);

    /// <summary>损伤烟 / 引擎喘振的既有档位（受击帧重伤档，§2.17 ④）。</summary>
    public const int SmokeHeavyLevel = 2;

    /// <summary>受击帧轻伤档（<c>Player.UpdateDamageFrame</c> 的三档口径）。</summary>
    public const int SmokeLightLevel = 1;

    /// <summary>被弹开读数的开关值（json 侧用 0/1 存）。</summary>
    public const double DeflectOn = 1.0;

    /// <summary>被弹开读数是否生效（消费点判此属性，不在调用点各自比对浮点）。</summary>
    public bool Deflect => DeflectRead >= DeflectOn * 0.5;
}

/// <summary>
/// 六型手感档案默认表 + 键名 + 域收口（结构与 <see cref="MachineRoster"/> 同口径：
/// **结构**（每型动哪几项、叫什么）在此并配单测；**数值**单源在
/// <c>data/balance.json machines.&lt;id&gt;.feel.*</c>（缺键回退本表）。
///
/// 默认表全落在域内（<see cref="MachineFeelTable.Sanitize"/> 后不变，单测钉住）；
/// 取值依据＝ROADMAP 设计稿逐条目 + 量级锚定 §2.13/§2.16/§2.17/§2.18 既有键（不越过同量级线）。
/// </summary>
public static class MachineFeelTable
{
    // ---- balance.json 键名（引擎层读配置用；键名只有这一处字符串）----

    public const string DashPopScaleKey = "dash_pop_scale_mult";
    public const string SwayKey = "sway_mult";
    public const string BankKey = "bank_mult";
    public const string VortexThresholdKey = "vortex_threshold_mult";
    public const string RecoilPxKey = "recoil_px_mult";
    public const string HitImpactKey = "hit_impact_mult";
    public const string DirectShakeKey = "direct_shake";
    public const string LagPxKey = "lag_px_mult";
    public const string FireLightKey = "fire_light_mult";
    public const string FireJitterKey = "fire_jitter_px";
    public const string HitSquashKey = "hit_squash_mult";
    public const string ParryGlowKey = "parry_glow_mult";
    public const string BobPxKey = "bob_px_mult";
    public const string AfterimageLifeKey = "afterimage_life_mult";
    public const string AfterimageRateKey = "afterimage_rate_mult";
    public const string DashSpeedlineKey = "dash_speedline";
    public const string MuzzleGlowKey = "muzzle_glow_mult";
    public const string HitSparkKey = "hit_spark_mult";
    public const string TracerLenKey = "tracer_len_px";
    public const string DamageFrameKey = "damage_frame_mult";
    public const string SmokeMinLevelKey = "smoke_min_level";
    public const string DeflectReadKey = "deflect_read";

    /// <summary>全部键（对账与遍历用；顺序无关）。</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        DashPopScaleKey, SwayKey, BankKey, VortexThresholdKey, RecoilPxKey, HitImpactKey,
        DirectShakeKey, LagPxKey, FireLightKey, FireJitterKey, HitSquashKey, ParryGlowKey,
        BobPxKey, AfterimageLifeKey, AfterimageRateKey, DashSpeedlineKey, MuzzleGlowKey,
        HitSparkKey, TracerLenKey, DamageFrameKey, SmokeMinLevelKey, DeflectReadKey,
    };

    /// <summary>机型手感档案的配置路径（<c>machines.&lt;id&gt;.feel.&lt;field&gt;</c>；标准型不列键）。</summary>
    public static string ConfigKey(MachineSpec spec, string field) =>
        "machines." + spec.Id + ".feel." + field;

    /// <summary>
    /// 名册默认档案（顺序与 <see cref="MachineRoster.All"/> 对齐，按 id 对账）。
    /// 每型的差异化字段即 ROADMAP 设计稿该型的 ①②层条目，未列字段 ＝ 基准：
    /// - 游隼：残影更密更长、冲刺速度线、冲刺弹跳更大、转向更「飘」、涡流更早；
    /// - 重锤：炮口辉光更大、弹着火花更重、后坐更大、命中顿帧更强 + 直击震动、加减速更沉；
    /// - 连弩：弹道曳光、炮口闪稍大、机身光更密、连射机身微抖；
    /// - 壁垒：受击读作「被弹开」、损伤帧更晚、受击回弹更小、弹反环更亮、机动更「钝」；
    /// - 巨像：损伤帧更早 + 损伤烟提前到轻伤档、加速更沉、受击压缩更小、悬停更稳。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, MachineFeel> Defaults = new Dictionary<string, MachineFeel>(
        StringComparer.Ordinal)
    {
        [MachineRoster.StandardId] = MachineFeel.Baseline,
        ["peregrine"] = MachineFeel.Baseline with
        {
            DashPopScaleMult = 1.5, SwayMult = 1.4, VortexThresholdMult = 0.6,
            AfterimageLifeMult = 1.6, AfterimageRateMult = 1.5, DashSpeedline = 0.85,
        },
        ["sledge"] = MachineFeel.Baseline with
        {
            RecoilPxMult = 1.6, HitImpactMult = 1.35, DirectShake = 5.0,
            LagPxMult = 1.5, MuzzleGlowMult = 1.45, HitSparkMult = 1.4,
        },
        ["repeater"] = MachineFeel.Baseline with
        {
            FireLightMult = 1.45, FireJitterPx = 1.2, TracerLenPx = 22.0, MuzzleGlowMult = 1.15,
        },
        ["bulwark"] = MachineFeel.Baseline with
        {
            SwayMult = 0.55, BankMult = 0.55, HitSquashMult = 0.5, ParryGlowMult = 1.35,
            DamageFrameMult = 0.7, DeflectRead = MachineFeel.DeflectOn,
        },
        ["colossus"] = MachineFeel.Baseline with
        {
            LagPxMult = 1.45, HitSquashMult = 0.7, BobPxMult = 0.5,
            DamageFrameMult = 1.35, SmokeMinLevel = MachineFeel.SmokeLightLevel,
        },
    };

    /// <summary>id → 档案；未知与空串回退基准（与 <see cref="MachineRoster.ById"/> 同白名单口径）。</summary>
    public static MachineFeel For(string? id) =>
        id != null && Defaults.TryGetValue(id, out var feel) ? feel : MachineFeel.Baseline;

    /// <summary>域下限：低于它 «更沉 / 更稳 / 更钝» 的档位会与基准读不出差（可读性下限）。</summary>
    public const double MultFloor = 0.2;

    /// <summary>域上限：高于它表现量级翻三倍，会从「性格」读成「换了一架飞机」。</summary>
    public const double MultCeiling = 3.0;

    /// <summary>
    /// 域收口：乘区钳 [0.2, 3.0]、绝对量钳到各自合法域（抖动 / 曳光 / 震动非负、
    /// 损伤帧倍率避开换挡抖动带、烟档只许 1..2、开关 0/1）。非有限值一律回基准——
    /// NaN 渗进表现参数会沿变换链静默污染整棵子树。
    /// </summary>
    public static MachineFeel Sanitize(MachineFeel feel)
    {
        if (!IsFinite(feel))
        {
            return MachineFeel.Baseline;
        }

        return feel with
        {
            DashPopScaleMult = ClampMult(feel.DashPopScaleMult),
            SwayMult = ClampMult(feel.SwayMult),
            BankMult = ClampMult(feel.BankMult),
            VortexThresholdMult = ClampMult(feel.VortexThresholdMult),
            RecoilPxMult = ClampMult(feel.RecoilPxMult),
            HitImpactMult = ClampMult(feel.HitImpactMult),
            DirectShake = Clamp(feel.DirectShake, 0.0, 12.0),
            LagPxMult = ClampMult(feel.LagPxMult),
            FireLightMult = ClampMult(feel.FireLightMult),
            FireJitterPx = Clamp(feel.FireJitterPx, 0.0, 4.0),
            HitSquashMult = ClampMult(feel.HitSquashMult),
            ParryGlowMult = ClampMult(feel.ParryGlowMult),
            BobPxMult = ClampMult(feel.BobPxMult),
            AfterimageLifeMult = ClampMult(feel.AfterimageLifeMult),
            AfterimageRateMult = ClampMult(feel.AfterimageRateMult),
            DashSpeedline = Clamp(feel.DashSpeedline, 0.0, 1.0),
            MuzzleGlowMult = ClampMult(feel.MuzzleGlowMult),
            HitSparkMult = ClampMult(feel.HitSparkMult),
            TracerLenPx = Clamp(feel.TracerLenPx, 0.0, 48.0),
            // 损伤帧倍率钳进 (0.5, 2]：过小会把轻伤/重伤两条阈值挤进同一窄带，档位来回抖
            DamageFrameMult = Clamp(feel.DamageFrameMult, 0.5, 2.0),
            SmokeMinLevel = Math.Clamp(feel.SmokeMinLevel, MachineFeel.SmokeLightLevel, MachineFeel.SmokeHeavyLevel),
            DeflectRead = feel.DeflectRead >= MachineFeel.DeflectOn * 0.5 ? MachineFeel.DeflectOn : 0.0,
        };
    }

    private static bool IsFinite(MachineFeel f) =>
        double.IsFinite(f.DashPopScaleMult) && double.IsFinite(f.SwayMult) && double.IsFinite(f.BankMult)
        && double.IsFinite(f.VortexThresholdMult) && double.IsFinite(f.RecoilPxMult) && double.IsFinite(f.HitImpactMult)
        && double.IsFinite(f.DirectShake) && double.IsFinite(f.LagPxMult) && double.IsFinite(f.FireLightMult)
        && double.IsFinite(f.FireJitterPx) && double.IsFinite(f.HitSquashMult) && double.IsFinite(f.ParryGlowMult)
        && double.IsFinite(f.BobPxMult) && double.IsFinite(f.AfterimageLifeMult) && double.IsFinite(f.AfterimageRateMult)
        && double.IsFinite(f.DashSpeedline) && double.IsFinite(f.MuzzleGlowMult) && double.IsFinite(f.HitSparkMult)
        && double.IsFinite(f.TracerLenPx) && double.IsFinite(f.DamageFrameMult) && double.IsFinite(f.DeflectRead);

    private static double ClampMult(double v) => Math.Clamp(v, MultFloor, MultCeiling);

    private static double Clamp(double v, double min, double max) => Math.Clamp(v, min, max);
}
