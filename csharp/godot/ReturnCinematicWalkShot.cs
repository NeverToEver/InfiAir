using Godot;

namespace InfiAir;


/// <summary>镜头 6 通道步行（原 return_cinematic.gd 内嵌类 _WalkShot；C# 源生成器不支持
/// 内嵌类，迁为同文件顶层类——BaseConsole 先例）。字段名保持原 snake 名（shot 构建/tween 依赖）。</summary>
public partial class ReturnCinematicWalkShot : Node2D
{
    public Node2D _world = null!; // 走廊世界容器（跟随主角 x 匀速平移，主角固定在画面左 1/3）
    public Godot.Collections.Dictionary _person = null!;
    public readonly List<Line2D> _lights = new(); // 顶部感应灯带分段（随主角位置逐节点亮起，身后缓灭）
    public readonly List<float> _light_x = new();
    public Polygon2D _door_l = null!;
    public Polygon2D _door_r = null!;
    public ColorRect _door_leak = null!;
    public bool _door_opened;
    public bool _walking = true;
    public float _scrolled;
    public float _stop_scroll = 140.0f; // 走 ~1.6s（90px/s）抵达舱门前停步（构建时按镜头时长等比缩放）
    public float _time_u = 1.0f; // 内部关键帧时间缩放（舱门滑开时长随镜头时长压缩）
    public float _phase;
    public float _bob_base_y;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        var personNode = (Node2D)_person["node"].AsGodotObject();
        if (_walking)
        {
            _scrolled += 90.0f * d;
            var worldPos = _world.Position;
            worldPos.X = -_scrolled;
            _world.Position = worldPos;
            _phase += d * 6.0f; // 步行循环（奔跑构件降频、步幅减半）
            if (_scrolled >= _stop_scroll)
            {
                _walking = false;
                OpenDoor();
            }
        }

        var personWorldX = 640.0f + _scrolled;
        for (var i = 0; i < _lights.Count; i++)
        {
            var target = 0.08f;
            if (_light_x[i] < personWorldX - 40.0f)
            {
                target = 0.85f; // 脚下已过：亮起
            }

            if (_light_x[i] < personWorldX - 400.0f)
            {
                target = 0.15f; // 身后 0.5s 级缓灭
            }

            var color = _lights[i].DefaultColor;
            color.A = Mathf.Lerp(color.A, target, 5.0f * d);
            _lights[i].DefaultColor = color;
        }

        // 肢体相位驱动（零堆分配）；停步后缓回直立
        var k = _walking ? 1.0f : 0.0f;
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

    public void OpenDoor()
    {
        if (_door_opened)
        {
            return;
        }

        _door_opened = true;
        GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -10.0f, 0.7f); // 舱门滑开 0.7 倍速
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_door_l, "position:x", _door_l.Position.X - 85.0f, 0.5f * _time_u);
        tween.TweenProperty(_door_r, "position:x", _door_r.Position.X + 85.0f, 0.5f * _time_u);
        tween.TweenProperty(_door_leak, "color:a", 0.6f, 0.5f * _time_u); // 门缝光线泄出
    }
}
