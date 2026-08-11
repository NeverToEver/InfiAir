using Godot;

namespace InfiAir;

/// <summary>
/// 直线子弹（M3a 全量迁移，2026-08-08 自 scripts/bullet.gd 迁移）：玩家弹与敌弹共用
/// bullet.tscn，经 setup/activate 区分阵营。正常产弹走 GameState.bullet_pool（对象池复用）；
/// 直接实例化（测试）走兼容路径。
/// 保持语义：R07 碰撞半径唯一事实源；P2-1 活跃计数；P0-3 共享图集单 Sprite2D；
/// 公平机制一（受击宽限）/二（擦弹单次）/四（弹反）；P0-1 敌弹注册表；P1-1 辅助瞄准追踪；
/// P2-10 致死高亮；宽限/擦弹/反射的池化复位。
/// Player/Enemy 为 GDScript 类（M3b/M3c 迁移前）经鸭子调用（HasMethod/Get/Call）。
/// </summary>
public partial class Bullet : Area2D
{
    /// <summary>R07：碰撞半径唯一事实源（player.gd 擦弹环形带判定引用此常量）。</summary>
    public const float CollisionRadius = 6.0f;

    /// <summary>U14 同款：bullet_type meta 键静态缓存（Enemy/TurretBattery/BossFire 写入，
    /// 本类 _applyFaction 复位消费；2026-08-10 审计 H1——原每发 SetMeta/HasMeta 字符串字面量转换）。</summary>
    internal static readonly StringName MetaBulletType = new("bullet_type");

    /// <summary>R07 跨语言访问器（GDScript 不能经脚本资源读 C# 常量/静态属性——实测，
    /// 静态方法可调；M3c player 迁移后改直接引用常量）。</summary>
    public static float GetCollisionRadius() => CollisionRadius;

    public Vector2 Direction { get; set; } = Vector2.Down;
    public float Speed { get; set; } = 900.0f;
    public int Damage { get; set; } = 1;
    public bool IsPlayerBullet { get; set; } = true;
    public bool Homing { get; set; }
    public float HomingTime { get; set; }
    /// <summary>追踪转向速率（rad 级插值系数；精英炮台弱锁定追踪弹降为 1.5）。</summary>
    public float HomingTurnRate { get; set; } = 4.0f;
    /// <summary>辅助瞄准追踪目标（P1-1，玩家弹专用）；池化 activate 复位为 null。</summary>
    public Node2D? HomingTarget { get; set; }
    /// <summary>穿透剩余次数（玩家弹，穿透弹 buff）。</summary>
    public int Pierce { get; set; }
    /// <summary>命中产生 AoE 爆炸（玩家弹，爆炸弹 buff）。</summary>
    public bool Explosive { get; set; }
    /// <summary>导弹溅射（母舰导弹）。</summary>
    public int SplashDamage { get; set; }
    public float SplashRadius { get; set; }
    /// <summary>击毁得分系数（母舰弹丸为 1/3）。</summary>
    public float ScoreScale { get; set; } = 1.0f;

    /// <summary>爆炸弹 buff 固定值（对齐原作单层取值：半径 50、伤害 30）。</summary>
    public float ExplosiveRadius { get; private set; } = 50.0f;
    public int ExplosiveDamage { get; private set; } = 30;
    /// <summary>弹丸视觉缩放（设计值 × world_scale，碰撞半径不变）。</summary>
    public float VisualScale { get; private set; } = 1.3f;
    /// <summary>敌弹视觉缩放（设计值 × world_scale）。</summary>
    public float EnemyVisualScale { get; private set; } = 2.4f;
    /// <summary>辅助瞄准追踪近距收敛半径。</summary>
    public float HomingSnapRadius { get; private set; } = 36.0f;
    /// <summary>受击宽限帧窗口（秒），balance.json player.grace_period 钳制 (0, 0.15]。</summary>
    public float GracePeriod { get; private set; } = 0.05f;
    /// <summary>弹反倍率（player.parry.*）。</summary>
    public float ReflectSpeedMult { get; private set; } = 2.0f;
    public float ReflectDamageMult { get; private set; } = 1.5f;

