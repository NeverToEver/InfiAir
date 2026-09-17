namespace InfiAir.Core.Combat;

/// <summary>
/// 辅助瞄准档位参数的读取口径（纯逻辑，零 Godot 依赖）：配置损坏（负值）时回退默认，
/// 而不是钳成 0——钳 0 对 <c>magnet_strength</c> 等于「不再磁吸」，对 <c>magnet_range</c>
/// 等于「磁吸永不生效」，护栏加在没人读的一侧（旧实现把钳制留在 Player 的诊断读口上，
/// 真正做磁吸算术的 AimFrameLayer 吃的是未钳制副本：负 strength 经
/// <c>Normalized() * 负值</c> 把准星朝远离目标方向推，负 range 使磁吸整块失效）。
///
/// 0 是合法取值（= 该机制关闭），只有负值与非有限量算损坏。
/// 比较式写法与引擎 <c>Mathf.Max</c> 同口径（NaN 经比较落到回退值，不得改用会原样传 NaN 的写法）。
/// </summary>
public static class AimAssistParams
{
    /// <summary>磁吸输入窗口下界的抬升量：两键相等时 MagnetPull 的 t = 0/0 = NaN 会污染准星。</summary>
    public const float WindowEpsilon = 0.01f;

    /// <summary>非负域钳：合法（非负且有限）原样返回，损坏回退 <paramref name="fallback"/>。</summary>
    public static float NonNegativeOr(float value, float fallback)
        => float.IsFinite(value) && value >= 0.0f ? value : fallback;

    /// <summary>磁吸输入窗口两端：各自非负域钳，且上界抬到下界之上（相等即 MagnetPull 除零得 NaN）。</summary>
    public static (float Min, float Full) MagnetWindow(float min, float full, float defMin, float defFull)
    {
        var lo = NonNegativeOr(min, defMin);
        var hi = NonNegativeOr(full, defFull);
        return (lo, hi > lo + WindowEpsilon ? hi : lo + WindowEpsilon);
    }
}
