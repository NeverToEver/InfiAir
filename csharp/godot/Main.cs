using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace InfiAir;

/// <summary>
/// 主场景：串联生成器、HUD 与各 UI 层，处理母舰召唤（H）、返航（B）、
/// 开始面板（继续对局/新游戏）与常驻 BGM。Esc/手柄 B/Android 返回的全局路由
/// 在 BackNavigator（process_mode=Always；本节点暂停时收不到 _unhandled_input）。
/// M6 全量迁移（2026-08-08 自 scripts/main.gd）。
/// 同批迁移类 typed 直调：Spawner/IntroCinematic/ReturnCinematic/MothershipSummonWindow/
/// </summary>
public partial class Main : Node2D
{
    private const string BgmPath = "res://assets/audio/bgm_loop.wav";
    private static readonly PackedScene MothershipScene = GD.Load<PackedScene>("res://scenes/mothership.tscn");
    private static readonly PackedScene IntroScene = GD.Load<PackedScene>("res://scenes/intro_cinematic.tscn");
    private static readonly PackedScene ReturnScene = GD.Load<PackedScene>("res://scenes/return_cinematic.tscn");
    public float DOCK_CHARGE_TIME { get; set; } = 3.0f;
    public float HOME_CHARGE_TIME { get; set; } = 1.5f;
    public float GIVE_UP_HOLD_TIME { get; set; } = 3.0f;
    /// <summary>Boss 狂暴子弹时间（对齐原作 ENRAGE_SLOW_FACTOR=0.24）：1.2s 全局慢速 → 0.3s 恢复 → 快照弹幕
    /// （子弹时间是狂暴序列 TRANSITION 的表现；序列编排/锁血/锁玩家移动由 boss.gd 接管）</summary>
    public float ENRAGE_SLOW_SCALE { get; set; } = 0.24f;
    public float ENRAGE_BULLET_TIME { get; set; } = 1.2f;
    public float ENRAGE_RAMP_TIME { get; set; } = 0.3f;

    private Spawner _spawner = null!;
    private Hud _hud = null!;
    private PauseUi _pauseUi = null!;
    private BaseConsole _baseUi = null!;
    private Player _player = null!;
    private Starfield _starfield = null!;
    private Camera2D _camera = null!;
    private bool _gameOver;
    /// <summary>B 梯队（fair plan §8）：死亡回放录制器（main._process 采样，死亡时生成重放演出）</summary>
    private readonly DeathReplay _replay = new();
    private bool _homecoming;
    private AudioStreamPlayer? _bgmPlayer;
    private float _dockCooldown;
    private Mothership? _mothership;
    private bool _charging;
    private float _chargeTime;
    private Mothership _chargeGhost = null!;
    private Node2D _chargeFx = null!; // 蓄力特效容器（与 _charge_ghost 同位，随蓄力显隐）
    private Sprite2D _chargeGlow = null!; // 虚影背光
    private readonly List<Line2D> _chargeRings = new(); // 收缩椭圆环 ×2
    private GpuParticles2D _chargeInflow = null!; // 内吸粒子
    private float _homeChargeTime;
    private float _giveUpCharge;
    // Boss 狂暴子弹时间状态（main 统一接管）。time_scale 复位覆盖全部路径（离场/逃跑/返航/
    // 放弃/玩家死亡统一复位，B2 已修复——2026-08-05 P4 注释修正）
    private float _bulletTimeLeft; // >0：子弹时间剩余（游戏秒，随 time_scale 缩放）
    private float _timeScaleRamp = -1.0f; // >=0：恢复过渡进度 0..1
    private Boss? _enrageBoss;
    /// <summary>播放中的开场过场（BackNavigator 据此路由 Esc=跳过；null = 未播放）</summary>
    private IntroCinematic? _intro;
    /// <summary>播放中的返航过场（BackNavigator 据此路由 Esc=跳过；null = 未播放）</summary>
    private ReturnCinematic? _return;
    /// <summary>播放中的轨道打击清场动画（继续出击时触发；null = 未播放）</summary>
    private OrbitalStrike? _strike;
    /// <summary>播放中的母舰召唤机库小窗（蓄力完成后触发；null = 未播放）</summary>
    private MothershipSummonWindow? _summonWindow;
    /// <summary>精英炮塔事件编排节点（_ready 创建并登记给 spawner 互斥）</summary>
    private EliteTurretEvent _event = null!;
    /// <summary>轰炸编队事件编排节点（_ready 创建并登记给 spawner；最低优先级随机事件）</summary>
    private FormationStrikeEvent _formation = null!;
    /// <summary>Meta HUD 血量/受击后处理层（_ready 创建；DYING 呼吸缩放经 _apply_camera_zoom 组合）</summary>
    private MetaHealthFX _metaFx = null!;
    /// <summary>辅助瞄准框覆盖层（_ready 创建；世界坐标单节点画全部标记敌框，登记 GameState.aim_frame_layer）</summary>
    private AimFrameLayer _aimFrames = null!;
    /// <summary>触屏虚拟输入层（mobile touch）</summary>
    private VirtualControls _virtualControls = null!;
    private bool _breathWasActive;
    /// <summary>give_up（K 键自毁）动作静态绑定判定（project.godot 定义，改键系统不删动作，结果全程不变）：
    /// _ready 缓存一次，避免 _process 每帧 InputMap.has_action 字典查找</summary>
    private bool _giveUpBound;
    private bool _entryFinishedConnected; // C# event 无 is_connected 查询：入场衔接幂等连接守卫
    private GameEventManager _events = null!;
    private FogEventManager _fogEvents = null!;
    private readonly Callable _onPlayerDied;
    private readonly Callable _onViewZoomChanged;
    private readonly Callable _onTouchControlsChanged;

    public Main()
    {
        _onPlayerDied = Callable.From(OnPlayerDied);
        _onViewZoomChanged = Callable.From<float>(OnViewZoomChanged);
        _onTouchControlsChanged = Callable.From<bool>(OnTouchControlsChanged);
    }

