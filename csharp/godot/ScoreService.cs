using Godot;
using InfiAir.Core.Progression;

namespace InfiAir;

/// <summary>
/// 计分域服务（第五轮拆域，2026-08-11）：原 GameState.State.cs 计分簇——Score/Kills/BossKills/
/// Combo 状态、连击系统与里程碑推进全部职责迁入本服务。
/// Godot 绑定层：里程碑曲线/连击配置经 GameState.Instance 跨域访问（ScoreMultiplier 经
/// RunProgression 门面、MilestoneMult 经 GameState 私有包装、_progression 经
/// GameState.Progression 转发）。门面转发先例：与 MetaService/MissionsService 同构——
/// GameState 组合持有本服务，GameState.State.cs 为门面对齐转发（签名/语义不变），保持
/// 唯一 autoload：GameState 约定。信号：本服务以 C# 事件 ScoreChanged/MilestoneReached/
/// ComboChanged 通知；GameState 订阅后转发为同名信号（发射点/次数/顺序与拆域前逐位一致）。
/// </summary>
public sealed partial class ScoreService : RefCounted
{

    // ---------------- 计分域（2026-08-11 自 GameState.State.cs 迁入） ----------------

    /// <summary>得分（对局会话态）。</summary>
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

    /// <summary>得分总量上限（P4 防御：手改 difficulty score 倍率防 int64 溢出；正常对局远达不到）</summary>
    private const int ScoreCapValue = 1_000_000_000;

    private const double MilestoneCycleMultValue = 1.35;

    /// <summary>里程碑阈值曲线（对齐原作 constants.py GameBalanceConstants 算法）：
    /// 首循环 8 档基础阈值，之后每循环的档差按 ×1.35^cycle 放大（阈值单调不回退）。
    /// GameState.ApplyBalance 的 Cfg 默认值与整表回退共用（2026-08-11 迁入本服务）。</summary>
    public static Godot.Collections.Array<int> BuildMilestoneBase() => new()
    {
        3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000,
    };

    /// <summary>击杀连击（2026-08-11，docs/archive/2026-08-11-score-combo-buff-pity-plan.md）：
    /// 窗口内连杀放大击杀分——怒首领蜂/虫姬链式得分的温和版（贪分 vs 稳）。</summary>
    public double ComboWindow { get; private set; } = 3.0;

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
    /// AC1（2026-08-11 健壮性审查）：step/max_mult 上界钳 [0,1e3]/[1,1e3]——巨值乘区在
    /// AddKillScore 的 (long) 乘算下溢出回绕为负（分数巨负进里程碑/榜单）；1e3 远超合理域
    /// （设计封顶 ×2.0）但杜绝 long 溢出（AB15/AB16 上界钳先例）。</summary>
    public void ApplyComboConfig(double window, double step, double maxMult)
    {
        ComboWindow = window;
        ComboStep = step;
        ComboMaxMult = maxMult;
    }

