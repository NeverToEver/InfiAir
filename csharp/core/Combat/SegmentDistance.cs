namespace InfiAir.Core.Combat;

/// <summary>
/// 点到线段的最近距（纯逻辑，零 Godot 依赖）。
///
/// 背景：这条算式原先在引擎侧就地写了两份（激光束命中、弹道擦边入框），只有后者带零长
/// 守卫。起终点重合时投影分母为 0，`0/0` 得 NaN，而 NaN 与命中阈值的比较恒为 false——
/// 不抛错、不打日志，命中判定静默失效（激光束越短越容易踩到，长束按当前配置恰好遮住了）。
/// 故算式收敛到本类一份，并显式定义退化语义：**零长线段 = 到端点 a 的距离**。
///
/// 求值顺序与引擎侧一致（点积先 X 后 Y、长度平方同为 `x*x + y*y`），换实现不移动
/// 「距离² ≤ 半径²」这类闭区间判定的边界。
/// </summary>
public static class SegmentDistance
{
    /// <summary>点到线段 ab 的最近距离平方（热路径用：与阈值平方比较可免开方）。</summary>
    public static float PointToSegmentSq(float px, float py, float ax, float ay, float bx, float by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var lenSq = (abx * abx) + (aby * aby);
        if (!(lenSq > 0.0f))
        {
            // 零长线段（含端点非有限的退化输入）：投影无定义，按到端点 a 的距离
            return Sq(px - ax, py - ay);
        }

        var t = Clamp01((((px - ax) * abx) + ((py - ay) * aby)) / lenSq);
        return Sq(px - (ax + (abx * t)), py - (ay + (aby * t)));
    }

    /// <summary>点到线段 ab 的最近距离（开方版；事件率调用点用）。</summary>
    public static float PointToSegment(float px, float py, float ax, float ay, float bx, float by) =>
        Sqrt(PointToSegmentSq(px, py, ax, ay, bx, by));

    // 与引擎 Mathf.Clamp(v, 0, 1) 同语义：NaN 双向比较皆 false 故原样穿透，不静默改成 0 或 1
    private static float Clamp01(float v) => v < 0.0f ? 0.0f : (v > 1.0f ? 1.0f : v);

    private static float Sq(float x, float y) => (x * x) + (y * y);

    private static float Sqrt(float v) => System.MathF.Sqrt(v);
}
