namespace InfiAir.Core.Machines;

/// <summary>贴图素材取哪一帧（与 <c>Player.UpdateDamageFrame</c> 的三档一致）。</summary>
public enum HullDamageFrame
{
    /// <summary>完好帧。</summary>
    Normal = 0,

    /// <summary>轻伤帧（HP 比例 ≤ light_ratio）。</summary>
    Light = 1,

    /// <summary>重伤帧（HP 比例 ≤ heavy_ratio）。</summary>
    Heavy = 2,
}

/// <summary>机型加成的那一项（决定面板文案键与百分比反算方向；每型只有一项，标准型为 None）。</summary>
public enum MachineTrait
{
    /// <summary>无加成（基准配置）。</summary>
    None,

    /// <summary>移动速度。</summary>
    MoveSpeed,

    /// <summary>攻击力（每发伤害）。</summary>
    Damage,

    /// <summary>攻击速度（开火间隔的倒数）。</summary>
    FireRate,

    /// <summary>防御（受到伤害的减免）。</summary>
    Defense,

    /// <summary>血量上限。</summary>
    MaxHealth,
}

/// <summary>
/// 机型乘区（纯逻辑，零 Godot 依赖）：全部是「乘在基准值上」的倍率，1.0 ＝ 基准。
///
/// 四项进攻/生存乘区的语义各不相同，别把方向记混——<see cref="FireIntervalMult"/> 与
/// <see cref="DamageTakenMult"/> 是**越小越强**（间隔越短、挨打越轻），另两项越大越强；
/// 面板文案的百分比由 <see cref="MachineTraitText.Percent"/> 按各自方向反算，不在这里手写。
/// </summary>
public readonly record struct MachineModifiers(
    double MoveSpeedMult,
    double DamageMult,
    double FireIntervalMult,
    double DamageTakenMult,
    double MaxHpMult)
{
    /// <summary>基准乘区（全 1.0）：标准型与「读不到配置」时的回退值。</summary>
    public static readonly MachineModifiers Baseline = new(1.0, 1.0, 1.0, 1.0, 1.0);

    /// <summary>攻击速度倍率（开火间隔的倒数）——供展示与判据读取，不参与写入路径。</summary>
    public double FireRateMult => FireIntervalMult > 0.0 ? 1.0 / FireIntervalMult : 1.0;
}

/// <summary>
/// 机型加成 / 代价的文案与方向判定：轴 → 文案键、轴 + 实际乘区 → 自然量百分比 / 是否有利。
/// **同一根轴只写一条文案键**（键里不带正负号），符号由 <see cref="SignedPercent"/> 从实际乘区反算——
/// 写死符号的代价键在数值被调成反向时会与面板显示打架，而那是不会报错的那种坏法。
/// 代价必须落在与加成不同的轴上，故两轴各读各的键、互不覆盖。
/// </summary>
public static class MachineTraitText
{
    /// <summary>加成项的文案键（面板行与描述行共用一份）。</summary>
    public static string Key(MachineTrait trait) => trait switch
    {
        MachineTrait.MoveSpeed => "MACHINE_TRAIT_SPEED",
        MachineTrait.Damage => "MACHINE_TRAIT_DAMAGE",
        MachineTrait.FireRate => "MACHINE_TRAIT_RATE",
        MachineTrait.Defense => "MACHINE_TRAIT_DEFENSE",
        MachineTrait.MaxHealth => "MACHINE_TRAIT_HEALTH",
        _ => "MACHINE_TRAIT_STANDARD",
    };

