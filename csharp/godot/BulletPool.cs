using Godot;

namespace InfiAir;

/// <summary>
/// 子弹对象池（M3a 全量迁移，2026-08-08 自 scripts/bullet_pool.gd 迁移；挂在 Main 下）：
/// 复用 bullet.tscn 实例，避免高频 instantiate/free。活跃弹挂 Main 下（清场/测试遍历可见），
/// 闲置弹收回池节点下。P2-3 同屏敌弹显式硬上限（500）保持。
/// </summary>
public partial class BulletPool : Node
{
    private static readonly PackedScene BulletScene = GD.Load<PackedScene>("res://scenes/bullet.tscn");

    /// <summary>P2-3：同屏敌弹显式硬上限（仅限制敌弹，玩家火力不受限）。</summary>
    public const int MaxEnemyActive = 500;

    private readonly Godot.Collections.Array<Bullet> _free = new();

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

    /// <summary>闲置实例数（A7 遗留清理：测试/诊断公开查询）。</summary>
    public int FreeCount() => _free.Count;

    /// <summary>M3a 过渡：活跃子弹总数（meta_health_fx D3 代理经本实例访问——
    /// GDScript 不能以类名引用 C# 静态成员，仅实例可达；M7 后改 typed 直调）。</summary>
    public int ActiveBulletCount => Bullet.ActiveCount;

    /// <summary>M3a 过渡：活跃爆炸实例数（同上）。</summary>
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
    /// 回收：重置状态并移回池节点下（不销毁）。reparent 延迟到空闲时执行（物理回调内不
    /// 改场景树）；若子弹在延迟执行前已被重激活（同帧复用）则跳过。幂等。
    /// </summary>
    public void Release(Bullet b)
    {
        if (!GodotObject.IsInstanceValid(b) || _free.Contains(b))
        {
            return;
        }

        b.Deactivate();
        _free.Add(b);
        CallDeferred(MethodName.ReparentDeferred, b);
    }

    /// <summary>延迟 reparent（物理回调内不直接改场景树）。public：CallDeferred 需引擎注册。</summary>
    public void ReparentDeferred(Bullet b)
    {
        if (GodotObject.IsInstanceValid(b) && !b.IsActive())
        {
            // 4.6 实测 reparent 会触发 b._exit_tree，置位防 forget 把子弹误清出 _free
            b.SetRepooling(true);
            b.Reparent(this);
            b.SetRepooling(false);
        }
    }

    /// <summary>子弹被外部 queue_free（清场/测试）时从池清单移除，防止悬空引用。</summary>
    public void Forget(Bullet b) => _free.Remove(b);
}
