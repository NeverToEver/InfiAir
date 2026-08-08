using System;
using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 敌机生成器：波次化刷新（普通波成组均布入场、按分数阶段解锁机型）+ 特殊槽调度
/// （每 3~4 个普通波一个精英波；Boss/精英/事件占用特殊槽，精英/Boss 击杀后追加休整波次）
/// + Boss 触发（4 种轮换，2026-08-04 扩 4 型含月蚀）。遭遇事件（精英炮塔/轰炸编队）触发策略
/// 自 2026-08-05 起由统一事件管理器接管（GameState.events，docs/EVENT_MANAGER.md）：本类仅保留
/// 互斥钩子（Boss 冻结/波次暂停）与特殊槽登记（notify_event_triggered）。
/// M6 全量迁移（2026-08-08 自 scripts/spawner.gd）：原 M3b/M3d 经脚本资源判型/实例化
/// （_spawn_telegraph_script/_enemy_script/_boss_script）在 C# 侧改为 typed 直调（SpawnTelegraph/
/// Enemy/Boss 均为 C# 类）；UPPER_SNAKE 配置表为实例属性（规则 19：静态字段禁持 Godot 对象）。
/// </summary>
public partial class Spawner : Node
{
    /// <summary>Boss 降入完成（_spawn_boss 后发出；main.gd/_hud 连接，M6 改连 PascalCase 名）。</summary>
    [Signal]
    public delegate void BossSpawnedEventHandler(Boss boss);

    /// <summary>Boss 出场预警（_trigger_boss 时发出；main.gd/_hud 连接，M6 改连 PascalCase 名）。</summary>
    [Signal]
    public delegate void BossWarningEventHandler();

    /// <summary>排队中的一次性回调 Timer 与屏上入场预告线（D01）。返航时经 clear_pending() 释放，
    /// 防止 continue 继续出击后、入场动画窗口内敌机/Boss 带预告进场。</summary>
    private readonly List<Godot.Timer> _pendingTimers = new();
    private readonly List<Node2D> _pendingTelegraphs = new();

    // 静态资源原为 GDScript const preload；规则 19 禁静态持 Godot 对象 → 实例只读字段
    private readonly PackedScene _bossScene = GD.Load<PackedScene>("res://scenes/boss.tscn");

    /// <summary>贴图复用常量（2026-08-04：分裂者复用 3 型、重装炮台复用精英 1 型）。</summary>

    private Godot.Collections.Array<Godot.Collections.Dictionary> _enemyTypes = null!;
    private Godot.Collections.Array<Godot.Collections.Dictionary> _eliteTypes = null!;

    /// <summary>普通机型配置表（贴图即机型，数值差异化；弹种池仅 single/spread）
    /// scale 为纯视觉缩放：以锁定环/碰撞提示等指示器尺寸为锚（不动指示器），舰船视觉应明显大于指示器
    /// HP 定标（A11）：玩家弹伤 10、射速 0.15s 下 TTK≈1.2s（对齐原作 DPS 平衡器稳态）。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> ENEMY_TYPES
    {
        get => _enemyTypes;
        set => _enemyTypes = value;
    }

    /// <summary>精英机型配置表（弹种池仅 spread/laser）
    /// HP ≈ 普通均值 ×2.5（对齐原作精英倍率）；radius 与普通机同档
    /// （A10：原作精英碰撞盒不大于普通机，"精英更大"为疑似 bug 不移植）。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> ELITE_TYPES
    {
        get => _eliteTypes;
        set => _eliteTypes = value;
    }

    /// <summary>机型 i 在分数 >= UNLOCK_SCORES[i] 时解锁（5 档对应 5 型普通机；
    /// 2026-08-06 审计 H2：分裂者落地时未扩展解锁表 → 上界 mini(5, 4) 截断永不入池）。</summary>
    public Godot.Collections.Array<int> UNLOCK_SCORES { get; set; } = new() { 0, 300, 800, 1500, 2500 };

    /// <summary>波次节奏：普通波成组刷新，间隔/规模随对局时间 ramp。</summary>
    public float WAVE_INTERVAL_START { get; set; } = 7.0f;
    public float WAVE_INTERVAL_END { get; set; } = 4.0f;
    public float RAMP_TIME { get; set; } = 300.0f;

    /// <summary>Boss 击杀难度乘数对波次间隔的影响系数。</summary>
    public float DIFFICULTY_FACTOR { get; set; } = 0.15f;
    public float INTERVAL_MIN { get; set; } = 2.5f;
    public int WAVE_SIZE_START { get; set; } = 3;
    public int WAVE_SIZE_END { get; set; } = 5;

    /// <summary>特殊槽：每 SPECIAL_GAP_MIN~MAX 个普通波出一个精英波；Boss/事件触发同样占用并清零计数。</summary>
    public int SPECIAL_GAP_MIN { get; set; } = 3;
    public int SPECIAL_GAP_MAX { get; set; } = 4;

    /// <summary>休整：精英/Boss 击杀后追加的普通波次数（计数置负，拉长下一个特殊槽间隔）。</summary>
    public int REST_WAVES_AFTER_KILL { get; set; } = 2;
    public int ELITE_WAVE_SIZE { get; set; } = 1;
    public int BOSS_SCORE_STEP { get; set; } = 1500;

    /// <summary>Boss 触发最小间隔：分数步进触发需同时满足该时间门（防分数暴涨期连出 Boss）。</summary>
    public float BOSS_MIN_INTERVAL { get; set; } = 80.0f;
    public float BOSS_TIME_LIMIT { get; set; } = 120.0f;

    private float _waveTimer = 1.5f;
    private float _elapsed;