    /// <summary>P2-1：场上活跃子弹总数（activate/deactivate 成对维护；直实例化测试弹不计）。</summary>
    public static int ActiveCount { get; private set; }

    private float _homingElapsed;
    private BulletPool? _pool; // U13：typed（原 GodotObject? 动态派发）
    private bool _active;
    private bool _repooling;
    private Godot.Timer? _graceTimer;
    private Area2D? _graceHitbox;
    private bool _grazeDone;
    private Sprite2D? _sprite;

    /// <summary>P0-3：共享图集 Sprite2D（弹体+白芯光栅化进单张共享纹理）。</summary>
    private static readonly Vector2I TexSize = new(24, 8);
    private static readonly Vector2 TexOffset = new(11.0f, 4.0f);
    private static readonly Vector2[] ArrowBody =
    {
        new(-10, -3), new(4, -3), new(12, 0), new(4, 3), new(-10, 3),
    };
    private static readonly Vector2[] ArrowCore =
    {
        new(-4.5f, -1.5f), new(2, -1.5f), new(5.5f, 0), new(2, 1.5f), new(-4.5f, 1.5f),
    };

    private static Texture2D? _playerTex;
    private static Texture2D? _enemyTex;

    /// <summary>热路径缓存：view_world_rect 每物理帧一次动态调用（全弹共享），帧内复用。</summary>
    private static ulong _viewFrame;
    private static Rect2 _viewRect;

    /// <summary>A7：测试/诊断白盒断言经公开接口。</summary>
    public void Despawn() => _despawn();

    public void Explode() => _explode();

    public void Splash() => _splash();

    /// <summary>兼容路径：直接实例化时 setup() 后由 _ready 应用阵营外观（4 参便捷重载）。</summary>
    public void Setup(Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer)
    {
        Setup(pDirection, pSpeed, pDamage, pIsPlayer, false, 0.0f);
    }

    public void Setup(Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer, bool pHoming, float pHomingTime)
    {
        Direction = pDirection.Normalized();
        // H10：零方向弹回退 DOWN（防静止弹永驻场景）
        if (Direction == Vector2.Zero)
        {
            Direction = Vector2.Down;
        }

        // R07：零速钳制（0 速弹不位移不脱界，永驻场景；测试直写字段绕过 setup）
        Speed = Mathf.Max(pSpeed, 1.0f);
        // 敌方子弹伤害随对局进程 ramp
        Damage = pIsPlayer ? pDamage : Mathf.Max(1, (int)Mathf.Round(pDamage * GameState.Instance.EnemyDamageRamp()));
        IsPlayerBullet = pIsPlayer;
        Homing = pHoming;
        HomingTime = pHomingTime;
    }

    /// <summary>池化路径：激活并重置全部状态（4 参便捷重载）。</summary>
    public void Activate(Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer)
    {
        Activate(pDirection, pSpeed, pDamage, pIsPlayer, false, 0.0f);
    }

    public void Activate(Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer, bool pHoming, float pHomingTime)
    {
        Setup(pDirection, pSpeed, pDamage, pIsPlayer, pHoming, pHomingTime);
        _active = true;
        ActiveCount++;
        _homingElapsed = 0.0f;
        HomingTarget = null;
        _grazeDone = false;
        Pierce = 0;
        Explosive = false;
        SplashDamage = 0;
        SplashRadius = 0.0f;
        ScoreScale = 1.0f;
        HomingTurnRate = 4.0f;
        Visible = true;
        Monitoring = true;
        SetPhysicsProcess(true); // C04：位移走物理帧，与 Area2D overlap 检测同步
        _applyFaction();
    }

