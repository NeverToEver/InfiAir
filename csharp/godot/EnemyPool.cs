using Godot;

namespace InfiAir;

/// <summary>
/// 敌机对象池（M3b 全量迁移，2026-08-08 自 scripts/enemy_pool.gd 迁移；挂在 Main 下，
/// 模式同 BulletPool）：复用 enemy.tscn 实例。活跃敌机挂 Main 下（清场/测试遍历可见），
/// 闲置收回池节点下。
/// </summary>
public partial class EnemyPool : Node
{
    // U07：静态 Godot 资源改实例字段（退出 segfault 实测教训，UITheme.cs:53）
    private readonly PackedScene _enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");

    public const bool UsePool = true;

    private readonly Godot.Collections.Array<Enemy> _free = new();

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

    /// <summary>闲置实例数（A7 遗留清理：测试/诊断公开查询）。</summary>
    public int FreeCount() => _free.Count;

    /// <summary>spawn：取池实例或新建，激活并放置（p_bullet_type 空串 = 从弹种池随机）。</summary>
    public Enemy Spawn(
        Godot.Collections.Dictionary config, StringName strategy, float pDifficulty, Vector2 pos, StringName pBulletType)
    {
        Enemy? e = null;
        if (UsePool)
        {
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
            // 与 Release 侧 ReparentDeferred 对称延迟到空闲帧；SetRepooling 置位移入 ReparentToActive。
            CallDeferred(MethodName.ReparentToActive, e);
        }

        e.Position = pos;
        e.Reactivate(config, strategy, pDifficulty, pBulletType);
        return e;
    }

    public Enemy Spawn(Godot.Collections.Dictionary config, StringName strategy, float pDifficulty, Vector2 pos)
    {
        return Spawn(config, strategy, pDifficulty, pos, Enemy.NoBulletType);
    }

    /// <summary>回收：重置状态并移回池节点下（不销毁）。reparent 延迟到空闲时执行；
    /// 若敌机在延迟执行前已被重激活（同帧复用）则跳过。幂等防重复回收。
    /// H2（2026-08-10 审计）：幂等守卫改 IsActive O(1)——Deactivate 必置 false（本方法是
    /// Deactivate 唯一调用方），与 _free.Contains 线性扫描等价且免 O(n)。</summary>
    public void Release(Enemy e)
    {
        if (!GodotObject.IsInstanceValid(e) || !e.IsActive())
        {
            return;
        }

        // USE_POOL 恒 true（性能 A/B 对照开关；false 分支为纯 instantiate/free，已随迁移移除）
        e.Deactivate();
        _free.Add(e);
        CallDeferred(MethodName.ReparentDeferred, e);
    }

    /// <summary>延迟 reparent（物理回调内不直接改场景树）。public：CallDeferred 需引擎注册。</summary>
    public void ReparentDeferred(Enemy e)
    {
        if (GodotObject.IsInstanceValid(e) && !e.IsActive())
        {
            // 4.6 实测 reparent 会触发 e._exit_tree，置位防 forget 把敌机误清出 _free
            e.SetRepooling(true);
            e.Reparent(this);
            e.SetRepooling(false);
        }
    }

    /// <summary>延迟 reparent 到 Main（活跃池位）。与 ReparentDeferred 双向互斥（IsActive 仲裁）：
    /// 极端时序（deferred 执行前敌机已被回收）下保持闲置敌机在池节点下。</summary>
    public void ReparentToActive(Enemy e)
    {
        if (GodotObject.IsInstanceValid(e) && e.IsActive() && e.GetParent() != GetParent())
        {
            // R04：reparent 触发 e._exit_tree，置位防 unbind_enemy 误发信号
            e.SetRepooling(true);
            e.Reparent(GetParent());
            e.SetRepooling(false);
            // R12：reparent 的 _exit_tree（repooling 路径）会 UnregisterEnemy，而 Reactivate 注册在先——
            // 延迟 reparent 后补注册（幂等），与同步版「先 reparent 后 Reactivate 注册」语义对齐。
            GameState.Instance.RegisterEnemy(e);
        }
    }

    /// <summary>被外部 queue_free（清场/测试/场景重载）时从池清单移除。</summary>
    public void Forget(Enemy e) => _free.Remove(e);

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public Enemy spawn(Godot.Collections.Dictionary config, StringName strategy, float pDifficulty, Vector2 pos, StringName pBulletType)
        => Spawn(config, strategy, pDifficulty, pos, pBulletType);

    public Enemy spawn(Godot.Collections.Dictionary config, StringName strategy, float pDifficulty, Vector2 pos)
        => Spawn(config, strategy, pDifficulty, pos);

}
