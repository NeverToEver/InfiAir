using Godot;

namespace InfiAir;

/// <summary>
/// 屏幕震动：监听
/// GameState.ScreenShake 信号，随机偏移 + 指数衰减。process_mode 需为 Always
/// （场景文件中设置），保证暂停时震动也能衰减结束。
/// 信号声明 double、监听 float 类型不一致
/// （PlayerDamaged 已统一为 float，此信号低频无精度压力）——监听侧适配 double。
/// </summary>
public partial class CameraShake : Camera2D
{
    private readonly Callable _onShake;
    private float _decay = 6.0f;
    private float _strength;
    private bool _offsetActive; // 偏移非零标记：静止写门，仅在震动→静止过渡帧归零 Offset

    public CameraShake()
    {
        _onShake = Callable.From<double>(OnScreenShake);
    }

    public override void _Ready()
    {
        // C22：is_connected 守卫，相机重入树（场景重载/重挂）不重复连接
        var gs = GameState.Instance;
        if (gs != null && !gs.IsConnected(GameState.SignalName.ScreenShake, _onShake))
        {
            gs.Connect(GameState.SignalName.ScreenShake, _onShake);
        }

        _decay = Mathf.Max((float)GameState.Instance.Cfg("effects.shake.decay", _decay).AsDouble(), 0.001f); // H15：decay=0 震动永不衰减
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs != null && gs.IsConnected(GameState.SignalName.ScreenShake, _onShake))
        {
            gs.Disconnect(GameState.SignalName.ScreenShake, _onShake);
        }
    }

    public override void _Process(double delta)
    {
        if (_strength > 0.1f)
        {
            _offsetActive = true;
            Offset = new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0)) * _strength;
            _strength = Mathf.Lerp(_strength, 0.0f, _decay * (float)delta);
        }
        else
        {
            _strength = 0.0f;
            if (_offsetActive) // 静止写门：仅过渡帧归零一次（原每空帧重复写 Vector2.Zero）
            {
                _offsetActive = false;
                Offset = Vector2.Zero;
            }
        }
    }

    private void OnScreenShake(double strength)
    {
        _strength = Mathf.Max(_strength, (float)strength);
    }
}
