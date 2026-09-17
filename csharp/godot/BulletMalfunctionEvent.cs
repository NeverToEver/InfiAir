using Godot;

namespace InfiAir;

/// <summary>
/// 子弹错误事件：效果全部在玩家侧（出膛弹角度偏移 / 慢速失误弹 / 射速扰动），
/// 经 manager 统一 fog_event_started/ended 信号在 Player 侧应用（参数读 balance
/// fog_events.bullet_malfunction.*）。
/// 本类无自持效果——作为注册表条目存在（触发资格/时长/冷却仍由 `GameEventManager` 统一判定）。
/// </summary>
public partial class BulletMalfunctionEvent : FogEvent
{
    public override StringName EventId() => new StringName("bullet_malfunction");
}
