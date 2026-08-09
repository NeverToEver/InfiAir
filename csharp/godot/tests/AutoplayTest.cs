using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 模拟人工游玩探针（M7c 迁移自 test/autoplay_test.gd）：实例化真实对局（scenes/main.tscn），
/// 登录固定探针用户真实开局，用合成输入（Input.ActionPress/ActionRelease + Input.ParseInputEvent 合成鼠标移动）
/// 像真人一样游玩一整局，全程日志化。
///
/// 行为覆盖：随机走位/规避、Buff 三选一（优先未拥有过的种类，争取覆盖 19 种）、
/// 低血返航 B（基地 4 模块全操作：维修/补给/天赋路线/任务领奖）、召唤母舰 H
/// （含蓄力主动取消、非驻留态乱按、驻留驾驶、提前离舰、驻留到超时强制弹射）、
/// 狂暴子弹时间高频冲刺、随机暂停/恢复 + 暂停菜单「保存进度」、
/// 主动重进 main 走启动自动读档恢复（StartPanel 已退役后的「继续对局」路径）、
/// 对局中轮换设置（视角/窗口/语言/难度，数秒后切回）、
/// 暂停菜单退出确认窗「打开→取消」探针（永不确认退出，避免杀掉测试进程）。
/// 监控维度：每 5s 快照记录 Performance 监控器（对象/节点/孤儿节点/静态内存/FPS）、
/// 节点数与对象数上涨趋势、GameState.enemies 注册表 vs enemy 组场景集合双向差集一致性、
/// player_ref/对象池引用有效性、池规模上界、帧耗时恶化趋势，外加既有的
/// 卡死/数值越界/实体爆增/UI 状态一致性检查。异常用 GD.PushError("[ANOMALY] ...") 记录但不中断。
///
/// 探针性质：除崩溃/硬断言外不以 FAIL 结束，结尾打印 SUMMARY 供人工/脚本分析。
/// 引擎层 ERROR/WARNING 统计需在外部对 stderr 计数（进程内读不到自身 stderr）。
///
/// 运行：godot --headless --path . res://test/autoplay_test.tscn [-- --autoplay-seconds=480] [-- --seed=N]
/// </summary>
public partial class AutoplayTest : Node
{
    private const int FightP2 = (int)Boss.FightPhase.P2;  // M3d：Boss.FightPhase.P2（0/1/2 声明序）

    private const float TimeScale = 2.0f;  // 加速倍率（狂暴子弹时间期间让行给 main 编排，结束后恢复）
    private const double DefaultRunSeconds = 480.0;  // 真实时间预算（≈16 分钟游戏时间 @2x）
    private const int SnapshotIntervalMs = 5000;
    private const int CheckIntervalMs = 500;
    private const int MoveDecisionMs = 120;
    private const int AimIntervalMs = 250;
    private const double RestartMinRemainingS = 60.0;  // 剩余预算不足则死亡后不再重开

    // 卡死阈值（真实毫秒，已按 time_scale 放宽）
    private const int BuffStuckMs = 10000;
    private const int BossTimeoutMs = 120000;
    private const int HomeStuckMs = 8000;
    private const int BaseStuckMs = 20000;
    private const int ScoreStagnantMs = 60000;
    private const int SlowStuckMs = 15000;  // 狂暴减速残留判定（无狂暴 Boss 但 _enrage_slow 未复位）
    // 母舰各状态预期最长停留（State 枚举序 -> ms）
    // L01（2026-08-03 审查）：母舰状态表与 Mothership.State 枚举对齐（6 态，无 HOVER——
    // 原 7 项表把 DOCKING/RESUPPLY/STAY/RELEASE/DEPART 整体错位一档：真实 STAY 被配 10s
    // 阈值导致驻留期必误报 mothership_stuck，RELEASE 被配 70s 卡死漏报）
    private static readonly int[] MsStateTimeouts = new[] { 20000, 10000, 10000, 70000, 10000, 30000 };
    private static readonly string[] MsStateNames = new[] { "DESCEND", "DOCKING", "RESUPPLY", "STAY", "RELEASE", "DEPART" };
    // 实体爆增阈值
    private const int MaxPlayerBullets = 300;
    private const int MaxEnemyBullets = 800;
    private const int MaxEnemies = 120;
    // 池规模上界（闲置实例数应稳定在峰值并发附近；持续超过即疑似复用再次失效）
    private const int MaxBulletPool = 150;
    private const int MaxEnemyPool = 100;
    // 行为节奏
    private const int PauseGapMs = 20000;  // 随机暂停最小间隔
    private const int SettingGapMs = 25000;  // 设置切换最小间隔
    private const int SettingRestoreMs = 4500;  // 切出后多久切回
    private const int MenuReturnDelayMs = 1200;  // 暂停存档后重进 main 的延迟
    // 对象数泄漏判定（快照连续上涨次数与相对基线倍数）
    private const int ObjectLeakStreak = 4;
    private const double ObjectLeakRatio = 1.8;

    private static readonly Godot.Collections.Array<StringName> MoveActions = ["move_left", "move_right", "move_up", "move_down"];
    private static readonly Godot.Collections.Array<StringName> SettingKinds = ["view_zoom", "window_size", "locale", "difficulty", "aim_assist", "reduce_flash", "ctrl_toggle", "shift_toggle"];
    private const int BuffPoolSize = 19;  // buff_select.gd BUFF_POOL 种类数（覆盖率统计分母；2026-08-05 P4 修正 16→19）

    // buff_select.gd BUFF_POOL 的 max 值镜像（C# 侧 BuffSelect.BuffPool 为 private static 无公开访问器；
    // 口径同 buff_select.gd：cfg 覆盖池内默认；池内容调整需同步此处）
    private static readonly Godot.Collections.Dictionary BuffPoolMax = new()
    {
        ["power_shot"] = 5,
        ["rapid_fire"] = 4,
        ["spread_shot"] = 3,
        ["extra_life"] = 10,
        ["regen"] = 1,
        ["piercing"] = 2,
        ["explosive"] = 1,
        ["lifesteal"] = 1,
        ["armor"] = 1,
        ["evasion"] = 1,
        ["phase_dash"] = 3,
        ["slow_field"] = 1,
        ["efficient_boost"] = 2,
        ["laser_beam"] = 1,
        ["boost_recovery"] = 2,
        ["mothership_recall"] = 2,
        ["crit_shot"] = 3,
        ["shield"] = 2,
        ["bullet_speed"] = 3,
    };

    private double _runSeconds = DefaultRunSeconds;
    private int _seed = 20260722;
    private bool _started;
    private bool _finished;
    private long _t0Msec;
    private long _lastSnapMsec;
    private long _lastCheckMsec;

    // 对局引用（重开时刷新）
    private Main? _main;  // main 无 class_name 的旧注释已过时：Main 已迁 C#，typed 访问
    private Player? _player;
    private Spawner? _spawner;
    private BuffSelect? _buffUi;
    private Boss? _boss;

    // bot 状态
    private Vector2 _moveTarget;
    private long _nextMoveDecision;
    private long _nextAim;
    private long _dashReleaseAt;
    private long _nextDashTry;
    private bool _dockHolding;
    private long _dockHoldUntil;
    private bool _dockCancelEpisode;  // 本次蓄力是"主动取消"探针
    private long _nextDockConsider;
    private long _staySince;
    private bool _stayUntilEject;  // 本次驻留等到超时强制弹射
    private long _earlyLeaveAt;
    private bool _earlyHolding;
    private long _earlyHoldUntil;
    private bool _homeHolding;
    private long _homeHoldUntil;
    private long _nextHomeConsider;
    private long _restartAt;
    // 暂停链路
    private long _nextPauseConsider;
    private long _pauseOpenSince;
    private int _pauseStage;
    private long _settingsOpenSince;  // 暂停菜单内打开设置页的时刻
    private long _menuReturnAt;  // >0：到点重进 main 走自动读档恢复
    // B 梯队（2026-08-03 fair plan §8）+ Phase 0 L13 探针更新：
    // DDA 降档状态跟踪 / 死亡回放节点泄漏 / 母舰×事件互斥
    private long _lastDamagedMsec = -1;  // 最近一次玩家受击（player_damaged 信号）
    private int _ddaTriggerCount;  // DDA 降档触发次数（受击且此前未激活）
    private bool _ddaStuckReported;
    private Node? _replayNode;  // 当前死亡回放演出节点（DeathReplayPlayer）
    private long _replaySince;
    private bool _replayStuckReported;
    private int _replaySeenCount;  // 死亡回放演出观察次数
    private const int DdaStuckMs = 9000;  // dda.duration=5s；无受击超 9s 仍激活 = 降档卡死
    private const int ReplayStuckMs = 5000;  // 重放 3s 播完；超 5s 未自毁 = 泄漏
    // 设置轮换
    private long _nextSettingAt;
    private long _settingRestoreAt;
    private Godot.Collections.Dictionary _settingRestore = new();  // {kind, old}

    // 事件/异常 episode 状态
    private long _buffOpenSince;
    private long _buffPickAt;
    private bool _buffStuckReported;
    private long _bossSince;
    private bool _bossTimeoutReported;
    private int _msLastState = -1;
    private long _msStateSince;
    private bool _msStuckReported;
    private long _homecomingPendingSinceMs;  // 返航过场结束后才开始计时（过场期每检查点顺延）
    private bool _homeStuckReported;
    private long _slowSince;  // 玩家狂暴减速但无狂暴 Boss 的持续起点
    private bool _slowReported;
    private long _baseSince;
    private int _baseStage;
    private bool _baseStuckReported;
    private int _lastScore = -1;
    private long _scoreChangeMsec;
    private bool _scoreStagReported;
    private int _nodeBaseline;
    private int _nodePrev;
    private int _nodeRiseStreak;
    private bool _nodeLeakArmed = true;
    private double _lastHp = -1.0;
    private long _lastHitLogMsec;
    private Godot.Collections.Dictionary _anomalyRlLast = new();  // category -> last msec（数值类异常 10s 限频）
    // 引擎级监控
    private double _objBaseline;
    private double _objPrev;
    private int _objRiseStreak;
    private bool _objLeakArmed = true;
    private int _frameCount;
    private long _frameSnapMsec;
    private double _frameMsBaseline;
    private int _frameSnaps;
    private int _frameSlowStreak;
    private double _maxFrameMs;

