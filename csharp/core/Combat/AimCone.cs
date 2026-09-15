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
/// 取值一律由引擎侧从 balance 注入，core 不产生数值；本类只做「角度 → 点积阈值」的换算。
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
}
