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

    /// <summary>运动滞后单轴目标偏移（px）：accel01 是该轴加速度 ÷ 参考上限（-1..1，超界钳制），
    /// 贴图朝**加速度反方向**漂移（加速时被甩在后面）——符号取负。非有限入参或 maxPx ≤ 0 返回 0
    /// （这一帧不漂）。逐轴调用；随后的时间平滑由消费方走 <see cref="Approach"/>。</summary>
    public static double LagTargetPx(double accel01, double maxPx)
    {
        if (!double.IsFinite(accel01) || !double.IsFinite(maxPx) || maxPx <= 0.0)
        {
            return 0.0;
        }

        return -Math.Clamp(accel01, -1.0, 1.0) * maxPx;
    }

    /// <summary>转向跟随角（rad，贴图本地）：贴图旋转相对瞄准角的可回弹角惯性——accumulate
    /// 本帧转向量再指数衰减（rate 是每秒保留比例的指数系数），钳 ±maxRad。瞄准甩动时贴图短暂
    /// 落后再追上；瞄准静止时逐帧衰减回 0（贴图追平机体）。delta ≤ 0、rate/maxRad 非有限时
    /// 保持现状（maxRad 非有限时免钳——Clamp 的 NaN 界会抛）；current 非有限自愈为 0 起步；
    /// turnDelta 非有限按 0（只衰减不污染）。</summary>
    public static double SwayAfter(double current, double turnDelta, double maxRad, double rate, double delta)
    {
        if (!double.IsFinite(rate) || !double.IsFinite(delta) || delta <= 0.0 || !double.IsFinite(maxRad))
        {
            if (!double.IsFinite(current))
            {
                return 0.0;
            }

            return double.IsFinite(maxRad) ? Math.Clamp(current, -maxRad, maxRad) : current;
        }

        var c = double.IsFinite(current) ? current : 0.0;
        var t = double.IsFinite(turnDelta) ? turnDelta : 0.0;
        var next = (c + t) * Math.Exp(-Math.Max(rate, 0.0) * delta);
        return Math.Clamp(next, -maxRad, maxRad);
    }

    /// <summary>冲刺弹跳缩放系数（0..1 倍增）：elapsed ∈ [0, time) 内走抛物线过冲
    /// 1 + amp·4t(1-t)（t=0.5 处峰值 1+amp，两端 1）——「过冲再回拉」的一次性入出场；
    /// elapsed 出窗、time/amp ≤ 0 或任一非有限返回 1（不缩放）。</summary>
    public static double PopScale(double elapsedSeconds, double time, double amp)
    {
        if (!double.IsFinite(elapsedSeconds) || !double.IsFinite(time) || !double.IsFinite(amp)
            || time <= 0.0 || amp <= 0.0 || elapsedSeconds <= 0.0 || elapsedSeconds >= time)
        {
            return 1.0;
        }

        var t = elapsedSeconds / time;
        return 1.0 + amp * 4.0 * t * (1.0 - t);
    }

    /// <summary>悬停浮动偏移（px）：simTime 秒相位上的慢速正弦沉浮（引擎悬浮感；频率 hz ≤ 0
    /// 或任一入参非有限返回 0——「这一帧不动」）。</summary>
    public static double BobOffsetPx(double simTimeSeconds, double hz, double ampPx)
    {
        if (!double.IsFinite(simTimeSeconds) || !double.IsFinite(hz) || !double.IsFinite(ampPx) || hz <= 0.0)
        {
            return 0.0;
        }

        return ampPx * Math.Sin(2.0 * Math.PI * hz * simTimeSeconds);
    }

    /// <summary>速度伸缩系数（正=沿机头拉伸、负=压缩）：forwardSpeed01 是速度在机头方向的投影
    /// ÷ 满速（-1..1 钳制，加速档超速按满强度），乘 maxStretch 得轴向形变量。maxStretch ≤ 0 或
    /// 任一入参非有限返回 0（无形变）。交叉轴补偿走 <see cref="CounterScale"/>。</summary>
    public static double StretchFactor(double forwardSpeed01, double maxStretch)
    {
        if (!double.IsFinite(forwardSpeed01) || !double.IsFinite(maxStretch) || maxStretch <= 0.0)
        {
            return 0.0;
        }

        return Math.Clamp(forwardSpeed01, -1.0, 1.0) * maxStretch;
    }

    /// <summary>体积补偿的交叉轴缩放（1 - deform×ratio）：轴向拉伸时交叉轴按 ratio 收缩，
    /// 形变体积近似守恒（0.5 = 面积守恒；1.0 = 全补偿）。非有限入参或 ratio ≤ 0 返回 1
    /// （不补偿）。</summary>
    public static double CounterScale(double deform01, double ratio)
    {
        if (!double.IsFinite(deform01) || !double.IsFinite(ratio) || ratio <= 0.0)
        {
            return 1.0;
        }

        return 1.0 - deform01 * ratio;
    }

    /// <summary>引擎喘振系数（-1..1，**确定性**）：两个不可通约频率（1 : 1.37）的正弦叠加，
    /// 相位只取模拟时间——无随机源，无头固定步长下可重复（§4 确定性硬规则）。消费方把它
    /// 映射成「幅度 ±amp 的不规则抖动」（损伤状态引擎失稳的读数）。hz ≤ 0 或非有限返回 0。</summary>
    public static double SputterFactor(double simTimeSeconds, double hz)
    {
        if (!double.IsFinite(simTimeSeconds) || !double.IsFinite(hz) || hz <= 0.0)
        {
            return 0.0;
        }

        var w = 2.0 * Math.PI * hz * simTimeSeconds;
        return 0.5 * (Math.Sin(w) + Math.Sin(w * 1.37));
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
