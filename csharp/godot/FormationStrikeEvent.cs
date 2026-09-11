using Godot;
using System.Collections.Generic;

namespace InfiAir;

/// <summary>
/// 轰炸编队事件编排：最低优先级随机遭遇——
/// IDLE → FORMATION_ENTER（自屏顶外靠近）→ FORMATION_TURN（90° 转航向，全程压坡）
/// → BOMBING_RUN（横穿、按波次交错投弹）→ FORMATION_EXIT（加速离场）→ IDLE（冷却）。
/// 不冻结 Boss 调度；占用波次槽——运行期间暂停普通波次
/// （Start() 置 spawner 波次暂停，与精英炮塔事件互斥，见设计文档 §1/§2）；
/// 可被返航 Abort() 打断（编队与在场炸弹一并清除，无结算，冷却照计）。编队锚点运动与战机
/// 偏移/朝向/侧倾由本节点 _Process 驱动；状态计时全在 _Process，不产生 Timer 节点。
/// 动态实体（战机/炸弹）一律挂 Main 下。
/// 结算三分支（决定这场遭遇的收益上限）：
///   全数拦截 = 一架未坠 + 一枚未投 → reward_intercept + 逐枚拦截奖（最难）；
///   全歼 = 打光编队（含已投弹）→ reward_all_clear；
///   放它离场 → 只有击坠得分。
/// CommOverlay（C# 同程序集 typed）；FormationCraft/FormationBomb 为 C# typed 直调。
/// 公共骨架（spawner 注入/母舰缓存/冷却/ResumeWaves 等）在 EncounterEventBase
/// （与 EliteTurretEvent 共享）。
/// </summary>
public partial class FormationStrikeEvent : EncounterEventBase
{
    public enum State
    {
        IDLE,
        FORMATION_ENTER,
        FORMATION_TURN,
        BOMBING_RUN,
        FORMATION_EXIT,
    }

    /// <summary>离场段时长（设计文档 §3 状态机常量，不在 §5 可调表内）。</summary>
    private const float ExitTime = 1.5f;

    /// <summary>僚机楔形偏移步进（后掠 ±55px 递增）。</summary>
    private const float WingStep = 55.0f;

    /// <summary>同机两次投弹间隔（设计文档 §3 常量）。</summary>
    private const float BombStagger = 0.4f;

    // ---- 配置（读 balance.json formation_strike_event 段，脚本值为缺键回退，两者保持一致） ----
    public int MinScore { get; set; } = 500;
    public float Cooldown { get; set; } = 50.0f;
    public Godot.Collections.Dictionary CraftCounts { get; set; } = new()
    {
        ["easy"] = 3,
        ["medium"] = 4,
        ["hard"] = 5,
    };
    public int CraftHpBase { get; set; } = 60;
    public int CraftScore { get; set; } = 200;
    public float ApproachSpeed { get; set; } = 260.0f;
    public float ApproachY { get; set; } = 260.0f; // 接近高度（相对视野上缘偏移）
    public float TurnTime { get; set; } = 1.2f;
    public float RunSpeed { get; set; } = 340.0f;
    /// <summary>离场加速度（px/s²）：FORMATION_EXIT 段逐帧加速的斜率。</summary>
    public float ExitAccel { get; set; } = 420.0f;
    public float BombInterval { get; set; } = 0.8f;
    public int BombsPerCraft { get; set; } = 2;
    public float BombFallSpeed { get; set; } = 300.0f;
    public float BombFuse { get; set; } = 1.2f;
    public int BombDamage { get; set; } = 20;
    public float BombRadius { get; set; } = 120.0f;
    public float BombEdgeFalloff { get; set; } = 0.35f;
    public int BombHp { get; set; } = 8;
    public int BombScore { get; set; } = 50;
    public int BombReflectDamage { get; set; } = 45;
    /// <summary>投弹波次数（每机每波投一枚）：1 = 单次齐投（无波次感），≥2 = 拉成多波轰炸。</summary>
    public int VolleyBatches { get; set; } = 2;
    /// <summary>相邻两波之间的间隔（秒）——波次间隔是「威胁有节奏」的来源。</summary>
    public float VolleyGap { get; set; } = 1.35f;
    public int RewardAllClear { get; set; } = 200;
    /// <summary>全歼奖励：一架未坠、一枚未投就把编队打光（比全歼更难）。</summary>
    public int RewardIntercept { get; set; } = 400;
    /// <summary>每枚被空中击落/弹反的炸弹额外奖励（拦住威胁本身的回报）。</summary>
    public int RewardPerIntercept { get; set; } = 25;

