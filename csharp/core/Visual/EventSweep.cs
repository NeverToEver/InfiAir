namespace InfiAir.Core.Visual;

/// <summary>
/// 舰体事件流光与击杀连击里程碑的算式（纯逻辑，零 Godot 依赖）：扫过进度 0..1、
/// 里程碑档位、档位对应的幅度倍率。生产侧只取进程写入着色器参数。
///
/// 为什么在 core：扫光是**一次性事件反馈、不常驻**（`DESIGN_BASELINE` §1.9.1
/// 「静止几乎不动、变化时才动」）——它的「有没有」由进度是否回到 0 表达，而「哪一档更强」
/// 是纯算式；写错的表现是流光停在半途常亮或档位永不触发，两者都不崩、不报错。
///
/// 档位语义（`DESIGN_BASELINE` §2.12 判据 6「可减弱」的取值面）：连击每次递增都扫一次，
/// 连击数是 10 / 50 / 100 的整倍时扫得更强——100 的整倍必是 50 与 10 的整倍，
/// 故按高到低判，取最高档（顺序写反会让 100 档被 10 档吃掉，玩家只看到「每 10 连一样强」）。
/// 幅度倍率另乘基础值（balance `effects.motion.sweep_amp`），本类不持有任何绝对取值。
///
/// 非法入参一律落到「无扫光 / 无加成」而不是抛异常或 NaN：NaN 会顺着乘法污染整屏着色器输出
/// （整块黑或白，引擎侧零报错），在扫光这里正是「整只舰体常亮」——比不闪更糟。
/// </summary>
public static class EventSweep
{
    /// <summary>第一档里程碑步长（连击的整倍）：此后每满一次连击里程碑都扫一次更强的流光。</summary>
    public const int MilestoneStep = 10;

    /// <summary>第二档里程碑步长（更强一档）。</summary>
    public const int MilestoneMid = 50;

    /// <summary>第三档里程碑步长（最强一档）。</summary>
    public const int MilestoneHigh = 100;

    /// <summary>里程碑档位（0 = 非里程碑，1 = 10 的整倍，2 = 50 的整倍，3 = 100 的整倍）。
    /// combo ≤ 0 返回 0：连击断连（ResetCombo）也走 ComboChanged，那是「本局断连」不是「击杀节拍」，
    /// 扫光不该在断连时亮一下。</summary>
    public static int MilestoneTier(int combo)
    {
        if (combo <= 0)
        {
            return 0;
        }

        if (combo % MilestoneHigh == 0)
        {
            return 3;
        }

        return combo % MilestoneMid == 0 ? 2 : (combo % MilestoneStep == 0 ? 1 : 0);
    }

    /// <summary>档位 → 幅度倍率（普通扫光 1.0）。boostPerTier 来自 balance
    /// （`effects.motion.sweep_milestone_boost`），非有限或为负时按 0 处理（退化为「无档位差异」，
    /// 而不是把倍率算成 NaN 或负值——负幅度会让着色器里的高斯带变成减光）。</summary>
    public static float AmplitudeFactor(int tier, float boostPerTier)
    {
        var boost = float.IsFinite(boostPerTier) && boostPerTier > 0.0f ? boostPerTier : 0.0f;
        return 1.0f + Math.Clamp(tier, 0, 3) * boost;
    }

    /// <summary>扫过进度（0..1）：elapsed 自触发当帧起算的**模拟**秒数（§2.10：表现层推进
    /// 走模拟时间，不是墙钟）。elapsed ≤ 0 返回 0（尚未推进）；duration ≤ 0 返回 1
    /// ——半途停下会让流光常亮，宁可瞬间走完；任一入参非有限返回 0（无扫光）。</summary>
    public static float Progress01(float elapsed, float duration)
    {
        if (!float.IsFinite(elapsed) || !float.IsFinite(duration))
        {
            return 0.0f;
        }

        if (duration <= 0.0f)
        {
            return 1.0f;
        }

        if (elapsed <= 0.0f)
        {
            return 0.0f;
        }

        return elapsed >= duration ? 1.0f : elapsed / duration;
    }
}
