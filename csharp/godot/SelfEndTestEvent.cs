using Godot;

namespace InfiAir;

/// <summary>
/// 迷雾事件系统测试用事件（M4 迁移，2026-08-08）：复杂事件——内部目标达成后主动
/// request_end 提前结束（2 个 tick）。原 GDScript _SelfEndTestEvent（test/fog_event_test.gd
/// 内嵌类 extends FogEvent）违反跨语言继承禁令（C#↔GDScript 不可互相继承），随 M4 以
/// C# 测试事件类重写；fog_event_test 经 load().new() 实例化。M7 测试迁 C# 后删除。
/// </summary>
public partial class SelfEndTestEvent : FogEvent
{
    private int _ticks;

    public override StringName EventId() => new StringName("_self_end_test");

    protected override void OnTick(float delta)
    {
        _ticks += 1;
        if (_ticks >= 2)
        {
            RequestEnd();
        }
    }
}
