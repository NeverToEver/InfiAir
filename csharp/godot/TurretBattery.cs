using Godot;

namespace InfiAir;

/// <summary>
/// 精英炮塔事件炮台：航母甲板上升起的独立可摧毁单位（docs/ELITE_TURRET_EVENT.md；
/// 2026-08-08 自 scripts/turret_battery.gd 迁移）。
/// 弱锁定索敌：炮塔以限速转向玩家，开火朝向 = 当前朝向 + ±spread_deg 出膛散布；
/// 弹药按预设序列轮换（全部复用敌侧弹种，参数读 enemies/boss 配置段）。
/// 升起期间不可被攻击（monitorable=false 为主机制，K09；monitoring 口径同步关闭）；
/// 被毁时爆炸 + 基座环熄灭（由事件编排处理）。
/// 迁移期：GameState 经 GameStateBridge（热路径 player_ref 帧缓存）；BulletPool/Bullet 为 C# 类
/// 经动态派发（Call/Set，Bullet 的 SetMeta 不注册引擎表——snake_case "set_meta"）；
/// HpBar 为 GDScript SegmentedBar（untyped Control，自定义属性经引擎 Set 走属性 setter）。
/// </summary>
public partial class TurretBattery : Area2D
{
    [Signal]
    public delegate void DiedEventHandler(TurretBattery turret);

    // ---- 弹药速度/伤害（读 balance.json enemies/boss 段，脚本值为缺键回退） ----
    public float SingleSpeed { get; private set; } = 420.0f;
    public float SpreadSpeed { get; private set; } = 340.0f;
    public float LaserSpeed { get; private set; } = 720.0f;
    public float HomingSpeed { get; private set; } = 300.0f;
    public float SniperSpeed { get; private set; } = 650.0f;
    public float SpreadFanStep { get; private set; } = 0.314159f;
    public int DmgSingle { get; private set; } = 12;
    public int DmgSpread { get; private set; } = 10;
    public int DmgLaser { get; private set; } = 20;
    public int DmgHoming { get; private set; } = 12;
    public int DmgSniper { get; private set; } = 21;

    public int MaxHp { get; set; } = 80;
    public int Hp { get; set; } = 80;
    /// <summary>弹药轮换序列（StringName：single/spread3/spread5/laser/weak_homing/sniper）。</summary>
    public Godot.Collections.Array AmmoSequence { get; set; } = new() { new StringName("single") };
    /// <summary>开火间隔范围（每座炮台独立计时）。</summary>
    public Vector2 FireInterval { get; set; } = new(2.0f, 2.4f);
    // ---- 弱锁定参数 ----
    public float TurnRate { get; set; } = 2.0f; // 炮塔转向速度上限（rad/s，机械转台感）
    public float HomingTurnRate { get; set; } = 1.5f;
    public float HomingTime { get; set; } = 0.6f;
    public float SpreadDeg { get; set; } = 7.0f;

    private bool _rising;
    private bool _ceased;
    /// <summary>P1-2：受击闪白手动衰减计时（_physics_process 逐帧 lerp，替代每命中新建 Tween）。</summary>
    private float _flashTimer;
    private const float FlashTime = 0.1f;
    /// <summary>P1-6：击杀震动强度缓存（_ready 一次性读入，热路径禁 cfg）。</summary>
    private float _shakeDie = 5.0f;

    private float _fireTimer;
    private int _ammoIndex;
    /// <summary>当前朝向（炮口方向，初始直指下方玩家区域）。</summary>
    private float _facing = Mathf.Pi / 2.0f;

    private Sprite2D _sprite = null!; // _Ready 赋值（tscn 固定结构）
    private Control _hpBar = null!; // _Ready 赋值（tscn 固定结构）
    private float _muzzleOffset; // 出弹点偏移（40 × world_scale，_ready 覆写）

    /// <summary>热路径缓存：player_ref 每物理帧一次动态调用（全实例共享）。</summary>
    private static ulong _frame = ulong.MaxValue;
    private static Variant _framePlayer;

    private static Variant CachedPlayer()
    {
        var f = Engine.GetPhysicsFrames();
        if (f != _frame)
        {
            _frame = f;
            _framePlayer = GameStateBridge.Get("player_ref");
        }

        return _framePlayer;
    }

