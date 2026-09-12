using Godot;
using InfiAir.Core.Events;

namespace InfiAir;

/// <summary>
/// 统一游戏事件管理器：批量管理全部随机游戏事件。
/// 挂载：GameState autoload 子节点（维持唯一 autoload 约定；经 GameState.Events 全局访问）。
/// 设计要点：
///   - 统一注册表 EVENT_FACTORIES（id -> 工厂 Callable，唯一事实源）：迷雾 4 事件默认注册，
///     遭遇事件（精英炮塔/轰炸编队）由 Main._Ready 经 RegisterEncounter() 注入缓存单例；
///   - 分组并发：fog | encounter 两组，组内单事件并发、组间可并行（保持现状：迷雾事件
///     可与遭遇事件并行，遭遇事件彼此/Boss 互斥）；
///   - 统一触发策略：fog 组沿用 fog_events.* 配置；encounter 组沿用 elite_turret_event.* /
///     formation_strike_event.*（balance.json key 零变化，仅读取方移动）；
///   - 统一生命周期与信号：EventStarted/EventEnded；fog 事件走 GameEvent 契约
///     （Start(ctx,duration) → Tick → 到期 End，context 由迷雾门面构建）；遭遇事件保持
///     Node 形态（自驱 FSM），管理器调 Start()/Abort() 并轮询 IsActive() 检测结束；
///   - 接线门控：_fogWired 由迷雾门面 Wire() 开启，_runActive 由 Main 设置；
///     遭遇触发门控 = 注入 Spawner 处理中 + 事件 CanTrigger。
/// 生产调用全部 C# typed（IEncounterEvent/GameEvent 契约）；无动态派发。
/// </summary>
public partial class GameEventManager : Node
{
    /// <summary>统一事件信号：事件开始（duration 秒；遭遇事件为 0，FSM 自驱时长）。</summary>
    [Signal]
    public delegate void EventStartedEventHandler(StringName eventId, float duration);

    /// <summary>统一事件信号：事件结束（迷雾到期/提前结束；遭遇 FSM 回 IDLE）。</summary>
    [Signal]
    public delegate void EventEndedEventHandler(StringName eventId);

    /// <summary>分组常量（组内单事件并发，组间并行）。</summary>
    public static readonly StringName GroupFog = new StringName("fog");

    public static readonly StringName GroupEncounter = new StringName("encounter");

    /// <summary>分组常量实例属性转发（UPPER_SNAKE 访问口径保持）。</summary>
    public StringName GROUP_FOG => GroupFog;

    public StringName GROUP_ENCOUNTER => GroupEncounter;

    /// <summary>事件工厂注册表（唯一事实源；迷雾默认注册，遭遇经 register_encounter 注入）。</summary>
    public Godot.Collections.Dictionary EVENT_FACTORIES { get; set; } = new()
    {
        [new StringName("fake_enemies")] = Callable.From(() => new FakeEnemiesEvent()),
        [new StringName("mental_confusion")] = Callable.From(() => new ConfusionEvent()),
        [new StringName("bullet_malfunction")] = Callable.From(() => new BulletMalfunctionEvent()),
        [new StringName("direction_shift")] = Callable.From(() => new DirectionShiftEvent()),
    };

    // ---------------- fog 组配置（balance.json fog_events.*；脚本值为缺键回退） ----------------

    public bool FOG_ENABLED { get; set; } = true;

    public float FOG_TRIGGER_CHANCE { get; set; } = 0.35f;

    public float FOG_CHECK_INTERVAL { get; set; } = 3.0f;

    public float FOG_MIN_INTERVAL { get; set; } = 12.0f;

    public float FOG_FIRST_DELAY { get; set; } = 25.0f;

    public Godot.Collections.Dictionary FOG_WEIGHTS { get; set; } = new()
    {
        [new StringName("fake_enemies")] = 1.0f,
        [new StringName("mental_confusion")] = 1.0f,
        [new StringName("bullet_malfunction")] = 1.0f,
        [new StringName("direction_shift")] = 1.0f,
    };

    public Godot.Collections.Dictionary FOG_EVENT_DURATIONS { get; set; } = new()
    {
        [new StringName("fake_enemies")] = 8.0f,
        [new StringName("mental_confusion")] = 6.0f,
        [new StringName("bullet_malfunction")] = 7.0f,
        [new StringName("direction_shift")] = 6.0f,
    };

