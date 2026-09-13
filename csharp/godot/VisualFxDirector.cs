using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 战斗视觉总监：订阅实体注册/注销信号，为敌机击杀与 Boss 阶段/狂暴生成即时世界特效。
/// 纯表现：只读位置/阵营/进阶标志与 reduce_flash，绝不回写玩法字段——伤害、时序、位置、概率不变。
/// 由场景挂载（Main 子节点）；未挂载时全库行为与挂载前一致。
/// 并发封顶：同时最多 MaxLiveDirectorFx 个总监特效，超限跳过（不排队、不延迟）。
/// 池化敌机的 Died 连接会被 Enemy.Deactivate 主动断开，故在 VisibilityChanged 上幂等补连，
/// 保证同一实例复用到下一条命仍有击杀环；Boss 的 Died 亦用于退订清理。
/// </summary>
public partial class VisualFxDirector : Node
{
    /// <summary>总监特效同时在活上限：击杀/Boss 转场同帧并发时防爆发式堆节点。</summary>
    private const int MaxLiveDirectorFx = 10;

    private readonly HashSet<Enemy> _enemies = new();
    private readonly Dictionary<Enemy, Callable> _enemyVisHooks = new();

    // 共享 Callable 延迟到构造器绑定（字段初始值设定项不能引用实例方法）。
    // Enemy.Died 自带 this 参数，无需逐敌闭包；其余为无参/单参槽位。
    private readonly Callable _enemyDied;

    private readonly Callable _onEntityRegistered;
    private readonly Callable _onEntityUnregistered;
    private readonly Callable _onReduceFlashChanged;

    private Boss? _boss;
    private readonly Callable _bossPhase;
    private readonly Callable _bossEnraged;
    private readonly Callable _bossDied;

    private bool _reduceFlash;
    private int _live;

    public VisualFxDirector()
    {
        _enemyDied = Callable.From<Enemy>(OnEnemyDied);
        _onEntityRegistered = Callable.From<Node>(OnEntityRegistered);
        _onEntityUnregistered = Callable.From<Node>(OnEntityUnregistered);
        _onReduceFlashChanged = Callable.From<bool>(OnReduceFlashChanged);
        _bossPhase = Callable.From<int>(OnBossPhaseChanged);
        _bossEnraged = Callable.From(OnBossEnraged);
        _bossDied = Callable.From(OnBossDied);
    }

    public override void _Ready()
    {
        var gs = GameState.Instance;
        _reduceFlash = gs.ReduceFlash;
        if (!gs.IsConnected(GameState.SignalName.EntityRegistered, _onEntityRegistered))
        {
            gs.Connect(GameState.SignalName.EntityRegistered, _onEntityRegistered);
        }

        if (!gs.IsConnected(GameState.SignalName.EntityUnregistered, _onEntityUnregistered))
        {
            gs.Connect(GameState.SignalName.EntityUnregistered, _onEntityUnregistered);
        }

        if (!gs.IsConnected(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged))
        {
            gs.Connect(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged);
        }

        // 补订阅已在场实体（总监晚于实体入树时仍覆盖）
        foreach (var node in gs.Enemies)
        {
            Track(node);
        }
    }

    public override void _ExitTree()
    {
        var gs = GameState.TryGetInstance();
        if (gs != null)
        {
            if (gs.IsConnected(GameState.SignalName.EntityRegistered, _onEntityRegistered))
            {
                gs.Disconnect(GameState.SignalName.EntityRegistered, _onEntityRegistered);
            }

            if (gs.IsConnected(GameState.SignalName.EntityUnregistered, _onEntityUnregistered))
            {
                gs.Disconnect(GameState.SignalName.EntityUnregistered, _onEntityUnregistered);
            }

            if (gs.IsConnected(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged))
            {
                gs.Disconnect(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged);
            }
        }

        foreach (var e in _enemies)
        {
            UntrackEnemy(e);
        }

        _enemies.Clear();
        _enemyVisHooks.Clear();
        UntrackBoss();
    }

    private void OnReduceFlashChanged(bool enabled) => _reduceFlash = enabled;

    private void OnEntityRegistered(Node node) => Track(node);

    private void OnEntityUnregistered(Node node)
    {
        if (node is Enemy e)
        {
            UntrackEnemy(e);
        }
        else if (node is Boss b && ReferenceEquals(b, _boss))
        {
            UntrackBoss();
        }
    }

    private void Track(Node node)
    {
        if (node is Boss boss)
        {
            TrackBoss(boss);
        }
        else if (node is Enemy enemy)
        {
            TrackEnemy(enemy);
        }
    }

    // ---------------- 敌机击杀环 ----------------

