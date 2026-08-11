using Godot;

namespace InfiAir;

/// <summary>每物理帧一次的热路径共享缓存（空间换时间）：全场上百实体/子弹共享一次
/// view_world_rect 查询，替代 10 处同构样板（Enemy/Boss/EnrageSequence/BossAttacks/
/// TurretBattery/AimFrameLayer/Bullet/FormationStrikeEvent/StrikeCarrier/FormationBomb）
/// 各自每帧一次 Engine.GetPhysicsFrames + GameState.Instance.ViewWorldRect 调用。
/// U07 教训保留：静态缓存绝不持有 Godot 对象引用（悬空访问 + 退出 finalize 触碰），
/// 故只缓存纯值类型 Rect2；player 取用直读 GameState.Instance.PlayerRef（EntityManager
/// O(1) typed 属性，不缓存不装箱——原 Variant 包拆纯负优化）。</summary>
public static class FrameCache
{
    private static ulong _frame = ulong.MaxValue;
    private static Rect2 _view;

    public static Rect2 ViewRect()
    {
        var f = Engine.GetPhysicsFrames();
        if (f != _frame)
        {
            _frame = f;
            _view = GameState.Instance.ViewWorldRect();
        }

        return _view;
    }

    public static Node2D? Player() => GameState.Instance.PlayerRef;

    private static ulong _ddaFrame = ulong.MaxValue;
    private static float _ddaFactor = 1.0f;

    /// <summary>DDA 因子每物理帧一次共享缓存（原 Enemy 每敌机每帧 d / DdaFactor() → 每帧 1 次；
    /// 缓存因子而非逆因子——除法语义逐位等价，零行为风险；受击即时性不变：帧缓存每物理帧刷新）。</summary>
    public static float DdaFactor()
    {
        var f = Engine.GetPhysicsFrames();
        if (f != _ddaFrame)
        {
            _ddaFrame = f;
            _ddaFactor = (float)GameState.Instance.DdaFactor();
        }

        return _ddaFactor;
    }
}