    // ---------------- encounter 组配置（balance.json elite_turret_event.* / formation_strike_event.*） ----------------

    /// <summary>遭遇事件触发策略（id -> {interval, chance, min_score}；register_encounter 时读 balance）。</summary>
    public Godot.Collections.Dictionary ENCOUNTER_CONFIG { get; set; } = new();

    // ---------------- 运行时状态 ----------------

    /// <summary>空 StringName 复用——避免每帧构造 `new StringName()` 的 native 引用计数分配。</summary>
    private static readonly StringName EmptyId = new();

    /// <summary>遭遇触发参数值缓存（注册时一次性固化 interval/chance/min_score——
    /// Tick 每帧免 StringName 键构造 + Variant 装箱往返。值而非引用：ENCOUNTER_CONFIG 仅
    /// LoadBalance 重建、注册后无写入方（重载属诊断路径，见 ReloadConfig 注释），值缓存无可见性风险）。</summary>
    private readonly Dictionary<StringName, EncounterTriggerCfg> _encounterTrig = new();

    /// <summary>遭遇触发参数（注册时固化，见 _encounterTrig）。</summary>
    private sealed class EncounterTriggerCfg(float interval, float chance, int minScore)
    {
        public readonly float Interval = interval;
        public readonly float Chance = chance;
        public readonly int MinScore = minScore;
    }

    /// <summary>遭遇实例缓存（注册工厂即返回该实例的闭包——直接缓存实例，
    /// 消除 Poll/Tick 每帧 Callable.Call 动态派发；IsInstanceValid 校验防场景重载后死节点）。</summary>
    private readonly Dictionary<StringName, Node> _encounterInstance = new();

    private bool _runActive;
    /// <summary>迷雾组是否已接线（GameState 在迷雾门面 Wire() 时开启；未接线则本组完全惰性）。</summary>
    private bool _fogWired;
    private StringName _fogActiveId = EmptyId;
    private GameEvent? _fogActiveEvent;
    private Godot.Timer? _fogTimer;
    private float _fogCooldownLeft;
    private float _fogFirstDelayLeft;
    private float _fogCheckTimer;
    /// <summary>遭遇事件注册顺序（触发检查按注册序；main 先注册 elite 再 formation，保持原优先级）。</summary>
    private readonly Godot.Collections.Array<StringName> _encounterOrder = new();
    /// <summary>遭遇触发策略计时器（id -> 剩余秒）。CLR 字典镜像——避免每帧 GetValueOrDefault/
    /// 写入的 Variant 装箱 + native 往返。</summary>
    private readonly Dictionary<StringName, float> _encounterTimers = new();
    /// <summary>遭遇事件活跃快照（id -> bool；轮询检测结束发 event_ended）。</summary>
    /// <summary>遭遇结束信号待发集合——end_active 打断后 FSM 未立即回 IDLE 时
    /// 记 pending，由轮询在检测到回 IDLE 后统一补发（防双发/发在事件仍活跃时）。</summary>
    private readonly Godot.Collections.Dictionary _encounterEndPending = new();
    /// <summary>当前激活的遭遇事件 id（无则空）。</summary>
    private StringName _encounterActiveId = EmptyId;

    /// <summary>PickFogId 专用临时缓冲：仅同步流程内短暂使用，复用避免每次触发新建 Array。</summary>
    private readonly Godot.Collections.Array<StringName> _fogPickBuffer = new();
    /// <summary>spawner 依赖注入（main._ready 调用；遭遇触发门控 + 特殊槽通知，依赖注入延续）。</summary>
    /// <summary>遭遇触发门控注入（typed 化，消除每帧 HasMethod/Call）。</summary>
    private Spawner? _spawner;

    public override void _Ready()
    {
        LoadBalance();
        _fogCheckTimer = FOG_CHECK_INTERVAL;
        _fogFirstDelayLeft = FOG_FIRST_DELAY;
        // 无本局时完全惰性（_runActive 初值 false；标题屏不跑 Poll/Tick，SetRunActive 翻转启停）
        SetProcess(false);
    }