    private void TrackEnemy(Enemy e)
    {
        if (!_enemies.Add(e))
        {
            return;
        }

        // 每次复活的可见性翻转都补连 Died（Enemy.Deactivate 会主动断开全部 Died 连接）。
        // 该 Callable 捕获本敌实例（Callable 无 Bind），随敌销毁/退订一并丢弃。
        var visHook = Callable.From(() => ArmEnemyDied(e));
        _enemyVisHooks[e] = visHook;
        if (GodotObject.IsInstanceValid(e) && !e.IsConnected(CanvasItem.SignalName.VisibilityChanged, visHook))
        {
            e.Connect(CanvasItem.SignalName.VisibilityChanged, visHook);
        }

        ArmEnemyDied(e);
    }

    private void UntrackEnemy(Enemy e)
    {
        _enemies.Remove(e);
        if (_enemyVisHooks.Remove(e, out var visHook)
            && GodotObject.IsInstanceValid(e)
            && e.IsConnected(CanvasItem.SignalName.VisibilityChanged, visHook))
        {
            e.Disconnect(CanvasItem.SignalName.VisibilityChanged, visHook);
        }

        if (GodotObject.IsInstanceValid(e) && e.IsConnected(Enemy.SignalName.Died, _enemyDied))
        {
            e.Disconnect(Enemy.SignalName.Died, _enemyDied);
        }
    }

    /// <summary>幂等补连 Died：已连不重复（重复 Connect 会报信号已连接）。</summary>
    private void ArmEnemyDied(Enemy e)
    {
        if (!GodotObject.IsInstanceValid(e))
        {
            return;
        }

        if (!e.IsConnected(Enemy.SignalName.Died, _enemyDied))
        {
            e.Connect(Enemy.SignalName.Died, _enemyDied);
        }
    }

    private void OnEnemyDied(Enemy e)
    {
        if (!GodotObject.IsInstanceValid(e) || !UnderCap())
        {
            return;
        }

        Spawn(CombatVfx.KillRing(GetParent(), e.GlobalPosition, e.IsElite, _reduceFlash));
    }

    // ---------------- Boss 阶段与狂暴 ----------------

    private void TrackBoss(Boss b)
    {
        if (ReferenceEquals(_boss, b))
        {
            return;
        }

        UntrackBoss();
        _boss = b;
        if (!b.IsConnected(Boss.SignalName.PhaseChanged, _bossPhase))
        {
            b.Connect(Boss.SignalName.PhaseChanged, _bossPhase);
        }

        if (!b.IsConnected(Boss.SignalName.Enraged, _bossEnraged))
        {
            b.Connect(Boss.SignalName.Enraged, _bossEnraged);
        }

        if (!b.IsConnected(Boss.SignalName.Died, _bossDied))
        {
            b.Connect(Boss.SignalName.Died, _bossDied);
        }
    }

    private void UntrackBoss()
    {
        var b = _boss;
        _boss = null;
        if (b == null || !GodotObject.IsInstanceValid(b))
        {
            return;
        }

        if (b.IsConnected(Boss.SignalName.PhaseChanged, _bossPhase))
        {
            b.Disconnect(Boss.SignalName.PhaseChanged, _bossPhase);
        }

        if (b.IsConnected(Boss.SignalName.Enraged, _bossEnraged))
        {
            b.Disconnect(Boss.SignalName.Enraged, _bossEnraged);
        }

        if (b.IsConnected(Boss.SignalName.Died, _bossDied))
        {
            b.Disconnect(Boss.SignalName.Died, _bossDied);
        }
    }

    private void OnBossPhaseChanged(int newPhase)
    {
        // 进入狂暴时 PhaseChanged 与 Enraged 同帧发出，舍此取彼避免双爆；
        // 开局为 P1 不经 EnterPhase，故不会在出生时误触发。
        if (newPhase == (int)Boss.FightPhase.ENRAGE)
        {
            return;
        }

        var b = _boss;
        if (b == null || !GodotObject.IsInstanceValid(b) || !UnderCap())
        {
            return;
        }

        Spawn(CombatVfx.PhaseShockwave(GetParent(), b.GlobalPosition, _reduceFlash));
    }

    private void OnBossEnraged()
    {
        var b = _boss;
        if (b == null || !GodotObject.IsInstanceValid(b) || !UnderCap())
        {
            return;
        }

        Spawn(CombatVfx.EnrageBurst(GetParent(), b.GlobalPosition, _reduceFlash));
    }

    private void OnBossDied() => UntrackBoss();

    // ---------------- 总监并发封顶 ----------------

    private void Spawn(Node2D? fx)
    {
        if (fx == null)
        {
            return;
        }

        _live++;
        fx.TreeExited += () =>
        {
            _live = Mathf.Max(_live - 1, 0);
        };
    }

    /// <summary>是否仍有总监特效配额（调用方在构建前查询，避免超限时白建节点）。</summary>
    private bool UnderCap() => _live < MaxLiveDirectorFx;
}
