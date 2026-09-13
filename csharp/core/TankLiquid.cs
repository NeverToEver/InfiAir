namespace InfiAir.Core;

/// <summary>
/// 量槽液面的几何护栏（纯逻辑，零 Godot 依赖）。
///
/// 引擎侧 <c>FuelTank</c> 的填充多边形 = 液面采样点 + 内腔底两角；液面是绕液位摆动的正弦。
/// 摆动幅度相对内腔高度取值（常态约 4%，晃动叠加时到 15%），一旦液位低于波幅，
/// 波谷就探到内腔底边之下——多边形与底边自交，Godot 三角化失败、**整块填充静默不画**。
/// 低油量恰好是燃料读数最该看清的时候，却最先坏掉；且无头门禁走 dummy 渲染不执行 _Draw，
/// 抓不到这个错误。故把「波幅上限」与「可安全三角化的最薄液层」两条判定留在这里，
/// 由单测钉住，而不是留在绘制代码里靠人工过目兜底。
/// </summary>
public static class TankLiquid
{
    /// <summary>可安全三角化所需的最小液层厚度（px）：低于此值多边形退化成近共线，
    /// 三角化同样失败。此档只画液面线（线不参与三角化），视觉上读作「见底」。</summary>
    public const float MinDrawableHeightPx = 2.0f;

    /// <summary>液面波幅（px）。want = 内腔高 × 幅度比例；但不得超过液层厚度——
    /// 这是「波谷不越过内腔底」的充分条件：液面最高点 = 液位 + 波幅 ≤ 内腔底。</summary>
    public static float WaveAmplitude(float innerHeightPx, float levelRatio, float amplitudeRatio)
    {
        var height = heightOf(innerHeightPx);
        var fill = height * Clamp01(levelRatio);
        var want = height * (amplitudeRatio > 0.0f ? amplitudeRatio : 0.0f);
        return want < fill ? want : fill;
    }

    /// <summary>液层是否厚到能安全三角化：否 = 调用方跳过填充多边形、只留液面线。</summary>
    public static bool HasDrawableFill(float innerHeightPx, float levelRatio)
        => heightOf(innerHeightPx) * Clamp01(levelRatio) >= MinDrawableHeightPx;

    private static float heightOf(float innerHeightPx) => innerHeightPx > 0.0f ? innerHeightPx : 0.0f;

    private static float Clamp01(float value) => value < 0.0f ? 0.0f : (value > 1.0f ? 1.0f : value);
}
