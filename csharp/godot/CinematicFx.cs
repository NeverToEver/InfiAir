using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 过场/演出共享特效工具：软径向光晕、带纹理粒子、冲击波环、分层能量束、速度线/放射线场。
/// 供 ReturnCinematic / WarpGate / Mothership / 母舰召唤触发拍复用，
/// 避免多处重复实现硬边 GlowDot 与无纹理粒子工厂；全部零依赖、代码程序化构建。
/// RefCounted + 全静态工厂。
/// 注：C# 静态字段禁止持有 Godot 对象（引擎退出 finalize segfault 实测根因）——需跨调用复用的
/// 贴图缓存在 GameState autoload 的实例字段上（SoftDotTex，与引擎同生命周期），材质仍每次新建
/// （UITheme.Font 同款处理）。
/// C# 调用方（BossAttacks/Enemy/Mothership 过场）经 typed 直调——公开方法名为 PascalCase。
/// </summary>
public partial class CinematicFx : RefCounted
{
    public const int SoftTexSize = 64;

    public const int ParticleAmountCap = 96; // 硬性上限：每发射器 ≤96（性能预算：总存活 ≤400）

    /// <summary>64×64 径向渐变软点贴图（白色，alpha pow 衰减）：
    /// 粒子与光晕共用，消除硬边实心圆的廉价感；颜色经 modulate/process_material 乘算。
    /// 惰性建一次、缓存于 GameState autoload 的实例字段（全实例共用）——调用点遍布命中特效、
    /// 敌机尾焰与每个新建爆炸（4 次），逐次构建是 4096 次逐像素 SetPixel 加一次纹理解析；
    /// 缓存不持静态字段（禁持 Godot 对象）也不改像素公式，外观逐位不变。</summary>
    public static ImageTexture SoftTexture()
    {
        var host = GameState.Instance;
        if (host.SoftDotTex != null)
        {
            return host.SoftDotTex;
        }

        var tex = BuildSoftTexture();
        // 护栏（代码层，非探针）：同一宿主实例第二次构建＝共享缓存被绕过（改回每次构建、或字段被
        // 清空）。表现是纯性能劣化——不崩、不报错、画面逐位一致，除本行外没有任何信号。
        // 走 PushError 使其撞冒烟/截图探针的 ERROR 正则。计数在实例上：autoload 重建允许再建一次。
        if (host.SoftDotTexBuilds > 0)
        {
            GD.PushError($"CinematicFx.SoftTexture 在同一 GameState 实例上重复构建"
                + $"（第 {host.SoftDotTexBuilds + 1} 次）——共享缓存被绕过？");
        }

        host.SoftDotTexBuilds++;
        host.SoftDotTex = tex;
        return tex;
    }

    private static ImageTexture BuildSoftTexture()
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

    /// <summary>深空星云贴图工厂：256² 画布采样环面无缝能量场（core NebulaField：值噪声云 +
    /// 脊状细丝 + 暗尘带），再双线性放大。灰度能量场（RGB=alpha=能量值），颜色全部交给
    /// 调用方 modulate 染色——星空共用一张。贴图四向平铺无缝（无缝判据在 NebulaField 单测）。
    /// 惰性建一次、缓存于 GameState autoload 的实例字段（同 SoftTexture 口径）：输出只由
    /// (size, seed) 决定，而每个 Starfield 实例（开机链路 2 个、返航过场 5 个）都会调一次，
    /// 逐次重建是 256² 逐像素 SetPixel 加一次 768² 双线性放大上传的纯重复成本。
    /// **缓存只覆盖一套参数**（现存调用点全用默认值）：传异参即不命中，走非缓存路径并报错。
    /// 构建后不再改写，热路径零分配。</summary>
    public static ImageTexture NebulaTexture(int size = 768, int seed = 20260907)
    {
        var host = GameState.Instance;
        if (host.NebulaTex != null && host.NebulaTexSize == size && host.NebulaTexSeed == seed)
        {
            return host.NebulaTex;
        }

        // 护栏（代码层，非探针）：第二次构建只有两种来路，都只在耗时上悄悄变坏，不崩、不报错、
        // 画面逐位一致，除本行外没有任何信号——故走 PushError 撞冒烟探针的 ERROR 正则。
        if (host.NebulaTexBuilds > 0)
        {
            var sameParams = host.NebulaTexSize == size && host.NebulaTexSeed == seed;
            GD.PushError($"CinematicFx.NebulaTexture 在同一 GameState 实例上重复构建"
                + $"（第 {host.NebulaTexBuilds + 1} 次：请求 size={size} seed={seed}，"
                + $"缓存内为 size={host.NebulaTexSize} seed={host.NebulaTexSeed}）："
                + (sameParams
                    ? "参数相同却走到这里＝上面的缓存命中被绕过，已回落到逐实例重建。"
                    : "参数不同＝缓存只覆盖一套参数；要第二套参数请把缓存键改成按 (size, seed) 索引。"));
        }

        var tex = BuildNebulaTexture(size, seed);
        host.NebulaTexBuilds++;
        host.NebulaTexSize = size;
        host.NebulaTexSeed = seed;
        host.NebulaTex = tex;
        return tex;
    }