    /// <summary>水平出界判定的基础余量（px）：叠加在投弹动态余量上，投弹表为空时兜底。</summary>
    private const float RunOutMarginBase = 120.0f;

    private State _state = State.IDLE;
    protected override bool IsIdle => _state == State.IDLE;

    /// <summary>本事件的通讯强调色（琥珀：与精英炮塔的品红区分，也让台词框与编队身份色一致）。</summary>
    protected override Color CommAccent => UITheme.AccentGold;

    private float _stateTime;
    private Vector2 _anchor;
    private float _heading = Mathf.Pi / 2.0f; // 编队航向角（Vector2.Right.Rotated 语义；初始 +y 下降）
    private float _turnTarget;
    private float _speed;
    private float _exitSpeed;
    private Godot.Collections.Array _crafts = new(); // 稳定槽位：被击坠置 null，编队不收缩
    private Godot.Collections.Array<Vector2> _offsets = new();
    private int _alive;
    private Godot.Collections.Array<float> _dropTimes = new();
    private Godot.Collections.Array<int> _dropCraft = new();
    private int _dropIndex;
    /// <summary>已投弹计数（DroppedCount() 对外观测）。</summary>
    private int _dropped;

    /// <summary>未被引信引爆就消失的弹数（空中被击落 / 被弹反）——拦截计数的唯一来源。</summary>
    private int _intercepted;
    /// <summary>本次事件已发放过拦截奖励（防 Finish/Abort 双路径重发）。</summary>
    private bool _interceptAwarded;
    /// <summary>侧倾量（0..1，转弯与离场期写入，压坡用）。</summary>
    private float _bank;
    /// <summary>在场炸弹（事件结束/打断时随编队一并清理，与 FreeCrafts 同口径）。</summary>
    private readonly Godot.Collections.Array<FormationBomb> _bombs = new();

    /// <summary>炸弹对象池：闲置弹停用后回挂本节点（事件节点与 Main 同生命周期，跨场次事件复用）。
    /// 同屏活跃量小（≤5 机 × 波次），不设硬上限；取用侧 IsInstanceValid 过滤外部销毁的悬空条目。</summary>
    private readonly List<FormationBomb> _bombStock = new();

    /// <summary>待停放队列——ReleaseBomb 入队，_Process（idle 帧，物理回调外）批量 reparent 回
    /// 本节点；回收可能发生在物理信号分发中（击落/弹反命中），不得当场改树。</summary>
    private readonly List<FormationBomb> _pendingBombPark = new();

    /// <summary>入场警告后补播战术提示的倒计时（&lt;=0 已播/已取消）。</summary>
    private float _intelLeft;
    /// <summary>战术提示延迟（警告台词 3.5s 播完后再补一条，不叠字）。</summary>
    private const float IntelDelay = 4.0f;

