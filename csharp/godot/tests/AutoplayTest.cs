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
    private const int BuffPoolSize = 19;  // BuffSelect.cs 卡池种类数（覆盖率统计分母；2026-08-05 P4 修正 16→19）

    // BuffSelect.cs 卡池 max 值镜像（C# 侧 BuffSelect.BuffPool 为 private static 无公开访问器；
    // 口径同 BuffSelect.cs：cfg 覆盖池内默认；池内容调整需同步此处）
    private static readonly Godot.Collections.Dictionary BuffPoolMax = new()
    {
        ["power_shot"] = 5,
        ["rapid_fire"] = 4,
        ["spread_shot"] = 2,
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
    private Main? _main;
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
    // 战斗输入覆盖（原 autoplay 仅覆盖 move/dash/dock/homecoming，未覆盖 parry/boost/give_up 子系统）
    private long _nextParryConsider;
    private long _parryReleaseAt;
    private bool _parryWasActive;
    private long _nextBoostConsider;
    private bool _boostHolding;
    private long _boostHoldUntil;
    private long _nextGiveUpConsider;
    private bool _giveUpHolding;
    private long _giveUpHoldUntil;
    // fine_move（Ctrl 微调）与刻意擦弹诱导（graze-seeking）
    private long _nextFineMoveConsider;
    private bool _fineMoveHolding;
    private long _fineMoveHoldUntil;
    private long _nextGrazeConsider;
    private long _grazeSeekUntil;  // >0：擦弹诱导窗进行中
    // buff_panel（L 键）切换 buff 滚动栏
    private long _nextBuffPanelConsider;
    private int _buffPanelToggles;
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
    // 轨道打击 / 迷雾事件监控（间接触发但此前未显式监控）
    private int _strikeCount;
    private long _strikeSince;
    private bool _strikeWasActive;
    private const int StrikeStuckMs = 8000;  // OrbitalStrike.DURATION≈1.4s（cfg 可覆盖）+ 余量
    private const int FogStuckMs = 30000;  // 迷雾事件时长由 cfg fog_events.durations 决定，30s 兜底
    private int _fogCount;
    private long _fogActiveSince;
    private bool _fogWasActive;
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
    private int _parryCount;
    private int _parryActiveSeen;
    private int _boostCount;
    private int _giveUpProbes;
    private int _fineMoveCount;
    private int _grazeCount;
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
    private double _maxResources;
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
                    _runSeconds = double.Parse(arg.AsSpan("--autoplay-seconds=".Length), System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (arg.StartsWith("--seed="))
                {
                    _seed = int.Parse(arg.AsSpan("--seed=".Length), System.Globalization.CultureInfo.InvariantCulture);
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
            _nextParryConsider = _t0Msec + 2000;
            _nextBoostConsider = _t0Msec + 1500;
            _nextGiveUpConsider = _t0Msec + 60000;
            _nextFineMoveConsider = _t0Msec + 2500;
            _nextGrazeConsider = _t0Msec + 8000;
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
        // 迷雾事件：autoplay 探针主动覆盖所有随机路径——Main 的 current_scene 确定性保护在测试子节点
        // 实例化时默认关闭迷雾，但探针定位是主动测漏洞，故显式启用（覆盖 Main._Ready 的
        // SetRunActive(false)；遭遇组已能自动触发，无需额外启用）。
        _gs.FogEvents.SetRunActive(true);
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
        // 擦弹计数：订阅 GrazeArea 的 AreaEntered（与 Player 自身 handler 并存，不消费 _graze_done 标志）
        var grazeArea = _player.GetNodeOrNull<Area2D>("GrazeArea");
        if (grazeArea != null)
        {
            grazeArea.AreaEntered += OnGrazeAreaEntered;
        }
        _lastHp = _gs.Health;
        _lastScore = _gs.Score;
        _scoreChangeMsec = (long)Time.GetTicksMsec();
        Engine.TimeScale = TimeScale;
        _started = true;
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
            UpdateParry(now);
            UpdateBoost(now);
            UpdateGiveUp(now);
            UpdateFineMove(now);
            UpdateGrazeSeek(now);
            UpdateBuffPanel(now);
            UpdatePause(now);
            UpdateSettings(now);
        }
        TrackMothership(now);
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
        _parryWasActive = false;
        _grazeSeekUntil = 0;
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
        Input.ActionRelease("parry");
        Input.ActionRelease("boost");
        Input.ActionRelease("give_up");
        Input.ActionRelease("fine_move");
        _dockHolding = false;
        _homeHolding = false;
        _earlyHolding = false;
        _dashReleaseAt = 0;
        _parryReleaseAt = 0;
        _boostHolding = false;
        _giveUpHolding = false;
        _fineMoveHolding = false;
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
        GD.Print($"[AUTOPLAY] 战斗输入覆盖: 弹反 {_parryCount} 次（进入有效窗 {_parryActiveSeen} 次）| 加速 {_boostCount} 次 | 自毁探针 {_giveUpProbes} 次 | 微调 {_fineMoveCount} 次 | 擦弹接触 {_grazeCount} 次 | buff_panel {_buffPanelToggles} 次");
        GD.Print($"[AUTOPLAY] 事件监控补齐: 轨道打击 {_strikeCount} 次 | 迷雾事件 {_fogCount} 次");
        GD.Print($"[AUTOPLAY] 峰值: 节点 {_maxNodes} | 敌弹 {_maxEnemyBullets} | 玩家弹 {_maxPlayerBullets} | 敌机 {_maxEnemies} | 孤儿节点 {_maxOrphans:0} | 资源 {_maxResources:0} | 池(b={_maxBulletPool},e={_maxEnemyPool}) | 帧耗时 {_maxFrameMs:0.00}ms（基线 {_frameMsBaseline:0.00}ms）");
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
