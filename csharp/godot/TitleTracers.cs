using Godot;

namespace InfiAir;

/// <summary>
/// 标题屏·曳光火网：远处交火的一批短促弹道亮线，错峰亮起又熄灭，整批寿命走完自毁。
///
/// 为什么单独一个节点而不是每发一条 Line2D：一批 12–20 发，逐发建节点会把「一次交火」
/// 变成二十次入树/退树；这里一次分配、<see cref="_Draw"/> 一次画完（叠加混合），
/// 每发只在自己的时间窗里亮一下——这是「远处有人在打」的读数，不是弹幕演示。
/// </summary>
public partial class TitleTracers : Node2D
{
    /// <summary>整批寿命（s）：每发的亮起窗口是它的一小段（<see cref="OnRatio"/>）。</summary>
    private const float BatchLife = 1.2f;

    /// <summary>单发亮起时长占整批寿命的比例（其余时间是等待自己的窗口）。</summary>
    private const float OnRatio = 0.2f;

    private Vector2[] _pos = System.Array.Empty<Vector2>();
    private Vector2[] _dir = System.Array.Empty<Vector2>();
    private float[] _len = System.Array.Empty<float>();
    private float[] _speed = System.Array.Empty<float>();
    private float[] _alpha = System.Array.Empty<float>();
    private float[] _width = System.Array.Empty<float>();
    private float[] _phase = System.Array.Empty<float>();
    private Color[] _color = System.Array.Empty<Color>();
    private float _t;

    /// <summary>
    /// 一批曳光：<paramref name="count"/> 发落在 <paramref name="area"/> 内，相位均匀铺满整批寿命
    /// （错峰而不是齐射：齐射读作一次爆炸，错峰才读作持续交火）。
    /// 弹道方向以水平为主（远处交战双方在一条线上对射），少量斜向。
    /// </summary>
    public void Setup(int count, Rect2 area, Color warm, Color hostile)
    {
        _pos = new Vector2[count];
        _dir = new Vector2[count];
        _len = new float[count];
        _speed = new float[count];
        _alpha = new float[count];
        _width = new float[count];
        _phase = new float[count];
        _color = new Color[count];
        for (var i = 0; i < count; i++)
        {
            _pos[i] = new Vector2(
                (float)GD.RandRange(area.Position.X, area.End.X),
                (float)GD.RandRange(area.Position.Y, area.End.Y));
            var angle = (float)GD.RandRange(-0.35, 0.35) + (GD.Randf() < 0.5f ? 0.0f : Mathf.Pi);
            _dir[i] = Vector2.Right.Rotated(angle);
            _len[i] = (float)GD.RandRange(26.0, 78.0);
            _speed[i] = (float)GD.RandRange(420.0, 900.0);
            _alpha[i] = (float)GD.RandRange(0.25, 0.55);
            _width[i] = (float)GD.RandRange(1.2, 2.6);
            _phase[i] = (float)GD.RandRange(0.0, 1.0 - OnRatio);
            _color[i] = GD.Randf() < 0.4f ? hostile : warm;
        }
    }

    public override void _Ready()
    {
        Material = CinematicFx.AdditiveMaterial();
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _t += d;
        if (_t >= BatchLife)
        {
            QueueFree();
            return;
        }

        // 只在各自的窗口里推进：整批一起推进的话，排在后面的那几发会带着几百像素的位移出场
        for (var i = 0; i < _pos.Length; i++)
        {
            var local = (_t / BatchLife - _phase[i]) / OnRatio;
            if (local > 0.0f && local < 1.0f)
            {
                _pos[i] += _dir[i] * _speed[i] * d;
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        for (var i = 0; i < _pos.Length; i++)
        {
            var local = (_t / BatchLife - _phase[i]) / OnRatio;
            if (local <= 0.0f || local >= 1.0f)
            {
                continue;
            }

            // 单发包络：亮起与熄灭各占一半，两端都收到 0（无「啪一下出现」的硬边）
            var a = Mathf.Sin(Mathf.Pi * local) * _alpha[i];
            DrawLine(_pos[i] - _dir[i] * _len[i], _pos[i], new Color(_color[i], a), _width[i], true);
        }
    }
}
