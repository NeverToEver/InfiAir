namespace InfiAir.Core.Visual;

/// <summary>
/// 机体姿态算式（纯逻辑，零 Godot 依赖）：横移侧倾（banking）、开火后坐力、受击推挤与
/// 朝向跟随的包络与平滑。生产侧只做取值与适配（读速度 → 投影/换算 → 写贴图变换），
/// 算式与护栏只有这一份。
///
/// 设计约束（<c>DESIGN_BASELINE</c> §2.13）：
///   - **全部只作用于贴图节点**（Sprite2D 的 Rotation/Position），机体根节点与碰撞体不动——
///     姿态是表现层，判定几何（碰撞圆、擦弹环、弹道）与位置语义零改动。
///   - **动效强度**（设置项 fx_intensity）在引擎侧乘上 maxRad/maxPx 后才进本类，本类不感知设置。
///   - 非法入参一律返回 0 或保持现状：NaN 会顺着变换链污染整棵子树（引擎侧零报错），
///     「0」在语义上正好是「这一帧不动」。
/// </summary>
public static class BodyPose
{
    /// <summary>侧倾目标角（rad）：lateralSpeed 是速度在机体右向量上的投影（px/s），
    /// 除以满速得到 -1..1 的强度再乘最大角——超速（加速档）按满强度钳制不越界。
    /// maxSpeed ≤ 0 或任一入参非有限返回 0（无侧倾＝正对机头，是确定姿态）。</summary>
    public static double BankTarget(double lateralSpeed, double maxSpeed, double maxRad)
    {
        if (!double.IsFinite(lateralSpeed) || !double.IsFinite(maxSpeed) || !double.IsFinite(maxRad)
            || maxSpeed <= 0.0)
        {
            return 0.0;
        }

        return Math.Clamp(lateralSpeed / maxSpeed, -1.0, 1.0) * maxRad;
    }

    /// <summary>帧率无关指数逼近：current 向 target 收敛，rate 是每秒保留比例的指数系数
    /// （rate=12 → 每帧保留 exp(-12·delta)，约 80ms 收敛到 63% 差距内）。delta ≤ 0 或
    /// 任一入参非有限时原样返回——「这一帧不动」；current 为 NaN 也返回 target（自愈，
    /// 一次坏值不该永久污染姿态）。</summary>
    public static double Approach(double current, double target, double rate, double delta)
    {
        if (!double.IsFinite(target) || !double.IsFinite(rate) || !double.IsFinite(delta) || delta <= 0.0)
        {
            return double.IsFinite(current) ? current : target;
        }

        if (!double.IsFinite(current))
        {
            return target;
        }

        var k = Math.Exp(-Math.Max(rate, 0.0) * delta);
        return target + (current - target) * k;
    }

    /// <summary>后坐力偏移系数（0..1）：age 距开火的秒数，exp(-age/tau) 指数回位——开火瞬间
    /// 最大、之后快速回中，连发间隔短于 3τ 时表现为持续微沉。age ≥ 3tau 返回 0
    /// （消费方可停写 Position）；tau ≤ 0 或非有限入参返回 0（关掉后坐力）。</summary>
    public static double RecoilFactor(double ageSeconds, double tau)
    {
        if (!double.IsFinite(ageSeconds) || !double.IsFinite(tau) || tau <= 0.0 || ageSeconds < 0.0)
        {
            return 0.0;
        }

        if (ageSeconds >= 3.0 * tau)
        {
            return 0.0;
        }

        var f = Math.Exp(-ageSeconds / tau);
        return double.IsFinite(f) ? f : 0.0;
    }

    /// <summary>受击推挤系数（0..1）：age 距受击的秒数，exp(-age/tau) 同后坐力口径；
    /// 方向与最大位移由消费方相乘。与后坐力分开成两个入口是为了语义可查（一处改口径
    /// 不该顺手改到另一处）。</summary>
    public static double PushFactor(double ageSeconds, double tau) => RecoilFactor(ageSeconds, tau);

    /// <summary>朝向跟随目标角（rad，**贴图本地系**）：贴图机头朝上时，机头指向世界方向
    /// worldAngle 需要「角 + 90°」；rootTurn 是机体根节点累计的自转（敌机/Boss 场景根
    /// rotation = π），贴图本地角要把它减掉。speed01 = 速度/阈值（>1 视为朝向明确），
    /// 低于 1 按比例衰减到 0（悬停/缓慢漂移回正机头，不跟着微幅抖动）。
    /// maxRad 钳制转角上限（避免横穿时的整圈甩头）。任一入参非有限返回 0（回正）。</summary>
    public static double FaceTarget(double worldAngle, double speed01, double rootTurn, double maxRad)
    {
        if (!double.IsFinite(worldAngle) || !double.IsFinite(speed01) || !double.IsFinite(rootTurn)
            || !double.IsFinite(maxRad))
        {
            return 0.0;
        }

        var strength = Math.Clamp(speed01, 0.0, 1.0);
        var angle = worldAngle + Math.PI / 2.0 - rootTurn;
        // 目标角收敛到与当前连续的等价角由消费方的指数逼近完成（贴图当前角连续），这里只出目标。
        return Math.Clamp(NormalizeSigned(angle), -maxRad, maxRad) * strength;
    }

    /// <summary>把任意角规范到 (-π, π]（供钳制上限语义用；消费方若用连续逼近，不必先规范）。</summary>
    private static double NormalizeSigned(double angle)
    {
        var a = angle % (2.0 * Math.PI);
        if (a <= -Math.PI)
        {
            a += 2.0 * Math.PI;
        }
        else if (a > Math.PI)
        {
            a -= 2.0 * Math.PI;
        }

        return a;
    }
}