    public override void _Ready()
    {
        // min_score/cooldown 判型 + 域钳（对齐
        // FakeEnemiesEvent 条目判型口径——坏值 AsInt64/AsDouble 抛 InvalidCastException
        // 崩溃，判型失败回退脚本默认）——min_score 负值分数 0 即触发；cooldown ≤0
        // 冷却失效、事件结束即刻可再触发（风暴）
        MinScore = CfgFx.Int("formation_strike_event.min_score", MinScore, 0);
        Cooldown = CfgFx.Float("formation_strike_event.cooldown", Cooldown, CfgFx.IntervalFloor);
        // craft_counts 判型回退（精英炮塔侧同口径）——配置损坏为非 Dictionary
        // 时 start() 的 .get() 在 Variant 上运行时崩溃
        var cc = GameState.Instance.Cfg("formation_strike_event.craft_counts", CraftCounts);
        if (cc.VariantType == Variant.Type.Dictionary)
        {
            CraftCounts = cc.AsGodotDictionary();
        }

        // 以下标量键判型 + 语义域钳（判型失败回退脚本
        // 默认，不抛不崩；当前默认数据行为零变化）——craft_hp_base ≥1（0/负编队机 1 点
        // 即毁、事件瞬结反复刷全歼奖励）、craft_score/reward_all_clear ≥0（负分倒扣被
        // 连击放大）、run_speed ≥0.1（≤0 转弯/轰炸/离场悬挂屏内）、bomb_interval ≥0.05
        // （≤0 投弹表全 0 同帧轰炸风暴）、bomb_fall_speed ≥0.1（≤0 炸弹悬停不落地）、
        // bomb_fuse ≥0.05（≤0 炸弹瞬爆/无爆炸判定）、bomb_damage ≥0（负值回血）、
        // bomb_radius ≥0.1（≤0 爆炸永不命中）
        CraftHpBase = CfgFx.Int("formation_strike_event.craft_hp_base", CraftHpBase, 1);
        CraftScore = CfgFx.Int("formation_strike_event.craft_score", CraftScore, 0);
        // approach_speed 下限钳制——≤0 时编队永驻 FORMATION_ENTER，
        // 波次暂停常驻 → 普通波次与 Boss 调度全冻结
        ApproachSpeed = CfgFx.Float("formation_strike_event.approach_speed", ApproachSpeed, 1.0f);
        ApproachY = CfgFx.Float("formation_strike_event.approach_y", ApproachY);
        // turn_time 钳下限——0/负值时 FORMATION_TURN 的
        // _stateTime / TurnTime 除零（Clamp 兜底无 NaN，但转弯瞬完成、视觉跳变）
        TurnTime = CfgFx.Float("formation_strike_event.turn_time", TurnTime, CfgFx.IntervalFloor);
        RunSpeed = CfgFx.Float("formation_strike_event.run_speed", RunSpeed, 0.1f);
        // exit_accel 钳 ≥0——负值离场段反向减速（编队永驻屏内占波次槽）
        ExitAccel = CfgFx.Float("formation_strike_event.exit_accel", ExitAccel, 0.0f);
        BombInterval = CfgFx.Float("formation_strike_event.bomb_interval", BombInterval, CfgFx.IntervalFloor);
        // bombs_per_craft 钳 [1,20]——0 空跑（占波次槽无弹）、巨值投弹表/炸弹节点爆炸
        BombsPerCraft = CfgFx.Int("formation_strike_event.bombs_per_craft", BombsPerCraft, 1, 20);
        BombFallSpeed = CfgFx.Float("formation_strike_event.bomb_fall_speed", BombFallSpeed, 0.1f);
        BombFuse = CfgFx.Float("formation_strike_event.bomb_fuse", BombFuse, CfgFx.IntervalFloor);
        BombDamage = CfgFx.Int("formation_strike_event.bomb_damage", BombDamage, 0);
        BombRadius = CfgFx.Float("formation_strike_event.bomb_radius", BombRadius, 0.1f);
        // 边缘衰减倍率钳 [0,1]——>1 会让边缘比中心更疼（反直觉），负值回血
        BombEdgeFalloff = CfgFx.Float("formation_strike_event.bomb_edge_falloff", BombEdgeFalloff, 0.0f, 1.0f);
        // 炸弹可击落：hp ≥1（0 会让任何擦伤立即引爆，拦截分白送）
        BombHp = CfgFx.Int("formation_strike_event.bomb_hp", BombHp, 1);
        BombScore = CfgFx.Int("formation_strike_event.bomb_score", BombScore, 0);
        BombReflectDamage = CfgFx.Int("formation_strike_event.bomb_reflect_damage", BombReflectDamage, 0);
        // 波次数钳 [1,10]——0 会空跑（占波次槽不投弹），巨值把轰炸拉成永不停歇的弹幕
        VolleyBatches = CfgFx.Int("formation_strike_event.volley_batches", VolleyBatches, 1, 10);
        VolleyGap = CfgFx.Float("formation_strike_event.volley_gap", VolleyGap, CfgFx.IntervalFloor);
        RewardAllClear = CfgFx.Int("formation_strike_event.reward_all_clear", RewardAllClear, 0);
        RewardIntercept = CfgFx.Int("formation_strike_event.reward_intercept", RewardIntercept, 0);
        RewardPerIntercept = CfgFx.Int("formation_strike_event.reward_per_intercept", RewardPerIntercept, 0);
        base._Ready(); // 台词层创建 + spawner 兜底（公共骨架，见 EncounterEventBase）
    }