    /// <summary>特殊槽计数：普通波 +1；精英波/Boss/事件触发清零；精英/Boss 击杀置负（休整）。</summary>
    private int _wavesSinceSpecial;

    /// <summary>本周期特殊槽间隔（计数清零/休整时重抽，SPECIAL_GAP_MIN~MAX）。</summary>
    private int _nextSpecialGap = 3;

    /// <summary>敌机悬停带缓存（与 Enemy.HOVER_BAND 同源，相对可见区域顶缘的偏移，供波次锚点分配）。</summary>
    private Vector2 _hoverBand = new(150.0f, 430.0f);

    private float _bossTimer;
    private int _nextBossScore;

    private bool _bossActive;

    /// <summary>精英炮塔事件互斥：事件期间 Boss 触发被冻结（到期记 _boss_pending 一次，不累积）。</summary>
    private bool _bossFrozen;
    private bool _bossPending;

    /// <summary>事件期间普通波次暂停。</summary>
    private bool _wavesPaused;

    /// <summary>事件编排节点（main 在 _ready 登记；遭遇事件触发策略由统一事件管理器接管，
    /// 本引用供互斥查询（formation.can_trigger 检查 elite active）与测试访问器）。</summary>
    private Node? _event;

    /// <summary>轰炸编队事件编排节点（main 在 _ready 登记；互斥/访问器同上）。</summary>
    private Node? _formation;

    public Spawner()
    {
        _enemyTypes = BuildEnemyTypes();
        _eliteTypes = BuildEliteTypes();
        _nextBossScore = BOSS_SCORE_STEP;
    }

    public override void _Ready()
    {
        AddToGroup("spawner");
        ApplyBalance();
    }

    /// <summary>数值配置注入：机型表数值覆盖（贴图/策略/弹种池留在脚本），常量读入。</summary>
    private void ApplyBalance()
    {
        // L06（2026-08-03 审查）：间隔键下限钳制（H15 同族遗漏）——wave_interval_start ≤ 0 时
        // _current_interval 的 clampf 上界 ≤0 返回负值，_wave_timer 恒 ≤0 每帧刷一波（预告线
        // /Timer 无界增长挂死）；ramp_time ≤ 0 时 ramp 曲线瞬时跳变
        WAVE_INTERVAL_START = Mathf.Max((float)GameState.Instance.Cfg("spawner.wave_interval_start", WAVE_INTERVAL_START).AsDouble(), 0.05f);
        WAVE_INTERVAL_END = Mathf.Max((float)GameState.Instance.Cfg("spawner.wave_interval_end", WAVE_INTERVAL_END).AsDouble(), 0.05f);
        RAMP_TIME = Mathf.Max((float)GameState.Instance.Cfg("spawner.ramp_time", RAMP_TIME).AsDouble(), 0.01f);
        INTERVAL_MIN = Mathf.Max((float)GameState.Instance.Cfg("spawner.interval_min", INTERVAL_MIN).AsDouble(), 0.0f);
        BOSS_SCORE_STEP = (int)GameState.Instance.Cfg("spawner.boss_score_step", BOSS_SCORE_STEP).AsDouble();
        BOSS_MIN_INTERVAL = (float)GameState.Instance.Cfg("spawner.boss_min_interval", BOSS_MIN_INTERVAL).AsDouble();
        BOSS_TIME_LIMIT = (float)GameState.Instance.Cfg("spawner.boss_time_limit", BOSS_TIME_LIMIT).AsDouble();
        DIFFICULTY_FACTOR = (float)GameState.Instance.Cfg("spawner.difficulty_factor", DIFFICULTY_FACTOR).AsDouble();
        // C18：cfg 返回 Variant，显式转 Array[int] 再赋 typed 变量
        var us = GameState.Instance.Cfg("spawner.unlock_scores", UNLOCK_SCORES);
        var usArr = new Godot.Collections.Array<int>();
        if (us.VariantType == Variant.Type.Array)
        {
            foreach (var v in us.AsGodotArray())
            {
                // L05（2026-08-03 审查）：元素级判型（E04 同族遗漏）——Dict 元素 int() 抛运行时
                // 错误（启动即崩）、字符串静默转 0 使全部机型开局解锁；非数字元素跳过
                if ((v.VariantType == Variant.Type.Int || v.VariantType == Variant.Type.Float) && v.VariantType != Variant.Type.Bool)
                {
                    usArr.Add((int)v.AsDouble());
                }
            }
        }

        UNLOCK_SCORES = usArr.Count > 0 ? usArr : new Godot.Collections.Array<int> { 0, 300, 800, 1500, 2500 };
        WAVE_SIZE_START = (int)GameState.Instance.Cfg("spawner.wave_size_start", WAVE_SIZE_START).AsDouble();
        WAVE_SIZE_END = (int)GameState.Instance.Cfg("spawner.wave_size_end", WAVE_SIZE_END).AsDouble();
        SPECIAL_GAP_MIN = (int)GameState.Instance.Cfg("spawner.special_gap_min", SPECIAL_GAP_MIN).AsDouble();
        SPECIAL_GAP_MAX = (int)GameState.Instance.Cfg("spawner.special_gap_max", SPECIAL_GAP_MAX).AsDouble();
        REST_WAVES_AFTER_KILL = (int)GameState.Instance.Cfg("spawner.rest_waves_after_kill", REST_WAVES_AFTER_KILL).AsDouble();
        ELITE_WAVE_SIZE = (int)GameState.Instance.Cfg("spawner.elite_wave_size", ELITE_WAVE_SIZE).AsDouble();
        // G06：嵌套结构判型（对齐 C03/E03 损坏 JSON 回退默认口径）——手改 JSON 使 band 非 2 元素数组时不崩溃
        var band = GameState.Instance.Cfg("enemies.hover_band", new Godot.Collections.Array { _hoverBand.X, _hoverBand.Y });
        if (band.VariantType == Variant.Type.Array)
        {
            var bandArr = band.AsGodotArray();
            if (bandArr.Count >= 2)
            {
                _hoverBand = new Vector2((float)bandArr[0].AsDouble(), (float)bandArr[1].AsDouble());
            }
        }

        // 遭遇事件触发参数（trigger_interval/trigger_chance/min_score）自 2026-08-05 起由
        // 统一事件管理器读取（scripts/event_manager.gd _load_balance，键不变）
        var normal = GameState.Instance.Cfg("enemies.types", new Godot.Collections.Array());
        if (normal.VariantType == Variant.Type.Array)
        {
            var normalArr = normal.AsGodotArray();
            for (var i = 0; i < Mathf.Min(normalArr.Count, ENEMY_TYPES.Count); i++)
            {
                MergeType(ENEMY_TYPES[i], normalArr[i]);
            }
        }

        var elites = GameState.Instance.Cfg("elites.types", new Godot.Collections.Array());
        if (elites.VariantType == Variant.Type.Array)
        {
            var elitesArr = elites.AsGodotArray();
            for (var i = 0; i < Mathf.Min(elitesArr.Count, ELITE_TYPES.Count); i++)
            {
                MergeType(ELITE_TYPES[i], elitesArr[i]);
            }
        }
    }

