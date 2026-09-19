using System;
using Godot;
using InfiAir.Core.Combat;

namespace InfiAir;

/// <summary>
/// 精英炮塔事件编排：
/// IDLE → CARRIER_ENTER（航母降入 2s）→ 炮塔升起充能 1.5s → TURRET_ACTIVE（30s 倒计时）
/// → 成功（全歼，事件档位奖励 RewardScore）/失败（超时撤退）→ CARRIER_EXIT → BOSS_DELAY（4s）→ IDLE。
/// 与 Boss 互斥：进入 CARRIER_ENTER 冻结 Boss 调度（到期记 _boss_pending 一次，不累积），
/// BOSS_DELAY 结束时解冻并补触发一次。事件期间普通波次暂停（CARRIER_EXIT 起恢复）。
/// Spawner/HUD 为 C# typed 调用，turret.tscn 场景绑定 Instantiate&lt;TurretBattery&gt;。
/// 公开 API 为 PascalCase。公共骨架（spawner 注入/母舰缓存/冷却/ResumeWaves 等）在
/// EncounterEventBase（与 FormationStrikeEvent 共享）。
/// </summary>
public partial class EliteTurretEvent : EncounterEventBase
{
    public enum State { IDLE, CARRIER_ENTER, TURRET_ACTIVE, CARRIER_EXIT, BOSS_DELAY }

    /// <summary>炮台场景按需加载，不做静态持有——静态字段持 Godot 资源会在引擎退出后
    /// 被 .NET finalize 触碰 native 而 segfault（同 UITheme.Font 口径）；
    /// GD.Load 命中引擎资源缓存，每次取用代价可忽略。</summary>
    private static PackedScene TurretScene => GD.Load<PackedScene>("res://scenes/turret.tscn");

    // ---- 配置（读 balance.json elite_turret_event 段，脚本值为缺键回退） ----
    public float Duration { get; private set; } = 30.0f;
    public float EnterTime { get; private set; } = 2.0f;
    public float RiseTime { get; private set; } = 1.5f;
    public float BossResumeDelay { get; private set; } = 4.0f;
    public int TurretHpBase { get; private set; } = 80;
    public Godot.Collections.Dictionary TurretCounts { get; private set; } = new()
    {
        ["easy"] = 3,
        ["medium"] = 4,
        ["hard"] = 5,
    };
    public Vector2 FireInterval { get; private set; } = new(2.0f, 2.4f);
    public Godot.Collections.Dictionary WeakLock { get; private set; } = new()
    {
        ["turn_rate"] = 2.0,
        ["homing_turn_rate"] = 1.5,
        ["homing_time"] = 0.6,
        ["spread_deg"] = 7.0,
    };
    public Godot.Collections.Dictionary AmmoSequences { get; private set; } = new()
    {
        ["easy"] = new Godot.Collections.Array { new StringName("single"), new StringName("spread3"), new StringName("single") },
        ["medium"] = new Godot.Collections.Array
        {
            new StringName("single"), new StringName("spread3"), new StringName("laser"), new StringName("weak_homing"),
        },
        ["hard"] = new Godot.Collections.Array
        {
            new StringName("spread5"), new StringName("laser"), new StringName("weak_homing"),
            new StringName("sniper"), new StringName("single"),
        },
    };
    public int RewardScore { get; private set; } = 900;
    /// <summary>单座炮台击毁的击杀分（balance.json elite_turret_event.turret_score，与 turret_hp_base
    /// 同段）——走 AddKillScore，与原「炮塔击毁不给分」的差异见 Die 处注释。该判定经人类授权落地，反转需显式改值。</summary>
    public int TurretScore { get; private set; } = 150;
    public float HoverY { get; private set; } = 300.0f;
    public float Cooldown { get; private set; } = 60.0f;

    private State _state = State.IDLE;
    protected override bool IsIdle => _state == State.IDLE;

    /// <summary>炮台贴合自检容差（px）——悬停期航母浮动 &lt;0.1px/帧，容差只兜同帧顺序余量。</summary>
    private const float MountTolerance = 1.5f;

