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

/// <summary>某一项乘区是否被改动（用于「本型是否只动了它该动的那一项」这类判据）。</summary>
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
    /// 加成幅度（百分比整数，供文案补参）：由**实际生效的乘区**反算，不读表里的定稿值——
    /// 数值被调过之后面板上写的数字必须跟着走，否则玩家照着「+15%」去算却得不到那个结果。
    /// 方向按各自语义取：速度 / 伤害 / 血量取增量，开火间隔与受到伤害取减量（越大越强）。
    /// </summary>
    public static int Percent(MachineTrait trait, MachineModifiers mods)
    {
        var value = trait switch
        {
            MachineTrait.MoveSpeed => mods.MoveSpeedMult - 1.0,
            MachineTrait.Damage => mods.DamageMult - 1.0,
            MachineTrait.FireRate => mods.FireRateMult - 1.0,
            MachineTrait.Defense => 1.0 - mods.DamageTakenMult,
            MachineTrait.MaxHealth => mods.MaxHpMult - 1.0,
            _ => 0.0,
        };

        return (int)Math.Round(value * 100.0, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// 机型定义：「id → 文案键 / 贴图名 / 加成项」的映射只写在这里（数值另在 <c>balance.json machines.*</c>）。
///
/// 分两处放的理由与全库同口径：**结构**（有哪几型、每型动哪一项、叫什么、用哪套贴图）属纯逻辑，
/// 与 Godot 无关且要被单测钉住；**数值**属可调项，单源在 `data/balance.json`，
/// 由引擎层按 <see cref="Trait"/> 对应的键读取覆盖（读不到即回退到本表的默认乘区）。
/// </summary>
public sealed record MachineSpec(
    string Id,
    string NameKey,
    MachineTrait Trait,
    MachineModifiers Defaults)
{
    /// <summary>贴图名主干：标准型沿用既有文件名，特种型一律 <c>player_ship_&lt;id&gt;</c>。</summary>
    public string SpriteStem => Id == MachineRoster.StandardId ? "player_ship" : "player_ship_" + Id;
}

/// <summary>
/// 初始机型名册（纯逻辑，零 Godot 依赖）：六型 = 标准型（无加成，沿用既有外观与既有数值）
/// + 五型特种型（各一项加成，各一套外观）。
///
/// **为什么必须有标准型**：机型是开局前的一次选择，而它不能把既有玩家的默认体验改掉——
/// 旧档（settings.json / run.json）里没有机型键，一律归一到标准型，行为与加机型之前逐位一致；
/// 同时它给五型加成提供可读的基准点（面板上「+15%」是相对什么而言的）。
///
/// 数值取向见 <c>docs/DESIGN_BASELINE.md</c> §1.17，行业依据见 <c>docs/REFERENCES.md</c> §4.15。
/// </summary>
public static class MachineRoster
{
    /// <summary>标准型 id：旧档缺键、未知 id、空串一律归一到它（白名单口径，不设非法态）。</summary>
    public const string StandardId = "standard";

    /// <summary>
    /// 名册（顺序 ＝ 面板行的顺序，索引稳定）：标准型在首位 ＝ 默认选中项。
    /// 乘区默认值须与 <c>data/balance.json machines.&lt;id&gt;.*</c> 一致（两侧分叉时 json 完整则看不出来，
    /// 只有 json 缺失/损坏才回退到错值——改一侧必须同时改另一侧，见 AGENTS.md §3）。
    /// </summary>
    public static readonly IReadOnlyList<MachineSpec> All = new MachineSpec[]
    {
        new(StandardId, "MACHINE_NAME_STANDARD", MachineTrait.None, MachineModifiers.Baseline),
        new("peregrine", "MACHINE_NAME_PEREGRINE", MachineTrait.MoveSpeed, new MachineModifiers(1.15, 1.0, 1.0, 1.0, 1.0)),
        new("sledge", "MACHINE_NAME_SLEDGE", MachineTrait.Damage, new MachineModifiers(1.0, 1.2, 1.0, 1.0, 1.0)),
        new("repeater", "MACHINE_NAME_REPEATER", MachineTrait.FireRate, new MachineModifiers(1.0, 1.0, 0.85, 1.0, 1.0)),
        new("bulwark", "MACHINE_NAME_BULWARK", MachineTrait.Defense, new MachineModifiers(1.0, 1.0, 1.0, 0.85, 1.0)),
        new("colossus", "MACHINE_NAME_COLOSSUS", MachineTrait.MaxHealth, new MachineModifiers(1.0, 1.0, 1.0, 1.0, 1.2)),
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
