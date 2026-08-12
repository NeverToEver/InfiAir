using Godot;

namespace InfiAir;

/// <summary>
/// 对局进程服务（第五轮拆域，2026-08-11）：原 GameState.Difficulty.cs 全部职责——难度档位 /
/// 倍率缓存 / DDA 降档 / 进程 ramp / 里程碑曲线求值迁入本服务。
/// Godot 绑定层：DIFFICULTY_DEFS/Difficulty/DifficultyMultiplier/RunTime/MilestoneBase/
/// MilestoneCycleMult 经 GameState.Instance 跨域访问（状态字段本体迁入本服务，GameState 公开
/// 属性为门面转发）；_progression 经 GameState.Progression 转发；BalanceService ramp 转发簇经
/// 构造注入（_balanceService，与 MetaService 构造注入 UserDB 同构）。门面转发先例：与
/// MetaService/MissionsService 同构——GameState 组合持有本服务，GameState.Difficulty.cs 为
/// 门面对齐转发（签名/语义不变），保持唯一 autoload：GameState 约定。信号：本服务以 C# 事件
/// DifficultyChanged/DifficultySelected 通知；GameState 订阅后转发为同名信号（发射点/次数/
/// 顺序与拆域前逐位一致；AddBossKill/ApplyRunSave 的难度信号由 GameState 侧直发，不经本事件）。
/// </summary>
public sealed partial class RunProgressionService : RefCounted
{

    /// <summary>组合注入：GameState 持有的平衡配置服务（BalanceService ramp 转发簇使用；
    /// GameState.cs 构造器传入，与 MetaService 构造注入 UserDB 同构）。</summary>
    private readonly BalanceService _balanceService;

    public RunProgressionService(BalanceService balanceService)
    {
        _balanceService = balanceService;
    }

    // ---------------- 难度档位（2026-08-11 自 GameState.Difficulty.cs/State.cs 迁入） ----------------

    /// <summary>难度档位（profile 持久化，默认 medium；GameState 公开属性转发）。</summary>
    public StringName Difficulty { get; set; } = new StringName("medium");

    /// <summary>难度进程乘数（GameState 公开属性转发）。</summary>
    public double DifficultyMultiplier { get; set; } = 1.0;

    /// <summary>B 梯队（fair plan §8）：DDA 弹幕密度降档——玩家受击后短暂拉长敌弹/波次间隔
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

    /// <summary>M6（2026-08-10 审计）：难度倍率缓存——Difficulty 是公开属性，测试/调用方直写不经
    /// SetDifficulty（见 ScoreMultiplier 上方 2026-08-03 回退注释），故按档位惰性刷新（StringName
    /// 相等比较零分配）；DIFFICULTY_DEFS 替换（ReloadBalance→ApplyBalance）经 RefreshRegenCache 失效。</summary>
    private StringName _multCachedDifficulty = new(); // 空 StringName ≠ 任何合法档位 → 首读惰性重算
    private double _enemyHpMult = 1.0;
    private double _enemySpeedMult = 1.0;
    private double _spawnIntervalMult = 1.0;

    /// <summary>回血链热路径缓存（P0-2）：regen 档位难度变更时刷新。
    /// 默认值须与脚本默认 difficulty=medium 档一致（medium: regen_delay=4.0, regen_rate=2.0）。</summary>
    private double _regenDelay = 4.0;

    private double _regenRate = 2.0;

    /// <summary>难度乘数变化（Tick 时间档重算跨档）；GameState 订阅后转发为 DifficultyChanged 信号
    /// （AddBossKill/ApplyRunSave 的难度信号由 GameState 侧直发同名信号，不重复）。</summary>
    public event Action<double>? DifficultyChanged;

    /// <summary>难度档位选定（SetDifficulty）；GameState 订阅后转发为 DifficultySelected 信号。</summary>
    public event Action<StringName>? DifficultySelected;

    /// <summary>进程曲线参数注入（ApplyBalance 调用；Cfg 调用留在 GameState 侧）。
    /// 负值会使难度乘数随时间/Boss 击杀下行，钳制 ≥0 保曲线单调不减（注释随迁）。</summary>
    public void ApplyProgressionParams(double perBossKill, double perTenMinutes, double timeStepSeconds)
    {
        _progPerBossKill = perBossKill;
        _progPerTenMinutes = perTenMinutes;
        _progTimeStepSeconds = timeStepSeconds;
    }