    private static ImageTexture BuildNebulaTexture(int size, int seed)
    {
        const int BaseSize = 256;
        var field = InfiAir.Core.NebulaField.Build(BaseSize, seed);
        var img = Image.CreateEmpty(BaseSize, BaseSize, false, Image.Format.Rgba8);
        for (var y = 0; y < BaseSize; y++)
        {
            for (var x = 0; x < BaseSize; x++)
            {
                var v = field[y * BaseSize + x];
                img.SetPixel(x, y, new Color(v, v, v, v));
            }
        }

        img.Resize(size, size, Image.Interpolation.Bilinear);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>四芒衍射星贴图（亮星层的十字微光）：细高斯横竖光轴 + 小型软核，灰度。
    /// 96² 单张，确定性逐像素生成；旋转/缩放交给绘制端 DrawSetTransform，贴图零状态。
    /// 惰性建一次、缓存于 GameState autoload 的实例字段（同 SoftTexture 口径）：每个
    /// Starfield 实例都会调一次，逐次重建＝9216 次逐像素 SetPixel 加一次纹理解析。</summary>
    public static ImageTexture SpikeTexture()
    {
        var host = GameState.Instance;
        if (host.SpikeTex != null)
        {
            return host.SpikeTex;
        }

        var tex = BuildSpikeTexture();
        // 护栏（代码层，非探针）：同一宿主实例第二次构建＝共享缓存被绕过（同 SoftTexture）。
        if (host.SpikeTexBuilds > 0)
        {
            GD.PushError($"CinematicFx.SpikeTexture 在同一 GameState 实例上重复构建"
                + $"（第 {host.SpikeTexBuilds + 1} 次）——共享缓存被绕过？");
        }

        host.SpikeTexBuilds++;
        host.SpikeTex = tex;
        return tex;
    }

    private static ImageTexture BuildSpikeTexture()
    {
        const int Size = 96;
        const float Center = Size * 0.5f;
        const float Length = Size * 0.46f; // 光轴臂长（半径）
        const float AxisSigma = 1.7f;      // 光轴横向厚度（高斯 σ）
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var dx = x + 0.5f - Center;
                var dy = y + 0.5f - Center;
                // 横轴：沿 x 衰减出臂形，垂直向高斯收细；纵轴对称
                var ax = MathF.Exp(-(dx * dx) / (Length * Length * 0.35f)) * MathF.Exp(-(dy * dy) / (2.0f * AxisSigma * AxisSigma));
                var ay = MathF.Exp(-(dy * dy) / (Length * Length * 0.35f)) * MathF.Exp(-(dx * dx) / (2.0f * AxisSigma * AxisSigma));
                var d2 = dx * dx + dy * dy;
                var core = MathF.Exp(-d2 / (2.0f * 3.2f * 3.2f)) * 0.85f;
                var v = Math.Clamp(MathF.Max(MathF.Max(ax, ay), 0.0f) + core, 0.0f, 1.0f);
                img.SetPixel(x, y, new Color(v, v, v, v));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>软径向光晕：Sprite2D 承载软点贴图，scale/modulate 可直接 tween。
    /// additive material 共享（N 机 N 份相同材质 → 1 份，材质只读属性无实例差异）——
    /// 不做静态缓存（退出 segfault 规则），每次新建，语义等价。</summary>
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

    /// <summary>叠加态辉光圆点（复用 C# 顶层类 GlowDot，原内嵌 _GlowDot 同构；过场构图共用）。</summary>
    public static GlowDot Glow(float radius, Color color, bool additive = true)
    {
        var dot = new GlowDot { Radius = radius, DotColor = color };
        if (additive)
        {
            var mat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            dot.Material = mat;
        }

        return dot;
    }

    /// <summary>居中矩形 Polygon2D（w/h 为全宽全高，坐标 -0.5w..0.5w / -0.5h..0.5h；过场构图共用）。</summary>
    public static Polygon2D RectPoly(float w, float h, Color color)
    {
        var p = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-w * 0.5f, -h * 0.5f),
                new Vector2(w * 0.5f, -h * 0.5f),
                new Vector2(w * 0.5f, h * 0.5f),
                new Vector2(-w * 0.5f, h * 0.5f),
            },
            Color = color,
        };
        return p;
    }

    /// <summary>全屏底色 ColorRect（1920×1080 设计坐标，鼠标穿透；过场构图共用）。</summary>
    public static ColorRect BgRect(Color color)
    {
        var r = new ColorRect
        {
            Color = color,
            Position = Vector2.Zero,
            Size = new Vector2(1920.0f, 1080.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        return r;
    }

    /// <summary>折线 Line2D（Points 逐点折线，默认宽 2px；过场构图共用）。</summary>
    public static Line2D Line(Vector2[] points, Color color, float width = 2.0f)
    {
        var l = new Line2D
        {
            Points = points,
            DefaultColor = color,
            Width = width,
        };
        return l;
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
    /// 返回 Vector2[]（互操作语义一致）。</summary>
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

    /// <summary>母舰召唤·链路锁定拍（生产路径 <c>Main.SummonMothershipInternal</c> 与教程
    /// <c>Tutorial.SummonMothership</c> 共用同一发，两处出场读法一致）：落点一圈压扁冲击环
    /// （ry 比取穿梭门同值，读作「在门的平面上炸开」）+ 一团软闪——把「母舰就要从这里出来」
    /// 说在玩家正看着的地方。触发拍的「演出开始」信号原由机库小窗承担，小窗退役后归此处。
    /// 两个部件都随 add_child 自播自毁；<paramref name="worldScale"/> 由调用方给（世界层尺寸族）。</summary>
    public static void SummonTriggerBeat(Node parent, Vector2 pos, float worldScale)
    {
        var wave = Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = 190.0f * worldScale,
            ["time"] = 0.45f,
            ["ry_ratio"] = WarpGate.EllipseRatio,
            ["color"] = new Color(0.930f, 0.676f, 0.320f, 0.55f),
            ["core_color"] = new Color(1.000f, 0.960f, 0.870f, 0.95f),
            ["width"] = 10.0,
            ["start_scale"] = 0.25,
        });
        wave.Position = pos;
        parent.AddChild(wave);
        var flash = SoftGlow(150.0f * worldScale, new Color(1.000f, 0.930f, 0.780f, 0.6f));
        flash.Position = pos;
        parent.AddChild(flash);
        var tw = flash.CreateTween();
        tw.TweenProperty(flash, "modulate:a", 0.0, 0.28).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenCallback(Callable.From(flash.QueueFree));
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
}

/// <summary>双层扩散冲击环（粗辉光环 + 细亮芯环 + 可选低 alpha 填充盘），_ready 起 tween，播完自毁。
/// 同文件顶层类（C# 源生成器不支持内嵌类）。</summary>
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
        var color = CinematicFx.CfgColor(cfg, "color", UITheme.Holo);
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
        // 点列 <2 或目标 <2 时直接返回，防负索引/除零
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
        // _resample 对点列 <2 返回空集，此处防空 _samples 负索引越界（-2 越界）
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
    private Color _color = new(UITheme.HoloPale, 0.5f);

    public void Setup(Godot.Collections.Dictionary cfg)
    {
        var count = CinematicFx.CfgInt(cfg, "count", 28);
        _maxRadius = CinematicFx.CfgFloat(cfg, "max_radius", 900.0f);
        _color = CinematicFx.CfgColor(cfg, "color", new Color(UITheme.HoloPale, 0.5f));
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
