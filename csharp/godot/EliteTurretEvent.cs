using System;
using Godot;

namespace InfiAir;

/// <summary>
/// 精英炮塔事件编排：
/// IDLE → CARRIER_ENTER（航母降入 2s）→ 炮塔升起充能 1.5s → TURRET_ACTIVE（30s 倒计时）
/// → 成功（全歼，+500 基础分）/失败（超时撤退）→ CARRIER_EXIT → BOSS_DELAY（4s）→ IDLE。
/// 与 Boss 互斥：进入 CARRIER_ENTER 冻结 Boss 调度（到期记 _boss_pending 一次，不累积），
/// BOSS_DELAY 结束时解冻并补触发一次。事件期间普通波次暂停（CARRIER_EXIT 起恢复）。
/// Spawner/HUD 为 C# typed 调用，turret.tscn 场景绑定 Instantiate&lt;TurretBattery&gt;。
/// 公开 API 为 PascalCase。公共骨架（spawner 注入/母舰缓存/冷却/ResumeWaves 等）在
/// EncounterEventBase（与 FormationStrikeEvent 共享）。
/// </summary>
public partial class EliteTurretEvent : EncounterEventBase
{
    public enum State { IDLE, CARRIER_ENTER, TURRET_ACTIVE, CARRIER_EXIT, BOSS_DELAY }

    private static readonly PackedScene TurretScene = GD.Load<PackedScene>("res://scenes/turret.tscn");

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
    public int RewardScore { get; private set; } = 500;
    public float HoverY { get; private set; } = 300.0f;
    public float Cooldown { get; private set; } = 60.0f;

    private State _state = State.IDLE;
    protected override bool IsIdle => _state == State.IDLE;

    private StrikeCarrier? _carrier;
    private readonly Godot.Collections.Array<TurretBattery> _turrets = new();
    private float _timer;
    private float _hudPoll;
    private int _total;
    private int _destroyed;
    /// <summary>台词节点：0 未播 / 1 已播第1句 / 2 已播第2句。</summary>
    private int _lineStage;
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

        // fire_interval 判型回退（防非数组/短数组 _ready 崩溃）
        var fi = GameState.Instance.Cfg(
            "elite_turret_event.fire_interval", new Godot.Collections.Array { FireInterval.X, FireInterval.Y });
        if (fi.VariantType == Variant.Type.Array && fi.AsGodotArray().Count >= 2)
        {
            var fiArr = fi.AsGodotArray();
            FireInterval = new Vector2((float)fiArr[0].AsDouble(), (float)fiArr[1].AsDouble());
        }

        // WEAK_LOCK 判型——非 Dictionary 时透传给
        // turret.Setup 的弱锁参数会在消费方崩溃，与 TURRET_COUNTS 同口径回退
        var wl = GameState.Instance.Cfg("elite_turret_event.weak_lock", WeakLock);
        if (wl.VariantType == Variant.Type.Dictionary)
        {
            WeakLock = wl.AsGodotDictionary();
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
        _spawner?.SetBossFrozen(true);
        _spawner?.SetWavesPaused(true);

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
    /// 撤离处理；Boss 解冻/_boss_pending 补触发沿用现有 BOSS_DELAY → OnBossDelayEnd。</summary>
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
        if (_hud != null)
        {
            _hud.HideEventBar();
        }

        if (_comm != null)
        {
            _comm.Clear(); // 清掉已显台词，避免返航恢复后残留
        }

        ResumeWaves();
        if (_state == State.CARRIER_ENTER || _state == State.TURRET_ACTIVE)
        {
            _state = State.CARRIER_EXIT;
            CarrierRetreat(true); // 完整撤离（加速上升淡出）
        }
        // CARRIER_EXIT/BOSS_DELAY：撤离/解冻流程已在推进，无需干预
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
            turret.Position = _carrier!.Position + StrikeCarrier.Sockets[i] * (float)GameState.Instance.WorldScale;
            var socket = i;
            turret.Died += (t) => OnTurretDied(socket, t);
            GetParent().AddChild(turret);
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

        if (_hud != null)
        {
            _hud.ShowEventBar(_total);
        }
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        TickCooldown(d);

        if (_state != State.TURRET_ACTIVE)
        {
            return;
        }

        _timer -= d;
        _hudPoll -= d;
        if (_hudPoll <= 0.0f)
        {
            _hudPoll = 0.1f;
            if (_hud != null)
            {
                _hud.UpdateEventBar(_timer, Duration, _total - _destroyed);
            }
        }

        if (_timer <= 0.0f)
        {
            OnEventTimeout();
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

        if (_hud != null && _state == State.TURRET_ACTIVE)
        {
            _hud.UpdateEventBar(_timer, Duration, _total - _destroyed);
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

    /// <summary>成功结算：第 3 句台词 + 复用 Boss 击杀得分（基础 500，add_score 内乘难度倍率）。</summary>
    private void OnAllTurretsDestroyed()
    {
        if (_state != State.TURRET_ACTIVE)
        {
            return;
        }

        _state = State.CARRIER_EXIT;
        _comm!.ShowLine(_lines[2]);
        GameState.Instance.AddScore(RewardScore);
        if (_hud != null)
        {
            _hud.HideEventBar();
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
        if (_hud != null)
        {
            _hud.HideEventBar();
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

    /// <summary>BOSS_DELAY 结束：回 IDLE；若存在被冻结的 Boss 触发 → 立即触发一次（不累积）。</summary>
    private void OnBossDelayEnd()
    {
        _state = State.IDLE;
        _cooldownLeft = Cooldown;
        _turrets.Clear();
        if (_spawner != null)
        {
            _spawner.SetBossFrozen(false);
            if (_spawner.ConsumeBossPending())
            {
                _spawner.TriggerBoss();
            }
        }
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
