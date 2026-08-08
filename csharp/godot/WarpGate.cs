using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 母舰召唤·穿梭门（世界坐标，挂 Main 下）：小窗演出结束后由 main 创建在母舰停驻点。
/// 生命周期：OPENING 展开（gate.open_time）→ HOLD 保持（母舰穿出期间脉动，
/// 由 Mothership.begin_warp_in 收尾时调 close()；超时自动关闭兜底）→
/// CLOSING 收缩关闭（gate.close_time）→ 自销毁。
/// 数值取 balance.json effects.mothership_summon.gate，脚本默认值须保持一致。
/// M6 全量迁移（2026-08-08 自 scripts/warp_gate.gd）。
/// Enemy.sin_fast/cos_fast 为 C# 静态方法 typed 直调（原 GDScript 经脚本资源调用）。
/// </summary>
public partial class WarpGate : Node2D
{
    public float OPEN_TIME = 0.35f;

    public float CLOSE_TIME = 0.4f;

    public float RADIUS = 190.0f; // 门洞半径设计值（× world_scale 生效）

    /// <summary>HOLD 兜底时长：正常由母舰到达触发 close()，母舰被提前回收（返航）时自动关闭。</summary>
    public float HOLD_MAX = 3.0f;

    private static readonly Color CYAN = new(0.32f, 0.93f, 0.85f);
    private static readonly Color WARP_BLUE = new(0.35f, 0.6f, 1.0f);
    private const float ELLIPSE_RATIO = 0.55f; // 竖向压扁（透视门洞）

    public enum Phase { OPENING, HOLD, CLOSING }

    private Phase _phase = Phase.OPENING;
    private float _t;
    private Line2D _ring = null!;
    private Line2D _ringInner = null!;
    private readonly List<Line2D> _arcs = new();
    private Sprite2D _mouth = null!; // 门心软光填充（椭圆压扁，随开合/呼吸同步）
    private Vector2 _mouthBase = Vector2.One;
    private readonly List<Line2D> _swirls = new(); // 内旋弧 ×2（反向旋转，能量漩涡感）
    private GpuParticles2D _rimFx = null!; // 门缘内吸粒子（环发射 + 负径向速度）
    private Node2D _lip = null!; // 前唇层：下半环 z 压过母舰，读作「穿门而出」