    // 统计
    private int _runIndex;
    private int _deaths;
    private int _totalKills;
    private int _totalBossKills;
    private readonly System.Collections.Generic.List<int> _runScores = new();
    private int _buffPicks;
    private int _buffAnimatedPicks;  // 走三参确认动效路径的选取次数
    private readonly Godot.Collections.Dictionary _buffsSeen = new();  // id -> true（跨局累计，覆盖率统计）
    private int _msSummons;
    private int _chargeCancels;
    private int _earlyLeaves;
    private int _forcedEjects;
    private int _homecomings;
    private int _pauseSaves;
    private int _settingsOpens;
    private int _continueResumes;
    private int _exitProbes;
    private int _settingSwitches;
    private int _milestones;
    private int _bossEscapes;
    private int _baseRepairs;
    private int _baseRecharges;
    private int _routeChoices;
    private int _missionClaims;
    private int _bossP2Count;
    private int _bossEnrageCount;
    private int _turretEventCount;
    private int _formationEventCount;
    private bool _eventWasActive;
    private bool _formationWasActive;
    private int _maxNodes;
    private int _maxEnemyBullets;
    private int _maxPlayerBullets;
    private int _maxEnemies;
    private double _maxOrphans;
    private int _maxBulletPool;
    private int _maxEnemyPool;
    private readonly Godot.Collections.Dictionary _anomalyCounts = new();
    private readonly Godot.Collections.Dictionary _anomalyFirst = new();
    // 进程初始设置（结束恢复，同难度既有做法）
    private StringName _prevDifficulty = new("medium");
    private StringName _prevViewZoom = new("medium");
    private StringName _prevWindowSize = new("large");
    private string _prevLocale = "zh";
    private StringName _prevAimAssist = new("medium");
    private bool _prevReduceFlash;
    private bool _prevCtrlToggle;
    private bool _prevShiftToggle;

