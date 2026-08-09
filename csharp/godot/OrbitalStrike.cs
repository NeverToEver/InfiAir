using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 轨道打击清场动画（对齐原作 homecoming ORBITAL_STRIKE 阶段，见 docs/PORTING_PARITY.md）。
/// 从基地「继续出击」时由 main._resume_from_base() 触发；树保持暂停播放（process_mode=Always）。
/// 时轴（进度 p = t / DURATION）：
///   [0, MISSILE_FROM)         瞄准具淡入：命中点脉冲环 ×3 + 十字线（青色）
///   [MISSILE_FROM, IMPACT_AT) 导弹自屏顶 ease-in 下落（拖尾线 + 三角弹体 + 辉光头）
///   IMPACT_AT                 struck 信号：main 在此刻清场（Boss 保留）并恢复对局；
///                             命中演出：全屏青闪 + 纵向光柱 + 扩散环/内环 + 侧向光线，衰减至结束
///   >= 1.0                    finished 信号并自销毁
/// 数值取 balance.json effects.orbital_strike，脚本默认值须保持一致。
/// M6 全量迁移（2026-08-08 自 scripts/orbital_strike.gd）。
/// 注：原 GDScript 信号 struck/finished 迁移为 C# [Signal] Struck/Finished——
/// main.gd/tests 连接处需改连 PascalCase 名（先例 main.gd `_mothership.Departed.connect`：
/// GDScript 连 C# [Signal] 用 PascalCase，主代理集中处理）。
/// </summary>
public partial class OrbitalStrike : CanvasLayer
{
    [Signal]
    public delegate void StruckEventHandler();

    [Signal]
    public delegate void FinishedEventHandler();

    // ---- 时轴/尺寸配置（_ready 从 balance.json 覆盖；与脚本默认值一致；测试直写 DURATION） ----
    public float DURATION = 1.4f;

    public float IMPACT_AT = 0.56f;

    public float MISSILE_FROM = 0.3f;

    public float RETICLE_RADIUS = 46.0f;

    public float IMPACT_Y_RATIO = 0.42f;

    /// <summary>瞄准具/光柱主色（对齐原作青色 82,236,218 系）。</summary>
    private static readonly Color CYAN = new(0.32f, 0.93f, 0.85f);
    private const int RING_POINTS = 48;
    private const float MISSILE_START_Y = -140.0f;

    private float _t;
    private bool _impacted;
    private Vector2 _impactPoint = Vector2.Zero;