    private void LoadBalance()
    {
        FOG_ENABLED = GameState.Instance.Cfg("fog_events.enabled", FOG_ENABLED).AsBool();
        FOG_TRIGGER_CHANCE = (float)GameState.Instance.Cfg("fog_events.trigger_chance", FOG_TRIGGER_CHANCE).AsDouble();
        // ≤0 会每帧掷签
        FOG_CHECK_INTERVAL = Mathf.Max(
            (float)GameState.Instance.Cfg("fog_events.check_interval", FOG_CHECK_INTERVAL).AsDouble(), 0.1f);
        FOG_MIN_INTERVAL = Mathf.Max((float)GameState.Instance.Cfg("fog_events.min_interval", FOG_MIN_INTERVAL).AsDouble(), 0.0f);
        FOG_FIRST_DELAY = Mathf.Max((float)GameState.Instance.Cfg("fog_events.first_delay", FOG_FIRST_DELAY).AsDouble(), 0.0f);
        var weights = GameState.Instance.Cfg("fog_events.weights", FOG_WEIGHTS);
        if (weights.VariantType == Variant.Type.Dictionary)
        {
            FOG_WEIGHTS = weights.AsGodotDictionary();
        }

        var durations = GameState.Instance.Cfg("fog_events.durations", FOG_EVENT_DURATIONS);
        if (durations.VariantType == Variant.Type.Dictionary)
        {
            FOG_EVENT_DURATIONS = durations.AsGodotDictionary();
        }

        // 遭遇触发策略（与 spawner 原读键一致，balance.json 零变化）；
        // 判型经 CfgFx：坏类型（字符串/数组）直接 AsDouble/AsInt64 会抛 InvalidCastException
        // 崩在管理器 _Ready，回退脚本默认值才是「坏了也能开局」的口径
        ENCOUNTER_CONFIG = new Godot.Collections.Dictionary
        {
            [new StringName("elite_turret")] = new Godot.Collections.Dictionary
            {
                ["interval"] = CfgFx.Float("elite_turret_event.trigger_interval", 45.0f, 0.1f),
                ["chance"] = CfgFx.Float("elite_turret_event.trigger_chance", 0.35f, 0.0f, 1.0f),
                ["min_score"] = CfgFx.Int("elite_turret_event.min_score", 800, 0),
            },
            [new StringName("formation_strike")] = new Godot.Collections.Dictionary
            {
                ["interval"] = CfgFx.Float("formation_strike_event.trigger_interval", 40.0f, 0.1f),
                ["chance"] = CfgFx.Float("formation_strike_event.trigger_chance", 0.30f, 0.0f, 1.0f),
                ["min_score"] = CfgFx.Int("formation_strike_event.min_score", 500, 0),
            },
        };
    }

    /// <summary>配置重载公开入口（GameState.ReloadBalance 联动——只刷平衡缓存
    /// 会让 fog 配置停留旧值，与运行时不一致）。注意：遭遇组（ENCOUNTER_CONFIG）注册时
    /// 固化内层字典引用，重载后遭遇策略仍为注册时值——重载属诊断路径，
    /// 运行时 _Ready 只走一次。</summary>
    public void ReloadConfig() => LoadBalance();

    // ---------------- 对外公开接口（诊断/外部驱动经公开接口） ----------------

    public bool IsRunActive() => _runActive;

    /// <summary>本局活跃开关（main._ready/_exit_tree 设置；非活跃时强制结束进行中的迷雾事件）。
    /// 激活时必须重置遭遇触发计时与 fog 开局保护/检查计时——否则死亡重开/重进 main 会继承
    /// 上局剩余值（遭遇计时可 ≤0 → 新局开局即触发精英/编队；fog 每进程一次保护、第二局开局即触发）。</summary>
    public void SetRunActive(bool active)
    {
        if (active == _runActive)
        {
            return;
        }

        _runActive = active;
        // 帧驱动随本局开关——非活跃时 Poll/Tick 全为无操作空转（标题屏每帧白跑）
        SetProcess(active);
        if (!active)
        {
            EndFog();
            // 遭遇活跃态一并复位（防场景重入残留 → 对从未 start 的新实例广播幽灵
            // EventEnded 残留）
            _encounterActiveId = EmptyId;
            _encounterEndPending.Clear();
            return;
        }

        // 按注册序重置（计时键 = 注册 id，与 _encounterOrder 同集；interval 读注册固化值）
        foreach (var id in _encounterOrder)
        {
            if (_encounterTrig.TryGetValue(id, out var trig))
            {
                _encounterTimers[id] = trig.Interval;
            }
        }

        _fogFirstDelayLeft = FOG_FIRST_DELAY;
        _fogCheckTimer = FOG_CHECK_INTERVAL;
        // fog 冷却必须重置——否则上局事件结束残留的
        // _fog_cooldown_left 会额外推迟新局首个迷雾事件（最晚 12s）
        _fogCooldownLeft = 0.0f;
    }

