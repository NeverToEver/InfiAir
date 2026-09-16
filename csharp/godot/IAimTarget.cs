using Godot;

namespace InfiAir;

/// <summary>
/// 辅助瞄准可瞄准目标契约：辅助框、框内强追踪、弱追踪锥与磁吸的扫描只认本契约，
/// 不按具体类型判型。
///
/// 存在理由：这些扫描原按 <c>is Enemy</c> 判型，而遭遇单位（<see cref="TurretBattery"/>、
/// <see cref="FormationCraft"/>）不是 Enemy 子类——遭遇期间屏上唯一可打的目标整体不吃辅助瞄准，
/// 与 DESIGN_BASELINE §1.5「弱追踪覆盖全部敌机」冲突。Boss 刻意不实现本契约（保持既有例外）。
///
/// 性能约束：扫描是逐帧热路径（每渲染帧 × 全场实体），实现方必须是引用类型（接口引用不装箱）、
/// 只读属性不做分配；框包含/锥角/衰减的算式在 core <c>AimTargeting</c> 与调用侧，本契约只给数据。
/// </summary>
public interface IAimTarget
{
    /// <summary>是否可作为瞄准目标（存活、在册、未处于不可击毁的升起/收回态）。</summary>
    bool AimTargetable { get; }

    /// <summary>是否标记目标：决定辅助框显示与框内强追踪。遭遇单位在被处理为可打期间恒为标记目标
    /// （遭遇期间它们是屏上唯一可打目标），标记与计数登记同源，见 <see cref="AimTargetCount"/>。</summary>
    bool AimMarked { get; }

    /// <summary>世界坐标（框心与锥角/距离判定的基准）。</summary>
    Vector2 AimWorldPosition { get; }

    /// <summary>碰撞半径（已含 world_scale）；框半宽 = 本值 + 档位 frame_pad（pad 单源在 AimFrameLayer）。</summary>
    float AimCollisionRadius { get; }
}

/// <summary>
/// 可瞄准目标的在屏标记计数（零标记是常态：普通敌机按 <c>mark_ratio</c> 抽取，遭遇期外无遭遇单位）。
/// AimFrameLayer 据此跳过整表扫描与重绘。
///
/// 与 <see cref="Enemy.AimMarkedCount"/> 分开计数：普通敌机的计数由 AimMarked setter 成对维护、
/// 语义不变；遭遇单位由各自的生命周期登记（<see cref="SetEncounterMarked"/>）——两类目标的
/// 生命周期机制不同，混用一个计数器就要在 Enemy 侧开写口。
/// </summary>
public static class AimTargetCount
{
    private static int _encounterMarked;

    /// <summary>在屏标记目标总数（普通敌机 + 遭遇单位）。</summary>
    public static int Marked => Enemy.AimMarkedCount + _encounterMarked;

    /// <summary>遭遇单位标记登记（幂等；登记态即 <c>AimMarked</c>，见调用方字段）。</summary>
    public static void SetEncounterMarked(bool marked)
    {
        if (marked)
        {
            _encounterMarked += 1;
        }
        else if (_encounterMarked > 0)
        {
            _encounterMarked -= 1;
        }
    }
}
