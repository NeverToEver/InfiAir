using Godot;

namespace InfiAir;

/// <summary>
/// 统一游戏事件管理器（docs/EVENT_MANAGER.md；M 批次全量迁移）：批量管理全部随机游戏事件。
/// 挂载：GameState autoload 子节点（维持唯一 autoload 约定；经 GameState.events 全局访问）。
/// 设计要点：
///   - 统一注册表 EVENT_FACTORIES（id -> 工厂 Callable，唯一事实源）：迷雾 4 事件默认注册，
///     遭遇事件（精英炮塔/轰炸编队）由 main._ready 经 register_encounter() 注入缓存单例；
///   - 分组并发：fog | encounter 两组，组内单事件并发、组间可并行（保持现状：迷雾事件
///     可与遭遇事件并行，遭遇事件彼此/Boss 互斥）；
///   - 统一触发策略：fog 组沿用 fog_events.* 配置；encounter 组沿用 elite_turret_event.* /
///     formation_strike_event.*（balance.json key 零变化，仅读取方移动）；
///   - 统一生命周期与信号：event_started/event_ended；fog 事件走 GameEvent 契约
///     （start(ctx,duration) → tick → 到期 end，context 由迷雾门面构建）；遭遇事件保持
///     Node 形态（自驱 FSM），管理器调 start()/abort() 并轮询 is_active() 检测结束；
///   - 接线门控：_fog_wired 由迷雾门面 wire() 开启，_run_active 由 main 设置；
///     遭遇触发门控 = 注入 spawner 处理中 + 事件 can_trigger。
/// 鸭子调用（Call 动态派发）；fog 事件为 C# GameEvent 子类强类型调用。
/// </summary>
public partial class GameEventManager : Node
{
    /// <summary>统一事件信号：事件开始（duration 秒；遭遇事件为 0，FSM 自驱时长）。</summary>
    [Signal]
    public delegate void EventStartedEventHandler(StringName eventId, float duration);

    /// <summary>统一事件信号：事件结束（迷雾到期/提前结束；遭遇 FSM 回 IDLE）。</summary>
    [Signal]
    public delegate void EventEndedEventHandler(StringName eventId);

    /// <summary>分组常量（组内单事件并发，组间并行）——GDScript 经实例 GROUP_FOG/GROUP_ENCOUNTER 访问。</summary>
    public static readonly StringName GroupFog = new StringName("fog");

    public static readonly StringName GroupEncounter = new StringName("encounter");

    /// <summary>原 GDScript const 兼容（实例访问；静态成员经脚本资源不可达，故转发实例属性）。</summary>
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

    /// <summary>V 系列：空 StringName 复用（原多处 `new StringName()` 每帧构造，native 引用计数分配）。</summary>
    private static readonly StringName EmptyId = new();

    /// <summary>V 系列：遭遇配置缓存（注册时固化内层字典引用——消除 Tick 每帧空字典分配 +
    /// AsGodotDictionary cast；引用而非值：EventManagerTest 直写 ENCOUNTER_CONFIG 内层
    /// chance/min_score 固定掷签（132/268 行），值缓存会导致测试修改不可见）。</summary>
    private readonly Dictionary<StringName, Godot.Collections.Dictionary> _encounterCfg = new();

    /// <summary>V 系列：遭遇实例缓存（注册工厂即返回该实例的闭包——直接缓存实例，
    /// 消除 Poll/Tick 每帧 Callable.Call 动态派发；IsInstanceValid 校验防场景重载后死节点）。</summary>
    private readonly Dictionary<StringName, Node> _encounterInstance = new();