    /// <summary>迷雾组接线（GameState 在迷雾门面 wire() 时调用；开启后本组触发/生命周期由本管理器接管）。</summary>
    public void ActivateFog()
    {
        _fogWired = true;
        _fogCheckTimer = FOG_CHECK_INTERVAL;
        _fogFirstDelayLeft = FOG_FIRST_DELAY;
    }

    /// <summary>遭遇事件注册（main._ready 调用；实例由 main 创建并挂 Main 下；
    /// 注册进统一注册表并初始化触发计时）。</summary>
    public void RegisterEncounter(StringName pId, Node pEvent)
    {
        // 事件实例随 Main 释放后，长命管理器仍持有工厂闭包——重进 main
        // 的再注册窗口内 PollEncounters/EventFor 调旧闭包会触碰已释放实例（ObjectDisposedException）；
        // 闭包内必须判活，死实例 Yield Nil（两处调用点 StartFog/EventFor 均容忍 Nil，再注册后自愈）
        EVENT_FACTORIES[pId] = Callable.From<Node?>(() => GodotObject.IsInstanceValid(pEvent) ? pEvent : null);
        if (!_encounterOrder.Contains(pId))
        {
            _encounterOrder.Add(pId);
        }

        // 触发参数与实例一次性缓存（Tick 每帧读缓存，不再每帧解析/分配；
        // 参数值固化——注册后无写入方，重载属诊断路径不改遭遇策略，见 ReloadConfig 注释）
        // 条目级判型（与 LoadBalance 同口径）：注册发生在 Main._Ready，异常在这里同样开不了局
        var cfg = ENCOUNTER_CONFIG.GetValueOrDefault(pId, new Godot.Collections.Dictionary());
        var dict = cfg.AsGodotDictionary();
        _encounterTrig[pId] = new EncounterTriggerCfg(
            Num(dict, "interval", 45.0f, 0.1f, float.MaxValue),
            Num(dict, "chance", 0.3f, 0.0f, 1.0f),
            (int)Num(dict, "min_score", 0.0f, 0.0f, int.MaxValue));
        _encounterInstance[pId] = pEvent;
        if (!_encounterTimers.ContainsKey(pId))
        {
            _encounterTimers[pId] = _encounterTrig[pId].Interval;
        }
    }

    /// <summary>注册表条目取值（判型 + 域钳）：坏类型回退默认，不抛 InvalidCastException。</summary>
    private static float Num(Godot.Collections.Dictionary dict, string key, float def, float min, float max)
    {
        var v = dict.GetValueOrDefault(key, def);
        var x = v.VariantType is Variant.Type.Int or Variant.Type.Float ? (float)v.AsDouble() : def;
        return Mathf.Clamp(x, min, max);
    }

    /// <summary>spawner 依赖注入（main._ready 调用；遭遇触发门控 + 触发时占用特殊槽）。</summary>
    public void SetSpawner(Node spawner) => _spawner = spawner as Spawner;

    /// <summary>已注册事件 id 列表（EVENT_FACTORIES 为唯一事实源）。每次调用返回新数组，
    /// 调用方可安全持有结果（内部临时缓冲不对外暴露）。</summary>
    public Godot.Collections.Array<StringName> EventIds()
    {
        var ids = new Godot.Collections.Array<StringName>();
        foreach (var k in EVENT_FACTORIES.Keys)
        {
            ids.Add(k.AsStringName());
        }

        return ids;
    }