    private static bool IsNumber(Variant v) => (v.VariantType == Variant.Type.Int || v.VariantType == Variant.Type.Float) && v.VariantType != Variant.Type.Bool;

    private void MergeType(Godot.Collections.Dictionary dst, Variant srcV)
    {
        if (srcV.VariantType != Variant.Type.Dictionary)
        {
            return; // G06：结构损坏的机型条目整体跳过（回退脚本默认）
        }

        var src = srcV.AsGodotDictionary();
        // L05（2026-08-03 审查）：嵌套数组元素级判型（G06 只判容器形状）——Dict 元素
        // int()/float() 启动即崩、字符串静默 0（敌机 0 HP 秒死）；元素非数字整组跳过
        if (src.ContainsKey("hp"))
        {
            var hpV = src["hp"];
            if (hpV.VariantType == Variant.Type.Array)
            {
                var hpArr = hpV.AsGodotArray();
                if (hpArr.Count >= 2 && IsNumber(hpArr[0]) && IsNumber(hpArr[1]))
                {
                    dst["hp"] = new Vector2I((int)hpArr[0].AsDouble(), (int)hpArr[1].AsDouble());
                }
            }
        }

        if (src.ContainsKey("speed"))
        {
            var speedV = src["speed"];
            if (speedV.VariantType == Variant.Type.Array)
            {
                var speedArr = speedV.AsGodotArray();
                if (speedArr.Count >= 2 && IsNumber(speedArr[0]) && IsNumber(speedArr[1]))
                {
                    dst["speed"] = new Vector2((float)speedArr[0].AsDouble(), (float)speedArr[1].AsDouble());
                }
            }
        }

        foreach (var k in new[] { "score", "fire", "fire_interval", "scale", "radius" })
        {
            var v = src.GetValueOrDefault(k, new Variant());
            // 2026-08-03 审计（G06 口径对齐）：标量判型——坏值（字符串/数组）会在击杀结算
            // int(score_value) 处报类型错误；非数字整体跳过，回退脚本默认（bool 是 int 子类，排除）
            if (IsNumber(v))
            {
                dst[k] = v;
            }
        }
    }

    /// <summary>当前波次规模：随对局时间 ramp（WAVE_SIZE_START → WAVE_SIZE_END）。</summary>
    private int WaveSizeInternal()
    {
        var t = Mathf.Clamp(_elapsed / RAMP_TIME, 0.0f, 1.0f);
        return Mathf.Max(1, (int)Mathf.Round(Mathf.Lerp((float)WAVE_SIZE_START, (float)WAVE_SIZE_END, t)));
    }

    /// <summary>均分槽位取点：范围 [start, start+length] 均分 n 槽，取第 i 槽中心 ±25% 槽宽抖动。</summary>
    private float SlotPos(float start, float length, int n, int i)
    {
        var slot = length / n;
        return start + slot * (i + 0.5f) + (float)GD.RandRange(-0.25, 0.25) * slot;
    }

    /// <summary>当前分数阶段已解锁的普通机型池
    /// H07（健壮性审核）：unlock_scores 异常（全正/为空）时空池回退首型，防 randi()%0 崩溃。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> UnlockedTypes()
    {
        var pool = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        // 长度钳制：UNLOCK_SCORES 由 json 覆盖（_apply_balance）时可能短于 ENEMY_TYPES.size()，防越界
        for (var i = 0; i < Mathf.Min(ENEMY_TYPES.Count, UNLOCK_SCORES.Count); i++)
        {
            if (GameState.Instance.Score >= UNLOCK_SCORES[i])
            {
                pool.Add(ENEMY_TYPES[i]);
            }
        }

        if (pool.Count == 0)
        {
            pool.Add(ENEMY_TYPES[0]);
        }

        return pool;
    }

    /// <summary>当前在屏的 spread 弹种敌机数（离场中的不计）。
    /// B8 修复：改遍历 GameState.enemies 注册表（只含在屏活跃敌机）而非 "enemy" 组——
    /// 池化敌机 deactivate 时不 remove_from_group，组遍历会把池中闲置实例计入、虚抬 spread 上限。
    /// 2026-08-05：统一实体管理器 count_enemies 批量 API（docs/ENTITY_MANAGER.md）。</summary>
    private int CountSpreadEnemiesInternal()
    {
        return GameState.Instance.CountEnemies(Callable.From<GodotObject, bool>(e =>
            e is Enemy enemy && enemy.BulletType == "spread" && !enemy.IsExiting()));
    }