    // ---------------- 难度档位（2026-08-11 自 GameState.Difficulty.cs 迁入） ----------------

    /// <summary>切换难度档位（非法档位忽略），持久化到 profile 并广播</summary>
    public void SetDifficulty(StringName pDifficulty)
    {
        if (!GameState.Instance.DIFFICULTY_DEFS.ContainsKey(pDifficulty) || pDifficulty == Difficulty)
        {
            return;
        }

        Difficulty = pDifficulty;
        RefreshRegenCache();
        DifficultySelected?.Invoke(Difficulty);
        GameState.Instance.SaveProfile();
    }

    public string DifficultyLabel() => (string)GameState.Instance.Tr("DIFF_" + Difficulty.ToString().ToUpperInvariant());

    /// <summary>B 梯队：受击触发 DDA 降档（重入安全——幂等置位，重复受击刷新计时）；
    /// 2026-08-11：同源断连（受击 = 降档 + 断连双通道，均不致命）。</summary>
    public void OnPlayerDamagedDda(float amount, Vector2 fromPos)
    {
        _ddaTimer = DDA_DURATION;
        GameState.Instance.ResetCombo();
    }

    public int ScoreMultiplier()
    {
        // 2026-08-03 审计回退：曾尝试缓存 _score_multiplier_cache，但 difficulty 是公开字段，
        // 测试/调用方直写不触发 _refresh_regen_cache（白盒契约），缓存会返回旧值——本方法保持直接查表
        // （M6 后 enemy_hp/speed/spawn 三倍率改档位惰性缓存，直写经 StringName 比较失效检测，见下）
        // 2026-08-10 健壮性审查：钳入 [0, int.MaxValue]——手改 balance.json 倍率超大值时
        // 裸 (int) 截断回绕为负（负倍率 → 加分变扣分）
        return (int)Math.Clamp(
            GameState.Instance.DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["score"].AsInt64(), 0L, (long)int.MaxValue);
    }

    /// <summary>B 梯队（fair plan §8）：DDA 降档中（玩家受击后 DDA_DURATION 内）——消费方
    /// （enemy 开火计时 / spawner 波次间隔 / boss 攻击间隔）乘 dda_factor() 拉长间隔</summary>
    public bool DdaActive() => _ddaTimer > 0.0;

    /// <summary>DDA 降档乘区：active 时返回配置因子（>1 拉长间隔），否则 1.0（热路径零分支常态）</summary>
    public double DdaFactor() => _ddaTimer > 0.0 ? DDA_FACTOR : 1.0;

