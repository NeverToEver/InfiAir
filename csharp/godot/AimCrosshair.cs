using Godot;
using InfiAir.Core.Combat;

namespace InfiAir;

/// <summary>
/// 鼠标跟随准星（辅助瞄准重设计）：世界坐标 top_level Node2D，挂 Player 下。
/// 本局活跃（未暂停、未锁输入、存活）时显示并跟随 Player.aim_point()，同时隐藏系统光标；
/// 暂停/增幅/基地/结算/死亡/过场恢复系统光标并隐藏准星——同一条件驱动两处，
/// 避免双光标/无光标死角。LaserWeapon 光束走原始鼠标，与本准星天然一致。
/// 程序化四角 bracket + 中心点（指示器族，不乘 world_scale）。
/// Player 与 Enemy.SinFast 均 C# typed 直调。
///
/// 交战反馈（core <see cref="CrosshairState"/> 单源，本节点只做查询与绘制）：
/// 压住任一可打目标碰撞圆（出弹即命中）→ 琥珀向红粉混合变色；入辅助瞄准标记目标框
/// （强追踪已生效）→ 叠加金热色 + 一次性整圈旋转 + 括角收拢的锁定框，出框锁定视觉淡出。
/// 时长按模拟时间推进（本节点累计 _Process delta），配置在 balance.json effects.crosshair。
/// </summary>
public partial class AimCrosshair : Node2D
{
    private const float HalfSize = 14.0f;  // bracket 外接半宽
    private const float Arm = 6.0f;  // bracket 单臂长
    private const float Width = 2.0f;
    // 锁定框（括角收拢语汇）：终点半宽与单臂比沿用 bracket 族比例，起点在外框近旁配渐入
    private const float LockHalf = 6.0f;
    private const float LockFromRatio = 0.9f;
    private const float LockArmRatio = 0.5f;
    /// <summary>时长默认 = balance.json effects.crosshair.* 定稿值（键缺失/损坏回退此值，两处必须一致）。</summary>
    private const float DefLockTime = 0.25f;
    private const float DefColorBlendTime = 0.08f;
    private const float DefReleaseFadeTime = 0.12f;
    /// <summary>交战反馈状态机（纯逻辑）：零标记/无目标时三混合全 0，绘制退化为常态琥珀。</summary>
    private CrosshairState _fx = new(DefLockTime, DefColorBlendTime, DefReleaseFadeTime);
    /// <summary>bracket 四角符号（静态复用，_draw 零分配）。</summary>
    private static readonly float[] SignValues = { -1.0f, 1.0f };

    private Player? _player;
    /// <summary>SceneTree 缓存（避免 _Process 每帧 GetTree() 原生往返取 Paused）。</summary>
    private SceneTree? _tree;

    /// <summary>模拟时间（秒，本节点累计 _Process delta）：脉冲相位基准，替代墙钟
    /// （帧率/机器性能无关；_Process 与 _Draw 同帧先后执行，_Draw 读到的是本帧已推进值）。</summary>
    private float _simTime;

    /// <summary>Player._load_balance 在 add_child 前调用（top_level 需入树前置位）。</summary>
    public void Init(Player p)
    {
        _player = p;
        TopLevel = true;
        ZIndex = 10;  // 世界实体之上（辅助框层 9、敌机/子弹 0），CanvasLayer HUD 之下
        ProcessMode = Node.ProcessModeEnum.Always;  // 暂停态也要能切回系统光标并隐藏准星
    }

    public override void _Ready()
    {
        _tree = GetTree();
        // 时长键损坏（≤0 会把除法变 Inf/NaN）钳到正下限（Bullet._pulseHz 同款护栏）
        _fx = new CrosshairState(
            Mathf.Max(CfgTime("effects.crosshair.lock_time", DefLockTime), 0.001f),
            Mathf.Max(CfgTime("effects.crosshair.color_blend_time", DefColorBlendTime), 0.001f),
            Mathf.Max(CfgTime("effects.crosshair.release_fade_time", DefReleaseFadeTime), 0.001f));
    }

    private static float CfgTime(string key, float fallback)
        => (float)GameState.Instance.Cfg(key, fallback).AsDouble();

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
        _simTime += (float)delta;
        // 活跃判据与光标回写同源：Player.AimActive（存活 + 未锁输入）之外，本节点是 Always
        // 处理模式，树暂停也要自行判（暂停期必须把系统光标还回去）
        var active = _player != null
            && _tree is { Paused: false }
            && _player.AimActive();
        if (active)
        {
            var aim = _player!.AimPoint();  // active 蕴含 _player 非空（NRT 流分析不透传布尔变量）
            GlobalPosition = aim;
            // 交战态查询与推点同帧同源（aim 即本帧 _aimSmooth）：锁定走框包含（= 强追踪已生效，
            // Player 粘滞查询已热帧缓存）；可攻击走碰撞圆包含（= 出弹即命中），锁定蕴含之故短路
            if (GameState.Instance.AimFrameLayer is AimFrameLayer layer)
            {
                var marked = layer.MarkedTargetAt(aim) != null;
                var hostile = marked || layer.TargetableTargetAt(aim) != null;
                _fx.Update((float)delta, hostile, marked);
            }
            else
            {
                _fx.Update((float)delta, hostileInReach: false, markedInFrame: false);
            }

            Rotation = _fx.SpinAngleRad;
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
        var pulse = 0.75f + 0.25f * Enemy.SinFast(_simTime * 6.0f);
        // 三态颜色单源 UITheme：常态琥珀 →（可攻击）红粉 →（锁定）金热，混合量由状态机给
        var c = UITheme.AimAmber;
        c = c.Lerp(UITheme.AimOnTarget, _fx.HostileBlend);
        c = c.Lerp(UITheme.AimAmberHot, _fx.LockedBlend);
        DrawCornerBrackets(HalfSize, Arm, c * new Color(1.0f, 1.0f, 1.0f, pulse));

        // 锁定框：从外框近旁收拢到 LockHalf，透明度随锁定混合渐入/淡出，随节点整体旋转
        if (_fx.LockContract > 0.001f)
        {
            var half = Mathf.Lerp(HalfSize * LockFromRatio, LockHalf, _fx.LockContract);
            var lockLine = c * new Color(1.0f, 1.0f, 1.0f, pulse * _fx.LockedBlend);
            DrawCornerBrackets(half, half * LockArmRatio, lockLine);
        }

        DrawCircle(Vector2.Zero, 1.6f, c * new Color(1.0f, 1.0f, 1.0f, pulse));
    }

    /// <summary>四角 bracket（本准星与锁定框共用一份角语汇；指示器族，不乘 world_scale）。</summary>
    private void DrawCornerBrackets(float half, float arm, Color c)
    {
        foreach (var sx in SignValues)
        {
            foreach (var sy in SignValues)
            {
                var corner = new Vector2(sx * half, sy * half);
                DrawLine(corner, corner - new Vector2(sx * arm, 0.0f), c, Width, true);
                DrawLine(corner, corner - new Vector2(0.0f, sy * arm), c, Width, true);
            }
        }
    }
}
