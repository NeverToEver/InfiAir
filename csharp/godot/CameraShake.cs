using Godot;

namespace InfiAir;

/// <summary>
/// 屏幕震动：每帧从 GameFeelService 读 trauma，按 trauma^2 映射位移 + 微旋转（钳 ≤0.4°）。
/// 震源经 GameState.Shake 只累加 trauma 值（不再直接给振幅），本类只负责采样与噪声——
/// 幂次映射让小额冲击近乎无感、大额才猛烈，高频抖动不再叠加成持续晃动。
/// process_mode 需为 Always（场景文件中设置），保证暂停时震动也能衰减结束。
/// 衰减与时间缩放合成在 GameFeelService.Tick（按真实帧长推进，不受顿帧与子弹时间拖慢）。
/// </summary>
public partial class CameraShake : Camera2D
{
    private double _maxOffset = 22.0; // 位移上限（effects.shake.max_offset；trauma^2 后乘此值）
    private float _rotMaxDeg; // 微旋转上限（effects.shake.rotation_deg，钳 ≤0.4°）
    private bool _offsetActive; // 偏移非零标记：静止写门，仅在震动→静止过渡帧归零 Offset/Rotation

    public override void _Ready()
    {
        _maxOffset = Mathf.Max((float)GameState.Instance.Cfg("effects.shake.max_offset", 22.0).AsDouble(), 0.0f);
        _rotMaxDeg = Mathf.Clamp((float)GameState.Instance.Cfg("effects.shake.rotation_deg", 0.4).AsDouble(), 0.0f, 0.4f);
    }

    public override void _Process(double delta)
    {
        var magnitude = (float)GameState.Instance.ShakeMagnitude();
        if (magnitude > 1e-4)
        {
            _offsetActive = true;
            Offset = new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0))
                * (float)_maxOffset * (float)magnitude;
            // 微旋转与位移同量纲：满创伤给 rotMaxDeg，半创伤因平方只剩四分之一
            Rotation = Mathf.DegToRad((float)GD.RandRange(-1.0, 1.0) * magnitude * _rotMaxDeg);
        }
        else
        {
            if (_offsetActive) // 静止写门：仅过渡帧归零一次（否则每空帧重复写 Vector2.Zero）
            {
                _offsetActive = false;
                Offset = Vector2.Zero;
                Rotation = 0.0f;
            }
        }
    }
}
