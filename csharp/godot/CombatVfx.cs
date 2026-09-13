using Godot;

namespace InfiAir;

/// <summary>
/// 战斗即时视觉（纯表现，零玩法）：击杀环、弹反环、冲刺爆发、直击火花/暴击星芒、
/// Boss 阶段冲击/狂暴爆发。全部为 add_child 后 tween 自毁的一次性节点；只读颜色/world_scale，
/// 不回写任何玩法字段，调用点一律位于玩法判定之后（不影响伤害、时序、位置、概率）。
/// 半径按设计像素给，根节点 Scale = world_scale（与实体同口径）。
/// 软点贴图每段特效只建一张、其内多枚共享（SoftGlow 每次调用都重建 64² 贴图，高频直击下过重）。
/// 轻量特效（直击/弹反/冲刺）经静态在活计数封顶，超限直接跳过——高频直击不会堆节点与 tween。
/// 重特效（击杀环/Boss 阶段）由 VisualFxDirector 侧计数封顶，本类不重复封顶。
/// </summary>
public partial class CombatVfx : RefCounted
{
    /// <summary>轻量特效同时在活上限：满额时跳过整段构建，防高频直击造成节点/tween 抖动。</summary>
    private const int LightCap = 24;

    private const float SparkLife = 0.16f;
    private const float DashLife = 0.34f;
    private const float ParryLife = 0.30f;
    private const float CritLife = 0.30f;

    private static int _lightLive;

    // ---------------- 公开构建口 ----------------

    /// <summary>击杀环：小尺寸琥珀/金环 + 一闪，压在原爆炸之下不抢主爆炸。
    /// elite 走更金更亮的配色；reduce_flash 时去掉白热闪只留暗环。</summary>
    public static Node2D? KillRing(Node parent, Vector2 pos, bool elite, bool reduceFlash)
    {
        var root = NewRoot(parent, pos);
        if (root == null)
        {
            return null;
        }

        var gold = elite ? UITheme.AccentGold : UITheme.Accent;
        var ringAlpha = reduceFlash ? 0.26f : (elite ? 0.60f : 0.52f);
        AddRing(
            root,
            elite ? 128.0f : 82.0f,
            elite ? 0.38f : 0.32f,
            new Color(gold, ringAlpha),
            new Color(UITheme.HoloPale, reduceFlash ? 0.35f : 0.85f),
            elite ? 3.6f : 3.0f,
            0.55f,
            0.20f);
        if (!reduceFlash)
        {
            AddFlash(root, SoftTex(), elite ? 40.0f : 28.0f, new Color(UITheme.AccentHot, elite ? 0.85f : 0.7f));
        }

        FadeAndFree(root, elite ? 0.46f : 0.40f);
        return root;
    }

    /// <summary>弹反成功环：快亮金环 + 数枚外飞碎片（玩家机位）；reduce_flash 时压暗环、去闪。</summary>
    public static Node2D? ParryRing(Node parent, Vector2 pos, bool reduceFlash)
    {
        if (!TryAcquireLight())
        {
            return null;
        }

        var root = NewRoot(parent, pos);
        if (root == null)
        {
            ReleaseLight();
            return null;
        }

        HoldLight(root);
        var tex = SoftTex();
        AddRing(
            root,
            54.0f,
            0.22f,
            new Color(UITheme.AccentHot, reduceFlash ? 0.35f : 0.9f),
            new Color(UITheme.HoloPale, reduceFlash ? 0.3f : 1.0f),
            3.2f,
            1.0f,
            0.28f);
        if (!reduceFlash)
        {
            AddFlash(root, tex, 22.0f, new Color(UITheme.HoloPale, 0.8f));
        }

        const int shardCount = 6;
        for (var i = 0; i < shardCount; i++)
        {
            var dir = Vector2.Right.Rotated(Mathf.Tau * i / shardCount);
            AddShard(root, tex, dir, 7.0f, 48.0f, new Color(UITheme.AccentGold, 0.9f), 2.4f, ParryLife);
        }

        FadeAndFree(root, ParryLife);
        return root;
    }

    /// <summary>冲刺爆发：沿冲刺方向压扁的发射环 + 反向拖尾碎片，补足残影之外的一次性读感。</summary>
    public static Node2D? DashBurst(Node parent, Vector2 pos, Vector2 dir, bool reduceFlash)
    {
        if (dir == Vector2.Zero)
        {
            dir = Vector2.Up;
        }

        if (!TryAcquireLight())
        {
            return null;
        }

        var root = NewRoot(parent, pos);
        if (root == null)
        {
            ReleaseLight();
            return null;
        }

        HoldLight(root);
        var tex = SoftTex();
        // 根节点对齐冲刺方向：局部 +x 即前进方向，环压成沿运动轴的椭圆
        root.Rotation = dir.Angle();
        AddRing(
            root,
            68.0f,
            0.26f,
            new Color(UITheme.AccentGold, reduceFlash ? 0.28f : 0.6f),
            new Color(UITheme.HoloPale, reduceFlash ? 0.25f : 0.8f),
            3.0f,
            0.42f,
            0.30f);
        if (!reduceFlash)
        {
            AddFlash(root, tex, 20.0f, new Color(UITheme.AccentGold, 0.65f));
        }

        const int shardCount = 5;
        for (var i = 0; i < shardCount; i++)
        {
            // 局部 -x = 冲刺反向拖尾；小幅扇开避免整齐
            var dirLocal = Vector2.Left.Rotated((i - (shardCount - 1) * 0.5f) * 0.22f);
            AddShard(root, tex, dirLocal, 6.0f, 34.0f, new Color(UITheme.Accent, 0.8f), 2.6f, DashLife);
        }

        FadeAndFree(root, DashLife);
        return root;
    }

