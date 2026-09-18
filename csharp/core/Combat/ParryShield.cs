namespace InfiAir.Core.Combat;

/// <summary>
/// 弧光弹反盾的几何判据（纯逻辑，零 Godot 依赖）：盾心在玩家、朝向机头前方的扇形，
/// 命中条件＝「弹心到盾心的距离 ≤ 半径」**且**「与机头方向的夹角 ≤ 半角」（整角口径见 <see cref="AimCone"/>）。
///
/// 为什么单独成类：这条判据原先就地写在 `Player.OnParryShieldEntered` 里，只能靠引擎侧探针覆盖，
/// 而它有两个「差一点就错」的边界，单测才钉得住——半径上取等号（恰在半径上的弹算命中）、
/// 弧边取等号（恰在半角上的弹算命中）。这两处反过来写不产生任何运行信号，只表现为
/// 「贴脸弹反偶尔不生效」。
///
/// 与引擎侧的对应关系：两条调用路径（`area_entered` 事件与有效窗口内的逐帧扫描）共用本判据，
/// 语义不得各写一份。逐帧扫描的存在理由不在这里（那是引擎事件投递时机的问题，
/// 见 `Player._PhysicsProcess` 的盾判定注释），但两条路径判的必须是同一件事。
/// 取值一律由引擎侧从 balance 注入，core 不产生数值。
/// </summary>
public static class ParryShield
{
    /// <summary>相对位移 (dx, dy) 是否落在盾的扇形内。
    /// <paramref name="noseAngleRad"/> 为机头方向弧度（含机身 Rotation；引擎侧由
    /// <c>Vector2.Up.Rotated(Rotation).Angle()</c> 给出）。</summary>
    public static bool Covers(float dx, float dy, float noseAngleRad, float radius, float arcDeg)
    {
        // 距离与角度都按 float 逐位复刻引擎侧口径（Mathf.Sqrt / Mathf.Atan2）：本判据是阈值判定，
        // 用 double 中间量会在「恰好落在半径上」这类边界上与引擎侧差 1 ulp，把等号两侧换边。
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > radius)
        {
            return false;
        }

        var arcHalf = AimCone.HalfAngleRadFromFullAngleDeg(arcDeg);
        return MathF.Abs(AngleDifference(MathF.Atan2(dy, dx), noseAngleRad)) <= arcHalf;
    }

    /// <summary>角度差归一到 (-π, π]（逐位复刻 <c>Mathf.AngleDifference</c> 的
    /// <c>Wrap(from - to, -π, π)</c> 实现）——照着 ±π 处的换边行为写，调用方才不用自带容差。</summary>
    private static float AngleDifference(float from, float to)
    {
        var delta = from - to;
        return delta - (2.0f * MathF.PI) * MathF.Floor((delta + MathF.PI) / (2.0f * MathF.PI));
    }
}
