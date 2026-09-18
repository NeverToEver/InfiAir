namespace InfiAir.Core.Combat;

/// <summary>
/// 辅助瞄准目标的几何判定（纯逻辑，零 Godot 依赖）：辅助框半宽、框包含判定、框沿距与锥角判定。
/// 扫描循环仍在引擎侧（要遍历 Godot 节点注册表），但**算式**收在这里，可单测——
/// 原先这些算式内联在 AimFrameLayer 的每帧扫描里，判不出的坏法（框沿距把负分量计入长度、
/// 半宽漏加 pad、锥角阈值方向反）都只在画面上表现为「辅瞄偏弱/偏强」。
/// </summary>
public static class AimTargeting
{
    /// <summary>框半宽 = 碰撞半径 + 档位内边距（两者各自非负域钳：负半宽使框判定恒不通过）。</summary>
    public static float FrameHalfSize(float collisionRadius, float pad)
    {
        var r = collisionRadius > 0.0f ? collisionRadius : 0.0f;
        var p = pad > 0.0f ? pad : 0.0f;
        return r + p;
    }

    /// <summary>点是否落在轴对齐方框内（含边界）。</summary>
    public static bool InFrame(float px, float py, float cx, float cy, float half)
        => Abs(px - cx) <= half && Abs(py - cy) <= half;

    /// <summary>点是否落在圆内（含边界）：方框粗筛省平方；负/NaN 半径（数据损坏）恒不含。</summary>
    public static bool InCircle(float px, float py, float cx, float cy, float radius)
    {
        if (!(radius >= 0.0f))
        {
            return false;
        }

        var dx = Abs(px - cx);
        var dy = Abs(py - cy);
        if (dx > radius || dy > radius)
        {
            return false;
        }

        return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>框沿距（点未入框时的欧氏距离，入框为 0）：只取框外分量——
    /// 单轴出框时另一轴为负，把负分量计入长度会系统性偏近（磁吸偏弱、range 边界误判）。</summary>
    public static float FrameEdgeDistance(float px, float py, float cx, float cy, float half)
    {
        var dx = Abs(px - cx) - half;
        var dy = Abs(py - cy) - half;
        var ox = dx > 0.0f ? dx : 0.0f;
        var oy = dy > 0.0f ? dy : 0.0f;
        return Sqrt(ox * ox + oy * oy);
    }

    /// <summary>目标方向是否落在瞄准锥内（<paramref name="aimDir"/> 须为单位向量，与既有调用口径一致）。
    /// 判据写成「不排除」的取反形式：与原点重合（<c>to / 0</c> 得 NaN）或锥阈值为 NaN 时比较为假、
    /// 目标仍被选中——这是既有语义（NaN 不得把目标排除），换成 <c>&gt;=</c> 会把它反过来。</summary>
    public static bool InCone(float aimX, float aimY, float toX, float toY, float coneCos)
    {
        var len = Sqrt(toX * toX + toY * toY);
        var dot = aimX * (toX / len) + aimY * (toY / len);
        return !(dot < coneCos);
    }

    // 逐位等价于引擎 Mathf.Abs/Sqrt（同一求值顺序；MathF.Sqrt 与 Mathf.Sqrt 同为正确舍入）
    private static float Abs(float v) => v < 0.0f ? -v : v;

    private static float Sqrt(float v) => System.MathF.Sqrt(v);
}