    private Node2D _reticle = null!; // 瞄准具（3 脉冲环 + 十字线）
    private readonly List<Line2D> _reticleRings = new();
    private Node2D _reticleCross = null!;
    private Vector2[] _unitCircle = System.Array.Empty<Vector2>(); // P4：单位圆点集缓存（_layout_ring 帧内免重算三角）
    private Node2D _missile = null!; // 导弹容器（拖尾/弹体/辉光）
    private Line2D _missileTrail = null!;
    private ColorRect _flash = null!;
    private Polygon2D _column = null!;
    private Line2D _ringOuter = null!;
    private Line2D _ringInner = null!;
    private readonly List<Polygon2D> _rays = new();
    private Vector2 _screen = Vector2.Zero; // _ready 缓存视口尺寸（D17：命中段热路径免每帧查询）

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always;
        Layer = 24; // 对局世界与 HUD 之上、基地 UI（25）之下
        // H15（健壮性审核）：时轴序钳制——duration=0 首帧 finished、impact_at≥1.0 时 struck 不可达
        // （main 收不到 _on_orbital_struck，树保持暂停+锁输入软锁）、missile_from≥impact_at 时瞄准段除零
        DURATION = Mathf.Max((float)GameState.Instance.Cfg("effects.orbital_strike.duration", DURATION).AsDouble(), 0.01f);
        // W 系列（2026-08-09）：补下限 0.05——impact_at≤0 时首帧即 struck+清场（H15 只封了另一侧，E07 单帧大 delta 同族另一入口）
        IMPACT_AT = Mathf.Clamp((float)GameState.Instance.Cfg("effects.orbital_strike.impact_at", IMPACT_AT).AsDouble(), 0.05f, 0.95f);
        MISSILE_FROM = Mathf.Max(Mathf.Min((float)GameState.Instance.Cfg("effects.orbital_strike.missile_from", MISSILE_FROM).AsDouble(), IMPACT_AT - 0.05f), 0.0f);
        RETICLE_RADIUS = (float)GameState.Instance.Cfg("effects.orbital_strike.reticle_radius", RETICLE_RADIUS).AsDouble();
        IMPACT_Y_RATIO = (float)GameState.Instance.Cfg("effects.orbital_strike.impact_y_ratio", IMPACT_Y_RATIO).AsDouble();
        _screen = GetViewport().GetVisibleRect().Size;
        _impactPoint = new Vector2(_screen.X * 0.5f, _screen.Y * IMPACT_Y_RATIO);
        BuildReticle();
        BuildMissile();
        BuildImpactFx();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        var p = _t / DURATION;
        if (p >= 1.0f)
        {
            // 兜底（2026-08-03 审计）：单帧大 delta（窗口失焦恢复/低端机卡顿）可越过 IMPACT_AT 直达 1.0，
            // 必须先补发 struck——它是 main 恢复对局（paused=false + unlock_input）的唯一入口，缺发则软锁
            if (!_impacted)
            {
                _impacted = true;
                _missile.Hide();
                _reticle.Hide();
                GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION_BIG);
                GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_seq_final", 24.0).AsDouble());
                EmitSignal(SignalName.Struck);
            }
            EmitSignal(SignalName.Finished);
            QueueFree();
            return;
        }
        if (!_impacted && p >= IMPACT_AT)
        {
            _impacted = true;
            _missile.Hide();
            _reticle.Hide();
            GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION_BIG);
            GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_seq_final", 24.0).AsDouble());
            EmitSignal(SignalName.Struck);
        }
        UpdateVisuals(p);
    }

    /// <summary>瞄准具：3 圈脉冲环 + 缓慢旋转的十字线，贴在命中点上。</summary>
    private void BuildReticle()
    {
        _reticle = new Node2D { Position = _impactPoint };
        AddChild(_reticle);
        for (var i = 0; i < 3; i++)
        {
            var ring = MakeRingLine(RETICLE_RADIUS, 2.0f, CYAN);
            _reticle.AddChild(ring);
            _reticleRings.Add(ring);
        }
        _reticleCross = new Node2D();
        _reticle.AddChild(_reticleCross);
        var arm = RETICLE_RADIUS * 1.5f;
        Vector2[][] crossSegments =
        {
            new[] { new Vector2(-arm, 0.0f), new Vector2(-arm * 0.4f, 0.0f) },
            new[] { new Vector2(arm * 0.4f, 0.0f), new Vector2(arm, 0.0f) },
            new[] { new Vector2(0.0f, -arm), new Vector2(0.0f, -arm * 0.4f) },
            new[] { new Vector2(0.0f, arm * 0.4f), new Vector2(0.0f, arm) },
        };
        foreach (var pts in crossSegments)
        {
            var seg = new Line2D { Width = 2.0f, DefaultColor = CYAN, Points = pts };
            _reticleCross.AddChild(seg);
        }
    }

    /// <summary>导弹：拖尾线（透明→青渐变）+ 朝下的三角弹体 + 辉光头。</summary>
    private void BuildMissile()
    {
        _missile = new Node2D();
        AddChild(_missile);
        _missileTrail = new Line2D { Width = 3.0f };
        var grad = new Gradient();
        grad.SetColor(0, new Color(CYAN, 0.0f));
        grad.SetColor(1, CYAN);
        _missileTrail.Gradient = grad;
        _missileTrail.Points = new[] { Vector2.Zero, Vector2.Zero }; // C28：预分配，帧内只写元素
        _missile.AddChild(_missileTrail);
        var body = new Polygon2D
        {
            Polygon = new[] { new Vector2(0.0f, 14.0f), new Vector2(-5.0f, -10.0f), new Vector2(5.0f, -10.0f) },
            Color = new Color(0.9f, 1.0f, 0.98f),
        };
        _missile.AddChild(body);
        var glow = new Polygon2D { Polygon = CirclePoints(10.0f, 16), Color = new Color(CYAN, 0.55f) };
        _missile.AddChild(glow);
        _missile.Hide();
    }

    /// <summary>命中演出节点（初始全透明，struck 后随进度衰减）。</summary>
    private void BuildImpactFx()
    {
        _flash = new ColorRect { Color = new Color(CYAN, 0.0f) };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _flash.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_flash);
        var screen = GetViewport().GetVisibleRect().Size;
        _column = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(_impactPoint.X - 45.0f, 0.0f),
                new Vector2(_impactPoint.X + 45.0f, 0.0f),
                new Vector2(_impactPoint.X + 45.0f, screen.Y),
                new Vector2(_impactPoint.X - 45.0f, screen.Y),
            },
            Color = new Color(CYAN, 0.0f),
        };
        AddChild(_column);
        _ringOuter = MakeRingLine(40.0f, 4.0f, new Color(CYAN, 0.0f));
        _ringOuter.Position = _impactPoint;
        AddChild(_ringOuter);
        _ringInner = MakeRingLine(20.0f, 2.0f, new Color(CYAN, 0.0f));
        _ringInner.Position = _impactPoint;
        AddChild(_ringInner);
        for (var dir = -1.0f; dir <= 1.0f; dir += 2.0f)
        {
            var ray = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(_impactPoint.X, _impactPoint.Y - 2.0f),
                    new Vector2(_impactPoint.X + dir * screen.X * 0.5f, _impactPoint.Y - 14.0f),
                    new Vector2(_impactPoint.X + dir * screen.X * 0.5f, _impactPoint.Y + 14.0f),
                    new Vector2(_impactPoint.X, _impactPoint.Y + 2.0f),
                },
                Color = new Color(CYAN, 0.0f),
            };
            AddChild(ray);
            _rays.Add(ray);
        }
    }

    private void UpdateVisuals(float p)
    {
        if (p < IMPACT_AT)
        {
            // 瞄准具：整体淡入 + 三环错相脉冲 + 十字线缓转
            var fade = Mathf.Clamp(p / 0.08f, 0.0f, 1.0f);
            _reticle.Modulate = new Color(1.0f, 1.0f, 1.0f, fade);
            for (var i = 0; i < _reticleRings.Count; i++)
            {
                var pulse = 0.7f + 0.3f * Enemy.SinFast(Mathf.Tau * (p * 4.0f + i / 3.0f)); // M3b：Enemy 迁 C#，静态直调
                LayoutRing(_reticleRings[i], RETICLE_RADIUS * pulse);
            }
            _reticleCross.Rotation = p * 1.5f;
            if (p >= MISSILE_FROM)
            {
                _missile.Show();
                var mp = (p - MISSILE_FROM) / (IMPACT_AT - MISSILE_FROM);
                var head = new Vector2(_impactPoint.X, Mathf.Lerp(MISSILE_START_Y, _impactPoint.Y, mp * mp));
                _missile.Position = head;
                // C28：预分配 2 点，set_point_position 原地写（points[i]= 值语义副本不生效）
                _missileTrail.SetPointPosition(0, new Vector2(0.0f, MISSILE_START_Y - head.Y));
                _missileTrail.SetPointPosition(1, Vector2.Zero);
            }
        }
        else
        {
            // 命中后：q ∈ [0,1] 全程衰减
            var q = (p - IMPACT_AT) / (1.0f - IMPACT_AT);
            var fadeOut = 1.0f - q;
            _flash.Color = new Color(CYAN, 0.85f * fadeOut * fadeOut);
            _column.Color = new Color(CYAN, 0.55f * fadeOut);
            var screen = _screen;
            var diag = screen.Length();
            LayoutRing(_ringOuter, Mathf.Lerp(40.0f, diag * 0.6f, 1.0f - fadeOut * fadeOut));
            _ringOuter.DefaultColor = new Color(CYAN, 0.9f * fadeOut);
            LayoutRing(_ringInner, Mathf.Lerp(20.0f, RETICLE_RADIUS * 3.0f, q));
            _ringInner.DefaultColor = new Color(CYAN, 0.8f * fadeOut);
            foreach (var ray in _rays)
            {
                ray.Color = new Color(CYAN, 0.5f * fadeOut);
            }
        }
    }

    private Line2D MakeRingLine(float radius, float width, Color color)
    {
        var ring = new Line2D { Width = width, DefaultColor = color, Closed = true };
        // C28：预建点集（长度固定），帧内经 set_point_position 原地改写（零分配、线宽不随 scale 变）
        ring.Points = CirclePoints(1.0f, RING_POINTS);
        LayoutRing(ring, radius);
        return ring;
    }

    private void LayoutRing(Line2D ring, float radius)
    {
        // C28：原地写点集元素（set_point_position 直写内部数组），不重建 PackedVector2Array、
        // 不缩放节点（缩放会连带放大线宽）
        // P4（2026-08-05）：单位圆点集缓存一次——原每帧重算 RING_POINTS 次常数 cos/sin
        //（reticle/瞄准环多环叠加帧内数十次三角调用）
        if (_unitCircle.Length == 0)
        {
            _unitCircle = CirclePoints(1.0f, RING_POINTS);
        }
        for (var i = 0; i < RING_POINTS; i++)
        {
            ring.SetPointPosition(i, _unitCircle[i] * radius);
        }
    }

    private Vector2[] CirclePoints(float radius, int count)
    {
        var pts = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var a = Mathf.Tau * i / count;
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        return pts;
    }

    // ---------------- UPPER_SNAKE 配置字段（M7 后保留） ----------------
    // DURATION 等 UPPER_SNAKE 公开字段保留原 GDScript 公开 var 语义：测试直写（orbital_strike_test /
    // ui_capture / return_cinematic / elite_turret_event_test 经 `strike.DURATION` / `main.strike().DURATION`）。
    // 本类无 snake_case 方法桥（C# 迁移后无动态派发调用面）。
}
