using Godot;

namespace InfiAir;

/// <summary>
/// 迷雾事件系统测试用事件（M4 迁移，2026-08-08）：极简事件——只实现 event_id
/// （无任何钩子），验证最简形态走通 start→duration→end 全生命周期。
/// 原 GDScript _MinimalTestEvent（test/fog_event_test.gd 内嵌类 extends FogEvent）违反
/// 跨语言继承禁令，随 M4 以 C# 测试事件类重写；fog_event_test 经 load().new() 实例化。
/// M7 测试迁 C# 后删除。
/// </summary>
public partial class MinimalTestEvent : FogEvent
{
    public override StringName EventId() => new StringName("_minimal_test");
}
