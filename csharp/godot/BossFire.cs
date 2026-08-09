using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// Boss 弹幕发射器（M3 批次迁移，2026-08-08 自 scripts/boss_fire.gd 迁移；docs/AUDIT_VAULT.md A3）。
/// 纯发射逻辑，不持 Boss 状态；位置经 boss 参数、出弹点偏移/机体缩放经注入字段。
/// Boss / BossAttacks / EnrageSequence 共用本发射器，避免跨类私有访问（A1 约束）。
/// 纯 C# 类（原 RefCounted，无信号/导出）：弹池经 BulletPool（C# 类型）类型化发射，
/// </summary>
public partial class BossFire : RefCounted
{
    /// <summary>出弹点偏移（Boss._ready 注入：MUZZLE_OFFSET = 设计值 × world_scale）。</summary>
    public float MuzzleOffset { get; set; }

    /// <summary>全局机体缩放（Boss._ready 注入 _ws），用于随机体特效的偏移缩放。</summary>
    public float WorldScale { get; set; } = 1.0f;

    /// <summary>cross 攻击起始角（随波次进动，BossFire 内维护）。</summary>
    private float _crossAngle;

    /// <summary>面向玩家的方向（player 为空回退 Vector2.DOWN）。</summary>
    public static Vector2 PlayerDir(Node2D from)
    {
        var player = GameState.Instance.PlayerRef;
        if (player != null)
        {
            var p = (Node2D)player;
            var dir = (p.GlobalPosition - from.GlobalPosition).Normalized();
            return dir != Vector2.Zero ? dir : Vector2.Down; // G026：圆心重合时回退
        }

        return Vector2.Down;
    }

    public void FireFan(Node2D boss, int pCount, float speed, int damage)
    {
        var baseDir = PlayerDir(boss);
        var half = (pCount - 1) * 0.5f;
        for (var i = 0; i < pCount; i++)
        {
            var dir = baseDir.Rotated(Mathf.DegToRad(20.0f * (i - half)));
            var b = SpawnBullet(dir, speed, damage);
            if (b == null)
            {
                continue; // P2-3：同屏敌弹硬上限，跳过本次发射（槽位剩余照常）
            }

            b.Position = boss.Position + dir * MuzzleOffset;
        }
    }

    public void FireHoming(Node2D boss, Vector2 pOffset, float speed, int damage)
    {
        var pool = GameState.Instance.BulletPool as BulletPool;
        if (pool == null)
        {
            return;
        }

        var b = pool.Fire(Vector2.Down, speed, damage, false, true, 1.5f);
        if (b == null)
        {
            return; // P2-3：同屏敌弹硬上限
        }

        b.Position = boss.Position + pOffset * WorldScale;
    }

    /// <summary>狙击弹：p_dir 为零向量时自机狙（保留旧语义），否则沿 telegraph 锁定方向。</summary>
    public void FireSniper(Node2D boss, Vector2 pDir, float speed, int damage)
    {
        var dir = pDir != Vector2.Zero ? pDir : PlayerDir(boss);
        var b = SpawnBullet(dir, speed, damage);
        if (b == null)
        {
            return; // P2-3：同屏敌弹硬上限
        }

        b.Position = boss.Position + dir * MuzzleOffset;
    }

    public void FireCross(Node2D boss, float speed, int damage)
    {
        for (var i = 0; i < 4; i++)
        {
            var dir = Vector2.Right.Rotated(_crossAngle + i * Mathf.Pi / 2.0f);
            var b = SpawnBullet(dir, speed, damage);
            if (b == null)
            {
                continue; // P2-3：同屏敌弹硬上限
            }

            b.Position = boss.Position + dir * MuzzleOffset;
        }

        _crossAngle += Mathf.DegToRad(15.0f);
    }

    /// <summary>重弹（蓄力重炮/狂暴齐射/猎杀狙击共用）：高亮加粗外观。</summary>
    public void FireHeavy(Node2D boss, Vector2 pDir, float pSpeed, int pDamage)
    {
        var b = SpawnBullet(pDir, pSpeed, pDamage);
        if (b == null)
        {
            return; // P2-3：同屏敌弹硬上限
        }

        b.Position = boss.Position + pDir * MuzzleOffset;
        var poly = b.SpriteNode(); // C24：缓存引用，不再每次 get_node（Bullet 为 C# 类）
        if (poly != null)
        {
            poly.Scale = new Vector2(2.4f, 2.4f);
            poly.SelfModulate = new Color(1.0f, 0.6f, 0.3f); // P0-3：Sprite2D 无 color，用 self_modulate
        }
    }

    /// <summary>环弹（差异化狂暴各型共用）：meta=enrage_ring（与快照环弹同标记）。</summary>
    public void FireRing(Node2D boss, int pCount, float pSpeed, int pDamage, float pOffset)
    {
        var count = Mathf.Max(2, pCount); // H15：cfg 直读为 0 时 float(i)/float(p_count) 除零 NaN 方向
        for (var i = 0; i < count; i++)
        {
            var dir = Vector2.Right.Rotated(pOffset + Mathf.Tau * i / count);
            var b = SpawnBullet(dir, pSpeed, pDamage);
            if (b == null)
            {
                continue; // P2-3：同屏敌弹硬上限
            }

            b.Position = boss.Position + dir * MuzzleOffset;
            b.SetMeta("bullet_type", new StringName("enrage_ring"));
        }
    }

