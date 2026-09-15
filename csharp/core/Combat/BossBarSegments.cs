namespace InfiAir.Core.Combat;

/// <summary>
/// Boss 血条的分段几何（纯逻辑，零 Godot 依赖）：**段界即阶段阈值**，段权与刻度线一律由
/// phase2/enrage 血量比例派生，不再由 HUD 另存一份常量。
///
/// 段权（段序 = 阶段顺序 P1→P2→ENRAGE）= [1−p2, p2−e, e]，和恒为 1；
/// 阶段刻度线（自血条左端量起的比例）= [p2, e]，与段权的前缀和逐项对应（单测钉住）。
/// 阈值由调用方从活着的 Boss 实例读（Boss.Phase2HpRatio/EnrageHpRatio，其单源是
/// balance 的 boss.phase2_hp_ratio / boss.enrage.hp_ratio）。
///
/// 为什么必须有这份派生：原实现把 [0.3, 0.4, 0.3] 与 [0.7, 0.3] 硬编码在 HUD 里（注释自称
/// 「与默认值一致、解耦」），把 phase2_hp_ratio 调到 0.6 后 Boss 在 60% 转阶段而血条段界与
/// 刻度线仍在 70%——玩家从血条读到的阶段边界是假的，掉段闪也会定位到错段；冒烟/长局/截图
/// 探针都不判段界，属静默错位。
/// </summary>
public static class BossBarSegments
{
    /// <summary>段数（P1 / P2 / ENRAGE）。</summary>
    public const int Count = 3;

    /// <summary>段界保序步长：倒挂输入（p2 ≤ e）时把 P2 抬到 ENRAGE 之上，与 Boss 侧同款收口
    /// （Boss.LoadBalance 亦以 0.01 步长修正）。</summary>
    private const float OrderStep = 0.01f;

    /// <summary>ENRAGE 阈值上限：为保序留出至少一个 OrderStep（0.98 + 0.01 = 0.99）。</summary>
    private const float EnrageMax = 0.99f - OrderStep;

    /// <summary>段权（三项：P1/P2/ENRAGE），非负且和恒为 1。</summary>
    public static float[] Weights(float phase2HpRatio, float enrageHpRatio)
    {
        var (p2, e) = Thresholds(phase2HpRatio, enrageHpRatio);
        return new[] { 1.0f - p2, p2 - e, e };
    }

    /// <summary>阶段刻度线比例（两项：P2 段界 / ENRAGE 段界），自血条左端量起、单调递减。</summary>
    public static float[] Ticks(float phase2HpRatio, float enrageHpRatio)
    {
        var (p2, e) = Thresholds(phase2HpRatio, enrageHpRatio);
        return new[] { p2, e };
    }

    /// <summary>阈值域收口：非有限按 0，钳到 [0.01, 0.99]（Boss.LoadBalance 的同款域），
    /// 并保证 P2 &gt; ENRAGE——调用方传入原始配置或倒挂输入时，血条段权不得出现负值、
    /// 段界不得反序（Boss 侧已保证保序，此处仍按防御写）。</summary>
    private static (float Phase2, float Enrage) Thresholds(float phase2HpRatio, float enrageHpRatio)
    {
        var e = MathF.Min(Clamp01(enrageHpRatio), EnrageMax);
        var p2 = Clamp01(phase2HpRatio);
        if (p2 <= e)
        {
            p2 = e + OrderStep; // e ≤ 0.98 → p2 ≤ 0.99，严格高于 e
        }

        return (p2, e);
    }

    private static float Clamp01(float ratio)
    {
        if (!float.IsFinite(ratio) || ratio < 0.0f)
        {
            return 0.0f;
        }

        return ratio > 1.0f ? 1.0f : ratio;
    }
}