    /// <summary>指定分组当前激活事件 id（无则空）。</summary>
    public StringName ActiveId(StringName pGroup)
    {
        if (pGroup == GroupFog)
        {
            return _fogActiveId;
        }

        if (pGroup == GroupEncounter)
        {
            return _encounterActiveId;
        }

        return EmptyId;
    }

    /// <summary>指定分组当前激活事件对象（无则 null）。</summary>
    public Variant ActiveEvent(StringName pGroup)
    {
        if (pGroup == GroupFog)
        {
            return _fogActiveEvent != null ? Variant.From(_fogActiveEvent) : new Variant();
        }

        if (pGroup == GroupEncounter)
        {
            var e = EventFor(_encounterActiveId);
            return e != null ? Variant.From(e) : new Variant();
        }

        return new Variant();
    }

    /// <summary>指定分组是否可触发（fog：启用 + run_active + 无进行中 + 开局保护/冷却结束）。</summary>
    public bool CanTriggerGroup(StringName pGroup)
    {
        if (pGroup == GroupFog)
        {
            return _fogWired
                && FOG_ENABLED
                && _runActive
                && _fogActiveId == EmptyId
                && _fogFirstDelayLeft <= 0.0f
                && _fogCooldownLeft <= 0.0f;
        }

        return false;
    }

    /// <summary>指定分组自动触发路径单步检查（fog：资格满足则按权重掷签并启动；返回是否触发）。</summary>
    public bool TryTriggerGroup(StringName pGroup)
    {
        if (pGroup == GroupFog)
        {
            if (!CanTriggerGroup(GroupFog))
            {
                return false;
            }

            if (GD.Randf() >= FOG_TRIGGER_CHANCE)
            {
                return false;
            }

            var id = PickFogId();
            if (id == EmptyId)
            {
                return false; // 空注册表防御
            }

            return StartFog(id);
        }

        return false;
    }

    /// <summary>强制启动一次已注册遭遇（诊断/无头探针入口）：与自动触发共用同一条启动路径
    /// ——活跃 id 登记、特殊槽通知、EventStarted 广播一并走齐。绕过管理器直调 Start 会让
    /// 「事件在跑、管理器不知道」，只能靠轮询兜底自愈；触发路径因此只留这一条。</summary>
    public bool TryStartEncounter(StringName pId)
    {
        if (!_encounterOrder.Contains(pId))
        {
            return false;
        }

        var ev = EventFor(pId);
        if (!GodotObject.IsInstanceValid(ev) || ev is not IEncounterEvent enc || enc.IsActive())
        {
            return false;
        }

        StartEncounter(pId, enc);
        return true;
    }

    /// <summary>立即结束指定分组进行中的事件（fog：清理效果；encounter：abort 打断）。</summary>
    public void EndActive(StringName pGroup)
    {
        if (pGroup == GroupFog)
        {
            EndFog();
        }
        else if (pGroup == GroupEncounter)
        {
            var id = _encounterActiveId;
            if (id == EmptyId)
            {
                return;
            }

            var ev = EventFor(id);
            if (ev is IEncounterEvent enc) // typed（Abort 进遭遇契约接口）
            {
                enc.Abort();
            }

            _encounterActiveId = EmptyId;
            // event_ended 统一由轮询在 FSM 回 IDLE 后发——否则此处即发 + 轮询再发会双发，
            // 且第二次发在事件仍活跃时；同步回 IDLE 则本处补发，异步则记 pending 由轮询补发
            var stillActive = ev is IEncounterEvent enc2 && enc2.IsActive(); // typed
            if (stillActive)
            {
                _encounterEndPending[id] = true;
            }
            else
            {
                EmitSignal(SignalName.EventEnded, id);
            }
        }
    }

    /// <summary>全部事件终止（返航/死亡路径：迷雾清除 + 遭遇打断）。</summary>
    public void EndAll()
    {
        EndFog();
        EndActive(GroupEncounter);
    }

    /// <summary>当前 fog 事件剩余时长（无事件返回 0）。</summary>
    public float ActiveRemaining()
    {
        if (_fogTimer == null || !GodotObject.IsInstanceValid(_fogTimer))
        {
            return 0.0f;
        }

        return (float)_fogTimer.TimeLeft;
    }

