using Godot;

namespace InfiAir;

/// <summary>
/// 轰炸编队事件·编队战机（docs/FORMATION_STRIKE_EVENT.md §3）：
/// 楔形编队成员，注册 enemy 组与 GameState.Enemies（玩家子弹/激光可命中）；实现 IDamageable。
/// 自身无 AI：位置/朝向由 FormationStrikeEvent._Process 按编队锚点驱动。
/// 被击坠：爆炸 + 注销注册表，击坠得分由事件编排结算。
/// </summary>
public partial class FormationCraft : Area2D, IDamageable
{
    [Signal]
    public delegate void DiedEventHandler(FormationCraft craft);

    /// <summary>机体贴图（原 GDScript preload）。</summary>
    // U07：静态 Godot 资源改实例字段（退出 segfault 实测教训，UITheme.cs:53）
    private readonly Texture2D _texture = GD.Load<Texture2D>("res://assets/sprites/enemy_ship_2.png");

    public int MaxHp { get; set; } = 60;
    public int Hp { get; set; } = 60;

    private Sprite2D? _sprite;
    /// <summary>P1-2：受击闪白手动衰减计时（_PhysicsProcess 逐帧 lerp，替代每命中新建 Tween）。</summary>
    private float _flashTimer;
    private const float FlashTime = 0.1f;
    /// <summary>P1-6：击杀震动强度缓存（_Ready 一次性读入，热路径禁 cfg）。</summary>
    private float _shakeDie = 5.0f;

    /// <summary>setup() 在入树/_Ready() 之前调用。</summary>
    public void Setup(int pHp)
    {
        MaxHp = Mathf.Max(1, pHp);
        Hp = MaxHp;
    }

    public override void _Ready()
    {
        CollisionLayer = 4; // 第 3 层：enemy（玩家子弹以 enemy 组结算）
        CollisionMask = 0;
        _sprite = new Sprite2D
        {
            Texture = _texture,
            Scale = Vector2.One * 0.9f * (float)GameState.Instance.WorldScale, // 设计值 0.9 × 全局缩放
        };
        AddChild(_sprite);
        var shape = new CollisionShape2D();
        var circle = new CircleShape2D { Radius = 26.0f * (float)GameState.Instance.WorldScale };
        shape.Shape = circle;
        AddChild(shape);
        GameState.Instance.BindEnemy(this); // 统一绑定（docs/ENTITY_MANAGER.md）
        // P1-6：击杀震动强度缓存
        _shakeDie = (float)GameState.Instance.Cfg("effects.shake.enemy_die", _shakeDie).AsDouble();
    }

    /// <summary>P1-2：受击闪白逐帧衰减（编队机自身无移动回调，独立物理帧推进闪白；FlashFx 共享实现）。</summary>
    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        if (_flashTimer <= 0.0f)
        {
            return;
        }

        // 判空守卫保留在调用前（timer 早退之后、归色之前，与原顺序一致）
        if (_sprite == null)
        {
            return;
        }

        FlashFx.Update(_sprite, ref _flashTimer, d, FlashTime, Colors.White);
    }

    public override void _ExitTree()
    {
        GameState.Instance.UnbindEnemy(this); // 统一解绑（docs/ENTITY_MANAGER.md）
    }

    public void TakeDamage(int amount, float scoreScale)
    {
        if (Hp <= 0)
        {
            return;
        }

        Hp -= amount;
        // 受击闪白（_sprite 在 _Ready 构建；防御性判空与 _PhysicsProcess 同口径）
        if (_sprite != null)
        {
            _sprite.Modulate = new Color(2.0f, 2.0f, 2.0f);
        }

        _flashTimer = FlashTime;
        if (Hp <= 0)
        {
            Die();
        }
    }

    public void TakeDamage(int amount) => TakeDamage(amount, 1.0f);

    public void Die()
    {
        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION);
        GameState.Instance.Shake(_shakeDie);
        Explosion.SpawnAt(GetParent(), GlobalPosition, 1.0f);
        EmitSignal(SignalName.Died, this);
        QueueFree();
    }

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public void take_damage(int amount, float scoreScale) => TakeDamage(amount, scoreScale);

    public void take_damage(int amount) => TakeDamage(amount);

    public int hp { get => Hp; set => Hp = value; }
}
