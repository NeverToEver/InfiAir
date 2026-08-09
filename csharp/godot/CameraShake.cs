using Godot;

namespace InfiAir;

/// <summary>
/// 屏幕震动（M3a 全量迁移，2026-08-08 自 scripts/camera_shake.gd 迁移）：监听
/// GameState.screen_shake 信号，随机偏移 + 指数衰减。process_mode 需为 Always
/// （场景文件中设置），保证暂停时震动也能衰减结束。
/// 迁移期：GameState 为 GDScript autoload，signal 经 Connect("ScreenShake") 连接
/// （C22 is_connected 守卫保持）。V 系列：信号声明 double、监听 float 类型不一致
/// （U16 已统一 PlayerDamaged 为 float，此信号低频无精度压力）——监听侧适配 double。
/// </summary>
public partial class CameraShake : Camera2D
{
    private readonly Callable _onShake;
    private float _decay = 6.0f;
    private float _strength;

    public CameraShake()
    {
        _onShake = Callable.From<double>(OnScreenShake);
    }

    public override void _Ready()
    {
        // C22：is_connected 守卫，相机重入树（场景重载/重挂）不重复连接
        var gs = GameState.Instance;
        if (gs != null && !gs.IsConnected("ScreenShake", _onShake))
        {
            gs.Connect("ScreenShake", _onShake);
        }

        _decay = Mathf.Max((float)GameState.Instance.Cfg("effects.shake.decay", _decay).AsDouble(), 0.001f); // H15：decay=0 震动永不衰减
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs != null && gs.IsConnected("ScreenShake", _onShake))
        {
            gs.Disconnect("ScreenShake", _onShake);
        }
    }

    public override void _Process(double delta)
    {
        if (_strength > 0.1f)
        {
            Offset = new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0)) * _strength;
            _strength = Mathf.Lerp(_strength, 0.0f, _decay * (float)delta);
        }
        else
        {
            _strength = 0.0f;
            Offset = Vector2.Zero;
        }
    }

    private void OnScreenShake(double strength)
    {
        _strength = Mathf.Max(_strength, (float)strength);
    }
}
