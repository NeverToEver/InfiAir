using Godot;

namespace InfiAir;

/// <summary>
/// 短间隔随机方向事件：周期发射强制方向脉冲（经 FogEvent.EmitDirectionShift →
/// manager fog_direction_shift 信号），玩家 hold 秒内移动向量被替换为随机单位方向。
/// tick 驱动（无自持 Timer——duration 计时由编排器统一负责，事件结束自动停止）。
/// 配置：balance.json fog_events.direction_shift（shift_interval / hold_time）。
/// </summary>
public partial class DirectionShiftEvent : FogEvent
{
    private float _interval = 0.7f;
    private float _hold = 0.3f;
    private float _acc;

    public override StringName EventId() => new StringName("direction_shift");

    protected override void OnStart()
    {
        // H15 族：≤0 每帧脉冲
        _interval = Mathf.Max(
            (float)GameStateBridge.Call("cfg", "fog_events.direction_shift.shift_interval", _interval).AsDouble(), 0.05f);
        _hold = Mathf.Max((float)GameStateBridge.Call("cfg", "fog_events.direction_shift.hold_time", _hold).AsDouble(), 0.0f);
        _acc = 0.0f;
        EmitShift(); // 事件开始立即脉冲一次
    }

    protected override void OnTick(float delta)
    {
        _acc += delta;
        if (_acc >= _interval)
        {
            _acc = 0.0f;
            EmitShift();
        }
    }

    private void EmitShift()
    {
        var dir = new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0));
        if (dir.LengthSquared() < 1.0e-6f)
        {
            dir = Vector2.Down;
        }

        EmitDirectionShift(dir.Normalized(), _hold);
    }
}
