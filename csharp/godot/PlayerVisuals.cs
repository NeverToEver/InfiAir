using Godot;

namespace InfiAir;

/// <summary>
/// 玩家视觉职责聚合：尾焰、
/// 冲刺残影池、机身色调（弹反金/擦弹金/无敌闪烁）、受击点脉动、弹反盾视觉、擦弹闪光状态。
/// 组合委托模式（同 PlayerDamage/PlayerDash/PlayerParry）：不持有节点所有权，经 Player
/// 传入的节点引用操作；公开接口供 Player 帧驱动与外部（PlayerDash 残影入口）调用。
/// 拆分动机：Player 视觉与战斗逻辑解耦（DESIGN_BASELINE §2.3）。
/// 无信号/导出 → 纯 C# 类（不继承 GodotObject）；仅 Player 调用。Enemy.SinFast 为同命名空间
/// 静态方法，直接引用。
/// </summary>
public class PlayerVisuals
{
    /// <summary>冲刺残影小池（预建复用，替代逐次 new Sprite2D + Tween + queue_free）。</summary>
    private const int AfterimagePoolSize = 4;
    private const float AfterimageFadeTime = 0.3f;
    private static readonly Color AfterimageColor = new(1.0f, 0.72f, 0.34f, 0.5f);

    private Sprite2D _sprite = null!;
    private Vector2 _spriteScaleBase = Vector2.One; // 贴图设计缩放（Init 时捕获），冲刺弹跳的倍增基准
    private GpuParticles2D _thruster = null!;
    private Polygon2D _hitboxDot = null!;
    private Polygon2D _parryArc = null!;
    private Node2D _parryRim = null!; // 分段盾缘容器（能量格子节点，Modulate/Scale 级联）
    private Polygon2D _parryShine = null!;
    private Line2D _parryPulse = null!; // 激活金光一闪（白金圆环扩张淡出，一次性）

    /// <summary>弹反命中闪光剩余时长（白金色提亮 + 边缘外扩脉冲；SetParryFlash 置位）。</summary>
    private float _parryFlash;
    private const float ParryFlashTime = 0.18f;

    /// <summary>激活闪光环剩余时长（SetParryActivatePulse 置位）。</summary>
    private float _parryPulseTimer;
    private const float ParryPulseTime = 0.32f;

    /// <summary>弹反高光带顶点缓冲预分配（UpdateParryVisuals 每物理帧原地写，防 new Vector2[6]）。</summary>
    private readonly Vector2[] _parryShinePoly = new Vector2[6];

    // ---- 机体姿态（横移侧倾 + 开火后坐力，DESIGN_BASELINE §2.13）：只写贴图节点的
    // Rotation/Position，机体根节点与碰撞体不动；振幅乘动效强度（fx_intensity），0 = 关闭。
    private float _bankAngle;         // 当前侧倾角（rad，贴图本地，指数平滑逼近目标）
    private float _recoilAge = 10.0f; // 距上次开火的秒数（初值大于 3τ：开机无残余后坐力）
    private float _bankMax = 0.14f;   // effects.motion.player_bank_max_rad（Init 读入，已乘动效强度）
    private float _bankRate = 12.0f;  // effects.motion.player_bank_rate
    private float _recoilPx = 2.5f;   // effects.motion.player_recoil_px（已乘世界缩放与动效强度）
    private float _recoilTau = 0.09f; // effects.motion.player_recoil_tau

    // ---- 机体活性（运动滞后漂移 / 转向跟随 / 悬停浮动 / 冲刺弹跳，DESIGN_BASELINE §2.16）：
    // 与姿态层同口径——只写贴图节点的 Rotation/Position/Scale，机体根节点与碰撞体不动；
    // 振幅乘动效强度（fx_intensity），0 = 回到本批之前的画面。
    private float _lagPx = 4.0f;      // effects.motion.player_lag_px（已乘动效强度）
    private float _lagRate = 10.0f;   // effects.motion.player_lag_rate
    private float _lagX;              // 滞后漂移当前偏移（root 本地 x，指数平滑逼近目标）
    private float _lagY;
    private float _swayMax = 0.18f;   // effects.motion.player_sway_max_rad（已乘动效强度）
    private float _swayRate = 10.0f;  // effects.motion.player_sway_rate
    private float _sway;              // 转向跟随当前角（rad，accumulated + 衰减，钳 ±max）
    private float _bobPx = 1.6f;      // effects.motion.player_bob_px（已乘动效强度）
    private float _bobHz = 0.6f;      // effects.motion.player_bob_hz
    private float _popAmp = 0.06f;    // effects.motion.player_dash_pop_scale（已乘动效强度）
    private float _popTime = 0.16f;   // effects.motion.player_dash_pop_time
    private float _popAge = 10.0f;    // 距冲刺置位的秒数（初值出窗：开机无残余弹跳）

    // ---- 尾焰油门平滑（三态档位切换指数过渡 + 升档增亮 kick）：rate 钳 0 = 关闭（直跳旧行为），
    // kick 幅度/时长为就地 const（§2.8 口径：小动效不新增 balance 键）。
    private float _thrusterRate = 9.0f; // effects.motion.player_thruster_rate
    private (float Speed, float Amount, float Alpha) _thrusterCur;
    private bool _thrusterHasState;     // 首帧直取目标（无上一帧可平滑）
    private float _thrusterKickAge = 10.0f;
    private const float ThrusterKickAmp = 0.35f;
    private const float ThrusterKickTime = 0.15f;

    // ---- 机身反馈与损伤状态（DESIGN_BASELINE §2.17）：开火机身光、速度伸缩、受击压缩、
    // 损伤烟与引擎喘振、导航灯、机动喷口。与 §2.13/§2.16 同口径——只写贴图节点及其子节点
    // 的变换/调制，机体根节点与碰撞体不动；振幅乘动效强度（fx_intensity），0 = 本批之前画面。
    private float _fireLightAmp = 0.35f;  // effects.motion.player_fire_light_amp（已乘动效强度）
    private float _fireLightTau = 0.07f;  // effects.motion.player_fire_light_tau
    private float _stretchMax = 0.08f;    // effects.motion.player_stretch_max（已乘动效强度）
    private float _stretchRate = 10.0f;   // effects.motion.player_stretch_rate
    private float _stretch;               // 当前速度伸缩量（正=沿机头拉伸），指数平滑逼近
    private float _hitSquashAmp = 0.12f;  // effects.motion.player_hit_squash（已乘动效强度）
    private float _hitSquashTau = 0.10f;  // effects.motion.player_hit_squash_tau
    private float _hitAge = 10.0f;        // 距上次受击的秒数（初值大于 3τ：开机无残余压缩）
    private float _sputterAmp = 0.35f;    // effects.motion.player_sputter_amp（已乘动效强度）
    private float _sputterHz = 7.0f;      // effects.motion.player_sputter_hz
    private int _damageLevel;             // 受击帧等级（0 正常 / 1 轻伤 / 2 重伤；SetDamageLevel 置位）
    private float _smokeRatio = 0.6f;     // effects.motion.player_damage_smoke_ratio（已乘动效强度）
    private float _smokeCur;              // 损伤烟当前排放比（指数平滑，防开关跳变）
    private GpuParticles2D? _smoke;
    private float _nozzleAmp = 0.45f;     // effects.motion.player_nozzle_alpha（已乘动效强度）
    private Sprite2D? _nozzleLeft;        // 机动喷口（RCS 语汇：推力反向点火）
    private Sprite2D? _nozzleRight;
    private Sprite2D? _nozzleRetro;
    private float _nozzleLeftA;           // 三喷口当前 alpha（指数平滑；余量随加速度）
    private float _nozzleRightA;
    private float _nozzleRetroA;
    private Sprite2D? _navPort;           // 航行灯（左红/右绿常亮微呼吸 + 尾部白色双闪频闪）
    private Sprite2D? _navStarboard;
    private Sprite2D? _navStrobe;
    private float _navAmp = 0.5f;         // 航行灯亮度上限（已乘动效强度）
    private static readonly Color NavPortColor = new(1.0f, 0.28f, 0.22f);
    private static readonly Color NavStarboardColor = new(0.35f, 1.0f, 0.5f);
    private static readonly Color NavStrobeColor = new(1.0f, 0.98f, 0.9f);
    private const float NavStrobePeriod = 1.8f;  // 双闪周期（s）
    private const float NavStrobeFlash = 0.07f;  // 单次闪光时长（s）
    private const float NavStrobeGap = 0.16f;    // 双闪间隔（s）
    private const float NavBreathHz = 0.38f;     // 左右航行灯呼吸频率（Hz）
    private const float StretchCrossRatio = 0.5f; // 速度伸缩的交叉轴补偿比例（面积近似守恒）
    private const float HitSquashCrossRatio = 0.7f; // 受击压缩的交叉轴补偿比例（横向鼓、纵向扁）
    private bool _scaleDirty;             // 上一帧写入了非基准缩放（退出时补写一次归位）