    /// <summary>Setup() 在入树/_Ready() 之前调用，不能用 GetNode。</summary>
    public void Setup(int pHp, Godot.Collections.Array pAmmo, Vector2 pFireInterval, Godot.Collections.Dictionary weakLock)
    {
        MaxHp = Mathf.Max(1, pHp);
        Hp = MaxHp;
        AmmoSequence.Clear();
        foreach (var a in pAmmo)
        {
            AmmoSequence.Add(a.AsStringName());
        }

        FireInterval = pFireInterval;
        TurnRate = (float)weakLock.GetValueOrDefault("turn_rate", TurnRate).AsDouble();
        HomingTurnRate = (float)weakLock.GetValueOrDefault("homing_turn_rate", HomingTurnRate).AsDouble();
        HomingTime = (float)weakLock.GetValueOrDefault("homing_time", HomingTime).AsDouble();
        SpreadDeg = (float)weakLock.GetValueOrDefault("spread_deg", SpreadDeg).AsDouble();
    }

    public override void _Ready()
    {
        GameStateBridge.Call("bind_enemy", this); // 统一绑定（docs/ENTITY_MANAGER.md）
        // 数值配置缓存（启动一次读入）
        SingleSpeed = (float)GameStateBridge.Call("cfg", "enemies.bullet_speed", SingleSpeed).AsDouble();
        SpreadSpeed = (float)GameStateBridge.Call("cfg", "enemies.spread_bullet_speed", SpreadSpeed).AsDouble();
        LaserSpeed = (float)GameStateBridge.Call("cfg", "enemies.laser_bullet_speed", LaserSpeed).AsDouble();
        HomingSpeed = (float)GameStateBridge.Call("cfg", "boss.homing_bullet_speed", HomingSpeed).AsDouble();
        SniperSpeed = (float)GameStateBridge.Call("cfg", "boss.sniper_bullet_speed", SniperSpeed).AsDouble();
        SpreadFanStep = (float)GameStateBridge.Call("cfg", "enemies.spread_fan_step", SpreadFanStep).AsDouble();
        DmgSingle = (int)GameStateBridge.Call("cfg", "enemies.bullet_damage.single", DmgSingle).AsInt64();
        DmgSpread = (int)GameStateBridge.Call("cfg", "enemies.bullet_damage.spread", DmgSpread).AsInt64();
        DmgLaser = (int)GameStateBridge.Call("cfg", "enemies.bullet_damage.laser", DmgLaser).AsInt64();
        DmgHoming = (int)GameStateBridge.Call("cfg", "boss.bullet_damage.homing", DmgHoming).AsInt64();
        DmgSniper = (int)GameStateBridge.Call("cfg", "boss.bullet_damage.sniper", DmgSniper).AsInt64();
        // 机体尺寸族：设计值 × 全局缩放（tscn 存 1.0 基准，幂等覆盖）
        var ws = (float)GameStateBridge.Get("world_scale").AsDouble();
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _sprite.Scale = Vector2.One * ws;
        if (GetNode<CollisionShape2D>("CollisionShape2D").Shape is CircleShape2D bodyCircle)
        {
            bodyCircle.Radius = 26.0f * ws;
        }

        _hpBar = GetNode<Control>("HpBar");
        _hpBar.OffsetLeft = -24.0f * ws;
        _hpBar.OffsetTop = -46.0f * ws;
        _hpBar.OffsetRight = 24.0f * ws;
        _hpBar.OffsetBottom = -38.0f * ws;
        _muzzleOffset = 40.0f * ws;
        _hpBar.Set("max_value", 100.0f);
        _hpBar.Set("value", 100.0f);
        _hpBar.Set("fill_color", new Color(1.0f, 0.25f, 0.75f)); // 精英品红
        _fireTimer = (float)GD.RandRange(FireInterval.X, FireInterval.Y);
        // P1-6：击杀震动强度缓存
        _shakeDie = (float)GameStateBridge.Call("cfg", "effects.shake.enemy_die", _shakeDie).AsDouble();
    }

    public override void _ExitTree()
    {
        GameStateBridge.Call("unbind_enemy", this); // 统一解绑（docs/ENTITY_MANAGER.md）
    }

    /// <summary>升起充能动画（盖板旋开炮塔升起，约 rise_time 秒；期间不可被攻击）。</summary>
    public void Rise(float duration)
    {
        _rising = true;
        // K09：monitorable=false 才是「不可被攻击」的正确机制——monitoring 只控制本 Area
        // 检测别人，玩家弹命中与否取决于弹侧 monitoring + 本侧 monitorable；原 monitoring=false
        // 不阻止玩家弹 area_entered（弹丸命中被 take_damage 守卫吃掉，白白销毁）
        Monitoring = false;
        Monitorable = false;
        Scale = Vector2.Zero;
        var m = Modulate;
        m.A = 0.0f;
        Modulate = m;
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", Vector2.One, duration).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "modulate:a", 1.0f, duration * 0.6f);
    }

