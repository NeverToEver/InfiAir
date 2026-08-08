using System.Collections.Generic;
using System.Linq;
using Godot;

namespace InfiAir;

/// <summary>
/// 精英炮塔事件·打击航母（docs/ELITE_TURRET_EVENT.md 第 2 节；2026-08-08 自 scripts/strike_carrier.gd 迁移）：
/// 背景式巨型单位（不可被攻击，无碰撞层），自屏幕上方深空降入悬停，
/// 作为炮台展开的舞台；事件结束按胜负两种姿态撤离（受创慢速 / 完整加速）。
/// 基座环即状态灯：待命暗红 → 升起充能品红高亮 → 炮台被毁对应环熄灭。
/// 迁移期：GameState 经 GameStateBridge（热路径 view 帧缓存）；SinFast 经 Enemy（C# 同程序集静态）；
/// entered/exited 以 [Signal] PascalCase 注册，C# 侧连接经 C# event（+=）。
/// </summary>
public partial class StrikeCarrier : Node2D
{
    [Signal]
    public delegate void EnteredEventHandler();

    [Signal]
    public delegate void ExitedEventHandler();

    private static readonly Texture2D CarrierTexture = GD.Load<Texture2D>("res://assets/sprites/strike_carrier.png");

    /// <summary>基座相对偏移设计值（与生成器 TURRET_WELLS 对齐：贴图坐标 - (600, 350)；使用点 × world_scale）。</summary>
    public static readonly Vector2[] Sockets =
    {
        new(-170.0f, 120.0f), // 左翼台内
        new(170.0f, 120.0f), // 右翼台内
        new(-310.0f, 80.0f), // 左翼台外
        new(310.0f, 80.0f), // 右翼台外
        new(0.0f, 170.0f), // 中央前甲板
    };

    public enum State { ENTER, HOVER, RETREAT }

    /// <summary>撤退参数（复用 Boss escape 参数族量级）。</summary>
    public float RetreatStartSpeed { get; private set; } = 120.0f;
    public float RetreatAccel { get; private set; } = 420.0f;

    private State _state = State.ENTER;
    private float _enterT;
    private float _enterDuration = 2.0f;
    private float _startY;
    private float _hoverY = 300.0f;
    private float _retreatSpeed;
    private float _retreatFactor = 1.0f; // 受创撤离放慢
    private float _hoverTime;
    private readonly List<Line2D> _rings = new();
    private Sprite2D _sprite = null!; // 构造函数赋值（设计值 1.0 × 全局缩放）

    /// <summary>热路径缓存：view_world_rect 每物理帧一次动态调用（全实例共享）。</summary>
    private static ulong _frame = ulong.MaxValue;
    private static Rect2 _frameView;

    private static Rect2 CachedView()
    {
        var f = Engine.GetPhysicsFrames();
        if (f != _frame)
        {
            _frame = f;
            _frameView = GameStateBridge.Call("view_world_rect").AsRect2();
        }

        return _frameView;
    }

    public StrikeCarrier()
    {
        _sprite = new Sprite2D
        {
            Texture = CarrierTexture,
            Scale = Vector2.One * (float)GameStateBridge.Get("world_scale").AsDouble(), // 设计值 1.0 × 全局缩放
        };
        AddChild(_sprite);
    }

    public override void _Ready()
    {
        RetreatStartSpeed = (float)GameStateBridge
            .Call("cfg", "elite_turret_event.carrier.retreat_start_speed", RetreatStartSpeed).AsDouble();
        RetreatAccel = (float)GameStateBridge
            .Call("cfg", "elite_turret_event.carrier.retreat_accel", RetreatAccel).AsDouble();
        // 深空淡入
        var m = Modulate;
        m.A = 0.0f;
        Modulate = m;
        BuildRings();
    }

