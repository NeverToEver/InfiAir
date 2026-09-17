using Godot;
using InfiAir.Core.Combat;

namespace InfiAir;

/// <summary>
/// 精英炮塔事件炮台：航母甲板上升起的独立可摧毁单位。
/// 弱锁定索敌：炮塔以限速转向玩家，开火朝向 = 当前朝向 + ±spread_deg 出膛散布；
/// 弹药按预设序列轮换（全部复用敌侧弹种，参数读 enemies/boss 配置段）。
/// 升起期间不可被攻击（monitorable=false 为主机制；monitoring 口径同步关闭）；
/// 被毁时爆炸 + 基座环熄灭（由事件编排处理）。
/// HpBar 为 C# SegmentedBar 直调；实现 IDamageable，伤害经 EntityDamage 统一分派。
/// 实现 IAimTarget：与普通敌机同吃辅助瞄准（遭遇期间它是屏上唯一可打目标）。
/// </summary>
public partial class TurretBattery : Area2D, IDamageable, IAimTarget
{
    [Signal]
    public delegate void DiedEventHandler(TurretBattery turret);

    // 开火热路径 StringName 静态缓存——避免每发炮弹 new StringName/字符串字面量比较
    private static readonly StringName AmmoSingle = new("single");
    private static readonly StringName AmmoSpread = new("spread");
    private static readonly StringName AmmoLaser = new("laser");
    private static readonly StringName AmmoHoming = new("homing");
    private static readonly StringName AmmoSniper = new("sniper");
    private static readonly StringName AmmoSpread3 = new("spread3");
    private static readonly StringName AmmoSpread5 = new("spread5");
    private static readonly StringName AmmoWeakHoming = new("weak_homing");

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

    /// <summary>击毁入账的击杀分（balance.json elite_turret_event.turret_score，事件编排注入）：
    /// 走 AddKillScore，吃连击/score_amp/难度档倍率——与普通敌机同口。</summary>
    public int ScoreValue { get; set; } = 150;
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
    /// <summary>受击闪白手动衰减计时（_physics_process 逐帧 lerp，替代每命中新建 Tween）。</summary>
    private float _flashTimer;
    private Vector2 _flashBaseScale = Vector2.One; // 受击缩放回弹基准（非闪白期捕获，FlashFx 回位用）
    private const float FlashTime = 0.1f;
    /// <summary>击杀震动强度缓存（_ready 一次性读入，热路径禁 cfg）。</summary>
    private float _shakeDie = 5.0f;

    private float _fireTimer;
    private int _ammoIndex;
    /// <summary>当前朝向（炮口方向，初始直指下方玩家区域）。</summary>
    private float _facing = Mathf.Pi / 2.0f;

    private Sprite2D _sprite = null!; // _Ready 赋值（tscn 固定结构）
    private SegmentedBar _hpBar = null!; // _Ready 赋值（tscn 固定结构）
    private float _muzzleOffset; // 出弹点偏移（40 × world_scale，_ready 覆写）
    /// <summary>碰撞半径缓存（_Ready 按机体尺寸族写入，26 × world_scale）——辅助框半宽的基数
    /// （不读节点：扫描是逐帧热路径，节点查询太贵）。</summary>
    private float _bodyRadius;
    /// <summary>辅助瞄准标记登记态（true = 已登记 → 屏上画框并吃强追踪）。</summary>
    private bool _aimMarked;

