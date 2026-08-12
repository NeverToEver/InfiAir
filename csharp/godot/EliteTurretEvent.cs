using System;
using Godot;

namespace InfiAir;

/// <summary>
/// 精英炮塔事件编排（docs/ELITE_TURRET_EVENT.md；2026-08-08 自 scripts/elite_turret_event.gd 迁移）：
/// IDLE → CARRIER_ENTER（航母降入 2s）→ 炮塔升起充能 1.5s → TURRET_ACTIVE（30s 倒计时）
/// → 成功（全歼，+500 基础分）/失败（超时撤退）→ CARRIER_EXIT → BOSS_DELAY（4s）→ IDLE。
/// 与 Boss 互斥：进入 CARRIER_ENTER 冻结 Boss 调度（到期记 _boss_pending 一次，不累积），
/// BOSS_DELAY 结束时解冻并补触发一次。事件期间普通波次暂停（CARRIER_EXIT 起恢复）。
/// enemy_hp_multiplier/enemy_hp_ramp/world_scale）；M7 后 spawner/HUD 为 C# typed 调用，
/// turret.tscn 场景绑定 Instantiate&lt;TurretBattery&gt;。
/// 白盒断言 API 为 PascalCase（少量 snake_case 兼容桥保留）；嵌套枚举状态值经 GetStateXxx 静态方法访问。
/// </summary>
public partial class EliteTurretEvent : Node, IEncounterEvent // U14：遭遇契约接口（管理器 typed 轮询）
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
    /// <summary>A5：spawner 依赖注入（main._ready 经 SetSpawner 设置；替代 group 现找）。
    /// U14：字段 typed 化（Spawner 已 C#，消除动态派发）。</summary>
    private Spawner? _spawner;
    private StrikeCarrier? _carrier;
    private readonly Godot.Collections.Array<TurretBattery> _turrets = new();
    private float _timer;
    private float _hudPoll;
    private int _total;
    private int _destroyed;
    /// <summary>台词节点：0 未播 / 1 已播第1句 / 2 已播第2句。</summary>
    private int _lineStage;
    private readonly Godot.Collections.Array<String> _lines = new();
    private float _cooldownLeft;
    private CommOverlay? _comm;
    private Hud? _hud; // U13：typed

    /// <summary>A5：spawner 依赖注入（main._ready 调用；替代 group 现找）。</summary>
    public void SetSpawner(Node spawner) => _spawner = spawner as Spawner;

    /// <summary>A7：测试/诊断白盒断言经公开接口。</summary>
    public State GetState() => _state;

    public Godot.Collections.Array<String> Lines() => _lines;

    public Godot.Collections.Array<TurretBattery> Turrets() => _turrets;

    public int Total() => _total;

    public int LineStage() => _lineStage;

    public CommOverlay? Comm() => _comm;

    public void SetCooldownLeft(float seconds) => _cooldownLeft = seconds;

    public void SetState(State pState) => _state = pState;

    public float CooldownLeft() => _cooldownLeft;

    public override void _Ready()
    {
        Duration = (float)GameState.Instance.Cfg("elite_turret_event.duration", Duration).AsDouble();
        // 2026-08-10 健壮性审查：enter_time 钳下限（StrikeCarrier.ENTER 的 _enterT/该值除零，
        // Clamp 兜底无 NaN，但降入瞬完成、视觉跳变）
        EnterTime = Mathf.Max((float)GameState.Instance.Cfg("elite_turret_event.enter_time", EnterTime).AsDouble(), 0.05f);
        RiseTime = (float)GameState.Instance.Cfg("elite_turret_event.rise_time", RiseTime).AsDouble();
        BossResumeDelay = (float)GameState.Instance.Cfg("elite_turret_event.boss_resume_delay", BossResumeDelay).AsDouble();
        TurretHpBase = (int)GameState.Instance.Cfg("elite_turret_event.turret_hp_base", TurretHpBase).AsInt64();
        // K14（H13 同族延续）：turret_counts/ammo_sequences 判型回退——非 Dictionary 时
        // 后续 .get() 在 Variant 上调用会运行时崩溃（G06 口径只覆盖了 fire_interval 等标量）
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

        // H13（健壮性审核）：fire_interval 判型回退（G06 口径，防非数组/短数组 _ready 崩溃）
        var fi = GameState.Instance.Cfg(
            "elite_turret_event.fire_interval", new Godot.Collections.Array { FireInterval.X, FireInterval.Y });
        if (fi.VariantType == Variant.Type.Array && fi.AsGodotArray().Count >= 2)
        {
            var fiArr = fi.AsGodotArray();
            FireInterval = new Vector2((float)fiArr[0].AsDouble(), (float)fiArr[1].AsDouble());
        }

        // R07：WEAK_LOCK 判型（K14 同族延续）——非 Dictionary 时 :203 透传给
        // turret.Setup 的弱锁参数会在消费方崩溃，与 TURRET_COUNTS 同口径回退
        var wl = GameState.Instance.Cfg("elite_turret_event.weak_lock", WeakLock);
        if (wl.VariantType == Variant.Type.Dictionary)
        {
            WeakLock = wl.AsGodotDictionary();
        }

        RewardScore = (int)GameState.Instance.Cfg("elite_turret_event.reward_score", RewardScore).AsInt64();
        HoverY = (float)GameState.Instance.Cfg("elite_turret_event.carrier.hover_y", HoverY).AsDouble();
        Cooldown = (float)GameState.Instance.Cfg("elite_turret_event.cooldown", Cooldown).AsDouble();
        _comm = new CommOverlay();
        AddChild(_comm);
        // U16：K15 对称兜底——与 FormationStrikeEvent 同款（事件节点先于 spawner 入树时
        // 注入为 null，Boss 冻结/波次暂停钩子会静默失效；兜底 group 现找）
        _spawner ??= GetTree().GetFirstNodeInGroup("spawner") as Spawner;
    }

    public bool IsActive() => _state != State.IDLE;

    /// <summary>触发条件：IDLE 且冷却结束（Boss 互斥由 spawner 侧检查）。</summary>
    public bool CanTrigger()
    {
        if (_state != State.IDLE || _cooldownLeft > 0.0f)
        {
            return false;
        }

        // L13：母舰在场期不触发——母舰自动火力（玩家弹阵营）可摧毁事件单位并全额发奖，
        // 玩家进保护舱零参与挂机收益；在场判定经惰性缓存（U14：原每帧组查询，节点失效重查）
        if (MothershipPresent())
        {
            return false;
        }

        return true;
    }

    /// <summary>母舰在场惰性缓存：首次查得后缓存引用，释放/退组失效自动重查（替代每帧组查询）。</summary>
    private Node? _mothershipCache;

    private bool MothershipPresent()
    {
        if (_mothershipCache != null && GodotObject.IsInstanceValid(_mothershipCache)
            && _mothershipCache.IsInGroup("mothership"))
        {
            return true;
        }

        _mothershipCache = GetTree().GetFirstNodeInGroup("mothership");
        return _mothershipCache != null;
    }

    /// <summary>事件启动（互斥检查通过后由事件管理器调用）。</summary>
    public void Start()
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

        // 冻结 Boss 调度 + 暂停普通波次（spawner 钩子；A5 注入 _spawner。U14：typed 直调）
        _spawner?.SetBossFrozen(true);
        _spawner?.SetWavesPaused(true);

        _carrier = new StrikeCarrier();
        var carrier = _carrier;
        var evView = GameState.Instance.ViewWorldRect(); // D10：载体入场锚点统一 view 基线
        carrier.Position = new Vector2(evView.GetCenter().X, evView.Position.Y - 450.0f);
        carrier.Entered += OnCarrierEntered;
        carrier.Exited += OnCarrierExited;
        GetParent().AddChild(carrier);
        // 2026-08-06 审计：HOVER_Y 为距可见区顶缘偏移（D10 同族遗漏）——原绝对 y 在
        // 非默认视角档（zoom>1 可见区下移）偏高 140~222px，炮塔行锚点随之偏高
        carrier.Enter(evView.Position.Y + HoverY, EnterTime);
        GameState.Instance.Shake(GameState.Instance.Cfg("elite_turret_event.carrier.shake", 4.0).AsDouble());
        _hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
    }

    /// <summary>返航中止（main._start_homecoming 调用）：IDLE 直接返回；清掉在场炮塔（queue_free
    /// 不触发 died 计分，自行清理注册清单）、隐藏 HUD 事件条、恢复普通波次，航母按完整
    /// 撤离处理；Boss 解冻/_boss_pending 补触发沿用现有 BOSS_DELAY → OnBossDelayEnd。</summary>
    public void Abort()
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
            _comm.Clear(); // B13：清掉已显台词，避免返航恢复后残留
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
        // Q16（2026-08-05）：turret_counts 上限钳制——配置 >5 时 SOCKETS[i] 越界崩溃
        //（StrikeCarrier.Sockets 固定 5 槽）。
        // 2026-08-10：难度键条目值判型（K14 只判容器层）——字符串/数组等坏值 AsInt64 抛
        // InvalidCastException 崩溃（事件触发即崩），坏值回退默认 4
        // AC13（2026-08-11）：下限 0/负 → 无炮塔事件空跑（30s 倒计时 + BOSS_DELAY 4s，
        // Boss 冻结/波次暂停共 34s 玩家干等）——钳 [1, Sockets.Length]（Q16 只封上限）
        var diffStr = GameState.Instance.Difficulty.ToString();
        var tcV = TurretCounts.GetValueOrDefault(diffStr, new Variant());
        var rawTotal = tcV.VariantType is Variant.Type.Int or Variant.Type.Float ? (int)tcV.AsInt64() : 4;
        _total = Mathf.Clamp(rawTotal, 1, StrikeCarrier.Sockets.Length);
        // HP 三级乘算：基准 × 难度档 × 对局进程 ramp（与普通敌机同口径，避免后期退化为送分道具）
        var hp = Mathf.Max(
            1,
            (int)Mathf.Round(
                TurretHpBase
                * (float)GameState.Instance.EnemyHpMultiplier()
                * (float)GameState.Instance.EnemyHpRamp()));
        // 2026-08-06 审计：ammo 条目级判型（K14 只判容器 Dictionary 未判难度键条目）——
        // 难度键缺失/非 Array 时 `for a in p_ammo` 崩溃（boss patterns 侧有 L07 元素级判型）；
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
        if (_cooldownLeft > 0.0f && _state == State.IDLE)
        {
            _cooldownLeft -= d;
        }

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

        // 2026-08-03 审计：收回中的炮塔已无 died 依赖（_ceased 守卫），立即清引用数组，
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

    /// <summary>普通波次在 CARRIER_EXIT 起恢复（Boss 冻结保留到 BOSS_DELAY 结束）。</summary>
    private void ResumeWaves()
    {
        _spawner?.SetWavesPaused(false);
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

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public static int GetStateIdle() => (int)State.IDLE;

    public static int GetStateCarrierEnter() => (int)State.CARRIER_ENTER;

    public static int GetStateTurretActive() => (int)State.TURRET_ACTIVE;

    public static int GetStateCarrierExit() => (int)State.CARRIER_EXIT;

    public static int GetStateBossDelay() => (int)State.BOSS_DELAY;

    public int state() => (int)GetState();

    public float DURATION { get => Duration; set => Duration = value; }

    public float ENTER_TIME { get => EnterTime; set => EnterTime = value; }

    public float RISE_TIME { get => RiseTime; set => RiseTime = value; }

    public float BOSS_RESUME_DELAY { get => BossResumeDelay; set => BossResumeDelay = value; }

    public Vector2 FIRE_INTERVAL { get => FireInterval; set => FireInterval = value; }

    public float HOVER_Y { get => HoverY; set => HoverY = value; }
}