    public override void _Ready()
    {
        OPEN_TIME = (float)GameState.Instance.Cfg("effects.mothership_summon.gate.open_time", OPEN_TIME).AsDouble();
        CLOSE_TIME = (float)GameState.Instance.Cfg("effects.mothership_summon.gate.close_time", CLOSE_TIME).AsDouble();
        RADIUS = (float)GameState.Instance.Cfg("effects.mothership_summon.gate.radius", RADIUS).AsDouble()
            * (float)GameState.Instance.WorldScale;
        ZIndex = -1; // 门洞衬在母舰之后
        _ring = MakeRing(4.0f, CYAN);
        _ringInner = MakeRing(2.0f, WARP_BLUE);
        // C28：环预建单位半径点集，_layout 帧内仅写 scale（零分配）
        _ring.Points = EllipsePoints(1.0f, 48);
        _ringInner.Points = EllipsePoints(1.0f, 48);
        for (var i = 0; i < 3; i++)
        {
            var arc = new Line2D { Width = 3.0f, DefaultColor = new Color(CYAN, 0.7f) };
            // C28：弧预建单位点集（span 50°、10 点），_layout 帧内仅写 scale
            var pts = new Vector2[10];
            var a0 = Mathf.Tau * i / 3.0f;
            for (var j = 0; j < 10; j++)
            {
                var a = a0 + Mathf.DegToRad(50.0f) * j / 9.0f;
                pts[j] = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * ELLIPSE_RATIO);
            }
            arc.Points = pts;
            AddChild(arc);
            _arcs.Add(arc);
        }
        // 门心软光填充（替代硬边圆盘）：软点贴图椭圆压扁，alpha 由 _layout 驱动
        _mouth = (Sprite2D)CinematicFx.SoftGlow(RADIUS * 0.85f, new Color(WARP_BLUE, 0.0f));
        _mouth.Scale = new Vector2(_mouth.Scale.X, _mouth.Scale.Y * ELLIPSE_RATIO);
        _mouthBase = _mouth.Scale;
        AddChild(_mouth);
        // 内旋弧 ×2：预建点集，帧内仅旋转/缩放/透明度（零分配）
        for (var i = 0; i < 2; i++)
        {
            var swirl = new Line2D
            {
                Width = 2.5f - 0.5f * i,
                DefaultColor = new Color(CYAN.Lightened(0.25f), 0.8f),
                Points = ArcPoints(RADIUS * (0.55f + 0.15f * i), 70.0f, 12),
                Material = (CanvasItemMaterial)CinematicFx.AdditiveMaterial(),
            };
            AddChild(swirl);
            _swirls.Add(swirl);
        }
        // 门缘内吸粒子：环上发射、负径向速度流向门心
        _rimFx = (GpuParticles2D)CinematicFx.Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 48,
            ["lifetime"] = 0.55,
            ["vel_min"] = 0.0,
            ["vel_max"] = 0.0,
            ["scale_min"] = 3.0,
            ["scale_max"] = 6.0,
            ["color"] = new Color(CYAN, 0.7f),
        });
        if (_rimFx.ProcessMaterial is ParticleProcessMaterial rimMat)
        {
            rimMat.Direction = Vector3.Zero;
            rimMat.Spread = 0.0f;
            rimMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
            rimMat.EmissionRingAxis = new Vector3(0.0f, 0.0f, 1.0f);
            rimMat.EmissionRingRadius = RADIUS;
            rimMat.EmissionRingInnerRadius = RADIUS * 0.97f;
            rimMat.EmissionRingHeight = 0.0f;
            rimMat.RadialVelocityMin = -1.9f * RADIUS;
            rimMat.RadialVelocityMax = -2.7f * RADIUS;
        }
        AddChild(_rimFx);
        // 前唇层：下半环（辉光 + 亮芯）z=5 压过母舰（-1+5=4 > 母舰 0），CLOSING 随整体收缩淡出
        _lip = new Node2D { ZIndex = 5 };
        var lipPts = ArcPoints(RADIUS, 180.0f, 25);
        var lipGlow = new Line2D
        {
            Width = 10.0f,
            DefaultColor = new Color(WARP_BLUE, 0.35f),
            Points = lipPts,
            Material = (CanvasItemMaterial)CinematicFx.AdditiveMaterial(),
        };
        _lip.AddChild(lipGlow);
        var lipCore = new Line2D
        {
            Width = 3.5f,
            DefaultColor = new Color(CYAN.Lightened(0.35f), 0.95f),
            Points = lipPts,
            Material = (CanvasItemMaterial)CinematicFx.AdditiveMaterial(),
        };
        _lip.AddChild(lipCore);
        AddChild(_lip);
        GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -6.0, 0.5);
    }

    /// <summary>当前阶段（原 GDScript `phase()`；A7：测试/诊断白盒断言经公开接口）。</summary>
    public Phase GetPhase() => _phase;

    /// <summary>母舰穿出完成（或提前收回）时调用：进入关闭段（幂等）。</summary>
    public void Close()
    {
        if (_phase == Phase.CLOSING)
        {
            return;
        }
        _phase = Phase.CLOSING;
        _t = 0.0f;
        _rimFx.Emitting = false; // 关闭段停止内吸粒子，存量随收缩消散
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        switch (_phase)
        {
            case Phase.OPENING:
            {
                var p = Mathf.Clamp(_t / OPEN_TIME, 0.0f, 1.0f);
                Layout(1.0f - (1.0f - p) * (1.0f - p), 1.0f); // ease-out 展开
                if (p >= 1.0f)
                {
                    _phase = Phase.HOLD;
                    _t = 0.0f;
                }
                break;
            }
            case Phase.HOLD:
                // 保持期：呼吸脉动 + 弧段旋转
                Layout(1.0f + 0.04f * Enemy.SinFast(_t * 6.0f), 1.0f); // M3b：Enemy 迁 C#，静态直调
                if (_t >= HOLD_MAX)
                {
                    Close();
                }
                break;
            case Phase.CLOSING:
            {
                var p = Mathf.Clamp(_t / CLOSE_TIME, 0.0f, 1.0f);
                Layout(1.0f - p, 1.0f - p);
                if (p >= 1.0f)
                {
                    QueueFree();
                }
                break;
            }
        }
        for (var i = 0; i < _arcs.Count; i++)
        {
            _arcs[i].Rotation = _t * (1.5f + 0.4f * i) * (i % 2 == 0 ? 1.0f : -1.0f);
        }
        // 内旋弧反向旋转（能量漩涡）
        for (var i = 0; i < _swirls.Count; i++)
        {
            _swirls[i].Rotation = -_t * (2.2f + 0.7f * i) * (i % 2 == 0 ? 1.0f : -1.0f);
        }
    }

    /// <summary>scale_p：门洞开合比例；alpha_p：整体透明度。</summary>
    private void Layout(float scaleP, float alphaP)
    {
        // C28：环/弧预建点集，帧内经 set_point_position 原地写（零分配、线宽不随 scale 变）
        LayoutEllipse(_ring, RADIUS * scaleP, 48);
        _ring.DefaultColor = new Color(CYAN, 0.9f * alphaP);
        LayoutEllipse(_ringInner, RADIUS * 0.82f * scaleP, 48);
        _ringInner.DefaultColor = new Color(WARP_BLUE, 0.7f * alphaP);
        // 附件层（mouth/lip/swirls）：经节点 scale 随开合伸缩——mouth 为 Sprite2D 无线宽问题；
        // lip/swirls 为 Line2D，线宽随比例伸缩是既有表现（与环/弧的原地写点集不同，维持现状）
        _mouth.Scale = _mouthBase * scaleP;
        var mouthMod = _mouth.Modulate;
        mouthMod.A = 0.4f * alphaP * scaleP;
        _mouth.Modulate = mouthMod;
        for (var i = 0; i < _swirls.Count; i++)
        {
            _swirls[i].Scale = Vector2.One * scaleP;
            var swirlMod = _swirls[i].Modulate;
            swirlMod.A = 0.8f * alphaP;
            _swirls[i].Modulate = swirlMod;
        }
        _lip.Scale = Vector2.One * scaleP;
        var lipMod = _lip.Modulate;
        lipMod.A = alphaP;
        _lip.Modulate = lipMod;
        for (var i = 0; i < _arcs.Count; i++)
        {
            var arc = _arcs[i];
            // C28：弧预建点集（_ready），帧内 set_point_position 原地写（线宽不变）
            var r = RADIUS * 1.12f * scaleP;
            var a0 = Mathf.Tau * i / 3.0f;
            for (var j = 0; j < 10; j++)
            {
                var a = a0 + Mathf.DegToRad(50.0f) * j / 9.0f;
                arc.SetPointPosition(j, new Vector2(Enemy.CosFast(a), Enemy.SinFast(a) * ELLIPSE_RATIO) * r);
            }
            arc.DefaultColor = new Color(CYAN, 0.7f * alphaP);
        }
    }

    /// <summary>扇弧点集（起始角 0，随节点旋转动画；ry 压扁对齐门洞透视）。</summary>
    private Vector2[] ArcPoints(float radius, float spanDeg, int count)
    {
        var pts = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var a = Mathf.DegToRad(spanDeg) * i / (count - 1);
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * ELLIPSE_RATIO) * radius;
        }
        return pts;
    }

    /// <summary>C28：原地写椭圆点集（set_point_position 直写内部数组，零分配、线宽不随 scale 变）。</summary>
    private void LayoutEllipse(Line2D line, float radius, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var a = Mathf.Tau * i / count;
            line.SetPointPosition(i, new Vector2(Enemy.CosFast(a), Enemy.SinFast(a) * ELLIPSE_RATIO) * radius);
        }
    }

    private Line2D MakeRing(float width, Color color)
    {
        var ring = new Line2D { Width = width, Closed = true, DefaultColor = color };
        AddChild(ring);
        return ring;
    }

    private Vector2[] EllipsePoints(float radius, int count)
    {
        var pts = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var a = Mathf.Tau * i / count;
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * ELLIPSE_RATIO) * radius;
        }
        return pts;
    }

    // ---------------- GDScript 鸭子调用兼容桥（M6 过渡，M7 删除） ----------------
    // 调用方：main.gd（WarpGate.new()/position——主代理改 typed/脚本资源）、
    // Mothership.cs（_warp_gate.Call("close") 动态派发）、
    // test/mothership_summon_test.gd（gate.phase()；WarpGate.Phase.CLOSING 改经脚本资源静态访问器）。

    public int phase() => (int)GetPhase();

    public void close() => Close();

    /// <summary>静态枚举访问器：GDScript 不能以类名引用 C# 嵌套枚举
    /// （mothership_summon_test.gd `WarpGate.Phase.CLOSING` 改 `WarpGateScript.GetPhaseClosing()`；先例 Mothership.GetState*）。</summary>
    public static int GetPhaseOpening() => (int)Phase.OPENING;

    public static int GetPhaseHold() => (int)Phase.HOLD;

    public static int GetPhaseClosing() => (int)Phase.CLOSING;
}
