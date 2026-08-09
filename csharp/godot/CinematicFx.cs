using Godot;
using System.Collections.Generic;

namespace InfiAir;

/// <summary>
/// 过场/演出共享特效工具：软径向光晕、带纹理粒子、冲击波环、分层能量束、速度线/放射线场。
/// 供 intro_cinematic / return_cinematic / mothership_summon_window / warp_gate / mothership 复用，
/// 替代此前在多处复制的硬边 GlowDot 与无纹理粒子工厂；全部零依赖、代码程序化构建。
/// M6 全量迁移（2026-08-08 自 scripts/cinematic_fx.gd）：RefCounted + 全静态工厂。
/// 注：原 GDScript static var 缓存贴图/材质（G022 共享优化）——C# 静态字段禁止持有 Godot 对象
/// （引擎退出 finalize segfault 实测根因，M1-M6 批次规则 19），改每次构建（UITheme.Font 同款处理）；
/// 内容确定性一致，仅多次构建时重复生成。
/// GDScript 调用方（M6 过场/测试）经脚本资源调 snake 桥；C# 调用方（BossAttacks/Enemy/Mothership
/// 过渡期 GD.Load&lt;GDScript&gt; 动态调）接线后改 typed——公开方法名保留（PascalCase + snake 桥）。
/// </summary>
public partial class CinematicFx : RefCounted
{
    public const int SoftTexSize = 64;

    public const int SOFT_TEX_SIZE = SoftTexSize; // UPPER_SNAKE 兼容（过渡期接线/旧名引用）

    public const int ParticleAmountCap = 96; // 硬性上限：每发射器 ≤96（性能预算：总存活 ≤400）

    public const int PARTICLE_AMOUNT_CAP = ParticleAmountCap; // UPPER_SNAKE 兼容

    /// <summary>静态缓存的 64×64 径向渐变软点贴图（白色，alpha pow 衰减）：
    /// 粒子与光晕共用，消除硬边实心圆的廉价感；颜色经 modulate/process_material 乘算。
    /// M6 迁移注：原 GDScript static var 缓存——按批次规则每次构建（内容确定性一致）。</summary>
    public static ImageTexture SoftTexture()
    {
        var img = Image.CreateEmpty(SoftTexSize, SoftTexSize, false, Image.Format.Rgba8);
        var half = SoftTexSize * 0.5f;
        for (var y = 0; y < SoftTexSize; y++)
        {
            for (var x = 0; x < SoftTexSize; x++)
            {
                var d = new Vector2(x + 0.5f - half, y + 0.5f - half).Length() / half;
                img.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, Mathf.Pow(Mathf.Clamp(1.0f - d, 0.0f, 1.0f), 2.2f)));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>软径向光晕：Sprite2D 承载软点贴图，scale/modulate 语义与旧 GlowDot 一致（可直接 tween）。
    /// G022：additive material 共享（N 机 N 份相同材质 → 1 份，材质只读属性无实例差异）——
    /// M6 迁移注：原 static var 缓存改为每次新建（退出 segfault 规则），语义等价。</summary>
    public static CanvasItemMaterial AdditiveMaterial()
    {
        return new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    }

    public static Sprite2D SoftGlow(float radius, Color color) => SoftGlow(radius, color, true);

    public static Sprite2D SoftGlow(float radius, Color color, bool additive)
    {
        var s = new Sprite2D
        {
            Texture = SoftTexture(),
            Scale = Vector2.One * (radius / (SoftTexSize * 0.5f)),
            Modulate = color,
        };
        if (additive)
        {
            s.Material = AdditiveMaterial();
        }

        return s;
    }

    /// <summary>与既有 _particles(cfg) 同契约的粒子工厂（键：amount/lifetime/explosiveness/one_shot/
    /// direction/spread/vel_min/vel_max/gravity/damping_min/damping_max/scale_min/scale_max/color/additive）。
    /// 默认挂软点贴图（"textured": false 关闭）；cfg 的 scale 语义保持"像素直径"，内部换算到 64px 贴图。</summary>
    public static GpuParticles2D Particles(Godot.Collections.Dictionary cfg)
    {
        var p = new GpuParticles2D
        {
            Amount = Mathf.Min(CfgInt(cfg, "amount", 32), ParticleAmountCap),
            Lifetime = CfgFloat(cfg, "lifetime", 1.0f),
            Explosiveness = CfgFloat(cfg, "explosiveness", 0.0f),
            OneShot = CfgBool(cfg, "one_shot", false),
        };
        var mat = new ParticleProcessMaterial
        {
            Direction = CfgVector3(cfg, "direction", new Vector3(0.0f, -1.0f, 0.0f)),
            Spread = CfgFloat(cfg, "spread", 180.0f),
            InitialVelocityMin = CfgFloat(cfg, "vel_min", 100.0f),
            InitialVelocityMax = CfgFloat(cfg, "vel_max", 200.0f),
            Gravity = CfgVector3(cfg, "gravity", Vector3.Zero),
            DampingMin = CfgFloat(cfg, "damping_min", 0.0f),
            DampingMax = CfgFloat(cfg, "damping_max", 0.0f),
        };
        var texScale = 1.0f;
        if (CfgBool(cfg, "textured", true))
        {
            p.Texture = SoftTexture();
            texScale = 1.0f / SoftTexSize;
        }

        mat.ScaleMin = CfgFloat(cfg, "scale_min", 2.0f) * texScale;
        mat.ScaleMax = CfgFloat(cfg, "scale_max", 4.0f) * texScale;
        mat.Color = CfgColor(cfg, "color", new Color(1.0f, 0.6f, 0.15f));
        p.ProcessMaterial = mat;
        if (CfgBool(cfg, "additive", true))
        {
            p.Material = AdditiveMaterial();
        }

        return p;
    }

    /// <summary>闭合椭圆环点集（Line2D 用；ry_ratio 压扁做透视门洞/光圈）。
    /// M6 迁移注：PackedVector2Array → Vector2[]（批次规则 9，互操作语义一致）。</summary>
    public static Vector2[] RingPoints(int n, float r) => RingPoints(n, r, 1.0f);

    public static Vector2[] RingPoints(int n, float r, float ryRatio)
    {
        // n<=1 无闭合环可言（n=0 读未写元素、n=1 退化单点自环），直接返回空集
        if (n <= 1)
        {
            return System.Array.Empty<Vector2>();
        }

        var pts = new Vector2[n + 1];
        for (var i = 0; i < n; i++)
        {
            var a = Mathf.Tau * i / n;
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * ryRatio) * r;
        }

        pts[n] = pts[0];
        return pts;
    }