    /// <summary>
    /// 该轴的**自然量**变化百分比（整数）：由实际生效的乘区反算，不读表里的定稿值——
    /// 数值被调过之后面板上的数字必须跟着走，否则玩家照着「+15%」去算却得不到那个结果。
    /// 「自然量」＝玩家读这一轴时心里的那个量：速度 / 伤害 / 血量就是乘区本身，
    /// 攻击速度是**开火间隔的倒数**（间隔 ×0.85 读作「攻击速度 +18%」），
    /// 受到伤害是**伤害乘区本身**（×0.85 读作「受到伤害 -15%」——「-」在这里是少挨打，不是变弱）。
    /// **符号只表达自然量的增减，不表达强弱**：强弱判定见 <see cref="IsBeneficial"/>，
    /// 两者分开，才不会为了显示好看把方向判定也一起拧过去。
    /// </summary>
    public static int Percent(MachineTrait trait, MachineModifiers mods)
    {
        var value = trait switch
        {
            MachineTrait.MoveSpeed => mods.MoveSpeedMult - 1.0,
            MachineTrait.Damage => mods.DamageMult - 1.0,
            MachineTrait.FireRate => mods.FireRateMult - 1.0,
            MachineTrait.Defense => mods.DamageTakenMult - 1.0,
            MachineTrait.MaxHealth => mods.MaxHpMult - 1.0,
            _ => 0.0,
        };

        return (int)Math.Round(value * 100.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>带符号的百分比（供 `%s` 文案补参）：`+15%` / `-10%` / `+0%`。
    /// 符号即 <see cref="Percent"/> 的符号，故面板上的正负永远等于该轴自然量的实际变化。</summary>
    public static string SignedPercent(MachineTrait trait, MachineModifiers mods)
    {
        var percent = Percent(trait, mods);
        return (percent < 0 ? "-" : "+") + Math.Abs(percent) + "%";
    }

    /// <summary>
    /// 该轴此刻是否朝**对玩家有利**的方向偏（加成必须是，代价必须不是）。
    /// 方向按轴而定：速度 / 伤害 / 血量递增为有利，开火间隔与受到伤害递减为有利。
    /// 与 <see cref="Percent"/> 的符号**不是一回事**——受到伤害轴上的 `-15%` 是有利（少挨打），
    /// 攻击速度轴上的 `-11%` 是不利（打得慢）；把两者混成一个符号，必有一头显示反。
    /// </summary>
    public static bool IsBeneficial(MachineTrait trait, MachineModifiers mods) => trait switch
    {
        MachineTrait.MoveSpeed => mods.MoveSpeedMult > 1.0,
        MachineTrait.Damage => mods.DamageMult > 1.0,
        MachineTrait.FireRate => mods.FireIntervalMult < 1.0,
        MachineTrait.Defense => mods.DamageTakenMult < 1.0,
        MachineTrait.MaxHealth => mods.MaxHpMult > 1.0,
        _ => false,
    };
}

/// <summary>
/// 机型定义：「id → 文案键 / 贴图名 / 加成项 / 代价项」的映射只写在这里（数值另在 <c>balance.json machines.*</c>）。
///
/// 分两处放的理由与全库同口径：**结构**（有哪几型、每型动哪两项、叫什么、用哪套贴图）属纯逻辑，
/// 与 Godot 无关且要被单测钉住；**数值**属可调项，单源在 `data/balance.json`，
/// 由引擎层按 <see cref="Trait"/> / <see cref="Penalty"/> 对应的键读取覆盖（读不到即回退本表的默认乘区）。
///
/// **每型一项加成 + 一项代价**（标准型两者皆无）：代价必须落在与加成**不同的轴**上，
/// 否则两笔相抵只剩中间值，玩家读不到「这是一架什么样的飞机」。
/// </summary>
public sealed record MachineSpec(
    string Id,
    string NameKey,
    MachineTrait Trait,
    MachineTrait Penalty,
    MachineModifiers Defaults)
{
    /// <summary>贴图名主干：标准型沿用既有文件名，特种型一律 <c>player_ship_&lt;id&gt;</c>。</summary>
    public string SpriteStem => Id == MachineRoster.StandardId ? "player_ship" : "player_ship_" + Id;
}

/// <summary>
/// 初始机型名册（纯逻辑，零 Godot 依赖）：六型 = 标准型（无加成、无代价，沿用既有外观与既有数值）
/// + 五型特种型（**各一项加成 + 各一项代价**，各一套外观）。
///
/// **为什么必须有标准型**：机型是开局前的一次选择，而它不能把既有玩家的默认体验改掉——
/// 旧档（settings.json / run.json）里没有机型键，一律归一到标准型，行为与加机型之前逐位一致；
/// 同时它给五型加成提供可读的基准点（面板上「+15%」是相对什么而言的）。
///
/// 数值取向见 <c>docs/DESIGN_BASELINE.md</c> §1.17，行业依据见 <c>docs/REFERENCES.md</c> §4.15 / §4.16。
/// </summary>
public static class MachineRoster
{
    /// <summary>标准型 id：旧档缺键、未知 id、空串一律归一到它（白名单口径，不设非法态）。</summary>
    public const string StandardId = "standard";

    /// <summary>
    /// 名册（顺序 ＝ 面板行的顺序，索引稳定）：标准型在首位 ＝ 默认选中项。
    /// 乘区默认值须与 <c>data/balance.json machines.&lt;id&gt;.*</c> 一致（两侧分叉时 json 完整则看不出来，
    /// 只有 json 缺失/损坏才回退到错值——改一侧必须同时改另一侧，见 AGENTS.md §3）。
    ///
    /// 代价的取值规则（依据见 <c>REFERENCES</c> §4.16）：**无条件**、与加成**不同轴**、
    /// 数值幅度约为加成的 0.5–0.7 倍——损失在体感上约为等量收益的两倍，1:1 的数值交换体感是亏的；
    /// 而象征性的小代价又抵不住收益（能力预算不成立）。三条都由 <c>MachineRosterTests</c> 钉住。
    /// </summary>
    public static readonly IReadOnlyList<MachineSpec> All = new MachineSpec[]
    {
        new(StandardId, "MACHINE_NAME_STANDARD", MachineTrait.None, MachineTrait.None, MachineModifiers.Baseline),
        new("peregrine", "MACHINE_NAME_PEREGRINE", MachineTrait.MoveSpeed, MachineTrait.Damage,
            new MachineModifiers(1.15, 0.9, 1.0, 1.0, 1.0)),
        new("sledge", "MACHINE_NAME_SLEDGE", MachineTrait.Damage, MachineTrait.MoveSpeed,
            new MachineModifiers(0.9, 1.2, 1.0, 1.0, 1.0)),
        new("repeater", "MACHINE_NAME_REPEATER", MachineTrait.FireRate, MachineTrait.Defense,
            new MachineModifiers(1.0, 1.0, 0.85, 1.12, 1.0)),
        new("bulwark", "MACHINE_NAME_BULWARK", MachineTrait.Defense, MachineTrait.FireRate,
            new MachineModifiers(1.0, 1.0, 1.12, 0.85, 1.0)),
        new("colossus", "MACHINE_NAME_COLOSSUS", MachineTrait.MaxHealth, MachineTrait.MoveSpeed,
            new MachineModifiers(0.88, 1.0, 1.0, 1.0, 1.2)),
    };

    /// <summary>机型数（面板行数与环形切换的模数）。</summary>
    public static int Count => All.Count;

    /// <summary>默认机型（标准型，名册首位）。</summary>
    public static MachineSpec Default => All[0];

    /// <summary>索引 → 机型（越界取模回正：面板环形切换与命令行两路来源共用，不设非法索引语义）。</summary>
    public static MachineSpec At(int index)
    {
        var wrapped = index % Count;
        return All[wrapped < 0 ? wrapped + Count : wrapped];
    }

    /// <summary>id → 机型；未知与空串回退默认（**这就是旧档兼容的那一条**：读不出即基准型）。</summary>
    public static MachineSpec ById(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return Default;
        }

        foreach (var spec in All)
        {
            if (string.Equals(spec.Id, id, StringComparison.Ordinal))
            {
                return spec;
            }
        }

        return Default;
    }

    /// <summary>id → 名册索引（未知 id 归默认型的索引）。</summary>
    public static int IndexOf(string? id)
    {
        var target = ById(id).Id;
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Id, target, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>环形切换（面板与手柄方向键共用；步长可负）。</summary>
    public static MachineSpec Cycle(string? id, int step) => At(IndexOf(id) + step);

    /// <summary>主贴图路径（<paramref name="frame"/> 选受击帧）。</summary>
    public static string SpritePath(MachineSpec spec, HullDamageFrame frame = HullDamageFrame.Normal) => frame switch
    {
        HullDamageFrame.Light => Res(spec.SpriteStem + "_hit_1.png"),
        HullDamageFrame.Heavy => Res(spec.SpriteStem + "_hit_2.png"),
        _ => Res(spec.SpriteStem + ".png"),
    };

    /// <summary>能量发光遮罩路径（R=走线 / B=引擎，供 <c>ship_energy.gdshader</c> 叠加）。
    /// 命名由主贴图主干派生，与 <c>ShipEnergyFx.GlowTextureFor</c> 的推导口径一致。</summary>
    public static string GlowSpritePath(MachineSpec spec) => Res(spec.SpriteStem + "_glow.png");

    /// <summary>主贴图路径（按 id，未知 id 回退默认型）。</summary>
    public static string SpritePath(string? id, HullDamageFrame frame = HullDamageFrame.Normal) => SpritePath(ById(id), frame);

    // ---- balance.json 键名（引擎层读配置用；键名只有这一处字符串，避免调用点各写一遍）----

    public const string MoveSpeedKey = "move_speed_mult";
    public const string DamageKey = "damage_mult";
    public const string FireIntervalKey = "fire_interval_mult";
    public const string DamageTakenKey = "damage_taken_mult";
    public const string MaxHpKey = "max_hp_mult";

    /// <summary>机型乘区的配置路径（<c>machines.&lt;id&gt;.&lt;field&gt;</c>；标准型不列键，读不到即回退默认）。</summary>
    public static string ConfigKey(MachineSpec spec, string field) => "machines." + spec.Id + "." + field;

    /// <summary>乘区下限：0 与负值会把玩法项打成无效（速度 0 ＝ 不能动、间隔 0 ＝ 每帧开火、
    /// 受到伤害 0 ＝ 无敌），改档是可达输入面，故在下限处拦住。</summary>
    public const double MinMult = 0.25;

    /// <summary>乘区上限：4 倍已是本作弹幕与操控不可能成立的值，超出按上限收口。</summary>
    public const double MaxMult = 4.0;

    /// <summary>乘区收口：逐项钳进 [<see cref="MinMult"/>, <see cref="MaxMult"/>]。
    /// 各消费点另有自己的域钳（间隔 ≥0.05 / 伤害 ≥1 / 血上限 ≥0.1），此处只管乘区本身。
    /// 非有限值按基准 1.0 处理——NaN 渗进玩家数值会一路静默传播（钳制函数不拦 NaN）。</summary>
    public static MachineModifiers Sanitize(MachineModifiers mods) => new(
        Clamp(mods.MoveSpeedMult),
        Clamp(mods.DamageMult),
        Clamp(mods.FireIntervalMult),
        Clamp(mods.DamageTakenMult),
        Clamp(mods.MaxHpMult));

    private static double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, MinMult, MaxMult) : 1.0;

    private static string Res(string file) => "res://assets/sprites/" + file;
}