    private bool _runActive;
    /// <summary>迷雾组是否已接线（GameState 在迷雾门面 wire() 时开启；未接线则本组完全惰性，
    /// 保证分阶段迁移期间与旧 FogEventManager 驱动不重叠）。</summary>
    private bool _fogWired;
    private StringName _fogActiveId = EmptyId;
    private GameEvent? _fogActiveEvent;
    private Godot.Timer? _fogTimer;
    private float _fogCooldownLeft;
    private float _fogFirstDelayLeft;
    private float _fogCheckTimer;
    /// <summary>遭遇事件注册顺序（触发检查按注册序；main 先注册 elite 再 formation，保持原优先级）。</summary>
    private readonly Godot.Collections.Array<StringName> _encounterOrder = new();
    /// <summary>遭遇触发策略计时器（id -> 剩余秒）。</summary>
    private readonly Godot.Collections.Dictionary _encounterTimers = new();
    /// <summary>遭遇事件活跃快照（id -> bool；轮询检测结束发 event_ended）。</summary>
    /// <summary>Q13（2026-08-05）：遭遇结束信号待发集合——end_active 打断后 FSM 未立即回 IDLE 时
    /// 记 pending，由轮询在检测到回 IDLE 后统一补发（防双发/发在事件仍活跃时）。</summary>
    private readonly Godot.Collections.Dictionary _encounterEndPending = new();
    /// <summary>当前激活的遭遇事件 id（无则空）。</summary>
    private StringName _encounterActiveId = EmptyId;
    /// <summary>spawner 依赖注入（main._ready 调用；遭遇触发门控 + 特殊槽通知，A5 依赖注入延续）。</summary>
    /// <summary>遭遇触发门控注入（U14：typed 化，消除每帧 HasMethod/Call）。</summary>
    private Spawner? _spawner;

    public override void _Ready()
    {
        LoadBalance();
        _fogCheckTimer = FOG_CHECK_INTERVAL;
        _fogFirstDelayLeft = FOG_FIRST_DELAY;
    }

    private void LoadBalance()
    {
        FOG_ENABLED = GameState.Instance.Cfg("fog_events.enabled", FOG_ENABLED).AsBool();
        FOG_TRIGGER_CHANCE = (float)GameState.Instance.Cfg("fog_events.trigger_chance", FOG_TRIGGER_CHANCE).AsDouble();
        // H15 族：≤0 每帧掷签
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

        // 遭遇触发策略（与 spawner 原读键一致，balance.json 零变化）
        ENCOUNTER_CONFIG = new Godot.Collections.Dictionary
        {
            [new StringName("elite_turret")] = new Godot.Collections.Dictionary
            {
                ["interval"] = Mathf.Max((float)GameState.Instance.Cfg("elite_turret_event.trigger_interval", 45.0).AsDouble(), 0.1f),
                ["chance"] = Mathf.Clamp(
                    (float)GameState.Instance.Cfg("elite_turret_event.trigger_chance", 0.35).AsDouble(), 0.0f, 1.0f),
                ["min_score"] = (int)GameState.Instance.Cfg("elite_turret_event.min_score", 800).AsInt64(),
            },
            [new StringName("formation_strike")] = new Godot.Collections.Dictionary
            {
                ["interval"] = Mathf.Max(
                    (float)GameState.Instance.Cfg("formation_strike_event.trigger_interval", 40.0).AsDouble(), 0.1f),
                ["chance"] = Mathf.Clamp(
                    (float)GameState.Instance.Cfg("formation_strike_event.trigger_chance", 0.30).AsDouble(), 0.0f, 1.0f),
                ["min_score"] = (int)GameState.Instance.Cfg("formation_strike_event.min_score", 500).AsInt64(),
            },
        };
    }

    /// <summary>P4（2026-08-05）：配置重载公开入口（GameState.reload_balance 联动——原诊断/测试注入
    /// 路径只刷平衡缓存，fog 配置停留旧值，与运行时不一致）。注意：遭遇组（ENCOUNTER_CONFIG）注册时
    /// 固化内层字典引用（fe1a186 为测试直写可见性），重载后遭遇策略仍为注册时值——仅诊断/测试路径，
    /// 运行时 _ready 只走一次（W 系列 2026-08-09 口径澄清）。</summary>
    public void ReloadConfig() => LoadBalance();

    // ---------------- 对外公开接口（A1 约定：测试/诊断经公开接口） ----------------

    public bool IsRunActive() => _runActive;

