using Godot;
using InfiAir.Core.Progression;

namespace InfiAir;

/// <summary>
/// 本局进程服务：难度档位 / 倍率缓存 / DDA 降档 / 进程 ramp / 里程碑曲线求值。
/// Godot 绑定层：DIFFICULTY_DEFS/Difficulty/DifficultyMultiplier/RunTime/MilestoneBase/
/// MilestoneCycleMult 经 GameState.Instance 跨域访问（状态字段本体在本服务，GameState 公开
/// 属性为门面转发）；里程碑阈值/难度乘数曲线直调 InfiAir.Core.Progression 静态纯函数；
/// BalanceService ramp 转发簇经构造注入（_balanceService）。GameState 组合持有本服务并做
/// 门面对齐转发（签名/语义不变），保持唯一 autoload：GameState 约定。信号：本服务以 C# 事件
/// DifficultyChanged/DifficultySelected 通知；GameState 订阅后转发为同名信号（发射点/次数/
/// 顺序恒定不变；AddBossKill 的难度信号由 GameState 侧直发，不经本事件）。
/// </summary>
public sealed partial class RunProgressionService : RefCounted
{

    /// <summary>组合注入：GameState 持有的平衡配置服务（BalanceService ramp 转发簇使用；
    /// GameState.cs 构造器传入）。</summary>
    private readonly BalanceService _balanceService;

    public RunProgressionService(BalanceService balanceService)
    {
        _balanceService = balanceService;
    }

    // ---------------- 难度档位 ----------------

    /// <summary>难度档位（settings.json 持久化，默认 medium；GameState 公开属性转发）。</summary>
    public StringName Difficulty { get; set; } = new StringName("medium");

    /// <summary>难度进程乘数（GameState 公开属性转发）。</summary>
    public double DifficultyMultiplier { get; set; } = 1.0;

    /// <summary>DDA 弹幕密度降档——玩家受击后短暂拉长敌弹/波次间隔
    /// （只拉间隔不降收益，分数公平）；_apply_balance 从 balance.json dda 段缓存（GameState 公开属性转发）。</summary>
    public double DDA_DURATION { get; set; } = 5.0;

    public double DDA_FACTOR { get; set; } = 1.3;

    /// <summary>DDA 降档剩余计时（受击置位，_Process 经 Tick 推进；0 = 未降档）。</summary>
    private double _ddaTimer;

    /// <summary>难度进程曲线参数（_apply_balance 从 balance.json progression 段读取缓存，热路径免查 JSON）</summary>
    private double _progPerBossKill = 0.6;

    private double _progPerTenMinutes = 1.5;

    private double _progTimeStepSeconds = 30.0;

    /// <summary>已计入难度乘数的时间档位（按 time_step_seconds 量化步进，避免连续漂移）</summary>
    private int _difficultyTimeStep;

    /// <summary>难度倍率缓存——Difficulty 是公开属性，直写不经
    /// SetDifficulty（见 ScoreMultiplier 上方缓存失效说明），故按档位惰性刷新（StringName
    /// 相等比较零分配）；DIFFICULTY_DEFS 替换（ReloadBalance→ApplyBalance）经 RefreshRegenCache 失效。</summary>
    private StringName _multCachedDifficulty = new(); // 空 StringName ≠ 任何合法档位 → 首读惰性重算
    private double _enemyHpMult = 1.0;
    private double _enemySpeedMult = 1.0;
    private double _spawnIntervalMult = 1.0;
    private int _scoreMult = 1; // score 倍率并入档位惰性缓存（缓存失效约束见 ScoreMultiplier）

    /// <summary>回血链热路径缓存：regen 档位难度变更时刷新。
    /// 默认值须与脚本默认 difficulty=medium 档一致（medium: regen_delay=4.0, regen_rate=2.0）。</summary>
    private double _regenDelay = 4.0;

    private double _regenRate = 2.0;

    /// <summary>难度乘数变化（Tick 时间档重算跨档）；GameState 订阅后转发为 DifficultyChanged 信号
    /// （AddBossKill 的难度信号由 GameState 侧直发同名信号，不重复）。</summary>
    public event Action<double>? DifficultyChanged;

    /// <summary>难度档位选定（SetDifficulty）；GameState 订阅后转发为 DifficultySelected 信号。</summary>
    public event Action<StringName>? DifficultySelected;