    /// <summary>充能完毕（由事件编排在倒计时开始时调用）：可被攻击、开始开火。</summary>
    public void Activate()
    {
        _rising = false;
        Monitoring = true;
        Monitorable = true;
    }

    /// <summary>超时撤退：停火并收回盖板（弹药不再产生）。</summary>
    public void CeaseFireAndRetract()
    {
        if (_ceased || Hp <= 0)
        {
            return;
        }

        _ceased = true;
        Monitoring = false;
        Monitorable = false; // K09：同 rise 期——收回动画期间玩家弹应穿过而非被白吃
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", Vector2.Zero, 0.8f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        tween.TweenProperty(this, "modulate:a", 0.0f, 0.8f);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        // P1-2：受击闪白手动衰减（rising/ceased 时也推进，闪白不残留）
        UpdateFlash(d);
        if (_rising || _ceased || Hp <= 0)
        {
            return;
        }

        // 弱锁定索敌：限速转向玩家（lerp_angle 缓动 + rad/s 上限，机械转台感）
        var player = CachedPlayer();
        if (player.VariantType != Variant.Type.Nil)
        {
            var target = (((Node2D)player).GlobalPosition - GlobalPosition).Angle();
            var maxStep = TurnRate * d;
            var diff = Mathf.Wrap(target - _facing, -Mathf.Pi, Mathf.Pi);
            _facing += Mathf.Clamp(diff, -maxStep, maxStep);
            _sprite.Rotation = _facing - Mathf.Pi / 2.0f; // 贴图炮口朝上（-Y），旋转到朝向
        }

        _fireTimer -= d;
        if (_fireTimer <= 0.0f)
        {
            _fireTimer = (float)GD.RandRange(FireInterval.X, FireInterval.Y);
            FireCurrentAmmo();
        }
    }

    /// <summary>开火朝向 = 炮塔当前朝向 + ±spread_deg 出膛散布（非精确指向）。</summary>
    private Vector2 FireDir() => Vector2.Right.Rotated(_facing + Mathf.DegToRad((float)GD.RandRange(-SpreadDeg, SpreadDeg)));

    private void FireCurrentAmmo()
    {
        if (AmmoSequence.Count == 0)
        {
            return;
        }

        var ammo = AmmoSequence[_ammoIndex % AmmoSequence.Count].AsStringName();
        _ammoIndex += 1;
        if (ammo == "spread3")
        {
            FireFan(3);
        }
        else if (ammo == "spread5")
        {
            FireFan(5);
        }
        else if (ammo == "laser")
        {
            SpawnBullet(FireDir(), LaserSpeed, DmgLaser, new StringName("laser"));
        }
        else if (ammo == "weak_homing")
        {
            var dir = FireDir();
            var pool = (GodotObject?)GameStateBridge.Get("bullet_pool");
            if (pool == null)
            {
                return;
            }

            var b = pool.Call("Fire", dir, HomingSpeed, DmgHoming, false, true, HomingTime);
            if (b.VariantType == Variant.Type.Nil)
            {
                return; // P2-3：同屏敌弹硬上限，本次开火放弃
            }

            ((Bullet)b).HomingTurnRate = HomingTurnRate;
            ((GodotObject)b).Set("position", GlobalPosition + dir * _muzzleOffset);
            ((GodotObject)b).Call("set_meta", "bullet_type", new StringName("homing"));
        }
        else if (ammo == "sniper")
        {
            SpawnBullet(FireDir(), SniperSpeed, DmgSniper, new StringName("sniper"));
        }
        else
        {
            SpawnBullet(FireDir(), SingleSpeed, DmgSingle, new StringName("single"));
        }
    }

    /// <summary>扇形散射：以开火朝向为中心 ±(n-1)/2 步展开。</summary>
    private void FireFan(int count)
    {
        var center = FireDir();
        var half = (count - 1) / 2.0f;
        for (var i = 0; i < count; i++)
        {
            SpawnBullet(center.Rotated(SpreadFanStep * (i - half)), SpreadSpeed, DmgSpread, new StringName("spread"));
        }
    }