    private StrikeCarrier? _carrier;
    private readonly Godot.Collections.Array<TurretBattery> _turrets = new();
    private float _timer;
    private float _hudPoll;
    private int _total;
    private int _destroyed;
    /// <summary>台词节点：0 未播 / 1 已播第1句 / 2 已播第2句。</summary>
    private int _lineStage;
    /// <summary>打断标志（返航/死亡 Abort 置位）：收场不补触发被冻结的 Boss。</summary>
    private bool _aborted;

    /// <summary>炮台贴合自检已报错（只报一次，避免逐帧刷屏）。</summary>
    private bool _mountErrorLogged;
    private readonly Godot.Collections.Array<String> _lines = new();
    private Hud? _hud; // typed

    public override void _Ready()
    {
        // duration 钳下限——≤0 时事件开启即超时结算，波次暂停/恢复空转
        Duration = Mathf.Max((float)GameState.Instance.Cfg("elite_turret_event.duration", Duration).AsDouble(), 1.0f);
        // enter_time 钳下限（StrikeCarrier.ENTER 的 _enterT/该值除零，
        // Clamp 兜底无 NaN，但降入瞬完成、视觉跳变）
        EnterTime = Mathf.Max((float)GameState.Instance.Cfg("elite_turret_event.enter_time", EnterTime).AsDouble(), CfgFx.IntervalFloor);
        // rise_time 钳下限（同 enter_time 视觉跳变口径）
        RiseTime = Mathf.Max((float)GameState.Instance.Cfg("elite_turret_event.rise_time", RiseTime).AsDouble(), CfgFx.IntervalFloor);
        // boss_resume_delay 钳下限（Schedule 负值行为未定义）
        BossResumeDelay = Mathf.Max((float)GameState.Instance.Cfg("elite_turret_event.boss_resume_delay", BossResumeDelay).AsDouble(), CfgFx.IntervalFloor);
        TurretHpBase = (int)GameState.Instance.Cfg("elite_turret_event.turret_hp_base", TurretHpBase).AsInt64();
        // turret_score 钳 ≥0（负分被连击乘区倒扣）
        TurretScore = CfgFx.Int("elite_turret_event.turret_score", TurretScore, 0);
        // turret_counts/ammo_sequences 判型回退——非 Dictionary 时
        // 后续 .get() 在 Variant 上调用会运行时崩溃（标量口径判型只覆盖 fire_interval 等）
        var tc = GameState.Instance.Cfg("elite_turret_event.turret_counts", TurretCounts);
        if (tc.VariantType == Variant.Type.Dictionary)
        {
            TurretCounts = tc.AsGodotDictionary();
        }

        var am = GameState.Instance.Cfg("elite_turret_event.ammo_sequences", AmmoSequences);
        if (am.VariantType == Variant.Type.Dictionary)
        {
            AmmoSequences = am.AsGodotDictionary();
        }

        // fire_interval 判型回退（防非数组/短数组 _ready 崩溃）+ **元素级判型与域钳**：
        // 容器判型只保证它是个数组，元素写成字符串/数组时 Godot 的 AsDouble 宽松转换得 0（不抛），
        // 于是 _fireTimer 恒 0 → 炮台每物理帧开火且零报错。判型与钳制口径单源在 core EncounterConfig。
        var fi = GameState.Instance.Cfg(
            "elite_turret_event.fire_interval", new Godot.Collections.Array { FireInterval.X, FireInterval.Y });
        if (fi.VariantType == Variant.Type.Array && fi.AsGodotArray().Count >= 2)
        {
            var fiArr = fi.AsGodotArray();
            var lo = fiArr[0];
            var hi = fiArr[1];
            var loValid = lo.VariantType is Variant.Type.Int or Variant.Type.Float;
            var hiValid = hi.VariantType is Variant.Type.Int or Variant.Type.Float;
            var (min, max) = EncounterConfig.Range(
                loValid,
                loValid ? (float)lo.AsDouble() : 0.0f,
                hiValid,
                hiValid ? (float)hi.AsDouble() : 0.0f,
                FireInterval.X,
                FireInterval.Y,
                CfgFx.IntervalFloor);
            FireInterval = new Vector2(min, max);
        }

        // WEAK_LOCK 判型——非 Dictionary 时透传给
        // turret.Setup 的弱锁参数会在消费方崩溃，与 TURRET_COUNTS 同口径回退。
        // 容器判型之外**逐键判型与域钳**（同 fire_interval 的元素级口径）：turn_rate 写成坏值时
        // AsDouble 得 0 → 炮台不再转向、弹道永远朝下。净化后的字典是传给炮台的唯一来源。
        var wl = GameState.Instance.Cfg("elite_turret_event.weak_lock", WeakLock);
        if (wl.VariantType == Variant.Type.Dictionary)
        {
            // 回退默认取属性现值（脚本默认表），不把四个默认数字再抄一份
            var raw = wl.AsGodotDictionary();
            var defaults = WeakLock;
            WeakLock = new Godot.Collections.Dictionary
            {
                ["turn_rate"] = WeakLockFloat(raw, "turn_rate", defaults, 0.0f),
                ["homing_turn_rate"] = WeakLockFloat(raw, "homing_turn_rate", defaults, 0.0f),
                ["homing_time"] = WeakLockFloat(raw, "homing_time", defaults, CfgFx.IntervalFloor),
                ["spread_deg"] = WeakLockFloat(raw, "spread_deg", defaults, 0.0f),
            };
        }

        RewardScore = (int)GameState.Instance.Cfg("elite_turret_event.reward_score", RewardScore).AsInt64();
        HoverY = (float)GameState.Instance.Cfg("elite_turret_event.carrier.hover_y", HoverY).AsDouble();
        // cooldown 钳下限——0 冷却 + 高触发率下事件背靠背连发，波次被长期挤占近饿死
        Cooldown = Mathf.Max((float)GameState.Instance.Cfg("elite_turret_event.cooldown", Cooldown).AsDouble(), CfgFx.IntervalFloor);
        base._Ready(); // 台词层创建 + spawner 兜底（公共骨架，见 EncounterEventBase）
    }

