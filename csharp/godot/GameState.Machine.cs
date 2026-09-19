using Godot;
using InfiAir.Core.Machines;
using InfiAir.Core.Storage;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：初始机型（六选一；口径见 <c>DESIGN_BASELINE</c> §1.17，依据见 <c>REFERENCES</c> §4.15）。
///
/// **一个字段两处用途**：机型既是玩家在标题屏的一次选择（偏好，落 settings.json），
/// 也是**本局**起飞的机型（落 run.json）。两者同源同一个字段，避免「设置里写着 A、飞的是 B」——
/// 新的一局与练习局取偏好；「继续上次出击」以存档里的机型为准并**同步回偏好**
/// （本局不能换机，所以飞过的那一型就是此刻该显示的那一型）。
///
/// **生效路径**：<see cref="MachineMods"/> 是收口后的乘区，消费点只有三处——
/// 机动/射速/攻击力在 Player 启动读一次（换机时经 <see cref="SignalName.MachineChanged"/> 重读）、
/// 血上限在本类 <c>ApplyMachineHealth</c> 折进基础上限（吸血是「基础上限 × 比例」的定额，同口径跟进）、
/// 受到伤害在 PlayerDamage 结算时乘进减免链。
///
/// 旧档（settings.json / run.json 都没有机型键）一律归一到标准型：
/// 与加机型之前的数值、贴图逐位一致，不报错、不改档。
/// </summary>
public partial class GameState : Node
{
    private MachineSpec _machine = MachineRoster.Default;
    private MachineModifiers _machineMods = MachineModifiers.Baseline;
    private MachineFeel _machineFeel = MachineFeel.Baseline;

    /// <summary>机型变更（选定 / 读档还原 / 恢复默认）后发出：Player 据此重读数值与贴图。
    /// 信号带 id 而非下标——存档、设置、平衡表三处引用的都是 id，下标只在面板内部用。</summary>
    [Signal]
    public delegate void MachineChangedEventHandler(string id);

    /// <summary>当前生效的机型 id（settings.json / run.json 里的稳定串）。</summary>
    public string MachineId => _machine.Id;

    /// <summary>当前生效的机型（名册项：文案键、贴图名、加成项）。</summary>
    public MachineSpec Machine => _machine;

    /// <summary>当前生效的乘区（balance.json 覆盖 + 域收口后的值，非名册默认值）。</summary>
    public MachineModifiers MachineMods => _machineMods;

    /// <summary>当前生效的手感档案（表现层 / 手感层机型系数；与乘区同口解析，消费点在
    /// PlayerVisuals / Bullet / Player 的表现路径——判定与玩法数值不得读它）。</summary>
    public MachineFeel Feel => _machineFeel;

    /// <summary>任一机型的生效手感档案（逐行展示 / 预览用；与生效值同一条求值路径）。</summary>
    public MachineFeel FeelFor(string? id) => ResolveMachineFeel(MachineRoster.ById(id));

    /// <summary>任一机型的生效乘区（机型面板逐行显示加成幅度用）。
    /// 与生效值走同一条求值路径，面板上的数字因此不可能与实际起飞时的乘区分叉。</summary>
    public MachineModifiers MachineModsFor(string? id) => ResolveMachineMods(MachineRoster.ById(id));

    /// <summary>
    /// 选定机型（标题屏面板与「继续上次出击」共用）：归一 id → 重解乘区 → 结算血上限 → 落偏好 → 广播。
    /// 未知 / 空 id 回落标准型——这是白名单口径，不设非法态，也不拒绝写入
    /// （手改档与未来版本的新 id 都只会得到标准型，而不是抛错卡住开机）。
    /// </summary>
    public void SetMachine(string? id) => ApplyMachine(id, persistPreference: true);

    /// <summary>读 run.json 里的机型 id（无档 / 缺键 / 读不出返回空串）。
    /// **只读不落任何状态**：供标题屏在切场景前判断「继续的话会飞哪一型」，
    /// 真正的应用在 <c>ApplyRunDict</c>（那里与血上限、增幅同一批还原）。</summary>
    public string PeekRunMachineId()
    {
        var result = LoadJsonCore(RunPathValue);
        if (result.Status != SaveLoadStatus.Ok || result.Tree is null)
        {
            return string.Empty;
        }

        return RunFieldNormalize.ReadString(result.Tree, "machine", string.Empty);
    }

    /// <summary>内部收口：解析乘区 → 血上限 → 偏好 → 广播。次序即依赖序
    /// （血上限要在广播前算好，否则 HUD 在信号回调里读到的还是旧上限）。
    /// <paramref name="persistPreference"/> 为真时连 settings.json 一起写：调用方都是
    /// 「玩家确实改了这一项」的时点（面板选定 / 继续出击同步存档机型），磁盘与内存的差
    /// 只可能来自这里，故写盘也收在这里，不在调用点各写一遍。</summary>
    private void ApplyMachine(string? id, bool persistPreference)
    {
        var spec = MachineRoster.ById(id);
        var changed = !string.Equals(spec.Id, _machine.Id, System.StringComparison.Ordinal);
        _machine = spec;
        _machineMods = ResolveMachineMods(spec);
        _machineFeel = ResolveMachineFeel(spec);
        ApplyMachineHealth();
        if (persistPreference && !string.Equals(_settings.MachineId, spec.Id, System.StringComparison.Ordinal))
        {
            _settings.MachineId = spec.Id;
            SaveSettings();
        }

        if (changed)
        {
            EmitSignal(SignalName.MachineChanged, spec.Id);
        }
    }

