using Godot;
using InfiAir.Core.Progression;

namespace InfiAir;

/// <summary>
/// 计分域服务：Score/Kills/BossKills/Combo 状态、连击系统与里程碑推进。
/// Godot 绑定层：里程碑曲线/连击配置经 GameState.Instance 跨域访问（ScoreMultiplier 经
/// RunProgression 门面、MilestoneMult 经 GameState 私有包装、MilestoneThreshold 经
/// GameState → RunProgressionService 直调 InfiAir.Core.Progression 纯函数）。
/// GameState 组合持有本服务并做门面对齐转发（签名/语义不变），保持唯一 autoload：GameState 约定。
/// 信号：本服务以 C# 事件 ScoreChanged/MilestoneReached/ComboChanged 通知；GameState 订阅后
/// 转发为同名信号（发射点/次数/顺序恒定不变）。
/// </summary>
public sealed partial class ScoreService : RefCounted
{

    // ---------------- 计分域 ----------------

    /// <summary>得分（本局会话态）。</summary>
    public int Score { get; set; }

    public int Kills { get; set; }

    public int BossKills { get; set; }

    /// <summary>当前连击数（0 = 已断连；断连/受击/重开归零）。</summary>
    public int Combo { get; private set; }

    /// <summary>连击窗口剩余计时（_Process 经 Tick 推进；超时断连）。</summary>
    private double _comboTimer;

    private int _nextMilestone = 3000; // = MilestoneBase[0]

    private int _milestoneCount;

    /// <summary>生效的里程碑表（默认值见 BuildMilestoneBase()，可被 balance.json 覆盖；ApplyBalance 注入）。</summary>
    public Godot.Collections.Array<int> MilestoneBase { get; set; } = BuildMilestoneBase();

    public double MilestoneCycleMult { get; set; } = MilestoneCycleMultValue;

    /// <summary>得分总量上限（防手改 difficulty score 倍率导致 int64 溢出；正常本局远达不到）</summary>
    private const int ScoreCapValue = 1_000_000_000;

    private const double MilestoneCycleMultValue = 1.35;

    /// <summary>里程碑阈值曲线（对齐原作 constants.py GameBalanceConstants 算法）：
    /// 首循环 8 档基础阈值，之后每循环的档差按 ×1.35^cycle 放大（阈值单调不回退）。
    /// GameState.ApplyBalance 的 Cfg 默认值与整表回退共用。</summary>
    public static Godot.Collections.Array<int> BuildMilestoneBase() => new()
    {
        3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000,
    };

    /// <summary>击杀连击：
    /// 窗口内连杀放大击杀分——怒首领蜂/虫姬链式得分的温和版（贪分 vs 稳）。</summary>
    public double ComboWindow { get; private set; } = 3.0;

    /// <summary>score_amp 增幅：击杀分乘区底数（combo_guard 同款：pow(factor, 有效层级)，1.0 = 未购）。</summary>
    private double _scoreAmpFactor = 1.0;

    /// <summary>combo_guard 增幅：连击窗口延长底数（pow(factor, 有效层级)）。</summary>
    private double _comboWindowFactor = 1.0;

    private static readonly StringName AugScoreAmpId = new("score_amp");
    private static readonly StringName AugComboGuardId = new("combo_guard");

    public double ComboStep { get; private set; } = 0.1;

    public double ComboMaxMult { get; private set; } = 2.0;

    /// <summary>得分变化（AddScore）；GameState 订阅后转发为 ScoreChanged 信号。</summary>
    public event Action<int>? ScoreChanged;

    /// <summary>里程碑触达（AddScore 逐档推进）；GameState 订阅后转发为 MilestoneReached 信号。</summary>
    public event Action<int>? MilestoneReached;

    /// <summary>连击变化（AddKillScore 推进/ResetCombo 断连）；GameState 订阅后转发为 ComboChanged 信号。</summary>
    public event Action<int>? ComboChanged;