    /// <summary>从机型弹种池抽取弹种；spread 超同屏上限时退化（普通→single，精英→laser）。
    /// 同屏上限按难度取（GameState.spread_enemy_cap：easy 1 / medium 2 / hard 3）。</summary>
    private StringName PickBulletTypeInternal(Godot.Collections.Dictionary config)
    {
        var pool = (Godot.Collections.Array)config.GetValueOrDefault("bullet_types", new Godot.Collections.Array { new StringName("single") });
        // H07（健壮性审核）：空弹种池回退单发
        if (pool.Count == 0)
        {
            pool = new Godot.Collections.Array { new StringName("single") };
        }

        var btype = (StringName)pool[(int)(GD.Randi() % (uint)pool.Count)];
        if (btype == "spread" && CountSpreadEnemiesInternal() >= GameState.Instance.SpreadEnemyCap())
        {
            btype = (bool)config.GetValueOrDefault("elite", false) ? new StringName("laser") : new StringName("single");
        }

        return btype;
    }

    /// <summary>普通波：成组均布入场（x 按视口均分槽 + 抖动；锚点在悬停带内均分槽 + 抖动，加 view 顶基线）。</summary>
    private void SpawnNormalWaveInternal()
    {
        var pool = UnlockedTypes();
        var n = WaveSizeInternal();
        var view = GameState.Instance.ViewWorldRect();
        for (var i = 0; i < n; i++)
        {
            var config = pool[(int)(GD.Randi() % (uint)pool.Count)];
            var x = SlotPos(view.Position.X + 60.0f, view.Size.X - 120.0f, n, i);
            var anchor = SlotPos(view.Position.Y + _hoverBand.X, _hoverBand.Y - _hoverBand.X, n, i);
            QueueEnemy(config, x, anchor);
        }
    }

    /// <summary>精英波：占用特殊槽，ELITE_WAVE_SIZE 个精英均布入场；击杀触发休整。</summary>
    private void SpawnEliteWaveInternal()
    {
        var view = GameState.Instance.ViewWorldRect();
        for (var i = 0; i < ELITE_WAVE_SIZE; i++)
        {
            var config = ELITE_TYPES[(int)(GD.Randi() % (uint)ELITE_TYPES.Count)];
            var x = SlotPos(view.Position.X + 60.0f, view.Size.X - 120.0f, ELITE_WAVE_SIZE, i);
            QueueEnemy(config, x, (float)GD.RandRange(view.Position.Y + _hoverBand.X, view.Position.Y + _hoverBand.Y), true);
        }
    }

    /// <summary>单机随机入口（兼容旧调用/测试）：随机 x + 悬停带内随机锚点。</summary>
    private void SpawnEnemyInternal()
    {
        var pool = UnlockedTypes();
        var config = pool[(int)(GD.Randi() % (uint)pool.Count)];
        var view = GameState.Instance.ViewWorldRect();
        QueueEnemy(
            config,
            (float)GD.RandRange(view.Position.X + 60.0f, view.End.X - 60.0f),
            (float)GD.RandRange(view.Position.Y + _hoverBand.X, view.Position.Y + _hoverBand.Y));
    }

    /// <summary>单机入场：0.6s 红色入场预告后敌机才进场（波次与单机入口共用）。</summary>
    private void QueueEnemy(Godot.Collections.Dictionary config, float x, float anchor, bool special = false)
    {
        var strategies = (Godot.Collections.Array<StringName>)config["strategies"];
        var strategy = strategies[(int)(GD.Randi() % (uint)strategies.Count)];
        var btype = PickBulletTypeInternal(config);
        var view = GameState.Instance.ViewWorldRect();
        // R07：telegraph 时长判型 + 下限钳制（L 系列判型族登记遗留）——0/负值使
        // 预告线立即超时生成敌机或 Timer 反向；坏值回退脚本默认。
        // 2026-08-06 审计：时长同步注入预告线实例（原视觉 DURATION 硬编码 0.6，调参时
        // 视觉寿命与敌机出现时刻脱钩——预告线自毁与 _schedule 计时两套时钟）
        var td = GameState.Instance.Cfg("spawner.telegraph_duration", SpawnTelegraph.GetDefaultDuration());
        var telegraphDuration = Mathf.Max(
            td.VariantType == Variant.Type.Float || td.VariantType == Variant.Type.Int
                ? (float)td.AsDouble()
                : SpawnTelegraph.GetDefaultDuration(),
            0.01f);
        var telegraph = new SpawnTelegraph();
        telegraph.Position = new Vector2(x, view.Position.Y);
        telegraph.Duration = telegraphDuration;
        GetParent()!.AddChild(telegraph);
        _pendingTelegraphs.Add(telegraph);
        // 预告线自毁（超时 / clear_pending 释放）时解除登记，与 _pendingTimers 的 OnPendingTimerFired 对称
        telegraph.TreeExited += () => OnTelegraphFreed(telegraph);
        Schedule(telegraphDuration, () => OnTelegraphTimeout(config, strategy, btype, x, anchor, special));
    }