    /// <summary>
    /// 乘区求值：名册默认值作回退，逐键读 balance.json（键缺失/损坏即用默认——
    /// 与全库 <c>Cfg(key, 代码内默认值)</c> 口径一致，两侧必须同步改）。
    /// 读到的值一律过 <see cref="MachineRoster.Sanitize"/>：域外与 NaN 都在这里收口。
    /// </summary>
    private MachineModifiers ResolveMachineMods(MachineSpec spec)
    {
        var defaults = spec.Defaults;
        return MachineRoster.Sanitize(new MachineModifiers(
            Cfg(MachineRoster.ConfigKey(spec, MachineRoster.MoveSpeedKey), defaults.MoveSpeedMult).AsDouble(),
            Cfg(MachineRoster.ConfigKey(spec, MachineRoster.DamageKey), defaults.DamageMult).AsDouble(),
            Cfg(MachineRoster.ConfigKey(spec, MachineRoster.FireIntervalKey), defaults.FireIntervalMult).AsDouble(),
            Cfg(MachineRoster.ConfigKey(spec, MachineRoster.DamageTakenKey), defaults.DamageTakenMult).AsDouble(),
            Cfg(MachineRoster.ConfigKey(spec, MachineRoster.MaxHpKey), defaults.MaxHpMult).AsDouble()));
    }

    /// <summary>
    /// 手感档案求值：默认表回退，逐键读 balance.json（键缺失/损坏即用默认——
    /// 与乘区同一口径，json 只列要覆盖的差异键，未列项即 core 默认）。
    /// 读到的值一律过 <see cref="MachineFeelTable.Sanitize"/>。
    /// </summary>
    private MachineFeel ResolveMachineFeel(MachineSpec spec)
    {
        var d = MachineFeelTable.For(spec.Id);
        return MachineFeelTable.Sanitize(d with
        {
            DashPopScaleMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.DashPopScaleKey), d.DashPopScaleMult).AsDouble(),
            SwayMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.SwayKey), d.SwayMult).AsDouble(),
            BankMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.BankKey), d.BankMult).AsDouble(),
            VortexThresholdMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.VortexThresholdKey), d.VortexThresholdMult).AsDouble(),
            RecoilPxMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.RecoilPxKey), d.RecoilPxMult).AsDouble(),
            HitImpactMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.HitImpactKey), d.HitImpactMult).AsDouble(),
            DirectShake = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.DirectShakeKey), d.DirectShake).AsDouble(),
            LagPxMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.LagPxKey), d.LagPxMult).AsDouble(),
            FireLightMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.FireLightKey), d.FireLightMult).AsDouble(),
            FireJitterPx = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.FireJitterKey), d.FireJitterPx).AsDouble(),
            HitSquashMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.HitSquashKey), d.HitSquashMult).AsDouble(),
            ParryGlowMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.ParryGlowKey), d.ParryGlowMult).AsDouble(),
            BobPxMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.BobPxKey), d.BobPxMult).AsDouble(),
            AfterimageLifeMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.AfterimageLifeKey), d.AfterimageLifeMult).AsDouble(),
            AfterimageRateMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.AfterimageRateKey), d.AfterimageRateMult).AsDouble(),
            DashSpeedline = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.DashSpeedlineKey), d.DashSpeedline).AsDouble(),
            MuzzleGlowMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.MuzzleGlowKey), d.MuzzleGlowMult).AsDouble(),
            HitSparkMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.HitSparkKey), d.HitSparkMult).AsDouble(),
            TracerLenPx = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.TracerLenKey), d.TracerLenPx).AsDouble(),
            DamageFrameMult = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.DamageFrameKey), d.DamageFrameMult).AsDouble(),
            SmokeMinLevel = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.SmokeMinLevelKey), d.SmokeMinLevel).AsInt32(),
            DeflectRead = Cfg(MachineFeelTable.ConfigKey(spec, MachineFeelTable.DeflectReadKey), d.DeflectRead).AsDouble(),
        });
    }

    /// <summary>
    /// 健康配置注入（机型血上限乘区折进 <c>MaxHpBase</c>）。
    /// 折进基础上限而非另立一项的理由：吸血回复量是「基础上限 × base_hp_fraction」的定额
    /// （§1.4 明确它不随 extra_life 抬升的当前上限复利），机型血上限属**基础上限**的一部分，
    /// 同口径跟进才不会给重装型留一个隐形惩罚。
    /// 默认值写死 100.0 而不是取 <c>_combat.MaxHpBase</c>：后者已被乘区折过，
    /// 拿它当回退默认会在每次重解时把乘区再叠一遍（键缺失才走这条路，正是不报错的那种坏法）。
    /// </summary>
    private void ApplyMachineHealth()
    {
        var baseHp = Mathf.Max(Cfg("player.max_health", 100.0).AsDouble(), 0.1) * _machineMods.MaxHpMult;
        _combat.ApplyHealthConfig(
            Mathf.Max(baseHp, 0.1),
            Mathf.Max(Cfg("augments.extra_life.max_hp_bonus", 50.0).AsDouble(), 0.0),
            Mathf.Max(Cfg("augments.lifesteal.base_hp_fraction", 0.05).AsDouble(), 0.0));

        // 上限缩小时把当前血量一并收口（贴图切到重装型再切回来时，血条不得高于分母）。
        // 不在此发 HealthChanged：调用点要么紧接着整份写血量（ResetRun / 读档），
        // 要么根本没有进行中的一局（标题屏选机）。
        var maxHp = _combat.MaxHealth();
        if (_combat.Health > maxHp)
        {
            _combat.Health = maxHp;
        }
    }
}
