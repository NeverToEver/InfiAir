using Godot;

namespace InfiAir;


/// <summary>镜头 4：操作台紧急启动容器（原 GDScript intro_cinematic.gd 内嵌类 _ConsoleShot，迁移为同文件顶层类）。
/// 双手点按 / 扣合 + 雷达扫掠/回波点亮（预计算角度，零堆分配）。</summary>
public partial class IntroConsoleShot : Node2D
{
    public List<Node2D> Hands = new();  // 双手剪影：倒计时前在按钮簇上快速点按
    public List<Vector2> Targets = new();
    public List<Polygon2D> Cells = new();  // 可点按按钮（闪烁重着色 + 双手目标池）
    public List<Vector2> Handles = new();
    public List<Polygon2D> OpenShapes = new();  // 张开手形（点按态）
    public List<Polygon2D> GripShapes = new();  // 扣合手形（抓把手态）
    public bool Grabbing;  // 倒计时结束：双手猛抓两侧把手
    public float[] Retarget = new float[] { 0.0f, 0.15f };
    public float[] Press = new float[] { 0.0f, 0.0f };  // 按下-抬起起伏剩余时长
    public Line2D RadarSweep = null!;  // 左副屏雷达扫掠针（rotation 随时间推进）
    public List<Sprite2D> RadarBlips = new();  // 雷达回波亮点（扫过点亮、余晖衰减）
    public float[] RadarBlipAngles = System.Array.Empty<float>();
    public float[] RadarBlipE = System.Array.Empty<float>();  // 回波余晖能量 0..1

    private float _radarAngle;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        for (var h = 0; h < Hands.Count; h++)
        {
            var hand = Hands[h];
            if (Grabbing)
            {
                hand.Position = hand.Position.Lerp(Handles[h], 9.0f * d);
            }
            else
            {
                Retarget[h] -= d;
                if (Retarget[h] <= 0.0f)
                {
                    Retarget[h] = (float)GD.RandRange(0.12, 0.28);
                    Targets[h] = Cells[(int)(GD.Randi() % (uint)Cells.Count)].Position;
                }

                // 到位后触发一次按下-抬起起伏（指尖压向台面再弹回）
                if (Press[h] <= 0.0f && hand.Position.DistanceSquaredTo(Targets[h]) < 100.0f)
                {
                    Press[h] = 0.16f;
                }

                var dip = 0.0f;
                if (Press[h] > 0.0f)
                {
                    Press[h] -= d;
                    dip = 5.0f * Mathf.Sin(Mathf.Pi * Mathf.Clamp(1.0f - Press[h] / 0.16f, 0.0f, 1.0f));
                }

                hand.Position = hand.Position.Lerp(Targets[h] + new Vector2(0.0f, dip), 16.0f * d);
            }
        }

        // 雷达：扫掠针匀速旋转；扫过回波角时点亮，余晖按 0.7/s 衰减（预计算角度，零堆分配）
        _radarAngle = Mathf.Wrap(_radarAngle + d * 3.6f, 0.0f, Mathf.Tau);
        RadarSweep.Rotation = _radarAngle;
        for (var b = 0; b < RadarBlips.Count; b++)
        {
            var bDiff = Mathf.Abs(Mathf.Wrap(_radarAngle - RadarBlipAngles[b] + Mathf.Pi, 0.0f, Mathf.Tau) - Mathf.Pi);
            if (bDiff < 0.2f)
            {
                RadarBlipE[b] = 1.0f;
            }
            else
            {
                RadarBlipE[b] = Mathf.Max(0.0f, RadarBlipE[b] - d * 0.7f);
            }

            var blipModulate = RadarBlips[b].Modulate;
            blipModulate.A = 0.12f + 0.88f * RadarBlipE[b];
            RadarBlips[b].Modulate = blipModulate;
        }
    }
}
