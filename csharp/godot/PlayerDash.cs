using Godot;

namespace InfiAir;

/// <summary>
/// 玩家相位冲刺组件。
/// Dash 状态机与计时；经 Player 属性转发与公开方法交互。开局即可用，耗 25% 满值燃料。
/// 纯 C# 逻辑类（无信号/导出）：由 C# Player 组合持有；GameState 经
/// Instance 门面访问。
/// </summary>
public class PlayerDash
{
    /// <summary>冲刺进行中（Player._physics_process 驱动 update_move）。</summary>
    public bool Dashing { get; set; }

    /// <summary>冲刺剩余时长（秒）。</summary>
    public float DashTimer { get; set; }

    /// <summary>冲刺方向（输入或瞄准方向归一化；零输入时取准星方向，再回退 UP）。</summary>
    public Vector2 DashDir { get; set; } = Vector2.Zero;

    /// <summary>冲刺冷却剩余（秒，Player 帧驱动 tick_cooldown 递减）。</summary>
    public float DashCooldown { get; set; }

    /// <summary>残影生成间隔计时（秒）。</summary>
    public float AfterimageTimer { get; set; }

    // ---- 数值配置（Player._load_balance 经 Configure 注入；与脚本默认值一致） ----
    public float DashDistance { get; private set; } = 200.0f;
    public float DashTime { get; private set; } = 0.25f;
    public float AfterimageInterval { get; private set; } = 0.08f;

    /// <summary>注意：本类**不持有**冷却上限——生效上限的唯一单源是 <c>Player._dashCooldownMax</c>
    /// （置值与 HUD 充能环的分母都取它）。这里再存一份只会成为「改了没反应」的假旋钮。</summary>
    public void Configure(float distance, float time, float afterimageInterval)
    {
        DashDistance = distance;
        DashTime = time;
        AfterimageInterval = afterimageInterval;
    }

    public bool IsDashing() => Dashing;

    /// <summary>中止进行中的冲刺（外部编排锁输入时调用）：只清残留位移与残影计时，
    /// **不回滚已扣燃料与已置冷却**——中断来自编排（母舰召唤/对接）而非玩家决策，
    /// 退款会把「锁输入」变成白拿一次冷却重置。残留不清的表现是解锁后第一帧用旧 DashDir
    /// 把剩余冲刺跑完（最多一个 dash_distance 的位移 + 残影）。</summary>
    public void Cancel()
    {
        Dashing = false;
        DashTimer = 0.0f;
        AfterimageTimer = 0.0f;
    }

    public float CooldownRemaining() => DashCooldown;

    /// <summary>冷却递减（Player._physics_process 每帧调用）。</summary>
    public void TickCooldown(float delta) => DashCooldown = Mathf.Max(DashCooldown - delta, 0.0f);

    /// <summary>启动冲刺（Player 门面已校验冷却/未冲刺/燃料；扣 25% 满值燃料）。</summary>
    public void Start(Vector2 inputDir, Player player)
    {
        Dashing = true;
        DashTimer = DashTime;
        player.SetFuel(Mathf.Max(player.FuelAmount() - player.DashFuelCost(), 0.0f));
        if (inputDir != Vector2.Zero)
        {
            DashDir = inputDir.Normalized();
        }
        else
        {
            // 无方向输入时向虚拟准星方向冲刺（aim_point 为键鼠+右摇杆统一平滑点）——
            // 不得取真实鼠标位置：纯手柄玩家鼠标停在任意处，冲刺方向会与机头/瞄准无关
            DashDir = (player.AimPoint() - player.GlobalPosition).Normalized();
            if (DashDir == Vector2.Zero)
            {
                DashDir = Vector2.Up;
            }
        }

        DashCooldown = player.DashCooldownMax();
        AfterimageTimer = 0.0f;
        GameState.Instance.PlaySfx(SfxId.Dash);
        RumbleService.Dash(); // 冲刺震动
    }

    /// <summary>冲刺移动驱动（残影生成/位移/回弹；尾焰由 Player 侧保留视觉）。</summary>
    public void UpdateMove(float delta, Player player)
    {
        DashTimer -= delta;
        AfterimageTimer -= delta;
        if (AfterimageTimer <= 0.0f)
        {
            AfterimageTimer = AfterimageInterval;
            player.SpawnAfterimage();
        }

        player.Velocity = DashDir * (DashDistance / DashTime);
        player.MoveAndSlide();
        player.Position = player.ClampToView(player.Position);
        if (DashTimer <= 0.0f)
        {
            Dashing = false;
            GameState.Instance.PlaySfx(SfxId.Dash, -3.0);
        }
    }

}
