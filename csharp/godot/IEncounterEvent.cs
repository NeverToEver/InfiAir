namespace InfiAir;

/// <summary>
/// 遭遇事件契约（U14，2026-08-09 审计）：GameEventManager 遭遇轮询 typed 化接口——
/// 消除每帧 HasMethod/Call 动态派发（原 GDScript 鸭子契约）；新增遭遇事件类型实现本接口
/// 即可经 RegisterEncounter 注册（GameEventManager 内部以 is IEncounterEvent 分派）。
/// </summary>
public interface IEncounterEvent
{
    /// <summary>事件状态机是否活跃（FSM 非 IDLE）。</summary>
    bool IsActive();

    /// <summary>触发资格（自身冷却/分数/母舰在场等；Boss 与互斥检查由管理器侧负责）。</summary>
    bool CanTrigger();

    /// <summary>启动事件（管理器在互斥检查通过后调用）。</summary>
    void Start();

    /// <summary>立即打断事件（EndActive 调用；FSM 异步回 IDLE 由管理器 pending 补发 event_ended）。</summary>
    void Abort();
}
