using Godot;

namespace InfiAir;

/// <summary>
/// 鼠标跟随准星（M3b 全量迁移，2026-08-08 自 scripts/aim_crosshair.gd 迁移，P1-1 辅助瞄准
/// 重设计）：世界坐标 top_level Node2D，挂 Player 下。
/// 对局活跃（未暂停、未锁输入、存活）时显示并跟随 Player.aim_point()，同时隐藏系统光标；
/// 暂停/Buff/基地/结算/死亡/过场恢复系统光标并隐藏准星——同一条件驱动两处，
/// 避免双光标/无光标死角。laser_weapon 光束走原始鼠标，与本准星天然一致。
/// 程序化四角 bracket + 中心点（指示器族，不乘 world_scale）。
/// 迁移期动态访问：Player 为 C# 类（InfiAir.Player，M3c 并行迁移）直接调用；
/// Enemy.SinFast 静态直接调用（原经脚本资源 load）。
/// </summary>
public partial class AimCrosshair : Node2D
{
    private const float HalfSize = 14.0f;  // bracket 外接半宽
    private const float Arm = 6.0f;  // bracket 单臂长
    private const float Width = 2.0f;
    private static readonly Color CrosshairColor = new(0.55f, 0.95f, 1.0f, 0.95f);
    /// <summary>bracket 四角符号（静态复用，_draw 零分配）。</summary>
    private static readonly float[] SignValues = { -1.0f, 1.0f };

    private Player? _player;

    /// <summary>Player._load_balance 在 add_child 前调用（top_level 需入树前置位）。</summary>
    public void Init(Player p)
    {
        _player = p;
        TopLevel = true;
        ZIndex = 10;  // 世界实体之上（辅助框层 9、敌机/子弹 0），CanvasLayer HUD 之下
        ProcessMode = Node.ProcessModeEnum.Always;  // 暂停态也要能切回系统光标并隐藏准星
    }

    /// <summary>GDScript 鸭子调用兼容桥（M3b 过渡，M7 删除）：player.gd 迁移前以 init(self) 调用。</summary>
    public void init(Player p) => Init(p);

    public override void _ExitTree()
    {
        // 场景切换/重开兜底：准星消亡时归还系统光标
        if (Input.MouseMode == Input.MouseModeEnum.Hidden)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    public override void _Process(double delta)
    {
        var active = _player != null
            && !GetTree().Paused
            && !_player.IsDead()
            && !_player.IsInputLocked();
        if (active)
        {
            GlobalPosition = _player!.AimPoint();  // active 蕴含 _player 非空（NRT 流分析不透传布尔变量）
            QueueRedraw();
        }

        Visible = active;
        var want = active ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != want)
        {
            Input.MouseMode = want;
        }
    }

    public override void _Draw()
    {
        var pulse = 0.75f + 0.25f * Enemy.SinFast((float)Time.GetTicksMsec() / 1000.0f * 6.0f);
        var c = CrosshairColor * new Color(1.0f, 1.0f, 1.0f, pulse);
        foreach (var sx in SignValues)
        {
            foreach (var sy in SignValues)
            {
                var corner = new Vector2(sx * HalfSize, sy * HalfSize);
                DrawLine(corner, corner - new Vector2(sx * Arm, 0.0f), c, Width, true);
                DrawLine(corner, corner - new Vector2(0.0f, sy * Arm), c, Width, true);
            }
        }

        DrawCircle(Vector2.Zero, 1.6f, c);
    }
}