    /// <summary>测试/诊断：立即结束降档（对齐「测试经公开接口」白盒契约）</summary>
    public void ResetDda() => _ddaTimer = 0.0;

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
        _multCachedDifficulty = Difficulty;
    }

    /// <summary>敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))，随 Boss 击杀线性成长。
    /// 纯查询委托 BalanceService（难度乘数作参数）。</summary>
    public float EnemyHpRamp() => (float)_balanceService.EnemyHpRamp(GameState.Instance.DifficultyMultiplier);

    /// <summary>敌方 HP ramp（显式难度乘数版本，2026-08-09 审计补充）：调用方以自身难度快照计算——
    /// Enemy.Setup 的 pDifficulty 参数是显式入参（分裂子机/测试可传非全局 DifficultyMultiplier 值），
    /// 语义同原直查 Cfg 全链路，但走 Load 时缓存的 ramp 因子（免每敌机 path.Split + Variant 装箱）。</summary>
    public float EnemyHpRamp(double difficultyMultiplier) => (float)_balanceService.EnemyHpRamp(difficultyMultiplier);

    /// <summary>敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))，
    /// 统一作用于全部敌方伤害源（敌弹/Boss 弹/撞体/编队炸弹；2026-07-29 无限段修订）。
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
    /// AB15：钳 [0, int.MaxValue]（同文件 ScoreMultiplier 已钳，孪生遗漏）——手改 &gt;2^31
    /// 经裸 (int) 回绕负 → spread 敌机同屏上限恒负、整类玩法消失。</summary>
    public int SpreadEnemyCap() => (int)Math.Clamp(
        GameState.Instance.DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["spread_cap"].AsInt64(), 0L, (long)int.MaxValue);

    /// <summary>被动回血：距上次受伤 regen_delay 秒起每秒回 regen_rate HP（对齐原作 HEALTH_REGEN）
    /// P0-2：档位值在难度变更/重新加载时缓存，热路径免双层字典查找</summary>
    public double PassiveRegenDelay() => _regenDelay;

    public double PassiveRegenRate() => _regenRate;

    /// <summary>回血链/倍率缓存刷新（SetDifficulty/ApplyBalance/ApplySettingsDict 调用；GameState
    /// 侧私有包装）。M6：DIFFICULTY_DEFS 可能被 ApplyBalance 整表替换（ReloadBalance）——倍率缓存
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

    // ---------------- BalanceService Load 缓存转发（2026-08-10 perf 批次；原每 spawn Cfg 全链路） ----------------

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
    /// 2026-08-07：算法核心迁移 InfiAir.Core.Progression.MilestoneCurve（C# 纯函数，xUnit 直测；
    /// 逐位等价：pow 钳制、roundf half-away-from-zero、累加顺序一致）。</summary>
    public int MilestoneThreshold(int index) => (int)GameState.Instance.Progression.MilestoneThreshold(
        index, Variant.From(GameState.Instance.MilestoneBase).AsGodotArray(), GameState.Instance.MilestoneCycleMult, MilestoneMult());

    /// <summary>难度档阈值倍率（DIFFICULTY_DEFS 经 _valid_difficulty_defs 校验，milestone 恒为正数；
    /// ScoreService.RestoreMilestones 经 GameState 私有包装调用）</summary>
    public double MilestoneMult() => (double)GameState.Instance.DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["milestone"].AsDouble();

    /// <summary>难度乘数对局进程曲线（2026-07-29 无限段修订，D1=必死曲线，docs/archive/ENDLESS_BALANCE_PLAN.md）：
    /// 1 + per_boss_kill×Boss击杀 + 时间轴累进（每 time_step_seconds 量化一档，每 10 分钟 +per_ten_minutes）。
    /// 线性无封顶：敌方 HP/伤害 ramp 随之无限增长，最终超过玩家固定成长上限。
    /// 返回乘数是否变化；变化时由调用方广播 difficulty_changed（apply_run_save 统一在末尾广播）。</summary>
    public bool RecomputeDifficultyInternal()
    {
        // (int)Mathf.Floor(x) 简化为 (int)x（opt-hotpath 合并，2026-08-11）：RunTime 恒 ≥ 0
        // （初值 0、仅 _Process += delta、存档读入 Clamp [0,1e6]、重置为 0；测试直写非负），
        // 对非负数截断与 floor 等价，省一次原生调用
        var step = (int)(GameState.Instance.RunTime / _progTimeStepSeconds);
        // 2026-08-07：曲线公式迁移 InfiAir.Core.Progression.DifficultyCurve（C#，运算顺序逐位等价）
        var newMult = GameState.Instance.Progression.DifficultyMultiplier(
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
    /// 难度乘数（去硬顶曲线的时间分量）；DDA 降档计时（受击触发；暂停时 process 冻结，与对局节奏一致）。</summary>
    public void Tick(double delta)
    {
        // 时间轴难度档：跨过量化步进边界时重算难度乘数（去硬顶曲线的时间分量）
        // (int)Mathf.Floor(x) 简化为 (int)x（opt-hotpath 合并）：RunTime ≥ 0 时截断等价 floor（省原生调用）
        if ((int)(GameState.Instance.RunTime / _progTimeStepSeconds) != _difficultyTimeStep)
        {
            if (RecomputeDifficultyInternal())
            {
                DifficultyChanged?.Invoke(DifficultyMultiplier);
            }
        }

        // DDA 降档计时（受击触发；暂停时 process 冻结，与对局节奏一致）
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
        _ddaTimer = 0.0; // A 审计：DDA 计时跨对局残留——旧局受击降档渗透新局
    }
}
