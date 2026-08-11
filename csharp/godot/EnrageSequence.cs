using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// Boss 狂暴状态机（A3 拆分，docs/AUDIT_VAULT.md A3；2026-08-08 全量迁移，自 scripts/enrage_sequence.gd）。
/// 狂暴 5 子状态机（TRANSITION→ACTIVE→RELEASE_HOLD→RETURN→NONE）+ 四型差异化 ACTIVE +
/// 轨道路径计算 + 锁血/玩家减速。经 Boss typed 公开属性/方法直读配置与位置，
/// 弹幕发射经注入 BossFire/BossAttacks，避免跨类私有访问（A1 约束）。
/// Y 系列（2026-08-09）：Boss 链 typed 化——StringName 动态派发（Get/Call）与双命名桥删除，
/// 参数直用 Boss 类型；Enemy 迁 C# 后直接 Enemy.SinFast/CosFast 与 is Enemy 判型。
/// </summary>
public partial class EnrageSequence : RefCounted
{
    // ---- 对齐 Boss.EnragePhase（enum { NONE, TRANSITION, ACTIVE, RELEASE_HOLD, RETURN }） ----
    public const int EnrageNone = 0;
    public const int EnrageTransition = 1;
    public const int EnrageActive = 2;
    public const int EnrageReleaseHold = 3;
    public const int EnrageReturn = 4;

    /// <summary>猎杀环绕瞬停点（右→上→左→下→右→上，共 6 点；末点为顶部，RELEASE 回底部）。</summary>
    private static readonly float[] StalkerPointAnglesDeg = { 0.0f, -90.0f, 180.0f, 90.0f, 0.0f, -90.0f };

    // ---- 注入：弹幕发射器 / 攻击状态机 / 机体缩放（Boss._ready 经 configure 传入） ----
    // V 系列：typed（原 GodotObject 动态派发）
    private BossFire _fire = null!;
    private BossAttacks _attacks = null!;

    /// <summary>机体缩放（configure 注入；小怪召唤偏移/环弹偏移共用）。</summary>
    public float WorldScale { get; set; } = 1.0f;

    // 狂暴序列状态（计时单位均为游戏秒，随 time_scale 缩放）
    private int _phase = EnrageNone;
    private float _timer; // TRANSITION+ACTIVE 剩余（progress 驱动轨道）
    private float _transitionTimer;
    private float _releaseHoldTimer;
    private float _returnTimer;
    private float _attackTimer;
    private int _attackIndex;
    /// <summary>锁血：触发→RELEASE_HOLD 开始前 HP 锁定在 30% 检查点（任何伤害不掉血不死）。</summary>
    private bool _healthLock;
    private Vector2 _snapshotTarget = Vector2.Zero; // 触发时玩家位置快照（轨道中心）
    private Vector2 _transitionOrigin = Vector2.Zero;
    private Vector2 _returnOrigin = Vector2.Zero;
    private Vector2 _returnTarget = Vector2.Zero;
    private Player? _slowedPlayer; // 被施加狂暴减速的玩家（用于精确复位；M3c：Player 已迁 C#）
    private Vector2 _bossSize = new(328.0f, 328.0f); // 贴图有效尺寸（begin 传入，算轨道半径）
    // 差异化狂暴各型状态
    private float _ringAngle; // 1 型环弹起始角（随波次进动）
    private float _summonTimer; // 3 型倾巢召唤计时
    private int _summonWaves; // 3 型已放小怪波数
    private float _aimElapsed = -1.0f; // 2 型逐点瞄准计时（<0 = 未瞄准）
    private bool _releaseSalvoDone; // 1/2 型 RELEASE 一次性收尾已结算
    private Vector2 _releaseOrigin = Vector2.Zero; // 2 型 RELEASE 回轨道底部起点
    private Line2D? _aimLine;
    private Vector2 _sniperDir = Vector2.Down;