    /// <summary>事件启动（互斥检查通过后由事件管理器调用）。</summary>
    public override void Start()
    {
        if (_state != State.IDLE)
        {
            return;
        }

        _state = State.CARRIER_ENTER;
        _destroyed = 0;
        _lineStage = 0;
        _aborted = false;
        // 10 句台词无放回随机抽取 3 句，绑定三个进度节点
        var pool = new Godot.Collections.Array<String>();
        for (var i = 0; i < 10; i++)
        {
            pool.Add("ETQ_" + (i + 1));
        }

        pool.Shuffle();
        _lines.Clear();
        for (var i = 0; i < 3; i++)
        {
            _lines.Add(pool[i]);
        }

        // 冻结 Boss 调度 + 暂停普通波次（spawner 钩子；注入 _spawner，typed 直调）
        HoldBoss();
        HoldWaves();

        _carrier = new StrikeCarrier();
        var carrier = _carrier;
        var evView = GameState.Instance.ViewWorldRect(); // 载体入场锚点统一 view 基线
        carrier.Position = new Vector2(evView.GetCenter().X, evView.Position.Y - 450.0f);
        carrier.Entered += OnCarrierEntered;
        carrier.Exited += OnCarrierExited;
        GetParent().AddChild(carrier);
        // HOVER_Y 为距可见区顶缘偏移——绝对 y 在
        // 非默认视角档（zoom>1 可见区下移）会偏高 140~222px，炮塔行锚点随之偏高
        carrier.Enter(evView.Position.Y + HoverY, EnterTime);
        GameState.Instance.Shake(GameState.Instance.Cfg("elite_turret_event.carrier.shake", 4.0).AsDouble());
        _hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
    }

    /// <summary>返航中止（main._start_homecoming 调用）：IDLE 直接返回；清掉在场炮塔（queue_free
    /// 不触发 died 计分，自行清理注册清单）、隐藏 HUD 事件条、恢复普通波次，航母按完整
    /// 撤离处理；Boss 解冻沿用 BOSS_DELAY → OnBossDelayEnd，打断路径不补触发 pending Boss。</summary>
    public override void Abort()
    {
        if (_state == State.IDLE)
        {
            return;
        }

        foreach (var turret in _turrets)
        {
            if (GodotObject.IsInstanceValid(turret))
            {
                turret.QueueFree();
            }
        }

        _turrets.Clear();
        if (_hud != null && GodotObject.IsInstanceValid(_hud))
        {
            _hud.HideEventBar(); // HUD 可能先于本节点释放（场景切换），`?.` 拦不住已释放对象
        }

        if (_comm != null)
        {
            _comm.Clear(); // 清掉已显台词，避免返航恢复后残留
        }

        ResumeWaves();
        _aborted = true;
        if (_state == State.CARRIER_ENTER || _state == State.TURRET_ACTIVE)
        {
            _state = State.CARRIER_EXIT;
            CarrierRetreat(true); // 完整撤离（加速上升淡出）
        }
        // CARRIER_EXIT/BOSS_DELAY：撤离/解冻流程已在推进，无需干预
    }

