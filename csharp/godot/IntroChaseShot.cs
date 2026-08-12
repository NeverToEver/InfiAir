using Godot;

namespace InfiAir;


/// <summary>镜头 5：弹射尾追视角容器（原 GDScript intro_cinematic.gd 内嵌类 _ChaseShot，迁移为同文件顶层类）。
/// 壁面/防撞灯/速度线按透视收缩向后流动（数组预建，_process 零分配）。</summary>
public partial class IntroChaseShot : Node2D
{
    public Node2D ShakeRoot = null!;
    public List<(Line2D Strut, int Side)> Struts = new();  // [Line2D, side(0=左壁 1=右壁)]：壁面斜向结构线，透视收缩向后流动
    public List<(IntroGlowDot Lamp, int Side)> WallLights = new();  // [_GlowDot, side]：壁面防撞灯，随结构线同款透视滚动
    public List<Line2D> SpeedLines = new();
    public List<(Line2D Line, float SideSign)> EdgeLines = new();  // [Line2D, side_sign]：边缘放射速度线，回卷保持同侧

    private float _railSpeed = 400.0f;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        ShakeRoot.Position = new Vector2((float)GD.RandRange(-6.0, 6.0), (float)GD.RandRange(-6.0, 6.0));
        _railSpeed += 2600.0f * d;
        foreach (var pair in Struts)
        {
            var strut = pair.Strut;
            var y = strut.Points[0].Y + _railSpeed * d;
            if (y > 1200.0f)
            {
                y -= 1560.0f;
            }

            // 透视：越远（靠上）壁面越窄，结构线随之收缩
            var ty = Mathf.Clamp((y + 50.0f) / 1180.0f, 0.0f, 1.2f);
            float innerX;
            float outerX;
            if (pair.Side == 0)
            {
                innerX = Mathf.Lerp(700.0f, 620.0f, ty);
                outerX = Mathf.Lerp(430.0f, 250.0f, ty);
            }
            else
            {
                innerX = Mathf.Lerp(1220.0f, 1300.0f, ty);
                outerX = Mathf.Lerp(1490.0f, 1670.0f, ty);
            }

            // C28：创建时已预分配 2 点，set_point_position 原地写（points[i]= 是值语义副本不生效）
            strut.SetPointPosition(0, new Vector2(innerX, y));
            strut.SetPointPosition(1, new Vector2(outerX, y + 70.0f));
        }

        foreach (var lampPair in WallLights)
        {
            var lamp = lampPair.Lamp;
            var ly = lamp.Position.Y + _railSpeed * d;
            if (ly > 1200.0f)
            {
                ly -= 1560.0f;
            }

            var lty = Mathf.Clamp((ly + 50.0f) / 1180.0f, 0.0f, 1.2f);
            // 灯点贴在壁面中线，随透视收缩
            var lampPos = lamp.Position;
            lampPos.X = lampPair.Side == 0 ? Mathf.Lerp(565.0f, 435.0f, lty) : Mathf.Lerp(1355.0f, 1485.0f, lty);
            lampPos.Y = ly;
            lamp.Position = lampPos;
        }

        foreach (var sl in SpeedLines)
        {
            sl.Position += new Vector2(0.0f, 2600.0f * d);
            if (sl.Position.Y > 1300.0f)
            {
                sl.Position = new Vector2((float)GD.Randf() * 1920.0f, -200.0f - (float)GD.Randf() * 300.0f);
            }
        }

        foreach (var edgePair in EdgeLines)
        {
            var el = edgePair.Line;
            el.Position += new Vector2(0.0f, 2600.0f * d);
            if (el.Position.Y > 1300.0f)
            {
                el.Position = new Vector2(
                    edgePair.SideSign < 0.0f ? (float)GD.Randf() * 230.0f : 1690.0f + (float)GD.Randf() * 230.0f,
                    -260.0f);
            }
        }
    }
}