    private GameState _gs = null!;

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always;  // 暂停（Buff/结算/基地）时也要继续驱动 bot
        try
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--autoplay-seconds="))
                {
                    _runSeconds = double.Parse(arg.Substring("--autoplay-seconds=".Length), System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (arg.StartsWith("--seed="))
                {
                    _seed = int.Parse(arg.Substring("--seed=".Length), System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            GD.Seed((ulong)_seed);
            _gs = GetNode<GameState>("/root/GameState");
            // 确定性：清残留存档；固定 medium 难度（结束恢复原档位，同 smoke_test 做法）
            _gs.DeleteSave();
            _prevDifficulty = _gs.Difficulty;
            _prevViewZoom = _gs.ViewZoom;
            _prevWindowSize = _gs.WindowSize;
            _prevLocale = _gs.Locale;
            _prevAimAssist = _gs.AimAssistLevel;
            _prevReduceFlash = _gs.ReduceFlash;
            _prevCtrlToggle = _gs.CtrlToggleMode;
            _prevShiftToggle = _gs.ShiftToggleMode;
            _gs.SetDifficulty(new StringName("medium"));
            _gs.MilestoneReached += OnMilestone;
            _gs.PlayerDied += OnPlayerDied;
            _gs.HealthChanged += OnHealthChanged;
            // B 梯队：DDA 降档由受击信号驱动（同 Meta HUD 受击层同源）
            _gs.PlayerDamaged += OnPlayerDamaged;
            _t0Msec = (long)Time.GetTicksMsec();
            _lastSnapMsec = _t0Msec;
            _lastCheckMsec = _t0Msec;
            _frameSnapMsec = _t0Msec;
            _nextSettingAt = _t0Msec + SettingGapMs;
            _nextPauseConsider = _t0Msec + PauseGapMs;
            Log($"START budget={_runSeconds:0}s real (time_scale={TimeScale:0.0}) seed={_seed}");
            _ = StartRun();
        }
        catch (System.Exception e)
        {
            // 探针不以 FAIL 结束：初始化异常也走正常退出码
            GD.PushError($"[ANOMALY] [{ElapsedS(),7:0.0}s] [test_exception] 初始化异常: {e}");
            TestExit.Quit(0);
        }
    }

    /// <summary>实例化 main 走真实开局路径（登录固定探针用户；有档时 main 启动自动读档继续——T4 适配）</summary>
    private async Task StartRun(bool pContinue = false)
    {
        _runIndex++;
        Log($"=== RUN {_runIndex} START（{(pContinue ? "继续对局" : "新游戏")}） ===");
        if (!_gs.UserExists("autoplay_runner"))
        {
            _gs.CreateUser("autoplay_runner", "autoplay_pass");
        }
        _gs.LoginUser("autoplay_runner");
        if (!pContinue)
        {
            _gs.DeleteSave();  // 新局清该用户存档
        }
        _main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        // 本测试根节点必须是 ALWAYS（暂停期间继续驱动 bot），但 Main 继承根节点的模式
        // 会导致整个对局在暂停时照跑——显式把 Main 钉回 PAUSABLE，还原真实暂停语义。
        _main.ProcessMode = Node.ProcessModeEnum.Pausable;
        AddChild(_main);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // 退出确认窗探针（暂停→退出→取消，绝不确认，避免 quit 杀进程）
        if (GD.Randf() < 0.3f)
        {
            ProbeExitConfirm();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (pContinue)
        {
            _continueResumes++;
            Log($"继续对局：main 启动自动读档恢复（第 {_continueResumes} 次，存档 score={_gs.Score}）");
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _player = _main.GetNode<Player>("Player");
        _spawner = _main.GetNode<Spawner>("Spawner");
        _buffUi = _main.GetNode<BuffSelect>("BuffUI");
        _spawner.BossSpawned += OnBossSpawned;
        _moveTarget = _player.Position;
        _lastHp = _gs.Health;
        _lastScore = _gs.Score;
        _scoreChangeMsec = (long)Time.GetTicksMsec();
        Engine.TimeScale = TimeScale;
        _started = true;
    }

    /// <summary>退出确认窗「打开→取消」探针：覆盖 BackNavigator CANCEL_EXIT 分支（暂停→退出游戏→取消）</summary>
    private void ProbeExitConfirm()
    {
        var main = _main!;
        var exitConfirm = main.GetNode<ExitConfirm>("ExitConfirm");
        var pauseUi = main.GetNode<PauseUi>("PauseUI");
        pauseUi.Open();
        pauseUi.Quit();
        if (!exitConfirm.Visible)
        {
            Anomaly("exit_confirm_no_show", "暂停→退出游戏未弹出确认窗");
            pauseUi.Close();
            return;
        }
        main.GetNode<BackNavigator>("BackNavigator").GoBack();  // 确认窗可见 → CANCEL_EXIT
        if (exitConfirm.Visible)
        {
            Anomaly("exit_confirm_stuck", "退出确认窗取消后仍可见");
            return;
        }
        pauseUi.Close();
        _exitProbes++;
        Log($"退出确认窗 打开→取消 探针通过（第 {_exitProbes} 次）");
    }

    public override void _Process(double delta)
    {
        if (!_started || _finished)
        {
            return;
        }
        long now = (long)Time.GetTicksMsec();
        _frameCount++;
        if (ElapsedS() >= _runSeconds)
        {
            Finish();
            return;
        }
        if (_main == null || !IsInstanceValid(_main))
        {
            return;  // 重开过渡帧
        }
        ReassertTimeScale();
        BotTick(now);
        if (now - _lastSnapMsec >= SnapshotIntervalMs)
        {
            _lastSnapMsec = now;
            Snapshot(now);
        }
        if (now - _lastCheckMsec >= CheckIntervalMs)
        {
            _lastCheckMsec = now;
            Checks(now);
        }
    }

    /// <summary>狂暴子弹时间（main.gd 直接写 Engine.time_scale=0.24→恢复 1.0）结束后恢复加速倍率</summary>
    private void ReassertTimeScale()
    {
        var main = _main!;
        if (main.BulletTime() <= 0.0f && main.TimeScaleRamp() < 0.0f && Engine.TimeScale != TimeScale)
        {
            Engine.TimeScale = TimeScale;
        }
    }

    // ---------------- bot 行为 ----------------

    private void BotTick(long now)
    {
        // 死亡重开
        if (_restartAt > 0 && now >= _restartAt)
        {
            _restartAt = 0;
            if (_runSeconds - ElapsedS() > RestartMinRemainingS)
            {
                _ = DoRestart();
            }
            else
            {
                Finish();
            }
            return;
        }
        // 主动重进 main（存档保留，启动自动读档恢复）
        if (_menuReturnAt > 0 && now >= _menuReturnAt)
        {
            _menuReturnAt = 0;
            _ = DoMenuReturn();
            return;
        }
        HandleBuffUi(now);
        HandleBaseUi(now);
        HandlePauseUi(now);
        bool playing = !GetTree().Paused && _player != null && !_player.IsDead();
        if (playing)
        {
            UpdateMovement(now);
            UpdateAim(now);
            UpdateDash(now);
            UpdateDock(now);
            UpdateHomecoming(now);
            UpdatePause(now);
            UpdateSettings(now);
        }
        TrackMothership(now);
    }

    private void HandleBuffUi(long now)
    {
        if (_buffUi == null || !IsInstanceValid(_buffUi))
        {
            return;
        }
        if (_buffUi.Visible)
        {
            if (_buffUi.Closing())
            {
                // 确认动效播放中：不重复 pick（动效结束才 visible=false）；动效期也纳入卡死计时
                if (_buffOpenSince == 0)
                {
                    _buffOpenSince = now;
                    _buffStuckReported = false;
                }
                return;
            }
            if (_buffOpenSince == 0)
            {
                _buffOpenSince = now;
                _buffStuckReported = false;
                _buffPickAt = now + 400 + (long)(GD.Randi() % 600);  // 模拟真人看牌时间
                var ids = new System.Collections.Generic.List<StringName>();
                foreach (var b in _buffUi.CurrentAvailable())
                {
                    ids.Add(b.AsGodotDictionary()["id"].AsStringName());
                }
                Log($"Buff 三选一弹出 candidates=[{string.Join(", ", ids)}]");
            }
            else if (now >= _buffPickAt)
            {
                var avail = _buffUi.CurrentAvailable();
                if (avail.Count > 0)
                {
                    // 优先选本进程尚未拥有过的种类，争取覆盖全部 buff 效果代码
                    var unseen = new Godot.Collections.Array();
                    foreach (var v in avail)
                    {
                        var b = v.AsGodotDictionary();
                        if (!_buffsSeen.ContainsKey(b["id"]))
                        {
                            unseen.Add(v);
                        }
                    }
                    var pool = unseen.Count > 0 ? unseen : avail;
                    var pick = pool[(int)(GD.Randi() % (uint)pool.Count)].AsGodotDictionary();
                    int pickIdx = -1;  // 候选卡在 _cards 中的索引（顺序与 _current_available 对应）
                    for (int i = 0; i < avail.Count; i++)
                    {
                        if (avail[i].AsGodotDictionary()["id"].AsString() == pick["id"].AsString())
                        {
                            pickIdx = i;
                            break;
                        }
                    }
                    var ev = new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left };
                    // 10% 走真实三参确认动效路径：合成鼠标左键事件发给目标卡片
                    // （card.gui_input → _on_card_gui_input 的 card!=null 分支 → _closing 动效，
                    // ~200ms 后 _on_pick_close_finished 才关闭面板并恢复对局）；其余走两参立即关闭
                    Control? card = null;
                    bool animated = false;
                    if (GD.Randf() < 0.10f && pickIdx >= 0 && _buffUi.Cards().GetChildCount() > pickIdx)
                    {
                        card = _buffUi.Cards().GetChild(pickIdx) as Control;
                    }
                    if (card != null)
                    {
                        card.EmitSignal(Control.SignalName.GuiInput, ev);
                        animated = true;
                    }
                    else
                    {
                        _buffUi.PickBuff(pick["id"].AsStringName());
                    }
                    _buffPicks++;
                    if (animated)
                    {
                        _buffAnimatedPicks++;
                    }
                    _buffsSeen[pick["id"]] = true;
                    // 选取后立即校验层数上限（口径同 buff_select.gd：cfg 覆盖池内默认）
                    int poolMax = 1;
                    if (BuffPoolMax.ContainsKey(pick["id"]))
                    {
                        poolMax = BuffPoolMax[pick["id"]].AsInt32();
                    }
                    int cap = _gs.Cfg($"buffs.{pick["id"].AsStringName()}.max_stacks", poolMax).AsInt32();
                    if (_gs.BuffCount(pick["id"].AsStringName()) > cap)
                    {
                        AnomalyRl("buff_over_cap", $"Buff {pick["id"].AsStringName()} 层数 {_gs.BuffCount(pick["id"].AsStringName())} 超过上限 {cap}", now);
                    }
                    Log($"Buff 选择: {pick["id"].AsStringName()}（层数 {_gs.BuffCount(pick["id"].AsStringName())}，已覆盖种类 {_buffsSeen.Count}/{BuffPoolSize}" + (animated ? "，动效路径" : "") + "）");
                }
                _buffOpenSince = 0;
            }
        }
        else
        {
            _buffOpenSince = 0;
        }
    }

    /// <summary>基地控制台全模块：维修 → 补给燃料 → 天赋路线选择 → 任务领奖 → 继续出击</summary>
    private void HandleBaseUi(long now)
    {
        if (_main == null || !IsInstanceValid(_main))
        {
            return;
        }
        var baseUi = _main.BaseUi();
        if (baseUi.Visible)
        {
            if (_baseSince == 0)
            {
                _baseSince = now;
                _baseStage = 0;
                _baseStuckReported = false;
                Log($"进入基地整备（RP={_gs.Rp} HP={_gs.Health:0}）");
                return;
            }
            long t = now - _baseSince;
            if (_baseStage == 0 && t >= 600)
            {
                _baseStage = 1;
                if (_gs.Rp >= _gs.RP_REPAIR_COST && _gs.Health < _gs.MaxHealth())
                {
                    baseUi.Repair();
                    _baseRepairs++;
                    Log($"基地维修：HP -> {_gs.Health:0}（RP={_gs.Rp}）");
                }
            }
            else if (_baseStage == 1 && t >= 1000)
            {
                _baseStage = 2;
                if (_gs.Rp >= _gs.RP_RECHARGE_COST && _player != null && _player.FuelAmount() < _player.FuelMax - 1.0f)
                {
                    baseUi.Recharge();
                    _baseRecharges++;
                    Log($"基地补给燃料：-> {_player.FuelAmount():0}（RP={_gs.Rp}）");
                }
            }
            else if (_baseStage == 2 && t >= 1400)
            {
                _baseStage = 3;
                foreach (var kv in _gs.ROUTE_LINES)
                {
                    var line = kv.Key.AsStringName();
                    if (_gs.ChosenRoutes.ContainsKey(line))
                    {
                        continue;  // 每线每局限选一次
                    }
                    var options = _gs.ROUTE_LINES[line].AsGodotArray();
                    int total = _gs.BuffCount(options[0].AsStringName()) + _gs.BuffCount(options[1].AsStringName());
                    if (total == 0)
                    {
                        continue;
                    }
                    var opt = options[(int)(GD.Randi() % (uint)options.Count)].AsStringName();
                    if (_gs.IsBuffLocked(opt))
                    {
                        opt = options[1].AsStringName() == opt ? options[0].AsStringName() : options[1].AsStringName();
                    }
                    if (_gs.IsBuffLocked(opt))
                    {
                        continue;
                    }
                    baseUi.ChooseRoute(line, opt);
                    _routeChoices++;
                    Log($"天赋路线选择：{line} -> {opt}（合并后 {_gs.BuffCount(opt)} 层）");
                }
            }
            else if (_baseStage == 3 && t >= 1800)
            {
                _baseStage = 4;
                // 任务轮换：领取在场已完成任务（active_mission_ids，非固定 MISSION_DEFS）
                foreach (var id in _gs.ActiveMissionIds())
                {
                    if (_gs.IsMissionDone(id) && !_gs.IsMissionClaimed(id))
                    {
                        baseUi.ClaimMission(id);
                        _missionClaims++;
                        Log($"领取任务奖励：{id}（RP={_gs.Rp}）");
                    }
                }
            }
            else if (_baseStage == 4 && t >= 2600)
            {
                baseUi.Resume();
                Log("继续出击，返回对局");
                _baseSince = 0;
            }
        }
        else
        {
            _baseSince = 0;
        }
    }

    /// <summary>随机暂停：Esc 打开暂停菜单（走 BackNavigator 真实路由）</summary>
    private void UpdatePause(long now)
    {
        if (now < _nextPauseConsider)
        {
            return;
        }
        _nextPauseConsider = now + PauseGapMs + (long)(GD.Randi() % 20000);
        if (GD.Randf() < 0.6f)
        {
            var main = _main!;
            main.GetNode<BackNavigator>("BackNavigator").GoBack();  // 战斗中 → OPEN_PAUSE
            if (main.PauseUi().Visible)
            {
                _pauseOpenSince = now;
                _pauseStage = 0;
                Log("暂停：Esc 打开暂停菜单");
            }
        }
    }

    /// <summary>暂停菜单链路：「保存进度」写档 →（50% 打开设置页再返回）→ 恢复对局（或重进 main 走自动读档）</summary>
    private void HandlePauseUi(long now)
    {
        if (_pauseOpenSince == 0)
        {
            return;
        }
        var main = _main!;
        var pauseUi = main.PauseUi();
        var settingsUi = main.GetNode<SettingsUi>("SettingsUI");
        if (!pauseUi.Visible && !settingsUi.Visible)
        {
            _pauseOpenSince = 0;  // 未打开成功或已被其他路径关闭
            return;
        }
        long t = now - _pauseOpenSince;
        if (_pauseStage == 0 && t >= 600)
        {
            _pauseStage = 1;
            if (GD.Randf() < 0.75f)
            {
                pauseUi.Save();
                _pauseSaves++;
                Log($"暂停菜单：保存进度（第 {_pauseSaves} 次）");
            }
        }
        else if (_pauseStage == 1 && t >= 1300)
        {
            // 50% 打开设置页（pause_ui 隐藏、SettingsUI 显示），随后 go_back 走
            // BackNavigator CLOSE_SETTINGS 真实路由返回（opener 恢复可见）
            if (GD.Randf() < 0.5f)
            {
                pauseUi.OpenSettings();
                if (settingsUi.Visible)
                {
                    _settingsOpenSince = now;
                    _pauseStage = 2;
                    Log("暂停菜单：打开设置页");
                }
                else
                {
                    _pauseStage = 3;
                }
            }
            else
            {
                _pauseStage = 3;
            }
        }
        else if (_pauseStage == 2 && now - _settingsOpenSince >= 900)
        {
            _pauseStage = 3;
            _settingsOpens++;
            main.GetNode<BackNavigator>("BackNavigator").GoBack();  // 设置页 → CLOSE_SETTINGS
            Log($"暂停菜单：设置页返回（第 {_settingsOpens} 次）");
        }
        else if (_pauseStage == 3 && t >= 2400)
        {
            _pauseStage = 4;
            _pauseOpenSince = 0;
            if (GD.Randf() < 0.35f && _gs.HasSave())
            {
                // 重进 main → 启动自动读档恢复（与新游戏不同的代码路径）
                pauseUi.Close();
                _menuReturnAt = now + MenuReturnDelayMs;
                Log("暂停恢复，稍后重进 main 走自动读档继续");
            }
            else
            {
                main.GetNode<BackNavigator>("BackNavigator").GoBack();  // 暂停中 → RESUME_GAME
                Log("暂停：Esc 恢复对局");
            }
        }
    }

    /// <summary>对局中轮换设置项（视角/窗口/语言/难度），数秒后切回：压测热路径上的信号处理器</summary>
    private void UpdateSettings(long now)
    {
        if (_settingRestoreAt > 0)
        {
            if (now >= _settingRestoreAt)
            {
                var restoreKind = _settingRestore["kind"].AsStringName();
                var restoreOld = _settingRestore["old"];
                ApplySetting(restoreKind, restoreOld);
                Log($"设置切回：{restoreKind} -> {Str(restoreOld)}");
                _settingRestoreAt = 0;
                _settingRestore = new Godot.Collections.Dictionary();
            }
            return;
        }
        if (now < _nextSettingAt)
        {
            return;
        }
        _nextSettingAt = now + SettingGapMs + (long)(GD.Randi() % 15000);
        var kind = SettingKinds[(int)(GD.Randi() % (uint)SettingKinds.Count)];
        Variant old;
        Variant newVal;
        switch (kind.ToString())
        {
            case "view_zoom":
                old = _gs.ViewZoom;
                newVal = PickOther(ToUntyped(new Godot.Collections.Array<Variant>(_gs.VIEW_ZOOM_LEVELS.Keys)), old);
                break;
            case "window_size":
                old = _gs.WindowSize;
                newVal = PickOther(ToUntyped(new Godot.Collections.Array<Variant>(_gs.WINDOW_SIZE_LEVELS.Keys)), old);
                break;
            case "locale":
                old = _gs.Locale;
                newVal = old.AsString() == "zh" ? "en" : "zh";
                break;
            case "difficulty":
                old = _gs.Difficulty;
                newVal = PickOther(ToUntyped(_gs.DIFFICULTY_ORDER), old);
                break;
            case "aim_assist":
                old = _gs.AimAssistLevel;
                newVal = PickOther(ToUntyped(_gs.AIM_ASSIST_ORDER), old);
                break;
            case "reduce_flash":
                old = _gs.ReduceFlash;
                newVal = !old.AsBool();
                break;
            case "ctrl_toggle":
                old = _gs.CtrlToggleMode;
                newVal = !old.AsBool();
                break;
            case "shift_toggle":
                old = _gs.ShiftToggleMode;
                newVal = !old.AsBool();
                break;
            default:
                return;  // 不可达（kind 恒来自 SETTING_KINDS）
        }
        if (newVal.VariantType == Variant.Type.Nil || newVal.Equals(old))
        {
            return;
        }
        _settingRestore = new Godot.Collections.Dictionary { ["kind"] = kind, ["old"] = old };
        _settingRestoreAt = now + SettingRestoreMs;
        _settingSwitches++;
        ApplySetting(kind, newVal);
        Log($"设置切换：{kind} {Str(old)} -> {Str(newVal)}（{SettingRestoreMs}ms 后切回）");
    }

    private static Variant PickOther(Godot.Collections.Array options, Variant current)
    {
        var others = new Godot.Collections.Array();
        foreach (var o in options)
        {
            if (!o.Equals(current))
            {
                others.Add(o);
            }
        }
        if (others.Count == 0)
        {
            return default;
        }
        return others[(int)(GD.Randi() % (uint)others.Count)];
    }

    private static Godot.Collections.Array ToUntyped(Godot.Collections.Array<Variant> items)
    {
        var result = new Godot.Collections.Array();
        foreach (var v in items)
        {
            result.Add(v);
        }
        return result;
    }

    private static Godot.Collections.Array ToUntyped(Godot.Collections.Array<StringName> items)
    {
        var result = new Godot.Collections.Array();
        foreach (var s in items)
        {
            result.Add(s);
        }
        return result;
    }

    private void ApplySetting(StringName kind, Variant value)
    {
        switch (kind.ToString())
        {
            case "view_zoom":
                _gs.SetViewZoom(value.AsStringName());
                break;
            case "window_size":
                _gs.SetWindowSize(value.AsStringName());
                break;
            case "locale":
                _gs.SetLocale(value.AsString());
                break;
            case "difficulty":
                _gs.SetDifficulty(value.AsStringName());
                break;
            case "aim_assist":
                _gs.SetAimAssistLevel(value.AsStringName());
                break;
            case "reduce_flash":
                _gs.SetReduceFlash(value.AsBool());
                break;
            case "ctrl_toggle":
                _gs.SetCtrlToggleMode(value.AsBool());
                break;
            case "shift_toggle":
                _gs.SetShiftToggleMode(value.AsBool());
                break;
        }
    }

    /// <summary>随机游走 + 远离密集敌弹/敌机的简单规避</summary>
    private void UpdateMovement(long now)
    {
        if (now < _nextMoveDecision)
        {
            return;
        }
        _nextMoveDecision = now + MoveDecisionMs;
        var player = _player!;
        var view = _gs.ViewWorldRect();
        if (player.Position.DistanceTo(_moveTarget) < 80.0f || GD.Randf() < 0.1f)
        {
            _moveTarget = new Vector2(
                (float)GD.RandRange(view.Position.X + 100.0f, view.End.X - 100.0f),
                (float)GD.RandRange(view.Position.Y + 100.0f, view.End.Y - 100.0f));
        }
        var steer = _moveTarget - player.Position;
        steer = steer.Length() > 60.0f ? steer.Normalized() : Vector2.Zero;
        // 规避：240px 内敌弹/编队炸弹（同权重）+ 160px 内敌机的反加权和
        var dodge = Vector2.Zero;
        foreach (var child in _main!.GetChildren())
        {
            if (child is Bullet b)
            {
                if (!b.IsPlayerBullet)
                {
                    float d = player.Position.DistanceTo(b.Position);
                    if (d < 240.0f && d > 1.0f)
                    {
                        dodge += (player.Position - b.Position) / d * (1.0f - d / 240.0f) * 2.0f;
                    }
                }
                continue;
            }
            if (child is FormationBomb fb)
            {
                float d = player.Position.DistanceTo(fb.Position);
                if (d < 240.0f && d > 1.0f)
                {
                    dodge += (player.Position - fb.Position) / d * (1.0f - d / 240.0f) * 2.0f;
                }
            }
        }
        foreach (var e in _gs.Enemies)
        {
            var n = e as Node2D;
            if (n == null)
            {
                continue;
            }
            float d = player.Position.DistanceTo(n.GlobalPosition);
            if (d < 160.0f && d > 1.0f)
            {
                dodge += (player.Position - n.GlobalPosition) / d * (1.0f - d / 160.0f) * 3.0f;
            }
        }
        steer += dodge;
        SetMoveActions(steer);
    }

    private void SetMoveActions(Vector2 steer)
    {
        var want = new Godot.Collections.Dictionary
        {
            ["move_right"] = steer.X > 0.35f,
            ["move_left"] = steer.X < -0.35f,
            ["move_down"] = steer.Y > 0.35f,
            ["move_up"] = steer.Y < -0.35f,
        };
        foreach (var kv in want)
        {
            if (kv.Value.AsBool())
            {
                Input.ActionPress(kv.Key.AsStringName());
            }
            else
            {
                Input.ActionRelease(kv.Key.AsStringName());
            }
        }
    }

    private void UpdateAim(long now)
    {
        if (now < _nextAim)
        {
            return;
        }
        _nextAim = now + AimIntervalMs;
        var player = _player!;
        var target = Vector2.Zero;
        float bestSq = float.PositiveInfinity;
        foreach (var e in _gs.Enemies)
        {
            var n = e as Node2D;
            if (n == null)
            {
                continue;
            }
            float dSq = player.Position.DistanceSquaredTo(n.GlobalPosition);
            if (dSq < bestSq)
            {
                bestSq = dSq;
                target = n.GlobalPosition;
            }
        }
        if (bestSq == float.PositiveInfinity)
        {
            target = player.Position + new Vector2((float)GD.RandRange(-300.0f, 300.0f), -400.0f);
        }
        // 世界坐标 → canvas → 屏幕（无头模式 warp_mouse 无效，合成鼠标移动事件）
        var canvasPos = _main!.GetCanvasTransform() * target;
        var win = GetTree().Root.GetScreenTransform() * canvasPos;
        var mev = new InputEventMouseMotion { Position = win, GlobalPosition = win };
        Input.ParseInputEvent(mev);
    }

    /// <summary>周期性冲刺：敌弹密集时优先触发（需已解锁 phase_dash）；狂暴/子弹时间期间更频繁</summary>
    private void UpdateDash(long now)
    {
        if (_dashReleaseAt > 0 && now >= _dashReleaseAt)
        {
            Input.ActionRelease("dash");
            _dashReleaseAt = 0;
        }
        if (now < _nextDashTry)
        {
            return;
        }
        var player = _player!;
        var main = _main!;
        bool enrageActive = (_boss != null && IsInstanceValid(_boss) && _boss.IsEnraged()) || main.BulletTime() > 0.0f || main.TimeScaleRamp() >= 0.0f;
        _nextDashTry = now + (enrageActive ? 250 : 500);
        if (!player.DashUnlocked() || player.DashCooldownRemaining() > 0.0f || player.IsDashing())
        {
            return;
        }
        double threat = 0.0;
        foreach (var child in main.GetChildren())
        {
            if (child is Bullet b && !b.IsPlayerBullet)
            {
                float d = player.Position.DistanceTo(b.Position);
                if (d < 240.0f)
                {
                    threat += 1.0 - d / 240.0;
                }
            }
        }
        double threshold = enrageActive ? 0.4 : 1.0;
        double idleChance = enrageActive ? 0.25 : 0.05;
        if (threat > threshold || GD.Randf() < idleChance)
        {
            Input.ActionPress("dash");
            _dashReleaseAt = now + 150;
        }
    }

    /// <summary>母舰：Boss 战或 HP 偏低时概率蓄力召唤（含蓄力主动取消探针）；
    /// 驻留驾驶一段时间（WASD 已被移动驱动复用）后提前离舰，或驻留到超时强制弹射</summary>
    private void UpdateDock(long now)
    {
        var main = _main!;
        if (main.IsHomecoming() || main.IsGameOver())
        {
            if (_dockHolding)
            {
                Input.ActionRelease("dock");
                _dockHolding = false;
            }
            return;
        }
        var ms = main.Mothership();
        if (_dockHolding)
        {
            if (ms != null)
            {
                Input.ActionRelease("dock");
                _dockHolding = false;
                _msSummons++;
                Log($"母舰召唤成功（第 {_msSummons} 次）");
            }
            else if (now >= _dockHoldUntil)
            {
                Input.ActionRelease("dock");
                _dockHolding = false;
                if (_dockCancelEpisode)
                {
                    _chargeCancels++;
                    Log($"母舰蓄力主动取消（第 {_chargeCancels} 次）");
                }
                else
                {
                    Log("母舰蓄力超时未召唤，松手");
                }
                _dockCancelEpisode = false;
            }
        }
        else if (ms == null && main.DockCooldown() <= 0.0f && now >= _nextDockConsider)
        {
            double hpRatio = _gs.Health / _gs.MaxHealth();
            if (_boss != null || hpRatio < 0.7)
            {
                float roll = GD.Randf();
                if (roll < 0.15f)
                {
                    // 蓄力取消探针：按住短时间后在蓄满前松手
                    Input.ActionPress("dock");
                    _dockHolding = true;
                    _dockCancelEpisode = true;
                    _dockHoldUntil = now + 300 + (long)(GD.Randi() % 600);
                    Log("开始蓄力召唤母舰（计划中途取消）");
                }
                else if (roll < 0.6f)
                {
                    Input.ActionPress("dock");
                    _dockHolding = true;
                    _dockCancelEpisode = false;
                    _dockHoldUntil = now + 8000;  // 蓄力 3s + 机库小窗 ~2.6s，留足余量
                    Log($"开始蓄力召唤母舰（boss={BoolStr(_boss != null)} hp={hpRatio * 100.0:0}%）");
                }
                else
                {
                    _nextDockConsider = now + 20000;
                }
            }
            else
            {
                _nextDockConsider = now + 10000;
            }
        }
        else if (ms != null && ms.GetState() < Mothership.State.STAY && GD.Randf() < 0.002f)
        {
            // 边界探针：非驻留态（降入/吸附/补给）乱按 H，应为无操作
            Input.ActionPress("dock");
            Input.ActionRelease("dock");
        }
        // 驻留驾驶一段时间后提前离舰；部分局驻留到超时强制弹射
        if (_earlyHolding)
        {
            if (ms == null || ms.GetState() >= Mothership.State.RELEASE || now >= _earlyHoldUntil)
            {
                Input.ActionRelease("dock");
                _earlyHolding = false;
                if (ms != null && ms.GetState() >= Mothership.State.RELEASE)
                {
                    _earlyLeaves++;
                    Log($"提前离舰（第 {_earlyLeaves} 次，弹匣 {ms.GetMagCells()} 格）");
                }
            }
        }
        else if (ms != null && ms.GetState() == Mothership.State.STAY)
        {
            if (_staySince == 0)
            {
                _staySince = now;
                _stayUntilEject = GD.Randf() < 0.35f;
                _earlyLeaveAt = now + (_stayUntilEject ? 60000 : 6000 + (long)(GD.Randi() % 8000));
                if (_stayUntilEject)
                {
                    Log("本次驻留等到超时强制弹射");
                }
            }
            else if (now >= _earlyLeaveAt)
            {
                Input.ActionPress("dock");
                _earlyHolding = true;
                _earlyHoldUntil = now + 4000;
            }
        }
        else
        {
            _staySince = 0;
        }
    }

    /// <summary>返航：血量低（或 Boss 战且半血以下）概率蓄力 B</summary>
    private void UpdateHomecoming(long now)
    {
        var main = _main!;
        if (main.IsHomecoming())
        {
            if (_homeHolding)
            {
                Input.ActionRelease("homecoming");
                _homeHolding = false;
                _homecomings++;
                Log($"返航触发（第 {_homecomings} 次）");
            }
            return;
        }
        if (_homeHolding)
        {
            if (now >= _homeHoldUntil)
            {
                Input.ActionRelease("homecoming");
                _homeHolding = false;
                Log("返航蓄力超时未触发，松手");
            }
            return;
        }
        if (now < _nextHomeConsider || main.IsGameOver())
        {
            return;
        }
        _nextHomeConsider = now + 8000;
        double hpRatio = _gs.Health / _gs.MaxHealth();
        bool want = hpRatio < 0.35 || (_boss != null && hpRatio < 0.6);
        if (want && GD.Randf() < 0.6f)
        {
            Input.ActionPress("homecoming");
            _homeHolding = true;
            _homeHoldUntil = now + 4000;
            Log($"开始蓄力返航（hp={hpRatio * 100.0:0}% boss={BoolStr(_boss != null)}）");
        }
    }

    /// <summary>母舰状态变化日志 + 卡死 episode 跟踪</summary>
    private void TrackMothership(long now)
    {
        var ms = (_main != null && IsInstanceValid(_main)) ? _main.Mothership() : null;
        int state = ms == null ? -1 : (int)ms.GetState();
        if (state != _msLastState)
        {
            if (_msLastState >= 0 || state >= 0)
            {
                string fromS = _msLastState < 0 ? "NONE" : MsStateNames[_msLastState];
                string toS = state < 0 ? "NONE" : MsStateNames[state];
                Log($"母舰状态 {fromS} -> {toS}");
            }
            if (_msLastState == (int)Mothership.State.STAY && state == (int)Mothership.State.RELEASE)
            {
                if (_stayUntilEject && !_earlyHolding)
                {
                    _forcedEjects++;
                    Log($"驻留超时强制弹射（第 {_forcedEjects} 次）");
                }
                _stayUntilEject = false;
            }
            _msLastState = state;
            _msStateSince = now;
            _msStuckReported = false;
        }
        else if (state >= 0 && !_msStuckReported)
        {
            if (state < MsStateTimeouts.Length && now - _msStateSince > MsStateTimeouts[state])
            {
                _msStuckReported = true;
                Anomaly("mothership_stuck", $"母舰状态 {MsStateNames[state]} 超过 {MsStateTimeouts[state] / 1000}s 未推进");
            }
        }
    }

    // ---------------- 事件 ----------------

    private void OnMilestone(int milestoneScore)
    {
        _milestones++;
        Log($"里程碑达成 score={milestoneScore}（第 {_milestones} 次）");
    }

    private void OnBossSpawned(Boss boss)
    {
        _boss = boss;
        _bossSince = (long)Time.GetTicksMsec();
        _bossTimeoutReported = false;
        Log($"Boss 出现 type={boss.BossType} hp={boss.MaxHp:0}");
        boss.Enraged += () =>
        {
            _bossEnrageCount++;
            Log($"Boss 狂暴 type={boss.BossType}（第 {_bossEnrageCount} 次）");
        };
        boss.PhaseChanged += OnBossPhaseChanged;
        boss.Died += () => OnBossDied(boss);
        boss.Escaped += () =>
        {
            _bossEscapes++;
            Log($"Boss 逃跑 type={boss.BossType}（第 {_bossEscapes} 次）");
            ClearBoss(boss);
        };
        boss.TreeExited += () => ClearBoss(boss);
    }

    private void OnBossPhaseChanged(int newPhase)
    {
        if (newPhase == FightP2)  // M3d：Boss.FightPhase.P2（C# 枚举直接引用，P1=0/P2=1/ENRAGE=2）
        {
            _bossP2Count++;
            Log($"Boss 进入 P2（第 {_bossP2Count} 次）");
        }
    }

    private void OnBossDied(Boss boss)
    {
        if (boss.IsEscaped)
        {
            return;  // 逃跑离场也会发 died（通知血条/生成器），非击杀
        }
        _totalBossKills++;
        Log($"Boss 击杀 type={boss.BossType}（本进程累计 {_totalBossKills}）");
        ClearBoss(boss);
    }

    private void ClearBoss(Boss boss)
    {
        if (_boss == boss)
        {
            _boss = null;
        }
    }

    private void OnPlayerDied()
    {
        _deaths++;
        _totalKills += _gs.Kills;
        _runScores.Add(_gs.Score);
        Log($"玩家死亡 run={_runIndex} score={_gs.Score} kills={_gs.Kills} boss_kills={_gs.BossKills}");
        ReleaseAllInputs();
        _menuReturnAt = 0;
        _restartAt = (long)Time.GetTicksMsec() + 3000;  // 留 3s 走到结算界面
    }

    private void OnHealthChanged(double newHealth)
    {
        long now = (long)Time.GetTicksMsec();
        if (_lastHp >= 0.0 && newHealth < _lastHp - 0.01 && now - _lastHitLogMsec > 1000)
        {
            _lastHitLogMsec = now;
            Log($"玩家受击 HP {_lastHp:0.0} -> {newHealth:0.0}");
        }
        _lastHp = newHealth;
    }

    /// <summary>B 梯队：受击触发 DDA 降档——记录时刻与触发次数（受击即计数：
    /// GameState 自连接先于本回调置位，用 dda_active 判触发会恒 false）</summary>
    private void OnPlayerDamaged(float amount, Vector2 fromPos)
    {
        _lastDamagedMsec = (long)Time.GetTicksMsec();
        _ddaTriggerCount++;
    }

    /// <summary>死亡重开：删档开新局</summary>
    private async Task DoRestart()
    {
        Log("重开新一局");
        ResetTransitionState();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _gs.DeleteSave();
        _gs.ResetRun();
        _ = StartRun();
    }

    /// <summary>主动重进 main：保留存档重新实例化 main（启动自动读档，等价于回主界面再进对局）</summary>
    private async Task DoMenuReturn()
    {
        Log($"主动重进 main 读档继续（保留存档 score={_gs.Score}）");
        ResetTransitionState();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _ = StartRun(true);  // 不 delete_save、不 reset_run：读档恢复路径
    }

    private void ResetTransitionState()
    {
        _boss = null;
        _msLastState = -1;
        _staySince = 0;
        _stayUntilEject = false;
        _buffOpenSince = 0;
        _homecomingPendingSinceMs = 0;
        _slowSince = 0;
        _slowReported = false;
        _eventWasActive = false;
        _formationWasActive = false;
        _pauseOpenSince = 0;
        GetTree().Paused = false;
        _started = false;
        ReleaseAllInputs();
        _main!.QueueFree();
    }

    private void ReleaseAllInputs()
    {
        foreach (var a in MoveActions)
        {
            Input.ActionRelease(a);
        }
        Input.ActionRelease("dash");
        Input.ActionRelease("dock");
        Input.ActionRelease("homecoming");
        _dockHolding = false;
        _homeHolding = false;
        _earlyHolding = false;
        _dashReleaseAt = 0;
    }

    // ---------------- 快照与不变量检查 ----------------

    private void Snapshot(long now)
    {
        var main = _main!;
        int pBullets = 0;
        int eBullets = 0;
        foreach (var child in main.GetChildren())
        {
            if (child is Bullet b)
            {
                if (b.IsPlayerBullet)
                {
                    pBullets++;
                }
                else
                {
                    eBullets++;
                }
            }
        }
        int mainNodes = main.GetChildCount();
        int totalNodes = CountNodes(GetTree().Root);
        _maxNodes = Mathf.Max(_maxNodes, totalNodes);
        _maxEnemyBullets = Mathf.Max(_maxEnemyBullets, eBullets);
        _maxPlayerBullets = Mathf.Max(_maxPlayerBullets, pBullets);
        _maxEnemies = Mathf.Max(_maxEnemies, _gs.Enemies.Count);
        // 引擎级监控器
        // 引擎级监控器（GDScript Performance.OBJECT_COUNT 常量 → C# Performance.Monitor 枚举）
        double objCount = Performance.GetMonitor(Performance.Monitor.ObjectCount);
        double nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        double orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        double memStatic = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        double fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        _maxOrphans = Mathf.Max(_maxOrphans, orphans);
        // 池规模
        int bulletPoolN = -1;
        int enemyPoolN = -1;
        var bulletPool = _gs.BulletPool as Node;
        if (bulletPool != null && IsInstanceValid(bulletPool))
        {
            bulletPoolN = bulletPool.GetChildCount();
            _maxBulletPool = Mathf.Max(_maxBulletPool, bulletPoolN);
        }
        var enemyPool = _gs.EnemyPool as Node;
        if (enemyPool != null && IsInstanceValid(enemyPool))
        {
            enemyPoolN = enemyPool.GetChildCount();
            _maxEnemyPool = Mathf.Max(_maxEnemyPool, enemyPoolN);
        }
        // 帧耗时（真实 ms/帧，含 time_scale=2 的放大效应）
        double frameMs = 0.0;
        int frames = _frameCount;
        _frameCount = 0;
        if (frames > 0)
        {
            frameMs = (double)(now - _frameSnapMsec) / frames;
        }
        _frameSnapMsec = now;
        _maxFrameMs = Mathf.Max(_maxFrameMs, frameMs);
        string bossS = "none";
        if (_boss != null && IsInstanceValid(_boss))
        {
            bossS = $"type{_boss.BossType} hp={_boss.Hp:0}/{_boss.MaxHp:0}" + (_boss.IsEnraged() ? "(enraged)" : "");
        }
        var ms = main.Mothership();
        string msS = ms == null ? "none" : MsStateNames[(int)ms.GetState()];
        string ddaS = _gs.DdaActive() ? "on" : "-";  // B 梯队：DDA 降档状态
        Log(
            $"SNAP run={_runIndex} t_game={_gs.RunTime:0}s score={_gs.Score} hp={_gs.Health:0}/{_gs.MaxHealth():0} kills={_gs.Kills} enemies={_gs.Enemies.Count} "
            + $"bullets(p={pBullets},e={eBullets}) boss={bossS} ms={msS} dda={ddaS} diff={_gs.DifficultyMultiplier:0.00} elapsed={_spawner!.Elapsed():0}s "
            + $"nodes(main={mainNodes},total={totalNodes}) ts={Engine.TimeScale:0.00} paused={BoolStr(GetTree().Paused)} "
            + $"perf(obj={objCount:0},nodes={nodeCount:0},orphan={orphans:0},mem={memStatic / 1048576.0:0.0}MB,fps={fps:0},fms={frameMs:0.00}) pool(b={bulletPoolN},e={enemyPoolN})"
        );
        // 孤儿节点：任何非零值都是泄漏信号（比节点直方图更灵敏）
        if (orphans > 0.0)
        {
            AnomalyRl("orphan_nodes", $"孤儿节点数 {orphans:0}", now);
        }
        // 池规模上界
        if (bulletPoolN > MaxBulletPool)
        {
            AnomalyRl("pool_growth", $"子弹池闲置实例 {bulletPoolN} 超过 {MaxBulletPool}", now);
        }
        if (enemyPoolN > MaxEnemyPool)
        {
            AnomalyRl("pool_growth", $"敌机池闲置实例 {enemyPoolN} 超过 {MaxEnemyPool}", now);
        }
        // 节点泄漏趋势：连续 3 个快照上涨且超过基线 3 倍
        if (_nodeBaseline == 0)
        {
            _nodeBaseline = totalNodes;
            _nodePrev = totalNodes;
            _objBaseline = objCount;
            _objPrev = objCount;
            return;
        }
        if (totalNodes > _nodePrev)
        {
            _nodeRiseStreak++;
        }
        else
        {
            _nodeRiseStreak = 0;
        }
        _nodePrev = totalNodes;
        if (totalNodes < _nodeBaseline * 2)
        {
            _nodeLeakArmed = true;
        }
        if (_nodeLeakArmed && _nodeRiseStreak >= 3 && totalNodes > _nodeBaseline * 3)
        {
            _nodeLeakArmed = false;
            Anomaly("node_leak", $"节点数连续上涨 {_nodeBaseline} -> {totalNodes}（基线 {_nodeBaseline}）");
            DumpNodeHistogram();
        }
        // 对象数泄漏趋势（池复用失效时对象只进不出，比节点数更早显现）
        if (objCount > _objPrev)
        {
            _objRiseStreak++;
        }
        else
        {
            _objRiseStreak = 0;
        }
        _objPrev = objCount;
        if (objCount < _objBaseline * 1.3)
        {
            _objLeakArmed = true;
        }
        if (_objLeakArmed && _objRiseStreak >= ObjectLeakStreak && objCount > _objBaseline * ObjectLeakRatio)
        {
            _objLeakArmed = false;
            Anomaly("object_leak", $"对象数连续上涨 {_objBaseline:0} -> {objCount:0}（基线 {_objBaseline:0}）");
            DumpNodeHistogram();
        }
        // 帧耗时恶化：前 5 个快照取最小值作基线（避免初始恰逢演出/弹幕高峰抬高基线
        // 而掩盖真实悬崖），持续 3 倍即报（难度升高后的性能悬崖）
        if (frameMs > 0.0)
        {
            _frameSnaps++;
            if (_frameSnaps <= 5)
            {
                _frameMsBaseline = _frameMsBaseline <= 0.0 ? frameMs : Mathf.Min(_frameMsBaseline, frameMs);
            }
            else if (_frameMsBaseline > 0.0 && frameMs > _frameMsBaseline * 3.0)
            {
                _frameSlowStreak++;
                if (_frameSlowStreak >= 3)
                {
                    _frameSlowStreak = 0;
                    Anomaly("frame_time", $"帧耗时恶化 {frameMs:0.00}ms（基线 {_frameMsBaseline:0.00}ms，enemies={_gs.Enemies.Count} bullets={pBullets + eBullets}）");
                }
            }
            else
            {
                _frameSlowStreak = 0;
            }
        }
    }

    /// <summary>节点泄漏诊断：打印 Main 子树及根节点的直接子节点类型分布</summary>
    private void DumpNodeHistogram()
    {
        var main = _main!;
        Log("--- 节点直方图（泄漏诊断） ---");
        HistogramLine(main, "  ");
        foreach (var child in main.GetChildren())
        {
            if (child.GetChildCount() > 3)
            {
                HistogramLine(child, "    ");
            }
        }
    }

    private void HistogramLine(Node node, string indent)
    {
        var byClass = new Godot.Collections.Dictionary();
        foreach (var child in node.GetChildren())
        {
            string cls = child.GetClass();
            var script = child.GetScript();
            if (script.VariantType != Variant.Type.Nil && script.AsGodotObject() is Script s)
            {
                cls = s.ResourcePath.GetFile();
            }
            byClass[cls] = byClass.GetValueOrDefault(cls, 0).AsInt32() + 1;
        }
        var parts = new System.Collections.Generic.List<string>();
        foreach (var kv in byClass)
        {
            parts.Add($"{kv.Key}: {kv.Value.AsInt32()}");
        }
        Log($"{indent}{node.Name} <{node.GetClass()}> children={node.GetChildCount()} {{{string.Join(", ", parts)}}}");
    }

    /// <summary>注册表差集诊断用：节点类名（有脚本的取脚本文件名，同 _histogram_line 口径）</summary>
    private static string ClassLabel(GodotObject obj)
    {
        if (obj is Node n)
        {
            var script = n.GetScript();
            if (script.VariantType != Variant.Type.Nil && script.AsGodotObject() is Script s)
            {
                return s.ResourcePath.GetFile().GetBaseName();
            }
        }
        return obj.GetClass();
    }

    /// <summary>「类名×n, ...」格式化（注册表差集消息明细）</summary>
    private static string FmtClassCounts(Godot.Collections.Dictionary counts)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var kv in counts)
        {
            parts.Add($"{kv.Key}×{kv.Value.AsInt32()}");
        }
        return string.Join(", ", parts);
    }

    private static int CountNodes(Node root)
    {
        int n = 1;
        foreach (var child in root.GetChildren())
        {
            n += CountNodes(child);
        }
        return n;
    }

    private void Checks(long now)
    {
        var main = _main!;
        // 数值越界
        if (_gs.Health < -0.01 || _gs.Health > _gs.MaxHealth() + 0.01)
        {
            AnomalyRl("hp_bounds", $"HP 越界 {_gs.Health:0.00}（上限 {_gs.MaxHealth():0.00}）", now);
        }
        if (_gs.Score < 0)
        {
            AnomalyRl("negative_score", $"分数为负 {_gs.Score}", now);
        }
        // 实体爆增
        int pBullets = 0;
        int eBullets = 0;
        Node replayFound = null!;  // B 梯队：死亡回放演出节点（同遍历检测）
        foreach (var child in main.GetChildren())
        {
            if (child is Bullet b)
            {
                if (b.IsPlayerBullet)
                {
                    pBullets++;
                }
                else
                {
                    eBullets++;
                }
            }
            else if (child is DeathReplayPlayer)
            {
                replayFound = child;
            }
        }
        if (pBullets > MaxPlayerBullets)
        {
            AnomalyRl("entity_explosion", $"玩家子弹数 {pBullets} 超过 {MaxPlayerBullets}", now);
        }
        if (eBullets > MaxEnemyBullets)
        {
            AnomalyRl("entity_explosion", $"敌方子弹数 {eBullets} 超过 {MaxEnemyBullets}", now);
        }
        if (_gs.Enemies.Count > MaxEnemies)
        {
            AnomalyRl("entity_explosion", $"敌机注册数 {_gs.Enemies.Count} 超过 {MaxEnemies}", now);
        }
        // 注册表一致性：enemy 组集合与注册表双向差集比对
        // （四类注册者 Enemy/Boss/TurretBattery/FormationCraft 组语义与注册表一致；
        // 两侧都跳过 _active==false 的池化 Enemy——deactivate 同步注销、deferred reparent 亚帧窗口）
        var sceneSet = new Godot.Collections.Dictionary();  // Node -> true
        foreach (var n in GetTree().GetNodesInGroup("enemy"))
        {
            var node = n as Node;
            if (node == null || !main.IsAncestorOf(node))
            {
                continue;
            }
            var en = node as Enemy;
            if (en != null && !en.IsActive())
            {
                continue;
            }
            sceneSet[node] = true;
        }
        var registrySet = new Godot.Collections.Dictionary();  // 有效实例 -> true
        bool staleFound = false;
        foreach (var e in _gs.Enemies)
        {
            if (!IsInstanceValid(e))
            {
                staleFound = true;
                continue;  // 失效实例归 registry_stale 管，不参与差集
            }
            var re = e as Enemy;
            if (re != null && !re.IsActive())
            {
                continue;
            }
            registrySet[e] = true;
        }
        if (staleFound)
        {
            AnomalyRl("registry_stale", "GameState.enemies 含失效实例", now);
        }
        var regExtra = new Godot.Collections.Dictionary();  // 类名 -> 计数
        foreach (var kv in registrySet)
        {
            if (!sceneSet.ContainsKey(kv.Key))
            {
                string k = ClassLabel(kv.Key.AsGodotObject());
                regExtra[k] = regExtra.GetValueOrDefault(k, 0).AsInt32() + 1;
            }
        }
        if (regExtra.Count > 0)
        {
            AnomalyRl("registry_mismatch", $"注册表多出: {FmtClassCounts(regExtra)}（注册表 {registrySet.Count} vs 场景 {sceneSet.Count}）", now);
        }
        var sceneExtra = new Godot.Collections.Dictionary();
        foreach (var kv in sceneSet)
        {
            if (!registrySet.ContainsKey(kv.Key))
            {
                string k = ClassLabel(kv.Key.AsGodotObject());
                sceneExtra[k] = sceneExtra.GetValueOrDefault(k, 0).AsInt32() + 1;
            }
        }
        if (sceneExtra.Count > 0)
        {
            AnomalyRl("registry_mismatch", $"场景多出: {FmtClassCounts(sceneExtra)}（注册表 {registrySet.Count} vs 场景 {sceneSet.Count}）", now);
        }
        // 引用有效性：player_ref / 对象池
        if (_player != null && IsInstanceValid(_player) && _gs.PlayerRef != _player)
        {
            AnomalyRl("player_ref_mismatch", "GameState.player_ref 未指向当前玩家", now);
        }
        var bulletPool = _gs.BulletPool as Node;
        if (bulletPool == null || !IsInstanceValid(bulletPool))
        {
            AnomalyRl("pool_ref_invalid", "GameState.bullet_pool 引用失效", now);
        }
        else if (bulletPool.GetParent() != main)
        {
            AnomalyRl("pool_ref_invalid", "GameState.bullet_pool 父节点不是当前 Main（残留旧对局池）", now);
        }
        var enemyPool = _gs.EnemyPool as Node;
        if (enemyPool == null || !IsInstanceValid(enemyPool))
        {
            AnomalyRl("pool_ref_invalid", "GameState.enemy_pool 引用失效", now);
        }
        else if (enemyPool.GetParent() != main)
        {
            AnomalyRl("pool_ref_invalid", "GameState.enemy_pool 父节点不是当前 Main（残留旧对局池）", now);
        }
        // Buff UI 卡死
        if (_buffUi != null && IsInstanceValid(_buffUi) && _buffUi.Visible)
        {
            if (_buffOpenSince > 0 && now - _buffOpenSince > BuffStuckMs && !_buffStuckReported)
            {
                _buffStuckReported = true;
                Anomaly("buff_ui_stuck", $"Buff UI 可见超过 {BuffStuckMs / 1000}s 未关闭");
            }
        }
        // Boss 超时
        if (_boss != null && IsInstanceValid(_boss) && !_bossTimeoutReported)
        {
            if (now - _bossSince > BossTimeoutMs)
            {
                _bossTimeoutReported = true;
                Anomaly("boss_timeout", $"Boss type={_boss.BossType} 在场超过 {BossTimeoutMs / 1000}s");
            }
        }
        // 返航/基地卡死（返航过场播放期不计时：过场真实时长可达数十秒，计时起点顺延到结束）
        if (main.IsHomecoming())
        {
            if (main.ReturnCinematic() != null)
            {
                _homecomingPendingSinceMs = now;
                _homeStuckReported = false;
            }
            else if (_homecomingPendingSinceMs == 0)
            {
                _homecomingPendingSinceMs = now;
                _homeStuckReported = false;
            }
            else if (!_homeStuckReported && !main.BaseUi().Visible && now - _homecomingPendingSinceMs > HomeStuckMs)
            {
                _homeStuckReported = true;
                Anomaly("homecoming_stuck", $"返航过场结束 {HomeStuckMs / 1000}s 后基地 UI 仍未显示");
            }
        }
        else
        {
            _homecomingPendingSinceMs = 0;
        }
        if (main.BaseUi().Visible && _baseSince > 0 && now - _baseSince > BaseStuckMs && !_baseStuckReported)
        {
            _baseStuckReported = true;
            Anomaly("base_ui_stuck", $"基地 UI 可见超过 {BaseStuckMs / 1000}s 未关闭");
        }
        // 狂暴减速残留：玩家仍减速但无狂暴 Boss（Boss 离场/死亡后未复位），持续 15s episode 报一次
        bool bossEnraged = _boss != null && IsInstanceValid(_boss) && _boss.IsEnraged();
        if (_player != null && IsInstanceValid(_player) && Mathf.Abs(_player.EnrageSlow() - 1.0f) > 0.001f && !bossEnraged)
        {
            if (_slowSince == 0)
            {
                _slowSince = now;
            }
            else if (!_slowReported && now - _slowSince > SlowStuckMs)
            {
                _slowReported = true;
                Anomaly("enrage_slow_stuck", $"玩家狂暴减速 {_player.EnrageSlow():0.00} 持续 {SlowStuckMs / 1000}s 但无狂暴 Boss");
            }
        }
        else
        {
            _slowSince = 0;
            _slowReported = false;
        }
        // 事件触发计数：非活跃 -> 活跃跃迁各 +1（500ms 轮询事件状态机）
        var elite = main.Event() as EliteTurretEvent;
        bool turretActive = elite != null && elite.GetState() != EliteTurretEvent.State.IDLE;
        if (turretActive && !_eventWasActive)
        {
            _turretEventCount++;
            Log($"精英炮塔事件触发（第 {_turretEventCount} 次）");
        }
        _eventWasActive = turretActive;
        var formation = main.Formation() as FormationStrikeEvent;
        bool formationActive = formation != null && formation.GetState() != FormationStrikeEvent.State.IDLE;
        if (formationActive && !_formationWasActive)
        {
            _formationEventCount++;
            Log($"轰炸编队事件触发（第 {_formationEventCount} 次）");
        }
        _formationWasActive = formationActive;
        // B 梯队：DDA 降档卡死——无受击超时仍激活（受击刷新计时，持续受击不算；恢复后复位）
        if (_gs.DdaActive())
        {
            if (_lastDamagedMsec >= 0 && now - _lastDamagedMsec > DdaStuckMs && !_ddaStuckReported)
            {
                _ddaStuckReported = true;
                Anomaly("dda_stuck", $"DDA 降档激活超 {DdaStuckMs / 1000}s 无受击（未按时恢复）");
            }
        }
        else
        {
            _ddaStuckReported = false;
        }
        // B 梯队：死亡回放演出节点跟踪——出现即计时，超时未自毁 = 泄漏（3s 播完、5s 兜底）
        if (replayFound != null)
        {
            if (_replayNode != replayFound)
            {
                _replayNode = replayFound;
                _replaySince = now;
                _replaySeenCount++;
                _replayStuckReported = false;
            }
            else if (!_replayStuckReported && now - _replaySince > ReplayStuckMs)
            {
                _replayStuckReported = true;
                Anomaly("replay_stuck", $"死亡回放演出节点存活超 {ReplayStuckMs / 1000}s（未自毁）");
            }
        }
        else if (_replayNode != null)
        {
            _replayNode = null;
            _replaySince = 0;
        }
        // Phase 0 L13：母舰×事件互斥——母舰在场期精英炮塔/编队事件不得触发
        // （can_trigger 组查询互斥；探针交叉验证事件状态机与母舰在场）
        var ms = main.Mothership();
        if (ms != null && IsInstanceValid(ms) && (turretActive || formationActive))
        {
            AnomalyRl("ms_event_mutex", $"母舰在场期事件触发（elite={BoolStr(turretActive)} formation={BoolStr(formationActive)}）", now);
        }
        // UI 状态一致性：结算面板与基地面板同显 / 玩家死亡但游戏未停且无结算面板
        var gameOverUi = main.GetNode<GameOverUi>("GameOverUI");
        if (gameOverUi.Visible && main.BaseUi().Visible)
        {
            AnomalyRl("ui_overlap", "GameOverUI 与基地 UI 同时可见", now);
        }
        if (_player != null && _player.IsDead() && !GetTree().Paused && !gameOverUi.Visible)
        {
            AnomalyRl("dead_no_gameover", "玩家已死亡、游戏未暂停且结算面板不可见", now);
        }
        // 分数停滞 + 场上无敌机（疑似不刷怪/不结算）
        if (_gs.Score != _lastScore)
        {
            _lastScore = _gs.Score;
            _scoreChangeMsec = now;
            _scoreStagReported = false;
        }
        else if (!_scoreStagReported && now - _scoreChangeMsec > ScoreStagnantMs
            && _gs.Enemies.Count == 0 && _boss == null && !GetTree().Paused
            && !main.IsHomecoming() && !main.IsGameOver())
        {
            _scoreStagReported = true;
            Anomaly("score_stagnant", $"分数 {ScoreStagnantMs / 1000}s 未增长且场上无敌机（疑似不刷怪）");
        }
    }

    // ---------------- 收尾 ----------------

    private void Finish()
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        // 收尾局（未死亡）也计入统计
        if (_player != null && IsInstanceValid(_player) && !_player.IsDead())
        {
            _totalKills += _gs.Kills;
            _runScores.Add(_gs.Score);
        }
        Log("DONE");
        if (_main != null && IsInstanceValid(_main))
        {
            DumpNodeHistogram();
        }
        GD.Print("");
        GD.Print("[AUTOPLAY] ==================== SUMMARY ====================");
        GD.Print($"[AUTOPLAY] 真实时长 {ElapsedS():0}s | 对局数 {_runIndex} | 死亡 {_deaths} 次 | seed={_seed}");
        GD.Print($"[AUTOPLAY] 每局得分 [{string.Join(", ", _runScores)}] | 总击杀 {_totalKills} | Boss 击杀 {_totalBossKills}");
        GD.Print($"[AUTOPLAY] Buff 选取 {_buffPicks} 次（覆盖种类 {_buffsSeen.Count}/{BuffPoolSize}）| 母舰召唤 {_msSummons} 次 | 返航 {_homecomings} 次");
        GD.Print($"[AUTOPLAY] 母舰边界：蓄力取消 {_chargeCancels} | 提前离舰 {_earlyLeaves} | 强制弹射 {_forcedEjects}");
        GD.Print($"[AUTOPLAY] 暂停存档 {_pauseSaves} 次 | 暂停开设置页 {_settingsOpens} 次 | 继续对局 {_continueResumes} 次 | 退出确认探针 {_exitProbes} 次 | 设置切换 {_settingSwitches} 次");
        GD.Print($"[AUTOPLAY] 基地：维修 {_baseRepairs} | 补给 {_baseRecharges} | 路线选择 {_routeChoices} | 任务领奖 {_missionClaims}");
        GD.Print($"[AUTOPLAY] Buff 动效路径选取 {_buffAnimatedPicks} 次 | Boss P2 {_bossP2Count} 次 | 狂暴 {_bossEnrageCount} 次 | 逃跑 {_bossEscapes} 次 | 里程碑 {_milestones} 次 | 炮塔事件 {_turretEventCount} 次 | 编队事件 {_formationEventCount} 次");
        GD.Print($"[AUTOPLAY] B 梯队: DDA 降档触发 {_ddaTriggerCount} 次 | 死亡回放演出 {_replaySeenCount} 次（播完自毁，无泄漏）");
        GD.Print($"[AUTOPLAY] 峰值: 节点 {_maxNodes} | 敌弹 {_maxEnemyBullets} | 玩家弹 {_maxPlayerBullets} | 敌机 {_maxEnemies} | 孤儿节点 {_maxOrphans:0} | 池(b={_maxBulletPool},e={_maxEnemyPool}) | 帧耗时 {_maxFrameMs:0.00}ms（基线 {_frameMsBaseline:0.00}ms）");
        int totalAnomalies = 0;
        foreach (var kv in _anomalyCounts)
        {
            totalAnomalies += kv.Value.AsInt32();
        }
        GD.Print($"[AUTOPLAY] 异常总数 {totalAnomalies}（{_anomalyCounts.Count} 类）");
        foreach (var kv in _anomalyCounts)
        {
            GD.Print($"[AUTOPLAY]   - {kv.Key.AsStringName()} ×{kv.Value.AsInt32()} | 首例: {_anomalyFirst[kv.Key].AsString()}");
        }
        if (_anomalyCounts.Count == 0)
        {
            GD.Print("[AUTOPLAY]   （无异常）");
        }
        GD.Print("[AUTOPLAY] ===================================================");
        // 清理：释放输入、恢复全局状态、删残留存档、恢复原设置档位
        ReleaseAllInputs();
        _gs.StopAllSfx();
        Engine.TimeScale = 1.0f;
        GetTree().Paused = false;
        _gs.DeleteSave();
        if (_gs.Difficulty != _prevDifficulty)
        {
            _gs.SetDifficulty(_prevDifficulty);
        }
        if (_gs.ViewZoom != _prevViewZoom)
        {
            _gs.SetViewZoom(_prevViewZoom);
        }
        if (_gs.WindowSize != _prevWindowSize)
        {
            _gs.SetWindowSize(_prevWindowSize);
        }
        if (_gs.Locale != _prevLocale)
        {
            _gs.SetLocale(_prevLocale);
        }
        if (_gs.AimAssistLevel != _prevAimAssist)
        {
            _gs.SetAimAssistLevel(_prevAimAssist);
        }
        if (_gs.ReduceFlash != _prevReduceFlash)
        {
            _gs.SetReduceFlash(_prevReduceFlash);
        }
        if (_gs.CtrlToggleMode != _prevCtrlToggle)
        {
            _gs.SetCtrlToggleMode(_prevCtrlToggle);
        }
        if (_gs.ShiftToggleMode != _prevShiftToggle)
        {
            _gs.SetShiftToggleMode(_prevShiftToggle);
        }
        TestExit.Quit(0);  // 探针不以 FAIL 结束（原 gd 同款：load TestExit.cs Quit(0)）
    }