    /// <summary>Setup() 在入树/_Ready() 之前调用，不能用 GetNode。</summary>
    public void Setup(int pHp, Godot.Collections.Array pAmmo, Vector2 pFireInterval, Godot.Collections.Dictionary weakLock)
    {
        MaxHp = Mathf.Max(1, pHp);
        Hp = MaxHp;
        AmmoSequence.Clear();
        foreach (var a in pAmmo)
        {
            // 元素级判型口径（对齐 EliteTurretEvent 弹药序列）：混入非字符串元素时
            // AsStringName() 抛 InvalidCastException，坏值跳过；全坏回退单发 single
            if (a.VariantType == Variant.Type.String || a.VariantType == Variant.Type.StringName)
            {
                AmmoSequence.Add(a.AsStringName());
            }
        }

        if (AmmoSequence.Count == 0)
        {
            AmmoSequence.Add(AmmoSingle);
        }

        FireInterval = pFireInterval;
        // 弱锁四键逐键判型 + 域钳（口径单源在 core EncounterConfig）：元素写成字符串/数组时
        // AsDouble 宽松转换得 0（不抛），turn_rate=0 让炮台不再转向、弹道永远朝下；
        // 负值经转向钳制区间倒置会变成「瞬间对准玩家」。转向/散布钳 ≥0，时长钳正下限。
        TurnRate = ReadWeakLock(weakLock, "turn_rate", TurnRate, 0.0f);
        HomingTurnRate = ReadWeakLock(weakLock, "homing_turn_rate", HomingTurnRate, 0.0f);
        HomingTime = ReadWeakLock(weakLock, "homing_time", HomingTime, CfgFx.IntervalFloor);
        SpreadDeg = ReadWeakLock(weakLock, "spread_deg", SpreadDeg, 0.0f);
    }

    /// <summary>弱锁单键读取：判型失败/非有限/负值回退默认，合法值钳到 <paramref name="floor"/> 之上。</summary>
    private static float ReadWeakLock(Godot.Collections.Dictionary cfg, string key, float fallback, float floor)
    {
        var v = cfg.GetValueOrDefault(key, new Variant());
        var valid = v.VariantType is Variant.Type.Int or Variant.Type.Float;
        return EncounterConfig.RangeEndpoint(valid, valid ? (float)v.AsDouble() : 0.0f, fallback, floor);
    }

    // ---- IAimTarget 契约（辅助瞄准扫描只读量） ----

    /// <summary>可打 = 未被摧毁、不在升起充能期、不在收回离场期（与 TakeDamage 的守卫同口径）。</summary>
    public bool AimTargetable => Hp > 0 && !_rising && !_ceased;

    /// <summary>标记态与计数同源：登记即标记（<see cref="SetAimMarked"/>）。</summary>
    public bool AimMarked => _aimMarked;

    /// <summary>世界坐标（炮台挂在航母节点下，取全局坐标）。</summary>
    public Vector2 AimWorldPosition => GlobalPosition;

    /// <summary>碰撞半径（已含 world_scale）；辅助框半宽 = 本值 + 档位 frame_pad。</summary>
    public float AimCollisionRadius => _bodyRadius;

    /// <summary>标记登记（幂等）：改标记即改计数——AimFrameLayer 的零标记早退依赖计数准确。
    /// 升起前/收回后不计（那两段不可被攻击，画框会误导）。</summary>
    private void SetAimMarked(bool marked)
    {
        if (_aimMarked == marked)
        {
            return;
        }

        _aimMarked = marked;
        AimTargetCount.SetEncounterMarked(marked);
    }

