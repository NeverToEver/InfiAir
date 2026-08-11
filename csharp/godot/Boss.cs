using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// Boss（M3d 全量迁移，2026-08-08 自 scripts/boss.gd 迁移）：4 种轮换（1 重装 / 2 游击 / 3 母舰 /
/// 4 月蚀），HP 分段驱动阶段框架（BOSS_REDESIGN §4.1）：P1（100–70%）→ P2（70–30%）→ ENRAGE
/// （&lt;30%），P1/P2 各为数据驱动的模式表循环；段切换：0.6s 蓄力辉光 + 抖屏 + 变调音效 + 清自身
/// 开火计时。走位/攻击/狂暴经 A3 组合委托 BossMovement/BossAttacks/EnrageSequence（纯 C# 类）；
/// 弹幕经 BossFire（纯 C# 类）。语义保持：模式表脚本默认值镜像 balance.json、难度分档统一应用
/// （§4.4）、阶段转场清弹 + 玩家短暂无敌（机制三）、狂暴锁血 30% + 玩家移速 ×0.35、逃跑警告 +
/// 上飘、体碰信号事件驱动（P0-2）、受击闪白手动衰减（P1-2）。
/// 类型化调用；HUD 警告横幅/FormationBomb 经群组/脚本资源判定；EnrageSequence/BossAttacks/
/// BossMovement 对本类经 Get/Call 动态派发（snake_case/PascalCase 双名，M7 重定型）。
/// </summary>
public partial class Boss : Area2D
{
    [Signal]
    public delegate void HealthChangedEventHandler(float current, float maximum);

    [Signal]
    public delegate void DiedEventHandler();

    [Signal]
    public delegate void EnragedEventHandler();

    /// <summary>常规阶段切换（P1→P2、进入 ENRAGE）时发出，HUD 血条短闪。</summary>
    [Signal]
    public delegate void PhaseChangedEventHandler(int newPhase);

    /// <summary>逃跑离场时发出（击毁不会发）；died 在击毁与逃跑离场时都会发出。</summary>
    [Signal]
    public delegate void EscapedEventHandler();

    /// <summary>狂暴子状态机（对齐原作 BossState 的 4 个 ENRAGE_* 子状态）。</summary>
    public enum EnragePhase { NONE, TRANSITION, ACTIVE, RELEASE_HOLD, RETURN }

    /// <summary>常规阶段（§4.1）：P1/P2 模式表循环，ENRAGE 为狂暴（序列结束后「余怒」沿用 P2 表提速）。</summary>
    public enum FightPhase { P1, P2, ENRAGE }

    /// <summary>冲刺掠过（二型 P2 攻击）子状态。</summary>
    public enum SweepState { NONE, AIM, DASH, RETURN }

    // ---- 静态常量表 / 实例资源（U07：静态 Godot 资源改实例字段——退出 segfault 实测教训） ----
    private readonly Texture2D _bossSprite1 = GD.Load<Texture2D>("res://assets/sprites/boss_ship_1.png");
    private readonly Texture2D _bossSprite2 = GD.Load<Texture2D>("res://assets/sprites/boss_ship_2.png");
    private readonly Texture2D _bossSprite3 = GD.Load<Texture2D>("res://assets/sprites/boss_ship_3.png");
    private readonly Texture2D _bossSprite4 = GD.Load<Texture2D>("res://assets/sprites/boss_ship_4.png");
    /// <summary>4 型「月蚀」专属贴图（环刃法师，2026-08-09 接线）。
    /// 数组在构造器装配（字段初始化器禁引用实例字段）。</summary>
    private readonly Texture2D[] _bossTextures;
    /// <summary>独立召唤计时（不占模式表）：3 型「母舰」专属（_physics_process 查询）。</summary>
    private static readonly Dictionary<int, bool> SummonerTypes = new() { [3] = true, [4] = false };
    /// <summary>受击闪白总时长（游击型更短）：_flash_hit 查询。</summary>
    private static readonly Dictionary<int, float> HitFlashByType = new() { [1] = 0.1f, [2] = 0.05f, [3] = 0.1f, [4] = 0.1f };
    /// <summary>2026-08-07 审计：逃跑警告闪烁与狂暴底色提常量（原每帧构造 Color）。</summary>
    private static readonly Color EscapeBlinkColor = new(1.8f, 1.3f, 0.5f);
    private static readonly Color EnrageBlinkColor = new(1.5f, 0.65f, 0.65f);