    public override void _Ready()
    {
        _spawner = GetNode<Spawner>("Spawner");
        _hud = GetNode<Hud>("HUD");
        _pauseUi = GetNode<PauseUi>("PauseUI");
        _baseUi = GetNode<BaseConsole>("BaseUI");
        _player = GetNode<Player>("Player");
        _starfield = GetNode<Starfield>("Starfield");
        _camera = GetNode<Camera2D>("Camera2D");
        AddToGroup("main");
        _giveUpBound = InputMap.HasAction("give_up"); // 静态动作绑定缓存（_process 每帧读取）
        DOCK_CHARGE_TIME = Mathf.Max((float)GameState.Instance.Cfg("mothership.dock_charge_time", DOCK_CHARGE_TIME).AsDouble(), 0.01f); // H15：=0 除零
        HOME_CHARGE_TIME = Mathf.Max((float)GameState.Instance.Cfg("effects.home_charge_time", HOME_CHARGE_TIME).AsDouble(), 0.01f); // H15：=0 除零（蓄力进度比例）
        GIVE_UP_HOLD_TIME = Mathf.Max((float)GameState.Instance.Cfg("effects.give_up_hold_time", GIVE_UP_HOLD_TIME).AsDouble(), 0.01f); // H15：=0 除零（蓄力进度比例）
        ENRAGE_SLOW_SCALE = Mathf.Max((float)GameState.Instance.Cfg("boss.enrage.slow_scale", ENRAGE_SLOW_SCALE).AsDouble(), 0.01f); // H15 族：=0 使狂暴慢速完全冻结
        ENRAGE_BULLET_TIME = Mathf.Max((float)GameState.Instance.Cfg("boss.enrage.bullet_time", ENRAGE_BULLET_TIME).AsDouble(), 0.01f); // H15 族：=0 跳过子弹时间演出
        ENRAGE_RAMP_TIME = Mathf.Max((float)GameState.Instance.Cfg("boss.enrage.ramp_time", ENRAGE_RAMP_TIME).AsDouble(), 0.01f); // K05：H15 同族遗漏（=0 时 _time_scale_ramp 除零）
        // 防御：上一场对局若在子弹时间内结束（死亡重开），确保全局速度已复位
        Engine.TimeScale = 1.0f;
        RenderingServer.SetDefaultClearColor(new Color(0.02f, 0.02f, 0.06f));
        _spawner.BossSpawned += _hud.ShowBossBar;
        _spawner.BossSpawned += OnBossSpawned;
        _spawner.BossWarning += _hud.ShowBossBanner;
        // 精英炮塔事件：编排节点挂 Main 下（清场/测试遍历可见），spawner 持引用做互斥
        _event = new EliteTurretEvent();
        AddChild(_event);
        _event.SetSpawner(_spawner); // A5：依赖注入，替代事件侧 group 现找
        _spawner.SetEliteEvent(_event);
        // 轰炸编队事件：同模式登记（最低优先级随机事件，不冻结 Boss/波次）
        _formation = new FormationStrikeEvent();
        AddChild(_formation);
        _formation.SetSpawner(_spawner); // K15：A5 依赖注入延续——编队事件侧不再 group 现找 spawner
        _spawner.SetFormationEvent(_formation);
        // 统一事件管理器接线（docs/EVENT_MANAGER.md）：遭遇事件注册进统一注册表（缓存单例），
        // 触发策略/信号由管理器接管；spawner 注入用于触发门控与特殊槽通知
        var evV = GameState.Instance.Events;
        _events = evV;
        _events.SetSpawner(_spawner);
        _events.RegisterEncounter(new StringName("elite_turret"), _event);
        _events.RegisterEncounter(new StringName("formation_strike"), _formation);
        _events.SetRunActive(GetTree().CurrentScene == this);
        var gs = GameState.Instance;
        if (gs != null && !gs.IsConnected("PlayerDied", _onPlayerDied))
        {
            gs.Connect("PlayerDied", _onPlayerDied);
        }

        _baseUi.ResumeRequested += OnResumeFromBase;
        // 迷雾事件：仅真实对局（main 为 current_scene）开启自动触发。
        // 测试以子节点实例化 main.tscn 时 current_scene 为测试场景 → 保持关闭，
        // 防止随机迷雾事件（如方向偏转把测试玩家推入弹幕触发擦弹得分）破坏测试断言确定性；
        // 需要测迷雾的用例显式 set_run_active(true)（同 intro 过场的 current_scene 判定惯例）
        var fogV = GameState.Instance.FogEvents;
        _fogEvents = fogV;
        _fogEvents.SetRunActive(GetTree().CurrentScene == this);
        // 视角缩放：应用到相机（震动只写 offset，与 zoom 互不干扰）；注册供可见区域计算
        GameState.Instance.CameraRef = _camera;
        // Meta HUD 血量/受击后处理层（layer=1，世界之上、HUD 之下；先于首次 zoom 组合创建）
        _metaFx = new MetaHealthFX();
        AddChild(_metaFx);
        // 辅助瞄准框覆盖层（P1-1）：世界坐标单节点，每帧统一画标记敌 bracket 框
        _aimFrames = new AimFrameLayer();
        AddChild(_aimFrames);
        // 触屏虚拟输入层（mobile touch，2026-08-07）：设置开关联动（默认关，桌面零回归）
        _virtualControls = new VirtualControls();
        AddChild(_virtualControls);
        GameState.Instance.VirtualControls = _virtualControls;
        _virtualControls.SetEnabled(GameState.Instance.TouchControls);
        if (gs != null && !gs.IsConnected("TouchControlsChanged", _onTouchControlsChanged))
        {
            gs.Connect("TouchControlsChanged", _onTouchControlsChanged);
        }

        ApplyCameraZoom();
        if (gs != null && !gs.IsConnected("ViewZoomChanged", _onViewZoomChanged))
        {
            gs.Connect("ViewZoomChanged", _onViewZoomChanged);
        }

        _ = StartBgmAsync();
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--startup-time") >= 0)
        {
            _ = ReportStartupTime();
        }

        // 蓄力虚影（长按 H 蓄力期间显示）：复用真实母舰场景实例做半透明预告，
        // 禁用状态机（仅外观，不移动/不对接），停驻高度取实例配置 HOVER_Y
        _chargeGhost = MothershipScene.Instantiate<Mothership>();
        AddChild(_chargeGhost);
        // L13：蓄力虚影非在场母舰——事件互斥（can_trigger 查 group "mothership"）须排除
        // 常驻虚影：虚影 main 场景常驻且 _ready 已入组，不退组则事件在整个对局恒被虚影拦截
        _chargeGhost.RemoveFromGroup("mothership");
        // 必须在入树后禁用：入树前调用 set_physics_process(false) 不生效（4.6 实测）
        _chargeGhost.SetPhysicsProcess(false);
        // C14：蓄力虚影居中取可见世界中心，不写死 960
        _chargeGhost.Position = new Vector2(GameState.Instance.ViewWorldRect().GetCenter().X, _chargeGhost.HoverY);
        var ghostMod = _chargeGhost.Modulate;
        ghostMod.A = 0.15f;
        _chargeGhost.Modulate = ghostMod;
        _chargeGhost.Visible = false;
        BuildChargeFx();
        // 账户系统（2026-08-04）：welcome 主场景已确认入口——有存档=「继续对局」启动即恢复
        // （main 侧自加载，welcome 不重复加载）；无存档=新开局（_apply_new_run 负责开场演出与
        // 死亡回放录制起点，原由 StartPanel 信号触发）
        if (GameState.Instance.HasSave())
        {
            OnContinueRun();
        }
        else
        {
            ApplyNewRun();
        }
    }

