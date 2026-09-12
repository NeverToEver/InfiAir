namespace InfiAir.Core.Combat;

/// <summary>
/// 弹反反射弹的运动学（纯逻辑，零 Godot 依赖）：弹反初速与逐帧寻敌转向。
/// 引擎侧 <c>FormationBomb</c> 只做适配（读节点位置、写 Velocity），决策留在这一层，才能用单测
/// 钉住「弹反必须咬得住横穿的编队」——原样奉还的初速只比编队横穿速度快一成，追尾永远差一截；
/// 速度余量是决定性参数，转向加速度只决定转弯半径。
/// </summary>
public static class BombReflectKinematics
{
    /// <summary>水平分量保留比例（只反垂直，与 Bullet 弹反同口径：弹体继续沿编队行进方向走）。</summary>
    public const float HorizontalKeep = 0.4f;

    /// <summary>垂直分量反转倍率（上升）。</summary>
    public const float RiseMult = 1.25f;

    /// <summary>寻敌目标视为重合的距离阈值（px）：重合时保持原速度——否则归一化方向退化，
    /// 速度会被 MoveToward 拖向零（弹体悬停）。</summary>
    public const float TargetMergeDistance = 1.0f;

    /// <summary>零向量判定阈值（Godot Vector2.MoveToward 的 CMP_EPSILON 同量级）。</summary>
    private const float Epsilon = 1e-5f;

    /// <summary>弹反初速：水平保留、垂直反向上升，整体乘速度倍率（balance bomb_reflect_speed_mult）。</summary>
    public static (float X, float Y) Launch(float velocityX, float velocityY, float speedMult)
        => (velocityX * HorizontalKeep * speedMult, -MathF.Abs(velocityY) * RiseMult * speedMult);

    /// <summary>单帧寻敌转向（速率不变）：速度朝目标方向转，单帧最多转 accel × dt；
    /// 目标重合或速度为零时原样返回。
    /// 「速率不变」是硬约束：Godot <c>Vector2.MoveToward</c> 走的是两向量之间的弦，
    /// 入弯角度越大掉速越多（实测一次掉头掉四成，追尾就此永远追不上）——转向只改方向。</summary>
    public static (float X, float Y) SteerHome(
        float velocityX, float velocityY, float toTargetX, float toTargetY, float accel, float dt)
    {
        var speed = MathF.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
        var distance = MathF.Sqrt((toTargetX * toTargetX) + (toTargetY * toTargetY));
        if (distance < TargetMergeDistance || speed <= 0.0f)
        {
            return (velocityX, velocityY);
        }

        var desiredX = toTargetX / distance * speed;
        var desiredY = toTargetY / distance * speed;
        var deltaX = desiredX - velocityX;
        var deltaY = desiredY - velocityY;
        var deltaLen = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        var maxStep = MathF.Max(accel, 0.0f) * MathF.Max(dt, 0.0f);
        if (deltaLen <= maxStep || deltaLen < Epsilon)
        {
            return (desiredX, desiredY);
        }

        var k = maxStep / deltaLen;
        var turnedX = velocityX + (deltaX * k);
        var turnedY = velocityY + (deltaY * k);
        var turnedLen = MathF.Sqrt((turnedX * turnedX) + (turnedY * turnedY));
        if (turnedLen < Epsilon)
        {
            return (desiredX, desiredY);
        }

        var scale = speed / turnedLen;
        return (turnedX * scale, turnedY * scale);
    }
}
