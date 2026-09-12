using Godot;

namespace InfiAir;

/// <summary>
/// 轰炸编队事件·编队战机：
/// 楔形编队成员，注册 enemy 组与 GameState.Enemies（玩家子弹/激光可命中）；实现 IDamageable。
/// 自身无 AI：位置/朝向由 FormationStrikeEvent._Process 按编队锚点驱动，侧倾（bank）由事件按
/// 转向/离场进度写入（俯视视角下「压坡转弯」比平推更有质量感）。
/// 身份识别：琥珀色调 + 翼尖航行灯（红/绿）+ 机腹投弹舱照明——与普通敌机同贴图但一眼可区分。
/// 被击坠：爆炸 + 注销注册表，击坠得分由事件编排结算。
/// </summary>
public partial class FormationCraft : Area2D, IDamageable
{
    [Signal]
    public delegate void DiedEventHandler(FormationCraft craft);

    /// <summary>机体贴图。</summary>
    // 静态 Godot 资源改实例字段（退出 segfault 实测教训，UITheme.cs:53）
    private readonly Texture2D _texture = GD.Load<Texture2D>("res://assets/sprites/enemy_ship_2.png");

    public int MaxHp { get; set; } = 60;
    public int Hp { get; set; } = 60;

    /// <summary>编队身份色（琥珀偏橙：与普通敌机的冷灰、精英炮塔的品红区分）。</summary>
    private static readonly Color FormationTint = new(1.0f, 0.82f, 0.62f);