    public override void _ExitTree()
    {
        // 子弹时间内退出（重开/测试结束）也要保证全局速度复位
        Engine.TimeScale = 1.0f;
        var camRef = GameState.Instance.CameraRef;
        if (camRef == _camera)
        {
            GameState.Instance.CameraRef = null;
        }

        _fogEvents.SetRunActive(false);
        // C22 模式（M6）：GameState 信号显式断开——退出时 GameState 先于本节点释放的
        // 时序下连接悬空可致退出 segfault（M5 实测定位；原 GDScript 自动断开，C# 需手动）
        var gs = GameState.Instance;
        if (gs == null)
        {
            return;
        }

        if (gs.IsConnected("PlayerDied", _onPlayerDied))
        {
            gs.Disconnect("PlayerDied", _onPlayerDied);
        }

        if (gs.IsConnected("ViewZoomChanged", _onViewZoomChanged))
        {
            gs.Disconnect("ViewZoomChanged", _onViewZoomChanged);
        }

        if (gs.IsConnected("TouchControlsChanged", _onTouchControlsChanged))
        {
            gs.Disconnect("TouchControlsChanged", _onTouchControlsChanged);
        }
    }

    /// <summary>对外公开接口（A1 修复）：BackNavigator/HUD 决策查询，禁止跨类直接读 _ 私有字段</summary>
    public bool IsIntroPlaying() => _intro != null;

    public bool IsReturnPlaying() => _return != null;

    public bool IsGameOver() => _gameOver;

    public bool IsHomecoming() => _homecoming;

    public Mothership? Mothership() => _mothership;

    /// <summary>A7：测试/诊断白盒断言经公开接口（命名语义化）</summary>
    public Player Player() => _player;

    public Hud Hud() => _hud;

    public BaseConsole BaseUi() => _baseUi;

    public PauseUi PauseUi() => _pauseUi;

    public MetaHealthFX MetaFx() => _metaFx;

    /// <summary>A7：测试/诊断白盒断言经公开接口（命名语义化）；遭遇事件实例由统一事件管理器
    /// 持有（main._ready 注册），访问器经管理器注册表取缓存单例</summary>
    public Node? Event() => _events.Event(new StringName("elite_turret")).AsGodotObject() as Node;

    public Node? Formation() => _events.Event(new StringName("formation_strike")).AsGodotObject() as Node;

    public OrbitalStrike? Strike() => _strike;

    public MothershipSummonWindow? SummonWindow() => _summonWindow;

    public void SetHomecoming(bool v) => _homecoming = v;

    public void SetGameOver(bool v) => _gameOver = v;

    public void SetBulletTime(float seconds) => _bulletTimeLeft = seconds;

    public float TimeScaleRamp() => _timeScaleRamp;

    public void PlayIntro() => PlayIntroCinematic();

    public void SkipIntro() => SkipIntroInternal();

    public void PlayReturn() => PlayReturnCinematic();

    public void SkipReturn() => SkipReturnInternal();

    public void StartHomecoming() => StartHomecomingInternal();

    public void SummonMothership() => SummonMothershipInternal();

    public void ResumeFromBase() => ResumeFromBaseInternal();

    /// <summary>A7：测试/诊断经公开接口（动作包装）——开场/继续出击后的战机入场序列</summary>
    public void StartEntrySequence() => StartEntrySequenceInternal();

    public void StopCharging() => StopChargingInternal();

    public void SetDockCooldown(float seconds) => _dockCooldown = seconds;

    public void OnMothershipDeparted(float seconds) => OnMothershipDepartedInternal(seconds);

    public bool Charging() => _charging;

    public Mothership ChargeGhost() => _chargeGhost;

    public float GiveUpCharge() => _giveUpCharge;

    public void ContinueRun() => OnContinueRun();

    public float BulletTime() => _bulletTimeLeft;

    public float DockCooldown() => _dockCooldown;

    public void SetChargeTime(float seconds) => _chargeTime = seconds;

    public IntroCinematic? Intro() => _intro;

    public ReturnCinematic? ReturnCinematic() => _return;

    private void OnViewZoomChanged(float _factor) => ApplyCameraZoom();