    /// <summary>池化回收：停用但保留实例。</summary>
    public void Deactivate()
    {
        _active = false;
        ActiveCount--;
        Visible = false;
        SetPhysicsProcess(false);
        Position = new Vector2(-500.0f, -500.0f);
        // P0-1：回收弹移出敌弹注册表（death_replay 录制数据源）
        if (!IsPlayerBullet)
        {
            GameState.Instance.UnregisterEnemyBullet(this);
        }

        _cancelGrace();
        CallDeferred(MethodName.DeferredDisableMonitoring);
    }

    /// <summary>物理回调内不能直改 monitoring，延迟到帧末；若已被重激活（同帧复用）则跳过。
    /// public：CallDeferred 需引擎注册。</summary>
    public void DeferredDisableMonitoring()
    {
        if (!_active)
        {
            Monitoring = false;
        }
    }

    /// <summary>对象池协调的内部状态封装（A1 修复，禁止跨类直写 _ 私有字段）。</summary>
    public void SetPool(BulletPool pool) => _pool = pool;

    public bool IsActive() => _active;

    public void SetRepooling(bool value) => _repooling = value;

    /// <summary>P0-3：视觉节点公开接口（替代原 polygon_node/core_node 双节点——已合并单 Sprite2D）。</summary>
    public Sprite2D? SpriteNode()
    {
        if (_sprite == null)
        {
            _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        }

        return _sprite;
    }

    /// <summary>机制二：擦弹单次计数——同一敌弹至多计 1 次（池化 activate 复位）。</summary>
    public bool TryGraze()
    {
        if (_grazeDone)
        {
            return false;
        }

        _grazeDone = true;
        return true;
    }

    /// <summary>机制四：弧光弹反——转玩家弹、镜面反射（盾法线=机头前方，direction.y 取反）、
    /// ×REFLECT_SPEED_MULT 返回、伤害 ×REFLECT_DAMAGE_MULT；追踪终止；取消受击宽限。</summary>
    public void Reflect()
    {
        IsPlayerBullet = true;
        Direction = new Vector2(Direction.X, -Direction.Y);
        Speed *= ReflectSpeedMult;
        Damage = Mathf.Max(1, (int)Mathf.Round(Damage * ReflectDamageMult));
        Homing = false;
        HomingTarget = null;
        _cancelGrace();
        _applyFaction();
    }

    public override void _Ready()
    {
        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
        ExplosiveRadius = (float)GameState.Instance.Cfg("buffs.explosive.radius_per_level", ExplosiveRadius).AsDouble();
        ExplosiveDamage = (int)GameState.Instance.Cfg("buffs.explosive.damage_per_level", ExplosiveDamage).AsInt64();
        VisualScale = (float)GameState.Instance.Cfg("effects.bullet_visual_scale", VisualScale).AsDouble()
            * (float)GameState.Instance.WorldScale;
        EnemyVisualScale = (float)GameState.Instance.Cfg("effects.enemy_bullet_visual_scale", EnemyVisualScale).AsDouble()
            * (float)GameState.Instance.WorldScale;
        // 机制一：宽限窗口钳制 (0, 0.15]
        GracePeriod = Mathf.Clamp((float)GameState.Instance.Cfg("player.grace_period", GracePeriod).AsDouble(), 0.001f, 0.15f);
        // 机制四：弹反倍率
        ReflectSpeedMult = (float)GameState.Instance.Cfg("player.parry.reflect_speed_mult", ReflectSpeedMult).AsDouble();
        ReflectDamageMult = (float)GameState.Instance.Cfg("player.parry.reflect_damage_mult", ReflectDamageMult).AsDouble();
        // 碰撞半径：设计值 × 全局缩放（幂等赋值）
        var shape = GetNode<CollisionShape2D>("CollisionShape2D");
        if (shape.Shape is CircleShape2D circle)
        {
            circle.Radius = CollisionRadius * (float)GameState.Instance.WorldScale;
        }

        _applyFaction();
    }

