using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 统一实体管理器：本局实体注册表 + 生命周期信号。
/// 语义：enemies 注册表 + O(1) has_enemy 热路径索引；敌弹注册表（death_replay 数据源）；
/// 特殊引用（player_ref/player_hitbox/bullet_pool/enemy_pool/aim_frame_layer/camera_ref）；
/// 统一绑定样板 bind_enemy/unbind_enemy。
/// 消费方直接迭代本类 Enemies 集合。
/// 两张表及其索引一律用托管集合而非 Godot 容器：注册表被全场逐帧迭代（辅助瞄准每帧两趟、
/// 溅射/激光逐命中、死亡回放逐物理帧采样），Godot 容器的逐元素取值都要走
/// Variant → 引擎对象封送（每次索引一次原生调用外加托管代理查表），托管集合是纯指针读取。
/// </summary>
public partial class EntityManager : RefCounted
{
    /// <summary>实体注册信号（新功能订阅口，GameState 收口转发）。</summary>
    [Signal]
    public delegate void EntityRegisteredEventHandler(Node node);

    [Signal]
    public delegate void EntityUnregisteredEventHandler(Node node);

    /// <summary>enemies 注册表（GameState.enemies 转发）。在册者一律 Node2D 派生
    /// （Enemy/Boss/FormationCraft/FormationBomb/TurretBattery 同为 Area2D 家族）。</summary>
    public List<Node2D> Enemies { get; } = new();

    /// <summary>enemies 的 O(1) 存在性索引（追踪弹每帧 has 判定）。</summary>
    private readonly HashSet<Node2D> _enemySet = new();

    /// <summary>enemies 在册索引表（node → 集合下标），swap-remove 双维护
    /// （_enemyBulletIndex 同款模式）——线性 Remove 的搬移成本随在册数增长，
    /// 敌机死亡即触发（池化回收路径每次死亡调两次）。消费方不依赖集合顺序：
    /// 迭代类（AimFrameLayer 严格最近选取/Bullet 溅射倒序/Mothership/EnrageSequence/Main 清场/
    /// DeathReplay 只读敌弹表）与 Contains/Count 判定均与顺序无关。</summary>
    private readonly Dictionary<Node2D, int> _enemyIndex = new();

    /// <summary>敌弹注册表（death_replay 录制数据源）。</summary>
    public List<Bullet> EnemyBullets { get; } = new();

    /// <summary>敌弹存在性索引。</summary>
    private readonly HashSet<Bullet> _enemyBulletSet = new();

    /// <summary>敌弹在册索引表（弹 → 集合下标），swap-remove 双维护。</summary>
    private readonly Dictionary<Bullet, int> _enemyBulletIndex = new();

    public Node2D? PlayerRef { get; set; }

    public Area2D? PlayerHitbox { get; set; }

    /// <summary>子弹对象池实例（BulletPool 自身 _Ready 登记）。</summary>
    public BulletPool? BulletPool { get; set; }

    /// <summary>敌机对象池实例（EnemyPool 自身 _Ready 登记）。</summary>
    public GodotObject? EnemyPool { get; set; }

    /// <summary>辅助瞄准框覆盖层实例（AimFrameLayer 自身 _Ready 登记）。</summary>
    public GodotObject? AimFrameLayer { get; set; }

    public Camera2D? CameraRef { get; set; }

    /// <summary>敌机登记（幂等；set 单次查找，索引表与集合同步维护）。</summary>
    public void RegisterEnemy(Node2D node)
    {
        if (_enemySet.Add(node))
        {
            _enemyIndex[node] = Enemies.Count;
            Enemies.Add(node);
        }
    }

    /// <summary>敌机注销（幂等——set 判定真实在册才移除，Deactivate/_ExitTree 双调用自然去重；
    /// swap-remove + 索引表 O(1)，见 _enemyIndex 注释）。</summary>
    public void UnregisterEnemy(Node2D node)
    {
        if (_enemySet.Remove(node))
        {
            // 双表一致性守卫：set 在册而索引缺键/越界/槽位不符 = 表分歧——直接取索引会静默得 0，
            // swap-remove 将错删 0 号元素。守卫命中走数组权威重建自愈（Repair*Tables）
            if (!_enemyIndex.Remove(node, out var idx))
            {
                RepairEnemyTables(node);
                return;
            }

            if (idx < 0 || idx >= Enemies.Count)
            {
                RepairEnemyTables(node);
                return;
            }

            // 第三类分歧：索引在界但槽位元素不符（index → 错误对象）——swap-remove 会错删末位
            if (!ReferenceEquals(Enemies[idx], node))
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
    private void RepairEnemyTables(Node2D node)
    {
        // 分歧路径节点原生侧可能已释放，直接取 .Name 会抛 ObjectDisposedException——降级输出防自愈路径反崩
        var label = GodotObject.IsInstanceValid(node) ? node.Name.ToString() : "<已释放节点>";
        GD.PushError($"[EntityManager] 敌机注册表分歧（set 在册而索引缺失/越界/槽位不符）：{label}，已按数组权威重建");
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
            _enemySet.Add(Enemies[i]);
        }
    }

    /// <summary>注册表存在性判定 O(1)（语义同注册表包含，deactivate 即移除）。</summary>
    public bool HasEnemy(Node2D node) => _enemySet.Contains(node);

    /// <summary>敌弹登记（幂等；set/索引表与集合同步维护）。</summary>
    public void RegisterEnemyBullet(Bullet b)
    {
        if (_enemyBulletSet.Add(b))
        {
            _enemyBulletIndex[b] = EnemyBullets.Count;
            EnemyBullets.Add(b);
        }
    }

    /// <summary>敌弹注销（幂等；set 判定真实在册才移除）。
    /// 线性 Remove 为 O(n) 扫描+搬移——敌弹消亡频率 = 弹幕生成频率，
    /// 同屏数百时每帧多次 O(n)；改 swap-remove + 索引表 O(1)。消费方（ClearNearbyEnemyBullets 倒序 /
    /// DeathReplay 只读采样）不依赖集合顺序。</summary>
    public void UnregisterEnemyBullet(Bullet b)
    {
        if (_enemyBulletSet.Remove(b))
        {
            // 双表一致性守卫（UnregisterEnemy 同款）：缺键直接取索引会静默得 0，错删 0 号元素
            if (!_enemyBulletIndex.Remove(b, out var idx))
            {
                RepairEnemyBulletTables(b);
                return;
            }

            if (idx < 0 || idx >= EnemyBullets.Count)
            {
                RepairEnemyBulletTables(b);
                return;
            }

            // 第三类分歧：索引在界但槽位元素不符（index → 错误对象）——swap-remove 会错删末位
            if (!ReferenceEquals(EnemyBullets[idx], b))
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
    private void RepairEnemyBulletTables(Bullet b)
    {
        GD.PushError("[EntityManager] 敌弹注册表分歧（set 在册而索引缺失/越界/槽位不符），已按数组权威重建");
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
            _enemyBulletSet.Add(EnemyBullets[i]);
        }
    }

    /// <summary>统一单位绑定：add_to_group("enemy") + register_enemy + entity_registered 信号（幂等）。</summary>
    public void BindEnemy(Node2D node)
    {
        node.AddToGroup("enemy");
        RegisterEnemy(node);
        EmitSignal(SignalName.EntityRegistered, node);
    }

    /// <summary>统一单位解绑：unregister + entity_unregistered 信号（组随节点释放自动退出）。</summary>
    public void UnbindEnemy(Node2D node)
    {
        UnregisterEnemy(node);
        EmitSignal(SignalName.EntityUnregistered, node);
    }
}