    /// <summary>相机 zoom 单点组合（D6）：视角档位 × DYING 呼吸缩放；震动只写 offset 不受影响</summary>
    private void ApplyCameraZoom()
    {
        var breath = 1.0f;
        if (_metaFx != null && _metaFx.BreathActive())
        {
            breath = _metaFx.BreathScale();
        }

        _camera.Zoom = Vector2.One * (float)GameState.Instance.ViewZoomFactor() * breath;
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        // Boss 狂暴子弹时间驱动（delta 已被 time_scale 缩放，计时为游戏秒）：
        // 0.24 慢速 1.2s → 0.3s 内线性恢复 1.0 → 恢复完成才发快照弹幕
        if (_bulletTimeLeft > 0.0f)
        {
            _bulletTimeLeft -= d;
            if (_bulletTimeLeft <= 0.0f)
            {
                _timeScaleRamp = 0.0f;
            }
        }

        if (_timeScaleRamp >= 0.0f)
        {
            _timeScaleRamp += d / ENRAGE_RAMP_TIME;
            if (_timeScaleRamp >= 1.0f)
            {
                _timeScaleRamp = -1.0f;
                Engine.TimeScale = 1.0f;
                FireEnrageSnapshot();
            }
            else
            {
                Engine.TimeScale = Mathf.Lerp(ENRAGE_SLOW_SCALE, 1.0f, _timeScaleRamp);
            }
        }

        if (_dockCooldown > 0.0f)
        {
            _dockCooldown -= d;
        }

        // 长按 H 蓄力召唤母舰（松手取消，不进冷却；召唤小窗播放中不再进入蓄力，
        // 否则蓄力满后 _summon_mothership 被小窗守卫挡下会反复进入蓄力态）。
        // 2026-08-06 审计：遭遇事件进行中禁止蓄力（L13 互斥只查触发期——事件中召唤
        // 母舰自动火力可清场全额领奖，玩家零参与挂机收益）
        var canCharge = _mothership == null
            && _dockCooldown <= 0.0f
            && !_gameOver
            && !_homecoming
            && _summonWindow == null
            && _events.ActiveId(_events.GROUP_ENCOUNTER) == new StringName();
        if (canCharge && Input.IsActionPressed("dock"))
        {
            _charging = true;
            _chargeTime += d;
            _chargeGhost.Visible = true;
            var cp = Mathf.Clamp(_chargeTime / DOCK_CHARGE_TIME, 0.0f, 1.0f);
            var ghostMod = _chargeGhost.Modulate;
            ghostMod.A = 0.15f + 0.25f * cp;
            _chargeGhost.Modulate = ghostMod;
            // 蓄力特效：背光渐亮 + 双环错峰收缩 + 内吸粒子（帧内仅属性写，零分配）
            _chargeFx.Visible = true;
            _chargeInflow.Emitting = true;
            var glowMod = _chargeGlow.Modulate;
            glowMod.A = 0.35f * cp;
            _chargeGlow.Modulate = glowMod;
            for (var i = 0; i < _chargeRings.Count; i++)
            {
                var rp = Mathf.Clamp(cp * 1.25f - 0.25f * i, 0.0f, 1.0f);
                var ring = _chargeRings[i];
                ring.Scale = Vector2.One * Mathf.Lerp(2.2f, 0.7f, rp);
                var ringMod = ring.Modulate;
                ringMod.A = 0.15f + 0.55f * rp;
                ring.Modulate = ringMod;
            }

            if (_chargeTime >= DOCK_CHARGE_TIME)
            {
                StopChargingInternal();
                SummonMothershipInternal();
            }
        }
        else if (_charging)
        {
            StopChargingInternal();
        }

        // 长按 B 蓄力返航（松手取消）
        if (!_gameOver && !_homecoming && Input.IsActionPressed("homecoming"))
        {
            _homeChargeTime += d;
            _hud.SetHomeCharge(_homeChargeTime / HOME_CHARGE_TIME);
            if (_homeChargeTime >= HOME_CHARGE_TIME)
            {
                _homeChargeTime = 0.0f;
                _hud.SetHomeCharge(-1.0f);
                StartHomecomingInternal();
            }
        }
        else if (_homeChargeTime > 0.0f)
        {
            _homeChargeTime = 0.0f;
            _hud.SetHomeCharge(-1.0f);
        }

        // 长按 K 蓄力放弃出击（自毁进死亡结算，松手取消；give_up 映射由 project.godot 提供）
        if (_giveUpBound && !_gameOver && !_homecoming && !_player.IsDead() && Input.IsActionPressed("give_up"))
        {
            _giveUpCharge += d;
            _hud.SetGiveUpCharge(_giveUpCharge / GIVE_UP_HOLD_TIME);
            if (_giveUpCharge >= GIVE_UP_HOLD_TIME)
            {
                _giveUpCharge = 0.0f;
                _hud.SetGiveUpCharge(-1.0f);
                GiveUp();
            }
        }
        else if (_giveUpCharge > 0.0f)
        {
            _giveUpCharge = 0.0f;
            _hud.SetGiveUpCharge(-1.0f);
        }

        // DYING 呼吸缩放（D6）：仅激活期逐帧组合；退出激活时复位一次到基础 zoom
        var breathOn = _metaFx != null && _metaFx.BreathActive();
        if (breathOn || _breathWasActive)
        {
            ApplyCameraZoom();
        }

        _breathWasActive = breathOn;
        // B 梯队：死亡回放录制（每渲染帧采样敌弹轨迹；死亡后树暂停本函数不再执行）
        // P0-1（2026-08-05 审计）：数据源改敌弹注册表，消除每帧 get_children + cast 分配链
        _replay.Record();
    }

    private void StopChargingInternal()
    {
        _charging = false;
        _chargeTime = 0.0f;
        _chargeGhost.Visible = false;
        _chargeFx.Visible = false;
        _chargeInflow.Emitting = false;
    }

