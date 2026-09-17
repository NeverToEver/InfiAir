namespace InfiAir.Core.Combat;

/// <summary>
/// 瞄准锥的余弦阈值换算（纯逻辑，零 Godot 依赖）。
///
/// 背景：工程里有两个「锥角」配置键，历史上各自被按**不同口径**解释，且都叫「锥角」——
/// 读代码或调参时按任一侧理解都会把实际辅助强度估错一倍（弱追踪域实际比键面读数宽一倍），
/// 而这类误判不产生任何运行信号。故把口径显式化并单测钉住：
///   - `player.aim_assist.levels.*.cone_angle_deg`（档位 6/8/10）语义＝**半角**（接受域 ±该值）；
///   - `augments.homing.lock_cone_deg`（44）语义＝**整角**（接受域 ±该值/2）。
/// 两个函数的命名即口径声明，调用方不得再就地写 Cos(DegToRad(...)) 自行解释。
///
/// 同族的另两处算式也在此收敛：按**角度**（而非点积）比较的扇形判定与绘制要的是
/// 「整角 → 半角弧度」；锥内强度的归一化内含 `1 - coneCos` 作除数，全向锥（coneCos＝1）
/// 会得 0/0＝NaN。两者原先都在调用点就地写，且后者靠调用端的 NaN 守卫兜底——
/// 表现是弱追踪被静默关掉，而不是报错。
///
/// 取值一律由引擎侧从 balance 注入，core 不产生数值；本类只做「角度 → 阈值/弧度」的换算。
/// </summary>
public static class AimCone
{
    /// <summary>半角口径：cone 值本身就是单边张角（接受域 ±coneDeg）。</summary>
    public static float CosFromHalfAngleDeg(float halfAngleDeg)
    {
        // 逐位等价于引擎侧 Mathf.Cos(Mathf.DegToRad(v))：先按 float 域换算弧度再取余弦
        // （全程 double 会在部分角度上差 1 ulp，进而改变边界上的命中判定）。
        var rad = (float)(halfAngleDeg * (System.Math.PI / 180.0));
        return (float)System.Math.Cos(rad);
    }

    /// <summary>整角口径：cone 值是完整张角（接受域 ±coneDeg/2）。</summary>
    public static float CosFromFullAngleDeg(float fullAngleDeg) =>
        CosFromHalfAngleDeg(fullAngleDeg * 0.5f);

    /// <summary>整角口径的半角弧度：供按角度比较的扇形判定与扇形/扇骨绘制使用（弹反接受域、
    /// 弹反扇面、轮缘分段、高光带各一处）。逐位等价于引擎侧 `Mathf.DegToRad(v) * 0.5f`。</summary>
    public static float HalfAngleRadFromFullAngleDeg(float fullAngleDeg)
    {
        var rad = (float)(fullAngleDeg * (System.Math.PI / 180.0));
        return rad * 0.5f; // 除 2 精确（2 的幂），先换算后取半与先取半后换算同值
    }

    /// <summary>锥内归一化强度：目标与瞄准方向的点积 dot 从锥边界（coneCos）贴边 0 升到正对 1。
    /// 全向锥（coneCos ≥ 1，即半角 360°）恒为满强度——`(dot - 1) / 0` 的 0/0 会得 NaN，
    /// 而 NaN 与 `homingRate &lt;= 0` 的比较恒 false，历史靠调用端 NaN 守卫兜底＝静默关掉弱追踪。
    /// 非有限 coneCos 同按「覆盖全向」处理（与 AimTargeting 的 NaN 不排除口径同族）。</summary>
    public static float ConeStrength(float dot, float coneCos)
    {
        var span = 1.0f - coneCos;
        if (!(span > 0.0f))
        {
            return 1.0f;
        }

        // 与引擎 Mathf.Clamp(v,0,1) 同语义：NaN 双向比较皆 false，故原样穿透（非有限 dot 由调用端兜底）
        var t = (dot - coneCos) / span;
        return t < 0.0f ? 0.0f : (t > 1.0f ? 1.0f : t);
    }
}
