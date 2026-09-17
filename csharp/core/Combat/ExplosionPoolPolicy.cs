namespace InfiAir.Core.Combat;

/// <summary>爆炸池「入池资格」判定（纯逻辑，零 Godot 依赖）。
/// 判据＝本实例入池后的保留总量（空闲 + 活跃 + 本实例）不超过上限：只有池与场上合计还没满，
/// 新建实例才留作复用，否则按临时实例处理（播完销毁）。
/// 只看空闲队列不够：池被取空时（并发高峰）空闲恒为 0，「空闲 &lt; 上限」恒真——上限永不触发，
/// 实例按历史峰值并发长期保留（每实例含 4 张贴图 + 3 个发射器 + 2 个环 + 8 片碎片）。
/// 活跃数取**全部在播实例**而非「在播且可复用的实例」：前者是后者之上界，判据偏保守（宁可多销毁），
/// 不会因低估总量而越限。
/// cap ≤ 0 表示不允许复用（全部临时实例，播完销毁）。</summary>
public static class ExplosionPoolPolicy
{
    /// <summary>本实例是否留作复用：空闲 + 活跃 + 本实例 ≤ cap。</summary>
    public static bool ShouldPool(int idleCount, int liveCount, int cap) => idleCount + liveCount + 1 <= cap;
}
