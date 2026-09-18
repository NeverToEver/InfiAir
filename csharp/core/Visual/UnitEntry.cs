namespace InfiAir.Core.Visual;

/// <summary>
/// 单位入场落位的缓动算式（纯逻辑，零 Godot 依赖）：elapsed/duration → 落位进度 0..1。
/// 生产侧（普通敌机与编队机）只把进度乘进自己的视觉通道，本类不碰任何节点。
///
/// 为什么是 ease-out 而不是线性：入场是「从屏外高速压下来、在落点刹住」，线性折点在
/// 逐帧观感里是可见的拐角，且终帧速度非零；三次 ease-out 起手快、收尾贴合落位。
/// 为什么在 core：进度算式的边界（duration ≤ 0、时钟未来值、异常 delta）决定了有没有
/// 「永久停在入场姿态」这一类静默坏点（机体永远偏小偏高，且只在个别路径上出现）。
///
/// 非有限入参返回 **1**（落位态）而不是 0：入场是纯外观，一次异常 delta 不该把单位永久
/// 钉在入场姿态上——落在终态是唯一自愈且不可见的选择。
/// </summary>
public static class UnitEntry
{
    /// <summary>落位进度（0 = 入场起点，1 = 已落位）：elapsed 是自入场起算的模拟秒数
    /// （§2.10：表现层推进用模拟时间）。duration ≤ 0 视为瞬间落位（返回 1，不做除零）；
    /// elapsed ≤ 0 返回 0（起点）；elapsed ≥ duration 返回 1（已落位）。</summary>
    public static float Placement01(float elapsed, float duration)
    {
        if (!float.IsFinite(elapsed) || !float.IsFinite(duration))
        {
            return 1.0f;
        }

        if (duration <= 0.0f)
        {
            return 1.0f;
        }

        if (elapsed <= 0.0f)
        {
            return 0.0f;
        }

        if (elapsed >= duration)
        {
            return 1.0f;
        }

        var t = elapsed / duration;
        var inv = 1.0f - t;
        return 1.0f - inv * inv * inv;
    }
}