    /// <summary>直击观感总入口（Bullet 直击路径单行调用）：每次直击一枚小火花；
    /// crit 为真时另加一层更亮星芒。位置取命中目标位（弹体回收后自身坐标已复位）。</summary>
    public static void DirectHit(Node parent, Vector2 pos, Vector2 normal, bool crit, bool reduceFlash)
    {
        ImpactSpark(parent, pos, normal, reduceFlash);
        if (crit)
        {
            CritBurst(parent, pos, reduceFlash);
        }
    }

    /// <summary>非致命直击火花：数枚小亮片逆来袭方向溅开（此前直击无反馈）。</summary>
    public static Node2D? ImpactSpark(Node parent, Vector2 pos, Vector2 normal, bool reduceFlash)
    {
        if (!TryAcquireLight())
        {
            return null;
        }

        var root = NewRoot(parent, pos);
        if (root == null)
        {
            ReleaseLight();
            return null;
        }

        HoldLight(root);
        var tex = SoftTex();
        if (normal == Vector2.Zero)
        {
            normal = Vector2.Up;
        }

        var baseAngle = normal.Angle();
        const int shardCount = 3;
        for (var i = 0; i < shardCount; i++)
        {
            // 火花集中在来袭反方向 ±60° 锥内（溅回来源侧）
            var spread = (i - (shardCount - 1) * 0.5f) * 0.6f;
            var dir = Vector2.Right.Rotated(baseAngle + spread);
            AddShard(root, tex, dir, 5.0f, 30.0f, new Color(UITheme.AccentHot, reduceFlash ? 0.4f : 0.92f), 2.2f, SparkLife);
        }

        if (!reduceFlash)
        {
            AddFlash(root, tex, 12.0f, new Color(UITheme.HoloPale, 0.6f));
        }

        FadeAndFree(root, SparkLife);
        return root;
    }

    /// <summary>暴击星芒：八向放射条 + 亮芯，比普通火花更亮更大、更易读。</summary>
    public static Node2D? CritBurst(Node parent, Vector2 pos, bool reduceFlash)
    {
        if (!TryAcquireLight())
        {
            return null;
        }

        var root = NewRoot(parent, pos);
        if (root == null)
        {
            ReleaseLight();
            return null;
        }

        HoldLight(root);
        const int spokeCount = 8;
        var color = new Color(UITheme.AccentHot, reduceFlash ? 0.4f : 0.95f);
        for (var i = 0; i < spokeCount; i++)
        {
            var a = Mathf.Tau * i / spokeCount;
            var dir = Vector2.Right.Rotated(a);
            var line = CinematicFx.Line(
                new[] { dir * 4.0f, dir * (i % 2 == 0 ? 46.0f : 30.0f) }, color, i % 2 == 0 ? 3.0f : 1.8f);
            root.AddChild(line);
        }

        var ws = Ws();
        root.Scale = Vector2.One * (ws * 0.35f);
        root.CreateTween().TweenProperty(root, "scale", Vector2.One * (ws * 1.05f), CritLife)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        if (!reduceFlash)
        {
            AddFlash(root, SoftTex(), 26.0f, new Color(UITheme.HoloPale, 0.85f));
        }

        FadeAndFree(root, CritLife);
        return root;
    }

    /// <summary>Boss 常规阶段转场：大范围竞技场冲击环 + 辉光盘（弱于狂暴）。</summary>
    public static Node2D? PhaseShockwave(Node parent, Vector2 pos, bool reduceFlash)
    {
        var root = NewRoot(parent, pos);
        if (root == null)
        {
            return null;
        }

        AddRing(
            root,
            430.0f,
            0.72f,
            new Color(UITheme.Accent, reduceFlash ? 0.22f : 0.5f),
            new Color(UITheme.HoloPale, reduceFlash ? 0.25f : 0.85f),
            reduceFlash ? 5.0f : 10.0f,
            0.6f,
            0.12f);
        if (!reduceFlash)
        {
            AddFlash(root, SoftTex(), 96.0f, new Color(UITheme.HoloPale, 0.55f));
        }

        FadeAndFree(root, 0.82f);
        return root;
    }