    public void AddScore(int points)
    {
        // 难度分数倍率统一在此乘算（easy ×1 / medium ×2 / hard ×3，配置表里的分值不变）
        // P4（2026-08-05）：得分总量钳制——手改配置 score 倍率极大时 int64 溢出（1e308 级）
        // 2026-08-10 健壮性审查：乘算提升 long 域——int × int 在 points×倍率超 2^31 时
        // 先回绕为负再进 Min（负分进入里程碑/榜单），long 域乘算后与上限钳制才生效
        Score = (int)Math.Min((long)Score + (long)points * GameState.Instance.ScoreMultiplier(), (long)ScoreCapValue);
        ScoreChanged?.Invoke(Score);
        // 2026-08-06 审计：里程碑推进改 while——与 apply_run_save 的全补口径一致（原单次 +1
        // 在单次加分跨多档时漏档：如 hard 倍率下高分击杀/Boss 奖励一次跨两档阈值），
        // 两路径行为统一（milestone_reached 按触发的档位逐档发，消费方按里程碑数计档）。
        // 2026-08-07：阈值求值迁移 C#（milestone_threshold 转发）；此处保持基于
        // _next_milestone 的 while——set_milestone_override 测试钩子允许阈值脱离曲线，
        // 批量推进（CountThresholdsUpTo）仅用于 apply_run_save 的存档恢复路径（低频、
        // 病态档数场景，批量收益大）；加分逐档仅 1-2 档，单值调用开销可忽略。
        // H03 兜底挂死守卫：与 MilestoneCurve.CountThresholdsUpTo 同款迭代上限——
        // cycle_mult 已钳 ≥1.0 后曲线单调，但 set_milestone_override 测试钩子允许阈值脱离
        // 曲线恒 ≤ Score（或阈值求值 int 溢出回绕为负），此时 while 永不退出，超限直接 break
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

    // ---------------- 击杀连击（2026-08-11；scoring.combo 段） ----------------

    /// <summary>击杀计分唯一入口（敌机击杀路径统一走此）：连击推进 + 乘区放大，
    /// 随后经 AddScore 乘难度倍率。Boss 击杀（AddBossKill）/事件奖励/擦弹不计连击。</summary>
    public void AddKillScore(int basePoints)
    {
        Combo += 1;
        _comboTimer = ComboWindow;
        // long 域乘算防回绕（乘区 double，截断前钳制 int 域；AddScore 内另有总分钳制）
        // AC1 双保险（2026-08-11 健壮性审查）：乘积钳 [0, long.MaxValue]——组合极端路径
        // （basePoints×乘区越界 → double→long 转换未定义/回绕巨负）下兜底防负分入账（AB12 双保险先例）
        var scaled = (long)Math.Round(Math.Clamp(basePoints * ComboMultiplier(), 0.0, (double)long.MaxValue));
        AddScore((int)Math.Min(scaled, (long)int.MaxValue));
        ComboChanged?.Invoke(Combo);
    }

    /// <summary>Boss 击杀计分（AddBossKill 编排内的计分域部分）：BossKills 推进 + 加分
    /// （G012：加分基准入 balance.json milestones.boss_kill_base；击杀低频，非热路径可直查）。</summary>
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

    /// <summary>断连（受击/测试/重开）：连击归零 + 计时清空 + 广播 HUD。幂等。</summary>
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

    /// <summary>连击窗口剩余时长（测试/诊断白盒读取；0 = 已断连）。</summary>
    public double ComboTimeLeft() => _comboTimer;

    /// <summary>里程碑恢复（ApplyRunSave 存档恢复路径调用）：按分数批量推进到当前档
    /// （CountThresholdsUpTo 单次调用 + O(1)/档 增量推进，含原 while 的 10000 档挂死守卫；
    /// 原逐档跨语言往返的 while 循环删除，存档恢复路径不再每档一次 GDScript 求值）。
    /// 只写内部计数/阈值，不发事件——信号由 ApplyRunSave 末尾统一直发（顺序不变）。</summary>
    public void RestoreMilestones(int score)
    {
        // H03 挂死守卫同源：CountThresholdsUpTo 内部封顶 10000 档（对齐原 apply_run_save 的 ms_cap）
        _milestoneCount = (int)GameState.Instance.Progression.CountThresholdsUpTo(
            score, Variant.From(MilestoneBase).AsGodotArray(), MilestoneCycleMult, GameState.Instance.MilestoneMult());
        _nextMilestone = GameState.Instance.MilestoneThreshold(_milestoneCount);
    }

    /// <summary>里程碑初始化（_Ready 与 ResetRun 共用）：计数归零 + 下一档阈值重算。</summary>
    public void InitMilestones()
    {
        _milestoneCount = 0;
        _nextMilestone = GameState.Instance.MilestoneThreshold(0);
    }

    /// <summary>连击窗口计时（_Process 经 GameState 调用）：窗口内无新击杀 → 超时断连
    /// （暂停时 process 冻结，与对局节奏一致）。</summary>
    public void Tick(double delta)
    {
        // 连击窗口计时：窗口内无新击杀 → 超时断连（暂停时冻结，与对局节奏一致）
        if (_comboTimer > 0.0)
        {
            _comboTimer -= delta;
            if (_comboTimer <= 0.0)
            {
                ResetCombo();
            }
        }
    }

    /// <summary>计分域复位（ResetRun 调用；信号发射点/顺序与拆域前一致——ComboChanged 经 ResetCombo）。</summary>
    public void ResetAll()
    {
        Score = 0;
        Kills = 0;
        BossKills = 0;
        InitMilestones();
        ResetCombo(); // 连击跨对局清零（幂等 + 广播 HUD）
    }

    // ---------------- 里程碑曲线（2026-08-11 自 GameState.Difficulty.cs 迁入的计分域语义部分） ----------------

    /// <summary>测试钩子（A7 遗留清理，公开化）：直接设定下一个里程碑阈值（不动曲线计数，保证测试确定性）</summary>
    public void SetMilestoneOverride(int threshold) => _nextMilestone = threshold;

    /// <summary>A7：测试/诊断白盒断言经公开接口
    /// 当前已触发的里程碑数（2026-08-04 母舰升级档位等消费点）</summary>
    public int MilestoneCount() => _milestoneCount;

    /// <summary>A7：测试/诊断白盒 setter（2026-08-06 审计：mothership_upgrade_test 曾直写
    /// _milestone_count ×5，补语义化公开接口；负值钳 0）</summary>
    public void SetMilestoneCount(int count) => _milestoneCount = Mathf.Max(count, 0);

    public int NextMilestone() => _nextMilestone;
}