    // A3 收敛：狂暴各阶段机型处理器注册表（boss_type → 处理器，_init 装配）。
    // 新增机型只需注册一行 + 一个处理器方法，不再改 update/_begin_release_hold 的 match（O 原则达成）。
    // AC10（2026-08-11 健壮性审查）：注册表 Callable → System.Action 直调（BossMovement.cs:44-54
    // 同款先例）——狂暴期每物理帧 TryGetValue 后 Call 走 Godot 动态派发（委托包装/跨边界开销），
    // 改 Action 委托直调零分配；注册/查询公开接口签名不变，行为逐字节等价
    private readonly Dictionary<int, System.Action<float, Boss>> _activeHandlers = new();
    private readonly Dictionary<int, System.Action<float, Boss>> _releaseHandlers = new();
    private readonly Dictionary<int, System.Action<Boss>> _releaseBeginHandlers = new();

    // A3 机型参数表：TRANSITION 阶段悬停原地不滑入轨道（1 型「旋转堡垒」专属）
    private static readonly Godot.Collections.Dictionary<int, bool> TransitionHoverTypes = new()
    {
        [1] = true,
        [4] = true,
    };

    // 热路径缓存：view_world_rect / player_ref 每物理帧一次动态调用（与 Enemy.cs 同款）。
    // U07：静态 Variant 持 Godot 对象引用改实例字段（悬空访问 + 退出 finalize 触碰风险）
    private ulong _frame = ulong.MaxValue;
    private Rect2 _frameView;
    private Variant _framePlayer;

    private Rect2 CachedView()
    {
        var f = Engine.GetPhysicsFrames();
        if (f != _frame)
        {
            _frame = f;
            _frameView = GameState.Instance.ViewWorldRect();
            _framePlayer = GameState.Instance.PlayerRef!;
        }

        return _frameView;
    }

    private Variant CachedPlayer() => _frame == Engine.GetPhysicsFrames() ? _framePlayer : GameState.Instance.PlayerRef!;

    public EnrageSequence()
    {
        _activeHandlers[1] = ActiveBulwark;
        _activeHandlers[2] = ActiveStalker;
        _activeHandlers[3] = ActiveHive;
        _activeHandlers[4] = ActiveEclipse;
        _releaseHandlers[1] = ReleaseBulwark;
        _releaseHandlers[2] = ReleaseStalker;
        _releaseHandlers[3] = ReleaseHive;
        _releaseHandlers[4] = ReleaseEclipse;
        _releaseBeginHandlers[1] = ReleaseBeginBulwark;
        _releaseBeginHandlers[2] = ReleaseBeginStalker;
        _releaseBeginHandlers[3] = ReleaseBeginHive;
        _releaseBeginHandlers[4] = ReleaseBeginEclipse;
    }

    /// <summary>注册表完整性查询（A3 架构断言测试经公开接口访问）。</summary>
    public bool HasActiveHandler(int type) => _activeHandlers.ContainsKey(type);

    /// <summary>注册表完整性查询（A3 架构断言测试经公开接口访问）。</summary>
    public bool HasReleaseHandler(int type) => _releaseHandlers.ContainsKey(type);

    /// <summary>注册表完整性查询（A3 架构断言测试经公开接口访问）。</summary>
    public bool HasReleaseBeginHandler(int type) => _releaseBeginHandlers.ContainsKey(type);

    /// <summary>注入发射器 / 攻击状态机 / 机体缩放（Boss._ready 调用）。V 系列：参数 typed。</summary>
    public void Configure(BossFire fire, BossAttacks attacks, float ws)
    {
        _fire = fire;
        _attacks = attacks;
        WorldScale = ws;
    }

    /// <summary>狂暴序列进行中（Boss._physics_process 据此进入序列驱动）。</summary>
    public bool IsActive() => _phase != EnrageNone;

    /// <summary>状态查询（测试/诊断白盒断言经公开接口，A3）。</summary>
    public int Phase() => _phase;

    /// <summary>
    /// 1/2 型 RELEASE 一次性收尾是否已结算（测试观测；不依赖弹在场时序——
    /// 8 路齐射向上路 ~0.27s 即出屏，场上计数在慢 runner 上与发射时刻竞争失败）。
    /// </summary>
    public bool ReleaseSalvoDone() => _releaseSalvoDone;

    /// <summary>已推进的 ACTIVE 攻击波次数（测试/诊断白盒断言经公开接口，A3）。</summary>
    public int AttackIndex() => _attackIndex;

    /// <summary>1 型环弹当前起始角（测试/诊断白盒断言经公开接口，A3）。</summary>
    public float RingAngle() => _ringAngle;

