namespace InfiAir.Core.Combat;

/// <summary>
/// 辅助瞄准的距离衰减分段曲线（纯逻辑，零 Godot 依赖）：peak 内全额 → end 处降到地板 min，
/// 中间线性；开火弱追踪与准星磁吸共用同一条曲线。数值由引擎侧注入
/// （balance.json player.aim_assist.falloff，当前 400 / 1400 / 0.3），core 不产生取值。
/// 原先藏在 Player 节点里；两种坏法都静默——峰值内提前衰减会让近距离追踪变钝（玩家只觉得「手感差」），
/// 末端地板写丢则远距离追踪直接归零。
/// </summary>
public static class AimFalloff
{
    /// <summary>取值：d &lt;= peak 全额；d &gt;= end 取地板；之间按 (d - peak) / (end - peak) 线性。
    /// 两端点都闭合（边界值归入「全额」与「地板」两侧）。
    /// 非法配置（peak &gt;= end）只由两端分支兜住、不进除法：peak 先命中，不产生 NaN。
    /// 非有限输入（NaN/±∞）必须回退安全值——NaN 与任何值比较恒假、会穿过两端分支落进除法，
    /// 结果乘进辅助强度后一路穿到准星偏移（磁吸路径的注入点无有限性判定，Mathf.Min 与
    /// Normalized 都不拦 NaN），表现是准星静默失准。NaN 距离按「最远」处理（地板）；
    /// 参数非法同样回退地板（与同层 TankLiquid/WarpClamp 同款口径）。</summary>
    public static float Evaluate(float distance, float peak, float end, float minValue)
    {
        var floor = float.IsFinite(minValue) ? minValue : 0.0f;
        if (float.IsNaN(distance) || !float.IsFinite(peak) || !float.IsFinite(end))
        {
            return floor;
        }

        if (distance <= peak)
        {
            return 1.0f;
        }

        if (distance >= end)
        {
            return floor;
        }

        return Lerp(1.0f, floor, (distance - peak) / (end - peak));
    }

    // 逐位等价于引擎侧的 Mathf.Lerp（Godot 实现 = from + (to - from) * weight；
    // 换成 (1 - weight) * from + weight * to 会在部分权重上差 1 ulp，进而改变漂移中的曲线取值）。
    private static float Lerp(float from, float to, float weight) => from + ((to - from) * weight);
}