    private Sprite2D? _sprite;
    private GlowDot? _bayLight; // 机腹投弹舱照明（投弹前预警闪烁、投弹瞬间满亮）
    private float _bayFlash;
    /// <summary>投弹预警剩余（投弹前的一小段闪烁窗口：玩家据此判断「这架要投了」）。</summary>
    private float _bayWarn;
    /// <summary>受击闪白手动衰减计时（_PhysicsProcess 逐帧 lerp，替代每命中新建 Tween）。</summary>
    private float _flashTimer;
    private Vector2 _flashBaseScale = Vector2.One; // 受击缩放回弹基准（非闪白期捕获，FlashFx 回位用）
    private const float FlashTime = 0.1f;
    private const float BayFlashTime = 0.18f;
    /// <summary>击杀震动强度缓存（_Ready 一次性读入，热路径禁 cfg）。</summary>
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
            Modulate = FormationTint,
        };
        AddChild(_sprite);
        // 翼尖航行灯（编队身份）+ 机腹投弹舱照明（默认灭，投弹时亮起）
        var ws = (float)GameState.Instance.WorldScale;
        var portLight = CinematicFx.Glow(4.5f * ws, new Color(1.0f, 0.35f, 0.3f, 0.85f));
        var starboardLight = CinematicFx.Glow(4.5f * ws, new Color(0.4f, 1.0f, 0.55f, 0.85f));
        portLight.Position = new Vector2(-26.0f, 4.0f) * ws;
        starboardLight.Position = new Vector2(26.0f, 4.0f) * ws;
        _sprite.AddChild(portLight);
        _sprite.AddChild(starboardLight);
        _bayLight = CinematicFx.Glow(7.0f * ws, new Color(1.0f, 0.78f, 0.35f, 1.0f));
        _bayLight.Position = new Vector2(0.0f, 12.0f) * ws;
        _bayLight.Visible = false;
        _sprite.AddChild(_bayLight);
        var shape = new CollisionShape2D();
        var circle = new CircleShape2D { Radius = 26.0f * (float)GameState.Instance.WorldScale };
        shape.Shape = circle;
        AddChild(shape);
        GameState.Instance.BindEnemy(this); // 统一绑定
        // 击杀震动强度缓存
        _shakeDie = (float)GameState.Instance.Cfg("effects.shake.enemy_die", _shakeDie).AsDouble();
    }

    /// <summary>侧倾写入（事件按转弯/离场进度调用）：节点横向压缩模拟俯视压坡。</summary>
    public void SetBank(float bank01)
    {
        var b = Mathf.Clamp(bank01, 0.0f, 1.0f);
        Scale = new Vector2(Mathf.Lerp(1.0f, 0.82f, b), 1.0f);
    }

    /// <summary>投弹预警（投放前由事件按时刻表提前调用）：机腹灯进入闪烁窗口，
    /// 玩家能看出「哪一架即将投弹」——投弹动作可预判，威胁才是公平的。
    /// 窗口长度由调用方给（唯一口径在事件编排的 BayWarnLead），不在此重复一份时长。</summary>
    public void WarnBay(float duration)
    {
        _bayWarn = Mathf.Max(_bayWarn, Mathf.Max(duration, 0.0f));
        if (_bayLight != null && GodotObject.IsInstanceValid(_bayLight))
        {
            _bayLight.Visible = true;
        }
    }

    /// <summary>投弹舱满亮（每次投弹由事件调用）：投弹动作的确认信号。</summary>
    public void FlashBay()
    {
        _bayWarn = 0.0f; // 投放已发生，预警窗口即刻结束
        _bayFlash = BayFlashTime;
        if (_bayLight != null && GodotObject.IsInstanceValid(_bayLight))
        {
            _bayLight.Visible = true;
        }
    }

    /// <summary>受击闪白逐帧衰减（编队机自身无移动回调，独立物理帧推进闪白；FlashFx 共享实现）。</summary>
    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        if (_flashTimer > 0.0f && _sprite != null)
        {
            FlashFx.Update(_sprite, ref _flashTimer, d, FlashTime, Colors.White, ref _flashBaseScale);
        }

        if (_bayWarn > 0.0f)
        {
            _bayWarn -= d;
        }

        if (_bayFlash <= 0.0f && _bayWarn <= 0.0f)
        {
            if (_bayLight != null && GodotObject.IsInstanceValid(_bayLight) && _bayLight.Visible)
            {
                _bayLight.Visible = false;
            }

            return;
        }

        if (_bayLight == null || !GodotObject.IsInstanceValid(_bayLight))
        {
            return;
        }

        // 投弹舱灯的可见期 = 预警窗口（脉冲）+ 投放窗口（满亮）；两者同源，先满亮后脉冲
        _bayLight.Visible = true;
        float k;
        if (_bayFlash > 0.0f)
        {
            _bayFlash -= d;
            k = Mathf.Clamp(_bayFlash / BayFlashTime, 0.0f, 1.0f);
        }
        else
        {
            k = 0.35f + (0.45f * Mathf.Abs(Enemy.SinFast(_bayWarn * 42.0f))); // 预警脉冲
        }

        _bayLight.Modulate = new Color(1.0f, 1.0f, 1.0f, k);
        if (_bayFlash <= 0.0f && _bayWarn <= 0.0f)
        {
            _bayLight.Visible = false;
        }
    }

    public override void _ExitTree()
    {
        GameState.Instance.UnbindEnemy(this); // 统一解绑
    }

    public void TakeDamage(int amount, float scoreScale)
    {
        if (Hp <= 0)
        {
            return;
        }

        Hp -= amount;
        // 受击闪白 + 缩放回弹（_sprite 在 _Ready 构建；防御性判空与 _PhysicsProcess 同口径）
        if (_sprite != null)
        {
            FlashFx.Hit(_sprite, ref _flashTimer, FlashTime, ref _flashBaseScale);
        }
        else
        {
            _flashTimer = FlashTime;
        }

        if (Hp <= 0)
        {
            Die();
        }
    }

    public void TakeDamage(int amount) => TakeDamage(amount, 1.0f);

    public void Die()
    {
        GameState.Instance.PlaySfx(SfxId.Explosion);
        GameState.Instance.Shake(_shakeDie);
        Explosion.SpawnAt(GetParent(), GlobalPosition, 1.0f);
        EmitSignal(SignalName.Died, this);
        QueueFree();
    }
}
