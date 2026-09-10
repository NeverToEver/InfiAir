using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 子弹对象池（挂在 Main 下）：
/// 复用 bullet.tscn 实例，避免高频 instantiate/free。活跃弹挂 Main 下（清场遍历可见），
/// 闲置弹收回池节点下。P2-3 同屏敌弹显式硬上限（500）保持。
/// </summary>
public partial class BulletPool : Node
{
    // V 系列：静态 PackedScene 持有 → 实例字段（禁静态持 Godot RefCounted）
    private readonly PackedScene BulletScene = GD.Load<PackedScene>("res://scenes/bullet.tscn");

    /// <summary>P2-3：同屏敌弹显式硬上限（仅限制敌弹，玩家火力不受限）。</summary>
    public const int MaxEnemyActive = 500;

    private readonly Godot.Collections.Array<Bullet> _free = new();

    /// <summary>P1-6-1：待停放队列——Release 入队，_Process（idle 帧，物理回调外）批量
    /// 关 monitoring + 回挂池节点，替代原每弹 2 条 CallDeferred（Deactivate 一条 + 本类
    /// ReparentDeferred 一条）的原生消息队列 Variant 编组开销。</summary>
    private readonly List<Bullet> _pendingPark = new();

    public override void _Ready()
    {
        GameState.Instance.BulletPool = this;
    }

    /// <summary>C21 修复：场景卸载时清空全局注册，避免 GameState.bullet_pool 悬空。</summary>
    public override void _ExitTree()
    {
        if (GameState.Instance.BulletPool == this)
        {
            GameState.Instance.BulletPool = null; // Nil → 置 null
        }
    }

    /// <summary>活跃子弹总数（MetaHealthFX D3 亮度代理经本实例读取；转发 Bullet.ActiveCount）。</summary>
    public int ActiveBulletCount => Bullet.ActiveCount;

    /// <summary>活跃爆炸实例数（同上；转发 Explosion.LiveCount()）。</summary>
    public int LiveExplosionCount => Explosion.LiveCount();

    /// <summary>取一枚子弹并激活（4 参便捷重载）。敌弹超硬上限时返回 null（调用方判空跳过）。</summary>
    public Bullet? Fire(Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer)
    {
        return Fire(pDirection, pSpeed, pDamage, pIsPlayer, false, 0.0f);
    }

    public Bullet? Fire(
        Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer, bool pHoming, float pHomingTime)
    {
        // P2-3：同屏敌弹显式硬上限（玩家弹永不限制）
        if (!pIsPlayer && Bullet.ActiveCount >= MaxEnemyActive)
        {
            return null;
        }

        Bullet? b = null;
        while (_free.Count > 0)
        {
            b = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);
            if (GodotObject.IsInstanceValid(b))
            {
                break;
            }

            b = null;
        }

        if (b == null)
        {
            b = BulletScene.Instantiate<Bullet>();
            b.SetPool(this);
            GetParent()!.AddChild(b); // 活跃弹挂 Main 下
        }
        else if (b.GetParent() != GetParent())
        {
            // 闲置弹从池节点挂回 Main
            b.Reparent(GetParent());
        }

        b.Activate(pDirection, pSpeed, pDamage, pIsPlayer, pHoming, pHomingTime);
        return b;
    }

    /// <summary>
    /// 回收：重置状态并入队待停放（不销毁）。monitoring 关闭与 reparent 由 _Process 帧末批量执行
    /// （物理回调内不改场景树）；若子弹在批量执行前已被重激活（同帧复用）则跳过。幂等。
    /// H2（2026-08-10 审计）：幂等守卫改 IsActive O(1)——Deactivate 必置 false（本方法是
    /// Deactivate 唯一调用方），与 _free.Contains 线性扫描等价且免 O(n)。
    /// </summary>
    public void Release(Bullet b)
    {
        if (!GodotObject.IsInstanceValid(b) || !b.IsActive())
        {
            return;
        }

        b.Deactivate();
        _free.Add(b);
        _pendingPark.Add(b);
    }

    /// <summary>P1-6-1：帧末批量停放（idle _Process 处于物理回调外，与原 CallDeferred 同帧末语义）。
    /// IsActive 仲裁同原 ReparentDeferred/DeferredDisableMonitoring——同帧重激活的弹跳过；
    /// 待帧末删除的弹跳过（原 deferred 路径由引擎静默丢弃）。</summary>
    public override void _Process(double delta)
    {
        if (_pendingPark.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _pendingPark.Count; i++)
        {
            var b = _pendingPark[i];
            if (!GodotObject.IsInstanceValid(b) || b.IsQueuedForDeletion() || b.IsActive())
            {
                continue;
            }

            b.Monitoring = false;
            if (b.GetParent() != this)
            {
                // 4.6 实测 reparent 会触发 b._exit_tree，置位防 forget 把子弹误清出 _free
                b.SetRepooling(true);
                b.Reparent(this);
                b.SetRepooling(false);
            }
        }

        _pendingPark.Clear();
    }

    /// <summary>子弹被外部 queue_free（清场等池外销毁路径）时从池清单移除，防止悬空引用。</summary>
    public void Forget(Bullet b) => _free.Remove(b);
}
