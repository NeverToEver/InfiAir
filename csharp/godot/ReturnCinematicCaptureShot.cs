using Godot;

namespace InfiAir;


/// <summary>镜头 4 捕获轨道（原 return_cinematic.gd 内嵌类 _CaptureShot；C# 源生成器不支持
/// 内嵌类，迁为同文件顶层类——BaseConsole 先例）。字段名保持原 snake 名（shot 构建/tween 依赖）。</summary>
public partial class ReturnCinematicCaptureShot : Node2D
{
    public Vector2[] _samples = System.Array.Empty<Vector2>(); // 捕获轨道弧采样（构建期预计算，供战机定位）
    public Sprite2D? _ship;
    public float _ship_u = 1.0f; // 战机沿轨位置参数（1=远端 → 0=站体端，tween 驱动；须保持原名：shot4 以 "tween_property(root, "_ship_u", …)" 按 ClassDB 属性名驱动）
    public readonly List<Sprite2D> _lights = new(); // 站体环缘航行灯 ×8（慢速追逐明灭）
    public float _t;

    public Vector2 SampleAt(float t)
    {
        var f = Mathf.Clamp(t, 0.0f, 1.0f) * (_samples.Length - 1);
        var i = (int)f;
        if (i >= _samples.Length - 1)
        {
            return _samples[_samples.Length - 1];
        }

        return _samples[i].Lerp(_samples[i + 1], f - i);
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _t += d;
        // 航行灯慢追逐：相位绕环依次点亮（alpha 窄脉冲 + 低常亮底）
        for (var i = 0; i < _lights.Count; i++)
        {
            var w = Mathf.Sin(_t * 1.5f - Mathf.Tau * i / 8.0f);
            var modulate = _lights[i].Modulate;
            modulate.A = 0.12f + 0.78f * Mathf.Pow(Mathf.Max(w, 0.0f), 4.0f);
            _lights[i].Modulate = modulate;
        }

        if (_ship != null)
        {
            _ship.Position = SampleAt(_ship_u);
            var ahead = SampleAt(Mathf.Max(_ship_u - 0.02f, 0.0f));
            // 贴图机头朝 +y 上（player.gd 同款：rotation = 航向角 + PI/2）
            _ship.Rotation = (ahead - _ship.Position).Angle() + Mathf.Pi * 0.5f;
        }
    }
}