    public override void _ExitTree()
    {
        // 被外部 queue_free 时通知池移除引用；池内 reparent 也经此回调（_repooling 置位不算离开池）
        if (_pool != null && GodotObject.IsInstanceValid(_pool) && !_repooling)
        {
            _pool!.Forget(this);
        }

        // 池化弹被外部销毁（未走 deactivate）时补减活跃计数
        if (_active)
        {
            _active = false;
            ActiveCount--;
        }

        // P0-1：外部销毁同步移出敌弹注册表（幂等）
        if (!IsPlayerBullet)
        {
            GameState.Instance.UnregisterEnemyBullet(this);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        if (HomingTarget != null)
        {
            // 辅助瞄准追踪（P1-1）：优先于 homing 玩家追踪分支；目标失效/超时限即直行
            if (!GodotObject.IsInstanceValid(HomingTarget))
            {
                HomingTarget = null;
            }
            else if (!(bool)GameState.Instance.EnemiesHas(HomingTarget)) // G010：注册表 O(1) 判定
            {
                HomingTarget = null;
            }
            else if (_homingElapsed < HomingTime)
            {
                _homingElapsed += d;
                var toTarget = HomingTarget.GlobalPosition - GlobalPosition;
                var dist = toTarget.Length();
                if (dist > 0.0f && dist <= HomingSnapRadius + Speed * d)
                {
                    // 近距直取：进入收敛半径后直接对准目标
                    Direction = toTarget / dist;
                    Rotation = Direction.Angle();
                }
                else
                {
                    // H05：dist==0 时保持原向（除零产生 inf/NaN 污染）。
                    // V 系列：原实现置 Vector2.Right 为 90° 突变，与注释「保持原向」矛盾——改为不动 Direction
                    if (dist > 0.0f)
                    {
                        // 距离越近转向越急：螺旋收敛
                        var rate = HomingTurnRate * (1.0f + HomingSnapRadius * 2.0f / dist);
                        var targetAngle = Mathf.LerpAngle(Direction.Angle(), toTarget.Angle(), rate * d);
                        Direction = Vector2.Right.Rotated(targetAngle);
                        Rotation = targetAngle;
                    }
                }
            }
        }
        else if (Homing && _homingElapsed < HomingTime)
        {
            _homingElapsed += d;
            var playerRef = GameState.Instance.PlayerRef;
            if (playerRef != null)
            {
                var playerNode = (Node2D)playerRef;
                var newAngle = Mathf.LerpAngle(
                    Direction.Angle(), (playerNode.GlobalPosition - GlobalPosition).Angle(), HomingTurnRate * d);
                Direction = Vector2.Right.Rotated(newAngle);
                Rotation = newAngle;
            }
        }

        Position += Direction * Speed * d;
        if (!CachedViewRect(80.0f).HasPoint(Position))
        {
            _despawn();
        }
    }

    /// <summary>view_world_rect 每物理帧一次动态调用缓存（全弹共享；M7 后改 typed 直调）。</summary>
    private static Rect2 CachedViewRect(float margin)
    {
        var frame = Engine.GetPhysicsFrames();
        if (frame != _viewFrame)
        {
            _viewFrame = frame;
            _viewRect = GameState.Instance.ViewWorldRect();
        }

        return margin == 0.0f ? _viewRect : _viewRect.Grow(margin);
    }

    /// <summary>爆炸弹 buff：命中时对周围敌人造成固定 AoE 伤害（主目标同吃，Boss 除外）。
    /// AC16（2026-08-11 审计）：删除其上孤儿「monitoring 延迟」summary（与 _explode 无关，AB21 同族）。</summary>
    private void _explode()
    {
        var arr = (Godot.Collections.Array)GameState.Instance.Enemies;
        var radiusSq = ExplosiveRadius * ExplosiveRadius; // 2026-08-10 审计 H5：平方距离比较免每敌 sqrt
        for (var i = arr.Count - 1; i >= 0; i--)
        {
            // U13：typed——原 is_boss 判定排除 Boss（恒 true 跳过）与无 take_damage 类，
            // `is Enemy` 直接等价（注册表含 Enemy 与 Boss，Boss 非 Enemy 子类）
            if (arr[i].AsGodotObject() is not Enemy enemy)
            {
                continue;
            }

            if (enemy.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= radiusSq)
            {
                // 2026-08-09 Y 系列：统一分派（enemy 已判型，单参 = scoreScale 1.0 既有语义）
                EntityDamage.Dispatch(enemy, ExplosiveDamage);
            }
        }

        Explosion.SpawnAt(GetParent(), GlobalPosition, 0.6f);
        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -6.0);
    }