    /// <summary>弱锁单键净化：判型失败/非有限/负值回退默认表取值，合法值钳到下限之上
    /// （判定单源在 core <see cref="EncounterConfig.RangeEndpoint"/>）。</summary>
    private static float WeakLockFloat(
        Godot.Collections.Dictionary cfg, string key, Godot.Collections.Dictionary defaults, float floor)
    {
        var v = cfg.GetValueOrDefault(key, new Variant());
        var valid = v.VariantType is Variant.Type.Int or Variant.Type.Float;
        var fallback = (float)defaults.GetValueOrDefault(key, 0.0).AsDouble();
        return EncounterConfig.RangeEndpoint(valid, valid ? (float)v.AsDouble() : 0.0f, fallback, floor);
    }

    /// <summary>航母悬停到位：基座盖板旋开、炮塔升起充能（不可被攻击）。</summary>
    private void OnCarrierEntered()
    {
        // turret_counts 上限钳制——配置 >5 时 SOCKETS[i] 越界崩溃
        //（StrikeCarrier.Sockets 固定 5 槽）。
        // 难度键条目值判型——字符串/数组等坏值 AsInt64 抛
        // InvalidCastException 崩溃（事件触发即崩），坏值回退默认 4
        // 下限 0/负 → 无炮塔事件空跑（30s 倒计时 + BOSS_DELAY 4s，
        // Boss 冻结/波次暂停共 34s 玩家干等）——钳 [1, Sockets.Length]
        var diffStr = GameState.Instance.Difficulty.ToString();
        var tcV = TurretCounts.GetValueOrDefault(diffStr, new Variant());
        var rawTotal = tcV.VariantType is Variant.Type.Int or Variant.Type.Float ? (int)tcV.AsInt64() : 4;
        _total = Mathf.Clamp(rawTotal, 1, StrikeCarrier.Sockets.Length);
        // HP 三级乘算：基准 × 难度档 × 本局进程 ramp（与普通敌机同口径，避免后期退化为送分道具）
        var hp = Mathf.Max(
            1,
            (int)Mathf.Round(
                TurretHpBase
                * (float)GameState.Instance.EnemyHpMultiplier()
                * (float)GameState.Instance.EnemyHpRamp()));
        // ammo 条目级判型（容器 Dictionary 之外还要判难度键条目）——
        // 难度键缺失/非 Array 时 `for a in p_ammo` 崩溃；
        // 缺键回退 medium，仍非 Array 回退内置默认序列
        var ammo = AmmoSequences.GetValueOrDefault(diffStr, new Variant());
        if (ammo.VariantType != Variant.Type.Array)
        {
            ammo = AmmoSequences.GetValueOrDefault("medium", new Variant());
        }

        if (ammo.VariantType != Variant.Type.Array)
        {
            ammo = new Godot.Collections.Array
            {
                new StringName("single"), new StringName("spread3"), new StringName("laser"), new StringName("weak_homing"),
            };
        }

        for (var i = 0; i < _total; i++)
        {
            var turret = TurretScene.Instantiate<TurretBattery>();
            turret.Setup(hp, ammo.AsGodotArray(), FireInterval, WeakLock);
            turret.ScoreValue = TurretScore;
            // 挂到航母节点下：基座偏移即父级本地坐标，悬停浮动与撤离加速随父级自动同步。
            // 原先挂 Main 下只在升起瞬间写一次绝对位置——悬停期错位 ±6px，超时路径更明显
            //（撤回动画 0.8s 内舰已上升约 230px，画面上是「炮台悬空收盖板、舰已飞走」）。
            // 连带代价：炮台的击毁爆炸（TurretBattery.Die 挂自身父级）随之落在航母体上——
            // 击杀只可能发生在炮台激活期，此时舰仅做 ±6px 悬停浮动，观感等价；
            // 撤回/撤离期的炮台已被 CeaseFireAndRetract 关掉 monitorable，不再产生爆炸。
            turret.Position = StrikeCarrier.Sockets[i] * (float)GameState.Instance.WorldScale;
            var socket = i;
            turret.Died += (t) => OnTurretDied(socket, t);
            _carrier!.AddChild(turret);
            _turrets.Add(turret);
            _carrier!.SetSocketCharging(i);
            turret.Rise(RiseTime);
        }

        Schedule(RiseTime, BeginCountdown);
    }