    /// <summary>对局活跃开关（main._ready/_exit_tree 设置；非活跃时强制结束进行中的迷雾事件）
    /// Q10/Q12（2026-08-05）：激活时重置遭遇触发计时与 fog 开局保护/检查计时——
    /// 原实现两者仅注册/接线时初始化且挂 autoload，死亡重开/重进 main 继承上局剩余值
    /// （遭遇计时可 ≤0 → 新局开局即触发精英/编队；fog 每进程一次保护、第二局开局即触发）。</summary>
    public void SetRunActive(bool active)
    {
        if (active == _runActive)
        {
            return;
        }

        _runActive = active;
        if (!active)
        {
            EndFog();
            return;
        }

        foreach (var k in _encounterTimers.Keys)
        {
            var id = k.AsStringName();
            var cfg = ENCOUNTER_CONFIG.GetValueOrDefault(id, new Godot.Collections.Dictionary());
            _encounterTimers[id] = Mathf.Max((float)cfg.AsGodotDictionary().GetValueOrDefault("interval", 45.0).AsDouble(), 0.1f);
        }

        _fogFirstDelayLeft = FOG_FIRST_DELAY;
        _fogCheckTimer = FOG_CHECK_INTERVAL;
        // 2026-08-06 审计：fog 冷却重置（Q12 同族遗漏）——上局事件结束残留的
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

    /// <summary>遭遇事件注册（main._ready 调用；实例由 main 创建并挂 Main 下，测试经 main.event()/
    /// main.formation() 访问；注册进统一注册表并初始化触发计时）。</summary>
    public void RegisterEncounter(StringName pId, Node pEvent)
    {
        EVENT_FACTORIES[pId] = Callable.From(() => pEvent);
        if (!_encounterOrder.Contains(pId))
        {
            _encounterOrder.Add(pId);
        }

        // V 系列：配置与实例一次性缓存（Tick 每帧读缓存，不再每帧解析/分配；
        // 缓存内层字典引用——测试直写 chance/min_score 即时可见）
        var cfg = ENCOUNTER_CONFIG.GetValueOrDefault(pId, new Godot.Collections.Dictionary());
        var dict = cfg.AsGodotDictionary();
        _encounterCfg[pId] = dict;
        _encounterInstance[pId] = pEvent;
        if (!_encounterTimers.ContainsKey(pId))
        {
            _encounterTimers[pId] = Mathf.Max((float)dict.GetValueOrDefault("interval", 45.0).AsDouble(), 0.1f);
        }
    }

    /// <summary>spawner 依赖注入（main._ready 调用；遭遇触发门控 + 触发时占用特殊槽）。</summary>
    public void SetSpawner(Node spawner) => _spawner = spawner as Spawner;

    /// <summary>已注册事件 id 列表（EVENT_FACTORIES 为唯一事实源）。</summary>
    public Godot.Collections.Array<StringName> EventIds()
    {
        var ids = new Godot.Collections.Array<StringName>();
        foreach (var k in EVENT_FACTORIES.Keys)
        {
            ids.Add(k.AsStringName());
        }

        return ids;
    }

    /// <summary>指定事件实例（遭遇事件返回缓存单例；迷雾事件返回新实例——仅诊断用）。</summary>
    public Variant Event(StringName pId)
    {
        var factory = EVENT_FACTORIES.GetValueOrDefault(pId, new Variant());
        if (factory.VariantType == Variant.Type.Callable)
        {
            return factory.AsCallable().Call();
        }

        return new Variant();
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

    /// <summary>强制启动指定事件（进行中/未注册返回 false；不受概率与冷却门控，测试/诊断直调）。</summary>
    public bool ForceTrigger(StringName pId)
    {
        var factory = EVENT_FACTORIES.GetValueOrDefault(pId, new Variant());
        if (factory.VariantType != Variant.Type.Callable)
        {
            return false;
        }

        if (GroupOf(pId) == GroupFog)
        {
            // 迷雾组未接线（分阶段迁移期间）或已有进行中事件 → 拒触发
            if (!_fogWired || _fogActiveId != EmptyId)
            {
                return false;
            }

            return StartFog(pId);
        }

        if (GroupOf(pId) == GroupEncounter)
        {
            // 遭遇组单事件并发（含手动 start 兜底登记，_encounter_active_id 为准）
            if (_encounterActiveId != EmptyId)
            {
                return false;
            }

            var evRaw = factory.AsCallable().Call();
            var ev = evRaw.VariantType == Variant.Type.Object ? evRaw.AsGodotObject() as Node : null;
            // U14：typed 分派（is_active 经 IEncounterEvent 契约）
            if (ev is not IEncounterEvent enc || enc.IsActive())
            {
                return false;
            }

            StartEncounter(pId, enc);
            return true;
        }

        return false;
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
            if (ev is IEncounterEvent enc) // U13：typed（Abort 进遭遇契约接口）
            {
                enc.Abort();
            }

            _encounterActiveId = EmptyId;
            // Q13（2026-08-05）：event_ended 统一由轮询在 FSM 回 IDLE 后发——
            // 原实现此处即发 + 轮询再发 = 双发且第二次发在事件仍活跃时；
            // 同步回 IDLE 则本处补发，异步则记 pending 由轮询补发
            var stillActive = ev is IEncounterEvent enc2 && enc2.IsActive(); // U13：typed
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

    /// <summary>测试/诊断：直接设定 fog 组冷却剩余（压缩时长确定性测试，不动 balance.json）。</summary>
    public void SetCooldownLeft(float seconds) => _fogCooldownLeft = seconds;

    public float CooldownLeft() => _fogCooldownLeft;

    /// <summary>测试/诊断：直接设定 fog 组开局保护剩余。</summary>
    public void SetFirstDelayLeft(float seconds) => _fogFirstDelayLeft = seconds;

    /// <summary>测试/诊断：fog 开局保护剩余（Q12 断言用）。</summary>
    public float FirstDelayLeft() => _fogFirstDelayLeft;

    /// <summary>测试/诊断：直接设定 fog 检查计时剩余（压缩检查周期，确定性测试）。</summary>
    public void SetCheckTimerLeft(float seconds) => _fogCheckTimer = Mathf.Max(seconds, 0.0f);

    /// <summary>测试/诊断：遭遇事件触发计时剩余（Q10 断言用）。</summary>
    public float EncounterTimerRemaining(StringName pId) => (float)_encounterTimers.GetValueOrDefault(pId, 0.0).AsDouble();

    /// <summary>测试/诊断：直接设定遭遇事件触发计时剩余（压缩时长确定性测试）。</summary>
    public void SetEncounterTimerRemaining(StringName pId, float seconds) => _encounterTimers[pId] = Mathf.Max(seconds, 0.0f);

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
        var d = (float)delta;
        PollEncounters();
        // fog 组（未接线前惰性，避免与旧 FogEventManager 双驱动）
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
                // Q07：总开关关闭时自动触发路径完全惰性
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

        // encounter 组（门控：注入 spawner 处理中——set_process(false)/暂停语义与现状一致；
        // is_processing() 反映 set_process 维度，can_process() 反映树/暂停维度）
        if (_spawner != null && GodotObject.IsInstanceValid(_spawner) && _spawner.IsProcessing() && _spawner.CanProcess())
        {
            TickEncounterTriggers(d);
        }
    }

    /// <summary>遭遇事件触发检查（镜像 spawner._process 原逻辑 + ScheduledEventTrigger 语义）：
    /// 按注册序逐个——事件可触发（can_trigger + Boss 互斥 + 精英事件额外要求编队不在场）
    /// 且分数门槛通过才推进计时，计时归零按概率掷签启动。</summary>
    private void TickEncounterTriggers(float delta)
    {
        var score = GameState.Instance.Score;
        foreach (var id in _encounterOrder)
        {
            // U14：遭遇事件 typed 分派（IEncounterEvent 契约，替代每帧 HasMethod/Call 动态派发）
            var ev = EventFor(id);
            // 2026-08-09 审计：EventFor fallback 工厂闭包捕获的正是已失效实例，可返回死引用
            // （正常路径被 RegisterEncounter 覆盖缓存防护）——触发侧二次判活，死引用不可触发
            if (!GodotObject.IsInstanceValid(ev) || ev is not IEncounterEvent enc || enc.IsActive())
            {
                continue;
            }

            if (!EncounterCanTrigger(id, enc))
            {
                continue;
            }

            // V 系列：注册时缓存的内层字典引用（零分配读取；测试直写即时可见）
            var dict = _encounterCfg.GetValueOrDefault(id);
            if (dict == null)
            {
                continue;
            }

            var minScore = (int)dict.GetValueOrDefault("min_score", 0).AsInt64();
            if (score < minScore)
            {
                continue; // 分数门槛未过：计时不推进（镜像 ScheduledEventTrigger）
            }

            var interval = Mathf.Max((float)dict.GetValueOrDefault("interval", 45.0).AsDouble(), 0.1f);
            var timer = (float)_encounterTimers.GetValueOrDefault(id, interval).AsDouble();
            timer -= delta;
            if (timer <= 0.0f)
            {
                _encounterTimers[id] = interval;
                if (GD.Randf() < (float)dict.GetValueOrDefault("chance", 0.3).AsDouble())
                {
                    StartEncounter(id, enc);
                }
            }
            else
            {
                _encounterTimers[id] = timer;
            }
        }
    }

    /// <summary>遭遇事件触发资格：事件自身 can_trigger（冷却/分数/母舰）+ Boss 未激活 +
    /// 精英事件额外要求编队不在场（镜像 spawner 原互斥链）。</summary>
    private bool EncounterCanTrigger(StringName pId, IEncounterEvent ev)
    {
        if (!ev.CanTrigger())
        {
            return false;
        }

        if (_spawner != null && GodotObject.IsInstanceValid(_spawner) && _spawner.IsBossActive())
        {
            return false;
        }

        if (pId == "elite_turret")
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
                    return false;
                }
            }
        }

