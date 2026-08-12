using Godot;

namespace InfiAir;


/// <summary>镜头 2 传送端口（原 return_cinematic.gd 内嵌类 _PortalShot；C# 源生成器不支持
/// 内嵌类，迁为同文件顶层类——BaseConsole 先例）。字段名保持原 snake 名（shot 构建/tween 依赖）。</summary>
public partial class ReturnCinematicPortalShot : Node2D
{
    public readonly List<Node2D> _dots = new(); // 环缘能量翻涌：12 个小 glow 点沿椭圆游走
    public Vector2 _center = Vector2.Zero;
    public float _rx = 130.0f;
    public float _ry = 240.0f;
    public Node2D? _inner_station; // 环内虚影站模糊景象（水平弥散抖动）
    public float _inner_base_x;
    public readonly List<Line2D> _swirls = new(); // 环内旋涡弧 ×2（反向旋转，空间搅动感）
    public readonly List<float> _swirl_speeds = new();
    public float _t;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _t += d;
        for (var i = 0; i < _dots.Count; i++)
        {
            var a = _t * 2.5f + Mathf.Tau * i / _dots.Count;
            _dots[i].Position = _center + new Vector2(Mathf.Cos(a) * _rx, Mathf.Sin(a) * _ry);
        }

        for (var i = 0; i < _swirls.Count; i++)
        {
            _swirls[i].Rotation += _swirl_speeds[i] * d;
        }

        if (_inner_station != null)
        {
            var pos = _inner_station.Position;
            pos.X = _inner_base_x + Mathf.Sin(_t * 25.0f) * 2.0f;
            _inner_station.Position = pos;
        }
    }
}
