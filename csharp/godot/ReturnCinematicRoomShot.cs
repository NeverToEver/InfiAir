using Godot;

namespace InfiAir;


/// <summary>镜头 7 休息室（原 return_cinematic.gd 内嵌类 _RoomShot；C# 源生成器不支持
/// 内嵌类，迁为同文件顶层类——BaseConsole 先例）。字段名保持原 snake 名（shot 构建/tween 依赖）。</summary>
public partial class ReturnCinematicRoomShot : Node2D
{
    public Godot.Collections.Dictionary _person = null!; // 人物关节（走入步行循环 + 躺后呼吸共用）
    public float _time_u = 1.0f; // u = dur/3.0（镜头内部关键帧缩放）
    public float _phase;
    public float _t;
    public float _bob_base_y;
    public float _breathe_w; // 呼吸权重（躺下完成后缓入，避免突变）
    public readonly List<Sprite2D> _stars = new(); // 观察窗外漂移星点（窗框内回卷）
    public readonly List<Vector2> _star_vel = new();
    public Rect2 _star_bounds;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _t += d;
        var u = _time_u;
        if (_t < 0.6f * u)
        {
            // 走入步行循环（窗口两端权重淡入淡出，结束自动缓回直立；肢体公式同 _WalkShot）
            _phase += d * 16.0f;
            var k = Mathf.Clamp(Mathf.Min(_t, 0.6f * u - _t) / Mathf.Max(0.12f * u, 0.001f), 0.0f, 1.0f); // H20：镜头时长 0 除零防御
            var personNode = (Node2D)_person["node"].AsGodotObject();
            var hips = _person["hips"].AsGodotArray();
            var knees = _person["knees"].AsGodotArray();
            var shoulders = _person["shoulders"].AsGodotArray();
            var elbows = _person["elbows"].AsGodotArray();
            for (var i = 0; i < 2; i++)
            {
                var p = _phase + Mathf.Pi * i;
                var hip = (Node2D)hips[i].AsGodotObject();
                var knee = (Node2D)knees[i].AsGodotObject();
                var shoulder = (Node2D)shoulders[i].AsGodotObject();
                var elbow = (Node2D)elbows[i].AsGodotObject();
                hip.Rotation = Mathf.Lerp(hip.Rotation, Mathf.Sin(p) * 0.5f * k, 12.0f * d);
                knee.Rotation = Mathf.Lerp(knee.Rotation, 0.05f + Mathf.Max(0.0f, Mathf.Sin(p - 1.8f)) * 0.7f * k, 12.0f * d);
                shoulder.Rotation = Mathf.Lerp(shoulder.Rotation, 0.1f - Mathf.Sin(p) * 0.35f * k, 12.0f * d);
                elbow.Rotation = Mathf.Lerp(elbow.Rotation, -(0.3f + 0.4f * k + Mathf.Sin(p + 0.8f) * 0.15f * k), 12.0f * d);
            }

            var nodePos = personNode.Position;
            nodePos.Y = _bob_base_y + 1.5f * (0.5f + 0.5f * Mathf.Cos(_phase * 2.0f)) * k;
            personNode.Position = nodePos;
        }
        else if (_t > 1.7f * u)
        {
            // 躺下后的呼吸起伏：躯干微小缩放/位移正弦
            _breathe_w = Mathf.Lerp(_breathe_w, 1.0f, 1.5f * d);
            var torso = (Node2D)_person["torso"].AsGodotObject();
            var b = Mathf.Sin(_t * 1.5f) * _breathe_w;
            torso.Scale = new Vector2(1.0f, 1.0f + 0.025f * b);
            torso.Position = new Vector2(0.0f, 0.6f * b);
        }

        // 观察窗外星点缓慢漂移（越界回卷，始终留在窗框内）
        for (var i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            var pos = s.Position + _star_vel[i] * d;
            if (pos.X < _star_bounds.Position.X)
            {
                pos.X += _star_bounds.Size.X;
            }
            else if (pos.X > _star_bounds.End.X)
            {
                pos.X -= _star_bounds.Size.X;
            }

            if (pos.Y < _star_bounds.Position.Y)
            {
                pos.Y += _star_bounds.Size.Y;
            }
            else if (pos.Y > _star_bounds.End.Y)
            {
                pos.Y -= _star_bounds.Size.Y;
            }

            s.Position = pos;
        }
    }
}