    // ---------------- 触发与编排 ----------------

    public override void _Process(double delta)
    {
        // autoload _Process 帧序在 main 场景 Spawner 之前——同帧 Boss/遭遇竞态
        // 由事件先启动（SetBossFrozen(true)），Boss 推迟至事件结束 + boss_resume_delay，
        // 仍保证触发不累积
        var d = (float)delta;
        PollEncounters();
        // fog 组（未接线前惰性，避免双驱动）
        if (_fogWired)
        {
            if (_fogActiveId != EmptyId)
            {
                // 事件进行中：逐帧驱动事件自持效果（duration 计时由 _fog_timer 负责；
                // 运行中事件不受 enabled 总开关关闭影响，跑完自然结束）
                if (_fogActiveEvent != null)
                {
                    _fogActiveEvent.Tick(d);
                }
            }
            else if (_runActive && FOG_ENABLED)
            {
                // 总开关关闭时自动触发路径完全惰性
                if (_fogFirstDelayLeft > 0.0f)
                {
                    _fogFirstDelayLeft -= d;
                }
                else if (_fogCooldownLeft > 0.0f)
                {
                    _fogCooldownLeft -= d;
                }
                else
                {
                    _fogCheckTimer -= d;
                    if (_fogCheckTimer <= 0.0f)
                    {
                        _fogCheckTimer = FOG_CHECK_INTERVAL;
                        if (GD.Randf() < FOG_TRIGGER_CHANCE)
                        {
                            StartFog(PickFogId());
                        }
                    }
                }
            }
        }

        // encounter 组
        if (CanDriveEncounters())
        {
            TickEncounterTriggers(d);
        }
    }

    /// <summary>遭遇触发驱动权门控（契约单点）：本类 _Process 依赖「autoload 树序先于 main 场景
    /// 处理」这一引擎保证——同帧 Boss/遭遇竞态由事件先启动 SetBossFrozen(true)、Boss
    /// 推迟至事件结束 + boss_resume_delay 兜住，触发不累积；本端提供 spawner 状态的逐帧重验。
    /// IsInsideTree 前置：本局回标题屏的场景切换立即摘树，本帧已排队的 _Process 仍会触发这一
    /// 次，摘树后对 spawner 调 CanProcess 会报原生 !is_inside_tree 错误。SummonInProgress：母舰
    /// 召唤蓄力/机库小窗窗口期不掷签（玩家锁输入 + 999s 无敌，事件奖励会被母舰自动火力白拿
    /// ——召唤蓄力与遭遇事件的互斥窗口补全）。</summary>
    private bool CanDriveEncounters()
    {
        return IsInsideTree()
            && !GameState.Instance.SummonInProgress
            && _spawner != null && GodotObject.IsInstanceValid(_spawner) && _spawner.IsInsideTree()
            && _spawner.IsProcessing() && _spawner.CanProcess();
    }

    /// <summary>遭遇事件触发检查（触发条件与计时推进的判定在 core EncounterTrigger，可单测）：
    /// 按注册序逐个——事件自身就绪、Boss 未激活、组内无其他遭遇在跑、分数达标才算有资格；
    /// 无资格时计时冻结（不累积），到点按概率掷签启动。</summary>
    private void TickEncounterTriggers(float delta)
    {
        var score = GameState.Instance.Score;
        foreach (var id in _encounterOrder)
        {
            // 遭遇事件 typed 分派（IEncounterEvent 契约，替代每帧 HasMethod/Call 动态派发）
            var ev = EventFor(id);
            // EventFor fallback 工厂闭包捕获的正是已失效实例，可返回死引用
            // （正常路径被 RegisterEncounter 覆盖缓存防护）——触发侧二次判活，死引用不可触发
            if (!GodotObject.IsInstanceValid(ev) || ev is not IEncounterEvent enc || enc.IsActive())
            {
                continue;
            }

            // 注册时固化的触发参数（零分配读取；替代每帧 Variant 字典往返）
            if (!_encounterTrig.TryGetValue(id, out var trig))
            {
                continue;
            }

            var eligible = EncounterTrigger.Eligible(
                enc.CanTrigger(), BossActive(), AnyOtherEncounterActive(id), score, trig.MinScore);
            var step = EncounterTrigger.Advance(
                _encounterTimers.GetValueOrDefault(id, trig.Interval), delta, trig.Interval, eligible, score, trig.MinScore);
            _encounterTimers[id] = step.Remaining;
            if (step.Due && GD.Randf() < trig.Chance)
            {
                StartEncounter(id, enc);
            }
        }
    }