    /// <summary>充能完毕：30s 倒计时开始，炮塔可被攻击并开火。</summary>
    private void BeginCountdown()
    {
        if (_state != State.CARRIER_ENTER)
        {
            return;
        }

        _state = State.TURRET_ACTIVE;
        _timer = Duration;
        foreach (var turret in _turrets)
        {
            if (GodotObject.IsInstanceValid(turret))
            {
                turret.Activate();
            }
        }

        if (_hud != null && GodotObject.IsInstanceValid(_hud))
        {
            _hud.ShowEventBar("ETV_TITLE", "ETV_TURRETS", UITheme.EventMagenta, _total);
        }
    }

    public override void _Process(double delta)
    {
        // 单帧推进上限（口径单源 FrameCache.MaxStepDelta，与编队事件同口径）：
        // 巨帧（开局/卡顿实测 >1s）一次吞掉事件窗口，极端下单帧直接判负
        var d = Mathf.Min((float)delta, FrameCache.MaxStepDelta);
        TickCooldown(d);

        if (_state != State.TURRET_ACTIVE)
        {
            return;
        }

        CheckTurretMounting();
        _timer -= d;
        _hudPoll -= d;
        if (_hudPoll <= 0.0f)
        {
            _hudPoll = 0.1f;
            if (_hud != null && GodotObject.IsInstanceValid(_hud))
            {
                _hud.UpdateEventBar(_timer / Duration, _total - _destroyed);
            }
        }

        if (_timer <= 0.0f)
        {
            OnEventTimeout();
        }
    }

    /// <summary>炮台贴合自检（激活期每帧一次）：炮台是航母子节点，本地坐标应恒等于对应基座偏移。
    /// 改回「挂 Main + 绝对位置」或另有节点抢写位置时，这一步会报错——无头冒烟不看画面，
    /// 「炮台与基座环错位」在别处是零症状（只错位、不崩、不打日志）。
    /// 局部坐标只看轴对齐，不含旋转：航母无旋转且 Sockets 为轴向偏移，成立。</summary>
    private void CheckTurretMounting()
    {
        if (_mountErrorLogged || _carrier == null || !GodotObject.IsInstanceValid(_carrier))
        {
            return;
        }

        var ws = (float)GameState.Instance.WorldScale;
        for (var i = 0; i < _turrets.Count; i++)
        {
            var turret = _turrets[i];
            if (!GodotObject.IsInstanceValid(turret) || turret.GetParent() != _carrier)
            {
                _mountErrorLogged = true;
                GD.PushError($"{GetType().Name}: 炮台未挂在航母节点下，悬停浮动与撤离不会随动");
                return;
            }

            // 索引 i 与基座的对应在炮台被毁后失效（_turrets 移除后收缩），只在未击毁任一炮台时逐槽比对
            if (_destroyed == 0 && i < StrikeCarrier.Sockets.Length
                && turret.Position.DistanceTo(StrikeCarrier.Sockets[i] * ws) > MountTolerance)
            {
                _mountErrorLogged = true;
                GD.PushError($"{GetType().Name}: 炮台 {i} 偏离基座 {turret.Position.DistanceTo(StrikeCarrier.Sockets[i] * ws):F1}px");
                return;
            }
        }
    }