    /// <summary>导弹溅射（母舰导弹）：半径内全部敌人（含主目标与 Boss）追加固定伤害。</summary>
    private void _splash()
    {
        var arr = (Godot.Collections.Array)GameState.Instance.Enemies;
        var radiusSq = SplashRadius * SplashRadius; // 2026-08-10 审计 H5：平方距离比较免每敌 sqrt
        for (var i = arr.Count - 1; i >= 0; i--)
        {
            var node = (Node2D?)arr[i];
            if (node == null)
            {
                continue;
            }

            if (node is Area2D && node.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= radiusSq)
            {
                // 2026-08-09 Y 系列：统一分派（原三处 switch 收敛）；溅射路径带 ScoreScale
                EntityDamage.Dispatch(node, SplashDamage, ScoreScale);
            }
        }

        Explosion.SpawnAt(GetParent(), GlobalPosition, 0.8f);
        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -6.0);
    }

    /// <summary>注册表实例是否为 Enemy 语义（M3b：C# 脚本无 GetGlobalName——改鸭子判定：
    /// 仅 Enemy/Boss 有 is_boss()，TurretBattery/FormationCraft 无；Boss 由调用方 is_boss 检查排除，
    /// 净效果与原 GDScript `as Enemy` 等价）。</summary>
    private static bool IsEnemyInstance(GodotObject o)
    {
        // U13：typed 判型（原鸭子 HasMethod("is_boss")——仅 Enemy/Boss 有该方法）
        return o is Enemy or Boss;
    }

    private void OnAreaEntered(Area2D area)
    {
        // 同物理帧重复命中守卫：monitoring 关闭延迟到帧末，已回收弹不再结算
        if (!_active && (_pool != null || IsQueuedForDeletion()))
        {
            return;
        }

        if (IsPlayerBullet)
        {
            if (area.IsInGroup("enemy"))
            {
                // crit_shot 暴击：层数 × 基础概率判定，命中 ×倍率伤害（玩家侧缓存经 player_ref）
                var hitDamage = Damage;
                var pRef = GameState.Instance.PlayerRef;
                if (pRef is Player p) // U13：typed（Player.CritChance/CritMultiplierValue 为 buff 缓存属性）
                {
                    var critChance = p.CritChance;
                    if (critChance > 0.0f && GD.Randf() < critChance)
                    {
                        hitDamage = (int)(Damage * p.CritMultiplierValue);
                    }
                }

                // 2026-08-09 Y 系列：统一分派（原三处 switch 收敛）；直击路径带 ScoreScale
                EntityDamage.Dispatch(area, hitDamage, ScoreScale);

                // 原作爆炸弹对 Boss 路径完全不触发（无爆炸视觉/溅射），仅直击；
                // U13：is_boss 语义 = Boss 恒 true（Enemy/Turret/Formation 爆炸条件原为 !is_boss || 无方法 = true）
                if (Explosive && area is not Boss)
                {
                    _explode();
                }

                if (SplashDamage > 0)
                {
                    _splash();
                }

                if (Pierce > 0)
                {
                    Pierce--;
                }
                else
                {
                    _despawn();
                }
            }
        }
        else if (area.IsInGroup("player_hitbox"))
        {
            // 机制一：受击宽限帧——进入 Hitbox 不立即结算，窗口内离开视为擦过不计伤
            _startGraceCheck(area);
        }
    }

    /// <summary>机制一：弹离开玩家 Hitbox（窗口内擦过）→ 取消宽限 Timer，不计伤。</summary>
    private void OnAreaExited(Area2D area)
    {
        if (area.IsInGroup("player_hitbox"))
        {
            _cancelGrace();
        }
    }

    /// <summary>机制一：启动宽限窗口（事件驱动；一次性 Timer 挂子弹下随场景释放）。</summary>
    private void _startGraceCheck(Area2D hitbox)
    {
        if (_graceTimer != null && !_graceTimer.IsStopped())
        {
            return;
        }

        _graceHitbox = hitbox;
        if (_graceTimer == null)
        {
            _graceTimer = new Godot.Timer { OneShot = true };
            _graceTimer.Timeout += OnGraceTimeout;
            AddChild(_graceTimer);
        }

        _graceTimer.WaitTime = GracePeriod;
        _graceTimer.Start();
    }

    private void _cancelGrace()
    {
        _graceTimer?.Stop();
        _graceHitbox = null; // 回收弹不携带旧 Hitbox 悬空引用
    }

    /// <summary>宽限到期：单次 overlaps 复核——仍与 Hitbox 重叠才结算。</summary>
    private void OnGraceTimeout()
    {
        if (!_active || _graceHitbox == null || !GodotObject.IsInstanceValid(_graceHitbox))
        {
            return;
        }

        var hitbox = _graceHitbox;
        _graceHitbox = null;
        if (!OverlapsArea(hitbox))
        {
            return;
        }

        // 既有受击结算链路（含无敌/闪避/单帧守卫、受击清弹、致死高亮）
        var pRef = GameState.Instance.PlayerRef;
        if (pRef == null)
        {
            return;
        }

        var player = (Player)pRef; // U13：typed
        if (player.TakeDamage((float)Damage, GlobalPosition))
        {
            // P2-10：致死一击弹丸高亮残留
            if (player.IsDead())
            {
                _lingerFatal();
            }
            else
            {
                _despawn();
            }
        }
    }

    /// <summary>P2-10：致死弹 0.5s 高亮残留（停位移/关碰撞/红闪高亮，一次性 Timer 到期回收）。</summary>
    private void _lingerFatal(float duration = 0.5f)
    {
        SetPhysicsProcess(false);
        Monitoring = false;
        Modulate = new Color(2.0f, 0.7f, 0.7f);
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite != null)
        {
            _sprite.SelfModulate = new Color(1.2f, 0.35f, 0.35f);
        }

        var t = new Godot.Timer { OneShot = true, WaitTime = duration };
        t.Timeout += () =>
        {
            _despawn();
            t.QueueFree();
        };
        AddChild(t);
        t.Start();
    }

    private void _despawn()
    {
        if (_pool != null && GodotObject.IsInstanceValid(_pool))
        {
            _pool!.Release(this);
        }
        else
        {
            QueueFree();
        }
    }

    private void _applyFaction()
    {
        Rotation = Direction.Angle();
        // 重置外观（敌机/Boss 激光长弹、母舰弹的自定义外观）
        Scale = Vector2.One;
        Modulate = Colors.White;
        _ensureTextures(); // P0-3：共享图集惰性生成（静态，首次调用）
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite == null)
        {
            return;
        }

        _sprite.Texture = IsPlayerBullet ? _playerTex : _enemyTex;
        _sprite.Scale = Vector2.One * (IsPlayerBullet ? VisualScale : EnemyVisualScale);
        // M1 审计：self_modulate 染色残留复位为白（laser 黄/Boss 重弹橙/致死高亮红）
        _sprite.SelfModulate = Colors.White;
        if (HasMeta(MetaBulletType))
        {
            RemoveMeta(MetaBulletType);
        }

        if (IsPlayerBullet)
        {
            CollisionLayer = 2; // 第 2 层：player_bullet
            CollisionMask = 4; // 命中第 3 层：enemy
        }
        else
        {
            CollisionLayer = 8; // 第 4 层：enemy_bullet
            CollisionMask = 1; // 命中第 1 层：player
        }

        // P0-1：敌弹注册表维护（幂等）——activate/_ready/reflect 均经此路径
        if (IsPlayerBullet)
        {
            GameState.Instance.UnregisterEnemyBullet(this);
        }
        else
        {
            GameState.Instance.RegisterEnemyBullet(this);
        }
    }

    /// <summary>P0-3：共享纹理惰性生成（静态，全实例共用；首次调用光栅化一次）。</summary>
    private static void _ensureTextures()
    {
        if (_playerTex != null)
        {
            return;
        }

        _playerTex = _stampTexture(ArrowBody, new Color(1.0f, 0.9f, 0.25f), ArrowCore, Colors.White);
        _enemyTex = _stampTexture(ArrowBody, new Color(1.0f, 0.38f, 0.3f), System.Array.Empty<Vector2>(), Colors.Transparent);
    }

    /// <summary>P0-3：把多边形（弹体 + 可选白芯）光栅化进共享纹理（像素级平移对齐，无缩放损失）。</summary>
    private static ImageTexture _stampTexture(Vector2[] body, Color bodyColor, Vector2[] core, Color coreColor)
    {
        var img = Image.CreateEmpty(TexSize.X, TexSize.Y, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        _fillPolygon(img, body, bodyColor);
        if (core.Length > 0)
        {
            _fillPolygon(img, core, coreColor);
        }

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>凸多边形扫描线填充（三角扇分解：以第一个点为公共顶点，全部顶点平移纹理偏移）。</summary>
    private static void _fillPolygon(Image img, Vector2[] pts, Color color)
    {
        for (var i = 1; i < pts.Length - 1; i++)
        {
            _fillTriangle(img, pts[0] + TexOffset, pts[i] + TexOffset, pts[i + 1] + TexOffset, color);
        }
    }

    /// <summary>三角形扫描线填充（按 y 排序，逐行求两条边交点的 x 区间）。</summary>
    private static void _fillTriangle(Image img, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        var pts = new[] { a, b, c };
        System.Array.Sort(pts, (p, q) => p.Y.CompareTo(q.Y));
        var y0 = Mathf.Max(0, (int)Mathf.Ceil(pts[0].Y));
        var y1 = Mathf.Min(img.GetHeight() - 1, (int)Mathf.Floor(pts[2].Y));
        for (var y = y0; y <= y1; y++)
        {
            var fy = (float)y + 0.5f;
            var xA = _edgeIntersect(pts[0], pts[2], fy);
            var xB = fy < pts[1].Y ? _edgeIntersect(pts[0], pts[1], fy) : _edgeIntersect(pts[1], pts[2], fy);
            var xa = Mathf.Max(0, (int)Mathf.Ceil(Mathf.Min(xA, xB)));
            var xb = Mathf.Min(img.GetWidth() - 1, (int)Mathf.Floor(Mathf.Max(xA, xB)));
            for (var x = xa; x <= xb; x++)
            {
                img.SetPixel(x, y, color);
            }
        }
    }

    /// <summary>线段在指定 y 处的 x（水平边退化返回起点 x）。</summary>
    private static float _edgeIntersect(Vector2 p, Vector2 q, float y)
    {
        if (Mathf.Abs(q.Y - p.Y) < 1.0e-6f)
        {
            return p.X;
        }

        return p.X + (y - p.Y) * (q.X - p.X) / (q.Y - p.Y);
    }
}