    /// <summary>预告计时结束后敌机实际进场（P1-1：普通波次统一走对象池，消灭每波 instantiate 抖动）。</summary>
    private void OnTelegraphTimeout(Godot.Collections.Dictionary config, StringName strategy, StringName btype, float x, float anchor, bool special)
    {
        var e = ((EnemyPool)GameState.Instance.EnemyPool!).Spawn(
            config, strategy, (float)GameState.Instance.DifficultyMultiplier,
            new Vector2(x, GameState.Instance.ViewWorldRect().Position.Y - 60.0f), btype);
        e.AnchorY = anchor;
        if (special)
        {
            e.Died += OnSpecialKilled;
        }
    }

    /// <summary>精英/Boss 击杀休整：追加 REST_WAVES_AFTER_KILL 个普通波才再出特殊槽。</summary>
    private void OnSpecialKilled(Enemy? enemy = null)
    {
        _wavesSinceSpecial = -REST_WAVES_AFTER_KILL;
        DrawSpecialGap();
    }

    /// <summary>重抽本周期特殊槽间隔。</summary>
    private void DrawSpecialGap()
    {
        _nextSpecialGap = GD.RandRange(SPECIAL_GAP_MIN, SPECIAL_GAP_MAX);
    }

    /// <summary>Boss-3 召唤的小怪（straight 型 1 型机），立即进场无预告。
    /// 作为 Main 的子节点，与正常敌机走同一套清场逻辑（返航/结算）。</summary>
    public Enemy SpawnMinion(Vector2 pos)
    {
        return ((EnemyPool)GameState.Instance.EnemyPool!).Spawn(
            ENEMY_TYPES[0], new StringName("straight"), (float)GameState.Instance.DifficultyMultiplier, pos);
    }

    /// <summary>Boss 出场流程：警告横幅 + 震动脉冲，2s 后 Boss 才降入。</summary>
    private void TriggerBossInternal()
    {
        _bossActive = true;
        _wavesSinceSpecial = 0; // Boss 占用特殊槽
        EmitSignal(SignalName.BossWarning);
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_warning", 14.0).AsDouble());
        Schedule(2.0f, () => SpawnBossInternal(0));
    }

    /// <summary>p_type &lt;= 0 时按击杀数轮换：第 N 只 Boss = 第 (N-1)%4+1 种（2026-08-04：轮换扩 4 型含月蚀）。</summary>
    private void SpawnBossInternal(int pType = 0)
    {
        _bossActive = true;
        if (pType <= 0)
        {
            pType = GameState.Instance.BossKills % 4 + 1; // 2026-08-04：轮换扩 4 型（月蚀）
        }

        var boss = _bossScene.Instantiate<Boss>();
        boss.Setup((float)GameState.Instance.DifficultyMultiplier, pType);
        boss.SetSpawner(this); // A5：依赖注入，替代 Boss 侧 group 现找
        var view = GameState.Instance.ViewWorldRect(); // D10：Boss 入场锚点统一 view 基线
        boss.Position = new Vector2(view.GetCenter().X, view.Position.Y - 160.0f);
        boss.Died += () => OnBossDied(boss);
        boss.Escaped += OnBossEscaped;
        GetParent()!.AddChild(boss);
        EmitSignal(SignalName.BossSpawned, boss);
    }

    /// <summary>Boss 离场统一结算。逃跑离场也会发 died（boss.gd 逃跑路径同时 emit escaped+died，
    /// 用于血条隐藏/生成器重排）；此处按 is_escaped 区分，只对真·击杀推进轮换与休整（B3 修复）。
    /// 逃跑期 collision_layer 已置 0（不再受弹），故逃跑中不存在"击毁"路径，is_escaped 判定无歧义。</summary>
    private void OnBossDied(Boss? boss = null)
    {
        _bossActive = false;
        _bossTimer = 0.0f;
        if (boss != null && boss.IsEscaped)
        {
            return; // 逃跑离场：不推进轮换、不给休整（与 OnBossEscaped 契约一致）
        }

        _nextBossScore += BOSS_SCORE_STEP;
        OnSpecialKilled(); // Boss 击杀休整
    }

    /// <summary>Boss 逃跑：不推进轮换、不给休整，仅解除波次/事件占用（Boss 计时重置，之后按分数/时间门再触发同型）。</summary>
    private void OnBossEscaped()
    {
        _bossActive = false;
        _bossTimer = 0.0f;
    }

    /// <summary>一次性计时回调。不用协程 await（SceneTreeTimer 或 Timer 均可）：退出时挂起的
    /// 协程函数状态会泄漏并连带持有其引用的资源；信号连接随 Timer 节点一并释放。</summary>
    private void Schedule(float seconds, Action callback)
    {
        var timer = new Godot.Timer { OneShot = true };
        AddChild(timer);
        timer.Timeout += () =>
        {
            callback();
            OnPendingTimerFired(timer);
        };
        _pendingTimers.Add(timer);
        timer.Start(seconds);
    }

    /// <summary>Timer 到点：解除登记并释放（连带 callback 信号连接一并随节点释放）。</summary>
    private void OnPendingTimerFired(Godot.Timer timer)
    {
        _pendingTimers.Remove(timer);
        timer.QueueFree();
    }

    /// <summary>预告线离树（0.6s 自毁或 clear_pending 释放）后解除登记，防悬空引用只增不减。</summary>
    private void OnTelegraphFreed(Node2D telegraph)
    {
        _pendingTelegraphs.Remove(telegraph);
    }

