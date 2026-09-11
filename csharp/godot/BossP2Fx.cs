using Godot;

namespace InfiAir;

/// <summary>
/// Boss P2 变身演出（纯视觉，玩法判定零改动；Boss.EnterPhase(P2) 接入）。
/// Shatter：P1→P2 切换瞬间装甲碎片炸散一圈——一次性 Polygon2D 飞散（Tween 驱动播完自毁，
/// 不建池；Explosion 的碎片为其池化实例私有机制，不可独立复用，故就地实现，数量上限 16）。
/// BossP2Halo：P2 期间持续光环粒子——低频环绕软点（additive），Boss 子节点随其离场自动释放。
/// 数值单源 data/balance.json effects.boss_p2_transform.*。
/// </summary>
public static class BossP2Fx
{
    // 碎片配色：暗钢紫晶舰体片 + 品红断面（敌侧阵营色，与 Boss 背光轮廓同族）
    private static readonly Color ShardHullA = new(0.30f, 0.26f, 0.44f);
    private static readonly Color ShardHullB = new(0.48f, 0.40f, 0.66f);
    private static readonly Color ShardEdge = new(1.0f, 0.42f, 0.72f);

    /// <summary>装甲碎片炸散一圈：均角飞出 + 自旋 + 淡出（Quad 缓出），中心叠一次性 additive 能量闪。</summary>
    public static void Shatter(Node parent, Vector2 center, float ws)
    {
        var count = CfgFx.Int("effects.boss_p2_transform.shard_count", 10, 4, 16);
        var speed = CfgFx.Float("effects.boss_p2_transform.shard_speed", 320.0f, 0.0f) * ws;
        var life = CfgFx.Float("effects.boss_p2_transform.shard_life", 0.8f, CfgFx.IntervalFloor);
        var size = CfgFx.Float("effects.boss_p2_transform.shard_size", 9.0f, 1.0f) * ws;
        for (var i = 0; i < count; i++)
        {
            var dir = Vector2.Right.Rotated(Mathf.Tau * i / count + GD.Randf() * 0.3f);
            var shard = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(size, 0.0f),
                    new Vector2(-size * 0.6f, size * 0.7f),
                    new Vector2(-size * 0.4f, -size * 0.6f),
                },
                Color = i % 3 == 2 ? ShardEdge : (i % 2 == 0 ? ShardHullA : ShardHullB),
                Position = center + dir * 40.0f * ws,
                Rotation = (float)GD.RandRange(0.0, Mathf.Tau),
            };
            parent.AddChild(shard);
            var tween = shard.CreateTween().SetParallel(true);
            tween.TweenProperty(shard, "position", shard.Position + dir * speed * life, life)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(shard, "rotation", shard.Rotation + (float)GD.RandRange(-4.0, 4.0), life);
            tween.TweenProperty(shard, "modulate:a", 0.0, life)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            tween.Chain().TweenCallback(Callable.From(shard.QueueFree));
        }

        // 中心能量闪：additive 软点爆闪 0.3s 扩张淡出（品红，呼应狂暴配色方向）
        var flash = CinematicFx.SoftGlow(150.0f * ws, new Color(1.0f, 0.5f, 0.8f, 0.85f));
        flash.Position = center;
        parent.AddChild(flash);
        var flashTween = flash.CreateTween().SetParallel(true);
        flashTween.TweenProperty(flash, "scale", flash.Scale * 1.8f, 0.3)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        flashTween.TweenProperty(flash, "modulate:a", 0.0, 0.3)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        flashTween.Chain().TweenCallback(Callable.From(flash.QueueFree));
    }
}

/// <summary>P2 持续光环：count 枚软点均角环绕（additive），容器低速自转 + 整体 alpha 低频呼吸；
/// Setup 后自驱动，零每帧分配（只写 Rotation/Modulate）。</summary>
public partial class BossP2Halo : Node2D
{
    private float _speed = 0.5f;
    private float _phase;

    public void Setup(int count, float radius, float speed, float size, Color color, float alpha)
    {
        _speed = speed;
        for (var i = 0; i < count; i++)
        {
            var a = Mathf.Tau * i / Mathf.Max(count, 1);
            var dot = CinematicFx.SoftGlow(size, new Color(color, alpha));
            dot.Position = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            AddChild(dot);
        }
    }

    public override void _Process(double delta)
    {
        Rotation += _speed * (float)delta;
        _phase += (float)delta;
        var m = Modulate;
        m.A = 0.75f + 0.25f * Mathf.Sin(_phase * 2.4f);
        Modulate = m;
    }
}