    /// <summary>连击配置注入（ApplyBalance 调用；Cfg 调用留在 GameState 侧，钳制注释随迁）。
    /// window ≤0 会每帧断连——钳制下限，step ≤0 乘区不增、max_mult &lt;1 会倒扣击杀分——钳制 ≥1；
    /// step/max_mult 上界钳 [0,1e3]/[1,1e3]——否则巨值乘区在
    /// AddKillScore 的 (long) 乘算下溢出回绕为负（分数巨负进里程碑/榜单）；1e3 远超合理域
    /// （设计封顶 ×2.0）但杜绝 long 溢出。</summary>
    public void ApplyComboConfig(double window, double step, double maxMult)
    {
        ComboWindow = window;
        ComboStep = step;
        ComboMaxMult = maxMult;
    }

    /// <summary>增幅配置注入（ApplyBalance 调用；Cfg 调用留在 GameState 侧）。
    /// 底数 ≤1 视为未购档（pow 语义下 ×1 不放大），钳制下限防倒扣。</summary>
    public void ApplyAugmentScoreConfig(double scoreAmpFactor, double comboWindowFactor)
    {
        _scoreAmpFactor = Math.Max(scoreAmpFactor, 1.0);
        _comboWindowFactor = Math.Max(comboWindowFactor, 1.0);
    }

    /// <summary>combo_guard 后的生效连击窗口（每杀刷新时求值；击杀频率下开销可忽略）。</summary>
    public double EffectiveComboWindow() =>
        ComboWindow * Math.Pow(_comboWindowFactor, GameState.Instance.TalentEffLevel(AugComboGuardId));

    public void AddScore(int points)
    {
        // 难度分数倍率统一在此乘算（easy ×1 / medium ×2 / hard ×3，配置表里的分值不变）
        // 得分总量钳制——手改配置 score 倍率极大时 int64 溢出（1e308 级）；
        // 乘算必须在 long 域：int × int 在 points×倍率超 2^31 时
        // 先回绕为负再进 Min（负分进入里程碑/榜单），long 域乘算后与上限钳制才生效
        Score = (int)Math.Min((long)Score + (long)points * GameState.Instance.ScoreMultiplier(), (long)ScoreCapValue);
        ScoreChanged?.Invoke(Score);
        // 里程碑推进必须 while 逐档——单次 +1 在单次加分跨多档时漏档
        // （如 hard 倍率下高分击杀/Boss 奖励一次跨两档阈值）；
        // milestone_reached 按触发的档位逐档发，消费方按里程碑数计档。
        // 阈值求值在 C#（milestone_threshold 转发）；此处保持基于
        // _next_milestone 的 while 逐档推进（阈值可随难度倍率脱离基础曲线），
        // 加分逐档仅 1-2 档，单值调用开销可忽略。
        // 兜底挂死守卫：迭代上限沿用 MilestoneCurve.MaxIterations——
        // cycle_mult 已钳 ≥1.0 后曲线单调，但阈值求值 int 溢出回绕为负时 while 永不退出，超限直接 break
        int iterations = 0;
        while (Score >= _nextMilestone)
        {
            _milestoneCount += 1;
            _nextMilestone = GameState.Instance.MilestoneThreshold(_milestoneCount);
            MilestoneReached?.Invoke(Score);
            iterations += 1;
            if (iterations >= MilestoneCurve.MaxIterations)
            {
                break;
            }
        }
    }

    // ---------------- 击杀连击（scoring.combo 段） ----------------

    /// <summary>击杀计分唯一入口（敌机击杀路径统一走此）：连击推进 + 乘区放大，
    /// 随后经 AddScore 乘难度倍率。Boss 击杀（AddBossKill）/事件奖励/擦弹不计连击。</summary>
    public void AddKillScore(int basePoints)
    {
        Combo += 1;
        _comboTimer = EffectiveComboWindow();
        // long 域乘算防回绕（乘区 double，截断前钳制 int 域；AddScore 内另有总分钳制）
        // 双保险：乘积钳 [0, long.MaxValue]——组合极端路径
        // （basePoints×乘区越界 → double→long 转换未定义/回绕巨负）下兜底防负分入账
        var amplified = basePoints * Math.Pow(_scoreAmpFactor, GameState.Instance.TalentEffLevel(AugScoreAmpId));
        var scaled = (long)Math.Round(Math.Clamp(amplified * ComboMultiplier(), 0.0, (double)long.MaxValue));
        AddScore((int)Math.Min(scaled, (long)int.MaxValue));
        ComboChanged?.Invoke(Combo);
    }