    /// <summary>Boss 狂暴爆发：比阶段转场更强的红金双环 + 放射能量条 + 大辉光。</summary>
    public static Node2D? EnrageBurst(Node parent, Vector2 pos, bool reduceFlash)
    {
        var root = NewRoot(parent, pos);
        if (root == null)
        {
            return null;
        }

        AddRing(
            root,
            540.0f,
            0.9f,
            new Color(UITheme.Danger, reduceFlash ? 0.25f : 0.6f),
            new Color(UITheme.AccentHot, reduceFlash ? 0.3f : 0.9f),
            reduceFlash ? 6.0f : 12.0f,
            0.62f,
            0.1f);
        AddRing(
            root,
            300.0f,
            0.55f,
            new Color(UITheme.AccentGold, reduceFlash ? 0.2f : 0.5f),
            new Color(UITheme.HoloPale, reduceFlash ? 0.2f : 0.8f),
            5.0f,
            0.62f,
            0.2f);

        const int streakCount = 14;
        var streakColor = new Color(UITheme.AccentHot, reduceFlash ? 0.2f : 0.55f);
        for (var i = 0; i < streakCount; i++)
        {
            var dir = Vector2.Right.Rotated(Mathf.Tau * i / streakCount);
            root.AddChild(CinematicFx.Line(new[] { Vector2.Zero, dir * 130.0f }, streakColor, 4.0f));
        }

        if (!reduceFlash)
        {
            AddFlash(root, SoftTex(), 150.0f, new Color(UITheme.HoloPale, 0.6f));
        }

        FadeAndFree(root, 1.0f);
        return root;
    }

    // ---------------- 内部构建辅助 ----------------

    /// <summary>轻量特效在活配额（满额即跳过）。</summary>
    private static bool TryAcquireLight()
    {
        if (_lightLive >= LightCap)
        {
            return false;
        }

        _lightLive++;
        return true;
    }

    private static void ReleaseLight() => _lightLive = Mathf.Max(_lightLive - 1, 0);

    /// <summary>节点离树即归还轻量配额（含被外部清场销毁的路径）。</summary>
    private static void HoldLight(Node2D root) => root.TreeExited += ReleaseLight;

    private static float Ws() => (float)GameState.Instance.WorldScale;

    private static ImageTexture SoftTex() => CinematicFx.SoftTexture();

    /// <summary>特效根：挂到 parent（世界坐标容器）下、缩放按 world_scale，位置取世界坐标。</summary>
    private static Node2D? NewRoot(Node? parent, Vector2 pos)
    {
        if (parent == null || !GodotObject.IsInstanceValid(parent))
        {
            return null;
        }

        var root = new Node2D();
        parent.AddChild(root);
        root.Scale = Vector2.One * Ws();
        root.GlobalPosition = pos;
        return root;
    }

    /// <summary>统一收尾：整段淡出 + 到点自毁（子节点随根销毁，不会残留）。</summary>
    private static void FadeAndFree(Node2D root, float life)
    {
        root.CreateTween().TweenProperty(root, "modulate:a", 0.0f, life)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        var free = root.CreateTween();
        free.TweenInterval(life + 0.03);
        free.TweenCallback(Callable.From(root.QueueFree));
    }

    /// <summary>共享软点 Sprite2D（radius 为软点直径语义，与 CinematicFx.SoftGlow 一致）。</summary>
    private static Sprite2D Point(Texture2D tex, float radius, Color color)
    {
        var s = new Sprite2D
        {
            Texture = tex,
            Scale = Vector2.One * (radius / (CinematicFx.SoftTexSize * 0.5f)),
            Modulate = color,
            Material = CinematicFx.AdditiveMaterial(),
        };
        return s;
    }

    /// <summary>中心辉光：从 0.35× 涨到 1×，透明度交给根淡出。</summary>
    private static void AddFlash(Node2D root, Texture2D tex, float radius, Color color)
    {
        var g = Point(tex, 1.0f, color);
        g.Scale = Vector2.One * (radius * 0.35f / (CinematicFx.SoftTexSize * 0.5f));
        root.AddChild(g);
        g.CreateTween().TweenProperty(g, "scale", Vector2.One * (radius / (CinematicFx.SoftTexSize * 0.5f)), 0.18f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>双层冲击环（CinematicFxShockwave 自播自毁）；rotation 供椭圆沿指定轴压扁。</summary>
    private static void AddRing(
        Node2D root,
        float radius,
        float time,
        Color color,
        Color coreColor,
        float width,
        float ryRatio,
        float startScale,
        float rotation = 0.0f)
    {
        var sw = CinematicFx.Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = radius,
            ["time"] = time,
            ["color"] = color,
            ["core_color"] = coreColor,
            ["width"] = width,
            ["ry_ratio"] = ryRatio,
            ["start_scale"] = startScale,
        });
        sw.Rotation = rotation;
        root.AddChild(sw);
    }

    /// <summary>外飞碎片：软点拉伸成条，沿 dir 飞出一段距离。</summary>
    private static void AddShard(
        Node2D root, Texture2D tex, Vector2 dir, float radius, float dist, Color color, float stretch, float life)
    {
        var half = CinematicFx.SoftTexSize * 0.5f;
        var s = Point(tex, 1.0f, color);
        s.Rotation = dir.Angle();
        s.Scale = new Vector2(radius * stretch / half, radius * 0.9f / half);
        s.Position = dir * (dist * 0.2f);
        root.AddChild(s);
        s.CreateTween().TweenProperty(s, "position", dir * dist, life)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }
}