    /// <summary>3 型已放小怪波数（测试/诊断白盒断言经公开接口，A3）。</summary>
    public int SummonWaves() => _summonWaves;

    /// <summary>触发时玩家位置快照（轨道中心；测试/诊断白盒断言经公开接口，A3）。</summary>
    public Vector2 SnapshotTarget() => _snapshotTarget;

    /// <summary>2 型瞄准线引用查询（测试/诊断白盒断言经公开接口，A3）。</summary>
    public Line2D? AimLine() => _aimLine;

    /// <summary>释放本类持有的瞄准线（B1 修复）。`BossAttacks.MakeAimLine` 创建的 Line2D
    /// 只由本类 `_aimLine` 持有，`BossAttacks.CancelAimLine()` 仅清其自身 `_aimLine`，
    /// 到不了这里——不显式清理会残留静态瞄准线并泄漏节点（每次 2 型狂暴约 6 个）。</summary>
    private void FreeAimLine()
    {
        if (_aimLine != null)
        {
            _aimLine.QueueFree();
            _aimLine = null;
        }
    }

    /// <summary>狂暴触发初始化（Boss._enrage 调用：数据 + 锁血 + 玩家减速；表现由 Boss 侧负责）。</summary>
    public void Begin(Boss boss, Vector2 snapshotPos, Vector2 bossSize)
    {
        _snapshotTarget = snapshotPos;
        _bossSize = bossSize;
        _ringAngle = 0.0f;
        _summonWaves = 0;
        _summonTimer = boss.E3SummonInterval;
        _aimElapsed = -1.0f;
        _releaseSalvoDone = false;
        _healthLock = true;
        _phase = EnrageTransition;
        _timer = boss.EnrageDuration;
        _transitionTimer = boss.EnrageTransitionDuration;
        _transitionOrigin = boss.Position;
        LockPlayerMovement(boss);
    }

    /// <summary>序列中断（逃跑/死亡/离场/教程收尾）：清状态 + 解血锁 + 复位减速 + 清 telegraph，幂等。</summary>
    public void Abort()
    {
        _phase = EnrageNone;
        _healthLock = false;
        _attacks.CancelAimLine();
        FreeAimLine();
        _aimElapsed = -1.0f;
        UnlockPlayerMovement();
    }

    /// <summary>兜底解锁（Boss._exit_tree 调用：任何离场路径不留玩家减速残留）。</summary>
    public void UnlockPlayer() => UnlockPlayerMovement();

    /// <summary>狂暴进行中是否锁血（Boss.take_damage 查询）。</summary>
    public bool IsHealthLocked() => _healthLock;

    /// <summary>
    /// 狂暴序列驱动：TRANSITION（蓄力抖动滑入轨道，1 型悬停原地）→ ACTIVE（各型差异化攻击）
    /// → RELEASE_HOLD（各型收尾爆发，§5.4 峰值）→ RETURN（飞回战斗位）→ NONE（常规「余怒」循环）。
    /// </summary>
    public void Update(float delta, Boss boss)
    {
        switch (_phase)
        {
            case EnrageTransition:
                _timer = Mathf.Max(_timer - delta, 0.0f);
                _transitionTimer -= delta;
                var t = Mathf.Clamp(1.0f - _transitionTimer / boss.EnrageTransitionDuration, 0.0f, 1.0f);
                var eased = 1.0f - Mathf.Pow(1.0f - t, 3.0f);
                var shake = new Vector2(
                    Enemy.SinFast(t * Mathf.Tau * 7.0f) * (1.0f - t) * 13.0f, Enemy.CosFast(t * Mathf.Tau * 5.0f) * (1.0f - t) * 8.0f);
                // 机型参数表：1 型「旋转堡垒」悬停原地，不滑入轨道
                var targetPos = HoverInTransition(boss) ? _transitionOrigin : PathCenter(Progress(boss), boss);
                boss.Position = _transitionOrigin.Lerp(targetPos, eased) + shake;
                if (_transitionTimer <= 0.0f)
                {
                    _phase = EnrageActive;
                    _attackTimer = boss.EnrageAttackWindup;
                    _attackIndex = 0;
                }

                break;
            case EnrageActive:
                _timer = Mathf.Max(_timer - delta, 0.0f);
                var typeA = boss.BossType;
                if (_activeHandlers.TryGetValue(typeA, out var activeHandler))
                {
                    activeHandler(delta, boss);
                }
                else
                {
                    ActiveFallback(delta, boss);
                }

                if (_timer <= 0.0f)
                {
                    BeginReleaseHold(boss);
                }

                break;
            case EnrageReleaseHold:
                _releaseHoldTimer -= delta;
                var typeR = boss.BossType;
                if (_releaseHandlers.TryGetValue(typeR, out var releaseHandler))
                {
                    releaseHandler(delta, boss);
                }
                else
                {
                    ReleaseFallback(delta, boss);
                }

                if (_releaseHoldTimer <= 0.0f)
                {
                    BeginReturn(boss);
                }

                break;
            case EnrageReturn:
                _returnTimer -= delta;
                var rt = Mathf.Clamp(1.0f - _returnTimer / boss.EnrageReturnDuration, 0.0f, 1.0f);
                var reased = rt * rt * (3.0f - 2.0f * rt);
                boss.Position = _returnOrigin.Lerp(_returnTarget, reased);
                if (_returnTimer <= 0.0f)
                {
                    _phase = EnrageNone;
                }

                break;
        }
    }