    /// <summary>
    /// 模式表脚本默认值（与 balance.json boss.phases.typeN 保持一致，AGENTS.md 约定）：
    /// 1 型 P1=[5路扇形,追踪弹] P2=[蓄力重炮,7路扇形]；2 型 P1=[3连狙] P2=[冲刺掠过,3连狙]；
    /// 3 型 P1=[旋转cross+召唤] P2=[编队齐射,弹幕墙]（召唤为独立计时，不在模式表内）；
    /// 4 型 P1=[ring_burst×3,追踪弹] P2=[ring_burst×3,旋转cross,3连狙]。
    /// </summary>
    private readonly Godot.Collections.Dictionary _defaultPatterns = new()
    {
        [1] = new Godot.Collections.Dictionary
        {
            ["p1"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("fan5"), ["waves"] = 3, ["interval"] = 1.6 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("homing"), ["waves"] = 2, ["interval"] = 1.6 },
            },
            ["p2"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("charged_cannon"), ["waves"] = 1, ["interval"] = 2.4 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("fan7"), ["waves"] = 3, ["interval"] = 1.4 },
            },
        },
        [2] = new Godot.Collections.Dictionary
        {
            ["p1"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("sniper3"), ["waves"] = 1, ["interval"] = 1.8 },
            },
            ["p2"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("dash_sweep"), ["waves"] = 1, ["interval"] = 2.5 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("sniper3"), ["waves"] = 1, ["interval"] = 1.5 },
            },
        },
        [3] = new Godot.Collections.Dictionary
        {
            ["p1"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("cross"), ["duration"] = 6.0, ["interval"] = 0.9 },
            },
            ["p2"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("minion_volley"), ["waves"] = 1, ["interval"] = 2.0 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("bullet_wall"), ["waves"] = 1, ["interval"] = 1.5 },
            },
        },
        [4] = new Godot.Collections.Dictionary
        {
            ["p1"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("ring_burst"), ["waves"] = 3, ["interval"] = 1.7 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("homing"), ["waves"] = 2, ["interval"] = 1.5 },
            },
            ["p2"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("ring_burst"), ["waves"] = 3, ["interval"] = 1.4 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("cross"), ["duration"] = 5.0, ["interval"] = 0.8 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("sniper3"), ["waves"] = 1, ["interval"] = 1.6 },
            },
        },
    };

    // ---- A3：组合组件（纯 C# 类；Configure 注入） ----
    private readonly BossFire _fire = new();
    private readonly BossMovement _movement = new();
    private readonly BossAttacks _attacks = new();
    private readonly EnrageSequence _enrageSequence = new();
    /// <summary>A5：spawner 依赖注入（spawner._spawn_boss 设置；替代 group 现找）。</summary>
    private Spawner? _spawner; // U13：typed

    // ---- 数值配置（_ready 从 balance.json 覆盖；与脚本默认值一致） ----
    public float EnterSpeed { get; set; } = 140.0f;
    /// <summary>战斗锚线距可见区域顶缘的偏移（small 档 view.position.y=0 时即绝对 y；使用点一律走 FightAnchorY()）。</summary>
    public float FightY { get; set; } = 230.0f;
    public float StrafeMinX { get; set; } = 300.0f;
    public float StrafeMaxX { get; set; } = 1620.0f;
    /// <summary>HP 基底（× 类型系数 × 难度乘数；对齐原作首发 Boss ≈12s TTK 量级）。</summary>
    public float HpBase { get; set; } = 800.0f;
    /// <summary>各类型移动速度 / 开火间隔（模式表 interval 缺键时的回退基准）/ 弹速。</summary>
    public Godot.Collections.Array<float> StrafeSpeeds { get; set; } = new() { 150.0f, 400.0f, 60.0f, 40.0f };
    public Godot.Collections.Array FireIntervals { get; set; } = new() { 1.6, 1.8, 0.9, 1.2 };
    public float FanBulletSpeed { get; set; } = 380.0f;
    public float HomingBulletSpeed { get; set; } = 300.0f;
    public float SniperBulletSpeed { get; set; } = 650.0f;
    public float CrossBulletSpeed { get; set; } = 260.0f;
    /// <summary>4 型「月蚀」ring_burst 环弹攻击参数（2026-08-04；默认值与 balance.json 双写）。</summary>
    public float RingBurstSpeed { get; set; } = 340.0f;
    public int BulletDamageRing { get; set; } = 14;
    /// <summary>阶段阈值：P2 = 70%（新增），ENRAGE = 30%（沿用原作）。</summary>
    public float Phase2HpRatio { get; set; } = 0.7f;
    public float EnrageHpRatio { get; set; } = 0.3f;
    /// <summary>「余怒」倍率：射速 ×1.3（原 ×1.5 下调，§5.4）/ 移速 ×1.3。</summary>
    public float EnrageRateMult { get; set; } = 1.3f;
    public float EnrageSpeedMult { get; set; } = 1.3f;
    /// <summary>狂暴期玩家减速乘区（替代定身，§4.3）：TRANSITION+ACTIVE 期间移速 ×0.35。</summary>
    public float EnragePlayerSlow { get; set; } = 0.35f;
    /// <summary>段切换演出时长（蓄力辉光 + 停火，§4.1）。</summary>
    public float PhaseShiftDuration { get; set; } = 0.6f;
    /// <summary>阶段转场公平感（2026-08-03 机制三）：切换时清全部活跃弹丸 + 给玩家短暂无敌。</summary>
    public bool ClearOnShift { get; set; } = true;
    public float TransitionInvincible { get; set; } = 1.0f;
    /// <summary>狙击 telegraph（§4.2/§5.2）：瞄准线 0.35s（前 0.2s 微跟踪玩家后固定），到点沿线出弹。</summary>
    public float SniperAimTime { get; set; } = 0.35f;
    public float SniperTrackTime { get; set; } = 0.2f;
    public float SniperBurstInterval { get; set; } = 0.12f;
    /// <summary>一型 P1 纵向下压（§5.1）：每 6s 下压 80px 再回。</summary>
    public float PressInterval { get; set; } = 6.0f;
    public float PressDepth { get; set; } = 80.0f;
    /// <summary>D05 P2 走位（balance.json boss.movement，公开字段供 BossMovement 读取）。</summary>
    public int Type1P2Strafe { get; set; } = 200;
    public float Type1P2BobAmp { get; set; } = 40.0f;
    public float Type1P2BobPeriod { get; set; } = 6.0f;
    public float Type2P2DashTime { get; set; } = 0.4f;
    public float Type2P2RestTime { get; set; } = 0.5f;
    public float Type3P1BobMin { get; set; } = 200.0f;
    public float Type3P1BobMax { get; set; } = 280.0f;
    public float Type3P1BobPeriod { get; set; } = 9.0f;
    public int Type3P2Strafe { get; set; } = 100;
    public float Type3P2BobAmp { get; set; } = 50.0f;
    public float Type3P2BobPeriod { get; set; } = 8.0f;
    /// <summary>蓄力重炮（一型 P2，§5.1）：0.6s 蓄力辉光 → 3 发高速重弹（间隔 0.25s，每发 0.15s 短闪光）。</summary>
    public float CannonCharge { get; set; } = 0.6f;
    public int CannonShots { get; set; } = 3;
    public float CannonInterval { get; set; } = 0.25f;
    public float CannonBulletSpeed { get; set; } = 700.0f;
    public int CannonDamage { get; set; } = 21;
    public float CannonFlash { get; set; } = 0.15f;
    /// <summary>冲刺掠过（二型 P2，§5.2）：0.5s 瞄准线 → 高速横穿玩家高度，路径拖 3 枚减速弹。</summary>
    public float SweepAim { get; set; } = 0.5f;
    public float SweepSpeed { get; set; } = 900.0f;
    public int SweepDropCount { get; set; } = 3;
    public float SweepDropSpeed { get; set; } = 150.0f;
    public int SweepDropDamage { get; set; } = 12;
    public float SweepReturnDuration { get; set; } = 0.8f;
    /// <summary>编队齐射（三型 P2，§5.3）：召唤 4 小怪列横队，0.8s 后齐射一轮自机狙。</summary>
    public int VolleyCount { get; set; } = 4;
    public float VolleyDelay { get; set; } = 0.8f;
    public float VolleyBulletSpeed { get; set; } = 420.0f;
    public int VolleyBulletDamage { get; set; } = 12;
    /// <summary>弹幕墙（三型 P2，§5.3）：10 路低速扇形墙，留 2 个相邻缺口（缺口方位避开自机 ±30°）。</summary>
    public int WallCount { get; set; } = 10;
    public float WallBulletSpeed { get; set; } = 220.0f;
    public int WallDamage { get; set; } = 12;
    public float WallArcDeg { get; set; } = 150.0f;
    /// <summary>
    /// 难度分档（§4.4，boss.difficulty_scaling）：索引 = [easy, medium, hard]。
    /// 只作用于 Boss 攻击密度/速度：开火间隔 ×、弹速 ×、弹数 ±（快照弹幕/伤害不动）。
    /// </summary>
    public Godot.Collections.Array DiffIntervalMult { get; set; } = new() { 1.15, 1.0, 0.85 };
    public Godot.Collections.Array DiffSpeedMult { get; set; } = new() { 0.9, 1.0, 1.1 };
    public Godot.Collections.Dictionary DiffCountDeltas { get; set; } = new()
    {
        ["fan"] = new Godot.Collections.Array { -1, 0, 1 },
        ["homing"] = new Godot.Collections.Array { -1, 0, 1 },
        ["cannon"] = new Godot.Collections.Array { -1, 0, 1 },
        ["volley"] = new Godot.Collections.Array { -1, 0, 1 },
        ["wall"] = new Godot.Collections.Array { -2, 0, 2 },
        ["ring"] = new Godot.Collections.Array { -2, 0, 2 },
        ["salvo"] = new Godot.Collections.Array { -2, 0, 2 },
        ["summon"] = new Godot.Collections.Array { -1, 0, 1 },
        ["drops"] = new Godot.Collections.Array { -1, 0, 1 },
        // 2026-08-05 Q28：ring_burst 为绝对值分档（json 缺键时回退此表，与 §5.6 一致）
        ["ring_burst"] = new Godot.Collections.Array { 10, 12, 14 },
    };
    public int EnrageSnapshotLasers { get; set; } = 4;
    public int EnrageSnapshotRing { get; set; } = 8;
    public float EnrageLaserSpeed { get; set; } = 820.0f;
    public float EnrageRingSpeed { get; set; } = 240.0f;
    /// <summary>狂暴序列时序（对齐原作 EnrageConstants @60fps：360/54/42/24/6/42/48 帧）。</summary>
    public float EnrageDuration { get; set; } = 6.0f;
    public float EnrageTransitionDuration { get; set; } = 0.9f;
    public float EnrageAttackInterval { get; set; } = 0.7f;
    public float EnrageAttackWindup { get; set; } = 0.4f;
    public float EnrageReleaseInterval { get; set; } = 0.1f;
    public float EnrageReleaseHoldDuration { get; set; } = 0.7f;
    public float EnrageReturnDuration { get; set; } = 0.8f;
    /// <summary>轨道：半径 = max(机体宽,高)×1.5 受屏幕边界约束（原作 PATH_RADIUS_SCALE/MIN_Y 钳制）。</summary>
    public float EnragePathRadiusScale { get; set; } = 1.5f;
    /// <summary>出弹点前伸：舰体边缘（原 100 按 r=120 机体定，机体 ÷3 后同步）。</summary>
    public float MuzzleOffset { get; set; } = 100.0f;
    public float EnrageSquarePathRatio { get; set; } = 0.48f;
    /// <summary>RELEASE 弹速 = ACTIVE 弹速 × 原作释放比例（回退路径用）。</summary>
    public float EnrageReleaseLaserSpeed { get; set; } = 300.0f;
    public float EnrageReleaseRingSpeed { get; set; } = 120.0f;
    /// <summary>一型狂暴「旋转堡垒」（§5.1，boss.enrage.type_1）。</summary>
    public float E1RingInterval { get; set; } = 0.5f;
    public int E1RingCount { get; set; } = 12;
    public float E1RingSpeed { get; set; } = 240.0f;
    public float E1RingPrecessionDeg { get; set; } = 15.0f;
    public float E1SalvoCharge { get; set; } = 0.5f;
    public int E1SalvoCount { get; set; } = 8;
    public float E1SalvoSpeed { get; set; } = 700.0f;
    public int E1SalvoDamage { get; set; } = 21;
    /// <summary>二型狂暴「猎杀环绕」（§5.2，boss.enrage.type_2）。</summary>
    public int E2PointCount { get; set; } = 6;
    public float E2PointInterval { get; set; } = 0.8f;
    public float E2Aim { get; set; } = 0.35f;
    public float E2SniperSpeed { get; set; } = 900.0f;
    public int E2SniperDamage { get; set; } = 21;
    public int E2ReleaseRingCount { get; set; } = 12;
    public float E2ReleaseRingSpeed { get; set; } = 120.0f;
    /// <summary>三型狂暴「倾巢」（§5.3，boss.enrage.type_3）。</summary>
    public float E3SummonInterval { get; set; } = 1.2f;
    public int E3SummonWaves { get; set; } = 3;
    public int E3SummonCount { get; set; } = 3;
    public float E3RingInterval { get; set; } = 0.9f;
    public int E3RingCount { get; set; } = 8;
    public float E3RingSpeed { get; set; } = 240.0f;
    public int E3ReleaseRingCount { get; set; } = 16;
    public float E3ReleaseRingSpeed { get; set; } = 120.0f;
    /// <summary>4 型「月蚀」狂暴：双环反向进动 + 蓄力环阵（boss.enrage.type_4）。</summary>
    public int E4RingCount { get; set; } = 10;
    public float E4RingInterval { get; set; } = 0.8f;
    public float E4RingSpeed { get; set; } = 200.0f;
    public float E4PrecessionDeg { get; set; } = 15.0f;
    public int E4ReleaseRingCount { get; set; } = 20;
    public float E4ReleaseRingSpeed { get; set; } = 130.0f;
    /// <summary>4 型「月蚀」中心悬停微摆（boss.movement.type4）。</summary>
    public float Move4BobAmp { get; set; } = 30.0f;
    public float Move4BobPeriod { get; set; } = 2.4f;
    /// <summary>逃跑：进入战斗 50s 未击杀触发，最后 3s 警告 + 上飘（对齐原作 3000/180 帧@60fps）。</summary>
    public float EscapeTime { get; set; } = 50.0f;
    public float EscapeWarning { get; set; } = 3.0f;
    public float EscapeDrift { get; set; } = 26.0f;
    public float EscapeStartSpeed { get; set; } = 120.0f;
    public float EscapeAccel { get; set; } = 420.0f;
    /// <summary>血条下方逃跑倒计时显示起点（剩余 ≤10s，§4.5）。</summary>
    public float EscapeCountdownFrom { get; set; } = 10.0f;
    /// <summary>各弹种伤害（对齐原作 boss_attack.py phase-1：spread 12+2=14 / aim 18+3=21 / wave 12 / 快照激光 21 / 快照环弹 12）。</summary>
    public int BulletDamageFan { get; set; } = 14;
    public int BulletDamageHoming { get; set; } = 12;
    public int BulletDamageSniper { get; set; } = 21;
    public int BulletDamageCross { get; set; } = 12;
    public int BulletDamageSnapshotLaser { get; set; } = 21;
    public int BulletDamageSnapshotRing { get; set; } = 12;
    /// <summary>身体撞击伤害（对齐原作 BOSS_COLLISION_DAMAGE=30）。</summary>
    public int CollisionDamage { get; set; } = 30;
    /// <summary>慢速力场：机体移速 ×0.8（对齐原作 boss 移动 slow_factor）。</summary>
    public float SlowFieldFactor { get; set; } = 0.8f;

    // ---- 对局状态（setup/take_damage 写入；GDScript 调用方/测试读写） ----
    public int BossType { get; set; } = 1;
    public float MaxHp { get; set; } = 30.0f;
    public float Hp { get; set; } = 30.0f;
    public bool IsEscaped { get; set; }

    // ---- 内部状态 ----
    private float _ws = 1.0f; // 全局机体缩放缓存（_ready 读取一次）
    private bool _inFight;
    private bool _enraged;
    private float _scoreScale = 1.0f;
    private float _survival;
    private bool _escapeWarned;
    private bool _escaping;
    private float _escapeSpeed;
    /// <summary>2026-08-06 审计：逃跑警告期上飘累计偏移——直接 `position.y -= drift*delta` 会被
    /// 绝对 y 赋值走位（type1 P2 / type3 P2 _move_bob、type4）逐帧覆盖，三型无上飘效果；
    /// 累计偏移由绝对赋值处（BossMovement）叠加，增量式走位保留直接减。</summary>
    private float _escapeDriftOffset;
    /// <summary>母舰召唤减速带：短时减速乘区（仅位移，经 slow_factor 生效）。</summary>
    private float _summonSlowTimer;
    private float _summonSlowFactor = 1.0f;
    /// <summary>slow_field buff 名（信号驱动 Refresh 用；U14 静态 StringName 口径）。</summary>
    private static readonly StringName SlowFieldId = new("slow_field");
    /// <summary>2026-08-07 审计：slow_field 布尔缓存（对齐 enemy.gd C22——物理帧免每帧 buff_count 字典查询；
    /// 2026-08-11 二轮收敛 BuffBoolCache：BuffsChanged 信号事件驱动）。</summary>
    private readonly BuffBoolCache _slowCache;
    /// <summary>2026-08-07 审计：体碰改信号事件驱动（对齐 enemy.gd P0-2）。</summary>
    private bool _bodyContact;
    // 阶段框架与模式表循环（§4.1）
    private FightPhase _fightPhase = FightPhase.P1;
    private Godot.Collections.Dictionary _patterns = new(); // {"p1": [...], "p2": [...]}，_ready 从配置载入
    private int _patternIndex;
    private float _patternLeft; // 当前模式剩余波次（或剩余时长秒）
    private bool _patternIsDuration;
    private float _fireTimer = 1.6f;
    /// <summary>G024：三型普通阶段召唤小怪间隔（balance.json boss.phases.type3.summon_interval 可覆盖）。</summary>
    private float _summonInterval = 6.0f;
    private float _summonTimer = 6.0f;
    /// <summary>A3 收敛：召唤机标志字段化（原每物理帧 SummonerTypes.TryGetValue 查询；Setup 按 BossType 固化）。
    /// 默认 BossType=1 非字典键 → 默认 false，与直实例化不调 Setup 的 TryGetValue 结果一致。</summary>
    private bool _isSummoner;
    /// <summary>贴图有效尺寸（_ready 实测更新，算轨道半径）。</summary>
    private Vector2 _bossSize = new(328.0f, 328.0f);
    private Sprite2D _sprite = null!;
    /// <summary>P1-2：受击闪白手动衰减（_physics_process 逐帧 lerp 回 _base_modulate）。</summary>
    private float _flashTimer;
    private float _flashTotal = 0.1f;

    private readonly Script _formationBombScript;

    public Boss()
    {
        _bossTextures = new[] { _bossSprite1, _bossSprite2, _bossSprite3, _bossSprite4 };
        _slowCache = new BuffBoolCache(SlowFieldId);
        _formationBombScript = GD.Load<Script>("res://csharp/godot/FormationBomb.cs");
    }

    public override void _Ready()
    {
        GameState.Instance.BindEnemy(this); // 统一绑定（docs/ENTITY_MANAGER.md）
        // 机体尺寸族：设计值 × 全局缩放（tscn 存 1.0 基准，幂等覆盖）
        _ws = (float)GameState.Instance.WorldScale;
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _sprite.Scale = Vector2.One * 1.15f * _ws;
        if (GetNode<CollisionShape2D>("CollisionShape2D").Shape is CircleShape2D circle)
        {
            circle.Radius = 120.0f * _ws;
        }

        MuzzleOffset = 100.0f * _ws;
        _fire.MuzzleOffset = MuzzleOffset;
        _fire.WorldScale = _ws;
        // V 系列：Configure 参数已 typed（BossFire）——直调发射器，原「注入本类并转发」桥删除
        _attacks.Configure(_fire, _ws);
        _enrageSequence.Configure(_fire, _attacks, _ws);
        // 数值配置缓存（启动一次读入）
        LoadBalance();
    }

    /// <summary>数值配置缓存（启动一次读入，避免每帧 Dictionary 路径查找；CfgFx 统一判型回退 + 域钳）。</summary>
    private void LoadBalance()
    {
        // 数值配置缓存（启动一次读入）
        EnterSpeed = CfgFx.Float("boss.enter_speed", EnterSpeed);
        FightY = CfgFx.Float("boss.fight_y", FightY);
        StrafeMinX = CfgFx.Float("boss.strafe_min_x", StrafeMinX);
        StrafeMaxX = CfgFx.Float("boss.strafe_max_x", StrafeMaxX);
        // V 系列：阶段阈值钳 (0.01, 0.99]——>1 时钳血逻辑把 HP 抬升到 >MaxHp 并永久锁血，≤0 免疫伤害（Q02 同根因）
        Phase2HpRatio = CfgFx.Float("boss.phase2_hp_ratio", Phase2HpRatio, 0.01f, 0.99f);
        EnrageHpRatio = CfgFx.Float("boss.enrage.hp_ratio", EnrageHpRatio, 0.01f, 0.99f);
        // AB18：保序修正——P2 段必须高于 ENRAGE 线（BOSS_REDESIGN §4.1 70%→30% 顺序），
        // 倒挂配置（phase2=0.2, enrage=0.3）使 P2 段整体跳过、Boss 以 P1 强度直接狂暴且无告警
        if (Phase2HpRatio <= EnrageHpRatio)
        {
            Phase2HpRatio = Mathf.Min(EnrageHpRatio + 0.01f, 0.98f);
        }
        EnrageRateMult = CfgFx.Float("boss.enrage.rate_mult", EnrageRateMult);
        EnrageSpeedMult = CfgFx.Float("boss.enrage.speed_mult", EnrageSpeedMult);
        EnragePlayerSlow = CfgFx.Float("boss.enrage.player_slow", EnragePlayerSlow);
        EnrageSnapshotLasers = CfgFx.Int("boss.enrage.snapshot_lasers", EnrageSnapshotLasers);
        EnrageSnapshotRing = CfgFx.Int("boss.enrage.snapshot_ring", EnrageSnapshotRing);
        EnrageLaserSpeed = CfgFx.Float("boss.enrage.laser_speed", EnrageLaserSpeed);
        EnrageRingSpeed = CfgFx.Float("boss.enrage.ring_speed", EnrageRingSpeed);
        // 2026-08-09 审计（R06 同族）：三个时序键作 EnrageSequence 除数的分母，0 值除零得 ±inf——
        // float 不崩、Clamp 收敛，但狂暴序列 1-2 帧内瞬间走完（行为退化）；下限钳制 ≥0.05
        EnrageDuration = CfgFx.Float("boss.enrage.duration", EnrageDuration, 0.05f);
        EnrageTransitionDuration = CfgFx.Float("boss.enrage.transition_duration", EnrageTransitionDuration, 0.05f);
        // AC9（2026-08-11 健壮性审查）：三时序键钳下限 ≥0.05（R06 只封了 duration 族）——
        // attack_windup≤0 使 ACTIVE 一进入 _attackTimer 即触发（蓄力 telegraph 归零）；
        // attack_interval/release_interval≤0 → 狂暴回退/释放路径每帧攻击风暴
        EnrageAttackInterval = CfgFx.Float("boss.enrage.attack_interval", EnrageAttackInterval, 0.05f);
        EnrageAttackWindup = CfgFx.Float("boss.enrage.attack_windup", EnrageAttackWindup, 0.05f);
        EnrageReleaseInterval = CfgFx.Float("boss.enrage.release_interval", EnrageReleaseInterval, 0.05f);
        // R06 同族：release_hold_duration 同为 EnrageSequence 除数，0/负值时 RELEASE_HOLD 段一帧压完
        // （Clamp(1-(-inf))=1，Boss 瞬跳回退），下限钳制 ≥0.05
        EnrageReleaseHoldDuration = CfgFx.Float("boss.enrage.release_hold_duration", EnrageReleaseHoldDuration, 0.05f);
        EnrageReturnDuration = CfgFx.Float("boss.enrage.return_duration", EnrageReturnDuration, 0.05f);
        EnragePathRadiusScale = CfgFx.Float("boss.enrage.path_radius_scale", EnragePathRadiusScale);
        // H12（健壮性审核）：square_path_ratio 钳制 (0,1]——0 会除零产生 inf 轨道 NaN
        EnrageSquarePathRatio = CfgFx.Float("boss.enrage.square_path_ratio", EnrageSquarePathRatio, 0.05f, 1.0f);
        EnrageReleaseLaserSpeed = CfgFx.Float("boss.enrage.release_laser_speed", EnrageReleaseLaserSpeed);
        EnrageReleaseRingSpeed = CfgFx.Float("boss.enrage.release_ring_speed", EnrageReleaseRingSpeed);
        _bossSize = _sprite.Texture.GetSize() * _sprite.Scale;
        EscapeTime = CfgFx.Float("boss.escape.time", EscapeTime);
        EscapeWarning = CfgFx.Float("boss.escape.warning", EscapeWarning);
        EscapeDrift = CfgFx.Float("boss.escape.drift", EscapeDrift);
        EscapeStartSpeed = CfgFx.Float("boss.escape.start_speed", EscapeStartSpeed);
        EscapeAccel = CfgFx.Float("boss.escape.accel", EscapeAccel);
        // 2026-08-07 审计：slow_field 缓存初始值 + buffs_changed 增量刷新（对齐 enemy.gd C22）
        _slowCache.Refresh();
        _slowCache.Connect(GameState.Instance);

        // 2026-08-07 审计：体碰信号事件驱动（对齐 enemy.gd P0-2；collision_mask=3 已含 player Hitbox 层 1）
        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
        EscapeCountdownFrom = CfgFx.Float("boss.escape.countdown_visible_from", EscapeCountdownFrom);
        HpBase = CfgFx.Float("boss.hp_base", HpBase);
        // C18：cfg 返回 Variant，显式转 Array[float] 再赋 typed 变量
        var ss = GameState.Instance.Cfg("boss.strafe_speeds", StrafeSpeeds);
        var ssArr = new Godot.Collections.Array<float>();
        if (ss.VariantType == Variant.Type.Array)
        {
            foreach (var v in ss.AsGodotArray())
            {
                ssArr.Add((float)v.AsDouble());
            }
        }

        StrafeSpeeds = ssArr.Count >= 3 ? ssArr : new Godot.Collections.Array<float> { 150.0f, 400.0f, 60.0f }; // H11：不足 3 元素回退默认
        // B5 修复：cfg 对数组返回共享 JSON 引用，_apply_difficulty_scaling 会就地乘算
        // FIRE_INTERVALS[i]——不拷贝会污染全局缓存、easy/hard 下跨 Boss 复合叠加。
        // H11：非数组类型时回退默认（原 .duplicate() 对非数组直接崩溃）；
        // 2026-08-10：空数组/不足 3 元素同回退（BaseFireInterval 的 Clamp(0,0,-1) 得 -1
        // 索引 FireIntervals[-1] 抛 IndexOutOfRangeException，与 StrafeSpeeds H11 同口径）
        var fiRaw = GameState.Instance.Cfg("boss.fire_intervals", FireIntervals);
        var fiArr = new Godot.Collections.Array();
        if (fiRaw.VariantType == Variant.Type.Array)
        {
            fiArr = (Godot.Collections.Array)fiRaw.AsGodotArray().Duplicate(true);
        }

        FireIntervals = fiArr.Count >= 3 ? fiArr : (Godot.Collections.Array)FireIntervals.Duplicate(true);
        FanBulletSpeed = CfgFx.Float("boss.fan_bullet_speed", FanBulletSpeed);
        HomingBulletSpeed = CfgFx.Float("boss.homing_bullet_speed", HomingBulletSpeed);
        SniperBulletSpeed = CfgFx.Float("boss.sniper_bullet_speed", SniperBulletSpeed);
        CrossBulletSpeed = CfgFx.Float("boss.cross_bullet_speed", CrossBulletSpeed);
        CollisionDamage = CfgFx.Int("boss.collision_damage", CollisionDamage);
        // 决策（2026-08-11 CfgFx 批 5）：slow_field.factor 此处保持无钳制直读——Enemy 侧同键
        // 钳 [0,1]，Boss 侧慢速力场仅作减速系数、无加速场语义；行为零变化铁律下不补钳，
        // CfgFx.Float 仅加判型回退（坏类型不崩）
        SlowFieldFactor = CfgFx.Float("buffs.slow_field.factor", SlowFieldFactor);
        BulletDamageFan = CfgFx.Int("boss.bullet_damage.fan", BulletDamageFan);
        BulletDamageHoming = CfgFx.Int("boss.bullet_damage.homing", BulletDamageHoming);
        BulletDamageSniper = CfgFx.Int("boss.bullet_damage.sniper", BulletDamageSniper);
        BulletDamageCross = CfgFx.Int("boss.bullet_damage.cross", BulletDamageCross);
        BulletDamageSnapshotLaser = CfgFx.Int("boss.bullet_damage.snapshot_laser", BulletDamageSnapshotLaser);
        BulletDamageSnapshotRing = CfgFx.Int("boss.bullet_damage.snapshot_ring", BulletDamageSnapshotRing);
        PhaseShiftDuration = CfgFx.Float("boss.phases.phase_shift_duration", PhaseShiftDuration);
        ClearOnShift = GameState.Instance.Cfg("boss.phases.clear_on_shift", ClearOnShift).AsBool();
        TransitionInvincible = CfgFx.Float("boss.phases.transition_invincible", TransitionInvincible);
        SniperAimTime = CfgFx.Float("boss.phases.telegraph.sniper_aim", SniperAimTime);
        SniperTrackTime = CfgFx.Float("boss.phases.telegraph.sniper_track", SniperTrackTime);
        SniperBurstInterval = CfgFx.Float("boss.phases.attacks.sniper3.burst_interval", SniperBurstInterval);
        PressInterval = CfgFx.Float("boss.phases.press_interval", PressInterval);
        PressDepth = CfgFx.Float("boss.phases.press_depth", PressDepth);
        Type1P2Strafe = CfgFx.Int("boss.movement.type1_p2_strafe", Type1P2Strafe);
        Type1P2BobAmp = CfgFx.Float("boss.movement.type1_p2_bob_amp", Type1P2BobAmp);
        Type1P2BobPeriod = CfgFx.Float("boss.movement.type1_p2_bob_period", Type1P2BobPeriod);
        Type2P2DashTime = CfgFx.Float("boss.movement.type2_p2_dash_time", Type2P2DashTime);
        Type2P2RestTime = CfgFx.Float("boss.movement.type2_p2_rest_time", Type2P2RestTime);
        Type3P1BobMin = CfgFx.Float("boss.movement.type3_p1_bob_min", Type3P1BobMin);
        Type3P1BobMax = CfgFx.Float("boss.movement.type3_p1_bob_max", Type3P1BobMax);
        Type3P1BobPeriod = CfgFx.Float("boss.movement.type3_p1_bob_period", Type3P1BobPeriod);
        Type3P2Strafe = CfgFx.Int("boss.movement.type3_p2_strafe", Type3P2Strafe);
        Type3P2BobAmp = CfgFx.Float("boss.movement.type3_p2_bob_amp", Type3P2BobAmp);
        Type3P2BobPeriod = CfgFx.Float("boss.movement.type3_p2_bob_period", Type3P2BobPeriod);
        // 阶段 B 攻击库参数（boss.phases.attacks.*）
        CannonCharge = CfgFx.Float("boss.phases.attacks.charged_cannon.charge", CannonCharge);
        CannonShots = CfgFx.Int("boss.phases.attacks.charged_cannon.shots", CannonShots);
        CannonInterval = CfgFx.Float("boss.phases.attacks.charged_cannon.interval", CannonInterval);
        CannonBulletSpeed = CfgFx.Float("boss.phases.attacks.charged_cannon.bullet_speed", CannonBulletSpeed);
        CannonDamage = CfgFx.Int("boss.phases.attacks.charged_cannon.damage", CannonDamage);
        CannonFlash = CfgFx.Float("boss.phases.attacks.charged_cannon.flash", CannonFlash);
        SweepAim = CfgFx.Float("boss.phases.attacks.dash_sweep.aim", SweepAim);
        SweepSpeed = CfgFx.Float("boss.phases.attacks.dash_sweep.speed", SweepSpeed);
        SweepDropCount = CfgFx.Int("boss.phases.attacks.dash_sweep.drop_count", SweepDropCount);
        SweepDropSpeed = CfgFx.Float("boss.phases.attacks.dash_sweep.drop_speed", SweepDropSpeed);
        SweepDropDamage = CfgFx.Int("boss.phases.attacks.dash_sweep.drop_damage", SweepDropDamage);
        // 2026-08-10 健壮性审查：return_duration 钳下限——0 时 dash_sweep RETURN 段
        // _sweepTimer/该值除零（Clamp 兜底无 NaN，但 Boss 冲刺后全程钉在回退原点不动）
        SweepReturnDuration = CfgFx.Float("boss.phases.attacks.dash_sweep.return_duration", SweepReturnDuration, 0.05f);
        VolleyCount = CfgFx.Int("boss.phases.attacks.minion_volley.count", VolleyCount);
        VolleyDelay = CfgFx.Float("boss.phases.attacks.minion_volley.delay", VolleyDelay);
        VolleyBulletSpeed = CfgFx.Float("boss.phases.attacks.minion_volley.bullet_speed", VolleyBulletSpeed);
        VolleyBulletDamage = CfgFx.Int("boss.phases.attacks.minion_volley.bullet_damage", VolleyBulletDamage);
        WallCount = CfgFx.Int("boss.phases.attacks.bullet_wall.count", WallCount);
        WallBulletSpeed = CfgFx.Float("boss.phases.attacks.bullet_wall.bullet_speed", WallBulletSpeed);
        WallDamage = CfgFx.Int("boss.phases.attacks.bullet_wall.damage", WallDamage);
        WallArcDeg = CfgFx.Float("boss.phases.attacks.bullet_wall.arc_deg", WallArcDeg);
        // 差异化狂暴参数（boss.enrage.type_*）
        // R06：interval 类键钳下限（L 系列判型族登记遗留）——0/负值使狂暴攻击每帧触发风暴
        E1RingInterval = CfgFx.Float("boss.enrage.type_1.ring_interval", E1RingInterval, 0.05f);
        E1RingCount = CfgFx.Int("boss.enrage.type_1.ring_count", E1RingCount);
        E1RingSpeed = CfgFx.Float("boss.enrage.type_1.ring_speed", E1RingSpeed);
        E1RingPrecessionDeg = CfgFx.Float("boss.enrage.type_1.ring_precession_deg", E1RingPrecessionDeg);
        E1SalvoCharge = CfgFx.Float("boss.enrage.type_1.salvo_charge", E1SalvoCharge, 0.05f);
        E1SalvoCount = CfgFx.Int("boss.enrage.type_1.salvo_count", E1SalvoCount);
        E1SalvoSpeed = CfgFx.Float("boss.enrage.type_1.salvo_speed", E1SalvoSpeed);
        E1SalvoDamage = CfgFx.Int("boss.enrage.type_1.salvo_damage", E1SalvoDamage);
        // AB4：point_count 钳下限 4（同族 count 键 E1RingCount/E3RingCount 等均有 floor，
        // 独漏此项）——配 0 使 _attackIndex < E2PointCount 恒假，二型狂暴 ACTIVE 冻结
        E2PointCount = CfgFx.Int("boss.enrage.type_2.point_count", E2PointCount, 4);
        E2PointInterval = CfgFx.Float("boss.enrage.type_2.point_interval", E2PointInterval, 0.05f);
        E2Aim = CfgFx.Float("boss.enrage.type_2.aim", E2Aim);
        E2SniperSpeed = CfgFx.Float("boss.enrage.type_2.sniper_speed", E2SniperSpeed);
        E2SniperDamage = CfgFx.Int("boss.enrage.type_2.sniper_damage", E2SniperDamage);
        E2ReleaseRingCount = CfgFx.Int("boss.enrage.type_2.release_ring_count", E2ReleaseRingCount);
        E2ReleaseRingSpeed = CfgFx.Float("boss.enrage.type_2.release_ring_speed", E2ReleaseRingSpeed);
        E3SummonInterval = CfgFx.Float("boss.enrage.type_3.summon_interval", E3SummonInterval, 0.05f);
        // G024：三型普通阶段召唤间隔入配置（对齐狂暴 E3 键）
        // AC7（2026-08-11 健壮性审查）：孪生键 E3SummonInterval 已钳 ≥0.05（上方同族）；≤0 时
        // _summonTimer 每帧归零 → 每物理帧召唤风暴（SummonMinions 刷兵失控）
        _summonInterval = CfgFx.Float("boss.phases.type3.summon_interval", _summonInterval, 0.05f);
        _summonTimer = _summonInterval;
        E3SummonWaves = CfgFx.Int("boss.enrage.type_3.summon_waves", E3SummonWaves);
        E3SummonCount = CfgFx.Int("boss.enrage.type_3.summon_count", E3SummonCount);
        E3RingInterval = CfgFx.Float("boss.enrage.type_3.ring_interval", E3RingInterval, 0.05f);
        E3RingCount = CfgFx.Int("boss.enrage.type_3.ring_count", E3RingCount);
        E3RingSpeed = CfgFx.Float("boss.enrage.type_3.ring_speed", E3RingSpeed);
        E3ReleaseRingCount = CfgFx.Int("boss.enrage.type_3.release_ring_count", E3ReleaseRingCount);
        E3ReleaseRingSpeed = CfgFx.Float("boss.enrage.type_3.release_ring_speed", E3ReleaseRingSpeed);
        // 4 型「月蚀」（2026-08-04）
        RingBurstSpeed = CfgFx.Float("boss.ring_burst.bullet_speed", RingBurstSpeed);
        BulletDamageRing = CfgFx.Int("boss.bullet_damage.ring", BulletDamageRing);
        Move4BobAmp = CfgFx.Float("boss.movement.type4.bob_amp", Move4BobAmp);
        // W 系列（2026-08-09）：bob_period 下限 0.05——≤0 时 MoveType4 周期除零 → 相位 NaN → SinFast 越界（R06 同族，MoveBob 侧已有 0.01 保护）
        Move4BobPeriod = CfgFx.Float("boss.movement.type4.bob_period", Move4BobPeriod, 0.05f);
        E4RingCount = CfgFx.Int("boss.enrage.type_4.ring_count", E4RingCount);
        E4RingInterval = CfgFx.Float("boss.enrage.type_4.ring_interval", E4RingInterval, 0.05f);
        E4RingSpeed = CfgFx.Float("boss.enrage.type_4.ring_speed", E4RingSpeed);
        E4PrecessionDeg = CfgFx.Float("boss.enrage.type_4.precession_deg", E4PrecessionDeg);
        E4ReleaseRingCount = CfgFx.Int("boss.enrage.type_4.release_ring_count", E4ReleaseRingCount);
        E4ReleaseRingSpeed = CfgFx.Float("boss.enrage.type_4.release_ring_speed", E4ReleaseRingSpeed);
        _movement.SyncPressTimer(PressInterval);
        // W 系列（2026-08-09）：difficulty_scaling 判型+空表守卫——损坏类型/空数组时 AsGodotArray 得空表，
        // ApplyDifficultyScaling 的 Clamp(tier,0,Count-1) 返回 -1 → 越界 SCRIPT ERROR（Q14/R06 同族防呆口径，坏值保持默认）
        var diffInterval = GameState.Instance.Cfg("boss.difficulty_scaling.interval_mult", DiffIntervalMult);
        if (diffInterval.VariantType == Variant.Type.Array && diffInterval.AsGodotArray().Count >= 3)
        {
            DiffIntervalMult = diffInterval.AsGodotArray();
        }
        var diffSpeed = GameState.Instance.Cfg("boss.difficulty_scaling.speed_mult", DiffSpeedMult);
        if (diffSpeed.VariantType == Variant.Type.Array && diffSpeed.AsGodotArray().Count >= 3)
        {
            DiffSpeedMult = diffSpeed.AsGodotArray();
        }
        var diffCounts = GameState.Instance.Cfg("boss.difficulty_scaling.counts", DiffCountDeltas);
        if (diffCounts.VariantType == Variant.Type.Dictionary)
        {
            DiffCountDeltas = diffCounts.AsGodotDictionary();
        }
        LoadPatterns();
        ApplyDifficultyScaling();
        StartPattern();
    }

    public override void _ExitTree()
    {
        GameState.Instance.UnbindEnemy(this); // 统一解绑（docs/ENTITY_MANAGER.md）
        // C22：显式断开 buffs_changed 信号连接（重入树不重复连接）
        _slowCache.Disconnect(GameState.Instance);

        _enrageSequence.UnlockPlayer(); // 兜底：离场必复位玩家减速，不留残留（A3 归 EnrageSequence）
    }

    // ---------------- 对外公开接口（A1 修复） ----------------

    /// <summary>setup：类型/HP 初始化（_ready 之前调用，不用 @onready）。</summary>
    public void Setup(float pDifficulty, int pType)
    {
        // K12：p_type 越界钳制（公开接口）——保护下方 hp_mults[p_type-1] 与 TEXTURES[p_type-1]
        // 双双越界（H11 只校验了数组长度）；轮换扩 4 型（2026-08-04 月蚀）后上限放开为 4
        pType = Mathf.Clamp(pType, 1, 4);
        BossType = pType;
        // A3 收敛：召唤机标志字段化（原每物理帧 SummonerTypes.TryGetValue 查询；BossType 仅此处写入，Setup 固化）
        _isSummoner = SummonerTypes.TryGetValue(BossType, out var s) && s;
        // HP 四级乘算：基准 × 型别倍率 × Boss 击杀 ramp × 难度档（与敌机同源 0.75/1.0/1.5）
        // H11（健壮性审核）：hp_mults 长度/元素校验——短数组越界得 null→float 0.0 → Boss 免疫伤害静默
        // Q02（2026-08-05）：校验与回退数组随 4 型扩容——原 3 元素校验/回退在 json 缺键/截断时
        // 令 hp_mults[3] 越界 → max_hp=0 → type4 出生即免疫伤害（仅 50s 逃跑兜底）
        var hpMultsRaw = GameState.Instance.Cfg("boss.hp_mults", new Godot.Collections.Array { 1.3, 0.7, 1.6, 1.2 });
        var hpMultsValid = false;
        Godot.Collections.Array hpMultsArr = new();
        if (hpMultsRaw.VariantType == Variant.Type.Array)
        {
            hpMultsArr = hpMultsRaw.AsGodotArray();
            hpMultsValid = hpMultsArr.Count >= 4;
            if (hpMultsValid)
            {
                foreach (var v in hpMultsArr)
                {
                    // R06：正值域校验（L 系列判型族登记遗留）——0/负倍率经 float() 后
                    // max_hp≤0 → take_damage 首行早退 → Boss 出生即免疫伤害（与 Q02 同根因）
                    var num = v.VariantType == Variant.Type.Int ? (float)v.AsInt64() : v.AsDouble();
                    if (v.VariantType == Variant.Type.Bool
                        || !(v.VariantType == Variant.Type.Int || v.VariantType == Variant.Type.Float)
                        || (float)num <= 0.0f)
                    {
                        hpMultsValid = false;
                        break;
                    }
                }
            }
        }

        var hpMults = hpMultsValid ? hpMultsArr : new Godot.Collections.Array { 1.3, 0.7, 1.6, 1.2 };
        MaxHp = (float)GameState.Instance.Cfg("boss.hp_base", HpBase).AsDouble()
            * (float)hpMults[pType - 1].AsDouble()
            * pDifficulty
            * (float)GameState.Instance.EnemyHpMultiplier();
        Hp = MaxHp;
        // setup() 在 _ready() 之前调用，不能用 @onready 变量
        GetNode<Sprite2D>("Sprite2D").Texture = _bossTextures[pType - 1];
    }

    public bool IsInFight() => _inFight;

    public bool IsEscaping() => _escaping;

    public void AbortEnrageSequence() => _enrageSequence.Abort();

    /// <summary>狂暴态查询（A3：BossMovement/BossAttacks/EnrageSequence 经公开接口交互）。</summary>
    public bool IsEnraged() => _enraged;

    /// <summary>A6：语义化类型查询（调用方不再依赖 `is Boss` 具体类型）。</summary>
    public bool IsBoss() => true;

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------
    public int EnragePhaseValue() => _enrageSequence.Phase();

    public int GetEnragePhaseNone() => (int)EnragePhase.NONE;

    public int GetEnragePhaseTransition() => (int)EnragePhase.TRANSITION;

    public int GetEnragePhaseActive() => (int)EnragePhase.ACTIVE;

    public int GetEnragePhaseReleaseHold() => (int)EnragePhase.RELEASE_HOLD;

    public int GetFightPhaseTransition() => (int)FightPhase.P1;

    public int GetFightPhaseActive() => (int)FightPhase.P2;

    public int SweepStateValue() => (int)_attacks.SweepState();

    /// <summary>默认模式表公开访问（boss_registry_test 校验 balance.json 用；C# 静态经脚本资源可调）。</summary>
    public Godot.Collections.Dictionary GetDefaultPatterns() => _defaultPatterns;

    /// <summary>召唤表公开访问（boss_registry_test 校验用；System 字典转 Godot 字典）。</summary>
    public static Godot.Collections.Dictionary GetSummonerTypes()
    {
        var result = new Godot.Collections.Dictionary();
        foreach (var kv in SummonerTypes)
        {
            result[kv.Key] = kv.Value;
        }

        return result;
    }

    /// <summary>闪白时长表公开访问（boss_registry_test 校验用）。</summary>
    public static Godot.Collections.Dictionary GetHitFlashByType()
    {
        var result = new Godot.Collections.Dictionary();
        foreach (var kv in HitFlashByType)
        {
            result[kv.Key] = kv.Value;
        }

        return result;
    }

    public int FightPhaseValue() => (int)_fightPhase;

    /// <summary>A3：模式循环计时复位（BossAttacks 冲刺掠过 RETURN 结束调用）。</summary>
    public void ResetFireTimer()
    {
        _fireTimer = (float)CurrentPattern().GetValueOrDefault("interval", BaseFireInterval()).AsDouble();
    }

    /// <summary>A7：测试/诊断白盒断言经公开接口（命名语义化；返回纯 C# 类，不注册进引擎表，
    /// GDScript 侧不可经此链访问组件——组件经 C# 直取，测试随批次重定型）。
    /// 注：方法名避开类型名（CS0119 遮蔽）。</summary>
    public EnrageSequence GetEnrageSequence() => _enrageSequence;

    public BossAttacks Attacks() => _attacks;

    public BossFire FireTool() => _fire;

    public void SetFireTimer(float seconds) => _fireTimer = seconds;

    public float FireTimer() => _fireTimer;

    public void SetFightPhase(int pPhase) => _fightPhase = (FightPhase)pPhase;

    public void SetSummonTimer(float seconds) => _summonTimer = seconds;

    public void SetPatterns(Godot.Collections.Dictionary patternDict) => _patterns = patternDict;

    public Godot.Collections.Dictionary Patterns() => _patterns;

    public void SetPatternIndex(int index) => _patternIndex = index;

    public int PatternIndex() => _patternIndex;

    public void StartPattern() => StartPatternInternal();

    public Color BaseModulateColor() => BaseModulate();

    public void SetSurvival(float seconds) => _survival = seconds;

    public void SetInFight(bool fighting) => _inFight = fighting;

    public bool EscapeWarned() => _escapeWarned;

    public void BeginEscape() => BeginEscapeInternal();

    /// <summary>A5：spawner 依赖注入（A5 改注入 spawner；BossAttacks/EnrageSequence 经公开接口调用）。</summary>
    public void SetSpawner(Node spawner) => _spawner = spawner as Spawner;

    /// <summary>编队小怪召唤（BossAttacks/EnrageSequence 经公开接口调用；返回实例供调用方标记）。</summary>
    public Enemy? SpawnMinionAt(Vector2 pos)
    {
        if (_spawner == null || !GodotObject.IsInstanceValid(_spawner))
        {
            return null;
        }

        return _spawner!.SpawnMinion(pos);
    }

    public void TakeDamage(int amount, float scoreScale)
    {
        if (_escaping)
        {
            return; // G02：逃跑期不再受任何伤害——激光 _damage_tick/溅射 _splash 按注册表+距离判定
            // 绕开 collision_layer=0，此处统一拦截，防逃跑窗口内补刀致死触发击杀奖励
        }

        if (Hp <= 0.0f)
        {
            return; // 已死亡待释放（同帧多发命中防重复结算）
        }

        if (_enrageSequence.IsHealthLocked())
        {
            FlashHit(); // 锁血期：仅受击闪白反馈，不掉血不死（致死也不死）
            return;
        }

        Hp -= amount;
        _scoreScale = scoreScale;
        if (Hp > 0.0f && !_enraged && Hp < MaxHp * EnrageHpRatio)
        {
            Hp = MaxHp * EnrageHpRatio;
        }

        EmitSignal(SignalName.HealthChanged, Hp, MaxHp);
        FlashHit();
        if (Hp <= 0.0f)
        {
            Die();
        }
        else if (!_enraged && Hp <= MaxHp * EnrageHpRatio)
        {
            Enrage();
        }
        else if (_fightPhase == FightPhase.P1 && Hp <= MaxHp * Phase2HpRatio)
        {
            EnterPhase(FightPhase.P2);
        }
    }

    public void TakeDamage(int amount) => TakeDamage(amount, 1.0f);

    /// <summary>狂暴快照弹幕：狂暴进入时的一次性齐射（由 main 在子弹时间结束后统一触发）。
    /// 4 道激光向弹（高速长弹，复用敌弹 laser 型表现）+ 8 方向环形慢弹。委托 BossFire。</summary>
    public void FireEnrageSnapshot()
    {
        if (_escaping)
        {
            return;
        }

        _fire.FireEnrageWave(
            this,
            EnrageLaserSpeed,
            EnrageRingSpeed,
            BulletDamageSnapshotLaser,
            BulletDamageSnapshotRing,
            EnrageSnapshotLasers,
            EnrageSnapshotRing);
    }

    /// <summary>慢速力场因子（全局机体移速 ×0.8；与狂暴移速倍率相乘）。
    /// 母舰召唤减速带命中时叠加短时乘区（同语义，仅位移）。</summary>
    public float SlowFactor()
    {
        var f = _slowCache.Value ? SlowFieldFactor : 1.0f;
        if (_summonSlowTimer > 0.0f)
        {
            f *= _summonSlowFactor;
        }

        return f;
    }

    /// <summary>母舰召唤减速带命中：duration 秒内位移速度 ×factor。</summary>
    public void ApplySlow(float duration, float factor)
    {
        _summonSlowTimer = duration;
        _summonSlowFactor = factor;
    }

    /// <summary>逃跑剩余秒数（HUD 逃跑倒计时读取口，§4.5）。</summary>
    public float EscapeRemaining() => EscapeTime - _survival;

    /// <summary>逃跑警告期上飘偏移（BossMovement 绝对 y 赋值走位叠加用；未进警告期返回 0）。</summary>
    public float EscapeDriftOffset() => _survival >= EscapeTime - EscapeWarning ? _escapeDriftOffset : 0.0f;

    /// <summary>
    /// 战斗锚线 y：FIGHT_Y 为距可见区域顶缘的偏移，调用时实时取 view 基线
    /// （与 StrafeRange() 边距处理对齐；zoom=1 时 view.position.y=0，锚线 = FIGHT_Y 本身）。
    /// 注：此处不缓存——view_zoom_test 同帧切换视角档并做精确相等断言，必须实时读。
    /// </summary>
    public float FightAnchorY() => GameState.Instance.ViewWorldRect().Position.Y + FightY;

    /// <summary>
    /// 巡航范围随可见世界区域收窄（zoom=1 时与配置值 STRAFE_MIN_X/MAX_X 一致）。
    /// 右缘边距 = 设计宽 1920 − STRAFE_MAX_X = 300px，随 view.end.x 平移保持（view_zoom_test
    /// 断言 large 档 hi = view.end.x − 300；2026-08-05 P4 复核后保留原语义，1920 为设计宽度常量）。
    /// 实时读 view（见 FightAnchorY 注释）。
    /// </summary>
    public Vector2 StrafeRange()
    {
        var view = GameState.Instance.ViewWorldRect();
        var lo = view.Position.X + StrafeMinX;
        var hi = Mathf.Max(view.End.X - (1920.0f - StrafeMaxX), lo);
        return new Vector2(lo, hi);
    }

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        UpdateFlash(d);
        if (_summonSlowTimer > 0.0f)
        {
            _summonSlowTimer -= d;
        }

        if (_escaping)
        {
            // 逃跑离场：向上加速飘出屏幕（不再受弹、不再开火）
            _escapeSpeed += EscapeAccel * d;
            Position += new Vector2(0.0f, -_escapeSpeed * d);
            if (Position.Y < FrameCache.ViewRect().Position.Y - 280.0f) // G08：出界基线对齐 view_world_rect
            {
                EmitSignal(SignalName.Escaped);
                EmitSignal(SignalName.Died); // 离场通知（血条/生成器重排）；非击毁，无击杀奖励
                QueueFree();
            }

            return;
        }

        if (!_inFight)
        {
            Position += new Vector2(0.0f, EnterSpeed * SlowFactor() * d);
            if (Position.Y >= FightAnchorY()) // 逐帧求值，支持战斗中途切视角档
            {
                _inFight = true;
                EmitSignal(SignalName.HealthChanged, Hp, MaxHp);
            }

            return;
        }

        // 存活计时：50s 未被击杀则逃跑；最后 3s 警告 + 上飘
        _survival += d;
        if (_survival >= EscapeTime)
        {
            BeginEscapeInternal();
            return;
        }

        if (_survival >= EscapeTime - EscapeWarning && !_escapeWarned)
        {
            _escapeWarned = true;
            ShowEscapeWarning();
        }

        // 狂暴序列接管移动与开火（逃跑计时照常走，序列中到点照样逃跑；撞击判定保留）
        if (_enrageSequence.IsActive())
        {
            if (_survival >= EscapeTime - EscapeWarning)
            {
                _sprite.Modulate = (int)(_survival * 8.0) % 2 == 0 ? EscapeBlinkColor : BaseModulate();
            }

            _enrageSequence.Update(d, this);
            CheckBodyCollision();
            return;
        }

        if (_survival >= EscapeTime - EscapeWarning)
        {
            // 上飘双路径：增量式走位（type1 P1/type3 P1）直接减；绝对 y 赋值走位
            // （type1 P2/type3 P2/type4）经 _escape_drift_offset 累计后在 BossMovement 叠加
            Position += new Vector2(0.0f, -EscapeDrift * d);
            _escapeDriftOffset += EscapeDrift * d;
            _sprite.Modulate = (int)(_survival * 8.0) % 2 == 0 ? EscapeBlinkColor : BaseModulate();
        }

        // 冲刺掠过（二型 P2）接管移动与模式编排；否则走位 + 模式表循环
        if (_attacks.IsSweepActive())
        {
            _attacks.Update(d, this);
        }
        else
        {
            // 走位与攻击解耦（§4.1）：A3 委托 BossMovement
            _movement.Update(d, this);

            // 模式表循环：波间隔由当前模式给出，波次/时长播完切下一个
            // （狂暴「余怒」射速 ×1.3：计时器流速加快，§5.4）
            _fireTimer -= d * (_enraged ? EnrageRateMult : 1.0f);
            if (_fireTimer <= 0.0f)
            {
                var pattern = CurrentPattern();
                // AB3：运行期兜底——interval 钳下限 0.05（装入清洗外的入口防每帧攻击风暴）
                _fireTimer = Mathf.Max((float)pattern.GetValueOrDefault("interval", BaseFireInterval()).AsDouble(), 0.05f);
                _attacks.Execute((StringName)pattern.GetValueOrDefault("attack", new StringName()), this);
                if (!_patternIsDuration)
                {
                    _patternLeft -= 1.0f;
                }
            }

            if (_patternIsDuration)
            {
                _patternLeft -= d;
            }

            if (_patternLeft <= 0.0f)
            {
                AdvancePattern();
            }

            // 持续型攻击轮询（狙击 telegraph / 3 连发 / 蓄力重炮 / 编队齐射 / 冲刺掠过）
            _attacks.Update(d, this);
        }

        // 母舰型召唤小怪（独立计时，不占模式表；机型参数表驱动）
        if (_isSummoner)
        {
            _summonTimer -= d;
            if (_summonTimer <= 0.0f)
            {
                _summonTimer = _summonInterval; // G024：间隔入配置
                SummonMinions();
            }
        }

        CheckBodyCollision();
    }

    // ---------------- 阶段框架与模式表（§4.1） ----------------

    /// <summary>当前模式（ENRAGE「余怒」沿用 P2 表提速）。</summary>
    private Godot.Collections.Dictionary CurrentPattern()
    {
        var list = (Godot.Collections.Array)Patterns()[_fightPhase == FightPhase.P1 ? "p1" : "p2"];
        return (Godot.Collections.Dictionary)list[_patternIndex % list.Count];
    }

    /// <summary>进入当前模式：初始化波次/时长与首波间隔。</summary>
    private void StartPatternInternal()
    {
        var pattern = CurrentPattern();
        _patternIsDuration = !pattern.ContainsKey("waves");
        if (_patternIsDuration)
        {
            _patternLeft = (float)pattern.GetValueOrDefault("duration", 6.0).AsDouble();
        }
        else
        {
            _patternLeft = (float)pattern.GetValueOrDefault("waves", 1).AsInt64();
        }

        // AB3：运行期兜底——首装同样钳下限（装入清洗外的入口）
        _fireTimer = Mathf.Max((float)pattern.GetValueOrDefault("interval", BaseFireInterval()).AsDouble(), 0.05f);
    }

    private void AdvancePattern()
    {
        var list = (Godot.Collections.Array)Patterns()[_fightPhase == FightPhase.P1 ? "p1" : "p2"];
        _patternIndex = (_patternIndex + 1) % list.Count;
        StartPatternInternal();
    }

    /// <summary>P1→P2 段切换：0.6s 蓄力辉光 + 抖屏 + 变调音效 + 清自身开火计时（§4.1），模式表重置循环。</summary>
    private void EnterPhase(FightPhase pPhase)
    {
        _fightPhase = pPhase;
        _patternIndex = 0;
        StartPatternInternal();
        _fireTimer = PhaseShiftDuration; // 段切换蓄力期停火
        // C11 修复：段切换归零一型纵向下压偏移，避免 P2 以残留下压永久停在锚线下方
        _movement.ResetPress();
        // L14：段切换 y 平滑过渡——P1 增量式下压（一型 press / 三型 band）当前偏移未补偿，
        // P2 绝对赋值锚线会 1/4 屏瞬移；从当前 y 平滑追锚线（过渡期由 _move_bob 收敛）
        _movement.BeginBobSmooth(Position.Y);
        _attacks.CancelAll();
        TransitionCleanup(); // 机制三：转场清弹 + 玩家短暂无敌（公平感喘息）
        _attacks.ChargeGlow(this, PhaseShiftDuration);
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.enrage", 16.0).AsDouble() * 0.5);
        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION_BIG, -10.0, 0.7);
        EmitSignal(SignalName.PhaseChanged, (int)pPhase);
    }

    /// <summary>
    /// 阶段转场公平感清理（2026-08-03 机制三）：清全部活跃弹丸（含编队炸弹，复用
    /// main._on_orbital_struck 同款遍历）+ 给玩家短暂无敌。逃跑期不走本路径（_begin_escape
    /// 不经阶段切换）。低频（一局数次）直接遍历可接受，无逐帧轮询。无敌只增不减。
    /// </summary>
    private void TransitionCleanup()
    {
        if (!ClearOnShift)
        {
            return;
        }

        foreach (var child in GetParent().GetChildren())
        {
            // M3a 起 Bullet 为 C# 类（is 判定）；FormationBomb 仍为 GDScript 类，经脚本资源判定
            if (child is Bullet || child.GetScript().AsGodotObject() == _formationBombScript)
            {
                child.QueueFree();
            }
        }

        // M3c：Player 迁 C#，player_ref 恒为 Player（null 语义保留）
        var playerV = GameState.Instance.PlayerRef;
        if (playerV != null)
        {
            var player = (Player?)playerV;
            if (player != null && player.InvincibleRemaining() < TransitionInvincible)
            {
                player.SetInvincible(TransitionInvincible);
            }
        }
    }

    /// <summary>模式表载入：配置缺键/损坏时逐项回退脚本默认值（AGENTS.md 约定两者保持一致）。
    /// 注意：cfg 返回的是 GameState 缓存 JSON 的共享引用，必须深拷贝，
    /// 否则 _apply_difficulty_scaling 的 interval 乘算会污染缓存、叠加到后续 Boss 实例。</summary>
    private void LoadPatterns()
    {
        // Q03（2026-08-05）：clampi 随 4 型扩容放开——原钳为 3 时 DEFAULT_PATTERNS 键 4 死数据、
        // type4 配置损坏时静默回退三型（母舰）模式表，违背「脚本回退镜像 json」约定
        var defaults = (Godot.Collections.Dictionary)_defaultPatterns[Mathf.Clamp(BossType, 1, 4)];
        _patterns = (Godot.Collections.Dictionary)defaults.Duplicate(true);
        var cfgPatterns = GameState.Instance.Cfg("boss.phases.type" + BossType, defaults);
        if (cfgPatterns.VariantType != Variant.Type.Dictionary)
        {
            return;
        }

        var cfgDict = cfgPatterns.AsGodotDictionary();
        foreach (var key in new[] { "p1", "p2" })
        {
            var list = cfgDict.GetValueOrDefault(key, new Godot.Collections.Array());
            // L07（2026-08-03 审查）：元素级判型（G06 只判容器层）——混入非 Dictionary 元素
            // 时 _current_pattern() typed 返回运行时类型错误、pattern.has 崩溃；坏元素跳过，
            // 全坏时保留脚本默认表（「损坏回退默认」口径）；深拷贝同样逐元素隔离共享 JSON
            if (list.VariantType != Variant.Type.Array)
            {
                continue;
            }

            var cleaned = new Godot.Collections.Array();
            foreach (var raw in list.AsGodotArray())
            {
                if (raw.VariantType == Variant.Type.Dictionary)
                {
                    var pat = raw.AsGodotDictionary().Duplicate(true);
                    // AB3：装入时清洗 interval 下限（R06 口径 0.05）——≤0 使 _fireTimer 每帧重装
                    // 即触发攻击（波次模式 1 帧烧 1 波、时长模式连射至弹上限）
                    if (pat.GetValueOrDefault("interval", new Variant()).VariantType is Variant.Type.Int or Variant.Type.Float)
                    {
                        pat["interval"] = Mathf.Max((float)pat["interval"].AsDouble(), 0.05f);
                    }
                    cleaned.Add(pat);
                }
            }

            if (cleaned.Count > 0)
            {
                _patterns[key] = cleaned;
            }
        }
    }

    /// <summary>
    /// 难度分档统一应用（§4.4）：档位 = GameState.difficulty（easy/medium/hard → 索引 0/1/2），
    /// 在配置载入后一次性乘算。只作用于 Boss 攻击密度/速度：开火间隔 ×1.15/×1/×0.85、
    /// 弹速 ×0.9/×1/×1.1、弹数按 boss.difficulty_scaling.counts 逐参数增减；
    /// telegraph 时长、快照弹幕（main 编排）、HP/伤害、机体移速不动。
    /// </summary>
    private void ApplyDifficultyScaling()
    {
        var order = GameState.Instance.DIFFICULTY_ORDER;
        var diff = GameState.Instance.Difficulty;
        var tier = order.IndexOf(diff);
        if (tier < 0)
        {
            tier = 1;
        }

        var intervalMult = (float)DiffIntervalMult[Mathf.Clamp(tier, 0, DiffIntervalMult.Count - 1)].AsDouble();
        var speedMult = (float)DiffSpeedMult[Mathf.Clamp(tier, 0, DiffSpeedMult.Count - 1)].AsDouble();
        // 开火间隔：模式表 interval + 攻击内部节奏
        foreach (var phaseKey in Patterns().Keys)
        {
            var list = (Godot.Collections.Array)Patterns()[phaseKey];
            foreach (var raw in list)
            {
                var pattern = (Godot.Collections.Dictionary)raw;
                if (pattern.ContainsKey("interval"))
                {
                    pattern["interval"] = (float)pattern["interval"].AsDouble() * intervalMult;
                }
            }
        }

        for (var i = 0; i < FireIntervals.Count; i++)
        {
            FireIntervals[i] = (float)FireIntervals[i].AsDouble() * intervalMult;
        }

        CannonInterval *= intervalMult;
        EnrageAttackInterval *= intervalMult;
        E1RingInterval *= intervalMult;
        E2PointInterval *= intervalMult;
        E3SummonInterval *= intervalMult;
        // 2026-08-03 审计：三型普通阶段召唤间隔随难度分档（对齐 §8.3「各内部节奏 ×interval_mult」）；
        // 同步首唤计时，否则第一个召唤用 _ready 时的未分档间隔
        _summonInterval *= intervalMult;
        _summonTimer = _summonInterval;
        E3RingInterval *= intervalMult;
        // 2026-08-06 审计 M4：4 型「月蚀」狂暴分档补齐（E33 同族遗漏）——interval/speed/count
        // 三表原无 type4 行，狂暴参数三档恒定（easy 偏难、hard 偏易）；与 1/2/3 型同款乘区
        E4RingInterval *= intervalMult;
        // 弹速（不含 main 编排的快照激光/环弹）
        FanBulletSpeed *= speedMult;
        HomingBulletSpeed *= speedMult;
        SniperBulletSpeed *= speedMult;
        CrossBulletSpeed *= speedMult;
        CannonBulletSpeed *= speedMult;
        SweepDropSpeed *= speedMult;
        VolleyBulletSpeed *= speedMult;
        WallBulletSpeed *= speedMult;
        E1RingSpeed *= speedMult;
        E1SalvoSpeed *= speedMult;
        E2SniperSpeed *= speedMult;
        E2ReleaseRingSpeed *= speedMult;
        E3RingSpeed *= speedMult;
        E3ReleaseRingSpeed *= speedMult;
        // M4：4 型普通阶段 ring_burst 环弹速 + 狂暴双环/蓄力环阵弹速随难度档（对齐 §4.4 全弹速分档）
        RingBurstSpeed *= speedMult;
        E4RingSpeed *= speedMult;
        E4ReleaseRingSpeed *= speedMult;
        // 弹数：逐参数分档增减，按攻击语义钳制下限（A3：增量迁入 BossAttacks）；
        // ring_burst 例外：counts.ring_burst 为每档弹数绝对值（Q01），直接写入 ring_delta
        _attacks.FanDelta = CountDelta("fan", tier);
        _attacks.HomingDelta = CountDelta("homing", tier);
        _attacks.RingDelta = CountDelta("ring_burst", tier);
        CannonShots = Mathf.Max(1, CannonShots + CountDelta("cannon", tier));
        VolleyCount = Mathf.Max(1, VolleyCount + CountDelta("volley", tier));
        WallCount = Mathf.Max(6, WallCount + CountDelta("wall", tier));
        SweepDropCount = Mathf.Max(1, SweepDropCount + CountDelta("drops", tier));
        E1RingCount = Mathf.Max(4, E1RingCount + CountDelta("ring", tier));
        E3RingCount = Mathf.Max(4, E3RingCount + CountDelta("ring", tier));
        E2ReleaseRingCount = Mathf.Max(4, E2ReleaseRingCount + CountDelta("ring", tier));
        E3ReleaseRingCount = Mathf.Max(4, E3ReleaseRingCount + CountDelta("ring", tier));
        E1SalvoCount = Mathf.Max(4, E1SalvoCount + CountDelta("salvo", tier));
        E3SummonCount = Mathf.Max(1, E3SummonCount + CountDelta("summon", tier));
        // M4：4 型狂暴弹数分档（ring 增量 [-2,0,2]，同 1/3 型环弹口径；下限 4 防越界）
        E4RingCount = Mathf.Max(4, E4RingCount + CountDelta("ring", tier));
        E4ReleaseRingCount = Mathf.Max(4, E4ReleaseRingCount + CountDelta("ring", tier));
    }

    /// <summary>弹数分档取值：boss.difficulty_scaling.counts[key][tier]，缺键/越界回退 0。</summary>
    private int CountDelta(string key, int tier)
    {
        var d = DiffCountDeltas.GetValueOrDefault(key, new Godot.Collections.Array { 0, 0, 0 });
        if (d.VariantType == Variant.Type.Array)
        {
            var arr = d.AsGodotArray();
            if (arr.Count > 0)
            {
                return (int)arr[Mathf.Clamp(tier, 0, arr.Count - 1)].AsInt64();
            }
        }

        return 0;
    }

    /// <summary>慢速力场与狂暴移速倍率之间的基础开火间隔（DDA 降档拉长，不降弹数/收益）。</summary>
    private float BaseFireInterval()
    {
        // B 梯队（fair plan §8）：DDA 降档拉长 Boss 攻击间隔（不降弹数/收益，分数公平）
        var idx = Mathf.Clamp(BossType - 1, 0, FireIntervals.Count - 1);
        return (float)FireIntervals[idx].AsDouble() * (float)GameState.Instance.DdaFactor();
    }

    private void SummonMinions()
    {
        if (_spawner == null || !GodotObject.IsInstanceValid(_spawner))
        {
            return;
        }

        var count = (int)GD.RandRange(2, 3);
        for (var i = 0; i < count; i++)
        {
            _spawner.Call(
                "spawn_minion",
                Position + new Vector2((float)GD.RandRange(-80.0, 80.0), 110.0f) * _ws);
        }
    }

    /// <summary>受击闪白（锁血期复用；P1-2 手动衰减替代 Tween，高频命中零分配）。</summary>
    private void FlashHit()
    {
        // 游击型受击硬直（闪白）更短（机型参数表驱动）
        _flashTotal = HitFlashByType.TryGetValue(BossType, out var ft) ? ft : 0.1f;
        FlashFx.Hit(_sprite, ref _flashTimer, _flashTotal); // 受击闪白
    }

    /// <summary>P1-2：受击闪白逐帧衰减（lerp 回基地色调，狂暴态 _base_modulate 实时取色）。</summary>
    private void UpdateFlash(float delta)
    {
        if (_flashTimer <= 0.0f)
        {
            return;
        }

        FlashFx.Update(_sprite, ref _flashTimer, delta, _flashTotal, BaseModulate());
    }

    private Color BaseModulate() => _enraged ? EnrageBlinkColor : Colors.White;

    /// <summary>
    /// 身体撞击（对齐原作 boss_vs_player.py 逐帧轮询语义）：入场降入与逃跑离场阶段不判定；
    /// 玩家 -30 HP（受击无敌帧节流连撞，无敌结束仍重叠会再次命中），Boss 不掉血、不自毁。
    /// 2026-08-07 审计：重叠状态由 area_entered/exited 事件驱动标记（collision_mask=3 已含
    /// player Hitbox 层 1），此处仅 O(1) 标记守卫（替代原每物理帧 overlaps_area 空间查询）。
    /// </summary>
    private void CheckBodyCollision()
    {
        if (!_bodyContact)
        {
            return;
        }

        // M3c：Player 迁 C#，player_ref 恒为 Player
        var player = FrameCache.Player() as Player;
        if (player == null || !GodotObject.IsInstanceValid(player))
        {
            return;
        }

        // 撞体伤害随对局进程 ramp（与 Boss 弹同一系数）；补传撞体位置作伤害源方向（D8）
        var dmg = EnemyFx.RampCollisionDamage(CollisionDamage);
        player.TakeDamage(dmg, GlobalPosition);
    }

    /// <summary>2026-08-07 审计：体碰重叠标记（对齐 enemy.gd P0-2；判定交回 _physics_process 守卫）。</summary>
    private void OnAreaEntered(Area2D area)
    {
        if (!area.IsInGroup("player_hitbox"))
        {
            return;
        }

        _bodyContact = true;
    }

    /// <summary>2026-08-07 审计：离开玩家 Hitbox → 清除重叠标记（停止每帧重掷）。</summary>
    private void OnAreaExited(Area2D area)
    {
        if (area.IsInGroup("player_hitbox"))
        {
            _bodyContact = false;
        }
    }

    private void Enrage()
    {
        _enraged = true;
        _fightPhase = FightPhase.ENRAGE;
        // 中断进行中的常规攻击/telegraph，启动狂暴序列：锁血 30% 检查点 + 快照玩家位置 + 玩家减速
        // （狂暴数据初始化 + 锁血 + 玩家减速委托 EnrageSequence，A3）
        _attacks.CancelAll();
        TransitionCleanup(); // 机制三：ENRAGE 转场同款清弹 + 玩家短暂无敌
        var playerV = GameState.Instance.PlayerRef;
        var snapshot = playerV != null
            ? ((Node2D)playerV).GlobalPosition
            : FrameCache.ViewRect().GetCenter();
        _enrageSequence.Begin(this, snapshot, _bossSize);
        _sprite.Modulate = BaseModulate();
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.enrage", 16.0).AsDouble());
        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION_BIG, -6.0);
        EmitSignal(SignalName.PhaseChanged, (int)FightPhase.ENRAGE);
        EmitSignal(SignalName.Enraged);
    }

    private void Die()
    {
        _enrageSequence.Abort();
        GameState.Instance.AddBossKill(_scoreScale);
        // 吸血 buff：Boss 击杀同样触发（对齐原作 boss_manager 路径，每帧至多一次）
        GameState.Instance.TryLifesteal();
        // M3a 起 Explosion 为 C#，静态方法直接调用
        Explosion.SpawnBossSequence(GetParent(), GlobalPosition);
        EmitSignal(SignalName.Died);
        QueueFree();
    }

    /// <summary>逃跑警告：复用 HUD 警告横幅（不可用时退化为 print），最后 3s 机身闪烁见 _physics_process。</summary>
    private void ShowEscapeWarning()
    {
        var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
        if (hud != null)
        {
            hud.ShowWarning(Tr("BOSS_ESCAPE_WARNING"));
        }
        else
        {
            GD.Print("[BOSS] 逃跑警告：Boss 即将逃离战场");
        }
    }

    /// <summary>50s 未被击杀：逃跑（无 add_boss_kill / 加分 / 难度提升 / 轮换推进）。</summary>
    private void BeginEscapeInternal()
    {
        _enrageSequence.Abort(); // 序列中断：解血锁 + 复位减速 + 清 telegraph
        _attacks.CancelAll(); // R07：常规攻击中断（瞄准线/蓄力/齐射计时/拖弹点），防逃跑期残留攻击继续结算
        _escaping = true;
        IsEscaped = true;
        _escapeSpeed = EscapeStartSpeed;
        CollisionLayer = 0; // 离场阶段不再受弹
        CollisionMask = 0;
        _bodyContact = false; // 2026-08-07 审计：逃跑期监控关闭，重叠标记复位防残留
        _sprite.Modulate = BaseModulate();
        GD.Print($"[BOSS] 存活 {(int)EscapeTime}s 未被击杀，逃离战场（无击杀奖励）");
    }

    // ---------------- Fire_* 转发（Y 系列：生产调用已 typed 直调 BossFire；保留供测试契约——
    // HitLogicTest 经 Boss.FireFan 等触发发射，签名与 BossFire.cs 对应方法逐参一致） ----------------
    public void FireFan(Node2D boss, int pCount, float speed, int damage) => _fire.FireFan(boss, pCount, speed, damage);

    public void FireHoming(Node2D boss, Vector2 pOffset, float speed, int damage) => _fire.FireHoming(boss, pOffset, speed, damage);

    public void FireSniper(Node2D boss, Vector2 pDir, float speed, int damage) => _fire.FireSniper(boss, pDir, speed, damage);

    public void FireCross(Node2D boss, float speed, int damage) => _fire.FireCross(boss, speed, damage);

    public void FireHeavy(Node2D boss, Vector2 pDir, float pSpeed, int pDamage) => _fire.FireHeavy(boss, pDir, pSpeed, pDamage);

    public void FireRing(Node2D boss, int pCount, float pSpeed, int pDamage, float pOffset)
        => _fire.FireRing(boss, pCount, pSpeed, pDamage, pOffset);

    public void FireEnrageWave(
        Node2D boss, float laserSpeed, float ringSpeed, int laserDamage, int ringDamage, int laserCount, int ringCount)
        => _fire.FireEnrageWave(boss, laserSpeed, ringSpeed, laserDamage, ringDamage, laserCount, ringCount);

    public void FireBulletWall(Node2D boss, int count, float speed, int damage, float arcDeg)
        => _fire.FireBulletWall(boss, count, speed, damage, arcDeg);
}