    /// <summary>机体底色（暖族提亮，DESIGN_BASELINE §2.1/§2.3 的战术琥珀）：全站唯一一份——
    /// Player._Ready 的初值与 UpdateFrame 的每帧写共用本常量（两份同名常量分叉时，运行时生效的是
    /// 每帧写的那份，初值侧被静默架空；原先的冷色是全息青退役残留）。</summary>
    public static readonly Color BodyTintBase = new(1.42f, 1.34f, 1.24f);

    // ---- 机体形态层（DESIGN_BASELINE §2.18）：机体的**形状**随状态改变。此前所有表现都是给
    // 同一副机形加光加色加位移，玩家因此读不出「现在在干什么」；本层让轮廓本身变。
    // 与 §2.13/§2.16/§2.17 同口径——只写贴图节点及其子节点的变换 / 颜色 / 显隐，
    // 机体根节点、碰撞圆、擦弹环、弹道判定逐位不动；振幅乘动效强度（fx_intensity），
    // 0 ＝ 整层不出现（回到本批之前的画面）。无新增贴图，全部程序化构建。
    private Node2D? _rigRoot;             // 形态层容器（整层显隐由动效强度一处控制）
    private Polygon2D? _gunBarrelLeft;    // 炮管：伸缩只改 Scale.Y（顶点预分配，零分配）
    private Polygon2D? _gunBarrelRight;
    private Sprite2D? _gunMuzzleLeft;     // 炮口辉光：随热量与开火点亮
    private Sprite2D? _gunMuzzleRight;
    private Sprite2D? _ventLeft;          // 散热排气口（停火后放热、弹反时倾倒能量）
    private Sprite2D? _ventRight;
    private Sprite2D? _vortexLeft;        // 翼尖涡流（高 G 机动时才出现）
    private Sprite2D? _vortexRight;

    /// <summary>机体形态层数值（effects.motion.player_gun_* / player_vortex_*，Init 读入）。</summary>
    private float _gunDeployRate = 16.0f;   // 伸出速率（1/s，指数逼近）
    private float _gunRetractRate = 5.5f;   // 收拢速率（伸出快、收回慢：开火要跟手，收枪可以缓）
    private float _gunHoldTime = 0.55f;     // 停火后保持伸出的时长（s）
    private float _gunHeatPerShot = 0.14f;  // 每发升温
    private float _gunHeatTau = 1.1f;       // 停火后降温时间常数（s）
    private float _ventThreshold = 0.25f;   // 散热排气起始热度
    private float _ventAlpha = 0.55f;       // 排气峰值 alpha（已乘动效强度）
    private float _vortexAlpha = 0.40f;     // 翼尖涡流峰值 alpha（已乘动效强度）
    private float _vortexThreshold = 0.35f; // 涡流起始横向加速度占比
    private float _deploy;                  // 0..1 机炮伸出量（指数逼近）
    private float _heat;                    // 0..1 炮管热量（开火累加、停火指数衰减）
    private float _fireHoldAge = 10.0f;     // 距最近一次开火的秒数（初值出窗：开机为收拢冷态）
    private bool _dashTuck;                 // 冲刺气动收拢（冲刺期物理早退，故由 Player 显式置位）
    private float _rigAccelX;               // 物理帧缓存的横向加速度占比（渲染帧推涡流用）
    private float _vortex;                  // 0..1 涡流当前强度（指数平滑，防方向抖动闪烁）
    private float _ventBurst = 10.0f;       // 弹反能量倾倒的一次性排气剩余时长（初值出窗）
    private float _gunBarrelLen;            // 炮管全长（贴图像素，构建期捕获：炮口定位用）
    private const float BarrelRetractRatio = 0.28f; // 收拢态炮管残留长度比（炮管不缩成零，留枪根）
    private const float GunPodHalfW = 7.0f; // 荚舱体半宽（贴图像素）
    private const float GunPodHalfH = 8.0f; // 荚舱体半高（贴图像素）
    private const float GunPodRimPx = 1.4f; // 荚舱描边宽（贴图像素）
    private const float GunBarrelHalfW = 2.0f; // 炮管半宽（贴图像素）
    private const float GunMuzzleSize = 15.0f; // 炮口辉光直径（贴图像素）
    private const float HeatJitterPx = 0.7f;   // 高热的炮管抖动幅度（贴图像素；就地 const 档）
    private const float HeatJitterHz = 13.0f;  // 抖动频率（Hz）：确定性双正弦，无随机源
    private const float HeatJitterMin = 0.6f;  // 起抖热度门槛
    private const float VentBurstTime = 0.32f; // 弹反倾倒的排气管持续时长（s）
    private const float VentFlickerHz = 9.0f;  // 排气闪烁频率（Hz）
    private const float VortexRate = 7.0f;     // 涡流强度平滑速率（1/s）
    private const float VortexWidthPx = 14.0f; // 涡流拖尾宽度（贴图像素；长度随强度变）
    private static readonly Color GunHullDark = new(0.24f, 0.215f, 0.19f); // 荚舱体（暗于机体，读作加装件）
    private static readonly Color GunRim = new(0.80f, 0.50f, 0.16f);      // 荚舱描边（琥珀镶边，接机体的走线语言）
    private static readonly Color GunSteel = new(0.40f, 0.36f, 0.31f);    // 冷态炮管（暖钢灰）
    private static readonly Color GunAmber = new(1.0f, 0.60f, 0.18f);     // 中温琥珀
    private static readonly Color GunWhite = new(1.0f, 0.95f, 0.84f);     // 白热
    private static readonly Color VentColor = new(1.0f, 0.52f, 0.16f);    // 排气暖橙
    private static readonly Color VortexColor = new(0.78f, 0.90f, 1.0f);  // 翼尖蒸气（冷白，与暖色机体对比）

    /// <summary>擦弹机身短闪光剩余时长（金色微闪，独立短计时；SetGrazeFlash 置位、UpdateFrame 递减）。</summary>
    private float _grazeFlash;
    private readonly System.Collections.Generic.List<Sprite2D> _afterimagePool = new();
    private int _afterimageIdx;
    private readonly System.Collections.Generic.List<Sprite2D> _activeAfterimages = new();

    // ---- 核心喷口三层软点（白芯/琥珀/红外，additive；叠加在 GpuParticles 尾焰之上） ----
    private Sprite2D? _flareCore;
    private Sprite2D? _flareMid;
    private Sprite2D? _flareOuter;
    // 数值 effects.thruster_core.*（Init 一次性读入，尺寸已乘全局缩放）
    private float _flareCoreSize = 12.0f;
    private float _flareMidSize = 24.0f;
    private float _flareOuterSize = 38.0f;
    private float _flareMidY = 6.0f;
    private float _flareOuterY = 14.0f;
    private float _flareSpeedStretch = 0.4f;
    private float _flareJitterPx = 1.5f;
    private float _flareAlphaCore = 0.85f;
    private float _flareAlphaMid = 0.5f;
    private float _flareAlphaOuter = 0.28f;
    private static readonly Color FlareCoreColor = new(1.0f, 0.96f, 0.88f); // 白芯
    private static readonly Color FlareMidColor = new(1.0f, 0.62f, 0.16f);   // 琥珀
    private static readonly Color FlareOuterColor = new(0.85f, 0.22f, 0.05f); // 红外

    /// <summary>
    /// 初始化：接收节点引用 + 预建残影池。world_root = Main（残影固定世界坐标，不随玩家移动；
    /// Main 场景构建期 add_child 会报 "busy setting up children"，延迟到帧末执行）。
    /// </summary>
    public void Init(
        Sprite2D sprite, GpuParticles2D thruster, Polygon2D hitboxDot, Polygon2D parryArc, Node2D parryRim,
        Polygon2D parryShine, Line2D parryPulse, Node worldRoot)
    {
        Init(sprite, thruster, hitboxDot, parryArc, parryRim, parryShine, parryPulse, worldRoot, AfterimagePoolSize);
    }