    /// <summary>1 型「旋转堡垒」ACTIVE：悬停原地，每 0.5s 一波 12 向环弹（起始角随波次进动）。</summary>
    private void ActiveBulwark(float delta, Boss boss)
    {
        _attackTimer -= delta;
        if (_attackTimer <= 0.0f)
        {
            _attackTimer = boss.E1RingInterval;
            _fire.FireRing(boss, boss.E1RingCount, boss.E1RingSpeed, boss.BulletDamageSnapshotRing, _ringAngle);
            _ringAngle += Mathf.DegToRad(boss.E1RingPrecessionDeg);
            _attackIndex += 1;
        }
    }

    /// <summary>2 型「猎杀环绕」ACTIVE：轨道 4 象限 6 点依次瞬停，每点 0.3s 瞄准线 + 单发狙。</summary>
    private void ActiveStalker(float delta, Boss boss)
    {
        if (_aimElapsed >= 0.0f)
        {
            _aimElapsed += delta;
            _sniperDir = PlayerDir(boss);
            if (_aimLine != null)
            {
                // C23：创建时已 add_point 预置 2 点，set_point_position 原地写（points[i]= 值语义不生效）
                _aimLine.SetPointPosition(0, _sniperDir * boss.MuzzleOffset);
                _aimLine.SetPointPosition(1, _sniperDir * 1200.0f);
                _aimLine.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.18f + 0.18f * Mathf.Abs(Enemy.SinFast(_aimElapsed * 25.0f)));
            }

            if (_aimElapsed >= boss.E2Aim)
            {
                FreeAimLine();
                _aimElapsed = -1.0f;
                _fire.FireHeavy(boss, _sniperDir, boss.E2SniperSpeed, boss.E2SniperDamage);
            }
        }

