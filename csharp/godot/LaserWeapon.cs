using Godot;

namespace InfiAir;

/// <summary>
/// 激光束武器（M3b 全量迁移，2026-08-08 自 scripts/laser_weapon.gd 迁移，对齐原作
/// LaserBuff + LASER_DURATION=180 帧）：挂载于 Player 节点下，GameState.buff_count(&amp;"laser_beam")
/// &gt; 0 时启用。就绪即自动触发：3s 持续光束替换普通子弹（禁用玩家自动开火），光束为
/// 穿透性直线，线上敌人每 0.1s 结算 16 伤害；结束后进入 8s 冷却再次触发。
/// 语义保持：buff 层数经 buffs_changed 信号缓存（Enemy.cs 同款，避免每物理帧跨语言
/// buff_count）；C23 预分配 points 数组帧内 set_point_position 原地写；E08 buff 归零时
/// 收束激活态光束。原 const SFX_BEAM = preload 移入 _Ready 惰性加载（GD.Load 同资源缓存）。
/// 迁移期动态访问：GameState 经 GameStateBridge；Player 为 C# 类（InfiAir.Player，M3c 并行迁移）。
/// </summary>
public partial class LaserWeapon : Node2D
{
    /// <summary>光束持续时长（s），_ready 从 balance.json 覆盖。</summary>
    public float BeamDuration { get; private set; } = 3.0f;
    /// <summary>冷却时长（s），_ready 从 balance.json 覆盖（原 var COOLDOWN）。</summary>
    public float CooldownDuration { get; private set; } = 8.0f;
    /// <summary>穿透结算间隔（s）。</summary>
    public float TickInterval { get; private set; } = 0.1f;
    /// <summary>每 tick 对线上敌人结算的伤害。</summary>
    public int TickDamage { get; private set; } = 16;
    /// <summary>光束长度（世界单位）。</summary>
    public float BeamLength { get; private set; } = 2400.0f;
    /// <summary>光束判定半宽（世界单位）。</summary>
    public float BeamHalfWidth { get; private set; } = 26.0f;
    /// <summary>敌机碰撞半径设计值（spawner 机型配置基准 30，× world_scale 生效）。</summary>
    public float EnemyHitRadius { get; private set; } = 30.0f;

    private static AudioStream? _sfxBeam;  // 原 const SFX_BEAM = preload(...)，_ready 惰性加载

    private bool _active;
    private float _activeTime;
    private float _cooldown;
    private float _tickTimer;
    private bool _savedAutofire = true;
    /// <summary>laser_beam 层数缓存（buffs_changed 信号驱动；热路径禁跨语言调用）。</summary>
    private bool _laserBeamOn;

    /// <summary>父节点（player.tscn 中 LaserWeapon 挂 Player 下；场景结构保证非空）。</summary>
    private Player _player = null!;
    private Line2D _beam = null!;
    private GpuParticles2D _glow = null!;

    private readonly Callable _onBuffsChanged = Callable.From(OnBuffsChanged);

    // ---------------- A7：测试/诊断白盒断言经公开接口 ----------------

    public bool Active() => _active;

    public float ActiveTime() => _activeTime;

    public float Cooldown() => _cooldown;

    public void SetCooldown(float seconds) => _cooldown = seconds;

    public void SetActiveTime(float seconds) => _activeTime = seconds;

    public Line2D Beam() => _beam;

    // ---------------- GDScript 鸭子调用兼容桥（M3b 过渡，M7 删除） ----------------
    // buff33_test 等 GDScript 调用方经动态派发以 snake_case 访问 C# 类方法
    // （C# 内部调用一律 PascalCase）。M7 全量迁移后删除本段。

    public bool active() => Active();

    public float active_time() => ActiveTime();

    public float cooldown() => Cooldown();

    public void set_cooldown(float seconds) => SetCooldown(seconds);

    public void set_active_time(float seconds) => SetActiveTime(seconds);

    public Line2D beam() => Beam();