    /// <summary>清空排队回调与入场预告线（D01/G01）：返航时调用，防 continue 后入场动画窗口内敌机/Boss 进场。
    /// 未入场的 Boss 预警随之取消：仅当场上已无存活 Boss 时复位 _boss_active，否则波次/Boss/事件
    /// 三守卫被永久冻结（continue 后整局空转无怪无 Boss）——但 Boss 已在场（返航链明确保留 Boss，
    /// 2026-08-06 审计 H1 修法）时不得复位：_boss_timer 战时持续增长、_next_boss_score 仅击杀才推进，
    /// 复位后 continue 继续出击分数门控立即满足，会出第二个同型 Boss（轮换/休整/狂暴单槽编排脱节）。
    /// 复位后按分数/时间门控再触发，属预期。</summary>
    public void ClearPending()
    {
        // G01 修复（H1 扩展）：预警 2s 窗口内取消须解除占用（SpawnBossInternal 未执行则无 died/escaped 复位
        // 路径）；Boss 已生成在场上则由注册表判定——存活 Boss 存在时保持 _bossActive 占用
        if (GameState.Instance.CountEnemies(Callable.From<GodotObject, bool>(e => e is Boss)) == 0)
        {
            _bossActive = false;
        }

        foreach (var timer in _pendingTimers)
        {
            if (GodotObject.IsInstanceValid(timer))
            {
                timer.Stop();
                timer.QueueFree();
            }
        }

        _pendingTimers.Clear();
        foreach (var telegraph in _pendingTelegraphs)
        {
            if (GodotObject.IsInstanceValid(telegraph))
            {
                telegraph.QueueFree();
            }
        }

        _pendingTelegraphs.Clear();
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _elapsed += d;
        // Boss 激活与事件暂停期间波次计时冻结（Boss/事件占用波次槽）
        if (!_wavesPaused && !_bossActive)
        {
            _waveTimer -= d;
            if (_waveTimer <= 0.0f)
            {
                _waveTimer = CurrentIntervalInternal();
                if (_wavesSinceSpecial >= _nextSpecialGap)
                {
                    SpawnEliteWaveInternal();
                    _wavesSinceSpecial = 0;
                    DrawSpecialGap();
                }
                else
                {
                    SpawnNormalWaveInternal();
                    _wavesSinceSpecial += 1;
                }
            }
        }

        _bossTimer += d;
        // 分数触发需同时越过最小间隔（防分数暴涨期战后连出 Boss）；时间兜底不受此限
        if (!_bossActive && ((GameState.Instance.Score >= _nextBossScore && _bossTimer >= BOSS_MIN_INTERVAL) || _bossTimer >= BOSS_TIME_LIMIT))
        {
            // 精英炮塔事件期间 Boss 触发被冻结：只记录一次 pending（重复到期覆盖，不累积）
            if (_bossFrozen)
            {
                _bossPending = true;
            }
            else
            {
                TriggerBossInternal();
            }
        }
    }

    private float CurrentIntervalInternal()
    {
        var baseInterval = Mathf.Lerp(WAVE_INTERVAL_START, WAVE_INTERVAL_END, Mathf.Clamp(_elapsed / RAMP_TIME, 0.0f, 1.0f));
        // 难度倍率：easy ×1.25（更疏）/ medium ×1 / hard ×0.8（更密）
        var interval = baseInterval * (float)GameState.Instance.SpawnIntervalMultiplier()
            / (1.0f + DIFFICULTY_FACTOR * ((float)GameState.Instance.DifficultyMultiplier - 1.0f));
        // B 梯队（fair plan §8）：DDA 降档拉长波次间隔（只拉间隔不降收益，分数公平）；
        // clamp 上界同步乘因子，避免拉长效果被上限吞掉
        return Mathf.Clamp(
            interval * (float)GameState.Instance.DdaFactor(),
            INTERVAL_MIN,
            WAVE_INTERVAL_START * (float)GameState.Instance.SpawnIntervalMultiplier() * (float)GameState.Instance.DdaFactor());
    }

    // ---------------- 对外公开接口（A1 修复） ----------------
    // 事件互斥/Boss 调度/计时状态封装，禁止跨类直接写 _ 私有字段；PascalCase 为 C# typed 访问名，
    // snake_case 别名见文末兼容桥（M6 过渡期 GDScript 调用方/测试经动态派发访问）。

    public void SetEliteEvent(Node? eventNode) => _event = eventNode;

    public void SetFormationEvent(Node? eventNode) => _formation = eventNode;

    public void SetElapsed(float seconds) => _elapsed = seconds;

    /// <summary>A7：测试/诊断白盒断言经公开接口（命名语义化）。</summary>
    public void SpawnBoss(int pType = 0) => SpawnBossInternal(pType);

    public void SpawnEnemy() => SpawnEnemyInternal();

    public void SpawnNormalWave() => SpawnNormalWaveInternal();

    public int WaveSize() => WaveSizeInternal();

    public int CountSpreadEnemies() => CountSpreadEnemiesInternal();

    public StringName PickBulletType(Godot.Collections.Dictionary config) => PickBulletTypeInternal(config);

    public float CurrentInterval() => CurrentIntervalInternal();

    public void SetBossTimer(float seconds) => _bossTimer = seconds;

    public void SetNextBossScore(int scoreValue) => _nextBossScore = scoreValue;

    public void SetWaveTimer(float seconds) => _waveTimer = seconds;

    public void SetWavesSinceSpecial(int count) => _wavesSinceSpecial = count;

    public void SetBossActive(bool active) => _bossActive = active;

    public bool BossFrozen() => _bossFrozen;

    public bool WavesPaused() => _wavesPaused;

    public bool BossPending() => _bossPending;

    public void SetBossPending(bool pending) => _bossPending = pending;

    public Node? FormationEvent() => _formation;

    public Vector2 HoverBand() => _hoverBand;

    public void NotifySpecialKilled() => OnSpecialKilled();

    public void NotifyBossDied() => OnBossDied();

    public float BossTimer() => _bossTimer;

    public float WaveTimer() => _waveTimer;