    /// <summary>蓄力特效（长按 H 期间随 _charge_ghost 显示）：虚影背光 + 双收缩椭圆环 + 内吸粒子。
    /// 与虚影同位（960, HOVER_Y），世界坐标；环半径 160 为设计值 × world_scale。</summary>
    private void BuildChargeFx()
    {
        var ws = (float)GameState.Instance.WorldScale;
        _chargeFx = new Node2D();
        // C14：与虚影同位（复用 _charge_ghost.position.x，不写死 960）
        _chargeFx.Position = new Vector2(_chargeGhost.Position.X, _chargeGhost.HoverY);
        _chargeFx.Visible = false;
        AddChild(_chargeFx);
        // 背光（衬在虚影之下：z -1）
        _chargeGlow = CinematicFx.SoftGlow(220.0f * ws, new Color(0.35f, 0.85f, 1.0f, 0.0f));
        _chargeGlow.ZIndex = -1;
        _chargeFx.AddChild(_chargeGlow);
        // 收缩椭圆环 ×2（透视压扁，蓄力进度驱动 2.2→0.7 错峰收缩）
        for (var i = 0; i < 2; i++)
        {
            var ring = new Line2D
            {
                Width = 2.5f,
                DefaultColor = new Color(0.4f, 0.9f, 1.0f),
            };
            ring.Points = CinematicFx.RingPoints(48, 160.0f * ws, 0.5f);
            ring.Material = CinematicFx.AdditiveMaterial();
            _chargeFx.AddChild(ring);
            _chargeRings.Add(ring);
        }

        // 内吸粒子：环上发射、负径向速度流向中心（蓄能汇聚感）
        var cfg = new Godot.Collections.Dictionary
        {
            ["amount"] = 36,
            ["lifetime"] = 0.7f,
            ["vel_min"] = 0.0f,
            ["vel_max"] = 0.0f,
            ["scale_min"] = 2.0f,
            ["scale_max"] = 4.0f,
            ["color"] = new Color(0.5f, 0.9f, 1.0f, 0.55f),
        };
        _chargeInflow = CinematicFx.Particles(cfg);
        var inflowMat = (ParticleProcessMaterial)_chargeInflow.ProcessMaterial;
        inflowMat.Direction = Vector3.Zero;
        inflowMat.Spread = 0.0f;
        inflowMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
        inflowMat.EmissionRingAxis = new Vector3(0.0f, 0.0f, 1.0f);
        inflowMat.EmissionRingRadius = 160.0f * ws;
        inflowMat.EmissionRingInnerRadius = 150.0f * ws;
        inflowMat.EmissionRingHeight = 0.0f;
        inflowMat.RadialVelocityMin = -90.0f * ws;
        inflowMat.RadialVelocityMax = -160.0f * ws;
        _chargeInflow.Emitting = false;
        _chargeFx.AddChild(_chargeInflow);
    }

    /// <summary>BGM 延后到首帧之后启动：3.5MB WAV 解码不占首帧关键路径</summary>
    private async Task StartBgmAsync()
    {
        try
        {
            // U17：await 段异常统一 try/catch（约定 §Async）——恢复期节点释放/引擎错误不静默吞
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // C15：await 后守卫——首帧前 main 被释放（无头测试同帧实例化释放）则不再操作 freed 实例
            if (!IsInsideTree())
            {
                return;
            }

            StartBgm();
        }
        catch (Exception ex)
        {
            GD.PushWarning("StartBgmAsync 异常：" + ex.Message);
        }
    }

