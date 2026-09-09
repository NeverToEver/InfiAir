using Godot;

namespace InfiAir;

/// <summary>
/// 统一实体管理器：对局实体注册表 + 生命周期信号。
/// 语义：enemies 注册表 + O(1) has_enemy 热路径索引；敌弹注册表（death_replay 数据源）；
/// 特殊引用（player_ref/player_hitbox/bullet_pool/enemy_pool/aim_frame_layer/camera_ref/
/// virtual_controls）；统一绑定样板 bind_enemy/unbind_enemy。
/// 消费方直接迭代本类 Enemies 集合；BulletPool/EnemyPool/AimFrameLayer/VirtualControls
/// 均为 C# 类（BulletPool 已重定型强类型，其余以 GodotObject 承载）。
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

    /// <summary>2026-08-10 审计：enemies 在册索引表（node -> 数组下标），swap-remove 双维护
    /// （_enemyBulletIndex 同款模式）——原 UnregisterEnemy 的 Array.Remove 为 O(n) 线性扫描+搬移，
    /// 敌机死亡即触发（池化回收路径每次死亡调两次）。消费方已核实不依赖数组顺序：
    /// 迭代类（AimFrameLayer 严格最近选取/Bullet 溅射倒序/Mothership/EnrageSequence/Main 清场/
    /// DeathReplay 只读敌弹表）与 Contains/Count 判定均与顺序无关。</summary>
    private readonly Godot.Collections.Dictionary _enemyIndex = new();

    /// <summary>P0-1：敌弹注册表（death_replay 录制数据源；元素为 Bullet）。</summary>
    public Godot.Collections.Array<GodotObject> EnemyBullets { get; } = new();

    private readonly Godot.Collections.Dictionary _enemyBulletSet = new(); // node -> true

    /// <summary>2026-08-09 审计：敌弹在册索引表（node -> 数组下标），swap-remove 双维护。</summary>
    private readonly Godot.Collections.Dictionary _enemyBulletIndex = new();

    public Node2D? PlayerRef { get; set; }

    public Area2D? PlayerHitbox { get; set; }

    /// <summary>子弹对象池实例（BulletPool 自身 _Ready 登记）。</summary>
    public BulletPool? BulletPool { get; set; }

    /// <summary>敌机对象池实例（EnemyPool 自身 _Ready 登记）。</summary>
    public GodotObject? EnemyPool { get; set; }

    /// <summary>辅助瞄准框覆盖层实例（AimFrameLayer 自身 _Ready 登记）。</summary>
    public GodotObject? AimFrameLayer { get; set; }

    public Camera2D? CameraRef { get; set; }

    /// <summary>触屏虚拟输入层实例（M5 重定型 VirtualControls）。</summary>
    public GodotObject? VirtualControls { get; set; }

    /// <summary>敌机登记（幂等；set 单次查找，索引表与数组同步维护）。</summary>
    public void RegisterEnemy(Node node)
    {
        if (!_enemySet.ContainsKey(node))
        {
            _enemyIndex[node] = Enemies.Count;
            Enemies.Add(node);
            _enemySet[node] = true;
        }
    }

    /// <summary>敌机注销（幂等——set 判定真实在册才移除，Deactivate/_ExitTree 双调用自然去重；
    /// swap-remove + 索引表 O(1)，见 _enemyIndex 注释）。</summary>
    public void UnregisterEnemy(Node node)
    {
        if (_enemySet.Remove(node))
        {
            // 双表一致性守卫：set 在册而索引缺键/越界 = 表分歧——直接取索引会静默得 0，
            // swap-remove 将错删 0 号元素。守卫命中走数组权威重建自愈（Repair*Tables）
            if (!_enemyIndex.ContainsKey(node))
            {
                RepairEnemyTables(node);
                return;
            }

            var idx = (int)_enemyIndex[node].AsInt64();
            _enemyIndex.Remove(node);
            if (idx < 0 || idx >= Enemies.Count)
            {
                RepairEnemyTables(node);
                return;
            }

            var last = Enemies[Enemies.Count - 1];
            if (!ReferenceEquals(last, node))
            {
                Enemies[idx] = last;
                _enemyIndex[last] = idx;
            }

            Enemies.RemoveAt(Enemies.Count - 1);
        }
    }

    /// <summary>敌机表分歧自愈：以数组为权威重建 set + 索引表，并从数组移除目标条目。
    /// 分歧属异常路径（正常时三表同步维护），线性操作不在乎 O(n)；PushError 留取证。</summary>
    private void RepairEnemyTables(Node node)
    {
        GD.PushError($"[EntityManager] 敌机注册表分歧（set 在册而索引缺失/越界）：{node.Name}，已按数组权威重建");
        for (var i = Enemies.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Enemies[i], node))
            {
                Enemies.RemoveAt(i);
            }
        }

        _enemyIndex.Clear();
        _enemySet.Clear();
        for (var i = 0; i < Enemies.Count; i++)
        {
            _enemyIndex[Enemies[i]] = i;
            _enemySet[Enemies[i]] = true;
        }
    }

    /// <summary>G010：注册表存在性判定 O(1)（语义同注册表包含，deactivate 即移除）。</summary>
    public bool HasEnemy(Node node) => _enemySet.ContainsKey(node);

    /// <summary>P0-1：敌弹登记（幂等；set/索引表与数组同步维护）。</summary>
    public void RegisterEnemyBullet(GodotObject b)
    {
        if (!_enemyBulletSet.ContainsKey(b))
        {
            _enemyBulletIndex[b] = EnemyBullets.Count;
            EnemyBullets.Add(b);
        }

        _enemyBulletSet[b] = true;
    }

    /// <summary>P0-1：敌弹注销（幂等；set 判定真实在册才移除）。
    /// 2026-08-09 审计：原 Array.Remove 为 O(n) 线性扫描+搬移——敌弹消亡频率 = 弹幕生成频率，
    /// 同屏数百时每帧多次 O(n)；改 swap-remove + 索引表 O(1)。消费方（ClearNearbyEnemyBullets 倒序 /
    /// DeathReplay 只读采样）不依赖数组顺序，已核实。</summary>
    public void UnregisterEnemyBullet(GodotObject b)
    {
        if (_enemyBulletSet.Remove(b))
        {
            // 双表一致性守卫（UnregisterEnemy 同款）：缺键直接取索引会静默得 0，错删 0 号元素
            if (!_enemyBulletIndex.ContainsKey(b))
            {
                RepairEnemyBulletTables(b);
                return;
            }

            var idx = (int)_enemyBulletIndex[b].AsInt64();
            _enemyBulletIndex.Remove(b);
            if (idx < 0 || idx >= EnemyBullets.Count)
            {
                RepairEnemyBulletTables(b);
                return;
            }

            var last = EnemyBullets[EnemyBullets.Count - 1];
            if (!ReferenceEquals(last, b))
            {
                EnemyBullets[idx] = last;
                _enemyBulletIndex[last] = idx;
            }

            EnemyBullets.RemoveAt(EnemyBullets.Count - 1);
        }
    }

    /// <summary>敌弹表分歧自愈（RepairEnemyTables 同款，数组权威重建）。</summary>
    private void RepairEnemyBulletTables(GodotObject b)
    {
        GD.PushError("[EntityManager] 敌弹注册表分歧（set 在册而索引缺失/越界），已按数组权威重建");
        for (var i = EnemyBullets.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(EnemyBullets[i], b))
            {
                EnemyBullets.RemoveAt(i);
            }
        }

        _enemyBulletIndex.Clear();
        _enemyBulletSet.Clear();
        for (var i = 0; i < EnemyBullets.Count; i++)
        {
            _enemyBulletIndex[EnemyBullets[i]] = i;
            _enemyBulletSet[EnemyBullets[i]] = true;
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
