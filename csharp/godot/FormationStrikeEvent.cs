using Godot;

namespace InfiAir;

/// <summary>
/// 轰炸编队事件编排（docs/FORMATION_STRIKE_EVENT.md）：最低优先级随机遭遇——
/// IDLE → FORMATION_ENTER（自屏顶外靠近）→ FORMATION_TURN（90° 转航向）
/// → BOMBING_RUN（横穿交错投弹）→ FORMATION_EXIT（加速离场）→ IDLE（冷却）。
/// 不冻结 Boss 调度；2026-07-29 修订为占用波次槽——运行期间暂停普通波次
/// （Start() 置 spawner 波次暂停，与精英炮塔事件互斥，见设计文档 §1/§2）；
/// 可被返航 Abort() 打断（无结算，冷却照计）。编队锚点运动与战机偏移/朝向由本节点
/// _Process 驱动；状态计时全在 _Process，不产生 Timer 节点。动态实体（战机/炸弹）一律挂 Main 下。
/// CommOverlay（C# 同程序集 typed）；FormationCraft/FormationBomb 为 C# typed 直调。
/// </summary>
public partial class FormationStrikeEvent : Node, IEncounterEvent // U14：遭遇契约接口（管理器 typed 轮询）
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
    public float BombInterval { get; set; } = 0.8f;
    public int BombsPerCraft { get; set; } = 2;
    public float BombFallSpeed { get; set; } = 300.0f;
    public float BombFuse { get; set; } = 1.2f;
    public int BombDamage { get; set; } = 20;
    public float BombRadius { get; set; } = 120.0f;
    public int RewardAllClear { get; set; } = 200;

    private State _state = State.IDLE;
    private float _stateTime;
    private float _cooldownLeft;
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
    /// <summary>已投弹计数（测试可观测）。</summary>
    private int _dropped;
    /// <summary>台词层（U14：typed 化——原降级 CanvasLayer + 动态派发，同族 EliteTurretEvent 为 typed）。</summary>
    private CommOverlay? _comm;
    private Spawner? _spawner;

    // ---- 热路径缓存：score / view_world_rect 每处理帧一次动态调用（全事件实例共享） ----
    private static ulong _frame = ulong.MaxValue;
    private static Rect2 _frameView;

    private static void RefreshFrameCache()
    {
        var f = Engine.GetProcessFrames();
        if (f != _frame)
        {
            _frame = f;
            _frameView = GameState.Instance.ViewWorldRect();
        }
    }

    private static Rect2 CachedView()
    {
        RefreshFrameCache();
        return _frameView;
    }

    /// <summary>K15：spawner 依赖注入（main._ready 调用，A5 延续——替代 group 现找，与 EliteTurretEvent 同款）。</summary>
    public void SetSpawner(Node spawner) => _spawner = spawner as Spawner; // U14：typed 字段

    public override void _Ready()
    {
        MinScore = (int)GameState.Instance.Cfg("formation_strike_event.min_score", MinScore).AsInt64();
        Cooldown = (float)GameState.Instance.Cfg("formation_strike_event.cooldown", Cooldown).AsDouble();
        // Q14（2026-08-05）：craft_counts 判型回退（K14 精英侧同口径）——配置损坏为非 Dictionary
        // 时 start() 的 .get() 在 Variant 上运行时崩溃
        var cc = GameState.Instance.Cfg("formation_strike_event.craft_counts", CraftCounts);
        if (cc.VariantType == Variant.Type.Dictionary)
        {
            CraftCounts = cc.AsGodotDictionary();
        }

        CraftHpBase = (int)GameState.Instance.Cfg("formation_strike_event.craft_hp_base", CraftHpBase).AsInt64();
        CraftScore = (int)GameState.Instance.Cfg("formation_strike_event.craft_score", CraftScore).AsInt64();
        // Q15（2026-08-05）：approach_speed 下限钳制——≤0 时编队永驻 FORMATION_ENTER，
        // 波次暂停常驻 → 普通波次与 Boss 调度全冻结
        ApproachSpeed = Mathf.Max(
            (float)GameState.Instance.Cfg("formation_strike_event.approach_speed", ApproachSpeed).AsDouble(), 1.0f);
        ApproachY = (float)GameState.Instance.Cfg("formation_strike_event.approach_y", ApproachY).AsDouble();
        TurnTime = (float)GameState.Instance.Cfg("formation_strike_event.turn_time", TurnTime).AsDouble();
        RunSpeed = (float)GameState.Instance.Cfg("formation_strike_event.run_speed", RunSpeed).AsDouble();
        BombInterval = (float)GameState.Instance.Cfg("formation_strike_event.bomb_interval", BombInterval).AsDouble();
        BombsPerCraft = (int)GameState.Instance.Cfg("formation_strike_event.bombs_per_craft", BombsPerCraft).AsInt64();
        BombFallSpeed = (float)GameState.Instance.Cfg("formation_strike_event.bomb_fall_speed", BombFallSpeed).AsDouble();
        BombFuse = (float)GameState.Instance.Cfg("formation_strike_event.bomb_fuse", BombFuse).AsDouble();
        BombDamage = (int)GameState.Instance.Cfg("formation_strike_event.bomb_damage", BombDamage).AsInt64();
        BombRadius = (float)GameState.Instance.Cfg("formation_strike_event.bomb_radius", BombRadius).AsDouble();
        RewardAllClear = (int)GameState.Instance.Cfg("formation_strike_event.reward_all_clear", RewardAllClear).AsInt64();
        _comm = new CommOverlay();
        AddChild(_comm);
        // K15：A5 依赖注入延续——由 main._ready 经 set_spawner 注入，替代 group 现找
        //（原实现事件节点先于 spawner 入树时 _spawner=null，互斥检查与波次暂停钩子静默失效）
        _spawner ??= GetTree().GetFirstNodeInGroup("spawner") as Spawner;
    }

    public bool IsActive() => _state != State.IDLE;

    /// <summary>A7：测试/诊断白盒断言经公开接口。</summary>
    public State GetState() => _state;

    public Godot.Collections.Array GetCrafts() => _crafts;

    public int AliveCount() => _alive;

    public int DroppedCount() => _dropped;

    public void SetCooldownLeft(float seconds) => _cooldownLeft = seconds;

    public float CooldownLeft() => _cooldownLeft;

    /// <summary>触发条件（最低优先级）：自身 IDLE 且冷却结束、分数达标、Boss 未激活、精英炮塔事件未激活。
    /// 掷签间隔/概率由 spawner 侧持有（elite 事件在本事件之前检查，本 tick 先启动则 is_active 拦截）。</summary>
    public bool CanTrigger()
    {
        // 分数实时读取（不走帧缓存）：测试/调用方同帧改分须立即生效（原 GDScript 直读
        // GameState.score 语义；帧缓存仅保留给 _process 热路径的 CachedView）
        var liveScore = (int)GameState.Instance.Score;
        if (_state != State.IDLE || _cooldownLeft > 0.0f || liveScore < MinScore)
        {
            return false;
        }

        // L13：母舰在场期不触发（同 elite：母舰自动火力清事件单位全额发奖，玩家零参与挂机）。
        // U14：惰性缓存替代每帧组查询（节点释放失效自动重查）
        if (MothershipPresent())
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

    /// <summary>母舰在场惰性缓存（同 EliteTurretEvent：首次查得缓存，释放/退组失效重查，替代每帧组查询）。</summary>
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

    /// <summary>事件启动（互斥检查通过后由 spawner 调用）。</summary>
    public void Start()
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
        // 占用波次槽：事件期间暂停普通波次（结束/打断时恢复。U14：typed 直调）
        if (_spawner != null && GodotObject.IsInstanceValid(_spawner))
        {
            _spawner.SetWavesPaused(true);
        }

        var view = CachedView();
        var x0 = (float)GD.RandRange(view.Position.X + view.Size.X * 0.4, view.Position.X + view.Size.X * 0.6);
        _anchor = new Vector2(x0, view.Position.Y - 120.0f);
        // 生成编队：长机居中，僚机后掠 ±55px 递增（楔形，槽位稳定）
        var difficulty = (string)(StringName)GameState.Instance.Difficulty;
        var count = (int)CraftCounts.GetValueOrDefault(difficulty, 4).AsInt64();
        // HP 三级乘算：基准 × 难度档 × 对局进程 ramp（与普通敌机同口径）
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
    }

    /// <summary>返航打断：编队立即解散离场，无结算，冷却照计（已投放的炸弹自然存续）。</summary>
    public void Abort()
    {
        if (_state == State.IDLE)
        {
            return;
        }

        FreeCrafts();
        _state = State.IDLE;
        _cooldownLeft = Cooldown;
        ResumeWaves();
        _comm?.Clear(); // B13：清掉已显警告台词，避免返航恢复后残留
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        if (_state == State.IDLE)
        {
            if (_cooldownLeft > 0.0f)
            {
                _cooldownLeft -= d;
            }

            return;
        }

        _stateTime += d;
        switch (_state)
        {
            case State.FORMATION_ENTER:
                _anchor.Y += ApproachSpeed * d;
                if (_anchor.Y >= CachedView().Position.Y + ApproachY)
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
                    var view = CachedView();
                    // 出界余量按投弹表剩余最大时长折算（2026-08-03 审计）：原固定 ±120 会在 hard 5 机
                    // 投弹段（最长 3.6s）未完时截断末机炸弹，最坏第 5 机 0 投弹；余量动态 = 末弹时刻 × 速度
                    var runMargin = _dropTimes.Count > 0 ? _dropTimes[_dropTimes.Count - 1] * RunSpeed : 0.0f;
                    if (_dropIndex >= _dropTimes.Count
                        || _anchor.X < view.Position.X - runMargin - 120.0f
                        || _anchor.X > view.End.X + runMargin + 120.0f)
                    {
                        BeginExit();
                    }

                    break;
                }

            case State.FORMATION_EXIT:
                _exitSpeed += 420.0f * d;
                _anchor += Vector2.Right.Rotated(_heading) * _exitSpeed * d;
                if (_stateTime >= ExitTime)
                {
                    Finish();
                }

                break;
        }

        UpdateCrafts();
    }

    /// <summary>转航向：朝较远侧缘方向 90° 转向。</summary>
    private void BeginTurn()
    {
        _state = State.FORMATION_TURN;
        _stateTime = 0.0f;
        var view = CachedView();
        _turnTarget = _anchor.X < view.Position.X + view.Size.X * 0.5f ? 0.0f : Mathf.Pi;
    }

    /// <summary>横穿投弹：构建交错投弹时刻表（长机先投，僚机错开 bomb_interval，同机间隔 0.4s）。</summary>
    private void BeginRun()
    {
        _state = State.BOMBING_RUN;
        _stateTime = 0.0f;
        _dropTimes.Clear();
        _dropCraft.Clear();
        _dropIndex = 0;
        // 循环次序 i 外层 / k 内层（2026-08-02 修复）：原 k 外层产生非排序时刻表
        // [0,0.8,1.6,2.4,0.4,...]，ProcessDrops 按单调 _stateTime 贪心消费会把第二波炸弹堆积到末尾同帧
        for (var i = 0; i < _crafts.Count; i++)
        {
            for (var k = 0; k < BombsPerCraft; k++)
            {
                _dropTimes.Add(i * BombInterval + k * BombStagger);
                _dropCraft.Add(i);
            }
        }
    }

    /// <summary>离场：沿当前航向加速穿出侧缘。</summary>
    private void BeginExit()
    {
        _state = State.FORMATION_EXIT;
        _stateTime = 0.0f;
        _exitSpeed = RunSpeed;
    }

    /// <summary>离场结束：清理剩余战机，回 IDLE 进冷却。</summary>
    private void Finish()
    {
        FreeCrafts();
        _state = State.IDLE;
        _cooldownLeft = Cooldown;
        ResumeWaves();
    }

    /// <summary>恢复普通波次（事件结束/打断时；精英炮塔事件可能同时持有暂停，以其自身恢复为准）。</summary>
    private void ResumeWaves()
    {
        if (_spawner != null && GodotObject.IsInstanceValid(_spawner))
        {
            _spawner.SetWavesPaused(false);
        }
    }

    /// <summary>按时刻表投弹：投弹点即当前位置正下方；已毁机跳过（时刻表照走）。</summary>
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

            var bomb = new FormationBomb();
            var dir = Vector2.Right.Rotated(_heading);
            // 炸弹伤害随对局进程 ramp（与敌弹同一系数）
            bomb.Setup(
                new Vector2(dir.X * RunSpeed * 0.35f, BombFallSpeed),
                BombFuse,
                Mathf.Max(1, (int)Mathf.Round(BombDamage * (float)GameState.Instance.EnemyDamageRamp())),
                BombRadius);
            bomb.Position = craft.Position + new Vector2(0.0f, 18.0f) * (float)GameState.Instance.WorldScale;
            GetParent().AddChild(bomb);
            _dropped++;
        }
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

    /// <summary>击坠：单机得分（add_score 内乘难度倍率）；全歼 → 全歼奖励 + 提前离场。</summary>
    private void OnCraftDied(FormationCraft craft, int index)
    {
        if (index >= 0 && index < _crafts.Count
            && _crafts[index].VariantType != Variant.Type.Nil
            && (FormationCraft)_crafts[index] == craft)
        {
            _crafts[index] = new Variant();
        }

        _alive = Mathf.Max(0, _alive - 1);
        GameState.Instance.AddScore(CraftScore);
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

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public State state() => GetState();

    // GDScript 无法以类名引用 C# 嵌套枚举（实测）——状态值经静态方法访问（脚本资源可调）
    public static int GetStateIdle() => (int)State.IDLE;

    public static int GetStateFormationEnter() => (int)State.FORMATION_ENTER;

    public static int GetStateFormationTurn() => (int)State.FORMATION_TURN;

    public static int GetStateBombingRun() => (int)State.BOMBING_RUN;

    public static int GetStateFormationExit() => (int)State.FORMATION_EXIT;
}