    public int WavesSinceSpecial() => _wavesSinceSpecial;

    public float Elapsed() => _elapsed;

    public void SetBossFrozen(bool frozen) => _bossFrozen = frozen;

    public void SetWavesPaused(bool paused) => _wavesPaused = paused;

    public bool IsBossActive() => _bossActive;

    public Node? EliteEvent() => _event;

    /// <summary>事件占用特殊槽（统一事件管理器触发遭遇事件时调用；镜像原 _waves_since_special = 0）。</summary>
    public void NotifyEventTriggered() => _wavesSinceSpecial = 0;

    /// <summary>读取并清除一次 Boss pending（事件解冻时若期间触发过 Boss 则补触发）。</summary>
    public bool ConsumeBossPending()
    {
        var was = _bossPending;
        _bossPending = false;
        return was;
    }

    public void TriggerBoss() => TriggerBossInternal();

    /// <summary>普通机型配置表（见 ENEMY_TYPES 属性；表构造自实例字段贴图——规则 19）。</summary>
    /// <summary>默认普通机型表（M6：静态化供 Tutorial 读 [0]——教程只用 straight 基础型；局部构建非静态持有）。</summary>
    public static Godot.Collections.Array<Godot.Collections.Dictionary> BuildEnemyTypes()
    {
        return new Godot.Collections.Array<Godot.Collections.Dictionary>
        {
            new() { // 1 型 均衡
                ["texture"] = GD.Load<Texture2D>("res://assets/sprites/enemy_ship_1.png"),
                ["strategies"] = new Godot.Collections.Array<StringName> { "straight", "sine" },
                ["hp"] = new Vector2I(65, 72),
                ["speed"] = new Vector2(115.0f, 145.0f),
                ["score"] = 100,
                ["fire"] = 0.25f,
                ["fire_interval"] = 2.2f,
                ["scale"] = 0.62f,
                ["radius"] = 34.0f,
                ["bullet_types"] = new Godot.Collections.Array<StringName> { "single", "spread" },
            },
            new() { // 2 型 高速低 HP
                ["texture"] = GD.Load<Texture2D>("res://assets/sprites/enemy_ship_2.png"),
                ["strategies"] = new Godot.Collections.Array<StringName> { "zigzag", "dive" },
                ["hp"] = new Vector2I(48, 56),
                ["speed"] = new Vector2(175.0f, 225.0f),
                ["score"] = 150,
                ["fire"] = 0.3f,
                ["fire_interval"] = 2.4f,
                ["scale"] = 0.62f,
                ["radius"] = 34.0f,
                ["bullet_types"] = new Godot.Collections.Array<StringName> { "single", "spread" },
            },
            new() { // 3 型 高 HP 慢速
                ["texture"] = GD.Load<Texture2D>("res://assets/sprites/enemy_ship_3.png"),
                ["strategies"] = new Godot.Collections.Array<StringName> { "spiral", "hover" },
                ["hp"] = new Vector2I(95, 112),
                ["speed"] = new Vector2(75.0f, 100.0f),
                ["score"] = 200,
                ["fire"] = 0.4f,
                ["fire_interval"] = 2.0f,
                ["scale"] = 0.68f,
                ["radius"] = 38.0f,
                ["bullet_types"] = new Godot.Collections.Array<StringName> { "spread", "single" },
            },
            new() { // 4 型 高分开火狂
                ["texture"] = GD.Load<Texture2D>("res://assets/sprites/enemy_ship_4.png"),
                ["strategies"] = new Godot.Collections.Array<StringName> { "noise", "hover", "aggressive" },
                ["hp"] = new Vector2I(56, 66),
                ["speed"] = new Vector2(120.0f, 155.0f),
                ["score"] = 250,
                ["fire"] = 0.8f,
                ["fire_interval"] = 1.8f,
                ["scale"] = 0.62f,
                ["radius"] = 34.0f,
                ["bullet_types"] = new Godot.Collections.Array<StringName> { "spread", "single" },
            },
            new() { // 5 型 分裂者（2026-08-04）：死亡分裂 2 小机（×0.6 缩放/HP 半/无分数/不开火）
                ["texture"] = GD.Load<Texture2D>("res://assets/sprites/enemy_ship_3.png"),
                ["strategies"] = new Godot.Collections.Array<StringName> { "straight", "hover" },
                ["hp"] = new Vector2I(80, 92),
                ["speed"] = new Vector2(100.0f, 130.0f),
                ["score"] = 220,
                ["fire"] = 0.3f,
                ["fire_interval"] = 2.2f,
                ["scale"] = 0.66f,
                ["radius"] = 36.0f,
                ["bullet_types"] = new Godot.Collections.Array<StringName> { "single", "spread" },
                ["split"] = true,
            },
        };
    }

