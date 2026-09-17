namespace InfiAir.Core.Progression;

/// <summary>
/// 本局达成目标参数：击杀 Boss 数或存活时长，任一满足即达成。默认值经人类认可（Boss 10 只 / 20 分钟）。
/// </summary>
public sealed class RunGoalConfig
{
    /// <summary>达成所需 Boss 击杀数（≤0 = 该项不参与判定）。</summary>
    public int BossKillsTarget { get; set; } = 10;

    /// <summary>达成所需存活秒数（≤0 = 该项不参与判定）。</summary>
    public double SurviveSeconds { get; set; } = 1200.0;
}

/// <summary>达成方式：未达成 / 靠 Boss 击杀达成 / 靠存活时长达成。</summary>
public enum RunGoalKind
{
    None = 0,
    BossKills = 1,
    Survive = 2,
}

/// <summary>
/// 本局达成判定：把「无尽必死曲线」从纯挫败变成有目标的挑战。
/// 纯 .NET、零 Godot 依赖，可独立单测。
///
/// 行业依据：Brotato 的无尽模式官方口径是「打过 wave 20 之后死亡仍算胜利」——
/// 必死曲线照旧，但给出一个「打到哪算赢」的锚点，让失败归因落回玩家自身
/// （Juul 的实证：玩家偏好「失败是自己造成的」，且「失败几次后通关」的评分最高）。
/// 达成判定不改变任何难度数值，也不终止本局——达成后仍可继续打（必死曲线不变）。
/// </summary>
public static class RunGoal
{
    /// <summary>是否已达成（任一条件满足）。</summary>
    public static bool Achieved(int bossKills, double runTime, RunGoalConfig cfg) =>
        Kind(bossKills, runTime, cfg) != RunGoalKind.None;

    /// <summary>达成方式（未达成返回 None）。两项都未配置时永远未达成。
    ///
    /// **固定优先级**（Boss 击杀 &gt; 存活），不是「哪个先达成」：本函数是当前状态的纯函数，
    /// 不持有历史，无法知道两个条件里哪个先满足；两条件都满足时恒报 Boss 击杀（它更主动、
    /// 是玩家操作的直接结果）。原注释写「取先到者」与实现不符，已更正。
    /// 注意 <see cref="Achieved"/> 不受优先级影响——任一满足即为真，达成与否不会因此抖动。</summary>
    public static RunGoalKind Kind(int bossKills, double runTime, RunGoalConfig cfg)
    {
        var killsTarget = cfg.BossKillsTarget;
        if (killsTarget > 0 && bossKills >= killsTarget)
        {
            return RunGoalKind.BossKills;
        }

        var surviveTarget = cfg.SurviveSeconds;
        if (surviveTarget > 0.0 && double.IsFinite(runTime) && runTime >= surviveTarget)
        {
            return RunGoalKind.Survive;
        }

        return RunGoalKind.None;
    }

    /// <summary>
    /// 达成进度（0..1）：取两个条件中更接近达成者的完成比例。
    /// 用于常驻进度显示，让玩家在达成前就能看到「还差多少」——这是把必死曲线变得可预期的主要手段。
    /// 两项都未配置时返回 1.0（无可追求目标，视为已达成以免显示一个永远填不满的条）。
    /// </summary>
    public static double Progress(int bossKills, double runTime, RunGoalConfig cfg)
    {
        var killTarget = cfg.BossKillsTarget;
        var surviveTarget = cfg.SurviveSeconds;
        var killConfigured = killTarget > 0;
        var surviveConfigured = surviveTarget > 0.0 && double.IsFinite(surviveTarget);
        if (!killConfigured && !surviveConfigured)
        {
            return 1.0;
        }

        var best = 0.0;
        if (killConfigured)
        {
            var ratio = Math.Max(bossKills, 0) / (double)killTarget;
            best = Math.Max(best, ratio);
        }

        if (surviveConfigured)
        {
            var t = double.IsFinite(runTime) && runTime > 0.0 ? runTime : 0.0;
            best = Math.Max(best, t / surviveTarget);
        }

        return Math.Clamp(best, 0.0, 1.0);
    }

    /// <summary>
    /// 两个条件里「更接近达成」的那一个（HUD 常驻进度显示哪条用）。
    /// 未配置的条件不参与；只有一项配置即返回该项；都未配置返回 None。
    /// 并列时取 Boss 击杀——与 <see cref="Kind"/> 的固定优先级同向，避免同一对输入在两处
    /// 判定上对并列点给出不同分支（HUD 曾自行内联一遍并列比较，与 <see cref="Progress"/>
    /// 的取最大口径不完全等价：单支未配置时内联会把 −1 当比例参与比较）。
    /// </summary>
    public static RunGoalKind CloserKind(int bossKills, double runTime, RunGoalConfig cfg)
    {
        var killTarget = cfg.BossKillsTarget;
        var surviveTarget = cfg.SurviveSeconds;
        var killConfigured = killTarget > 0;
        var surviveConfigured = surviveTarget > 0.0 && double.IsFinite(surviveTarget);
        if (!killConfigured && !surviveConfigured)
        {
            return RunGoalKind.None;
        }

        if (!surviveConfigured)
        {
            return RunGoalKind.BossKills;
        }

        if (!killConfigured)
        {
            return RunGoalKind.Survive;
        }

        var killRatio = Math.Max(bossKills, 0) / (double)killTarget;
        var t = double.IsFinite(runTime) && runTime > 0.0 ? runTime : 0.0;
        return killRatio >= t / surviveTarget ? RunGoalKind.BossKills : RunGoalKind.Survive;
    }
}

/// <summary>
/// 难度命名档位：把连续难度乘数 D 映射到命名档（RoR1 用 10 档命名把单调爬升叙事化）。
/// 纯 .NET、零 Godot 依赖，可独立单测。
///
/// 存在理由：原实现只有一个连续数字「难度 xN.NN」，玩家读不出「我现在到哪个阶段了」。
/// 命名档位让爬升可读、可讨论（「这局撑到第 4 档」），且不改变任何数值。
/// 档位阈值由 balance.json 给出（可调），本类只做区间判定。
/// </summary>
public static class DifficultyTier
{
    /// <summary>D 落在第几档（0 起）。阈值表须升序；空表返回 0。</summary>
    public static int IndexFor(double difficulty, IReadOnlyList<double> thresholds)
    {
        if (thresholds == null || thresholds.Count == 0)
        {
            return 0;
        }

        var d = double.IsFinite(difficulty) ? difficulty : thresholds[0];
        var index = 0;
        for (var i = 0; i < thresholds.Count; i++)
        {
            // 阈值非正值视为「该档从开局即适用」，仍参与比较（非单调表由 index 自然兜底为最小越过档）
            if (d >= thresholds[i])
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        return index;
    }
}
