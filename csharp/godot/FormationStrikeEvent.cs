using Godot;
using System.Collections.Generic;
using InfiAir.Core.Combat;

namespace InfiAir;

/// <summary>
/// 轰炸编队事件编排：最低优先级随机遭遇——
/// IDLE → FORMATION_ENTER（自屏顶外靠近，屏顶预告线标出进场航道）→ FORMATION_TURN（90° 转航向，
/// 全程压坡）→ BOMBING_RUN（横穿、按波次交错投弹，投弹前机腹灯预警）→ FORMATION_EXIT（加速离场）
/// → IDLE（冷却）。
/// 占用波次槽与 Boss 槽——运行期间暂停普通波次并冻结 Boss 调度（结束/打断时一并解冻并补触发），
/// 与精英炮塔事件互斥（互斥判定单源在 GameEventManager，本事件只报自身就绪）；
/// 可被返航 Abort() 打断（编队与在场炸弹一并清除，无结算，冷却照计）。编队锚点运动与战机
/// 偏移/朝向/侧倾由本节点 _Process 驱动；状态计时全在 _Process，不产生 Timer 节点。
/// 动态实体（战机/炸弹）一律挂 Main 下。
/// 结算三分支（决定这场遭遇的收益上限）：
///   全数拦截 = 一架未坠 + 一枚未投 → 拦截奖励（另有逐枚拦截分，在拦截当帧入账）；
///   全歼 = 打光编队（含已投弹）→ 全歼奖励；
///   放它离场 → 只有击坠得分。
/// 编队几何与投弹时刻表在 core（FormationPlan，可单测）；本节点只做逐帧消费与节点写入，
/// 槽位/时刻表都是定长 CLR 数组（零 Variant 装箱）。
/// 公共骨架（spawner 注入/母舰缓存/冷却/波次与 Boss 互斥）在 EncounterEventBase。
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

    /// <summary>离场段时长（状态机常量，非可调手感值）。</summary>
    private const float ExitTime = 1.5f;

    /// <summary>僚机楔形偏移步进（后掠 ±55px 递增；与 FormationPlan.Wedge 同参）。</summary>
    private const float WingStep = 55.0f;

    /// <summary>同机两枚炸弹的投放间隔（状态机常量）。</summary>
    private const float BombStagger = 0.4f;

    /// <summary>屏顶外起始高度：编队自可见区上方这么远处进入（进场预告线的提前量来源）。</summary>
    private const float SpawnLeadY = 120.0f;

    /// <summary>投弹预警提前量（秒）：机腹灯先闪，玩家才有反应窗口（投弹瞬间的闪亮只做「已投」确认）。</summary>
    private const float BayWarnLead = 0.45f;

    /// <summary>水平出界判定的基础余量（px）：叠加在投弹动态余量上。</summary>
    private const float RunOutMarginBase = 120.0f;

    /// <summary>HUD 进度条刷新间隔（秒）——与精英炮塔同口径，逐帧刷只是白写属性。</summary>
    private const float HudInterval = 0.1f;

    /// <summary>战术提示最早播报时刻（秒）：警告台词打字 + 停留约 3.9s，不叠字。</summary>
    private const float IntelEarliest = 4.0f;

    // ---- 配置（读 balance.json formation_strike_event 段，脚本值为缺键回退，两者保持一致） ----
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
    /// <summary>反射弹寻敌转向加速度（px/s²）——够快以咬住横穿的编队，又不至于瞬间掉头。</summary>
    public float BombReflectTurnAccel { get; set; } = 1600.0f;
    /// <summary>弹反初速倍率（1 = 原样奉还）——追尾横穿的编队要靠速度余量，转向加速度只决定
    /// 转弯半径；倍率不足的表现是「弹反成功但永远差一截」。</summary>
    public float BombReflectSpeedMult { get; set; } = 1.6f;
    /// <summary>投弹波次数（每机每波投一枚）：1 = 单次齐投（无波次感），≥2 = 拉成多波轰炸。</summary>
    public int VolleyBatches { get; set; } = 2;
    /// <summary>相邻两波之间的间隔（秒）——波次间隔是「威胁有节奏」的来源。</summary>
    public float VolleyGap { get; set; } = 1.35f;
    public int RewardAllClear { get; set; } = 200;
    /// <summary>全歼奖励：一架未坠、一枚未投就把编队打光（比全歼更难）。</summary>
    public int RewardIntercept { get; set; } = 400;
    /// <summary>每枚被空中击落/弹反的炸弹奖励（拦住威胁本身的回报，拦截当帧入账）。</summary>
    public int RewardPerIntercept { get; set; } = 25;

    private State _state = State.IDLE;
    protected override bool IsIdle => _state == State.IDLE;

    /// <summary>本事件的通讯强调色（琥珀：与精英炮塔的品红区分，也让台词框与编队身份色一致）。</summary>
    protected override Color CommAccent => UITheme.AccentGold;

    // ---- 编排状态 ----
    private float _stateTime;
    /// <summary>全程已用（模拟）时间：HUD 进度条的唯一依据（分段切换不清零）。</summary>
    private float _elapsed;
    /// <summary>全程计划时长：入场 + 转弯 + 投弹表末刻 + 离场（进度条满格时长）。</summary>
    private float _plannedTotal;
    private Vector2 _anchor;
    private float _heading = Mathf.Pi / 2.0f; // 编队航向角（Vector2.Right.Rotated 语义；初始 +y 下降）
    private float _turnTarget;
    private float _speed;
    private float _exitSpeed;
    /// <summary>侧倾量（0..1，转弯与离场期写入，压坡用）。</summary>
    private float _bank;

    // ---- 编队与投弹表（定长槽位：被击坠置 null，编队不收缩） ----
    private FormationCraft?[] _crafts = System.Array.Empty<FormationCraft?>();
    private Vector2[] _offsets = System.Array.Empty<Vector2>();
    private float[] _dropTimes = System.Array.Empty<float>();
    private int[] _dropCraft = System.Array.Empty<int>();
    private int _total;
    private int _alive;
    /// <summary>已投弹游标（时刻表单调不减，逐帧贪心消费）。</summary>
    private int _dropIndex;
    /// <summary>已发出投弹预警的游标（领先 _dropIndex BayWarnLead 秒）。</summary>
    private int _warnIndex;
    private int _dropped;

    /// <summary>未被引信引爆就消失的弹数（空中被击落 / 被弹反）——拦截计数的唯一来源。</summary>
    private int _intercepted;

    /// <summary>本次事件已结算（防 Finish/Abort 双路径重发奖励与台词）。</summary>
    private bool _settled;

    /// <summary>本次事件已打出全歼（打光编队）——收尾台词据此区分「全歼」与「普通清除」。</summary>
    private bool _allClear;

    /// <summary>进度台词阶段：0 未播 / 1 已播战损台词 / 2 已播拦截台词（一次事件至多一条，
    /// 台词框 3.5s 停留，进度台词叠起来只会互相顶掉）。</summary>
    private int _lineStage;

    /// <summary>战术提示播报时刻（_elapsed 口径；float.MaxValue = 已播/未排程）。</summary>
    private float _intelAt = float.MaxValue;

    /// <summary>在场炸弹（事件结束/打断时随编队一并清理，与 FreeCrafts 同口径）。</summary>
    private readonly List<FormationBomb> _bombs = new();

    /// <summary>炸弹对象池：闲置弹停用后回挂本节点（事件节点与 Main 同生命周期，跨场次事件复用）。
    /// 同屏活跃量小（≤5 机 × 波次），不设硬上限；取用侧 IsInstanceValid 过滤外部销毁的悬空条目。</summary>
    private readonly List<FormationBomb> _bombStock = new();

    /// <summary>待停放队列——ReleaseBomb 入队，_Process（idle 帧，物理回调外）批量 reparent 回
    /// 本节点；回收可能发生在物理信号分发中（击落/弹反命中），不得当场改树。</summary>
    private readonly List<FormationBomb> _pendingPark = new();

    /// <summary>HUD 事件条（与精英炮塔共用同一组件；本事件的身份色 = CommAccent）。</summary>
    private Hud? _hud;
    private float _hudPoll;

    /// <summary>进场预告线（复用普通波次的 SpawnTelegraph，只换身份色）：标出编队进场航道，
    /// 自毁由自身寿命负责，事件提前收场时由 Teardown 收回。</summary>
    private SpawnTelegraph? _telegraph;

    public override void _Ready()
    {
        // cooldown ≤0 冷却失效、事件结束即刻可再触发（风暴）
        Cooldown = CfgFx.Float("formation_strike_event.cooldown", Cooldown, CfgFx.IntervalFloor);
        // craft_counts 判型回退（精英炮塔侧同口径）——配置损坏为非 Dictionary
        // 时 CraftCounts.GetValueOrDefault 在 Variant 上运行时崩溃
        var cc = GameState.Instance.Cfg("formation_strike_event.craft_counts", CraftCounts);
        if (cc.VariantType == Variant.Type.Dictionary)
        {
            CraftCounts = cc.AsGodotDictionary();
        }

        // 以下标量键判型 + 语义域钳（判型失败回退脚本默认，不抛不崩）——craft_hp_base ≥1（0/负
        // 编队机 1 点即毁、事件瞬结反复刷全歼奖励）、craft_score/reward_* ≥0（负分倒扣被连击放大）、
        // approach_speed ≥1（≤0 编队永驻 FORMATION_ENTER，波次暂停常驻）、turn_time/bomb_interval/
        // bomb_fuse/volley_gap ≥IntervalFloor（0 使分段计时除零或同帧连投/瞬爆）、run_speed ≥0.1
        // （≤0 转弯/轰炸/离场悬挂屏内）、bomb_fall_speed ≥0.1（≤0 炸弹悬停不落地）、
        // bomb_damage ≥0（负值回血）、bomb_radius ≥0.1（≤0 爆炸永不命中）、
        // bombs_per_craft ∈[1,20]（0 空跑、巨值炸弹节点爆炸）、volley_batches ∈[1,10]（同上）
        CraftHpBase = CfgFx.Int("formation_strike_event.craft_hp_base", CraftHpBase, 1);
        CraftScore = CfgFx.Int("formation_strike_event.craft_score", CraftScore, 0);
        ApproachSpeed = CfgFx.Float("formation_strike_event.approach_speed", ApproachSpeed, 1.0f);
        ApproachY = CfgFx.Float("formation_strike_event.approach_y", ApproachY);
        TurnTime = CfgFx.Float("formation_strike_event.turn_time", TurnTime, CfgFx.IntervalFloor);
        RunSpeed = CfgFx.Float("formation_strike_event.run_speed", RunSpeed, 0.1f);
        // exit_accel 钳 ≥0——负值离场段反向减速（编队永驻屏内占波次槽）
        ExitAccel = CfgFx.Float("formation_strike_event.exit_accel", ExitAccel, 0.0f);
        BombInterval = CfgFx.Float("formation_strike_event.bomb_interval", BombInterval, CfgFx.IntervalFloor);
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
        // 转向加速度钳 ≥1——0 让反射弹失去寻敌，「弹反成功」退化成靠运气
        BombReflectTurnAccel = CfgFx.Float("formation_strike_event.bomb_reflect_turn_accel", BombReflectTurnAccel, 1.0f);
        // 反射初速倍率钳 ≥0.1——≤0 让反射弹原地悬停（弹反成功变成一枚不动的装饰）
        BombReflectSpeedMult = CfgFx.Float("formation_strike_event.bomb_reflect_speed_mult", BombReflectSpeedMult, 0.1f);
        VolleyBatches = CfgFx.Int("formation_strike_event.volley_batches", VolleyBatches, 1, 10);
        VolleyGap = CfgFx.Float("formation_strike_event.volley_gap", VolleyGap, CfgFx.IntervalFloor);
        RewardAllClear = CfgFx.Int("formation_strike_event.reward_all_clear", RewardAllClear, 0);
        RewardIntercept = CfgFx.Int("formation_strike_event.reward_intercept", RewardIntercept, 0);
        RewardPerIntercept = CfgFx.Int("formation_strike_event.reward_per_intercept", RewardPerIntercept, 0);
        base._Ready(); // 台词层创建 + spawner 兜底（公共骨架，见 EncounterEventBase）
    }

    /// <summary>编队存活数（HUD/诊断读）。</summary>
    public int AliveCount() => _alive;

    /// <summary>已投出弹数（含被拦截的；诊断读）。</summary>
    public int DroppedCount() => _dropped;

    /// <summary>被空中拦截/弹反的弹数（威胁被玩家拆掉的计数；诊断读）。</summary>
    public int InterceptedCount() => _intercepted;

    /// <summary>事件启动（互斥检查通过后由事件管理器调用）。</summary>
    public override void Start()
    {
        if (_state != State.IDLE)
        {
            return;
        }

        FreeCrafts(); // 防御：任何残留编队先清（正常路径上一次收场已清空）
        FreeBombs();
        ResetRunState();
        _state = State.FORMATION_ENTER;
        var view = FrameCache.ViewRect();
        var x0 = (float)GD.RandRange(view.Position.X + (view.Size.X * 0.4), view.Position.X + (view.Size.X * 0.6));
        _anchor = new Vector2(x0, view.Position.Y - SpawnLeadY);
        BuildFormation();
        // 计划时长只看静态配置：投弹表在编队成型时就已定，玩家打掉几架只让事件更早收场
        var approach = (view.Position.Y + ApproachY - _anchor.Y) / ApproachSpeed;
        _plannedTotal = Mathf.Max(approach, 0.0f) + TurnTime + LastDropTime() + ExitTime;
        HoldWaves();
        HoldBoss();
        ShowEntryTelegraph(x0, view);
        _hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
        if (_hud != null)
        {
            _hud.ShowEventBar("FBQ_HUD_TITLE", "FBQ_HUD_STATUS", CommAccent, _alive, _total, _intercepted);
        }

        RefreshEventBar(0.0f, force: true);
        _comm?.ShowLine("FBQ_WARN");
    }

    /// <summary>返航打断：编队与在场炸弹一并清除，无结算（拦截奖不发，已入账的逐枚拦截分保留），
    /// 冷却照计。</summary>
    public override void Abort()
    {
        if (_state == State.IDLE)
        {
            return;
        }

        Teardown(aborted: true);
    }

    public override void _Process(double delta)
    {
        // 单帧推进上限（口径单源 FrameCache.MaxStepDelta）：开局巨帧（实测 >1.4s）会让本状态机
        // 逐帧连跳——入场/转弯/投弹表在一两帧内跑完，编队闪现投完弹即离场（玩家与 event-probe
        // 冒烟双双看不到过程）。钳后卡顿帧按小步推进，低帧率下事件周期拉长而非被跳过
        var d = Mathf.Min((float)delta, FrameCache.MaxStepDelta);
        ProcessPendingBombParks(); // 帧末停放不限事件状态——IDLE 期也可能有待停放的回收弹
        if (_state == State.IDLE)
        {
            TickCooldown(d);
            return;
        }

        _stateTime += d;
        _elapsed += d;
        TickIntelHint();
        RefreshEventBar(d);
        // view 全帧一份（FrameCache 每物理帧只算一次）：入场判高、转弯定侧、轰炸判出界共用
        var view = FrameCache.ViewRect();
        switch (_state)
        {
            case State.FORMATION_ENTER:
                _anchor.Y += ApproachSpeed * d;
                if (_anchor.Y >= view.Position.Y + ApproachY)
                {
                    BeginTurn(view);
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
                    ProcessDropWarnings();
                    ProcessDrops();
                    PruneBombs();
                    // 出界余量按投弹表剩余最大时长折算：固定 ±120 会在 hard 5 机
                    // 投弹段截断末机炸弹（余量动态 = 末弹时刻 × 速度）
                    var runMargin = LastDropTime() * RunSpeed;
                    if (_dropIndex >= _dropTimes.Length
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

    /// <summary>单场开局的运行态复位（离场/打断后复用同一实例）。</summary>
    private void ResetRunState()
    {
        _stateTime = 0.0f;
        _elapsed = 0.0f;
        _plannedTotal = 0.0f;
        _heading = Mathf.Pi / 2.0f;
        _exitSpeed = 0.0f;
        _speed = ApproachSpeed;
        _turnTarget = 0.0f;
        _bank = 0.0f;
        _total = 0;
        _alive = 0;
        _dropIndex = 0;
        _warnIndex = 0;
        _dropped = 0;
        _intercepted = 0;
        _settled = false;
        _allClear = false;
        _lineStage = 0;
        _intelAt = float.MaxValue;
        _hudPoll = 0.0f;
    }

    /// <summary>生成编队：长机居中，僚机后掠 ±55px 递增（楔形，槽位稳定）；
    /// 几何与投弹名次/时刻表一次算定（core FormationPlan），此后只按表消费。</summary>
    private void BuildFormation()
    {
        var difficulty = (string)(StringName)GameState.Instance.Difficulty;
        // 难度键条目值判型（craft_counts 只判容器层）——坏值 AsInt64 抛
        // InvalidCastException 崩溃，回退默认 4；条目值域钳 [1,5]（对齐 EliteTurretEvent
        // turret_counts 口径）——巨值生成海量战机节点 OOM/软锁、0/负空跑（占波次槽无实体）
        var craftV = CraftCounts.GetValueOrDefault(difficulty, new Variant());
        var count = Mathf.Clamp(craftV.VariantType is Variant.Type.Int or Variant.Type.Float ? (int)craftV.AsInt64() : 4, 1, 5);
        // HP 三级乘算：基准 × 难度档 × 本局进程 ramp（与普通敌机同口径）
        var hp = Mathf.Max(
            1,
            (int)Mathf.Round(
                CraftHpBase
                * (float)GameState.Instance.EnemyHpMultiplier()
                * (float)GameState.Instance.EnemyHpRamp()));
        var slots = FormationPlan.Wedge(count, WingStep);
        var rank = FormationPlan.DropRank(slots);
        _total = count;
        _alive = count;
        _crafts = new FormationCraft[count];
        _offsets = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            _offsets[i] = new Vector2(slots[i].X, slots[i].Y);
            var index = i; // 闭包捕获副本：C# for 循环变量为单变量，直接捕获会全部指向末索引
            var craft = new FormationCraft();
            craft.Setup(hp);
            craft.Position = _anchor + _offsets[i];
            craft.Rotation = _heading + (Mathf.Pi / 2.0f);
            craft.Died += (c) => OnCraftDied(c, index);
            GetParent().AddChild(craft);
            _crafts[i] = craft;
        }

        var schedule = FormationPlan.Schedule(rank, VolleyBatches, BombsPerCraft, BombInterval, BombStagger, VolleyGap);
        _dropTimes = schedule.Times;
        _dropCraft = schedule.Crafts;
    }

    /// <summary>屏顶进场预告：复用普通波次的预告线组件（同几何、同寿命口径），换编队身份色——
    /// 玩家先看到「这条航道要来东西」，再看编队压下来。</summary>
    private void ShowEntryTelegraph(float x, Rect2 view)
    {
        var telegraph = new SpawnTelegraph
        {
            Position = new Vector2(x, view.Position.Y),
            Duration = GameState.Instance.SpawnerTelegraphDuration(),
            Tint = CommAccent,
        };
        GetParent().AddChild(telegraph);
        _telegraph = telegraph;
    }

    /// <summary>投弹后 4s（警告台词播完）补一条战术提示，且不早于轰炸段开始：
    /// 提示的落点是「炸弹可以怎么处理」，早于第一波投弹播等于空谈。</summary>
    private void TickIntelHint()
    {
        if (_intelAt > _elapsed)
        {
            return;
        }

        _intelAt = float.MaxValue;
        _comm?.ShowLine("FBQ_INTEL");
    }

    /// <summary>把当前侧倾量写到全体在编队机（被击坠槽位跳过）。</summary>
    private void ApplyBank()
    {
        for (var i = 0; i < _crafts.Length; i++)
        {
            var craft = _crafts[i];
            if (craft != null && GodotObject.IsInstanceValid(craft))
            {
                craft.SetBank(_bank);
            }
        }
    }

    /// <summary>转航向：朝较远侧缘方向 90° 转向。</summary>
    private void BeginTurn(Rect2 view)
    {
        _state = State.FORMATION_TURN;
        _stateTime = 0.0f;
        _turnTarget = _anchor.X < view.Position.X + (view.Size.X * 0.5f) ? 0.0f : Mathf.Pi;
    }

    /// <summary>横穿投弹：切到轰炸段并起投弹表消费游标（表在 BuildFormation 已排定序）。</summary>
    private void BeginRun()
    {
        _state = State.BOMBING_RUN;
        _stateTime = 0.0f;
        _dropIndex = 0;
        _warnIndex = 0;
        _bank = 0.0f;
        ApplyBank();
        // 战术提示的播报时刻：警告台词播完之后、且至少晚于轰炸段开始 0.8s
        _intelAt = Mathf.Max(IntelEarliest, _elapsed + 0.8f);
    }

    /// <summary>收回进场预告线（未到寿命才回收）。C# 侧 `?.` 拦不住已释放的 Godot 对象
    /// ——预告线是自毁节点，事件收场时它多半已经不在了，必须先判活。</summary>
    private void DiscardTelegraph()
    {
        if (_telegraph != null && GodotObject.IsInstanceValid(_telegraph))
        {
            _telegraph.QueueFree();
        }

        _telegraph = null;
    }

    /// <summary>离场：沿当前航向加速穿出侧缘（压坡回正，收尾干净）。</summary>
    private void BeginExit()
    {
        _state = State.FORMATION_EXIT;
        _stateTime = 0.0f;
        _exitSpeed = RunSpeed;
        _bank = 0.0f;
        ApplyBank();
        DiscardTelegraph();
    }

    /// <summary>离场结束：结算 → 收场。</summary>
    private void Finish()
    {
        Settle();
        Teardown(aborted: false); // 结果台词留在屏上（Clear 会把刚播的台词一并抹掉）
    }

    /// <summary>统一收场：清场 → 撤 HUD/台词/预告 → 回 IDLE 进冷却 → 释放波次与 Boss 互斥。
    /// 结束与打断都只走这一条（不再各写一份，避免漏撤 HUD 或漏解冻 Boss 这类静默残留）。
    /// <paramref name="aborted"/> 区分「打完收场」与「被返航/死亡打断」：打断路径要清台词、
    /// 且不在结算/死亡画面上补弹 Boss 预警。</summary>
    private void Teardown(bool aborted)
    {
        FreeCrafts();
        FreeBombs();
        if (_hud != null && GodotObject.IsInstanceValid(_hud))
        {
            _hud.HideEventBar(); // HUD 可能先于本节点释放（场景切换），`?.` 拦不住已释放对象
        }

        _hud = null;
        DiscardTelegraph();
        _intelAt = float.MaxValue;
        if (aborted)
        {
            _comm?.Clear(); // 返航恢复后不该残留指向已消失编队的台词
        }

        _state = State.IDLE;
        _cooldownLeft = Cooldown;
        ReleaseBoss(triggerPending: !aborted);
        ResumeWaves();
    }

    /// <summary>结算（只发一次）：全数拦截（一架未坠 + 一枚未投）→ 拦截奖励 + 专属台词；
    /// 全歼 → 全歼台词；放它离场 → 清除台词。逐枚拦截分在拦截当帧已入账，不在此处补发。
    /// 台词与奖励分开判：奖励可为 0（配置），台词不能省——否则玩家分不清「打光了」「拦住了」
    /// 与「它自己走了」。</summary>
    private void Settle()
    {
        if (_settled)
        {
            return;
        }

        _settled = true;
        if (_dropped == 0 && _alive > 0)
        {
            if (RewardIntercept > 0)
            {
                GameState.Instance.AddScore(RewardIntercept);
            }

            _comm?.ShowLine("FBQ_INTERCEPT_BONUS");
            GameState.Instance.PlaySfx(SfxId.Resupply, -4.0, 1.35);
        }
        else if (_allClear)
        {
            _comm?.ShowLine("FBQ_ALL_CLEAR");
        }
        else if (_dropped > 0)
        {
            _comm?.ShowLine("FBQ_CLEAR");
        }
    }

    /// <summary>投弹预警：在时刻表项到点前 BayWarnLead 秒点亮对应机的机腹警示灯。
    /// 预警与投放分两段（预警恒领先，因提前量远大于单帧上限）——投弹动作对玩家是「可预判」的，
    /// 而落点圈只说明「会炸到哪」，不说明「谁要投」。</summary>
    private void ProcessDropWarnings()
    {
        while (_warnIndex < _dropTimes.Length && _stateTime >= _dropTimes[_warnIndex] - BayWarnLead)
        {
            var idx = _dropCraft[_warnIndex];
            _warnIndex++;
            var craft = idx >= 0 && idx < _crafts.Length ? _crafts[idx] : null;
            if (craft != null && GodotObject.IsInstanceValid(craft))
            {
                craft.WarnBay(BayWarnLead);
            }
        }
    }

    /// <summary>按时刻表投弹：投弹点即当前编队机位置（+机腹偏移）；已毁机跳过（时刻表照走）。</summary>
    private void ProcessDrops()
    {
        while (_dropIndex < _dropTimes.Length && _stateTime >= _dropTimes[_dropIndex])
        {
            var idx = _dropCraft[_dropIndex];
            _dropIndex++;
            var craft = idx >= 0 && idx < _crafts.Length ? _crafts[idx] : null;
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
        bomb.ReflectTurnAccel = BombReflectTurnAccel;
        bomb.ReflectSpeedMult = BombReflectSpeedMult;
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
        bomb.Position = craft.Position + (new Vector2(0.0f, 18.0f) * (float)GameState.Instance.WorldScale);
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
            var b = _bombStock[^1];
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

    /// <summary>炸弹回收入池（弹体终局路径回调）：在场表移除 + 拦截计数与逐枚拦截分 + 停用入待停放队列。
    /// 幂等由弹侧 _parked 守卫（ReturnToPool 重入直接返回）。
    /// 拦截分在此即时入账（与击坠分同口径）——玩家拆掉的威胁当场兑现，不押到事件结算。</summary>
    public void ReleaseBomb(FormationBomb bomb)
    {
        if (!GodotObject.IsInstanceValid(bomb))
        {
            return;
        }

        _bombs.Remove(bomb);
        if (bomb.Intercepted)
        {
            _intercepted++;
            if (RewardPerIntercept > 0)
            {
                GameState.Instance.AddScore(RewardPerIntercept);
            }

            // 进度台词：第一次拆下炸弹时敌方的反应（与战损台词共用单条槽位，先到先播）
            if (_lineStage == 0 && _state != State.IDLE)
            {
                _lineStage = 2;
                _comm?.ShowLine("FBQ_TAUNT_INTERCEPT");
            }

            RefreshEventBar(0.0f, force: true);
        }

        bomb.Deactivate();
        _bombStock.Add(bomb);
        _pendingPark.Add(bomb);
    }

    /// <summary>帧末批量停放（idle _Process 处于物理回调外）：reparent 回本节点；
    /// IsParked 仲裁——同帧复活（重新 Activate）的弹跳过，避免活跃弹被误挂进池节点。</summary>
    private void ProcessPendingBombParks()
    {
        if (_pendingPark.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _pendingPark.Count; i++)
        {
            var b = _pendingPark[i];
            if (!GodotObject.IsInstanceValid(b) || b.IsQueuedForDeletion() || !b.IsParked())
            {
                continue;
            }

            if (b.GetParent() != this)
            {
                b.Reparent(this); // reparent 触发 b._ExitTree → UnbindEnemy（停放弹离开注册表）
            }
        }

        _pendingPark.Clear();
    }

    /// <summary>清除在场表中已被外部销毁（清场 sweep）的悬空条目。正常终局（引爆/击落/弹反/
    /// 出界）经 ReturnToPool 同步移除并在回收路径计拦截；本方法只对账未经池回收的外部销毁——
    /// 此类不算拦截（轨道打击清场的炸弹既没炸到人也没被玩家拆掉）。</summary>
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

        _bombs.Clear(); // 外部销毁（清场 sweep）的悬空条目兜底清除
    }

    /// <summary>反射弹的寻敌目标：本事件最近的在场编队机（弹反的语义就是回敬投弹者）。
    /// 供 FormationBomb 每帧调用——不扫描全局敌机注册表，且编队全灭后反射弹自然直飞离场。</summary>
    public bool TryGetNearestCraft(Vector2 from, out Vector2 target)
    {
        var best = float.MaxValue;
        target = default;
        var found = false;
        for (var i = 0; i < _crafts.Length; i++)
        {
            var craft = _crafts[i];
            if (craft == null || !GodotObject.IsInstanceValid(craft))
            {
                continue;
            }

            var pos = craft.Position;
            var dist = pos.DistanceSquaredTo(from);
            if (dist >= best)
            {
                continue;
            }

            best = dist;
            target = pos;
            found = true;
        }

        return found;
    }

    /// <summary>编队驱动：位置 = 锚点 + 随航向旋转的楔形偏移；机头朝航向。</summary>
    private void UpdateCrafts()
    {
        var rot = _heading - (Mathf.Pi / 2.0f);
        var headingRot = _heading + (Mathf.Pi / 2.0f);
        for (var i = 0; i < _crafts.Length; i++)
        {
            var craft = _crafts[i];
            if (craft == null || !GodotObject.IsInstanceValid(craft))
            {
                continue;
            }

            craft.Position = _anchor + _offsets[i].Rotated(rot);
            craft.Rotation = headingRot;
        }
    }

    /// <summary>HUD 事件条：进度 = 全程剩余比，计数行 = 编队机/规模/已拦截。
    /// 0.1s 节流；force 用于「数字变了必须立刻反映」的路径（击坠/拦截）。</summary>
    private void RefreshEventBar(float delta, bool force = false)
    {
        if (_hud == null || !GodotObject.IsInstanceValid(_hud))
        {
            return;
        }

        if (!force)
        {
            _hudPoll -= delta;
            if (_hudPoll > 0.0f)
            {
                return;
            }
        }

        _hudPoll = HudInterval;
        var fill = _plannedTotal > 0.0f ? Mathf.Clamp(1.0f - (_elapsed / _plannedTotal), 0.0f, 1.0f) : 0.0f;
        _hud.UpdateEventBar(fill, _alive, _total, _intercepted);
    }

    /// <summary>击坠：单机得分走 AddKillScore（连击+1、乘区放大后照常过难度倍率）；
    /// 全歼 → 全歼奖励 AddScore（不计连击）+ 提前离场。</summary>
    private void OnCraftDied(FormationCraft craft, int index)
    {
        if (index >= 0 && index < _crafts.Length && ReferenceEquals(_crafts[index], craft))
        {
            _crafts[index] = null;
        }

        _alive = Mathf.Max(0, _alive - 1);
        GameState.Instance.AddKillScore(CraftScore);
        // 进度台词：第一次战损时敌方指挥官的反应（与拦截台词共用单条槽位）
        if (_lineStage == 0 && _state != State.IDLE)
        {
            _lineStage = 1;
            _comm?.ShowLine("FBQ_TAUNT_LOSS");
        }

        RefreshEventBar(0.0f, force: true);
        if (_alive == 0 && _state != State.IDLE)
        {
            // 离场段（FORMATION_EXIT）击落最后一架同样记全歼——原守卫把奖励与台词
            // 一并跳过，玩家打光编队只拿「它自己走了」档；只有 BeginExit 需要防重入
            _allClear = true;
            if (RewardAllClear > 0)
            {
                GameState.Instance.AddScore(RewardAllClear);
            }

            if (_state != State.FORMATION_EXIT)
            {
                BeginExit();
            }
        }
    }

    private void FreeCrafts()
    {
        foreach (var craft in _crafts)
        {
            if (craft != null && GodotObject.IsInstanceValid(craft))
            {
                craft.QueueFree();
            }
        }

        _crafts = System.Array.Empty<FormationCraft?>();
        _offsets = System.Array.Empty<Vector2>();
        _dropTimes = System.Array.Empty<float>();
        _dropCraft = System.Array.Empty<int>();
        _total = 0;
        _alive = 0;
    }

    /// <summary>投弹表末刻（无弹为 0）——离场余量与计划时长的共同输入。</summary>
    private float LastDropTime() => _dropTimes.Length == 0 ? 0.0f : _dropTimes[^1];
}