    private void SpawnBullet(Vector2 dir, float bulletSpeed, int dmg, StringName pType)
    {
        var pool = (GodotObject?)GameStateBridge.Get("bullet_pool");
        if (pool == null)
        {
            return;
        }

        var b = pool.Call("Fire", dir, bulletSpeed, dmg, false);
        if (b.VariantType == Variant.Type.Nil)
        {
            return; // P2-3：同屏敌弹硬上限，本次开火放弃
        }

        var bullet = (GodotObject)b;
        bullet.Set("position", GlobalPosition + dir * _muzzleOffset);
        bullet.Call("set_meta", "bullet_type", pType);
        if (pType == "laser")
        {
            // 细长高亮快速弹（与敌机 laser 弹同表现，polygon 尖端朝 +x 即飞行方向）
            var poly = (Sprite2D?)bullet.Call("SpriteNode");
            if (poly != null)
            {
                poly.Scale = new Vector2(2.2f, 0.55f);
                poly.SelfModulate = new Color(1.0f, 0.85f, 0.35f); // P0-3：Sprite2D 无 color，用 self_modulate
            }
        }
    }

    public void TakeDamage(int amount) => TakeDamage(amount, 1.0f);

    public void TakeDamage(int amount, float scoreScale)
    {
        if (Hp <= 0 || _rising || _ceased)
        {
            return;
        }

        Hp -= amount;
        _hpBar.Set("value", Mathf.Clamp(Hp / (float)MaxHp, 0.0f, 1.0f) * 100.0f);
        _sprite.Modulate = new Color(2.0f, 2.0f, 2.0f); // 受击闪白
        _flashTimer = FlashTime;
        if (Hp <= 0)
        {
            Die();
        }
    }

    /// <summary>P1-2：受击闪白手动衰减（替代 Tween；线性 lerp 回本色，零分配）。</summary>
    private void UpdateFlash(float delta)
    {
        if (_flashTimer <= 0.0f)
        {
            return;
        }

        _flashTimer -= delta;
        if (_flashTimer <= 0.0f)
        {
            _sprite.Modulate = Colors.White;
        }
        else
        {
            _sprite.Modulate = _sprite.Modulate.Lerp(Colors.White, delta / FlashTime);
        }
    }

    public void Die()
    {
        GameStateBridge.Call("play_sfx", GameStateBridge.Get("SFX_EXPLOSION"));
        GameStateBridge.Call("shake", _shakeDie);
        Explosion.SpawnAt(GetParent(), GlobalPosition, 1.0f);
        EmitSignal(SignalName.Died, this);
        QueueFree();
    }

    // ---------------- GDScript 鸭子调用兼容桥（过渡，M7 删除） ----------------
    // 测试（test/elite_turret_event_test.gd）与 Bullet（C# 动态派发 take_damage）以 snake_case 访问。

    public bool Ceased() => _ceased;

    public bool ceased() => Ceased();

    public void setup(int pHp, Godot.Collections.Array pAmmo, Vector2 pFireInterval, Godot.Collections.Dictionary weakLock)
        => Setup(pHp, pAmmo, pFireInterval, weakLock);

    public void rise(float duration) => Rise(duration);

    public void activate() => Activate();

    public void cease_fire_and_retract() => CeaseFireAndRetract();

    public void take_damage(int amount, float scoreScale) => TakeDamage(amount, scoreScale);

    public void take_damage(int amount) => TakeDamage(amount);

    public void die() => Die();

    public int max_hp { get => MaxHp; set => MaxHp = value; }

    public int hp { get => Hp; set => Hp = value; }

    public Godot.Collections.Array ammo_sequence { get => AmmoSequence; set => AmmoSequence = value; }

    public Vector2 fire_interval { get => FireInterval; set => FireInterval = value; }

    public float turn_rate { get => TurnRate; set => TurnRate = value; }

    public float homing_turn_rate { get => HomingTurnRate; set => HomingTurnRate = value; }

    public float homing_time { get => HomingTime; set => HomingTime = value; }

    public float spread_deg { get => SpreadDeg; set => SpreadDeg = value; }

    public float SINGLE_SPEED { get => SingleSpeed; set => SingleSpeed = value; }

    public float SPREAD_SPEED { get => SpreadSpeed; set => SpreadSpeed = value; }

    public float LASER_SPEED { get => LaserSpeed; set => LaserSpeed = value; }

    public float HOMING_SPEED { get => HomingSpeed; set => HomingSpeed = value; }

    public float SNIPER_SPEED { get => SniperSpeed; set => SniperSpeed = value; }

    public float SPREAD_FAN_STEP { get => SpreadFanStep; set => SpreadFanStep = value; }

    public int DMG_SINGLE { get => DmgSingle; set => DmgSingle = value; }

    public int DMG_SPREAD { get => DmgSpread; set => DmgSpread = value; }

    public int DMG_LASER { get => DmgLaser; set => DmgLaser = value; }

    public int DMG_HOMING { get => DmgHoming; set => DmgHoming = value; }

    public int DMG_SNIPER { get => DmgSniper; set => DmgSniper = value; }
}
