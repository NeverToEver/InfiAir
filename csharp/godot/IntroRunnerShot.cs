using Godot;

namespace InfiAir;


/// <summary>镜头 3：侧视走廊奔跑容器（原 GDScript intro_cinematic.gd 内嵌类 _RunnerShot，迁移为同文件顶层类）。
/// 两拍跑步循环：双腿反相、手臂与对侧腿反相、躯干 2 倍频起伏（就地写 rotation/position，零堆分配）。</summary>
public partial class IntroRunnerShot : Node2D
{
    public List<Polygon2D> Scrollers = new();  // 警示条纹/墙肋/舱门框，反向滚动表现冲刺
    public List<Node2D> HipPivots = new();  // 髋：大腿前后摆幅
    public List<Node2D> KneePivots = new();  // 膝：摆动相屈膝、支撑相伸展
    public List<Node2D> ShoulderPivots = new();  // 肩：与对侧腿反相摆动
    public List<Node2D> ElbowPivots = new();  // 肘：保持弯曲微振
    public Node2D BobNode = null!;  // 人物整体随步频上下起伏
    public float BobBaseY;
    public ColorRect Red = null!;  // 应急灯全屏红闪
    public List<Line2D> SpeedLines = new();
    public List<Polygon2D> FgStruts = new();  // 前景近景支杆（比中景更快的反向视差，-1600px/s 回卷）

    private float _t;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        foreach (var s in Scrollers)
        {
            s.Position += new Vector2(-900.0f * (float)delta, 0.0f);
            if (s.Position.X < -160.0f)
            {
                s.Position += new Vector2(2240.0f, 0.0f);
            }
        }

        // 前景支杆：近景快速横扫（-1600px/s，快于中景 -900），出界回卷
        foreach (var fs in FgStruts)
        {
            fs.Position += new Vector2(-1600.0f * (float)delta, 0.0f);
            if (fs.Position.X < -200.0f)
            {
                fs.Position += new Vector2(2520.0f, 0.0f);
            }
        }

        // 两拍跑步循环：双腿反相、手臂与对侧腿反相、躯干 2 倍频起伏（就地写 rotation/position，零堆分配）
        // 关节符号约定（人物朝 +x）：正 rotation = 肢尖向 -x（后），负 = 向 +x（前）
        var runPhase = _t * 11.0f;
        for (var i = 0; i < 2; i++)
        {
            var p = runPhase + Mathf.Pi * i;
            HipPivots[i].Rotation = Mathf.Sin(p) * 0.72f;
            // 膝只向后弯：摆动相（p≈1.6..4.7）脚跟踢向臀部，触地前基本伸直
            KneePivots[i].Rotation = 0.08f + Mathf.Max(0.0f, Mathf.Sin(p - 1.8f)) * 1.35f;
            ShoulderPivots[i].Rotation = 0.1f - Mathf.Sin(p) * 0.6f;
            // 肘只向前弯：前臂保持朝前（奔跑摆臂姿态）
            ElbowPivots[i].Rotation = -(1.0f + Mathf.Sin(p + 0.8f) * 0.25f);
        }

        // bob 最低点对齐支撑相中点（腿在重心正下方），腾空相最高
        BobNode.Position = new Vector2(BobNode.Position.X, BobBaseY + 2.6f * (0.5f + 0.5f * Mathf.Cos(runPhase * 2.0f)));
        var redColor = Red.Color;
        redColor.A = 0.08f + 0.08f * Mathf.Max(0.0f, Mathf.Sin(_t * Mathf.Tau * 6.0f));  // 6Hz 呼吸闪烁
        Red.Color = redColor;
        foreach (var sl in SpeedLines)
        {
            sl.Position += new Vector2(-2200.0f * (float)delta, 0.0f);
            if (sl.Position.X < -320.0f)
            {
                sl.Position = new Vector2(2200.0f + (float)GD.Randf() * 400.0f, (float)GD.Randf() * 1080.0f);
            }
        }
    }
}
