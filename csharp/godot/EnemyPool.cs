using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 敌机对象池（挂在 Main 下，模式同 BulletPool）：复用 enemy.tscn 实例。活跃敌机挂 Main 下
/// （清场遍历可见），闲置收回池节点下。
/// </summary>
public partial class EnemyPool : Node
{
    // U07：静态 Godot 资源改实例字段（退出 segfault 实测教训，UITheme.cs:53）
    private readonly PackedScene _enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");

    private readonly Godot.Collections.Array<Enemy> _free = new();

    /// <summary>P1-6-1：待回收/待激活回挂队列——Release/Spawn 入队，_Process（idle 帧，物理回调外）
    /// 批量执行，替代原每敌 CallDeferred（ReparentDeferred/ReparentToActive）逐条消息派发。</summary>
    private readonly List<Enemy> _pendingPark = new();
    private readonly List<Enemy> _pendingActivate = new();
    public override void _Ready()
    {
        GameState.Instance.EnemyPool = this;
    }

    /// <summary>C21 修复：场景卸载时清空全局注册，避免 GameState.enemy_pool 悬空。</summary>
    public override void _ExitTree()
    {
        if (GameState.Instance.EnemyPool == this)
        {
            GameState.Instance.EnemyPool = null; // Nil → 置 null
        }
    }

    /// <summary>spawn：取池实例或新建，激活并放置（p_bullet_type 空串 = 从弹种池随机）。</summary>
    public Enemy Spawn(
        Godot.Collections.Dictionary config, StringName strategy, float pDifficulty, Vector2 pos, StringName pBulletType)
    {
        Enemy? e = null;
        while (_free.Count > 0)
        {
            e = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);
            if (GodotObject.IsInstanceValid(e))
            {
                break;
            }

            e = null;
        }

        if (e == null)
        {
            e = _enemyScene.Instantiate<Enemy>();
            e.SetPool(this);
            GetParent()!.AddChild(e);
        }
        else if (e.GetParent() != GetParent())
        {
            // R12：spawn 侧 reparent 在物理回调（碰撞信号）内触发 area_set_shape_disabled flush 报错，
            // 与 Release 侧停放对称入队，_Process 帧末批量回挂（P1-6-1，替代原 CallDeferred 逐条派发）
            _pendingActivate.Add(e);
        }

        e.Position = pos;
        e.Reactivate(config, strategy, pDifficulty, pBulletType);
        return e;
    }

    public Enemy Spawn(Godot.Collections.Dictionary config, StringName strategy, float pDifficulty, Vector2 pos)
    {
        return Spawn(config, strategy, pDifficulty, pos, Enemy.NoBulletType);
    }

    /// <summary>回收：重置状态并入队待停放（不销毁）。monitoring 关闭与 reparent 由 _Process 帧末
    /// 批量执行（物理回调内不改场景树）；若敌机在批量执行前已被重激活（同帧复用）则跳过。幂等防重复回收。
    /// H2（2026-08-10 审计）：幂等守卫改 IsActive O(1)——Deactivate 必置 false（本方法是
    /// Deactivate 唯一调用方），与 _free.Contains 线性扫描等价且免 O(n)。</summary>
    public void Release(Enemy e)
    {
        if (!GodotObject.IsInstanceValid(e) || !e.IsActive())
        {
            return;
        }

        // USE_POOL 恒 true（性能 A/B 对照开关已收敛；纯 instantiate/free 分支已移除）
        e.Deactivate();
        _free.Add(e);
        _pendingPark.Add(e);
    }

    /// <summary>P1-6-1：帧末批量停放/激活回挂（idle _Process 处于物理回调外，与原 CallDeferred
    /// 同帧末语义）。两队列以 IsActive 双向仲裁（同原 ReparentDeferred/ReparentToActive 互斥）：
    /// 先停放（回收优先，激活队列里已失效的条目随后被仲裁跳过），再回挂激活。</summary>
    public override void _Process(double delta)
    {
        if (_pendingPark.Count > 0)
        {
            for (var i = 0; i < _pendingPark.Count; i++)
            {
                var e = _pendingPark[i];
                if (!GodotObject.IsInstanceValid(e) || e.IsQueuedForDeletion() || e.IsActive())
                {
                    continue;
                }

                e.Monitoring = false;
                if (e.GetParent() != this)
                {
                    // 4.6 实测 reparent 会触发 e._exit_tree，置位防 forget 把敌机误清出 _free
                    e.SetRepooling(true);
                    e.Reparent(this);
                    e.SetRepooling(false);
                }
            }

            _pendingPark.Clear();
        }

        if (_pendingActivate.Count > 0)
        {
            for (var i = 0; i < _pendingActivate.Count; i++)
            {
                var e = _pendingActivate[i];
                if (!GodotObject.IsInstanceValid(e) || e.IsQueuedForDeletion() || !e.IsActive()
                    || e.GetParent() == GetParent())
                {
                    continue;
                }

                // R04：reparent 触发 e._exit_tree，置位防 unbind_enemy 误发信号
                e.SetRepooling(true);
                e.Reparent(GetParent());
                e.SetRepooling(false);
                // R12：reparent 的 _exit_tree（repooling 路径）会 UnregisterEnemy，而 Reactivate 注册在先——
                // 回挂后补注册（幂等），与「先 reparent 后 Reactivate 注册」语义对齐。
                GameState.Instance.RegisterEnemy(e);
            }

            _pendingActivate.Clear();
        }
    }

    /// <summary>被外部 queue_free（清场/场景重载等池外销毁路径）时从池清单移除。</summary>
    public void Forget(Enemy e) => _free.Remove(e);
}
