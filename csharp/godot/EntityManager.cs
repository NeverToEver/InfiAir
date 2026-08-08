using Godot;

namespace InfiAir;

/// <summary>
/// 统一实体管理器（M2 全量迁移，2026-08-08 自 scripts/entity_manager.gd 迁移；
/// docs/ENTITY_MANAGER.md）：对局实体注册表 + 生命周期信号。
/// 语义保持：enemies 注册表 + O(1) has_enemy 热路径索引；敌弹注册表（death_replay 数据源）；
/// 特殊引用（player_ref/player_hitbox/bullet_pool/enemy_pool/aim_frame_layer/camera_ref/
/// virtual_controls）；统一绑定样板 bind_enemy/unbind_enemy。
/// M2 过渡：Callable 参数化批量 API（for_each/clear/count）留在 GDScript facade 层
/// （GDScript lambda 跨语言传参不可靠），直接迭代本类 Enemies 集合；随调用方迁移后由 C# 实现。
/// 迁移期类型注记：BulletPool/EnemyPool/AimFrameLayer/VirtualControls 为 GDScript 类，
/// 以 GodotObject 承载（动态），随 M3/M5 重定型为强类型。
/// </summary>
public partial class EntityManager : RefCounted
{
    /// <summary>实体注册信号（新功能订阅口，GameState 收口转发）。</summary>
    [Signal]
    public delegate void EntityRegisteredEventHandler(Node node);

    [Signal]
    public delegate void EntityUnregisteredEventHandler(Node node);

    /// <summary>enemies 注册表（GameState.enemies 转发）。</summary>
    public Godot.Collections.Array<Node> Enemies { get; } = new();

    /// <summary>G010：enemies 的 O(1) 存在性索引（追踪弹每帧 has 判定）。</summary>
    private readonly Godot.Collections.Dictionary _enemySet = new(); // node -> true

    /// <summary>P0-1：敌弹注册表（death_replay 录制数据源；M3 重定型 Array&lt;Bullet&gt;）。</summary>
    public Godot.Collections.Array<GodotObject> EnemyBullets { get; } = new();

    private readonly Godot.Collections.Dictionary _enemyBulletSet = new(); // node -> true

    public Node2D? PlayerRef { get; set; }

    public Area2D? PlayerHitbox { get; set; }

    /// <summary>子弹对象池实例（bullet_pool.gd 在 _ready 登记；M3 重定型 BulletPool）。</summary>
    public GodotObject? BulletPool { get; set; }

    /// <summary>敌机对象池实例（enemy_pool.gd 在 _ready 登记；M3 重定型 EnemyPool）。</summary>
    public GodotObject? EnemyPool { get; set; }

    /// <summary>辅助瞄准框覆盖层实例（M5 重定型 AimFrameLayer）。</summary>
    public GodotObject? AimFrameLayer { get; set; }

    public Camera2D? CameraRef { get; set; }

    /// <summary>触屏虚拟输入层实例（M5 重定型 VirtualControls）。</summary>
    public GodotObject? VirtualControls { get; set; }

    public void RegisterEnemy(Node node)
    {
        if (!Enemies.Contains(node))
        {
            Enemies.Add(node);
        }

        _enemySet[node] = true;
    }

    public void UnregisterEnemy(Node node)
    {
        Enemies.Remove(node);
        _enemySet.Remove(node);
    }

    /// <summary>G010：注册表存在性判定 O(1)（语义同注册表包含，deactivate 即移除）。</summary>
    public bool HasEnemy(Node node) => _enemySet.ContainsKey(node);

    /// <summary>P0-1：敌弹登记（幂等；set 全写点与数组同步维护）。</summary>
    public void RegisterEnemyBullet(GodotObject b)
    {
        if (!_enemyBulletSet.ContainsKey(b))
        {
            EnemyBullets.Add(b);
        }

        _enemyBulletSet[b] = true;
    }

    /// <summary>P0-1：敌弹注销（幂等；set 判定真实在册才扫数组）。</summary>
    public void UnregisterEnemyBullet(GodotObject b)
    {
        if (_enemyBulletSet.Remove(b))
        {
            EnemyBullets.Remove(b);
        }
    }

    /// <summary>统一单位绑定：add_to_group("enemy") + register_enemy + entity_registered 信号（幂等）。</summary>
    public void BindEnemy(Node node)
    {
        node.AddToGroup("enemy");
        RegisterEnemy(node);
        EmitSignal(SignalName.EntityRegistered, node);
    }

    /// <summary>统一单位解绑：unregister + entity_unregistered 信号（组随节点释放自动退出）。</summary>
    public void UnbindEnemy(Node node)
    {
        UnregisterEnemy(node);
        EmitSignal(SignalName.EntityUnregistered, node);
    }
}