    /// <summary>编队存活数（HUD/诊断读）。</summary>
    public int AliveCount() => _alive;

    /// <summary>已投出弹数（含被拦截的；DroppedCount 对外观测）。</summary>
    public int DroppedCount() => _dropped;

    /// <summary>被空中拦截/弹反的弹数（威胁被玩家拆掉的计数）。</summary>
    public int InterceptedCount() => _intercepted;

    /// <summary>触发条件（最低优先级）：自身 IDLE 且冷却结束、分数达标、Boss 未激活、精英炮塔事件未激活。
    /// 掷签间隔/概率由 spawner 侧持有（elite 事件在本事件之前检查，本 tick 先启动则 is_active 拦截）。</summary>
    public override bool CanTrigger()
    {
        // 分数实时读取（不走帧缓存）：调用方同帧改分须立即生效（直读 GameState.Score；
        // 帧缓存仅保留给 _Process 热路径的 CachedView）
        var liveScore = (int)GameState.Instance.Score;
        if (liveScore < MinScore || !base.CanTrigger())
        {
            return false;
        }

        if (_spawner != null && GodotObject.IsInstanceValid(_spawner))
        {
            if (_spawner.IsBossActive())
            {
                return false;
            }

            if (_spawner.EliteEvent() is IEncounterEvent elite && elite.IsActive())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>事件启动（互斥检查通过后由 spawner 调用）。</summary>
    public override void Start()
    {
        if (_state != State.IDLE)
        {
            return;
        }

        _state = State.FORMATION_ENTER;
        _stateTime = 0.0f;
        _heading = Mathf.Pi / 2.0f;
        _speed = ApproachSpeed;
        _dropped = 0;
        _intercepted = 0;
        _interceptAwarded = false;
        _bank = 0.0f;
        _intelLeft = 0.0f;
        FreeBombs();
        // 占用波次槽：事件期间暂停普通波次（结束/打断时恢复；typed 直调）
        if (_spawner != null && GodotObject.IsInstanceValid(_spawner))
        {
            _spawner.SetWavesPaused(true);
        }

        var view = FrameCache.ViewRect();
        var x0 = (float)GD.RandRange(view.Position.X + view.Size.X * 0.4, view.Position.X + view.Size.X * 0.6);
        _anchor = new Vector2(x0, view.Position.Y - 120.0f);
        // 生成编队：长机居中，僚机后掠 ±55px 递增（楔形，槽位稳定）
        var difficulty = (string)(StringName)GameState.Instance.Difficulty;
        // 难度键条目值判型（craft_counts 只判容器层）——坏值 AsInt64 抛
        // InvalidCastException 崩溃，回退默认 4
        var craftV = CraftCounts.GetValueOrDefault(difficulty, new Variant());
        // craft_counts 条目值域钳 [1,5]（对齐 EliteTurretEvent
        // turret_counts 口径）——巨值生成海量战机节点 OOM/软锁、0/负空跑（占波次槽无实体）
        var count = Mathf.Clamp(craftV.VariantType is Variant.Type.Int or Variant.Type.Float ? (int)craftV.AsInt64() : 4, 1, 5);
        // HP 三级乘算：基准 × 难度档 × 本局进程 ramp（与普通敌机同口径）
        var hp = Mathf.Max(
            1,
            (int)Mathf.Round(
                CraftHpBase
                * (float)GameState.Instance.EnemyHpMultiplier()
                * (float)GameState.Instance.EnemyHpRamp()));
        _crafts.Clear();
        _offsets.Clear();
        _offsets.Add(Vector2.Zero);
        for (var i = 1; i < count; i++)
        {
            var side = i % 2 == 1 ? -1.0f : 1.0f;
            // 整数除法步进（原 GDScript integer_division 语义）
            var step = (float)((i + 1) / 2);
            _offsets.Add(new Vector2(side * WingStep * step, WingStep * step));
        }

        for (var i = 0; i < count; i++)
        {
            var index = i; // 闭包捕获副本：C# for 循环变量为单变量，直接捕获会全部指向末索引
            var craft = new FormationCraft();
            craft.Setup(hp);
            craft.Position = _anchor + _offsets[i];
            craft.Rotation = _heading + Mathf.Pi / 2.0f;
            craft.Died += (c) => OnCraftDied(c, index); // 原 .bind(i) 语义
            GetParent().AddChild(craft);
            _crafts.Add(craft);
        }

        _alive = count;
        _comm?.ShowLine("FBQ_WARN");
        _intelLeft = IntelDelay;
    }

    /// <summary>入场提示后补一条战术提示（只播一次）：告诉玩家这一场可以打、可以拦、可以弹反。
    /// 事件结束/打断即取消（残留提示会指向已不存在的编队）。</summary>
    private void TickIntelHint(float delta)
    {
        if (_intelLeft <= 0.0f || _comm == null)
        {
            return;
        }

        _intelLeft -= delta;
        if (_intelLeft <= 0.0f)
        {
            _comm.ShowLine("FBQ_INTEL");
        }
    }

    /// <summary>返航打断：编队与在场炸弹一并清除，无结算（拦截奖不发），冷却照计。</summary>
    public override void Abort()
    {
        if (_state == State.IDLE)
        {
            return;
        }

        FreeCrafts();
        FreeBombs();
        _state = State.IDLE;
        _cooldownLeft = Cooldown;
        _intelLeft = 0.0f; // 战术提示随事件取消（提示已不存在的编队等于误导）
        ResumeWaves();
        _comm?.Clear(); // 清掉已显警告台词，避免返航恢复后残留
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        ProcessPendingBombParks(); // 帧末停放不限事件状态——IDLE 期也可能有待停放的回收弹
        if (_state == State.IDLE)
        {
            TickCooldown(d);
            return;
        }

        _stateTime += d;
        TickIntelHint(d);
        switch (_state)
        {
            case State.FORMATION_ENTER:
                _anchor.Y += ApproachSpeed * d;
                if (_anchor.Y >= FrameCache.ViewRect().Position.Y + ApproachY)
                {
                    BeginTurn();
                }

                break;
            case State.FORMATION_TURN:
                {
                    var t = Mathf.Clamp(_stateTime / TurnTime, 0.0f, 1.0f);
                    _heading = Mathf.LerpAngle(Mathf.Pi / 2.0f, _turnTarget, t);
                    _speed = Mathf.Lerp(ApproachSpeed, RunSpeed, t);
                    _anchor += Vector2.Right.Rotated(_heading) * _speed * d;
                    // 压坡：转弯中段最深（sin 峰），进出两端归零——俯视视角下的「质量感」
                    _bank = Enemy.SinFast(Mathf.Pi * t);
                    ApplyBank();
                    if (t >= 1.0f)
                    {
                        BeginRun();
                    }

                    break;
                }

            case State.BOMBING_RUN:
                {
                    _anchor += Vector2.Right.Rotated(_heading) * RunSpeed * d;
                    ProcessDrops();
                    PruneBombs();
                    var view = FrameCache.ViewRect();
                    // 出界余量按投弹表剩余最大时长折算：固定 ±120 会在 hard 5 机
                    // 投弹段（未完时截断末机炸弹，最坏第 5 机 0 投弹；余量动态 = 末弹时刻 × 速度
                    var runMargin = _dropTimes.Count > 0 ? _dropTimes[_dropTimes.Count - 1] * RunSpeed : 0.0f;
                    if (_dropIndex >= _dropTimes.Count
                        || _anchor.X < view.Position.X - runMargin - RunOutMarginBase
                        || _anchor.X > view.End.X + runMargin + RunOutMarginBase)
                    {
                        BeginExit();
                    }

                    break;
                }

            case State.FORMATION_EXIT:
                _exitSpeed += ExitAccel * d;
                _anchor += Vector2.Right.Rotated(_heading) * _exitSpeed * d;
                // 离场压坡收正（提速爬升的姿态）
                _bank = Mathf.Lerp(_bank, 0.0f, Mathf.Min(1.0f, d * 3.0f));
                ApplyBank();
                if (_stateTime >= ExitTime)
                {
                    Finish();
                }

                break;
        }

        UpdateCrafts();
    }

    /// <summary>把当前侧倾量写到全体在编队机（被击坠槽位跳过）。</summary>
    private void ApplyBank()
    {
        foreach (var craftV in _crafts)
        {
            if (craftV.VariantType == Variant.Type.Nil)
            {
                continue;
            }

            if (craftV.AsGodotObject() is FormationCraft craft && GodotObject.IsInstanceValid(craft))
            {
                craft.SetBank(_bank);
            }
        }
    }

    /// <summary>转航向：朝较远侧缘方向 90° 转向。</summary>
    private void BeginTurn()
    {
        _state = State.FORMATION_TURN;
        _stateTime = 0.0f;
        var view = FrameCache.ViewRect();
        _turnTarget = _anchor.X < view.Position.X + view.Size.X * 0.5f ? 0.0f : Mathf.Pi;
    }

    /// <summary>横穿投弹：构建交错投弹时刻表。
    /// 波次化：每波内长机先投、僚机按到长机的排序依次错开 bomb_interval（时间=名次×间隔，
    /// 而非阵位索引——侧翼阵位不会把首枚弹推到波次末尾），同机两枚间隔 BombStagger；
    /// 相邻两波之间插入 volley_gap，形成「一波压制 → 呼吸 → 下一波」的节奏。
    /// 循环次序必须 i（波）外层 / k（机）内层：k 外层产生非排序时刻表，
    /// ProcessDrops 按单调 _stateTime 贪心消费会把后续波次堆积到末尾同帧。</summary>
    private void BeginRun()
    {
        _state = State.BOMBING_RUN;
        _stateTime = 0.0f;
        _dropTimes.Clear();
        _dropCraft.Clear();
        _dropIndex = 0;
        _bank = 0.0f;
        var batches = VolleyBatches;
        // 每机的投弹名次 = 到长机的距离排序（长机恒 0，僚机按侧移排序）
        var rank = new int[_crafts.Count];
        for (var i = 0; i < rank.Length; i++)
        {
            rank[i] = i;
        }

        System.Array.Sort(rank, (a, b) => _offsets[a].LengthSquared().CompareTo(_offsets[b].LengthSquared()));
        var order = new int[_crafts.Count];
        for (var i = 0; i < rank.Length; i++)
        {
            order[rank[i]] = i;
        }

        for (var b = 0; b < batches; b++)
        {
            for (var i = 0; i < _crafts.Count; i++)
            {
                for (var k = 0; k < BombsPerCraft; k++)
                {
                    _dropTimes.Add(b * VolleyGap + order[i] * BombInterval + k * BombStagger);
                    _dropCraft.Add(i);
                }
            }
        }
    }

    /// <summary>离场：沿当前航向加速穿出侧缘（压坡回正，收尾干净）。</summary>
    private void BeginExit()
    {
        _state = State.FORMATION_EXIT;
        _stateTime = 0.0f;
        _exitSpeed = RunSpeed;
        _bank = 0.0f;
        ApplyBank();
    }

    /// <summary>离场结束：清场结算 → 回 IDLE 进冷却。</summary>
    private void Finish()
    {
        SettleInterceptBonus();
        // 非拦截收尾补一条清除提示（拦截路径已由奖励台词收尾，不叠）
        if (_dropped > 0)
        {
            _comm?.ShowLine("FBQ_CLEAR");
        }

        FreeCrafts();
        FreeBombs();
        _state = State.IDLE;
        _cooldownLeft = Cooldown;
        ResumeWaves();
    }

    /// <summary>拦截结算：一架未坠 + 一枚未投 = 全数拦截（比全歼更难），
    /// 逐枚拦截另有 RewardPerIntercept 小奖。只在事件正常收尾时发（Abort 无结算）。</summary>
    private void SettleInterceptBonus()
    {
        if (_interceptAwarded)
        {
            return;
        }

        _interceptAwarded = true;
        if (_intercepted > 0)
        {
            GameState.Instance.AddScore(_intercepted * RewardPerIntercept);
        }

        if (_dropped == 0 && _alive > 0 && RewardIntercept > 0)
        {
            GameState.Instance.AddScore(RewardIntercept);
            _comm?.ShowLine("FBQ_INTERCEPT_BONUS");
            GameState.Instance.PlaySfx(SfxId.Resupply, -4.0, 1.35);
        }
    }

    /// <summary>按时刻表投弹：投弹点即当前编队机位置（+机腹偏移）；已毁机跳过（时刻表照走）。</summary>
    private void ProcessDrops()
    {
        while (_dropIndex < _dropTimes.Count && _stateTime >= _dropTimes[_dropIndex])
        {
            var idx = _dropCraft[_dropIndex];
            _dropIndex++;
            if (_crafts[idx].VariantType == Variant.Type.Nil)
            {
                continue;
            }

            var craft = (FormationCraft)_crafts[idx];
            if (craft == null || !GodotObject.IsInstanceValid(craft))
            {
                continue;
            }

            SpawnBomb(craft);
        }
    }

    private void SpawnBomb(FormationCraft craft)
    {
        var bomb = AcquireBomb();
        if (bomb.GetParent() == null)
        {
            GetParent().AddChild(bomb); // 新弹入树触发 _Ready（外观一次性构建）
        }
        else if (bomb.GetParent() != GetParent())
        {
            bomb.Reparent(GetParent()); // 池中弹：从池节点（本事件）挂回 Main
        }

        bomb.EdgeFalloff = BombEdgeFalloff;
        bomb.BombScore = BombScore;
        bomb.ReflectDamage = BombReflectDamage;
        var dir = Vector2.Right.Rotated(_heading);
        // 炸弹伤害随本局进程 ramp（与敌弹同一系数）
        bomb.Setup(
            new Vector2(dir.X * RunSpeed * 0.35f, BombFallSpeed),
            BombFuse,
            Mathf.Max(1, (int)Mathf.Round(BombDamage * (float)GameState.Instance.EnemyDamageRamp())),
            BombRadius);
        bomb.MaxHp = Mathf.Max(1, BombHp);
        bomb.Hp = bomb.MaxHp;
        bomb.Activate(); // 全运行态/外观复位（新弹幂等重入；回收弹经此复活并重绑注册表）
        bomb.Position = craft.Position + new Vector2(0.0f, 18.0f) * (float)GameState.Instance.WorldScale;
        _bombs.Add(bomb);
        craft.FlashBay(); // 机腹照明亮一下：投弹动作可见
        GameState.Instance.PlaySfx(SfxId.Dash, -14.0, 1.7); // 投弹舱释放的轻响（复用采样 + 高音变体）
        _dropped++;
    }

    /// <summary>取弹：池中有存活实例则复用，否则新建并登记归属池。</summary>
    private FormationBomb AcquireBomb()
    {
        while (_bombStock.Count > 0)
        {
            var b = _bombStock[_bombStock.Count - 1];
            _bombStock.RemoveAt(_bombStock.Count - 1);
            if (GodotObject.IsInstanceValid(b) && !b.IsQueuedForDeletion())
            {
                return b;
            }
        }

        var bomb = new FormationBomb();
        bomb.SetPool(this);
        return bomb;
    }

    /// <summary>炸弹回收入池（弹体终局路径回调）：在场表移除 + 拦截计数 + 停用入待停放队列。
    /// 幂等由弹侧 _parked 守卫（ReturnToPool 重入直接返回）。</summary>
    public void ReleaseBomb(FormationBomb bomb)
    {
        if (!GodotObject.IsInstanceValid(bomb))
        {
            return;
        }

        _bombs.Remove(bomb);
        if (bomb.Intercepted)
        {
            _intercepted++; // 拦截计数由回收路径同步结算（替代 PruneBombs 的事后对账）
        }

        bomb.Deactivate();
        _bombStock.Add(bomb);
        _pendingBombPark.Add(bomb);
    }

    /// <summary>帧末批量停放（idle _Process 处于物理回调外）：reparent 回本节点；
    /// IsParked 仲裁——同帧复活（重新 Activate）的弹跳过，避免活跃弹被误挂进池节点。</summary>
    private void ProcessPendingBombParks()
    {
        if (_pendingBombPark.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _pendingBombPark.Count; i++)
        {
            var b = _pendingBombPark[i];
            if (!GodotObject.IsInstanceValid(b) || b.IsQueuedForDeletion() || !b.IsParked())
            {
                continue;
            }

            if (b.GetParent() != this)
            {
                b.Reparent(this); // reparent 触发 b._ExitTree → UnbindEnemy（停放弹离开注册表）
            }
        }

        _pendingBombPark.Clear();
    }

    /// <summary>清除在场表中已被外部销毁（清场 sweep）的悬空条目。正常终局（引爆/击落/弹反/
    /// 出界）经 ReturnToPool 同步移除并在回收路径计拦截；本方法只对账未经池回收的外部销毁——
    /// 此类不算拦截（轨道打击清场的炸弹既没炸到人也没被玩家拆掉，与原口径一致）。</summary>
    private void PruneBombs()
    {
        for (var i = _bombs.Count - 1; i >= 0; i--)
        {
            var bomb = _bombs[i];
            if (bomb != null && GodotObject.IsInstanceValid(bomb))
            {
                continue;
            }

            _bombs.RemoveAt(i);
        }
    }

    /// <summary>清空在场炸弹（事件结束/返航打断时）：存活弹走回收入池（跨场次复用），
    /// 已投放的弹不留给下一局——池弹同属本事件节点，场景销毁时一并消亡。</summary>
    private void FreeBombs()
    {
        // 倒序迭代：ReleaseBomb 内部会从 _bombs 移除条目
        for (var i = _bombs.Count - 1; i >= 0; i--)
        {
            var bomb = _bombs[i];
            if (bomb != null && GodotObject.IsInstanceValid(bomb))
            {
                bomb.ReturnToPool();
            }
        }

        _bombs.Clear(); // 外部销毁（清场 sweep）的悬空条目兜底清除；拦截计数不经此处（原口径）
    }

    /// <summary>编队驱动：位置 = 锚点 + 随航向旋转的楔形偏移；机头朝航向。</summary>
    private void UpdateCrafts()
    {
        for (var i = 0; i < _crafts.Count; i++)
        {
            if (_crafts[i].VariantType == Variant.Type.Nil)
            {
                continue;
            }

            var craft = (FormationCraft)_crafts[i];
            if (craft == null || !GodotObject.IsInstanceValid(craft))
            {
                continue;
            }

            craft.Position = _anchor + _offsets[i].Rotated(_heading - Mathf.Pi / 2.0f);
            craft.Rotation = _heading + Mathf.Pi / 2.0f;
        }
    }

    /// <summary>击坠：单机得分走 AddKillScore（连击+1、乘区放大后照常过难度倍率，第 1 杀乘区 1.0）；
    /// 全歼 → 全歼奖励 AddScore（不计连击）+ 提前离场。</summary>
    private void OnCraftDied(FormationCraft craft, int index)
    {
        if (index >= 0 && index < _crafts.Count
            && _crafts[index].VariantType != Variant.Type.Nil
            && (FormationCraft)_crafts[index] == craft)
        {
            _crafts[index] = new Variant();
        }

        _alive = Mathf.Max(0, _alive - 1);
        GameState.Instance.AddKillScore(CraftScore); // 编队机击杀计连击（全歼奖励不计）
        if (_alive == 0 && _state != State.IDLE && _state != State.FORMATION_EXIT)
        {
            GameState.Instance.AddScore(RewardAllClear);
            BeginExit();
        }
    }

    private void FreeCrafts()
    {
        foreach (var craftV in _crafts)
        {
            if (craftV.VariantType != Variant.Type.Nil && GodotObject.IsInstanceValid(craftV.AsGodotObject()))
            {
                ((FormationCraft)craftV).QueueFree();
            }
        }

        _crafts.Clear();
        _offsets.Clear();
        _alive = 0;
    }
}
