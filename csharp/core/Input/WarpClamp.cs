namespace InfiAir.Core.Input;

/// <summary>
/// 鼠标 confine 的 warp 目标钳制（纯逻辑，零 Godot 依赖）。
/// 坐标系单源：Godot 4 的 Input.warp_mouse 与 DisplayServer.warp_mouse 都收**窗口相对坐标**
/// （GodotSharp.xml 原文：relative to an origin at the upper left corner of the currently
/// focused Window Manager game window）——窗口的屏幕位置不属于本函数的输入语义，
/// 叠加窗口屏幕偏移会把光标弹开该偏移量（窗口不在 (0,0) 时准星瞬跳 + 窗口左/上边带成死区）。
/// 调用方只负责把本函数结果原样交给 warp_mouse，不得再做坐标换算。
/// </summary>
public static class WarpClamp
{
    /// <summary>内容区边缘内侧保留量（px）：贴到 0 或 size 会被系统判为仍在窗外，形成 exited/warp 循环。</summary>
    public const float EdgeInset = 1.0f;

    /// <summary>把窗口相对坐标钳到 [EdgeInset, 边长 − EdgeInset]。
    /// 退化窗口（边长 &lt; 2×EdgeInset）不存在内侧区间，钉在 EdgeInset；
    /// 非有限输入（NaN/±∞）同样回退 EdgeInset——Math.Clamp 对 NaN 不设防，会原样穿出被 warp 到未定义位置。</summary>
    public static (float X, float Y) Target(float x, float y, float winWidth, float winHeight)
        => (Axis(x, winWidth), Axis(y, winHeight));

    private static float Axis(float value, float size)
    {
        var max = size - EdgeInset;
        if (!(max >= EdgeInset))
        {
            return EdgeInset;
        }

        return float.IsFinite(value) ? Math.Clamp(value, EdgeInset, max) : EdgeInset;
    }
}