    public override void _Ready()
    {
        GameState.Instance.BindEnemy(this); // 统一绑定
        // 数值配置缓存（启动一次读入）
        SingleSpeed = CfgFx.Float("enemies.bullet_speed", SingleSpeed);
        SpreadSpeed = CfgFx.Float("enemies.spread_bullet_speed", SpreadSpeed);
        LaserSpeed = CfgFx.Float("enemies.laser_bullet_speed", LaserSpeed);
        HomingSpeed = CfgFx.Float("boss.homing_bullet_speed", HomingSpeed);
        SniperSpeed = CfgFx.Float("boss.sniper_bullet_speed", SniperSpeed);
        SpreadFanStep = CfgFx.Float("enemies.spread_fan_step", SpreadFanStep);
        DmgSingle = CfgFx.Int("enemies.bullet_damage.single", DmgSingle);
        DmgSpread = CfgFx.Int("enemies.bullet_damage.spread", DmgSpread);
        DmgLaser = CfgFx.Int("enemies.bullet_damage.laser", DmgLaser);
        DmgHoming = CfgFx.Int("boss.bullet_damage.homing", DmgHoming);
        DmgSniper = CfgFx.Int("boss.bullet_damage.sniper", DmgSniper);
        // 机体尺寸族：设计值 × 全局缩放（tscn 存 1.0 基准，幂等覆盖）
        var ws = (float)GameState.Instance.WorldScale;
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _sprite.Scale = Vector2.One * ws;
        _bodyRadius = 26.0f * ws;
        if (GetNode<CollisionShape2D>("CollisionShape2D").Shape is CircleShape2D bodyCircle)
        {
            bodyCircle.Radius = _bodyRadius;
        }

        _hpBar = GetNode<SegmentedBar>("HpBar");
        _hpBar.OffsetLeft = -24.0f * ws;
        _hpBar.OffsetTop = -46.0f * ws;
        _hpBar.OffsetRight = 24.0f * ws;
        _hpBar.OffsetBottom = -38.0f * ws;
        _muzzleOffset = 40.0f * ws;
        _hpBar.MaxValue = 100.0f;
        _hpBar.Value = 100.0f;
        _hpBar.FillColor = UITheme.EventMagenta; // 精英品红（调色板单源）
        _fireTimer = (float)GD.RandRange(FireInterval.X, FireInterval.Y);
        // 击杀震动强度缓存
        _shakeDie = CfgFx.Float("effects.shake.enemy_die", _shakeDie);
    }

    public override void _ExitTree()
    {
        // 标记计数离树兜底（幂等）：事件 Abort/超时直接 QueueFree 的炮台不经 Die，
        // 漏减会让 AimFrameLayer 永远走「有标记」分支（每帧全表扫描 + 重绘）
        SetAimMarked(false);
        GameState.TryGetInstance()?.UnbindEnemy(this); // 统一解绑；autoload 可能先于本节点释放
    }