    /// <summary>进程曲线参数注入（ApplyBalance 调用；Cfg 调用留在 GameState 侧）。
    /// 负值会使难度乘数随时间/Boss 击杀下行，钳制 ≥0 保曲线单调不减。</summary>
    public void ApplyProgressionParams(double perBossKill, double perTenMinutes, double timeStepSeconds)
    {
        _progPerBossKill = perBossKill;
        _progPerTenMinutes = perTenMinutes;
        // 本地防线：timeStep ≤0 使本类两处 RunTime/_progTimeStepSeconds 除零；
        // 上游注入点（GameState.State.cs）已钳 0.1，此处不依赖跨层契约
        _progTimeStepSeconds = Math.Max(timeStepSeconds, 0.1);
    }

    // ---------------- 难度档位 ----------------

    /// <summary>切换难度档位（非法档位忽略），持久化到 settings.json 并广播</summary>
    public void SetDifficulty(StringName pDifficulty)
    {
        if (!GameState.Instance.DIFFICULTY_DEFS.ContainsKey(pDifficulty) || pDifficulty == Difficulty)
        {
            return;
        }

        Difficulty = pDifficulty;
        RefreshRegenCache();
        DifficultySelected?.Invoke(Difficulty);
        GameState.Instance.SaveSettings();
    }

    public string DifficultyLabel() => (string)GameState.Instance.Tr("DIFF_" + Difficulty.ToString().ToUpperInvariant());

    /// <summary>受击触发 DDA 降档（重入安全——幂等置位，重复受击刷新计时）。
    /// 同源断连（受击 = 降档 + 断连双通道，均不致命）。</summary>
    public void OnPlayerDamagedDda(float amount, Vector2 fromPos)
    {
        _ddaTimer = DDA_DURATION;
        GameState.Instance.ResetCombo();
    }

    public int ScoreMultiplier()
    {
        // 缓存必须按 Difficulty 失效：difficulty 是公开字段，直写不触发 RefreshRegenCache，
        // 否则缓存返回旧值——档位惰性缓存经 StringName 比较检测该变更（与 enemy_hp/speed/spawn
        // 三倍率同款）
        if (Difficulty != _multCachedDifficulty)
        {
            RefreshDifficultyMultCache();
        }

        return _scoreMult;
    }

    /// <summary>DDA 降档中（玩家受击后 DDA_DURATION 内）——消费方
    /// （enemy 开火计时 / spawner 波次间隔 / boss 攻击间隔）乘 dda_factor() 拉长间隔</summary>
    public bool DdaActive() => _ddaTimer > 0.0;

    /// <summary>DDA 降档乘区：active 时返回配置因子（>1 拉长间隔），否则 1.0（热路径零分支常态）</summary>
    public double DdaFactor() => _ddaTimer > 0.0 ? DDA_FACTOR : 1.0;

    /// <summary>DDA 计时剩余（读档写出用）。</summary>
    public double DdaRemaining() => _ddaTimer;

    /// <summary>当前难度时间档（读档写出用；本可由 RunTime 推导，存下以免曲线参数变化后档位漂移）。</summary>
    public int DifficultyTimeStep() => _difficultyTimeStep;

    public double EnemyHpMultiplier()
    {
        if (Difficulty != _multCachedDifficulty)
        {
            RefreshDifficultyMultCache();
        }

        return _enemyHpMult;
    }

    public double EnemySpeedMultiplier()
    {
        if (Difficulty != _multCachedDifficulty)
        {
            RefreshDifficultyMultCache();
        }

        return _enemySpeedMult;
    }

    private void RefreshDifficultyMultCache()
    {
        var def = GameState.Instance.DIFFICULTY_DEFS[Difficulty].AsGodotDictionary();
        _enemyHpMult = def["hp"].AsDouble();
        _enemySpeedMult = def["speed"].AsDouble();
        _spawnIntervalMult = def["spawn"].AsDouble();
        // 钳入 [0, int.MaxValue]——手改 balance.json 倍率超大值时
        // 裸 (int) 截断回绕为负（负倍率 → 加分变扣分）
        _scoreMult = (int)Math.Clamp(def["score"].AsInt64(), 0L, (long)int.MaxValue);
        _multCachedDifficulty = Difficulty;
    }

    /// <summary>敌方 HP 本局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))，随 Boss 击杀线性成长。
    /// 纯查询委托 BalanceService（难度乘数作参数）。</summary>
    public float EnemyHpRamp() => (float)_balanceService.EnemyHpRamp(GameState.Instance.DifficultyMultiplier);

