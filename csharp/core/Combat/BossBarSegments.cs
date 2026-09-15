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

    /// <summary>阈值保序收口（**单源**：Boss.LoadBalance 与血条段权/刻度都调它）。域钳到 [0,1]，
    /// ENRAGE 压到 EnrageMax（为抬升留出一个 OrderStep 余量），并保证 P2 &gt; ENRAGE。
    /// 倒挂输入（p2 ≤ e）时**先压 e 再抬 p2**：原引擎侧的 `Min(e + 0.01, 0.98)` 在 e 已顶到
    /// 0.99 时会算出 p2=0.98 &lt; e，保序修正自己产出了倒挂——Boss 按倒挂阈值转阶段、血条按
    /// 另一套画，玩家读到的阶段边界是假的。幂等：对已收口的值再调一次不变。</summary>
    public static (float Phase2, float Enrage) Normalize(float phase2HpRatio, float enrageHpRatio)
    {
        var e = MathF.Min(Clamp01(enrageHpRatio), EnrageMax);
        var p2 = Clamp01(phase2HpRatio);
        if (p2 <= e)
        {
            p2 = e + OrderStep; // e ≤ 0.98 → p2 ≤ 0.99，严格高于 e
        }

        return (p2, e);
    }

    /// <summary>段权（三项：P1/P2/ENRAGE），非负且和恒为 1。</summary>
    public static float[] Weights(float phase2HpRatio, float enrageHpRatio)
    {
        var (p2, e) = Normalize(phase2HpRatio, enrageHpRatio);
        return new[] { 1.0f - p2, p2 - e, e };
    }

    /// <summary>阶段刻度线比例（两项：P2 段界 / ENRAGE 段界），自血条左端量起、单调递减。</summary>
    public static float[] Ticks(float phase2HpRatio, float enrageHpRatio)
    {
        var (p2, e) = Normalize(phase2HpRatio, enrageHpRatio);
        return new[] { p2, e };
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