    /// <summary>Boss 击杀计分（AddBossKill 编排内的计分域部分）：BossKills 推进 + 加分
    /// （加分基准入 balance.json milestones.boss_kill_base；击杀低频，非热路径可直查）。</summary>
    public void AddBossKill(double scoreScale)
    {
        BossKills += 1;
        AddScore((int)(GameState.Instance.Cfg("milestones.boss_kill_base", 500.0).AsDouble() * scoreScale));
    }

    /// <summary>连击乘区：min(1 + (combo−1)×step, max_mult)；combo 0/1 → 1.0（第 1 杀不放大）。</summary>
    public double ComboMultiplier()
    {
        if (Combo <= 1)
        {
            return 1.0;
        }

        return Math.Min(1.0 + (Combo - 1) * ComboStep, ComboMaxMult);
    }

    /// <summary>断连（受击/重开）：连击归零 + 计时清空 + 广播 HUD。幂等。</summary>
    public void ResetCombo()
    {
        if (Combo == 0 && _comboTimer <= 0.0)
        {
            return;
        }

        Combo = 0;
        _comboTimer = 0.0;
        ComboChanged?.Invoke(0);
    }

    /// <summary>里程碑初始化（_Ready 与 ResetRun 共用）：计数归零 + 下一档阈值重算。</summary>
    public void InitMilestones()
    {
        _milestoneCount = 0;
        _nextMilestone = GameState.Instance.MilestoneThreshold(0);
    }

    /// <summary>连击窗口计时（_Process 经 GameState 调用）：窗口内无新击杀 → 超时断连
    /// （暂停时 process 冻结，与本局节奏一致）。</summary>
    public void Tick(double delta)
    {
        // 连击窗口计时：窗口内无新击杀 → 超时断连（暂停时冻结，与本局节奏一致）
        if (_comboTimer > 0.0)
        {
            _comboTimer -= delta;
            if (_comboTimer <= 0.0)
            {
                ResetCombo();
            }
        }
    }

    /// <summary>计分域复位（ResetRun 调用；信号发射点/顺序恒定——ComboChanged 经 ResetCombo）。</summary>
    public void ResetAll()
    {
        Score = 0;
        Kills = 0;
        BossKills = 0;
        InitMilestones();
        ResetCombo(); // 连击跨对局清零（幂等 + 广播 HUD）
    }

    // ---------------- 里程碑曲线 ----------------

    /// <summary>当前已触发的里程碑数（Mothership.Tier 升级档位等消费点）。</summary>
    public int MilestoneCount() => _milestoneCount;

    /// <summary>读档还原（本局存档）：写回汇总计数 + 里程碑档位，并以还原后的档位重算下一档阈值
    /// （_nextMilestone 无需持久化——它是档位的纯函数）。连击窗口计时不还原（读档从新一波开始）；
    /// 末尾补发 ScoreChanged 驱动 HUD 刷新。</summary>
    public void RestoreRunState(int score, int kills, int bossKills, int combo, int milestoneCount)
    {
        Score = Math.Clamp(score, 0, ScoreCapValue);
        Kills = Math.Max(kills, 0);
        BossKills = Math.Max(bossKills, 0);
        Combo = Math.Max(combo, 0);
        _comboTimer = 0.0;
        _milestoneCount = Math.Max(milestoneCount, 0);
        _nextMilestone = GameState.Instance.MilestoneThreshold(_milestoneCount);
        ScoreChanged?.Invoke(Score);
        ComboChanged?.Invoke(Combo);
    }
}