    /// <summary>cfg 构造 Shockwave（add_child 后自动播放并自毁）。</summary>
    public static CinematicFxShockwave Shockwave(Godot.Collections.Dictionary cfg)
    {
        var sw = new CinematicFxShockwave
        {
            Radius = CfgFloat(cfg, "radius", 300.0f),
            Time = CfgFloat(cfg, "time", 0.6f),
            Color = CfgColor(cfg, "color", new Color(1.0f, 0.7f, 0.3f, 0.5f)),
            CoreColor = CfgColor(cfg, "core_color", new Color(1.0f, 0.95f, 0.8f, 0.9f)),
            Width = CfgFloat(cfg, "width", 12.0f),
            RyRatio = CfgFloat(cfg, "ry_ratio", 1.0f),
            Fill = CfgBool(cfg, "fill", false),
            StartScale = CfgFloat(cfg, "start_scale", 0.15f),
        };
        return sw;
    }

    /// <summary>cfg 构造 BeamFlow（需在 add_child 前调用，setup 在内部完成建层）。</summary>
    public static CinematicFxBeamFlow Beam(Vector2[] points) => Beam(points, new Godot.Collections.Dictionary());

    public static CinematicFxBeamFlow Beam(Vector2[] points, Godot.Collections.Dictionary cfg)
    {
        var b = new CinematicFxBeamFlow();
        b.Setup(points, cfg);
        return b;
    }

    public static CinematicFxRadialStreaks RadialStreaks(Godot.Collections.Dictionary cfg)
    {
        var r = new CinematicFxRadialStreaks();
        r.Setup(cfg);
        return r;
    }

    // ---------------- cfg 字典读取辅助（GDScript Dictionary.get(key, default) 语义） ----------------
    // internal：同文件顶层类（Shockwave/BeamFlow/RadialStreaks）复用，不对外暴露。

    internal static int CfgInt(Godot.Collections.Dictionary cfg, string key, int def)
    {
        return cfg.TryGetValue(key, out var v) ? v.AsInt32() : def;
    }

    internal static float CfgFloat(Godot.Collections.Dictionary cfg, string key, float def)
    {
        return cfg.TryGetValue(key, out var v) ? (float)v.AsDouble() : def;
    }

    internal static bool CfgBool(Godot.Collections.Dictionary cfg, string key, bool def)
    {
        return cfg.TryGetValue(key, out var v) ? v.AsBool() : def;
    }

    internal static Color CfgColor(Godot.Collections.Dictionary cfg, string key, Color def)
    {
        return cfg.TryGetValue(key, out var v) ? v.AsColor() : def;
    }

    internal static Vector3 CfgVector3(Godot.Collections.Dictionary cfg, string key, Vector3 def)
    {
        return cfg.TryGetValue(key, out var v) ? v.AsVector3() : def;
    }