    private void OnTurretDied(int socket, TurretBattery turret)
    {
        _destroyed += 1;
        _turrets.Remove(turret);
        if (_carrier != null && GodotObject.IsInstanceValid(_carrier))
        {
            _carrier.SetSocketDestroyed(socket);
        }

        if (_hud != null && GodotObject.IsInstanceValid(_hud) && _state == State.TURRET_ACTIVE)
        {
            _hud.UpdateEventBar(_timer / Duration, _total - _destroyed);
        }

        // 进度台词节点：摧毁 ≥ ⌈总数/3⌉ → 第 1 句；≥ ⌈总数×2/3⌉ → 第 2 句；全歼 → 第 3 句
        if (_lineStage == 0 && _destroyed >= Mathf.Max(1, Mathf.CeilToInt(_total / 3.0f)))
        {
            _lineStage = 1;
            _comm!.ShowLine(_lines[0]);
        }

        if (_lineStage == 1 && _destroyed >= Mathf.Max(1, Mathf.CeilToInt(_total * 2.0f / 3.0f)) && _destroyed < _total)
        {
            _lineStage = 2;
            _comm!.ShowLine(_lines[1]);
        }

        if (_destroyed >= _total)
        {
            OnAllTurretsDestroyed();
        }
    }

    /// <summary>成功结算：第 3 句台词 + 事件档位奖励（基础 RewardScore，经 AddEventScore 乘难度奖励因子）。</summary>
    private void OnAllTurretsDestroyed()
    {
        if (_state != State.TURRET_ACTIVE)
        {
            return;
        }

        _state = State.CARRIER_EXIT;
        _comm!.ShowLine(_lines[2]);
        GameState.Instance.AddEventScore(RewardScore);
        if (_hud != null && GodotObject.IsInstanceValid(_hud))
        {
            _hud.HideEventBar(); // HUD 可能先于本节点释放（场景切换），`?.` 拦不住已释放对象
        }

        ResumeWaves();
        CarrierRetreat(false); // 受创撤离（冒烟+慢速）
    }

    /// <summary>失败结算：炮塔收回盖板，固定撤退台词，无奖励。</summary>
    private void OnEventTimeout()
    {
        _state = State.CARRIER_EXIT;
        foreach (var turret in _turrets)
        {
            if (GodotObject.IsInstanceValid(turret))
            {
                turret.CeaseFireAndRetract();
            }
        }

        // 收回中的炮塔已无 died 依赖（_ceased 守卫），立即清引用数组，
        // 消除最长 ~6s（BOSS_RESUME_DELAY 窗口）的失效引用驻留（OnBossDelayEnd 的 clear 幂等）
        _turrets.Clear();
        _comm!.ShowLine("ETQ_RETREAT");
        if (_hud != null && GodotObject.IsInstanceValid(_hud))
        {
            _hud.HideEventBar(); // HUD 可能先于本节点释放（场景切换），`?.` 拦不住已释放对象
        }

        ResumeWaves();
        CarrierRetreat(true); // 完整撤离（加速上升淡出）
    }

    /// <summary>航母撤离（复用 Boss escape 参数族量级；存活敌弹自然出界销毁，不清屏）。</summary>
    private void CarrierRetreat(bool victorious)
    {
        if (_carrier != null && GodotObject.IsInstanceValid(_carrier))
        {
            _carrier.Retreat(victorious);
        }
        else
        {
            OnCarrierExited();
        }
    }

    /// <summary>航母离场后进入 Boss 恢复间隔。</summary>
    private void OnCarrierExited()
    {
        _carrier = null;
        if (_state == State.CARRIER_EXIT)
        {
            _state = State.BOSS_DELAY;
            Schedule(BossResumeDelay, OnBossDelayEnd);
        }
    }

    /// <summary>BOSS_DELAY 结束：回 IDLE；若存在被冻结的 Boss 触发 → 立即触发一次（不累积）。
    /// 打断路径（_aborted）按基类口径不补触发——返航/死亡后在结算画面弹预警横幅。</summary>
    private void OnBossDelayEnd()
    {
        _state = State.IDLE;
        _cooldownLeft = Cooldown;
        _turrets.Clear();
        ReleaseBoss(triggerPending: !_aborted);
    }

    /// <summary>一次性计时回调（同 spawner._schedule：Godot.Timer 节点 + 信号，避免协程泄漏）。</summary>
    private void Schedule(float seconds, Action callback)
    {
        var timer = new Godot.Timer { OneShot = true };
        AddChild(timer);
        timer.Timeout += () =>
        {
            callback();
            timer.QueueFree();
        };
        timer.Start(seconds);
    }
}
