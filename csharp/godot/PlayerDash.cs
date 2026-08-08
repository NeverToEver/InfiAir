using Godot;

namespace InfiAir;

/// <summary>
/// 玩家相位冲刺组件（M3c 全量迁移，2026-08-08 自 scripts/player_dash.gd 迁移；A8 拆分，
/// docs/AUDIT_VAULT.md A8）。Dash 状态机与计时；经 Player 属性转发（_dashing 等语法不变）
/// 与公开方法交互。需要解锁 buff（Player.dash_unlocked）且耗 25% 满值燃料。
/// 纯 C# 逻辑类（原 RefCounted、无信号/导出）：由 C# Player 组合持有；GameState 经
/// GameStateBridge 动态访问（仅冲刺起止低频，不涉每帧热路径）。
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
    /// <summary>冲刺基础冷却上限（满值；实际生效上限经 Player.dash_cooldown_max 按 buff 缩放）。</summary>
    public float DashCooldownMax { get; private set; } = 4.0f;
    public float AfterimageInterval { get; private set; } = 0.08f;

    public void Configure(float distance, float time, float cooldown, float afterimageInterval)
    {
        DashDistance = distance;
        DashTime = time;
        DashCooldownMax = cooldown;
        AfterimageInterval = afterimageInterval;
    }

    public bool IsDashing() => Dashing;

    public float CooldownRemaining() => DashCooldown;

    /// <summary>冷却递减（Player._physics_process 每帧调用）。</summary>
    public void TickCooldown(float delta) => DashCooldown = Mathf.Max(DashCooldown - delta, 0.0f);

    /// <summary>启动冲刺（Player 门面已校验 unlock/冷却/未冲刺/燃料；扣 25% 满值燃料）。</summary>
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
            // K04：无方向输入时向虚拟准星方向冲刺（aim_point 为键鼠+右摇杆统一平滑点）——
            // 原实现取真实鼠标位置，纯手柄玩家鼠标停在任意处，冲刺方向与机头/瞄准无关
            DashDir = (player.AimPoint() - player.GlobalPosition).Normalized();
            if (DashDir == Vector2.Zero)
            {
                DashDir = Vector2.Up;
            }
        }

        DashCooldown = player.DashCooldownMax();
        AfterimageTimer = 0.0f;
        GameStateBridge.Call("play_sfx", GameStateBridge.Get("SFX_DASH"));
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
            GameStateBridge.Call("play_sfx", GameStateBridge.Get("SFX_DASH"), -3.0);
        }
    }

    // ---------------- GDScript 鸭子调用兼容桥（M3c 过渡，M7 删除） ----------------
    // 原 GDScript 公开 API（snake_case / UPPER_SNAKE 配置 var）别名转发。纯 C# 类不可被
    // GDScript 动态派发，本桥仅供源码级 API 对等与 C# 侧迁移测试沿用；M7 全量迁移后删除。

    public bool is_dashing() => IsDashing();

    public float cooldown_remaining() => CooldownRemaining();

    public void tick_cooldown(float delta) => TickCooldown(delta);

    public void start(Vector2 inputDir, Player player) => Start(inputDir, player);

    public void update_move(float delta, Player player) => UpdateMove(delta, player);

    public void configure(float distance, float time, float cooldown, float afterimageInterval)
        => Configure(distance, time, cooldown, afterimageInterval);

    public bool dashing { get => Dashing; set => Dashing = value; }

    public float dash_timer { get => DashTimer; set => DashTimer = value; }

    public Vector2 dash_dir { get => DashDir; set => DashDir = value; }

    public float dash_cooldown { get => DashCooldown; set => DashCooldown = value; }

    public float afterimage_timer { get => AfterimageTimer; set => AfterimageTimer = value; }

    public float DASH_DISTANCE { get => DashDistance; set => DashDistance = value; }

    public float DASH_TIME { get => DashTime; set => DashTime = value; }

    public float DASH_COOLDOWN { get => DashCooldownMax; set => DashCooldownMax = value; }

    public float AFTERIMAGE_INTERVAL { get => AfterimageInterval; set => AfterimageInterval = value; }
}
