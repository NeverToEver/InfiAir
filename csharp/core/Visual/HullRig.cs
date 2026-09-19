namespace InfiAir.Core.Visual;

/// <summary>
/// 机体形态算式（纯逻辑，零 Godot 依赖）：机炮收放、炮管热量、散热排气、翼尖涡流。
/// 生产侧只做取值与适配（读状态 → 算式 → 写节点变换/颜色），算式与护栏只有这一份。
///
/// 设计约束（<c>DESIGN_BASELINE</c> §2.18）：
///   - **只作用于贴图节点及其子节点**：形态层让机体「形状」随状态改变，但机体根节点、
///     碰撞圆、擦弹环、弹道判定逐位不动。
///   - **不参与玩法**：热量只驱动着色与抖动，**不构成过热惩罚**——数值层零改动。
///   - **动效强度**（设置项 fx_intensity）在引擎侧乘上振幅后才进本类，本类不感知设置。
///   - 非法入参一律返回静息态（收拢 / 冷 / 不排气 / 无涡流）：NaN 会顺着颜色与变换链
///     污染整棵贴图子树（引擎侧零报错），而「静息」正好是「这一帧不加读数」。
/// </summary>
public static class HullRig
{
    /// <summary>机炮展开目标（1 = 展开，0 = 收拢）：fireHoldAge 是距最近一次开火的秒数，
    /// 落在保持窗 <paramref name="holdTime"/> 内即展开。窗末取收（age == hold 即收）。
    /// holdTime ≤ 0 是「关闭该动效」的合法口径（机体常驻收拢）；任一入参非有限返回 0。</summary>
    public static double DeployTarget(double fireHoldAge, double holdTime)
    {
        if (!double.IsFinite(fireHoldAge) || !double.IsFinite(holdTime) || holdTime <= 0.0)
        {
            return 0.0;
        }

        return fireHoldAge < holdTime ? 1.0 : 0.0;
    }

    /// <summary>开火升温：每次开火累加 perShot 并钳 [0,1]。钳上限是读数前提——超界的插值
    /// 因子会让炮管着色饱和后再无变化（连发越久越热这个读数就没了）。
    /// 已坏的热度从 0 起算（自愈）；perShot 非有限或非正时保持现状。</summary>
    public static double HeatAfterShot(double heat, double perShot)
    {
        var h = double.IsFinite(heat) ? Clamp01(heat) : 0.0;
        if (!double.IsFinite(perShot) || perShot <= 0.0)
        {
            return h;
        }

        return Clamp01(h + perShot);
    }

    /// <summary>停火降温：热量按 exp(-delta/tau) 指数衰减（帧率无关）。tau ≤ 0 是「关掉热度层」
    /// 的口径——热度立刻归零（炮管回到冷态），不是「不冷却」。delta ≤ 0 或非有限时保持现状
    /// （停帧不是冷却到 0）；已坏的热度自愈为冷态。</summary>
    public static double HeatDecay(double heat, double tau, double delta)
    {
        if (!double.IsFinite(heat))
        {
            return 0.0;
        }

        var h = Clamp01(heat);
        if (!double.IsFinite(tau) || tau <= 0.0)
        {
            return 0.0;
        }

        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return h;
        }

        var k = Math.Exp(-delta / tau);
        return double.IsFinite(k) ? Clamp01(h * k) : 0.0;
    }

    /// <summary>散热排气强度（0..1）：热度在 <paramref name="threshold"/> 以下完全不排气，
    /// 阈值到 1 之间线性升到满——「刚打完正在放热」要有一个明确的起始热度，否则常态机体
    /// 永远在冒热气。threshold ≥ 1 表示永不排气（关闭该读数）。任一入参非有限返回 0。</summary>
    public static double VentStrength(double heat, double threshold)
    {
        if (!double.IsFinite(heat) || !double.IsFinite(threshold))
        {
            return 0.0;
        }

        var t = Clamp01(threshold);
        if (t >= 1.0)
        {
            return 0.0;
        }

        return Clamp01((Clamp01(heat) - t) / (1.0 - t));
    }

    /// <summary>翼尖涡流强度（0..1）：取横向加速度占比的**绝对值**——左右急转都要出涡流，
    /// 写成有符号会让一个转向永远不出现。低于 <paramref name="threshold"/> 为 0（直线巡航
    /// 不出涡流，涡流是机动读数而非常驻装饰），之上线性升到满并钳制（超速机动不放大）。
    /// threshold ≥ 1 表示永不出现。任一入参非有限返回 0。</summary>
    public static double VortexStrength(double accel01, double threshold)
    {
        if (!double.IsFinite(accel01) || !double.IsFinite(threshold))
        {
            return 0.0;
        }

        var t = Clamp01(threshold);
        if (t >= 1.0)
        {
            return 0.0;
        }

        return Clamp01((Math.Abs(accel01) - t) / (1.0 - t));
    }

    /// <summary>炮管热度着色双因子（各 0..1）：前半段钢灰→琥珀（Amber），后半段琥珀→白热（White）。
    /// 拆成两个因子是为了让消费方只做两次 Color.Lerp（struct 运算，零托管分配），
    /// 而不是每帧构造色阶表。热 ≤ 0 或非有限返回 (0,0) ＝ 冷态钢灰。</summary>
    public static (double Amber, double White) HeatTintMix(double heat)
    {
        if (!double.IsFinite(heat) || heat <= 0.0)
        {
            return (0.0, 0.0);
        }

        var h = Clamp01(heat);
        return (Clamp01(h * 2.0), Clamp01(h * 2.0 - 1.0));
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);
}
