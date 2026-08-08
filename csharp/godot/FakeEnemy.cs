using Godot;

namespace InfiAir;

/// <summary>
/// 伪敌机（迷雾事件·fake_enemies；M 批次全量迁移）：无伤害/无碰撞的幽灵敌机，纯视觉干扰。
/// 复用敌机贴图 + 幽灵闪烁（半透明青白调），不注册 GameState.enemies、不入 "enemy" 组、
/// 无碰撞形状——玩家子弹直接穿过（不结算、不消耗穿透），不参与任何对局系统
/// （辅助瞄准标记/击杀/分数/波次上限/注册表一致性均不受影响）。
/// 行为：可选入场延迟（错峰）→ 自屏幕顶降入 → 悬停带内水平摇摆，事件结束统一移除。
/// 迁移期：FakeEnemiesEvent（C#）直接 new + 强类型访问；GDScript 侧如实例化须
/// load("res://csharp/godot/FakeEnemy.cs").new() 并经 snake_case 动态访问。
/// </summary>
public partial class FakeEnemy : Node2D
{
    private static readonly Texture2D[] FakeTextures =
    {
        GD.Load<Texture2D>("res://assets/sprites/enemy_ship_1.png"),
        GD.Load<Texture2D>("res://assets/sprites/enemy_ship_2.png"),
    };

    /// <summary>幽灵外观：半透明青白调 + 正弦闪烁。</summary>
    private static readonly Color GhostTint = new(0.75f, 0.9f, 1.0f, 0.55f);

    private const float FlickerAmplitude = 0.18f;
    private const float FlickerFreq = 10.0f;
    /// <summary>悬停带（相对可见区域顶缘偏移，对齐 enemies.hover_band 量级）。</summary>
    private static readonly Vector2 HoverBand = new(150.0f, 430.0f);
    private const float DescendSpeed = 180.0f;
    private const float SwayAmp = 30.0f;
    private const float SwayFreq = 1.2f;

    /// <summary>错峰入场延迟（FakeEnemiesEvent 按 spawn_interval 分配）。</summary>
    public float EnterDelay { get; set; }

    private Sprite2D? _sprite;
    private float _t;
    private bool _entered;
    private float _hoverY;
    private float _startX;

    /// <summary>热路径缓存：view_world_rect(280) 每物理帧一次动态调用（全伪敌机共享）。</summary>
    private static ulong _frame = ulong.MaxValue;
    private static Rect2 _frameView280;

    private static Rect2 CachedView280()
    {
        var f = Engine.GetPhysicsFrames();
        if (f != _frame)
        {
            _frame = f;
            _frameView280 = GameState.Instance.ViewWorldRect(280.0);
        }

        return _frameView280;
    }

    public override void _Ready()
    {
        _startX = Position.X;
        var sprite = new Sprite2D();
        _sprite = sprite;
        sprite.Texture = FakeTextures[(int)(GD.Randi() % (uint)FakeTextures.Length)];
        sprite.Scale = Vector2.One * (float)GD.RandRange(0.55, 0.68) * (float)GameState.Instance.WorldScale;
        sprite.Modulate = GhostTint;
        AddChild(sprite);
        if (EnterDelay > 0.0f)
        {
            Visible = false;
            var t = new Godot.Timer { OneShot = true, WaitTime = EnterDelay };
            t.Timeout += () => OnDelayDone(t);
            AddChild(t);
            t.Start();
        }
        else
        {
            OnDelayDone(null);
        }
    }

    private void OnDelayDone(Godot.Timer? t)
    {
        if (t != null && GodotObject.IsInstanceValid(t))
        {
            t.QueueFree();
        }

        if (!GodotObject.IsInstanceValid(this) || IsQueuedForDeletion())
        {
            return;
        }

        Visible = true;
        _entered = true;
        // 锚点 = 出生点下方 120~260，钳入悬停带（对齐 Enemy._resolve_anchor 量级）
        var view = GameState.Instance.ViewWorldRect();
        _hoverY = Mathf.Clamp(
            Position.Y + (float)GD.RandRange(120.0, 260.0), view.Position.Y + HoverBand.X, view.Position.Y + HoverBand.Y);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_entered)
        {
            return;
        }

        var d = (float)delta;
        _t += d;
        // 幽灵闪烁（alpha 正弦，视觉干扰）
        var sprite = _sprite!;
        var m = sprite.Modulate;
        m.A = GhostTint.A + Mathf.Sin(_t * FlickerFreq) * FlickerAmplitude;
        sprite.Modulate = m;
        if (Position.Y < _hoverY)
        {
            // 下降期同步水平微摆（错开全波机械感）
            Position = new Vector2(
                _startX + Mathf.Sin(_t * SwayFreq * 0.5f) * SwayAmp * 0.5f,
                Mathf.Min(Position.Y + DescendSpeed * d, _hoverY));
        }
        else
        {
            Position = new Vector2(_startX + Mathf.Sin(_t * SwayFreq) * SwayAmp, Position.Y);
        }

        // 出屏销毁兜底（正常路径由 FogEventManager 在事件结束时统一移除，此路径防事件异常残留）。
        // 2026-08-06 审计 M3：余量对齐最大出生深度（事件侧出生 y = 视野顶 − randf(20,260)）——
        // 原 80px 余量使约 75% 个体出生即被销毁（幽灵机群实际可见 1-2 只，违背错峰入场设计）
        if (!CachedView280().HasPoint(Position))
        {
            QueueFree();
        }
    }

    // ---------------- GDScript 鸭子调用兼容桥（M 批次过渡，M7 删除） ----------------

    public float enter_delay { get => EnterDelay; set => EnterDelay = value; }
}