    /// <summary>升起充能动画（盖板旋开炮塔升起，约 rise_time 秒；期间不可被攻击）。</summary>
    public void Rise(float duration)
    {
        _rising = true;
        // monitorable=false 才是「不可被攻击」的正确机制——monitoring 只控制本 Area
        // 检测别人，玩家弹命中与否取决于弹侧 monitoring + 本侧 monitorable；monitoring=false
        // 不阻止玩家弹 area_entered（玩家弹命中被 take_damage 守卫吃掉，白白销毁）
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

    /// <summary>充能完毕（由事件编排在倒计时开始时调用）：可被攻击、开始开火、纳入辅助瞄准标记。</summary>
    public void Activate()
    {
        _rising = false;
        Monitoring = true;
        Monitorable = true;
        SetAimMarked(true);
    }

    /// <summary>超时撤退：停火并收回盖板（弹药不再产生）。</summary>
    public void CeaseFireAndRetract()
    {
        if (_ceased || Hp <= 0)
        {
            return;
        }

        _ceased = true;
        SetAimMarked(false); // 收回期不可被攻击：撤框，免得玩家追着一个打不动的目标
        Monitoring = false;
        Monitorable = false; // 同 rise 期——收回动画期间玩家弹应穿过而非被白吃
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", Vector2.Zero, 0.8f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        tween.TweenProperty(this, "modulate:a", 0.0f, 0.8f);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        // 受击闪白手动衰减（rising/ceased 时也推进，闪白不残留）
        UpdateFlash(d);
        if (_rising || _ceased || Hp <= 0)
        {
            return;
        }

        // 弱锁定索敌：限速转向玩家（lerp_angle 缓动 + rad/s 上限，机械转台感）
        var player = FrameCache.Player();
        if (player != null)
        {
            var target = (player.GlobalPosition - GlobalPosition).Angle();
            var maxStep = TurnRate * d;
            var diff = Mathf.Wrap(target - _facing, -Mathf.Pi, Mathf.Pi);
            _facing += Mathf.Clamp(diff, -maxStep, maxStep);
            // 贴图炮口朝画布上缘（生成器 turret() 的炮身在基座之上、炮口制退环/能量核在顶端），
            // turret.tscn 根节点与 Sprite2D 均无补偿 rotation；换算见 core TurretAim。
            // 原式 _facing - π/2 按「炮口朝下」推导，炮口与弹道反 180°（从基座方向出弹）。
            _sprite.Rotation = Core.Combat.TurretAim.SpriteRotation(_facing);
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
        if (ammo == AmmoSpread3)
        {
            FireFan(3);
        }
        else if (ammo == AmmoSpread5)
        {
            FireFan(5);
        }
        else if (ammo == AmmoLaser)
        {
            SpawnBullet(FireDir(), LaserSpeed, DmgLaser, AmmoLaser);
        }
        else if (ammo == AmmoWeakHoming)
        {
            var dir = FireDir();
            var pool = GameState.Instance.BulletPool;
            if (pool == null)
            {
                return;
            }

            var b = pool!.Fire(dir, HomingSpeed, DmgHoming, false, true, HomingTime);
            if (b == null)
            {
                return; // 同屏敌弹硬上限，本次开火放弃
            }

            b.HomingTurnRate = HomingTurnRate;
            b.Position = GlobalPosition + dir * _muzzleOffset;
        }
        else if (ammo == AmmoSniper)
        {
            SpawnBullet(FireDir(), SniperSpeed, DmgSniper, AmmoSniper);
        }
        else
        {
            SpawnBullet(FireDir(), SingleSpeed, DmgSingle, AmmoSingle);
        }
    }

    /// <summary>扇形散射：以开火朝向为中心 ±(n-1)/2 步展开。</summary>
    private void FireFan(int count)
    {
        var center = FireDir();
        var half = (count - 1) / 2.0f;
        for (var i = 0; i < count; i++)
        {
            SpawnBullet(center.Rotated(SpreadFanStep * (i - half)), SpreadSpeed, DmgSpread, AmmoSpread);
        }
    }

    private void SpawnBullet(Vector2 dir, float bulletSpeed, int dmg, StringName pType)
    {
        var pool = GameState.Instance.BulletPool;
        if (pool == null)
        {
            return;
        }

        var b = pool!.Fire(dir, bulletSpeed, dmg, false);
        if (b == null)
        {
            return; // 同屏敌弹硬上限，本次开火放弃
        }

        b.Position = GlobalPosition + dir * _muzzleOffset;
        if (pType == AmmoLaser)
        {
            // 细长高亮快速弹（与敌机 laser 弹同表现，polygon 尖端朝 +x 即飞行方向）
            var poly = b.SpriteNode();
            if (poly != null)
            {
                poly.Scale = new Vector2(2.2f, 0.55f);
                poly.SelfModulate = new Color(1.0f, 0.85f, 0.35f); // Sprite2D 无 color，用 self_modulate
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
        _hpBar.Value = Mathf.Clamp(Hp / (float)MaxHp, 0.0f, 1.0f) * 100.0f;
        FlashFx.Hit(_sprite, ref _flashTimer, FlashTime, ref _flashBaseScale); // 受击闪白 + 缩放回弹
        if (Hp <= 0)
        {
            Die();
        }
    }

    /// <summary>受击闪白手动衰减（替代 Tween；FlashFx 共享实现，零分配）。</summary>
    private void UpdateFlash(float delta)
    {
        if (_flashTimer <= 0.0f)
        {
            return;
        }

        FlashFx.Update(_sprite, ref _flashTimer, delta, FlashTime, Colors.White, ref _flashBaseScale);
    }

    public void Die()
    {
        // 遭遇单位与普通敌机同口结算：击杀数（「击杀 N 架」任务与敌机解锁门）+
        // 击杀分（吃连击/score_amp，随后经 AddScore 乘难度档倍率）。
        // 原实现两者都不给——屏上唯一可打目标被击毁后，进度门不推进、连分数都没有。
        SetAimMarked(false);
        GameState.Instance.AddKillScore(ScoreValue);
        GameState.Instance.AddKill();
        GameState.Instance.PlaySfx(SfxId.Explosion);
        GameState.Instance.Shake(_shakeDie);
        Explosion.SpawnAt(GetParent(), GlobalPosition, 1.0f);
        EmitSignal(SignalName.Died, this);
        QueueFree();
    }
}