    /// <summary>启动计时（--startup-time 传入时）：打印 boot → 首帧 / → 首面板就绪 的分段耗时</summary>
    private async Task ReportStartupTime()
    {
        try
        {
            // U17：await 段异常统一 try/catch + 判活守卫
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInsideTree())
            {
                return;
            }

            GD.Print(GdFormat("[startup] boot → first frame: %d ms", (long)Time.GetTicksMsec() - GameState.Instance.BootTicksMsec));
        }
        catch (Exception ex)
        {
            GD.PushWarning("ReportStartupTime 异常：" + ex.Message);
        }
    }

    private void StartBgm()
    {
        // P1-4（2026-08-05 审计）：CACHE_MODE_IGNORE 每次进 main 重新 load + 解码 3.5MB WAV；
        // 改 CACHE_MODE_REUSE 复用资源缓存（静态音频，缓存复用无副作用；音频路径保持等价不变）
        var stream = ResourceLoader.Load(BgmPath, "AudioStreamWAV", ResourceLoader.CacheMode.Reuse) as AudioStreamWav;
        // H04（健壮性审核）：运行时 load 判空——打包漏资源/磁盘异常时降级静默而非空引用崩溃
        if (stream == null)
        {
            GD.PushWarning("BGM 资源加载失败：" + BgmPath);
            return;
        }

        // 只设 loop_mode 即可整段循环；显式写 loop_begin/loop_end 会在退出时泄漏播放实例
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        _bgmPlayer = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = -18.0f,
        };
        AddChild(_bgmPlayer);
        _bgmPlayer.Play();
    }

    /// <summary>新对局（无存档或开始面板选「新游戏」）：数据层已由 reset_run/读档就绪，无需额外处理。
    /// 仅正常启动入口播放开场过场（测试以子节点实例化 main.tscn 时 current_scene != self，不播）</summary>
    private void ApplyNewRun()
    {
        // B 梯队：死亡回放录制开始（缓冲清空重录；死亡后 main._process 冻结自然停止）
        _replay.Begin();
        if (GetTree().CurrentScene == this)
        {
            PlayIntroCinematic();
        }
    }

    /// <summary>播放开场过场：冻结对局帧 0（树暂停，过场 process_mode=Always 照常播放），
    /// 播完/跳过统一走 finished 恢复。测试可直接调用本函数触发。</summary>
    private void PlayIntroCinematic()
    {
        if (_intro != null)
        {
            return;
        }

        _intro = IntroScene.Instantiate<IntroCinematic>();
        _intro.Finished += OnIntroFinished;
        AddChild(_intro);
        GetTree().Paused = true;
    }

    /// <summary>Esc 经 BackNavigator 路由至此；任意键/点击由过场自身 _unhandled_input 捕获</summary>
    private void SkipIntroInternal()
    {
        if (_intro != null)
        {
            _intro.Skip();
        }
    }

    private void OnIntroFinished()
    {
        _intro = null;
        GetTree().Paused = false;
        StartEntrySequenceInternal(); // 开场动画后播战机入场动画（替代原地无敌闪现）
    }

    /// <summary>播放返航过场：与 _play_intro_cinematic 同构（冻结对局，树暂停，process_mode=Always 播放）。
    /// BGM 引用交给过场做镜头 7 渐暗期淡出（_bgm_player 异步创建，取值判空）。测试可直接调用。</summary>
    private void PlayReturnCinematic()
    {
        if (_return != null)
        {
            return;
        }

        _return = ReturnScene.Instantiate<ReturnCinematic>();
        _return.Finished += OnReturnFinished;
        if (_bgmPlayer != null)
        {
            _return.BgmPlayer = _bgmPlayer;
        }

        AddChild(_return);
        GetTree().Paused = true;
    }

    /// <summary>Esc 经 BackNavigator 路由至此；任意键/点击由过场自身 _unhandled_input 捕获</summary>
    private void SkipReturnInternal()
    {
        if (_return != null)
        {
            _return.Skip();
        }
    }

    /// <summary>跳过与自然结束同一出口：基地 UI 在黑场下淡入；树保持暂停（基地界面本就是暂停态 UI）。
    /// BGM 已在过场镜头 7 淡出到 -40dB（或 skip 时立即置位），此处以 -30dB 淡入恢复（基地氛围）</summary>
    private void OnReturnFinished()
    {
        _return = null;
        _baseUi.ShowBase();
        if (_bgmPlayer != null)
        {
            var bgmTween = CreateTween();
            bgmTween.SetPauseMode(Tween.TweenPauseMode.Process); // 树保持暂停，tween 需照常推进
            bgmTween.TweenProperty(_bgmPlayer, "volume_db", -30.0f, 1.0);
        }
    }

    private void OnContinueRun()
    {
        var data = GameState.Instance.LoadRunData();
        if (data.Count == 0)
        {
            // 存档损坏已被 GameState 隔离备份：回退为新对局（数据层本就在默认态），不留死路径
            ApplyNewRun();
            return;
        }

        GameState.Instance.ApplyRunSave(data);
        var fuelV = data.GetValueOrDefault("fuel", Variant.From(_player.FuelMax));
        _player.SetFuel((float)GameState.Instance.SaveNum(fuelV, _player.FuelMax));
        var elapsedV = data.GetValueOrDefault("elapsed", Variant.From(0.0f));
        _spawner.SetElapsed((float)GameState.Instance.SaveNum(elapsedV, 0.0f));
        // D01 印证：continue 后同样存在入场动画窗口（敌机生成延迟由入场序列接管），
        // 与开场 _on_intro_finished / 继续出击 _on_orbital_struck 同构；is_connected 守卫可幂等调用
        StartEntrySequenceInternal();
    }

    private void OnPlayerDied()
    {
        _gameOver = true;
        // 迷雾事件：死亡终局清除进行中的事件（伪敌机/变色层/玩家效果信号复位）
        _fogEvents.EndActive();
        // 玩家死亡兜底：输入/狂暴移动锁立即解除（锁计时器随暂停冻结，不能依赖它解锁）
        _player.UnlockInput();
        _player.MovementLocked = false;
        // 死亡终局冻结 _process：狂暴子弹时间不复位会卡在 0.24（B2 修复）
        ResetGlobalTimeScale();
        // C25：死亡路径清理蓄力特效残留（_give_up 经 player_died 覆盖到此）
        StopChargingInternal();
        // 2026-08-06 审计：死亡路径清理召唤小窗（原仅返航路径清理）——give_up 与 dock
        // 蓄力同按 3s 同帧完成时小窗打开同帧死亡，finished 无人消费（_process 已冻结）小窗永驻
        if (_summonWindow != null)
        {
            _summonWindow.Finished -= OnSummonWindowFinished;
            _summonWindow.Skip();
            _summonWindow = null;
        }

        // B 梯队：死亡回放演出（幽灵弹幕重放死因 3s，播完自毁；process_mode=ALWAYS 暂停中照常）
        AddChild(_replay.Play());
    }

    /// <summary>对局终态复位全局速度（B2 修复）：返航/死亡/放弃路径会冻结 _process，
    /// 狂暴子弹时间（time_scale=0.24）不显式复位会卡到下次场景重载
    /// （返航过场 4 倍慢速播放直到轨道打击才自愈）。</summary>
    private void ResetGlobalTimeScale()
    {
        _bulletTimeLeft = 0.0f;
        _timeScaleRamp = -1.0f;
        _enrageBoss = null;
        Engine.TimeScale = 1.0f;
    }

    /// <summary>Boss 入场时挂接狂暴信号（狂暴弹幕/子弹时间由 main 统一编排）。
    /// U17：具名无参回调替代闭包捕获——闭包无法配对断开，Boss 若改对象池复用即双订阅；
    /// 上一只 Boss 仍存活时先断开再挂新（防双订阅）</summary>
    private void OnBossSpawned(Boss boss)
    {
        if (_enrageBoss != null && GodotObject.IsInstanceValid(_enrageBoss))
        {
            _enrageBoss.Enraged -= OnBossEnragedNoArg;
        }

        _enrageBoss = boss;
        boss.Enraged += OnBossEnragedNoArg;
    }

    private void OnBossEnragedNoArg()
    {
        var boss = _enrageBoss;
        if (boss != null && GodotObject.IsInstanceValid(boss))
        {
            OnBossEnraged(boss);
        }
    }

    /// <summary>狂暴触发：1.2s 子弹时间（全局 0.24，玩家同样减速——与原作一致）+ 泛红演出。
    /// 既有震动/警告音在 boss._enrage() 内；快照弹幕等子弹时间结束后才发。</summary>
    private void OnBossEnraged(Boss boss)
    {
        if (_gameOver || _homecoming)
        {
            return;
        }

        _enrageBoss = boss;
        _bulletTimeLeft = ENRAGE_BULLET_TIME;
        _timeScaleRamp = -1.0f;
        Engine.TimeScale = ENRAGE_SLOW_SCALE;
        EnrageVignette();
    }

    /// <summary>子弹时间结束：Boss 仍在场则发快照弹幕（作为 TRANSITION 收尾的一波；
    /// 玩家移动冻结/锁血由 Boss 狂暴序列自行管理）</summary>
    private void FireEnrageSnapshot()
    {
        var boss = _enrageBoss;
        _enrageBoss = null;
        if (boss == null || !GodotObject.IsInstanceValid(boss) || boss.IsQueuedForDeletion())
        {
            return; // Boss 在子弹时间内已被击杀/逃跑：time_scale 已恢复，无需弹幕
        }

        boss.FireEnrageSnapshot();
    }

    /// <summary>狂暴演出：全屏短暂泛红（tween 挂在 Always 层上，暂停时也能播完并自清）</summary>
    private void EnrageVignette()
    {
        var layer = new CanvasLayer
        {
            Layer = 30,
            ProcessMode = Node.ProcessModeEnum.Always,
        };
        AddChild(layer);
        var rect = new ColorRect
        {
            Color = new Color(0.85f, 0.05f, 0.05f, 0.0f),
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        rect.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(rect);
        var tween = layer.CreateTween();
        tween.TweenProperty(rect, "color:a", 0.35f, 0.15);
        tween.TweenProperty(rect, "color:a", 0.0f, 0.55);
        tween.TweenCallback(Callable.From(layer.QueueFree));
    }

    /// <summary>母舰状态文本（HUD 轮询）</summary>
    public string DockStatusText()
    {
        if (_charging)
        {
            return GdFormat((string)Tr("MS_CHARGING"), (int)(_chargeTime / DOCK_CHARGE_TIME * 100.0f));
        }

        if (_summonWindow != null)
        {
            return Tr("MS_DESCEND");
        }

        if (_mothership != null)
        {
            return _mothership.StateText();
        }

        if (_dockCooldown > 0.0f)
        {
            return GdFormat((string)Tr("MS_COOLDOWN"), Mathf.CeilToInt(_dockCooldown));
        }

        return Tr("MS_READY");
    }

    /// <summary>召唤序列（蓄力完成）：锁输入 + 事件驱动无敌（演出期对局不暂停，保护窗口与
    /// 对接期一致），弹出机库小窗演出；小窗 finished 后开穿梭门、母舰穿出</summary>
    private void SummonMothershipInternal()
    {
        if (_summonWindow != null)
        {
            return;
        }

        // 成功路径保底隐藏蓄力特效（自然流程 _stop_charging 已处理；测试直调走此分支）
        _chargeFx.Visible = false;
        _chargeInflow.Emitting = false;
        _player.LockInput();
        _player.Velocity = Vector2.Zero;
        _player.SetInvincible(999.0f);
        _summonWindow = new MothershipSummonWindow();
        _summonWindow.Finished += OnSummonWindowFinished;
        AddChild(_summonWindow);
    }

    /// <summary>小窗演出结束：在母舰停驻点打开穿梭门，母舰穿出减速入场（DESCEND 由母舰自驱；
    /// 到位后减速带 + 火力掩护 + 牵引回收进保护舱，均由母舰状态机接管）</summary>
    private void OnSummonWindowFinished()
    {
        _summonWindow = null;
        var gatePos = new Vector2(GameState.Instance.ViewWorldRect().GetCenter().X, _chargeGhost.HoverY);
        var gate = new WarpGate
        {
            Position = gatePos,
        };
        AddChild(gate);
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.mothership_summon.shake_gate", 6.0).AsDouble());
        _mothership = MothershipScene.Instantiate<Mothership>();
        _mothership.BeginWarpIn(gatePos, gate);
        _mothership.Departed += OnMothershipDepartedInternal;
        _mothership.TreeExited += () => _mothership = null;
        AddChild(_mothership);
    }

    private void OnMothershipDepartedInternal(float cooldown)
    {
        // mothership_recall buff：每层冷却 ×0.5（60s→30s→15s）
        _dockCooldown = cooldown
            * Mathf.Pow(
                (float)GameState.Instance.Cfg("buffs.mothership_recall.cooldown_factor", 0.5).AsDouble(),
                GameState.Instance.BuffCount(new StringName("mothership_recall")));
    }

    /// <summary>放弃出击（长按 K 3s）：自毁，走正常死亡结算（删档/最高分/结算面板）</summary>
    private void GiveUp()
    {
        var health = (float)GameState.Instance.Health;
        if (_player.IsDead() || health <= 0.0f)
        {
            return;
        }

        GameState.Instance.LoseHealth(health);
        _player.Die();
    }

    /// <summary>返航（局内中场整备）：锁输入、星光拉伸 + 返航过场，过场结束后进入基地控制台。
    /// 对局继续：不删档（反而更新存档）、Boss 保留、死亡才是唯一终局。</summary>
    private void StartHomecomingInternal()
    {
        _homecoming = true;
        // 返航冻结对局：狂暴子弹时间若在播先复位，避免过场以慢速播放（B2 修复）
        ResetGlobalTimeScale();
        // C25：返航路径清理蓄力特效残留（蓄力中按 B 返航时虚影/特效不再残留）
        StopChargingInternal();
        _homeChargeTime = 0.0f;
        _hud.SetHomeCharge(-1.0f);
        _player.LockInput();
        // 迷雾事件：返航中场整备清除进行中的干扰效果（继续出击后干净开局）
        _fogEvents.EndActive();
        // K01：返航统一清除召唤/对接期残留的 999s 无敌——_summon_mothership 设 set_invincible(999.0)，
        // 提前收回路径（下方 queue_free → mothership._exit_tree 仅 exit_pod 恢复显示）不重置无敌，
        // 正常 RELEASE 路径有 set_invincible(2.0) 覆盖；此处复位后继续出击的无敌由入场序列接管
        _player.SetInvincible(0.0f);
        _player.AbortEntry(); // D06：入场中按 B 返航时复位入场状态机（防新入场被守卫跳过）
        _player.Velocity = Vector2.Zero;
        _spawner.SetProcess(false);
        // D01：释放排队中的敌机/Boss 预告与一次性回调，防 continue 后入场动画窗口内进场
        _spawner.ClearPending();
        // 召唤小窗在播则断开回调后关闭（避免 finished 触发穿梭门/母舰创建）
        if (_summonWindow != null)
        {
            _summonWindow.Finished -= OnSummonWindowFinished;
            _summonWindow.Skip();
            _summonWindow = null;
        }

        // 母舰若在对接/驻留中，直接收回——按基础冷却进冷却（防"补给→返航→再召唤"无限循环）
        if (_mothership != null)
        {
            _mothership.QueueFree();
            OnMothershipDepartedInternal((float)GameState.Instance.Cfg("mothership.depart_cooldown", 60.0).AsDouble());
        }

        // 遭遇事件（轰炸编队/精英炮塔）进行中则打断：编队解散离场/航母完整撤离，无结算，
        // 冷却照计；由统一事件管理器统一 abort（Boss 解冻走事件自身 BOSS_DELAY 流程）
        _events.EndActive(_events.GROUP_ENCOUNTER);
        // 返航后存档保留更新，供「继续对局」使用
        GameState.Instance.SaveRun(_player.FuelAmount(), _spawner.Elapsed());
        _starfield.Warp(18.0f); // 保留：过场镜头 1 的充能与星光拉伸自然衔接
        PlayReturnCinematic();
    }

    /// <summary>继续出击：播放轨道打击清场动画（对齐原作 ORBITAL_STRIKE 阶段；树保持暂停）。
    /// 命中帧（struck）清场并恢复对局，动画结束（finished）仅释放引用。</summary>
    private void ResumeFromBaseInternal()
    {
        if (_strike != null)
        {
            return;
        }

        _strike = new OrbitalStrike();
        _strike.Struck += OnOrbitalStruck;
        _strike.Finished += OnOrbitalStrikeFinished;
        AddChild(_strike);
    }

    /// <summary>轨道打击命中：注册表驱动清场——Enemy（含池化）/FormationCraft/事件残留逐机触发爆炸
    /// 后移除（Boss 保留），再清全部弹丸与编队炸弹，恢复同一局</summary>
    private void OnOrbitalStruck()
    {
        // 统一实体管理器批量 API（docs/ENTITY_MANAGER.md）：非 Boss 单位逐机播爆炸
        // （原 GDScript for_each_enemy 以 Callable 谓词过滤；C# 侧无 Func 重载，
        // 语义等价直迭代 GameState.enemies 注册表——M2 注释即预告随调用方迁移由 C# 实现）
        var enemies = (Godot.Collections.Array)GameState.Instance.Enemies;
        foreach (var nodeV in enemies)
        {
            var e = nodeV.AsGodotObject() as Node;
            if (e == null || !GodotObject.IsInstanceValid(e))
            {
                continue;
            }

            if (e is Boss)
            {
                continue; // Boss 保留
            }

            if (e is Node2D n2d)
            {
                Explosion.SpawnAt(this, n2d.GlobalPosition);
            }
        }

        // 批量清除（Boss 保留）；queue_free 延迟释放，迭代期间注册表不变，无需 duplicate
        foreach (var nodeV in enemies)
        {
            var e = nodeV.AsGodotObject() as Node;
            if (e == null || !GodotObject.IsInstanceValid(e))
            {
                continue;
            }

            if (e is Boss)
            {
                continue;
            }

            e.QueueFree();
        }

        foreach (var child in GetChildren())
        {
            if (child is Bullet || child is FormationBomb)
            {
                child.QueueFree();
            }
        }

        _player.UnlockInput();
        _homecoming = false;
        GetTree().Paused = false;
        // 继续出击后播战机入场动画：无敌与敌机延迟由入场序列接管（替代原地无敌闪现）
        StartEntrySequenceInternal();
    }

    /// <summary>入场衔接（开场/继续出击后）：播战机入场动画，敌机生成延迟到动画结束才恢复</summary>
    private void StartEntrySequenceInternal()
    {
        _spawner.SetProcess(false);
        if (!_entryFinishedConnected)
        {
            _player.EntryFinished += OnEntryFinished; // M3c：C# [Signal] 以 PascalCase 注册
            _entryFinishedConnected = true;
        }

        _player.PlayEntryAnimation();
    }

    private void OnEntryFinished() => _spawner.SetProcess(true);

    private void OnOrbitalStrikeFinished() => _strike = null;

    private void OnResumeFromBase() => ResumeFromBaseInternal();

    private void OnTouchControlsChanged(bool enabled) => _virtualControls.SetEnabled(enabled);

    /// <summary>GDScript 字符串 % 格式化语义（%s/%d/%f 占位 + %% 转义；tr() 文案补参用，
    /// C# 无 % 运算符；数组参数按序填占位）。与 BaseConsole.GdFormat 同实现。</summary>
    private static string GdFormat(string format, params object[] args)
    {
        var sb = new System.Text.StringBuilder(format.Length + 16);
        var argIndex = 0;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '%' && i + 1 < format.Length)
            {
                var spec = format[i + 1];
                if (spec == '%')
                {
                    sb.Append('%');
                    i++;
                    continue;
                }

                if (spec == 's' || spec == 'd' || spec == 'f')
                {
                    sb.Append(argIndex < args.Length ? args[argIndex] : "?");
                    argIndex++;
                    i++;
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    // ---------------- GDScript 鸭子调用兼容桥（M6 过渡，M7 删除） ----------------
    // 调用方：scripts/back_navigator.gd（M5 已迁 C# BackNavigator，_main.Call("skip_intro"/"skip_return"/
    // "is_intro_playing"/"is_return_playing"/"is_game_over"/"is_homecoming")）+
    // test/{smoke,autoplay,back_navigation,intro_cinematic,return_cinematic,boss_enrage,boss_phase,
    // boss_pattern,boss_phase_transition,orbital_strike,buff33,summon_capture,visual_capture,
    // ui_capture,encounter_flow_contract,mothership_summon,mothership_upgrade,view_zoom,
    // event_manager,formation_strike_event,elite_turret_event,virtual_controls,meta_health_fx}_test
    // （player/mothership/strike/return_cinematic/summon_window/intro/formation/event/summon_mothership/
    // play_return/dock_cooldown/charge_ghost/set_bullet_time/play_intro/base_ui/skip_return/is_homecoming/
    // hud/on_mothership_departed/charging/is_game_over/start_homecoming/set_game_over/summon_window/
    // set_charge_time/stop_charging/start_entry_sequence/set_homecoming/set_dock_cooldown/resume_from_base/
    // pause_ui/give_up_charge/bullet_time/time_scale_ramp/dock_status_text/meta_fx/continue_run）。





    public Mothership? mothership() => Mothership();

    public Player player() => Player();

    public Hud hud() => Hud();

    public BaseConsole base_ui() => BaseUi();

    public PauseUi pause_ui() => PauseUi();


    public Node? @event() => Event();

    public Node? formation() => Formation();

    public OrbitalStrike? strike() => Strike();

    public MothershipSummonWindow? summon_window() => SummonWindow();






    public void skip_intro() => SkipIntro();


    public void skip_return() => SkipReturn();








    public bool charging() => Charging();



    public void continue_run() => ContinueRun();




    public IntroCinematic? intro() => Intro();

    public ReturnCinematic? return_cinematic() => ReturnCinematic();

}