    /// <summary>八角基座环（状态灯；默认隐藏，事件按启用基座逐个点亮）。</summary>
    private void BuildRings()
    {
        var ws = (float)GameStateBridge.Get("world_scale").AsDouble();
        foreach (var socket in Sockets)
        {
            var ring = new Line2D();
            var pts = new System.Collections.Generic.List<Vector2>();
            for (var i = 0; i < 9; i++)
            {
                var a = Mathf.Pi / 8.0f + i * Mathf.Pi / 4.0f;
                pts.Add(socket * ws + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 40.0f * ws);
            }

            ring.Points = pts.ToArray();
            ring.Width = 1.0f;
            ring.DefaultColor = new Color(0.47f, 0.12f, 0.24f, 0.9f); // 待命暗红
            ring.Visible = false;
            AddChild(ring);
            _rings.Add(ring);
        }
    }

    /// <summary>降入悬停：自屏幕上方深空下压到 hover_y（enter_duration 秒，缓出 + 淡入）。</summary>
    public void Enter(float hoverY, float duration)
    {
        _hoverY = hoverY;
        _enterDuration = duration;
        _startY = Position.Y;
        _state = State.ENTER;
        _enterT = 0.0f;
    }

    /// <summary>启用基座环（升起充能品红高亮）。</summary>
    public void SetSocketCharging(int index)
    {
        if (index >= 0 && index < _rings.Count)
        {
            _rings[index].Visible = true;
            _rings[index].DefaultColor = new Color(1.0f, 0.25f, 0.75f, 1.0f); // 精英品红
        }
    }

    /// <summary>炮台被毁：对应环熄灭。</summary>
    public void SetSocketDestroyed(int index)
    {
        if (index >= 0 && index < _rings.Count)
        {
            _rings[index].Visible = false;
        }
    }

    /// <summary>撤离：victorious=true 完整加速上升淡出；false 受创慢速（冒烟 + 变暗）。</summary>
    public void Retreat(bool victorious)
    {
        _state = State.RETREAT;
        _retreatSpeed = RetreatStartSpeed;
        _retreatFactor = victorious ? 1.0f : 0.55f;
        if (!victorious)
        {
            _sprite.Modulate = new Color(0.7f, 0.6f, 0.65f);
            // 受创冒烟：甲板几处爆点
            var ws = (float)GameStateBridge.Get("world_scale").AsDouble();
            for (var i = 0; i < 3; i++)
            {
                Explosion.SpawnAt(
                    GetParent(),
                    GlobalPosition + Sockets[i] * ws + new Vector2((float)GD.RandRange(-30.0, 30.0) * ws, 0.0f),
                    0.8f);
            }
        }

        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0.0f, 2.2f);
    }

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        switch (_state)
        {
            case State.ENTER:
                _enterT += d;
                var t = Mathf.Clamp(_enterT / _enterDuration, 0.0f, 1.0f);
                var eased = 1.0f - Mathf.Pow(1.0f - t, 3.0f);
                Position = new Vector2(Position.X, Mathf.Lerp(_startY, _hoverY, eased));
                var m = Modulate;
                m.A = Mathf.Max(m.A, t);
                Modulate = m;
                if (t >= 1.0f)
                {
                    Position = new Vector2(Position.X, _hoverY);
                    m.A = 1.0f;
                    Modulate = m;
                    _state = State.HOVER;
                    EmitSignal(SignalName.Entered);
                }

                break;
            case State.HOVER:
                // 悬停轻微浮动（质量感：慢速小幅）
                _hoverTime += d;
                Position = new Vector2(Position.X, _hoverY + Enemy.SinFast(_hoverTime * 0.8f) * 6.0f);
                break;
            case State.RETREAT:
                _retreatSpeed += RetreatAccel * _retreatFactor * d;
                Position = new Vector2(Position.X, Position.Y - _retreatSpeed * d);
                if (Position.Y < CachedView().Position.Y - 500.0f)
                {
                    EmitSignal(SignalName.Exited);
                    QueueFree();
                }

                break;
        }
    }

    // ---------------- GDScript 鸭子调用兼容桥（过渡，M7 删除） ----------------
    public void enter(float hoverY, float duration) => Enter(hoverY, duration);

    public void set_socket_charging(int index) => SetSocketCharging(index);

    public void set_socket_destroyed(int index) => SetSocketDestroyed(index);

    public void retreat(bool victorious) => Retreat(victorious);

    public float RETREAT_START_SPEED { get => RetreatStartSpeed; set => RetreatStartSpeed = value; }

    public float RETREAT_ACCEL { get => RetreatAccel; set => RetreatAccel = value; }
}