    /// <summary>敌方 HP ramp（显式难度乘数版本）：调用方以自身难度快照计算——
    /// Enemy.Setup 的 pDifficulty 参数是显式入参（分裂子机可传非全局 DifficultyMultiplier 值），
    /// 与直查 Cfg 语义相同，但走 Load 时缓存的 ramp 因子（免每敌机 path.Split + Variant 装箱）。</summary>
    public float EnemyHpRamp(double difficultyMultiplier) => (float)_balanceService.EnemyHpRamp(difficultyMultiplier);

    /// <summary>敌方伤害本局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))，
    /// 统一作用于全部敌方伤害源（敌弹/Boss 弹/撞体/编队炸弹）。
    /// 纯查询委托 BalanceService（难度乘数作参数）。</summary>
    public float EnemyDamageRamp() => (float)_balanceService.EnemyDamageRamp(GameState.Instance.DifficultyMultiplier);

    public double SpawnIntervalMultiplier()
    {
        if (Difficulty != _multCachedDifficulty)
        {
            RefreshDifficultyMultCache();
        }

        return _spawnIntervalMult;
    }

    /// <summary>spread 弹种敌机同屏上限（easy 1 / medium 2 / hard 3）。
    /// 钳 [0, int.MaxValue]（同文件 ScoreMultiplier 已钳）——手改 &gt;2^31
    /// 经裸 (int) 回绕负 → spread 敌机同屏上限恒负、整类玩法消失。</summary>
    public int SpreadEnemyCap() => (int)Math.Clamp(
        GameState.Instance.DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["spread_cap"].AsInt64(), 0L, (long)int.MaxValue);

    /// <summary>被动回血：距上次受伤 regen_delay 秒起每秒回 regen_rate HP（对齐原作 HEALTH_REGEN）
    /// 档位值在难度变更/重新加载时缓存，热路径免双层字典查找</summary>
    public double PassiveRegenDelay() => _regenDelay;

    public double PassiveRegenRate() => _regenRate;

    /// <summary>回血链/倍率缓存刷新（SetDifficulty/ApplyBalance/ApplySettingsDict 调用；GameState
    /// 侧私有包装）。DIFFICULTY_DEFS 可能被 ApplyBalance 整表替换（ReloadBalance）——倍率缓存
    /// 失效，下次读取惰性重算。</summary>
    public void RefreshRegenCache()
    {
        var def = GameState.Instance.DIFFICULTY_DEFS.GetValueOrDefault(Difficulty, new Variant());
        if (def.VariantType == Variant.Type.Dictionary)
        {
            _regenDelay = (double)def.AsGodotDictionary().GetValueOrDefault("regen_delay", _regenDelay).AsDouble();
            _regenRate = (double)def.AsGodotDictionary().GetValueOrDefault("regen_rate", _regenRate).AsDouble();
        }

        _multCachedDifficulty = new StringName();
    }

    // ---------------- BalanceService Load 缓存转发（避免每 spawn Cfg 全链路） ----------------

    /// <summary>敌方速度 ramp（显式难度乘数版本）：Enemy.Setup 以自身难度快照计算（EnemyHpRamp 同款模式）。</summary>
    public float EnemySpeedRamp(double difficultyMultiplier) => (float)_balanceService.EnemySpeedRamp(difficultyMultiplier);

    /// <summary>敌机移动策略参数表（Load 缓存引用，只读消费；Enemy.MakeStrategy 每 spawn 读取）。</summary>
    public Godot.Collections.Dictionary MoveStrategies() => _balanceService.MoveStrategies();

    /// <summary>辅助瞄准「强辅助」标记概率（Load 缓存；Enemy.Setup 每 spawn 读取）。</summary>
    public double AimMarkRatio() => _balanceService.AimMarkRatio();

    /// <summary>敌机入场预告时长（Load 缓存，判型/钳制已完成；Spawner.QueueEnemy 每 spawn 读取）。</summary>
    public float SpawnerTelegraphDuration() => _balanceService.SpawnerTelegraphDuration();

    // ---------------- 里程碑阈值曲线 ----------------

    /// <summary>第 index 次（0 起）里程碑的分数阈值：8 档基础阈值循环，档差按 ×1.35^cycle 增长，
    /// 再乘难度阈值倍率（easy ×1 / medium ×1 / hard ×1.5）。
    /// 算法核心在 InfiAir.Core.Progression.MilestoneCurve（C# 纯函数，零 Godot 依赖）：
    /// pow 钳制、roundf half-away-from-zero、累加顺序与曲线定义一致。</summary>
    public int MilestoneThreshold(int index) => (int)MilestoneCurve.Threshold(
        index, ToLongArray(GameState.Instance.MilestoneBase), GameState.Instance.MilestoneCycleMult, MilestoneMult());