    // ---------------- GDScript 鸭子调用兼容桥（M6 过渡，M7 删除） ----------------
    // 调用方：GDScript 过场经脚本资源 load("res://csharp/godot/CinematicFx.cs") 调静态方法
    // （scripts/main.gd soft_glow/ring_points/additive_material/particles、scripts/warp_gate.gd
    // soft_glow/additive_material/particles、scripts/intro_cinematic.gd particles/soft_glow、
    // scripts/return_cinematic.gd particles/soft_glow/shockwave/radial_streaks/beam）；
    // C# 侧 BossAttacks.cs/Enemy.cs/Mothership.cs 过渡期 GD.Load<GDScript>("cinematic_fx.gd")
    // 动态调 soft_glow/particles/ring_points/additive_material/shockwave（接线后改 typed）。



    public static Sprite2D soft_glow(float radius, Color color) => SoftGlow(radius, color);

    public static Sprite2D soft_glow(float radius, Color color, bool additive) => SoftGlow(radius, color, additive);

    public static GpuParticles2D particles(Godot.Collections.Dictionary cfg) => Particles(cfg);



    public static Node2D shockwave(Godot.Collections.Dictionary cfg) => Shockwave(cfg);

    public static Node2D beam(Vector2[] points) => Beam(points);

    public static Node2D beam(Vector2[] points, Godot.Collections.Dictionary cfg) => Beam(points, cfg);


    public static int GetSoftTexSize() => SoftTexSize;


    public static int GetParticleAmountCap() => ParticleAmountCap;

}

/// <summary>双层扩散冲击环（粗辉光环 + 细亮芯环 + 可选低 alpha 填充盘），_ready 起 tween，播完自毁。
/// 原 GDScript cinematic_fx.gd 内嵌类 Shockwave，迁移为同文件顶层类（C# 源生成器不支持内嵌类）。</summary>
public partial class CinematicFxShockwave : Node2D
{
    public float Radius = 300.0f;

    public float Time = 0.6f;

    public Color Color = new(1.0f, 0.7f, 0.3f, 0.5f);

    public Color CoreColor = new(1.0f, 0.95f, 0.8f, 0.9f);

    public float Width = 12.0f;

    public float RyRatio = 1.0f;

    public bool Fill;

    public float StartScale = 0.15f;

