namespace InfiAir.Core.Machines;

/// <summary>机型特性演出（轮盘上选中某一型时播的那一段：把「这型强在哪」演出来，而不是只写一行字）。</summary>
public enum MachineShowcaseKind
{
    /// <summary>标准型：校准环（没有特性本身也是一种读数）。</summary>
    Baseline,

    /// <summary>移动速度：横移冲刺 + 拖影。</summary>
    Speed,

    /// <summary>攻击力：重炮出膛 + 后坐。</summary>
    Impact,

    /// <summary>攻击速度：三连点射。</summary>
    Burst,

    /// <summary>防御：护盾环张开 + 来袭弹被弹开。</summary>
    Shield,

    /// <summary>血量上限：装甲环带逐段点亮。</summary>
    Armor,
}

/// <summary>
/// 机型 → 特性演出的映射（纯逻辑，零 Godot 依赖）：**一型一段专属演出**，引擎层只按
/// <see cref="KindOf"/> 分发到对应的播放函数，不在这层另写一份「哪型演什么」。
///
/// 为什么按 **id** 而不是按加成轴映射：演出演的是「这一架飞机的性格」，不是「这条乘区」——
/// 两个机型将来共用一条轴时（例如再加一型也是攻击力），按轴映射会让它们演同一段，
/// 玩家读到的就成了「又是这一套」。按 id 映射时漏配的那一型会落回 <see cref="MachineShowcaseKind.Baseline"/>
/// 与标准型撞车，<c>MachineShowcaseTests</c> 的「六型演出两两不同」会当场变红。
/// </summary>
public static class MachineShowcase
{
    /// <summary>该机型的演出（未知 / 空 id 与标准型同为基准演出）。</summary>
    public static MachineShowcaseKind KindOf(string? machineId) => MachineRoster.ById(machineId).Id switch
    {
        "peregrine" => MachineShowcaseKind.Speed,
        "sledge" => MachineShowcaseKind.Impact,
        "repeater" => MachineShowcaseKind.Burst,
        "bulwark" => MachineShowcaseKind.Shield,
        "colossus" => MachineShowcaseKind.Armor,
        _ => MachineShowcaseKind.Baseline,
    };
}