    /// <summary>Boss 是否占用遭遇槽（注入的 spawner 判活后直读；未注入按未激活处理）。</summary>
    private bool BossActive()
        => _spawner != null && GodotObject.IsInstanceValid(_spawner) && _spawner.IsBossActive();

    /// <summary>组内除本事件外是否有别的遭遇在跑（组内单活跃；未来新增遭遇自动沿用，无需硬编码 id）。</summary>
    private bool AnyOtherEncounterActive(StringName pId)
    {
        foreach (var other in _encounterOrder)
        {
            if (other == pId)
            {
                continue;
            }

            var o = EventFor(other);
            if (o is IEncounterEvent oe && oe.IsActive())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>遭遇事件启动：调事件 start()（事件内部处理波次/Boss 钩子），登记活跃并广播。</summary>
    private void StartEncounter(StringName pId, IEncounterEvent ev)
    {
        ev.Start();
        _encounterActiveId = pId;
        EmitSignal(SignalName.EventStarted, pId, 0.0f);
        // 事件占用特殊槽（镜像 spawner 原 _waves_since_special = 0）
        if (_spawner != null && GodotObject.IsInstanceValid(_spawner))
        {
            _spawner.NotifyEventTriggered();
        }
    }

    /// <summary>轮询遭遇事件结束（FSM 回 IDLE → 广播 event_ended；手动 start 亦被覆盖检测；
    /// pending 打断在检测到回 IDLE 后补发，信号恒在事件不活跃时发、恒只发一次）。</summary>
    private void PollEncounters()
    {
        foreach (var id in _encounterOrder)
        {
            var ev = EventFor(id);
            // 死引用按「不活跃」处理——直接跳过会让 _encounterActiveId 残留、编排卡死；
            // 按不活跃走下方分支把事件状态复位并广播结束，属自愈路径
            var active = GodotObject.IsInstanceValid(ev) && ev is IEncounterEvent enc && enc.IsActive(); // typed 分派
            if (_encounterEndPending.ContainsKey(id) && !active)
            {
                _encounterEndPending.Remove(id);
                _encounterActiveId = EmptyId;
                EmitSignal(SignalName.EventEnded, id);
            }
            else if (_encounterActiveId == id && !active)
            {
                _encounterActiveId = EmptyId;
                EmitSignal(SignalName.EventEnded, id);
            }
            else if (active && _encounterActiveId == EmptyId)
            {
                _encounterActiveId = id; // 手动 start 兜底登记
            }
        }
    }

    /// <summary>加权随机选 fog 事件（weights 缺键回退 1.0；注册表为空返回空；
    /// 全零权重退化为均匀随机，不得恒选首个（roll=0 会立即命中第一项）。</summary>
    /// <summary>条目级判型（FakeEnemiesEvent 同族口径）——fog weights/durations 坏值
    /// （字符串/数组）回退默认，不抛 InvalidCastException。</summary>
    private static double FogNum(Godot.Collections.Dictionary dict, StringName key, double def)
    {
        var v = dict.GetValueOrDefault(key, def);
        return v.VariantType is Variant.Type.Int or Variant.Type.Float ? v.AsDouble() : def;
    }

    private StringName PickFogId()
    {
        var ids = _fogPickBuffer;
        ids.Clear();
        foreach (var k in EVENT_FACTORIES.Keys)
        {
            var id = k.AsStringName();
            if (GroupOf(id) == GroupFog)
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return EmptyId; // 空注册表防御
        }

        var total = 0.0f;
        foreach (var id in ids)
        {
            total += Mathf.Max((float)FogNum(FOG_WEIGHTS, id, 1.0), 0.0f);
        }

        if (total <= 0.0f)
        {
            return ids[(int)(GD.Randi() % (uint)ids.Count)]; // 全零权重：均匀回退
        }

        var roll = GD.Randf() * total;
        foreach (var id in ids)
        {
            roll -= Mathf.Max((float)FogNum(FOG_WEIGHTS, id, 1.0), 0.0f);
            if (roll <= 0.0f)
            {
                return id;
            }
        }

        return ids[0];
    }

    /// <summary>迷雾事件启动：实例化 → duration Godot.Timer 先行（健壮性，镜像 FogEventManager）→
    /// 迷雾门面构建 context 注入 → start + 广播。context 键约定见 FogEvent 访问器；
    /// 通用键 "request_end" 由 GameEvent.request_end 使用（事件可主动提前结束）。</summary>
    private bool StartFog(StringName pId)
    {
        var factory = EVENT_FACTORIES.GetValueOrDefault(pId, new Variant());
        if (factory.VariantType != Variant.Type.Callable)
        {
            return false; // 注册表条目防御：id 未注册或条目非 Callable
        }

        _fogActiveId = pId;
        var evRaw = factory.AsCallable().Call();
        var ev = evRaw.VariantType == Variant.Type.Object ? evRaw.AsGodotObject() as GameEvent : null;
        if (ev == null)
        {
            _fogActiveId = EmptyId;
            return false;
        }

        _fogActiveEvent = ev;
        var duration = Mathf.Max((float)FogNum(FOG_EVENT_DURATIONS, pId, 6.0), CfgFx.IntervalFloor);
        if (_fogTimer != null && GodotObject.IsInstanceValid(_fogTimer))
        {
            _fogTimer.Stop(); // 防御：异常时序下的旧 timer 残留
            _fogTimer.QueueFree();
        }

        _fogTimer = TimerFx.OneShot(this, duration, EndFog);
        // context：迷雾门面构建（视觉容器/覆盖层/方向脉冲回调），request_end 回调指向本管理器
        var layer = FogLayer();
        Godot.Collections.Dictionary ctx;
        if (layer != null)
        {
            ctx = layer.BuildFogContext(Callable.From(EndFog));
        }
        else
        {
            ctx = new Godot.Collections.Dictionary { ["request_end"] = Callable.From(EndFog) };
        }

        ev.Start(ctx, duration);
        EmitSignal(SignalName.EventStarted, pId, duration);
        return true;
    }

    private void EndFog()
    {
        var id = _fogActiveId;
        if (id == EmptyId)
        {
            return; // 防御：重复结束（timer 回调 + 外部 end_active 竞态）
        }

        _fogActiveId = EmptyId;
        if (_fogTimer != null && GodotObject.IsInstanceValid(_fogTimer))
        {
            _fogTimer.Stop();
            _fogTimer.QueueFree();
            _fogTimer = null;
        }

        if (_fogActiveEvent != null)
        {
            _fogActiveEvent.End(); // 事件类清理自持效果（幂等）
        }

        _fogActiveEvent = null;
        _fogCooldownLeft = FOG_MIN_INTERVAL;
        _fogCheckTimer = FOG_CHECK_INTERVAL;
        EmitSignal(SignalName.EventEnded, id);
    }

    // ---------------- 内部辅助 ----------------

    /// <summary>注册表工厂取已注册实例（遭遇缓存单例；fog 事件返回新实例——仅 _event_for 用）。</summary>
    private Node? EventFor(StringName pId)
    {
        // 注册实例缓存优先（零动态派发；场景重载后旧实例失效 → fallback 工厂）
        if (_encounterInstance.TryGetValue(pId, out var cached) && GodotObject.IsInstanceValid(cached))
        {
            return cached;
        }

        var factory = EVENT_FACTORIES.GetValueOrDefault(pId, new Variant());
        if (factory.VariantType == Variant.Type.Callable)
        {
            var v = factory.AsCallable().Call();
            return v.VariantType == Variant.Type.Object ? v.AsGodotObject() as Node : null;
        }

        return null;
    }

    /// <summary>事件所属分组（遭遇注册序表为准；其余按注册表默认 fog）。</summary>
    public StringName GroupOf(StringName pId) => _encounterOrder.Contains(pId) ? GroupEncounter : GroupFog;

    /// <summary>迷雾门面（效果层/context 构建；GameState.fog_events）。</summary>
    private FogEventManager? FogLayer()
    {
        return GameState.Instance.FogEvents;
    }
}