    /// <summary>快照激光 + 环形慢弹（狂暴进入一次性齐射 / RELEASE 回退路径共用）。</summary>
    public void FireEnrageWave(
        Node2D boss, float laserSpeed, float ringSpeed, int laserDamage, int ringDamage, int laserCount, int ringCount)
    {
        // H15：count 下限钳制——cfg 直读为 0/负时防空齐射、float(i)/float(ring_count) 除零 NaN 方向
        var lasers = Mathf.Max(2, laserCount);
        var rings = Mathf.Max(2, ringCount);
        var aim = PlayerDir(boss);
        var side = aim.Orthogonal();
        for (var i = 0; i < lasers; i++)
        {
            var laser = SpawnBullet(aim, laserSpeed, laserDamage);
            if (laser == null)
            {
                continue; // P2-3：同屏敌弹硬上限
            }

            laser.Position = boss.Position + aim * MuzzleOffset + side * (i - 1.5f) * 44.0f * WorldScale;
            laser.SetMeta("bullet_type", new StringName("laser"));
            // 细长高亮快速弹（与敌机 laser 弹同表现，polygon 尖端朝 +x 即飞行方向）
            var poly = laser.SpriteNode(); // C24：缓存引用，不再每次 get_node（Bullet 为 C# 类）
            if (poly != null)
            {
                poly.Scale = new Vector2(2.2f, 0.55f);
                poly.SelfModulate = new Color(1.0f, 0.85f, 0.35f); // P0-3：Sprite2D 无 color，用 self_modulate
            }
        }

        for (var i = 0; i < rings; i++)
        {
            var dir = Vector2.Right.Rotated(Mathf.Tau * i / rings);
            var b = SpawnBullet(dir, ringSpeed, ringDamage);
            if (b == null)
            {
                continue; // P2-3：同屏敌弹硬上限
            }

            b.Position = boss.Position + dir * MuzzleOffset;
            b.SetMeta("bullet_type", new StringName("enrage_ring"));
        }
    }

    /// <summary>弹幕墙（三型 P2）：arc_deg 度扇形 count 槽位，留 2 个相邻缺口；
    /// 缺口方位避开自机当前方位 ±30°（无可行槽位时退化为离自机最远的槽，保证理论上可躲）。</summary>
    public void FireBulletWall(Node2D boss, int count, float speed, int damage, float arcDeg)
    {
        var slots = Mathf.Max(2, count); // H15：cfg 直读为 0/1 时 float(count-1) 除零 NaN 方向
        var arc = Mathf.DegToRad(arcDeg);
        var baseAngle = Vector2.Down.Angle();
        var toPlayer = PlayerDir(boss).Angle();
        var minGap = Mathf.DegToRad(30.0f);
        float SlotAngle(int i) => baseAngle - arc * 0.5f + arc * i / (slots - 1);
        var candidates = new List<int>();
        for (var g = 0; g < slots - 1; g++)
        {
            if (Mathf.Abs(Mathf.AngleDifference(SlotAngle(g), toPlayer)) > minGap
                && Mathf.Abs(Mathf.AngleDifference(SlotAngle(g + 1), toPlayer)) > minGap)
            {
                candidates.Add(g);
            }
        }

        var gapStart = -1;
        if (candidates.Count == 0)
        {
            var bestDist = -1.0f;
            for (var g = 0; g < slots - 1; g++)
            {
                var d = Mathf.Min(
                    Mathf.Abs(Mathf.AngleDifference(SlotAngle(g), toPlayer)),
                    Mathf.Abs(Mathf.AngleDifference(SlotAngle(g + 1), toPlayer)));
                if (d > bestDist)
                {
                    bestDist = d;
                    gapStart = g;
                }
            }
        }
        else
        {
            gapStart = candidates[(int)(GD.Randi() % (uint)candidates.Count)];
        }

        for (var i = 0; i < slots; i++)
        {
            if (i == gapStart || i == gapStart + 1)
            {
                continue;
            }

            var dir = Vector2.FromAngle(SlotAngle(i));
            var b = SpawnBullet(dir, speed, damage);
            if (b == null)
            {
                continue; // P2-3：同屏敌弹硬上限
            }

            b.Position = boss.Position + dir * MuzzleOffset;
        }
    }

    // ---------------- 内部实现 ----------------

    /// <summary>普通敌弹发射（4 参路径）：同屏硬上限时返回 null（调用方判空跳过）。</summary>
    private static Bullet? SpawnBullet(Vector2 dir, float speed, int damage)
    {
        var pool = GameState.Instance.BulletPool as BulletPool;
        return pool?.Fire(dir, speed, damage, false);
    }

    // V 系列：snake 桥删除（M3 过渡段）——全仓已 typed，无动态调用方；
    // Boss.cs 的 Fire_* 转发桥为测试契约保留。
}