    public void Init(
        Sprite2D sprite, GpuParticles2D thruster, Polygon2D hitboxDot, Polygon2D parryArc, Node2D parryRim,
        Polygon2D parryShine, Line2D parryPulse, Node worldRoot, int poolSize)
    {
        _sprite = sprite;
        _thruster = thruster;
        _hitboxDot = hitboxDot;
        _parryArc = parryArc;
        _parryRim = parryRim;
        _parryShine = parryShine;
        _parryPulse = parryPulse;
        for (var i = 0; i < poolSize; i++)
        {
            var ghost = new Sprite2D { Visible = false, Modulate = AfterimageColor };
            worldRoot.CallDeferred(Node.MethodName.AddChild, ghost);
            _afterimagePool.Add(ghost);
        }

        BuildThrusterFlare();
        var fx = FxIntensity();
        _bankMax = CfgFx.Float("effects.motion.player_bank_max_rad", _bankMax, 0.0f) * fx;
        _bankRate = CfgFx.Float("effects.motion.player_bank_rate", _bankRate, 0.0f);
        var ws = (float)GameState.Instance.WorldScale;
        _recoilPx = CfgFx.Float("effects.motion.player_recoil_px", _recoilPx, 0.0f) * ws * fx;
        // tau 下限取 0 而非 IntervalFloor：tau=0 是「关闭后坐力」的合法口径（RecoilFactor 对 tau≤0 返回 0），
        // 钳到 0.05 会把「关闭」误变成「极快回弹」。
        _recoilTau = CfgFx.Float("effects.motion.player_recoil_tau", _recoilTau, 0.0f);
        // 机体活性（§2.16）：振幅乘动效强度（0 = 本批之前画面）；频率/速率不乘（缩振幅不改频率，
        // §2.12 语义）。贴图基准缩放在 Init 时已被 Player.LoadBalance 写为设计值（0.65×ws），
        // 冲刺弹跳以它为基准做倍增，避免每帧重算世界缩放。
        _lagPx = CfgFx.Float("effects.motion.player_lag_px", _lagPx, 0.0f) * fx;
        _lagRate = CfgFx.Float("effects.motion.player_lag_rate", _lagRate, 0.0f);
        _swayMax = CfgFx.Float("effects.motion.player_sway_max_rad", _swayMax, 0.0f) * fx;
        _swayRate = CfgFx.Float("effects.motion.player_sway_rate", _swayRate, 0.0f);
        _bobPx = CfgFx.Float("effects.motion.player_bob_px", _bobPx, 0.0f) * fx;
        _bobHz = CfgFx.Float("effects.motion.player_bob_hz", _bobHz, 0.0f);
        _popAmp = CfgFx.Float("effects.motion.player_dash_pop_scale", _popAmp, 0.0f) * fx;
        _popTime = CfgFx.Float("effects.motion.player_dash_pop_time", _popTime, 0.0f);
        _thrusterRate = CfgFx.Float("effects.motion.player_thruster_rate", _thrusterRate, 0.0f);
        // 弹跳初始即「已出窗」（time+1）：即使误配超长窗（≥10s）也不会在开机时把机体弹一下
        _popAge = _popTime + 1.0f;
        // 机身反馈与损伤状态（§2.17）：同 §2.16 口径（振幅乘动效强度、频率/速率不乘）
        _fireLightAmp = CfgFx.Float("effects.motion.player_fire_light_amp", _fireLightAmp, 0.0f) * fx;
        _fireLightTau = CfgFx.Float("effects.motion.player_fire_light_tau", _fireLightTau, 0.0f);
        _stretchMax = CfgFx.Float("effects.motion.player_stretch_max", _stretchMax, 0.0f) * fx;
        _stretchRate = CfgFx.Float("effects.motion.player_stretch_rate", _stretchRate, 0.0f);
        _hitSquashAmp = CfgFx.Float("effects.motion.player_hit_squash", _hitSquashAmp, 0.0f) * fx;
        _hitSquashTau = CfgFx.Float("effects.motion.player_hit_squash_tau", _hitSquashTau, 0.0f);
        _sputterAmp = CfgFx.Float("effects.motion.player_sputter_amp", _sputterAmp, 0.0f) * fx;
        _sputterHz = CfgFx.Float("effects.motion.player_sputter_hz", _sputterHz, 0.0f);
        _smokeRatio = CfgFx.Float("effects.motion.player_damage_smoke_ratio", _smokeRatio, 0.0f, 1.0f) * fx;
        _nozzleAmp = CfgFx.Float("effects.motion.player_nozzle_alpha", _nozzleAmp, 0.0f, 1.0f) * fx;
        _navAmp = 0.5f * fx;
        // 机体形态层（§2.18）：同上口径——振幅乘动效强度、频率/速率不乘
        _gunDeployRate = CfgFx.Float("effects.motion.player_gun_deploy_rate", _gunDeployRate, 0.0f);
        _gunRetractRate = CfgFx.Float("effects.motion.player_gun_retract_rate", _gunRetractRate, 0.0f);
        _gunHoldTime = CfgFx.Float("effects.motion.player_gun_hold_time", _gunHoldTime, 0.0f);
        _gunHeatPerShot = CfgFx.Float("effects.motion.player_gun_heat_per_shot", _gunHeatPerShot, 0.0f);
        _gunHeatTau = CfgFx.Float("effects.motion.player_gun_heat_tau", _gunHeatTau, 0.0f);
        _ventThreshold = CfgFx.Float("effects.motion.player_gun_vent_threshold", _ventThreshold, 0.0f, 1.0f);
        _ventAlpha = CfgFx.Float("effects.motion.player_gun_vent_alpha", _ventAlpha, 0.0f, 1.0f) * fx;
        _vortexAlpha = CfgFx.Float("effects.motion.player_vortex_alpha", _vortexAlpha, 0.0f, 1.0f) * fx;
        _vortexThreshold = CfgFx.Float("effects.motion.player_vortex_threshold", _vortexThreshold, 0.0f, 1.0f);
        _fireHoldAge = _gunHoldTime + 1.0f; // 初始即出窗：开机是收拢冷态，不弹一下枪
        BuildDamageSmoke();
        BuildManeuverNozzles();
        BuildNavLights();
        BuildHullRig();
        _spriteScaleBase = sprite.Scale;
    }

    /// <summary>动效强度（0..1）：设置项 fx_intensity 的每帧直读（取值口单源在设置服务）。</summary>
    private static float FxIntensity() => (float)GameState.Instance.FxIntensity;

    /// <summary>开火后坐力置位（FireInternal 每发调用）：重置后坐计时，贴图向机尾回弹由 UpdateFrame 推进。
    /// 机身光反馈（§2.17）复用同一计时——开火瞬间机体被枪口火光照亮一瞬。
    /// 形态层（§2.18）同点置位：机炮伸出保持计时归零 + 炮管升温。</summary>
    public void NotifyFired()
    {
        _recoilAge = 0.0f;
        _fireHoldAge = 0.0f;
        _heat = (float)Core.Visual.HullRig.HeatAfterShot(_heat, _gunHeatPerShot);
    }

    /// <summary>冲刺气动收拢置位（Player 冲刺期间每物理帧下发）：冲刺把机炮收回荚舱，
    /// 读作「收枪加速」。冲刺两侧物理帧都早退、渲染帧照走，故收放由本标志而非物理帧状态驱动。</summary>
    public void SetDashTuck(bool tucked) => _dashTuck = tucked;

    /// <summary>弹反能量倾倒置位（Player 弹反成功路径调用）：盾把吸收的能量从散热口放掉——
    /// 炮管热量清零 + 排气口一次性猛喷（§2.18 ③把散热口接到防御动作上）。</summary>
    public void NotifyParryDischarge()
    {
        _heat = 0.0f;
        _ventBurst = 0.0f;
    }

    /// <summary>受击置位（扣血生效路径）：贴图横向压扁回弹（受击压缩，§2.17）。</summary>
    public void NotifyHit() => _hitAge = 0.0f;

    /// <summary>受击帧等级下发（Player.UpdateDamageFrame 变化时调用）：重伤档开启损伤烟与引擎喘振。</summary>
    public void SetDamageLevel(int level) => _damageLevel = level;