    public override void _Ready()
    {
        _player = GetParent() as Player;
        BeamDuration = (float)GameStateBridge.Call("cfg", "buffs.laser_beam.duration", BeamDuration).AsDouble();
        CooldownDuration = (float)GameStateBridge.Call("cfg", "buffs.laser_beam.cooldown", CooldownDuration).AsDouble();
        TickInterval = (float)GameStateBridge.Call("cfg", "buffs.laser_beam.tick_interval", TickInterval).AsDouble();
        TickDamage = (int)GameStateBridge.Call("cfg", "buffs.laser_beam.tick_damage", TickDamage).AsInt64();
        BeamLength = (float)GameStateBridge.Call("cfg", "buffs.laser_beam.length", BeamLength).AsDouble();
        BeamHalfWidth = (float)GameStateBridge.Call("cfg", "buffs.laser_beam.half_width", BeamHalfWidth).AsDouble();
        EnemyHitRadius = (float)GameStateBridge.Call("cfg", "buffs.laser_beam.hit_radius", EnemyHitRadius).AsDouble()
            * (float)GameStateBridge.Get("world_scale").AsDouble();
        _sfxBeam = GD.Load<AudioStream>("res://assets/audio/bullet_fire_c.wav");
        // 光束与末端光晕用 top_level 全局坐标，避免随机身旋转
        _beam = new Line2D
        {
            TopLevel = true,
            Width = 14.0f,
            DefaultColor = new Color(0.55f, 0.9f, 1.0f, 0.9f),
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
            Visible = false,
        };
        _beam.Points = new Vector2[] { Vector2.Zero, Vector2.Zero };  // C23：预分配，帧内只写元素
        AddChild(_beam);
        _glow = new GpuParticles2D
        {
            TopLevel = true,
            Amount = 24,
            Lifetime = 0.3f,
            Emitting = false,
        };
        var mat = new ParticleProcessMaterial
        {
            Direction = Vector3.Zero,
            Spread = 180.0f,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 40.0f,
            InitialVelocityMax = 120.0f,
            ScaleMin = 1.5f,
            ScaleMax = 3.0f,
            Color = new Color(0.6f, 0.95f, 1.0f, 0.9f),
        };
        _glow.ProcessMaterial = mat;
        AddChild(_glow);
        // buffs_changed 缓存：laser_beam 层数（Enemy.cs 同款；避免每物理帧跨语言 buff_count）
        var gs = GameStateBridge.Instance;
        if (gs != null)
        {
            gs.Connect("buffs_changed", _onBuffsChanged);
        }

        OnBuffsChanged();
    }

