namespace InfiAir.Core;

/// <summary>
/// 量槽液面的几何护栏（纯逻辑，零 Godot 依赖）。
///
/// 引擎侧 <c>FuelTank</c> 的填充多边形 = 液面采样点 + 内腔底两角；液面是绕液位摆动的正弦。
/// 两侧边界都要守：
///   **底侧**——摆动幅度相对内腔高度取值（常态约 4%，晃动叠加时到 15%），液位低于波幅时波谷探到
///   内腔底之下，多边形与底边自交，Godot 三角化失败、**整块填充静默不画**；低油量恰是读数最该看清时。
///   **顶侧**——满油时液位即内腔顶，同幅摆动会把液面与弯月线画到内腔之外（压过外框上缘）；
///   此侧不触发三角化失败（多边形仍简单），但绘制溢出内腔。
/// 故波幅同时受「液层厚度」与「液位上方余量」双向钳制；两条判定都留在这里由单测钉住，
/// 而不是留在绘制代码里靠人工过目兜底。无头门禁走 dummy 渲染虽会执行 _Draw（自交可报
/// 「Invalid polygon data」，冒烟错误正则已收录），但顶侧溢出不会报错——仍需几何护栏而非只依赖门禁。
/// </summary>
public static class TankLiquid
{
    /// <summary>可安全三角化所需的最小液层厚度（px）：低于此值多边形退化成近共线，
    /// 三角化同样失败。此档只画液面线（线不参与三角化），视觉上读作「见底」。</summary>
    public const float MinDrawableHeightPx = 2.0f;

    /// <summary>液面波幅（px）。want = 内腔高 × 幅度比例；但不得超过液层厚度与液位上方余量的较小者——
    /// 前者保证波谷不越过内腔底（防自交、防整块不画），后者保证波峰不越过内腔顶（防绘制溢出内腔）。
    /// 非有限输入（NaN/±∞）回退安全值：NaN 液位/比例会让波幅为 NaN，填充多边形顶点随即非法。</summary>
    public static float WaveAmplitude(float innerHeightPx, float levelRatio, float amplitudeRatio)
    {
        var height = heightOf(innerHeightPx);
        var level = Clamp01(levelRatio);
        var fill = height * level;
        var headroom = height * (1.0f - level);
        var room = fill < headroom ? fill : headroom;
        var ratio = float.IsFinite(amplitudeRatio) ? amplitudeRatio : 0.0f;
        var want = height * (ratio > 0.0f ? ratio : 0.0f);
        return want < room ? want : room;
    }

    /// <summary>液层是否厚到能安全三角化：否 = 调用方跳过填充多边形、只留液面线。</summary>
    public static bool HasDrawableFill(float innerHeightPx, float levelRatio)
        => heightOf(innerHeightPx) * Clamp01(levelRatio) >= MinDrawableHeightPx;

    private static float heightOf(float innerHeightPx)
        => float.IsFinite(innerHeightPx) && innerHeightPx > 0.0f ? innerHeightPx : 0.0f;

    /// <summary>钳 [0,1]；非有限（NaN/±∞）按 0 处理——NaN 通过与 0/1 的比较恒假，
    /// 原样放行会让液位与波幅变 NaN。</summary>
    private static float Clamp01(float value)
    {
        if (!float.IsFinite(value) || value < 0.0f)
        {
            return 0.0f;
        }

        return value > 1.0f ? 1.0f : value;
    }
}