    public override void _Ready()
    {
        var pts = CinematicFx.RingPoints(48, Radius, RyRatio);
        var glow = new Line2D
        {
            Points = pts,
            DefaultColor = Color,
            Width = Width,
            Material = CinematicFx.AdditiveMaterial(),
        };
        AddChild(glow);
        var core = new Line2D
        {
            Points = pts,
            DefaultColor = CoreColor,
            Width = Mathf.Max(Width * 0.3f, 1.5f),
            Material = CinematicFx.AdditiveMaterial(),
        };
        AddChild(core);
        if (Fill)
        {
            var disk = new Polygon2D
            {
                Polygon = pts,
                Color = new Godot.Color(Color.R, Color.G, Color.B, 0.18f),
                Material = CinematicFx.AdditiveMaterial(),
            };
            AddChild(disk);
            var ftw = disk.CreateTween();
            ftw.TweenProperty(disk, "modulate:a", 0.0f, Time * 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        Scale = Vector2.One * StartScale;
        var tw = CreateTween().SetParallel(true);
        tw.TweenProperty(this, "scale", Vector2.One, Time).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(glow, "modulate:a", 0.0f, Time).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.TweenProperty(core, "modulate:a", 0.0f, Time * 0.8f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.Chain().TweenCallback(Callable.From(QueueFree));
    }
}

/// <summary>分层能量束（宽低 alpha 辉光层 + 窄亮芯层）+ 沿线循环流光软点；
/// _process 只推进参数 u 并回写光点 position，零分配。</summary>
public partial class CinematicFxBeamFlow : Node2D
{
    private Vector2[] _samples = System.Array.Empty<Vector2>();
    private readonly List<Sprite2D> _dots = new();
    private float[] _dotU = System.Array.Empty<float>();
    private float _dotSpeed = 0.45f;
    private float _dotDir = 1.0f;

    public void Setup(Vector2[] points, Godot.Collections.Dictionary cfg)
    {
        var color = CinematicFx.CfgColor(cfg, "color", new Color(0.4f, 0.9f, 1.0f));
        var width = CinematicFx.CfgFloat(cfg, "width", 14.0f);
        _samples = Resample(points, 24);
        var glow = new Line2D
        {
            Points = points,
            DefaultColor = new Color(color, 0.25f),
            Width = width,
            Material = CinematicFx.AdditiveMaterial(),
        };
        AddChild(glow);
        var core = new Line2D
        {
            Points = points,
            DefaultColor = new Color(color.Lightened(0.5f), 0.75f),
            Width = Mathf.Max(width * 0.22f, 1.5f),
            Material = CinematicFx.AdditiveMaterial(),
        };
        AddChild(core);
        _dotSpeed = CinematicFx.CfgFloat(cfg, "dot_speed", 0.45f);
        _dotDir = CinematicFx.CfgFloat(cfg, "dot_dir", 1.0f);
        var dotCount = CinematicFx.CfgInt(cfg, "dot_count", 2);
        _dotU = new float[dotCount];
        for (var i = 0; i < dotCount; i++)
        {
            _dotU[i] = (float)i / Mathf.Max(dotCount, 1);
            var dot = CinematicFx.SoftGlow(CinematicFx.CfgFloat(cfg, "dot_radius", 10.0f), CinematicFx.CfgColor(cfg, "dot_color", new Color(0.8f, 1.0f, 1.0f)));
            AddChild(dot);
            _dots.Add(dot);
        }
    }

    private static Vector2[] Resample(Vector2[] points, int n)
    {
        // H20（健壮性审核）：点列 <2 或目标 <2 时直接返回，防负索引/除零
        if (points.Length < 2 || n < 2)
        {
            return System.Array.Empty<Vector2>();
        }

        var outPts = new Vector2[n];
        var segs = points.Length - 1;
        for (var i = 0; i < n; i++)
        {
            var f = (float)i / (n - 1) * segs;
            var idx = Mathf.Min((int)f, segs - 1);
            outPts[i] = points[idx].Lerp(points[idx + 1], f - idx);
        }

        return outPts;
    }

    private Vector2 SampleAt(float u)
    {
        // H20 补全：_resample 对点列 <2 返回空集，此处防空 _samples 负索引越界（-2 越界）
        if (_samples.Length == 0)
        {
            return Vector2.Zero;
        }

        var n = _samples.Length;
        var f = Mathf.Clamp(u, 0.0f, 1.0f) * (n - 1);
        var idx = Mathf.Min((int)f, n - 2);
        return _samples[idx].Lerp(_samples[idx + 1], f - idx);
    }

    public override void _Process(double delta)
    {
        for (var i = 0; i < _dots.Count; i++)
        {
            _dotU[i] = Mathf.PosMod(_dotU[i] + (float)delta * _dotSpeed * _dotDir, 1.0f);
            _dots[i].Position = SampleAt(_dotU[i]);
        }
    }
}

/// <summary>径向放射条纹场（跃迁隧道用）：软点贴图拉伸成条，从中心向外生长-淡出循环；
/// _process 仅改写 position/scale/modulate，零分配。</summary>
public partial class CinematicFxRadialStreaks : Node2D
{
    private readonly List<Sprite2D> _streaks = new();
    private float[] _angles = System.Array.Empty<float>();
    private float[] _progress = System.Array.Empty<float>();
    private float[] _rates = System.Array.Empty<float>();
    private float _maxRadius = 900.0f;
    private Color _color = new(0.6f, 0.85f, 1.0f, 0.5f);

    public void Setup(Godot.Collections.Dictionary cfg)
    {
        var count = CinematicFx.CfgInt(cfg, "count", 28);
        _maxRadius = CinematicFx.CfgFloat(cfg, "max_radius", 900.0f);
        _color = CinematicFx.CfgColor(cfg, "color", new Color(0.6f, 0.85f, 1.0f, 0.5f));
        var cycle = CinematicFx.CfgFloat(cfg, "cycle", 1.2f);
        _angles = new float[count];
        _progress = new float[count];
        _rates = new float[count];
        for (var i = 0; i < count; i++)
        {
            _angles[i] = GD.Randf() * Mathf.Tau;
            _progress[i] = GD.Randf();
            _rates[i] = (float)GD.RandRange(0.8, 1.3) / cycle;
            var s = CinematicFx.SoftGlow(32.0f, _color);
            s.Rotation = _angles[i];
            AddChild(s);
            _streaks.Add(s);
        }
    }

    public override void _Process(double delta)
    {
        for (var i = 0; i < _streaks.Count; i++)
        {
            _progress[i] = Mathf.PosMod(_progress[i] + (float)delta * _rates[i], 1.0f);
            var p = _progress[i];
            var rHead = p * _maxRadius;
            var rTail = Mathf.Max(0.0f, p - 0.3f) * _maxRadius;
            var lineLen = rHead - rTail;
            var s = _streaks[i];
            if (lineLen < 2.0f)
            {
                var mod = s.Modulate;
                mod.A = 0.0f;
                s.Modulate = mod;
                continue;
            }

            var dir = Vector2.FromAngle(_angles[i]);
            s.Position = dir * ((rHead + rTail) * 0.5f);
            s.Scale = new Vector2(lineLen / 32.0f, 6.0f / 32.0f);
            s.Modulate = new Color(_color.R, _color.G, _color.B, _color.A * Mathf.Sin(Mathf.Pi * p));
        }
    }
}