    public override void _ExitTree()
    {
        // buffs_changed 信号断开（Enemy.cs C22 模式；节点随 Player 一起释放）
        var gs = GameStateBridge.Instance;
        if (gs != null && gs.IsConnected("buffs_changed", _onBuffsChanged))
        {
            gs.Disconnect("buffs_changed", _onBuffsChanged);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        if (!_laserBeamOn)
        {
            // E08：buff 归零时收束激活态光束——防未来 buff 移除机制引入后 _end_beam 不执行、
            // autofire 卡禁（当前无 buff 移除机制不可达，兜底成本一行）
            if (_active)
            {
                EndBeam();
            }

            return;
        }

        _cooldown = Mathf.Max(_cooldown - d, 0.0f);
        if (_active)
        {
            _activeTime -= d;
            _tickTimer -= d;
            if (_activeTime <= 0.0f || _player.IsDead() || _player.IsInputLocked())
            {
                EndBeam();
                return;
            }

            var start = _player.GlobalPosition;
            var end = start + AimDir() * BeamLength;  // G021：参数已删
            // C23：预分配数组经 set_point_position 原地写（points[i]= 值语义副本不生效）
            _beam.SetPointPosition(0, start);
            _beam.SetPointPosition(1, end);
            _glow.Position = end;
            if (_tickTimer <= 0.0f)
            {
                _tickTimer += TickInterval;
                DamageTick(start, end);
            }
        }
        else if (_cooldown <= 0.0f && !_player.IsDead() && !_player.IsInputLocked() && !_player.IsEntryPlaying())
        {
            // K02：入场动画期不触发激光——入场期 autofire 被入场序列置 false，_start_beam 捕获后
            // _end_beam 恢复会把入场结束恢复的 true 覆盖成 false，自动开火永久关闭
            // B7 修复：触发判定随瞄准点（含磁吸/粘性）而非原始鼠标，与准星一致
            if ((_player.AimPoint() - _player.GlobalPosition).Length() > 1.0f)
            {
                StartBeam();
            }
        }
    }

    /// <summary>光束方向：经 _player.aim_point()（磁吸/粘性后的平滑瞄准点），与准星/开火共用同一点
    /// （B7 修复：P1-3 磁吸会让准星偏离原始鼠标，仍走 get_global_mouse_position 则光束与可见准星指向不同目标）。</summary>
    private Vector2 AimDir()  // G021：_start 参数从未使用，删除
    {
        var aim = _player.AimPoint() - _player.GlobalPosition;
        if (aim.Length() <= 1.0f)
        {
            // 贴图机头朝上，机身 rotation 对应 Vector2.UP 方向
            return Vector2.Up.Rotated(_player.Rotation);
        }

        return aim.Normalized();
    }

    private void StartBeam()
    {
        // K02 双保险：入场期直接调用（测试/其他路径）也不进入光束
        if (_player.IsEntryPlaying())
        {
            return;
        }

        // H06（健壮性审核）：autofire 捕获必须在 _active=true 之前——旧代码在门闩之后
        // 为不可达死代码，_end_beam 无条件恢复 true 会破坏入场期/测试关闭的 autofire 状态
        _savedAutofire = _player.AutoFireEnabled();
        _active = true;
        _activeTime = BeamDuration;
        _tickTimer = 0.0f;
        _beam.Visible = true;
        _glow.Emitting = true;
        _player.SetAutoFire(false);
        GameStateBridge.Call("play_sfx", _sfxBeam, -6.0);
    }

    private void EndBeam()
    {
        _active = false;
        _beam.Visible = false;
        _glow.Emitting = false;
        _cooldown = CooldownDuration;
        if (GodotObject.IsInstanceValid(_player))
        {
            _player.SetAutoFire(_savedAutofire);
        }
    }

    /// <summary>穿透结算：光束线段两侧的敌人（含 Boss）都吃伤害，不打断。
    /// P2：从尾向前索引遍历（take_damage→die→注销注册表 erase 只影响已处理的高索引区，
    /// 倒序不受突变破坏），免 10 次/秒的整表 duplicate 拷贝。</summary>
    private void DamageTick(Vector2 start, Vector2 end)
    {
        var arr = GameStateBridge.Get("enemies").AsGodotArray();
        for (var i = arr.Count - 1; i >= 0; i--)
        {
            var node = arr[i].AsGodotObject();
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            var pos = ((Node2D)node).GlobalPosition;  // 注册表元素均为 Node2D（Enemy/Boss/炮塔/编队机）
            if (DistToSegment(pos, start, end) <= BeamHalfWidth + EnemyHitRadius)
            {
                // Enemy（C# take_damage 别名）/Boss（GDScript）鸭子调用，语义与原 node.take_damage 一致
                node.Call("take_damage", TickDamage);
            }
        }
    }

    /// <summary>点到线段距离（静态纯函数）。</summary>
    private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0.0f, 1.0f);
        return (p - (a + ab * t)).Length();
    }

    /// <summary>laser_beam 层数缓存刷新（热路径禁字典约定；buffs_changed 信号驱动，_ready 初始一次）。</summary>
    private void OnBuffsChanged()
    {
        _laserBeamOn = (int)GameStateBridge.Call("buff_count", new StringName("laser_beam")).AsInt64() > 0;
    }
}