        return true;
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
    /// Q13：pending 打断在检测到回 IDLE 后补发，信号恒在事件不活跃时发、恒只发一次）。</summary>
    private void PollEncounters()
    {
        foreach (var id in _encounterOrder)
        {
            var ev = EventFor(id);
            // 2026-08-09 审计：死引用按「不活跃」处理——直接跳过会让 _encounterActiveId 残留、编排卡死；
            // 按不活跃走下方分支把事件状态复位并广播结束，属自愈路径
            var active = GodotObject.IsInstanceValid(ev) && ev is IEncounterEvent enc && enc.IsActive(); // U14：typed 分派
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
    /// P4：全零权重退化为均匀随机——原实现恒选首个（roll=0 立即命中第一项））。</summary>
    private StringName PickFogId()
    {
        var ids = new Godot.Collections.Array<StringName>();
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
            total += Mathf.Max((float)FOG_WEIGHTS.GetValueOrDefault(id, 1.0).AsDouble(), 0.0f);
        }

        if (total <= 0.0f)
        {
            return ids[(int)(GD.Randi() % (uint)ids.Count)]; // 全零权重：均匀回退
        }

        var roll = GD.Randf() * total;
        foreach (var id in ids)
        {
            roll -= Mathf.Max((float)FOG_WEIGHTS.GetValueOrDefault(id, 1.0).AsDouble(), 0.0f);
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
        var duration = Mathf.Max((float)FOG_EVENT_DURATIONS.GetValueOrDefault(pId, 6.0).AsDouble(), 0.05f);
        if (_fogTimer != null && GodotObject.IsInstanceValid(_fogTimer))
        {
            _fogTimer.Stop(); // 防御：异常时序下的旧 timer 残留
            _fogTimer.QueueFree();
        }

        _fogTimer = new Godot.Timer { OneShot = true };
        _fogTimer.Timeout += EndFog;
        AddChild(_fogTimer);
        _fogTimer.Start(duration);
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
        // V 系列：注册实例缓存优先（零动态派发；场景重载后旧实例失效 → fallback 工厂）
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

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public Variant @event(StringName pId) => Event(pId);
}