    /// <summary>损伤烟粒子（重伤档点亮）：暗灰软点、自机身后方慢速飘散，普通混合（非加色——
    /// 烟是遮挡不是发光）。排放比由 UpdateScale 按伤害等级平滑驱动（0 = 停发）。</summary>
    private void BuildDamageSmoke()
    {
        var mat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0.0f, 1.0f, 0.0f),
            Spread = 32.0f,
            InitialVelocityMin = 26.0f,
            InitialVelocityMax = 70.0f,
            DampingMin = 24.0f,
            DampingMax = 60.0f,
            ScaleMin = 22.0f / CinematicFx.SoftTexSize,
            ScaleMax = 46.0f / CinematicFx.SoftTexSize,
            ColorRamp = SmokeRamp(),
        };
        _smoke = new GpuParticles2D
        {
            Texture = CinematicFx.SoftTexture(),
            Position = new Vector2(
                (float)Core.Visual.PlayerHullLayout.DamageSmoke.X,
                (float)Core.Visual.PlayerHullLayout.DamageSmoke.Y), // 机身后段：烟从尾段冒出，不遮机头
            Amount = 12,
            Lifetime = 1.1f,
            AmountRatio = 0.0f,
            Emitting = false,
            ProcessMaterial = mat,
            ZIndex = -2, // 垫在机体与 GlowLayer 之下：烟从机身后面冒
        };
        _sprite.AddChild(_smoke);
    }

    /// <summary>烟色阶：中灰渐暗、两端透明（加色辉光场景里靠不透明度变化读作「冒烟」）。</summary>
    private static GradientTexture1D SmokeRamp()
    {
        var g = new Gradient
        {
            Offsets = new[] { 0.0f, 0.25f, 1.0f },
            Colors = new[]
            {
                new Color(0.42f, 0.40f, 0.38f, 0.0f),
                new Color(0.34f, 0.32f, 0.30f, 0.55f),
                new Color(0.20f, 0.19f, 0.18f, 0.0f),
            },
        };
        return new GradientTexture1D { Gradient = g };
    }

    /// <summary>机动喷口三枚（RCS 语汇，§2.17）：左右两枚贴机身侧缘、机首一枚朝前（反推）。
    /// 加色软点、挂在贴图下（随机体姿态/伸缩联动）；alpha 由 UpdateFrame 按机体本地系加速度
    /// 逐帧驱动——推力永远与加速度反向点火（向右加速 → 左喷口亮，制动 → 机首反推亮）。
    /// 坐标与尺寸取自 core `PlayerHullLayout`（贴图像素；单位口径见 MakeHullLight）。</summary>
    private void BuildManeuverNozzles()
    {
        _nozzleLeft = MakeHullLight(Core.Visual.PlayerHullLayout.RcsLeft, new Color(1.0f, 0.72f, 0.35f));
        _nozzleRight = MakeHullLight(Core.Visual.PlayerHullLayout.RcsRight, new Color(1.0f, 0.72f, 0.35f));
        _nozzleRetro = MakeHullLight(Core.Visual.PlayerHullLayout.RcsRetro, new Color(1.0f, 0.78f, 0.42f));
    }

    /// <summary>航行灯三枚（§2.17）：左舷红 / 右舷绿（慢呼吸常亮）+ 尾部白色双闪频闪。
    /// 贴图下挂载、加色小点（面积远低于 XAG 118 的 20% 屏线，不构成「闪」）；相位取模拟时间
    /// （无头固定步长可重复）。坐标与尺寸取自 core `PlayerHullLayout`——两灯分居主翼外段，
    /// 「左红右绿」这对读数由布局表与单测共同钉住（单位口径见 MakeHullLight）。</summary>
    private void BuildNavLights()
    {
        _navPort = MakeHullLight(Core.Visual.PlayerHullLayout.NavPort, NavPortColor);
        _navStarboard = MakeHullLight(Core.Visual.PlayerHullLayout.NavStarboard, NavStarboardColor);
        _navStrobe = MakeHullLight(Core.Visual.PlayerHullLayout.NavStrobe, NavStrobeColor);
    }

    /// <summary>机体形态层（§2.18）一次性构建：机炮荚舱 ×2、散热排气口 ×2、翼尖涡流 ×2，
    /// 全部挂在贴图下的同一容器里（坐标取自 core `PlayerHullLayout`，贴图像素；
    /// 单位口径见 MakeHullLight）。整层显隐由动效强度一处控制——本层含不透明几何（荚舱与炮管），
    /// alpha 归零不足以让它「回到本批之前的画面」，必须整层隐藏。</summary>
    private void BuildHullRig()
    {
        _rigRoot = new Node2D();
        _sprite.AddChild(_rigRoot);
        _gunBarrelLen = (float)Core.Visual.PlayerHullLayout.GunLeft.Size;
        BuildGunPod(Core.Visual.PlayerHullLayout.GunLeft, out var barrelL, out var muzzleL);
        _gunBarrelLeft = barrelL;
        _gunMuzzleLeft = muzzleL;
        BuildGunPod(Core.Visual.PlayerHullLayout.GunRight, out var barrelR, out var muzzleR);
        _gunBarrelRight = barrelR;
        _gunMuzzleRight = muzzleR;
        _ventLeft = BuildVent(Core.Visual.PlayerHullLayout.VentLeft);
        _ventRight = BuildVent(Core.Visual.PlayerHullLayout.VentRight);
        _vortexLeft = BuildVortex(Core.Visual.PlayerHullLayout.WingtipLeft);
        _vortexRight = BuildVortex(Core.Visual.PlayerHullLayout.WingtipRight);
    }

    /// <summary>机炮荚舱：舱体（不透明多边形）+ 炮管（Y 缩放表达伸缩）+ 炮口辉光（加色软点）。
    /// 炮管多边形按**全长**预分配、伸缩只改 Scale.Y——收放是逐帧量，重建顶点等于逐帧分配。
    /// 炮管局部 y ∈ [0, -size]：缩放锚在荚舱原点，缩短时枪口向舱体回收（真枪管缩进荚舱的读法）。</summary>
    private void BuildGunPod(Core.Visual.HullAnchor anchor, out Polygon2D barrel, out Sprite2D muzzle)
    {
        var root = new Node2D { Position = new Vector2((float)anchor.X, (float)anchor.Y) };
        // 描边层（略大一圈的琥珀多边形）垫在舱体之下：机体的美术语言是「暗板 + 琥珀走线」，
        // 一块纯灰方块贴上去读作占位块而非机件
        root.AddChild(new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-GunPodHalfW - GunPodRimPx, -GunPodHalfH - GunPodRimPx),
                new Vector2(GunPodHalfW + GunPodRimPx, -GunPodHalfH - GunPodRimPx),
                new Vector2(GunPodHalfW + GunPodRimPx, GunPodHalfH + GunPodRimPx),
                new Vector2(-GunPodHalfW - GunPodRimPx, GunPodHalfH + GunPodRimPx),
            },
            Color = GunRim,
        });
        var body = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-GunPodHalfW, -GunPodHalfH), new Vector2(GunPodHalfW, -GunPodHalfH),
                new Vector2(GunPodHalfW, GunPodHalfH), new Vector2(-GunPodHalfW, GunPodHalfH),
            },
            Color = GunHullDark,
        };
        root.AddChild(body);
        var len = (float)anchor.Size;
        barrel = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-GunBarrelHalfW, 0.0f), new Vector2(GunBarrelHalfW, 0.0f),
                new Vector2(GunBarrelHalfW, -len), new Vector2(-GunBarrelHalfW, -len),
            },
            Color = GunSteel,
        };
        root.AddChild(barrel);
        muzzle = new Sprite2D
        {
            Texture = CinematicFx.SoftTexture(),
            Material = CinematicFx.AdditiveMaterial(),
            Scale = Vector2.One * (GunMuzzleSize / CinematicFx.SoftTexSize),
            Modulate = new Color(GunAmber.R, GunAmber.G, GunAmber.B, 0.0f),
        };
        root.AddChild(muzzle);
        _rigRoot!.AddChild(root);
    }

    /// <summary>散热排气口：机背脊线上的加色软点（初灭，热度驱动点亮）。</summary>
    private Sprite2D BuildVent(Core.Visual.HullAnchor anchor)
    {
        var s = new Sprite2D
        {
            Texture = CinematicFx.SoftTexture(),
            Material = CinematicFx.AdditiveMaterial(),
            Position = new Vector2((float)anchor.X, (float)anchor.Y),
            Modulate = new Color(VentColor.R, VentColor.G, VentColor.B, 0.0f),
        };
        _rigRoot!.AddChild(s);
        return s;
    }

    /// <summary>翼尖涡流：自翼尖向机尾方向拉长的加色软点（初灭，高 G 机动驱动出现）。
    /// 软点贴图按非等比缩放读作细长拖尾——位置在 UpdateHullRig 里按当前长度贴回翼尖。</summary>
    private Sprite2D BuildVortex(Core.Visual.HullAnchor anchor)
    {
        var s = new Sprite2D
        {
            Texture = CinematicFx.SoftTexture(),
            Material = CinematicFx.AdditiveMaterial(),
            Position = new Vector2((float)anchor.X, (float)anchor.Y),
            Modulate = new Color(VortexColor.R, VortexColor.G, VortexColor.B, 0.0f),
        };
        _rigRoot!.AddChild(s);
        return s;
    }

    /// <summary>机身挂点小光点（加色软点，初始全灭）：机动喷口、航行灯与形态层共用构造；
    /// 坐标与尺寸直接吃 core `PlayerHullLayout` 的挂点（不在这里重写第二份数值）。</summary>
    private Sprite2D MakeHullLight(Core.Visual.HullAnchor anchor, Color color) =>
        MakeHullLight(new Vector2((float)anchor.X, (float)anchor.Y), (float)anchor.Size, color);

    /// <summary>机身挂点小光点（加色软点，初始全灭）：机动喷口、航行灯与形态层共用构造。
    ///
    /// **挂点单位口径**：`_sprite` 的缩放已含 `0.65 × world_scale`，其子节点的局部单位就
    /// **等于贴图像素**（贴图中心在 (0,0)，机头朝 -Y）。因此挂点一律照
    /// `generate_player_sprite.py` 的锚点注释写贴图像素，**不要再乘 world_scale**——乘了会把
    /// 世界缩放叠加两遍（0.4 时灯点只剩 0.5px、两盏舷灯挤进同一个像素），细节层静默消失。
    /// 反向提醒：写 `_sprite.Position`、或挂在不缩放父节点（如 Thruster）下的量是**世界像素**，
    /// 那侧必须显式乘 world_scale 才随机体等比。两处坐标系看着像、写法相反，混用不报错。</summary>
    private Sprite2D MakeHullLight(Vector2 pos, float size, Color color)
    {
        var s = new Sprite2D
        {
            Texture = CinematicFx.SoftTexture(),
            Material = CinematicFx.AdditiveMaterial(),
            Scale = Vector2.One * (size / CinematicFx.SoftTexSize),
            Modulate = new Color(color.R, color.G, color.B, 0.0f),
            Position = pos,
        };
        _sprite.AddChild(s);
        return s;
    }

    /// <summary>核心喷口三层软点（白芯/琥珀/红外，additive）：作为喷口根部的持续亮核，
    /// 叠加在 GpuParticles 尾焰之上；挂在 Thruster 节点下随其位置/缩放。数值 effects.thruster_core.*。</summary>
    private void BuildThrusterFlare()
    {
        var ws = (float)GameState.Instance.WorldScale;
        _flareCoreSize = CfgFx.Float("effects.thruster_core.core_size", _flareCoreSize, 1.0f) * ws;
        _flareMidSize = CfgFx.Float("effects.thruster_core.mid_size", _flareMidSize, 1.0f) * ws;
        _flareOuterSize = CfgFx.Float("effects.thruster_core.outer_size", _flareOuterSize, 1.0f) * ws;
        _flareMidY = 6.0f * ws;
        _flareOuterY = 14.0f * ws;
        _flareSpeedStretch = CfgFx.Float("effects.thruster_core.speed_stretch", _flareSpeedStretch, 0.0f);
        _flareJitterPx = CfgFx.Float("effects.thruster_core.jitter_px", _flareJitterPx, 0.0f) * ws;
        _flareAlphaCore = CfgFx.Float("effects.thruster_core.alpha_core", _flareAlphaCore, 0.0f, 1.0f);
        _flareAlphaMid = CfgFx.Float("effects.thruster_core.alpha_mid", _flareAlphaMid, 0.0f, 1.0f);
        _flareAlphaOuter = CfgFx.Float("effects.thruster_core.alpha_outer", _flareAlphaOuter, 0.0f, 1.0f);
        _flareCore = MakeFlareLayer(0.0f);
        _flareMid = MakeFlareLayer(_flareMidY);
        _flareOuter = MakeFlareLayer(_flareOuterY);
    }

    private Sprite2D MakeFlareLayer(float yOff)
    {
        var s = new Sprite2D
        {
            Texture = CinematicFx.SoftTexture(),
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f), // 初始灭，SetThruster 逐帧点亮
            Material = CinematicFx.AdditiveMaterial(),
            Position = new Vector2(0.0f, yOff),
        };
        _thruster.AddChild(s);
        return s;
    }

    /// <summary>尾焰档位应用（冲刺/加速/巡航/静止五处共用；engine_tint 由 Player 传入——增幅 外观
    /// 写入 Player.EngineTint，公开字段被 PlayerAugmentVisuals 访问，留在 Player 侧）。
    /// simTime = Player 累计模拟时间（秒），作为喷口抖动相位基准（替代墙钟）。
    /// delta = 物理帧长（秒）：三态档位切换走指数平滑（油门拉动感）+ 升档瞬间短促增亮；
    /// rate ≤ 0 或动效强度为 0 时直取目标（= 本批之前的硬切换行为）。</summary>
    public void SetThruster(float speedScale, float amountRatio, float alpha, Color engineTint, float simTime, float delta)
    {
        if (!_thrusterHasState || _thrusterRate <= 0.0f || FxIntensity() <= 0.0f)
        {
            _thrusterCur = (speedScale, amountRatio, alpha);
            _thrusterHasState = true;
        }
        else
        {
            // 升档（目标速度高于当前）置一次增亮 kick；降档不 kick（收油门不该闪光）
            if (speedScale > _thrusterCur.Speed + 0.01f)
            {
                _thrusterKickAge = 0.0f;
            }

            _thrusterCur = (
                (float)Core.Visual.BodyPose.Approach(_thrusterCur.Speed, speedScale, _thrusterRate, delta),
                (float)Core.Visual.BodyPose.Approach(_thrusterCur.Amount, amountRatio, _thrusterRate, delta),
                (float)Core.Visual.BodyPose.Approach(_thrusterCur.Alpha, alpha, _thrusterRate, delta));
        }

        _thrusterKickAge += delta;
        var kick = Mathf.Max(1.0f - _thrusterKickAge / ThrusterKickTime, 0.0f);
        // 引擎喘振（§2.17，仅重伤档）：确定性不规则抖动，喷口读数「失稳」
        var sputter = _damageLevel >= 2
            ? 1.0f + _sputterAmp * (float)Core.Visual.BodyPose.SputterFactor(simTime, _sputterHz)
            : 1.0f;
        var kickedAlpha = Mathf.Min(_thrusterCur.Alpha * (1.0f + ThrusterKickAmp * kick) * sputter, 1.0f);
        _thruster.SpeedScale = _thrusterCur.Speed;
        _thruster.AmountRatio = _thrusterCur.Amount;
        _thruster.SelfModulate = new Color(1.0f, 1.0f, 1.0f, kickedAlpha) * engineTint;
        UpdateThrusterFlare(_thrusterCur.Speed, kickedAlpha, engineTint, simTime);
    }

    /// <summary>核心喷口三层逐帧驱动（SetThruster 逐帧调用）：随速度 Y 向伸缩（外层拉伸更大）+
    /// 双正交高频小幅抖动；alpha 随尾焰档位，增幅 染色经 engineTint 只染琥珀/红外两层（白芯保白）。
    /// 相位基准为模拟时间，抖动频率与墙钟版一致（43/37 Hz）。只写 struct 属性，零托管分配。</summary>
    private void UpdateThrusterFlare(float speedScale, float alpha, Color engineTint, float simTime)
    {
        if (_flareCore == null || _flareMid == null || _flareOuter == null)
        {
            return;
        }

        var stretch = 1.0f + _flareSpeedStretch * Mathf.Max(speedScale - 1.0f, 0.0f);
        var jx = Mathf.Sin(simTime * 43.0f) * _flareJitterPx;
        var jy = Mathf.Cos(simTime * 37.0f) * _flareJitterPx;
        ApplyFlare(_flareCore, _flareCoreSize, _flareAlphaCore * alpha, stretch, jx, 0.0f + jy * 0.4f, FlareCoreColor);
        ApplyFlare(_flareMid, _flareMidSize, _flareAlphaMid * alpha, stretch * 1.15f, jx * 0.7f, _flareMidY + jy * 0.7f, FlareMidColor);
        ApplyFlare(_flareOuter, _flareOuterSize, _flareAlphaOuter * alpha, stretch * 1.35f, jx * 0.5f, _flareOuterY + jy, FlareOuterColor);
        // 琥珀/红外两层乘增幅 染色（白芯不染）
        _flareMid.Modulate = new Color(
            _flareMid.Modulate.R * engineTint.R, _flareMid.Modulate.G * engineTint.G,
            _flareMid.Modulate.B * engineTint.B, _flareMid.Modulate.A);
        _flareOuter.Modulate = new Color(
            _flareOuter.Modulate.R * engineTint.R, _flareOuter.Modulate.G * engineTint.G,
            _flareOuter.Modulate.B * engineTint.B, _flareOuter.Modulate.A);
    }

    private static void ApplyFlare(Sprite2D flare, float size, float a, float stretchY, float x, float y, Color color)
    {
        flare.Scale = new Vector2(size / CinematicFx.SoftTexSize, size / CinematicFx.SoftTexSize * stretchY);
        flare.Modulate = new Color(color.R, color.G, color.B, a);
        flare.Position = new Vector2(x, y);
    }

    /// <summary>残影生成（player_dash 冲刺时经 player.spawn_afterimage 转发）：复用池节点；
    /// 同一节点淡出中被再次冲刺命中时 alpha 重置重新淡出。</summary>
    public void SpawnAfterimage(Texture2D spriteTexture, Vector2 spriteScale, Vector2 gpos, float rot)
    {
        SpawnAfterimage(spriteTexture, spriteScale, gpos, rot, AfterimageColor);
    }

    public void SpawnAfterimage(Texture2D spriteTexture, Vector2 spriteScale, Vector2 gpos, float rot, Color color)
    {
        var ghost = _afterimagePool[_afterimageIdx];
        _afterimageIdx = (_afterimageIdx + 1) % _afterimagePool.Count;
        ghost.Texture = spriteTexture;
        ghost.Scale = spriteScale;
        ghost.GlobalPosition = gpos;
        ghost.GlobalRotation = rot;
        ghost.Modulate = color;
        ghost.Visible = true;
        if (!_activeAfterimages.Contains(ghost))
        {
            _activeAfterimages.Add(ghost);
        }
    }

    /// <summary>残影淡出推进（player._process 每帧调用；池内每节点 alpha 线性衰减，归零隐藏）。</summary>
    public void UpdateAfterimages(float delta)
    {
        if (_activeAfterimages.Count == 0)
        {
            return;
        }

        var i = 0;
        while (i < _activeAfterimages.Count)
        {
            var g = _activeAfterimages[i];
            var m = g.Modulate;
            m.A -= delta / AfterimageFadeTime;
            g.Modulate = m;
            if (m.A <= 0.0f)
            {
                g.Visible = false;
                _activeAfterimages.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>机身色调四源 + 受击点脉动 + 姿态/活性/机身反馈（横移侧倾、转向跟随、运动滞后漂移、
    /// 悬停浮动、开火后坐力、开火机身光、速度伸缩、机动喷口、航行灯）逐帧驱动。擦弹闪光在此递减
    /// （原 _physics_process 视觉分支）；无敌倒计时递减留在 player（战斗状态）。
    /// simTime = Player 累计模拟时间（秒），脉动/浮动的相位基准（无头固定步长可重复）。
    /// lateral01 = 横向速度占比（速度在机体右向量上的投影 ÷ MaxSpeed，Player 归一化后传入，
    /// 加速档可超 1 后由算式钳制），驱动横移侧倾；
    /// turnDelta = 本帧机体根节点转向量（rad，已规范到 -π..π），驱动转向跟随角惯性；
    /// accelLocalX/Y = 机体本地系加速度 ÷ 参考上限（帧间速度差分，Player 换算后传入），
    /// 驱动运动滞后漂移与机动喷口；
    /// forwardSpeed01 = 速度在机头方向的投影 ÷ MaxSpeed，驱动速度伸缩；
    /// 以上姿态/活性只写贴图节点的 Rotation/Position（Scale 归 UpdateScale 独占），
    /// 机体根节点与判定几何不动（§2.13/§2.16/§2.17）。</summary>
    public void UpdateFrame(float delta, float parryTint, float invincible, float simTime, float lateral01,
        float turnDelta, float accelLocalX, float accelLocalY, float forwardSpeed01)
    {
        // 横移侧倾：目标角按横向占比，指数平滑逼近（机头朝移动方向偏）
        var target = Core.Visual.BodyPose.BankTarget(lateral01, 1.0f, _bankMax);
        _bankAngle = (float)Core.Visual.BodyPose.Approach(_bankAngle, target, _bankRate, delta);
        // 转向跟随：累积本帧转向量后指数衰减（甩准星时贴图短暂落后再追上，静止时回正）
        _sway = (float)Core.Visual.BodyPose.SwayAfter(_sway, turnDelta, _swayMax, _swayRate, delta);
        _sprite.Rotation = _bankAngle + _sway;

        // 运动滞后漂移：贴图朝加速度反方向漂移（加速时被甩在后面），指数平滑回中
        _lagX = (float)Core.Visual.BodyPose.Approach(_lagX, Core.Visual.BodyPose.LagTargetPx(accelLocalX, _lagPx), _lagRate, delta);
        _lagY = (float)Core.Visual.BodyPose.Approach(_lagY, Core.Visual.BodyPose.LagTargetPx(accelLocalY, _lagPx), _lagRate, delta);

        // 速度伸缩（§2.17）：前飞沿机头拉伸、倒退压缩，交叉轴补偿；写点归 UpdateScale（渲染帧）
        var stretchTarget = Core.Visual.BodyPose.StretchFactor(forwardSpeed01, _stretchMax);
        _stretch = (float)Core.Visual.BodyPose.Approach(_stretch, stretchTarget, _stretchRate, delta);

        // 开火后坐力：贴图沿机尾（本地 +Y，贴图机头朝上）回弹；悬停浮动叠加同一轴向
        _recoilAge += delta;
        var recoil = (float)Core.Visual.BodyPose.RecoilFactor(_recoilAge, _recoilTau) * _recoilPx;
        var bob = (float)Core.Visual.BodyPose.BobOffsetPx(simTime, _bobHz, _bobPx);
        _sprite.Position = new Vector2(_lagX, _lagY + recoil + bob);

        UpdateManeuverNozzles(accelLocalX, accelLocalY, simTime, delta);
        UpdateNavLights(simTime);
        // 形态层（§2.18）在这里只缓存输入：机炮收放与涡流走渲染帧推进（冲刺期物理帧早退，
        // 而「收枪加速」这个读数只在冲刺期出现，放物理帧会被冻住）
        _rigAccelX = accelLocalX;

        // 开火机身光（§2.17）：与后坐力同计时——枪口火光照亮机体一瞬（暖向提亮，衰减 ~3τ）
        var fireLight = (float)Core.Visual.BodyPose.RecoilFactor(_recoilAge, _fireLightTau) * _fireLightAmp;

        Color m;
        if (parryTint > 0.0f)
        {
            m = BodyTintBase.Lerp(new Color(1.7f, 1.25f, 0.5f), parryTint);
        }
        else if (_grazeFlash > 0.0f)
        {
            _grazeFlash -= delta;
            m = BodyTintBase.Lerp(new Color(1.7f, 1.35f, 0.5f), 1.0f);
        }
        else if (invincible > 0.0f)
        {
            m = BodyTintBase;
            m.A = 0.35f + 0.65f * Mathf.Abs(Enemy.SinFast(simTime * 20.0f));
        }
        else
        {
            m = BodyTintBase;
        }

        if (fireLight > 0.0f)
        {
            m = new Color(m.R * (1.0f + fireLight), m.G * (1.0f + fireLight * 0.6f), m.B * (1.0f + fireLight * 0.25f), m.A);
        }

        _sprite.Modulate = m;

        var hd = _hitboxDot.Modulate;
        hd.A = 0.45f + 0.55f * Mathf.Abs(Enemy.SinFast(simTime * 6.0f));
        _hitboxDot.Modulate = hd;
    }

    /// <summary>机动喷口逐帧驱动（§2.17）：推力与加速度反向点火——右加速 → 左喷口亮，
    /// 左加速 → 右喷口亮，向机尾加速（前进中制动/倒退）→ 机首反推亮。余量 = 该轴加速度占比
    /// 钳 [0,1]，指数平滑（同一 lag 速率）防方向抖动时闪烁；亮度再乘确定性微闪（点火不稳）。</summary>
    private void UpdateManeuverNozzles(float accelLocalX, float accelLocalY, float simTime, float delta)
    {
        if (_nozzleLeft == null || _nozzleRight == null || _nozzleRetro == null)
        {
            return;
        }

        var flicker = 1.0f + 0.18f * (float)Core.Visual.BodyPose.SputterFactor(simTime, 11.0f);
        var leftTarget = Mathf.Clamp(accelLocalX, 0.0f, 1.0f) * _nozzleAmp;
        var rightTarget = Mathf.Clamp(-accelLocalX, 0.0f, 1.0f) * _nozzleAmp;
        var retroTarget = Mathf.Clamp(accelLocalY, 0.0f, 1.0f) * _nozzleAmp;
        _nozzleLeftA = (float)Core.Visual.BodyPose.Approach(_nozzleLeftA, leftTarget, _lagRate, delta);
        _nozzleRightA = (float)Core.Visual.BodyPose.Approach(_nozzleRightA, rightTarget, _lagRate, delta);
        _nozzleRetroA = (float)Core.Visual.BodyPose.Approach(_nozzleRetroA, retroTarget, _lagRate, delta);
        SetLightAlpha(_nozzleLeft, _nozzleLeftA, flicker);
        SetLightAlpha(_nozzleRight, _nozzleRightA, flicker);
        SetLightAlpha(_nozzleRetro, _nozzleRetroA, flicker);
    }

    /// <summary>航行灯逐帧驱动（§2.17）：左右红绿慢呼吸常亮（错相），尾部白色双闪频闪
    /// （每 NavStrobePeriod 两次短闪）。纯模拟时间相位、零随机。</summary>
    private void UpdateNavLights(float simTime)
    {
        if (_navPort == null || _navStarboard == null || _navStrobe == null)
        {
            return;
        }

        var breath = Mathf.Sin(simTime * Mathf.Tau * NavBreathHz);
        SetLightAlpha(_navPort, _navAmp * (0.55f + 0.25f * breath), 1.0f);
        SetLightAlpha(_navStarboard, _navAmp * (0.55f - 0.25f * breath), 1.0f);
        var t = simTime % NavStrobePeriod;
        var flash = t < NavStrobeFlash || (t >= NavStrobeGap && t < NavStrobeGap + NavStrobeFlash);
        SetLightAlpha(_navStrobe, flash ? _navAmp : 0.0f, 1.0f);
    }

    private static void SetLightAlpha(Sprite2D light, float alpha, float mul)
    {
        var c = light.Modulate;
        light.Modulate = new Color(c.R, c.G, c.B, Mathf.Clamp(alpha * mul, 0.0f, 1.0f));
    }

    /// <summary>擦弹机身金色短闪置位（_on_graze_entered 反馈三件套之一；时长 balance player.graze.flash_time）。</summary>
    public void SetGrazeFlash(float time) => _grazeFlash = time;

    /// <summary>冲刺弹跳置位（Player 冲刺成功启动的两处调用点）：贴图缩放自 1 过冲回拉
    /// （抛物线包络，峰值 1+amp）；渲染帧推进（_Process），物理早退的冲刺期也在走。</summary>
    public void NotifyDashPop() => _popAge = 0.0f;

    /// <summary>机体形态层逐**渲染帧**推进（Player._Process 调用，UpdateScale 之后）：机炮收放、
    /// 炮管热量、散热排气、翼尖涡流。走渲染帧而非物理帧是必须的——冲刺与入场期物理帧早退，
    /// 而「收枪加速」这个读数恰恰只在冲刺期出现，放物理帧会被冻住。
    /// simTime = Player 累计模拟时间（秒），抖动的相位基准（无头固定步长可重复，无随机源）。
    /// 所有写入都是变换 / 颜色 / 显隐，零托管分配；动效强度 0 时整层隐藏（＝本批之前画面）。</summary>
    public void UpdateHullRig(float delta, float simTime)
    {
        if (_rigRoot == null)
        {
            return;
        }

        var fx = FxIntensity();
        _rigRoot.Visible = fx > 0.0f;
        if (!_rigRoot.Visible)
        {
            return;
        }

        // ① 机炮收放：开火置保持窗，窗内伸出；冲刺强制收拢（气动）
        _fireHoldAge += delta;
        _ventBurst += delta;
        var target = _dashTuck ? 0.0f : (float)Core.Visual.HullRig.DeployTarget(_fireHoldAge, _gunHoldTime);
        var rate = target > _deploy ? _gunDeployRate : _gunRetractRate;
        _deploy = (float)Core.Visual.BodyPose.Approach(_deploy, target, rate, delta);

        // ② 炮管热量：开火累加（NotifyFired）、此处只管衰减与着色
        _heat = (float)Core.Visual.HullRig.HeatDecay(_heat, _gunHeatTau, delta);
        var (amber, white) = Core.Visual.HullRig.HeatTintMix(_heat);
        var barrelColor = GunSteel.Lerp(GunAmber, (float)amber).Lerp(GunWhite, (float)white);
        // 高热时枪管失稳微抖（确定性双正弦；门槛以下不动，免得常态枪管一直在颤）
        var jitter = _heat > HeatJitterMin
            ? HeatJitterPx * (float)Core.Visual.BodyPose.SputterFactor(simTime, HeatJitterHz)
            : 0.0f;
        var barrelScale = Mathf.Lerp(BarrelRetractRatio, 1.0f, _deploy);
        var muzzleA = _heat * 0.55f * _deploy;
        ApplyGun(_gunBarrelLeft, _gunMuzzleLeft, barrelScale, barrelColor, jitter, muzzleA, _gunBarrelLen);
        ApplyGun(_gunBarrelRight, _gunMuzzleRight, barrelScale, barrelColor, jitter, muzzleA, _gunBarrelLen);

        // ③ 散热排气：热度越过门槛即放热，弹反倾倒优先取大者；排气随热度变大变亮
        var vent = Mathf.Max((float)Core.Visual.HullRig.VentStrength(_heat, _ventThreshold),
            Mathf.Max(1.0f - _ventBurst / VentBurstTime, 0.0f));
        var flicker = 1.0f + 0.25f * (float)Core.Visual.BodyPose.SputterFactor(simTime, VentFlickerHz);
        var ventSize = 20.0f * (0.55f + 0.65f * vent) / CinematicFx.SoftTexSize; // 直径 11→24 贴图像素
        SetVent(_ventLeft, vent, ventSize, flicker);
        SetVent(_ventRight, vent, ventSize, flicker);

        // ④ 翼尖涡流：横向加速度占比（物理帧缓存）过门槛才出现，指数平滑防方向抖动闪烁；
        // 强度同时给长度与亮度——涡流「拉长」比「变亮」更像真蒸气
        var vt = (float)Core.Visual.HullRig.VortexStrength(_rigAccelX, _vortexThreshold);
        _vortex = (float)Core.Visual.BodyPose.Approach(_vortex, vt, VortexRate, delta);
        var len = (float)Core.Visual.PlayerHullLayout.WingtipLeft.Size * (0.35f + 0.65f * _vortex);
        var vortexA = _vortex * _vortexAlpha; // _vortexAlpha 已在 Init 乘过动效强度，此处不再乘
        SetVortex(_vortexLeft, (float)Core.Visual.PlayerHullLayout.WingtipLeft.X,
            (float)Core.Visual.PlayerHullLayout.WingtipLeft.Y, len, vortexA);
        SetVortex(_vortexRight, (float)Core.Visual.PlayerHullLayout.WingtipRight.X,
            (float)Core.Visual.PlayerHullLayout.WingtipRight.Y, len, vortexA);
    }

    private static void ApplyGun(
        Polygon2D? barrel, Sprite2D? muzzle, float barrelScale, Color barrelColor, float jitter, float muzzleA,
        float barrelLen)
    {
        if (barrel == null || muzzle == null)
        {
            return;
        }

        barrel.Scale = new Vector2(1.0f, barrelScale);
        barrel.Color = barrelColor;
        barrel.Position = new Vector2(jitter, 0.0f);
        // 炮口辉光贴在枪口当前位置（炮管局部 y ∈ [0, -全长]，锚在荚舱原点；长度取构建期字段，
        // 读 Polygon 属性会逐帧 marshal 一份新数组）
        muzzle.Position = new Vector2(0.0f, -barrelLen * barrelScale);
        muzzle.Modulate = new Color(GunAmber.R, GunAmber.G, GunAmber.B, muzzleA);
    }

    /// <summary>排气口写入：strength 只含「多热」，动效强度在 _ventAlpha 里（Init 一次乘好）。</summary>
    private void SetVent(Sprite2D? vent, float strength, float sizeScale, float flicker)
    {
        if (vent == null)
        {
            return;
        }

        vent.Scale = Vector2.One * sizeScale;
        vent.Modulate = new Color(VentColor.R, VentColor.G, VentColor.B,
            Mathf.Clamp(strength * flicker, 0.0f, 1.0f) * _ventAlpha);
    }

    /// <summary>涡流：自翼尖向机尾（+Y）延伸的细长软点——软点以自身中心定位，故整条拖尾
    /// 沿 +Y 平移半长，让起点贴住翼尖。</summary>
    private static void SetVortex(Sprite2D? vortex, float x, float y, float len, float alpha)
    {
        if (vortex == null)
        {
            return;
        }

        vortex.Position = new Vector2(x, y + len * 0.5f);
        vortex.Scale = new Vector2(VortexWidthPx / CinematicFx.SoftTexSize, len / CinematicFx.SoftTexSize);
        vortex.Modulate = new Color(VortexColor.R, VortexColor.G, VortexColor.B, alpha);
    }

    /// <summary>贴图缩放的唯一写者（逐渲染帧推进）：冲刺弹跳 × 速度伸缩（轴向，交叉轴体积补偿）
    /// × 受击压缩（横向压扁回弹），并顺带驱动损伤烟的排放比（重伤档点亮、指数平滑开关）。
    /// 物理早退区间（冲刺/入场/锁输入）也在走——弹跳与压缩的包络不该被物理早退冻住。
    /// 出窗后零开销早退（三个包络都归位时不再写 Scale）。</summary>
    public void UpdateScale(float delta)
    {
        var pop = (float)Core.Visual.BodyPose.PopScale(_popAge, _popTime, _popAmp);
        var hit = (float)Core.Visual.BodyPose.RecoilFactor(_hitAge, _hitSquashTau) * _hitSquashAmp;
        if (_popAge < _popTime || _hitAge < 3.0f * _hitSquashTau || _stretch != 0.0f || _scaleDirty)
        {
            _popAge += delta;
            _hitAge += delta;
            // 纵向 = 伸缩 × 受击压扁的交叉轴补偿；横向 = 伸缩的交叉轴补偿 × 受击压扁
            var sy = (1.0f + _stretch) * (float)Core.Visual.BodyPose.CounterScale(hit, HitSquashCrossRatio);
            var sx = (float)Core.Visual.BodyPose.CounterScale(_stretch, StretchCrossRatio) * (1.0f + hit);
            _sprite.Scale = new Vector2(_spriteScaleBase.X * sx * pop, _spriteScaleBase.Y * sy * pop);
            _scaleDirty = _stretch != 0.0f;
        }

        UpdateDamageSmoke(delta);
    }

    /// <summary>损伤烟排放比驱动（§2.17）：重伤档（damage level 2）目标 = smoke_ratio，否则 0；
    /// 指数平滑开关（防档位切换时烟骤开骤停），归零即停发（Emitting=false，不留常驻模拟开销）。</summary>
    private void UpdateDamageSmoke(float delta)
    {
        if (_smoke == null)
        {
            return;
        }

        var target = _damageLevel >= 2 ? _smokeRatio : 0.0f;
        if (_smokeCur <= 0.0f && target <= 0.0f)
        {
            if (_smoke.Emitting)
            {
                _smoke.Emitting = false;
            }

            return;
        }

        _smokeCur = (float)Core.Visual.BodyPose.Approach(_smokeCur, target, _lagRate, delta);
        _smoke.AmountRatio = _smokeCur;
        _smoke.Emitting = _smokeCur > 0.01f;
    }

    /// <summary>弹反命中闪光置位（Player 盾区反射成功时调用）：边缘白金色提亮 + 外扩脉冲。</summary>
    public void SetParryFlash() => _parryFlash = ParryFlashTime;
    /// <summary>激活金光一闪置位（Player 盾进入 ACTIVE 瞬间调用）：白金圆环 0.45×→1.5× 缓出扩张 + 淡出。</summary>
    public void SetParryActivatePulse()
    {
        _parryPulseTimer = ParryPulseTime;
        _parryPulse.Visible = true;
    }

    /// <summary>盾视觉逐物理帧驱动：WINDUP 小弧展开到全弧（缩放）、ACTIVE 盾缘能量脉动 + 珍珠流光
    /// 自弧线左端扫至右端、RECOVER 保持全弧、IDLE 隐藏；弹反命中时短闪（白金色提亮 + 边缘外扩）。
    /// 三层结构：暗金填充扇面 + 亮金分段盾缘（伪能量格）+ 流光高光带（零 shader 依赖，ADD 混合出辉光）。
    /// 参数化（expand/shine 来自 PlayerParry，radius/arc 来自 player 常量）——视觉不感知 弹反组件。
    /// 每物理帧调用：只写 Modulate/Scale（struct），流光带顶点走预分配缓冲，零托管分配。</summary>
    public void UpdateParryVisuals(float expand, float shine, float radius, float arcDeg, float delta, float simTime)
    {
        var visible = expand > 0.0f;
        _parryArc.Visible = visible;
        _parryRim.Visible = visible;
        if (!visible)
        {
            _parryShine.Visible = false;
            _parryFlash = 0.0f;
            _parryPulseTimer = 0.0f;
            _parryPulse.Visible = false;
            return;
        }

        if (_parryFlash > 0.0f)
        {
            _parryFlash = Mathf.Max(_parryFlash - delta, 0.0f);
        }

        // 激活金光一闪：0.32s 内圆环 0.45×→1.5× 二次缓出扩张，alpha 线性淡出
        if (_parryPulseTimer > 0.0f)
        {
            _parryPulseTimer = Mathf.Max(_parryPulseTimer - delta, 0.0f);
            var t = 1.0f - _parryPulseTimer / ParryPulseTime;
            var easeOut = 1.0f - (1.0f - t) * (1.0f - t);
            _parryPulse.Scale = Vector2.One * (0.45f + 1.05f * easeOut);
            _parryPulse.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f - t);
            if (_parryPulseTimer <= 0.0f)
            {
                _parryPulse.Visible = false;
            }
        }

        var flash = _parryFlash / ParryFlashTime;
        var scale = 0.3f + 0.7f * expand;
        _parryArc.Scale = Vector2.One * scale;
        _parryArc.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f + 1.4f * flash);
        // 盾缘：ACTIVE（shine>0）能量脉动，RECOVER 恒定高亮；命中闪叠加外扩 + 白金色提亮
        var pulse = shine > 0.0f ? 0.72f + 0.28f * Mathf.Abs(Mathf.Sin(simTime * 12.0f)) : 0.9f;
        _parryRim.Scale = Vector2.One * (scale * (1.0f + 0.14f * flash));
        _parryRim.Modulate = new Color(1.0f + 1.1f * flash, 1.0f + 0.6f * flash, 1.0f, Mathf.Min(pulse + 0.6f * flash, 1.0f));
        _parryShine.Visible = shine > 0.0f;
        if (!_parryShine.Visible)
        {
            return;
        }

        var arc = Core.Combat.AimCone.HalfAngleRadFromFullAngleDeg(arcDeg);
        var centerA = -Mathf.Pi / 2.0f - arc + 2.0f * arc * shine;
        var w = Mathf.DegToRad(14.0f); // 高光带角宽
        var sp = _parryShinePoly; // 预分配复用，防每物理帧 new Vector2[6]
        sp[0] = Vector2.Zero;
        for (var i = 0; i < 5; i++)
        {
            var a = centerA - w + 2.0f * w * i / 4.0f;
            sp[i + 1] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * scale;
        }

        _parryShine.Polygon = sp;
    }

}