    /// <summary>精英机型配置表（见 ELITE_TYPES 属性；表构造自实例字段贴图——规则 19）。</summary>
    /// <summary>默认精英机型表（M6：静态化供 difficulty_test 读 [0]；局部构建非静态持有）。</summary>
    public static Godot.Collections.Array<Godot.Collections.Dictionary> BuildEliteTypes()
    {
        return new Godot.Collections.Array<Godot.Collections.Dictionary>
        {
            new() { // 重甲
                ["texture"] = GD.Load<Texture2D>("res://assets/sprites/elite_ship_1.png"),
                ["strategies"] = new Godot.Collections.Array<StringName> { "straight", "sine" },
                ["hp"] = new Vector2I(190, 210),
                ["speed"] = new Vector2(75.0f, 95.0f),
                ["score"] = 400,
                ["fire"] = 0.5f,
                ["fire_interval"] = 2.2f,
                ["scale"] = 0.9f,
                ["radius"] = 38.0f,
                ["elite"] = true,
                ["bullet_types"] = new Godot.Collections.Array<StringName> { "spread" },
            },
            new() { // 游击
                ["texture"] = GD.Load<Texture2D>("res://assets/sprites/elite_ship_2.png"),
                ["strategies"] = new Godot.Collections.Array<StringName> { "zigzag", "dive", "noise" },
                ["hp"] = new Vector2I(135, 155),
                ["speed"] = new Vector2(195.0f, 245.0f),
                ["score"] = 350,
                ["fire"] = 0.6f,
                ["fire_interval"] = 2.0f,
                ["scale"] = 0.68f,
                ["radius"] = 34.0f,
                ["elite"] = true,
                ["bullet_types"] = new Godot.Collections.Array<StringName> { "laser", "spread" },
            },
            new() { // 炮艇
                ["texture"] = GD.Load<Texture2D>("res://assets/sprites/elite_ship_3.png"),
                ["strategies"] = new Godot.Collections.Array<StringName> { "hover", "spiral" },
                ["hp"] = new Vector2I(170, 190),
                ["speed"] = new Vector2(90.0f, 115.0f),
                ["score"] = 500,
                ["fire"] = 1.0f,
                ["fire_interval"] = 1.5f,
                ["scale"] = 0.78f,
                ["radius"] = 38.0f,
                ["elite"] = true,
                ["bullet_types"] = new Godot.Collections.Array<StringName> { "spread", "laser" },
            },
            new() { // 重装炮台（2026-08-04）：最高 HP 慢速弹幕机
                ["texture"] = GD.Load<Texture2D>("res://assets/sprites/elite_ship_1.png"),
                ["strategies"] = new Godot.Collections.Array<StringName> { "hover", "straight" },
                ["hp"] = new Vector2I(240, 270),
                ["speed"] = new Vector2(55.0f, 75.0f),
                ["score"] = 550,
                ["fire"] = 0.7f,
                ["fire_interval"] = 1.8f,
                ["scale"] = 1.0f,
                ["radius"] = 42.0f,
                ["elite"] = true,
                ["bullet_types"] = new Godot.Collections.Array<StringName> { "spread", "laser" },
            },
        };
    }

    // ---------------- GDScript 鸭子调用兼容桥（M6 过渡，M7 删除） ----------------
    // 调用方：main.gd（set_elite_event/set_formation_event/set_elapsed/elapsed/set_process/clear_pending；
    // boss_spawned/boss_warning 信号改连 PascalCase——主代理接线）、
    // csharp/godot/GameEventManager.cs（is_boss_active/notify_event_triggered）、
    // csharp/godot/EliteTurretEvent.cs（set_boss_frozen/set_waves_paused/consume_boss_pending/trigger_boss）、
    // csharp/godot/FormationStrikeEvent.cs（is_boss_active/elite_event/set_waves_paused）、
    // test/{autoplay,wave_pacing,elite_turret_event,formation_strike_event,difficulty,enemy_combat,smoke,
    // perf_bench,view_zoom,boss_phase_transition,boss_pattern,boss_enrage,hit_logic,entity_manager,fog_event,
    // buff33,buff_effects,graze,mothership_summon,orbital_strike,pool_reuse,entry_animation}_test 等。

    public void set_elite_event(Node? eventNode) => SetEliteEvent(eventNode);

    public void set_formation_event(Node? eventNode) => SetFormationEvent(eventNode);

    public void set_elapsed(float seconds) => SetElapsed(seconds);

    public void spawn_boss(int pType) => SpawnBoss(pType);

    public void spawn_boss() => SpawnBoss();

    public void spawn_enemy() => SpawnEnemy();

    public void spawn_normal_wave() => SpawnNormalWave();

    public int wave_size() => WaveSize();

    public int count_spread_enemies() => CountSpreadEnemies();

    public StringName pick_bullet_type(Godot.Collections.Dictionary config) => PickBulletType(config);

    public float current_interval() => CurrentInterval();

    public void set_boss_timer(float seconds) => SetBossTimer(seconds);

    public void set_next_boss_score(int scoreValue) => SetNextBossScore(scoreValue);

    public void set_wave_timer(float seconds) => SetWaveTimer(seconds);

    public void set_waves_since_special(int count) => SetWavesSinceSpecial(count);

    public void set_boss_active(bool active) => SetBossActive(active);

    public bool boss_frozen() => BossFrozen();

    public bool waves_paused() => WavesPaused();

    public bool boss_pending() => BossPending();

    public void set_boss_pending(bool pending) => SetBossPending(pending);

    public Node? formation_event() => FormationEvent();

    public Vector2 hover_band() => HoverBand();

    public void notify_special_killed() => NotifySpecialKilled();

    public void notify_boss_died() => NotifyBossDied();

    public float boss_timer() => BossTimer();

    public float wave_timer() => WaveTimer();

    public int waves_since_special() => WavesSinceSpecial();

    public float elapsed() => Elapsed();

    public void set_boss_frozen(bool frozen) => SetBossFrozen(frozen);

    public void set_waves_paused(bool paused) => SetWavesPaused(paused);

    public bool is_boss_active() => IsBossActive();

    public Node? elite_event() => EliteEvent();

    public void notify_event_triggered() => NotifyEventTriggered();

    public bool consume_boss_pending() => ConsumeBossPending();

    public void trigger_boss() => TriggerBoss();

    public Godot.Collections.Array<Godot.Collections.Dictionary> unlocked_types() => UnlockedTypes();

    public Enemy spawn_minion(Vector2 pos) => SpawnMinion(pos);

    public void clear_pending() => ClearPending();
}