    /// <summary>milestone_base 为 GameState 校验后的非负 int 数组（ScoreService.BuildMilestoneBase /
    /// ApplyBalance 元素级判型后才注入），此处直接转换。</summary>
    private static long[] ToLongArray(Godot.Collections.Array<int> values)
    {
        var result = new long[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            result[i] = values[i];
        }

        return result;
    }

    /// <summary>难度档阈值倍率（DIFFICULTY_DEFS 经 _valid_difficulty_defs 校验，milestone 恒为正数）</summary>
    public double MilestoneMult() => (double)GameState.Instance.DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["milestone"].AsDouble();

    /// <summary>难度乘数本局进程曲线（D1=必死曲线）：
    /// 1 + per_boss_kill×Boss击杀 + 时间轴累进（每 time_step_seconds 量化一档，每 10 分钟 +per_ten_minutes）。
    /// 线性无封顶：敌方 HP/伤害 ramp 随之无限增长，最终超过玩家固定成长上限。
    /// 返回乘数是否变化；变化时由调用方广播 difficulty_changed（AddBossKill 结算末尾统一广播）。</summary>
    public bool RecomputeDifficultyInternal()
    {
        // (int)Mathf.Floor(x) 简化为 (int)x：RunTime 恒 ≥ 0
        // （初值 0、仅 _Process += delta、重置为 0；公开属性直写约定为非负），
        // 对非负数截断与 floor 等价，省一次原生调用
        var step = (int)(GameState.Instance.RunTime / _progTimeStepSeconds);
        // 曲线公式在 InfiAir.Core.Progression.DifficultyCurve（C#，运算顺序逐位等价）
        var newMult = DifficultyCurve.Compute(
            GameState.Instance.RunTime, _progTimeStepSeconds, _progPerTenMinutes, _progPerBossKill, GameState.Instance.BossKills);
        _difficultyTimeStep = step;
        if (Mathf.IsEqualApprox(newMult, DifficultyMultiplier))
        {
            return false;
        }

        DifficultyMultiplier = newMult;
        return true;
    }

    public void RecomputeDifficulty() => RecomputeDifficultyInternal();

    /// <summary>难度时间档重算 + DDA 计时（_Process 经 GameState 调用）：跨过量化步进边界时重算
    /// 难度乘数（去硬顶曲线的时间分量）；DDA 降档计时（受击触发；暂停时 process 冻结，与本局节奏一致）。</summary>
    public void Tick(double delta)
    {
        // 时间轴难度档：跨过量化步进边界时重算难度乘数（去硬顶曲线的时间分量）
        // (int)Mathf.Floor(x) 简化为 (int)x：RunTime ≥ 0 时截断等价 floor（省原生调用）
        if ((int)(GameState.Instance.RunTime / _progTimeStepSeconds) != _difficultyTimeStep)
        {
            if (RecomputeDifficultyInternal())
            {
                DifficultyChanged?.Invoke(DifficultyMultiplier);
            }
        }

        // DDA 降档计时（受击触发；暂停时 process 冻结，与本局节奏一致）
        if (_ddaTimer > 0.0)
        {
            _ddaTimer -= delta;
        }
    }

    /// <summary>难度域复位（ResetRun 调用；DifficultyMultiplier/时间档/DDA 计时归零，
    /// 本方法无信号发射——信号顺序由 ResetRun 保持）。</summary>
    public void ResetAll()
    {
        DifficultyMultiplier = 1.0;
        _difficultyTimeStep = 0;
        _ddaTimer = 0.0; // DDA 计时必须复位——否则旧局受击降档渗透新局
    }

    /// <summary>读档还原（本局存档）：难度乘数/时间档/DDA 计时覆盖；倍率缓存随之刷新
    /// （DDA 计时不还原剩余时长——读档从新一波开始，降档仅作参考量不持久化语义）。</summary>
    public void RestoreRunState(double difficultyMultiplier, int difficultyTimeStep, double ddaTimer)
    {
        DifficultyMultiplier = Math.Max(difficultyMultiplier, 1.0);
        _difficultyTimeStep = Math.Max(difficultyTimeStep, 0);
        _ddaTimer = Math.Max(ddaTimer, 0.0);
        RefreshRegenCache();
    }
}