        _attackTimer -= delta;
        if (_attackTimer <= 0.0f && _attackIndex < boss.E2PointCount)
        {
            var angle = Mathf.DegToRad(StalkerPointAnglesDeg[_attackIndex % StalkerPointAnglesDeg.Length]);
            boss.Position = _snapshotTarget + new Vector2(Enemy.CosFast(angle), Enemy.SinFast(angle)) * PathRadius(boss);
            _attackIndex += 1;
            _attackTimer = boss.E2PointInterval;
            _attacks.CancelAimLine();
            FreeAimLine();
            _aimElapsed = 0.0f;
            _sniperDir = PlayerDir(boss);
            _aimLine = _attacks.MakeAimLine(boss, _sniperDir, 1200.0f);
        }
    }

    /// <summary>3 型「倾巢」ACTIVE：共用轨道环绕 + 每 1.2s 一波 3 小怪（共 3 波）+ 每 0.9s 一圈 8 向环弹。</summary>
    private void ActiveHive(float delta, Boss boss)
    {
        boss.Position = PathCenter(Progress(boss), boss);
        _attackTimer -= delta;
        if (_attackTimer <= 0.0f)
        {
            _attackTimer = boss.E3RingInterval;
            _fire.FireRing(boss, boss.E3RingCount, boss.E3RingSpeed, boss.BulletDamageSnapshotRing, 0.0f);
            _attackIndex += 1;
        }

        if (_summonWaves < boss.E3SummonWaves)
        {
            _summonTimer -= delta;
            if (_summonTimer <= 0.0f)
            {
                _summonTimer = boss.E3SummonInterval;
                _summonWaves += 1;
                var count = boss.E3SummonCount;
                for (var i = 0; i < count; i++)
                {
                    boss.SpawnMinionAt(
                        boss.Position + new Vector2((float)GD.RandRange(-80.0, 80.0), 110.0f) * WorldScale);
                }
            }
        }
    }

    /// <summary>4 型「月蚀」ACTIVE（2026-08-04）：中心悬停 + 双环反向进动——正环 + 反角环
    /// 交替成波（起始角 = precession × 波次索引，每波进动 E4_PRECESSION_DEG；_ring_angle 为 1 型字段，不在此用）。</summary>
    private void ActiveEclipse(float delta, Boss boss)
    {
        _attackTimer -= delta;
        if (_attackTimer <= 0.0f)
        {
            _attackTimer = boss.E4RingInterval;
            var angle = Mathf.DegToRad(boss.E4PrecessionDeg) * _attackIndex;
            _fire.FireRing(boss, boss.E4RingCount, boss.E4RingSpeed, boss.BulletDamageSnapshotRing, angle);
            _fire.FireRing(boss, boss.E4RingCount, boss.E4RingSpeed, boss.BulletDamageSnapshotRing, -angle);
            _attackIndex += 1;
        }
    }

    /// <summary>4 型「月蚀」释放——无持续结算（环阵已在 RELEASE_BEGIN 一次性结算）。</summary>
    private void ReleaseEclipse(float delta, Boss boss)
    {
    }

    /// <summary>4 型「月蚀」释放起手——蓄力环阵（20 向快慢双环）。</summary>
    private void ReleaseBeginEclipse(Boss boss)
    {
        _fire.FireRing(
            boss, boss.E4ReleaseRingCount, boss.E4ReleaseRingSpeed, boss.BulletDamageSnapshotRing, 0.0f);
    }

    /// <summary>序列进度 0→1（TRANSITION 起算，ACTIVE 结束到 1；对齐原作 enrage_progress）。</summary>
    private float Progress(Boss boss) => Mathf.Clamp(1.0f - _timer / boss.EnrageDuration, 0.0f, 1.0f);

    /// <summary>轨道半径：max(机体宽,高)×1.5，受屏幕边界约束（对齐原作 enrage_path_radius，下限 24）。</summary>
    private float PathRadius(Boss boss)
    {
        var baseRadius = Mathf.Max(_bossSize.X, _bossSize.Y) * boss.EnragePathRadiusScale;
        var view = CachedView();
        var half = _bossSize * 0.5f;
        var maxRadius = Mathf.Max(
            24.0f,
            Mathf.Min(
                Mathf.Min(_snapshotTarget.X - view.Position.X - half.X, view.End.X - _snapshotTarget.X - half.X),
                Mathf.Min(_snapshotTarget.Y - view.Position.Y - half.Y, view.End.Y - _snapshotTarget.Y - half.Y)));
        return Mathf.Min(baseRadius, maxRadius);
    }

    /// <summary>C10：方形路径角点（底→左→顶→右，index 4 循环回底），配合 PathCenter 无数组求值。</summary>
    private Vector2 SquareCorner(int index, float radius)
    {
        switch (index % 4)
        {
            case 0:
                return new Vector2(0.0f, radius);
            case 1:
                return new Vector2(-radius, 0.0f);
            case 2:
                return new Vector2(0.0f, -radius);
            default:
                return new Vector2(radius, 0.0f);
        }
    }

    /// <summary>轨道中心：前 48% 方形路径（底→左→顶→右→底），后 52% 圆形路径（底部起顺接）。</summary>
    private Vector2 PathCenter(float progress, Boss boss)
    {
        progress = Mathf.Clamp(progress, 0.0f, 1.0f);
        var radius = PathRadius(boss);
        var c = _snapshotTarget;
        var squareRatio = boss.EnrageSquarePathRatio;
        if (progress <= squareRatio)
        {
            var sp = progress / squareRatio;
            var segment = Mathf.Min(3, (int)(sp * 4.0f));
            var local = sp * 4.0f - segment;
            // C10：方形路径四角直接求两端点 lerp，避免每帧构建 5 元素数组（GC 压力）
            var from = c + SquareCorner(segment, radius);
            var to = c + SquareCorner(segment + 1, radius);
            return from.Lerp(to, local);
        }

        var cp = (progress - squareRatio) / (1.0f - squareRatio);
        var angle = Mathf.Pi / 2.0f + cp * Mathf.Tau;
        return c + new Vector2(Enemy.CosFast(angle), Enemy.SinFast(angle)) * radius;
    }

    /// <summary>ACTIVE 计时耗尽：进入释放阶段——解血锁、复位玩家减速 + 各型收尾爆发起手（§5.4 峰值）。</summary>
    private void BeginReleaseHold(Boss boss)
    {
        _phase = EnrageReleaseHold;
        _releaseHoldTimer = boss.EnrageReleaseHoldDuration;
        _healthLock = false;
        UnlockPlayerMovement();
        _attacks.CancelAimLine();
        FreeAimLine();
        _aimElapsed = -1.0f;
        _releaseSalvoDone = false;
        var type = boss.BossType;
        if (_releaseBeginHandlers.TryGetValue(type, out var beginHandler))
        {
            beginHandler(boss);
        }
        else
        {
            _attackTimer = 0.0f; // 回退路径：立即放第一波
        }
    }

    /// <summary>倾巢收尾：全部在场小怪齐射一轮自机狙。</summary>
    private void HiveVolleyAllMinions(Boss boss)
    {
        var minions = new Godot.Collections.Array();
        // 统一实体管理器批量 API（docs/ENTITY_MANAGER.md）：收集在场活跃小怪
        // M3d：直接遍历注册表（for_each_enemy 的 bool 谓词无法用 Callable.From——无 Func 重载）；
        // 语义等价：失效实例跳过 + Enemy 判型 + 活跃过滤
        var enemies = (Godot.Collections.Array)GameState.Instance.Enemies;
        foreach (var item in enemies)
        {
            if (item.AsGodotObject() is Enemy enemy && enemy.IsActive())
            {
                minions.Add(enemy);
            }
        }

        _attacks.MinionVolleyFire(boss, minions);
    }

    /// <summary>RELEASE_HOLD 结束：0.8s 飞回战斗位（x 钳回巡航范围、y 回战斗锚线 view 顶 + FIGHT_Y）。</summary>
    private void BeginReturn(Boss boss)
    {
        _phase = EnrageReturn;
        _returnTimer = boss.EnrageReturnDuration;
        _returnOrigin = boss.Position;
        var bounds = boss.StrafeRange();
        _returnTarget = new Vector2(Mathf.Clamp(boss.Position.X, bounds.X, bounds.Y), boss.FightAnchorY());
    }

    /// <summary>狂暴期玩家减速（替代原作 is_controls_locked 定身，§4.3）：移速 ×0.35，
    /// 仍可瞄准/射击/冲刺；TRANSITION+ACTIVE 有效。</summary>
    private void LockPlayerMovement(Boss boss)
    {
        var playerV = CachedPlayer();
        if (playerV.VariantType != Variant.Type.Nil)
        {
            var p = (Player)playerV;
            if (!p.IsDead())
            {
                _slowedPlayer = p;
                p.ApplyEnrageSlow(boss.EnragePlayerSlow);
            }
        }
    }

    private void UnlockPlayerMovement()
    {
        if (_slowedPlayer != null)
        {
            if (GodotObject.IsInstanceValid(_slowedPlayer))
            {
                _slowedPlayer.ApplyEnrageSlow(1.0f);
            }

            _slowedPlayer = null;
        }
    }

    /// <summary>面向玩家的方向（player 为空回退 Vector2.DOWN）。</summary>
    private Vector2 PlayerDir(Boss from)
    {
        var player = CachedPlayer();
        if (player.VariantType != Variant.Type.Nil)
        {
            var p = (Node2D)player;
            return (p.GlobalPosition - from.GlobalPosition).Normalized();
        }

        return Vector2.Down;
    }

    /// <summary>A3：TRANSITION 悬停判定（机型参数表驱动，取代散落的类型特判）。</summary>
    private bool HoverInTransition(Boss boss)
    {
        var type = boss.BossType;
        return TransitionHoverTypes.TryGetValue(type, out var hover) && hover;
    }

    /// <summary>A3：ACTIVE 回退处理器（非法 boss_type，防御路径：轨道环绕 + 定时狂暴波）。</summary>
    private void ActiveFallback(float delta, Boss boss)
    {
        boss.Position = PathCenter(Progress(boss), boss);
        _attackTimer -= delta;
        if (_attackTimer <= 0.0f)
        {
            _attackTimer = boss.EnrageAttackInterval;
            _attackIndex += 1;
            _fire.FireEnrageWave(
                boss,
                boss.EnrageLaserSpeed,
                boss.EnrageRingSpeed,
                boss.BulletDamageSnapshotLaser,
                boss.BulletDamageSnapshotRing,
                boss.EnrageSnapshotLasers,
                boss.EnrageSnapshotRing);
        }
    }

    /// <summary>A3：1 型「旋转堡垒」释放——8 路蓄力重炮齐射（蓄力辉光 telegraph 已在 ReleaseBeginBulwark 起手）。</summary>
    private void ReleaseBulwark(float delta, Boss boss)
    {
        if (!_releaseSalvoDone)
        {
            _attackTimer -= delta;
            if (_attackTimer <= 0.0f)
            {
                _releaseSalvoDone = true;
                var count = boss.E1SalvoCount;
                for (var i = 0; i < count; i++)
                {
                    var dir = Vector2.Right.Rotated(Mathf.Tau * (float)i / (float)count);
                    _fire.FireHeavy(boss, dir, boss.E1SalvoSpeed, boss.E1SalvoDamage);
                }
            }
        }
    }

    /// <summary>A3：2 型「猎杀环绕」释放——回轨道底部放 12 向慢速环弹。</summary>
    private void ReleaseStalker(float delta, Boss boss)
    {
        var t = Mathf.Clamp(1.0f - _releaseHoldTimer / boss.EnrageReleaseHoldDuration, 0.0f, 1.0f);
        var eased = t * t * (3.0f - 2.0f * t);
        boss.Position = _releaseOrigin.Lerp(_snapshotTarget + new Vector2(0.0f, PathRadius(boss)), eased);
        if (!_releaseSalvoDone && t >= 0.5f)
        {
            _releaseSalvoDone = true;
            _fire.FireRing(
                boss, boss.E2ReleaseRingCount, boss.E2ReleaseRingSpeed, boss.BulletDamageSnapshotRing, 0.0f);
        }
    }

    /// <summary>A3：3 型「倾巢」释放——无持续结算（16 向环弹 + 小怪齐射已在 ReleaseBeginHive 一次性结算）。</summary>
    private void ReleaseHive(float delta, Boss boss)
    {
    }

    /// <summary>A3：RELEASE_HOLD 回退处理器（非法 boss_type，防御路径：按固定间隔放狂暴波）。</summary>
    private void ReleaseFallback(float delta, Boss boss)
    {
        _attackTimer -= delta;
        if (_attackTimer <= 0.0f)
        {
            _attackTimer = boss.EnrageReleaseInterval;
            _fire.FireEnrageWave(
                boss,
                boss.EnrageReleaseLaserSpeed,
                boss.EnrageReleaseRingSpeed,
                boss.BulletDamageSnapshotLaser,
                boss.BulletDamageSnapshotRing,
                boss.EnrageSnapshotLasers,
                boss.EnrageSnapshotRing);
        }
    }

    /// <summary>A3：1 型释放起手——蓄力辉光 telegraph。</summary>
    private void ReleaseBeginBulwark(Boss boss)
    {
        var charge = boss.E1SalvoCharge;
        _attackTimer = charge;
        _attacks.ChargeGlow(boss, charge);
    }

    /// <summary>A3：2 型释放起手——记录当前位置为回轨道底部起点。</summary>
    private void ReleaseBeginStalker(Boss boss) => _releaseOrigin = boss.Position;

    /// <summary>A3：3 型释放起手——16 向环弹 + 全部在场小怪齐射（§5.4 峰值一次性结算）。</summary>
    private void ReleaseBeginHive(Boss boss)
    {
        _fire.FireRing(
            boss, boss.E3ReleaseRingCount, boss.E3ReleaseRingSpeed, boss.BulletDamageSnapshotRing, 0.0f);
        HiveVolleyAllMinions(boss);
    }
}