    private double ElapsedS()
    {
        if (_t0Msec == 0)
        {
            return 0.0;
        }
        return ((long)Time.GetTicksMsec() - _t0Msec) / 1000.0;
    }

    private void Log(string msg)
    {
        GD.Print($"[AUTOPLAY] [{ElapsedS(),7:0.0}s] {msg}");
    }

    private void Anomaly(string category, string msg)
    {
        _anomalyCounts[category] = _anomalyCounts.GetValueOrDefault(category, 0).AsInt32() + 1;
        if (!_anomalyFirst.ContainsKey(category))
        {
            _anomalyFirst[category] = msg;
        }
        GD.PushError($"[ANOMALY] [{ElapsedS(),7:0.0}s] [{category}] {msg}");
    }

    /// <summary>数值类异常限频（同类 10s 至多一条，避免刷屏）</summary>
    private void AnomalyRl(string category, string msg, long now)
    {
        if (now - _anomalyRlLast.GetValueOrDefault(category, -100000).AsInt32() < 10000)
        {
            return;
        }
        _anomalyRlLast[category] = now;
        Anomaly(category, msg);
    }

    /// <summary>GDScript str() 对齐：bool 小写、StringName 去引号、null 显示 &lt;null&gt;（仅日志用）</summary>
    private static string Str(Variant v)
    {
        if (v.VariantType == Variant.Type.Nil)
        {
            return "<null>";
        }
        if (v.VariantType == Variant.Type.Bool)
        {
            return BoolStr(v.AsBool());
        }
        if (v.VariantType == Variant.Type.StringName)
        {
            return v.AsStringName().ToString();
        }
        return v.ToString();
    }

    private static string BoolStr(bool b) => b ? "true" : "false";
}
