#if DEBUG || TOOLS
using Godot;
using InfiAir.Core.Tutorial;

namespace InfiAir;

public partial class ProbeHost
{
    /// <summary>教程全周期探针（--tutorial-probe）。</summary>
    private bool _tutorialProbe;

    /// <summary>启动教程探针：驱动节点挂到根上（教程死亡重开会重载当前场景，挂在场景里的探针
    /// 会随之被释放），再把当前场景换成生产教程场景。教程不经 Main，故本趟也不复用宿主里的 Main。</summary>
    private void StartTutorialProbe()
    {
        var driver = new TutorialProbeDriver
        {
            Name = "TutorialProbeDriver",
            ProcessMode = ProcessModeEnum.Always,
        };
        GetTree().Root.AddChild(driver);
        var err = GetTree().ChangeSceneToFile(TutorialProbeDriver.ScenePath);
        if (err != Error.Ok)
        {
            GD.PushError($"[tutorial-probe] 教程场景切换失败（{err}）——本趟判据取不到");
        }
    }
}

/// <summary>
/// 教程全周期探针（`--tutorial-probe`）：把生产场景 `scenes/tutorial.tscn` 当普通场景跑起来，
/// 经**生产输入面**（`Input.ActionPress/Release`）与**生产伤害入口**（`Enemy.TakeDamage` /
/// `Boss.TakeDamage`，与弹体命中同一条链）走满六阶段，判定全部落在玩家可见读数上
/// （阶段索引、标题与目标行文本、检查点、完成度），不注入无敌、不直接改血量或阶段。
///
/// 为什么必须探：教程是独立于 `Main` 的另一条生产入口，此前只有一趟「场景就绪」冒烟——
/// 阶段推进接线（达标 → 推进 → 下一阶段入场）、目标行补参成形（占位符与实参错位时玩家看到的
/// 是原样的 %s/%d）、键位感知（改键后目标行是否跟着变）、死亡重开本阶段与跳过，全是
/// 「不崩、不报错、只是没往下走」的形态，只判退出码抓不到。
///
/// 为什么驱动节点挂在**根**上：教程死亡重开走 `ReloadCurrentScene`（玩家重生不另造一套复活
/// 序列），当前场景会被换掉——挂在当前场景里的探针会跟着被释放；挂在根上则跨重载存活。
///
/// 三遍流程：第一遍从阶段 1 走满六阶段到完成（开跑前把 dock 改键到 J，断母舰阶段的目标行
/// 报出改后的键）；第二遍连跳两个阶段到实战、打到 2/5 后阵亡，断重开回到同一阶段且进度归零；
/// 第三遍连跳到停靠阶段，在**对接进行中**（玩家输入已被锁）跳过，断阶段推进、输入锁已解、
/// 玩家仍能移动——母舰被中途回收时它的解锁分支（RELEASE）永不执行，只断阶段索引判不出；
/// 收尾在返航阶段同按返航键与跳过键，断推进窗口内不会把基地面板弹出来。
/// </summary>
public partial class TutorialProbeDriver : Node
{
    /// <summary>生产教程场景路径（探针驱动的入口）。</summary>
    public const string ScenePath = "res://scenes/tutorial.tscn";

    /// <summary>场景加载等待上限（帧）：切场景/重载都在帧末生效，留 2 秒余量。</summary>
    private const int SceneLoadBudgetFrames = 120;

    /// <summary>单阶段推进的帧上限（帧）：最长的阶段是母舰停靠——蓄力 3s + 穿出/对接/补给
    /// 与提前离舰 ≈ 12s，取 20s 余量。超时即判失败，不静默空转到退出。</summary>
    private const int StageBudgetFrames = 1200;

    /// <summary>击杀节流（帧）：每 N 帧打一发生产伤害，给死亡结算与信号回调留出帧边界。</summary>
    private const int KillIntervalFrames = 3;

    /// <summary>Boss 输出节流（帧）与单发伤害：120 点血分多拍打到狂暴阈值（30%），
    /// 单发过大会越过阈值直接击杀（教程的达成判据是狂暴，不是击杀）。</summary>
    private const int BossDamageIntervalFrames = 20;

    private const int BossDamagePerHit = 6;

    /// <summary>两次相位突进之间的间隔（帧）：取生产冲刺冷却 4s + 余量（真按冷却走，
    /// 不用白盒写口——顺带覆盖「教程授予的相位突进真的能用」）。</summary>
    private const int DashGapFrames = 250;

    /// <summary>死亡重开的等待上限（帧）：提示 1.5s + 场景重载 ≈ 2s，取 6s 余量。</summary>
    private const int ReloadBudgetFrames = 360;

    /// <summary>机动阶段的按键编排（帧偏移 → 动作开关，偏移 **1 起**——步的第一帧 `_stepFrame`
    /// 已是 1，偏移写 0 的条目永不命中）：加速两次、突进两次，每次按下保持数帧以保证 rising edge
    /// 被采到，全部走生产输入面。</summary>
    private static readonly (int Frame, string Action, bool Press)[] ManeuverSchedule =
    {
        (1, "boost", true),
        (5, "boost", false),
        (13, "boost", true),
        (17, "boost", false),
        (31, "dash", true),
        (35, "dash", false),
        (31 + DashGapFrames, "dash", true),
        (35 + DashGapFrames, "dash", false),
    };

    /// <summary>改键判据用的键：dock 由 H 改到 J（两者都不得与其它动作冲突）。
    /// 判据只断「目标行报出了改后的键」——不断「不含出厂键 H」：英文文案 `Hold …` 自带大写 H，
    /// 那条断言在英文语言下会把正常文案判红（判据不得依赖某种语言的自然语言词形）。</summary>
    private const string ReboundDockKey = "J";

    private enum Step
    {
        WaitScene,       // 等教程场景就绪
        Rebind,          // 改键 dock（键位感知的判据来源）
        KillStage,       // 清场型阶段（训练靶 / 实战）：生产伤害击落
        Maneuver,        // 机动：加速 ×2 + 相位突进 ×2
        Dock,            // 母舰停靠：长按蓄力 → 对接补给 → 提前离舰
        Home,            // 返航：长按蓄力 → 基地
        Boss,            // 首领：打到狂暴
        VerifyDone,      // 断完成度与检查点清零
        Reload,          // 重载取一趟干净的教程（完成态的实例不可再用）
        Skip,            // 长按跳过本阶段
        DeathRun,        // 实战打到部分进度后阵亡
        VerifyRetry,     // 断重开回同一阶段且进度归零
        DockSummon,      // 长按召唤母舰直到进入对接（玩家输入被锁）
        DockSkipHold,    // 对接中长按跳过本阶段（与 Skip 同一段驱动，目标阶段不同）
        DockSkipVerify,  // 断阶段推进、输入锁已解、玩家仍能移动
        HomeSkipHold,    // 返航阶段同按返航 + 跳过：推进窗口内不得把基地弹出来
        Done,
    }

    private Step _step = Step.WaitScene;
    /// <summary>通用步（清场 / 跳过）的期望阶段与走完后的下一站：两遍流程复用同一段驱动。</summary>
    private int _expectStage;
    private Step _nextStep;

    /// <summary>连跳编排：当前这一跳的目标阶段（1 → 2）、两跳走完后的下一站。</summary>
    private int _skipTarget;
    private Step _afterChain;

    private Tutorial? _tutorial;
    private Player? _player;
    private int _stepFrame;
    private int _totalFrames;
    private int _checkedStage = -1;
    private int _killsBeforeDeath = -1;
    private bool _reloadRequested;
    private Tutorial? _reloadFrom;
    private Tutorial? _deathFrom;
    private bool _failed;
    private float _moveProbeStartY;

    /// <summary>完成标记只打一次（门禁按它判红绿；重复打标记会让「跑了两遍」看起来正常）。</summary>
    private bool _markerPrinted;

    public override void _Process(double delta)
    {
        if (_failed || _step == Step.Done)
        {
            return;
        }

        _totalFrames++;
        _stepFrame++;
        if (_step == Step.Reload)
        {
            DriveReload();
            return;
        }

        var tutorial = CurrentTutorial();
        if (tutorial == null)
        {
            var waitingForReload = _step is Step.VerifyRetry or Step.Skip or Step.DockSummon;
            if (_step == Step.WaitScene && _stepFrame > SceneLoadBudgetFrames)
            {
                Fail($"教程场景 {SceneLoadBudgetFrames} 帧内未就绪（切场景失败？）");
            }
            else if (waitingForReload && _reloadRequested && _stepFrame > ReloadBudgetFrames)
            {
                Fail("教程场景重载后未在预算内就绪（ReloadCurrentScene 未生效？）");
            }

            return;
        }

        // 场景重载会换掉教程节点：换新实例时清掉阶段级观测态（阶段读数需在新实例上重判）
        if (!ReferenceEquals(tutorial, _tutorial))
        {
            _tutorial = tutorial;
            _player = tutorial.GetNodeOrNull<Player>("Player");
            _checkedStage = -1;
            _stepFrame = 0;
            _reloadRequested = false;
        }

        switch (_step)
        {
            case Step.WaitScene:
                StepTo(Step.Rebind);
                break;
            case Step.Rebind:
                RebindDockKey();
                break;
            case Step.KillStage:
                DriveKillStage();
                break;
            case Step.Maneuver:
                DriveManeuverStage();
                break;
            case Step.Dock:
                DriveDockStage();
                break;
            case Step.Home:
                DriveHomeStage();
                break;
            case Step.Boss:
                DriveBossStage();
                break;
            case Step.VerifyDone:
                VerifyCompletion();
                break;
            case Step.Reload:
                break; // 已在上面提前返回
            case Step.Skip:
                DriveSkip();
                break;
            case Step.DeathRun:
                DriveDeathRun();
                break;
            case Step.VerifyRetry:
                VerifyRetry();
                break;
            case Step.DockSummon:
                DriveDockSummon();
                break;
            case Step.DockSkipHold:
                DriveSkip();
                break;
            case Step.DockSkipVerify:
                DriveDockSkipVerify();
                break;
            case Step.HomeSkipHold:
                DriveHomeSkipHold();
                break;
        }
    }

    public override void _ExitTree()
    {
        ReleaseKeys();
    }

    // ---------------- 各阶段驱动 ----------------

    /// <summary>改键 dock 到 J：母舰停靠阶段的目标行必须报出改后的键（键位感知的端到端判据——
    /// 写死键名的实现在这里报出 H，红）。</summary>
    private void RebindDockKey()
    {
        if (!GameState.Instance.RebindAction(new StringName("dock"), (int)Key.J))
        {
            Fail("生产改键口拒绝了 dock → J（键位占用或动作不在可改清单？）");
            return;
        }

        Begin(Step.KillStage, expectStage: 0, next: Step.Maneuver);
    }

    /// <summary>清场型阶段（训练靶 / 实战）：逐帧经生产伤害入口击落场上目标，阶段推进即算过。
    /// 期望阶段与下一站由 <see cref="Begin"/> 注入，两遍流程共用同一段驱动。</summary>
    private void DriveKillStage()
    {
        if (StageAdvanced(_expectStage, _nextStep))
        {
            return;
        }

        CheckStageReadout(_expectStage);
        if (_stepFrame % KillIntervalFrames == 0)
        {
            DamageEnemiesOnField();
        }

        OverBudget("清场");
    }

    /// <summary>机动阶段：按编排下发加速与突进（各两次），阶段推进即算过——下一站是实战阶段
    /// 的清场步（同一段驱动，期望阶段换成 2）。</summary>
    private void DriveManeuverStage()
    {
        var index = _tutorial!.StageIndex();
        if (index != 1)
        {
            if (index < 1)
            {
                Fail($"阶段落点不符：期望阶段 2（机动），实际 {index + 1}");
                return;
            }

            Begin(Step.KillStage, expectStage: 2, next: Step.Dock);
            return;
        }

        CheckStageReadout(1);
        foreach (var (frame, action, press) in ManeuverSchedule)
        {
            if (frame == _stepFrame)
            {
                SetAction(action, press);
            }
        }

        OverBudget("机动");
    }

    /// <summary>母舰停靠阶段：按住召唤键不放——先走完蓄力召唤，再在驻留期触发提前离舰
    /// （两者读同一动作）；离场即过关。</summary>
    private void DriveDockStage()
    {
        if (StageAdvanced(3, Step.Home))
        {
            Input.ActionRelease(new StringName("dock"));
            return;
        }

        CheckStageReadout(3);
        Input.ActionPress(new StringName("dock"));
        OverBudget("母舰停靠");
    }

    /// <summary>返航阶段：长按蓄力（1.5s＝90 帧，取余量后松手）→ 打开基地 → 自动过关。</summary>
    private void DriveHomeStage()
    {
        if (StageAdvanced(4, Step.Boss))
        {
            return;
        }

        CheckStageReadout(4);
        if (_stepFrame < 120)
        {
            Input.ActionPress(new StringName("homecoming"));
        }
        else
        {
            Input.ActionRelease(new StringName("homecoming"));
        }

        OverBudget("返航");
    }

    /// <summary>首领阶段：分拍打到狂暴阈值（教程以狂暴为达成判据，击杀不算过关）。</summary>
    private void DriveBossStage()
    {
        if (StageAdvanced(5, Step.VerifyDone))
        {
            return;
        }

        CheckStageReadout(5);
        // 末阶段的完成标志是完成态本身（不再有下一阶段索引可等）
        if (_tutorial!.IsFinished())
        {
            StepTo(Step.VerifyDone);
            return;
        }

        if (_stepFrame % BossDamageIntervalFrames == 0)
        {
            DamageBossOnField();
        }

        OverBudget("首领");
    }

    private void VerifyCompletion()
    {
        if (!_tutorial!.IsFinished())
        {
            Fail("首领阶段结束后教程未进入完成态");
            return;
        }

        if (!GameState.Instance.TutorialDone)
        {
            Fail("教程完成态未写完成度（tutorial_done）");
            return;
        }

        if (GameState.Instance.TutorialStage != 0)
        {
            Fail($"教程完成后检查点未清零（tutorial_stage={GameState.Instance.TutorialStage}）");
            return;
        }

        _afterChain = Step.DeathRun;
        StepTo(Step.Reload);
    }

    /// <summary>跳过判据：长按放弃出击键越过阈值即推进到下一阶段，检查点同步跟进
    /// （跳过链断线时玩家被卡在某一步，而引擎侧零错误）。目标阶段与下一站由 <see cref="Begin"/>
    /// 注入——第一遍用它在两处跳过之间衔接，第二遍用它从停靠阶段跳出去。</summary>
    private void DriveSkip()
    {
        var tutorial = _tutorial!;
        if (tutorial.StageIndex() != _skipTarget)
        {
            Input.ActionPress(new StringName("give_up"));
            OverBudget("跳过");
            return;
        }

        if (GameState.Instance.TutorialStage != _skipTarget)
        {
            Fail($"跳过后的检查点与阶段不符（tutorial_stage={GameState.Instance.TutorialStage}，"
                + $"阶段 {tutorial.StageIndex() + 1}）");
            return;
        }

        Input.ActionRelease(new StringName("give_up"));
        if (_skipTarget == 1)
        {
            _skipTarget = 2; // 连跳的第二跳（阶段 2 → 3）
            StepTo(Step.Skip);
            return;
        }

        EnterAfterChain();
    }

    /// <summary>连跳（阶段 1 → 2）走完后的落地：第二遍去「阵亡重开」（该步本就在实战阶段）；
    /// 第三遍的落点是停靠阶段，先经实战阶段清场过去——两遍的落点不同，故在连跳里统一分派。</summary>
    private void EnterAfterChain()
    {
        if (_afterChain == Step.DockSummon)
        {
            Begin(Step.KillStage, expectStage: 2, next: Step.DockSummon);
            return;
        }

        StepTo(_afterChain);
    }

    /// <summary>从「重载后的干净教程」起步的两连跳（阶段 1、阶段 2）：第二遍据此到实战阶段
    /// 做阵亡重开，第三遍据此停靠阶段做「对接中跳过」。</summary>
    private void BeginSkipChain()
    {
        _skipTarget = 1;
        StepTo(Step.Skip);
    }

    /// <summary>停靠中的跳过前置：长按召唤键直到对接开始（玩家输入被锁、机体进舱）——
    /// 这一锁是教程里唯一的上锁点，母舰被中途回收时它的解锁分支永不执行。</summary>
    private void DriveDockSummon()
    {
        var tutorial = _tutorial!;
        if (tutorial.StageIndex() != 3)
        {
            Fail($"对接中跳过的前置阶段不符：期望阶段 4（母舰停靠），实际 {tutorial.StageIndex() + 1}");
            return;
        }

        CheckStageReadout(3);
        Input.ActionPress(new StringName("dock"));
        if (_player is { } player && player.IsInputLocked())
        {
            Input.ActionRelease(new StringName("dock"));
            _skipTarget = 4;
            _afterChain = Step.DockSkipVerify;
            StepTo(Step.DockSkipHold);
            return;
        }

        OverBudget("对接召唤");
    }

    /// <summary>对接中跳过的判据：阶段推进到返航、输入锁已解、玩家仍能移动——母舰被中途回收时
    /// 它的解锁分支（RELEASE）永不执行，写坏的表现是此后阶段玩家完全不能动、不能开火，
    /// 而引擎侧零报错（只断阶段索引会让这条溜过去）。</summary>
    private void DriveDockSkipVerify()
    {
        var player = _player;
        if (player == null)
        {
            Fail("对接中跳过后取不到玩家节点");
            return;
        }

        if (_tutorial!.StageIndex() != 4)
        {
            Fail($"对接中跳过未推进到返航阶段（实际 {_tutorial.StageIndex() + 1}）");
            return;
        }

        if (player.IsInputLocked())
        {
            Fail("对接中跳过之后玩家输入仍是锁死状态（母舰被回收时未解锁，后续阶段不能动也不能开火）");
            return;
        }

        if (_stepFrame < 10)
        {
            return; // 让出舱/落位的那几帧先过去，再量移动
        }

        if (_stepFrame == 10)
        {
            _moveProbeStartY = player.GlobalPosition.Y;
            Input.ActionPress(new StringName("move_up"));
        }
        else if (_stepFrame > 40)
        {
            Input.ActionRelease(new StringName("move_up"));
            var moved = System.Math.Abs(_moveProbeStartY - player.GlobalPosition.Y);
            if (moved < 5.0f)
            {
                Fail($"对接中跳过后玩家推杆不动（位移 {moved:0.#}px）——输入通道被锁死");
                return;
            }

            StepTo(Step.HomeSkipHold);
        }
    }

    /// <summary>返航阶段的跳过：同按返航键与跳过键。跳过落地后的推进窗口（1s）里本阶段仍是返航，
    /// 若蓄力分支不受推进门控，1.5s 的蓄力会在那一刻把基地面板弹在下一阶段头上——树被暂停 1.2s、
    /// Boss 在基地背后入场，引擎侧零报错。判据是「窗口内树从未被暂停」。</summary>
    private void DriveHomeSkipHold()
    {
        if (_tutorial!.StageIndex() == 5)
        {
            Input.ActionRelease(new StringName("homecoming"));
            Input.ActionRelease(new StringName("give_up"));
            GameState.Instance.ResetKeyBindings(); // 恢复出厂键位（本趟改键只为键位感知判据）
            _step = Step.Done;
            PrintMarker();
            return;
        }

        if (GetTree().Paused)
        {
            Fail("跳过返航阶段后基地面板仍被弹出来（推进窗口内未门控蓄力，下一阶段被压在基地之下）");
            return;
        }

        Input.ActionPress(new StringName("homecoming"));
        Input.ActionPress(new StringName("give_up"));
        OverBudget("返航阶段跳过");
    }

    /// <summary>死亡重开的前置：在实战阶段打到部分进度（2/5）再阵亡——重开判据据此断
    /// 「同一阶段 + 进度归零」；只断阶段索引会让「进度没清」溜过去。</summary>
    private void DriveDeathRun()
    {
        var tutorial = _tutorial!;
        if (tutorial.StageIndex() != 2)
        {
            Fail($"死亡重开的前置阶段不符：期望阶段 3（实战），实际 {tutorial.StageIndex() + 1}");
            return;
        }

        if (_killsBeforeDeath < 0)
        {
            if (_stepFrame % KillIntervalFrames == 0)
            {
                // 一次只打一架：一发全清会让「重开后进度归零」的判据失去区分度
                // （全清时阵亡前读数已是 5/5，重开后 0/5 与 5/5 的差别仍可判，但「部分进度」
                // 这一更贴近真实玩法的场景就没人覆盖了）
                DamageOneEnemyOnField();
            }

            var shown = KillProgressShown();
            if (shown >= 2)
            {
                _killsBeforeDeath = shown;
                _deathFrom = tutorial; // 阵亡时的实例：重开判据是「场景上换成了另一个实例」
                _player?.Die(); // 生产死亡路径（死亡删档钩子按本局门控，教程非本局）
                _stepFrame = 0;
                return;
            }

            OverBudget("阵亡前置");
            return;
        }

        // 阵亡后教程经 ReloadCurrentScene 重开：等新实例出现（阶段读数须在新实例上重判）
        if (!ReferenceEquals(_tutorial, _deathFrom))
        {
            StepTo(Step.VerifyRetry);
            return;
        }

        if (_stepFrame > ReloadBudgetFrames)
        {
            Fail($"阵亡后 {ReloadBudgetFrames} 帧内教程未重开（重开链断线）");
        }
    }

    private void VerifyRetry()
    {
        var tutorial = _tutorial!;
        if (tutorial.StageIndex() != 2)
        {
            Fail($"死亡重开落点不是阵亡时的阶段（实际 {tutorial.StageIndex() + 1}，期望 3）");
            return;
        }

        var shown = KillProgressShown();
        if (shown != 0)
        {
            Fail($"死亡重开后阶段进度未归零（目标行读数 {shown}，阵亡前 {_killsBeforeDeath}）");
            return;
        }

        if (GameState.Instance.TutorialStage != 2)
        {
            Fail($"死亡重开后的检查点不符（tutorial_stage={GameState.Instance.TutorialStage}）");
            return;
        }

        // 第三遍要的是「从头连跳」：检查点此刻停在阵亡时的阶段（重开落点），先复位再重载。
        // 这是探针布景（同各趟预置 settings.json/run.json 的做法），不是生产行为。
        GameState.Instance.TutorialStage = 0;
        GameState.Instance.SaveSettings();
        _afterChain = Step.DockSummon;
        StepTo(Step.Reload);
    }

    // ---------------- 断言与工具 ----------------

    /// <summary>阶段推进判据：阶段索引越过期望值即本阶段完成（推进链有一秒延迟，索引先于文案切换）。
    /// 小于期望值即阶段回退/落点不符，直接判红。</summary>
    private bool StageAdvanced(int expectedStage, Step next)
    {
        var index = _tutorial!.StageIndex();
        if (index < expectedStage)
        {
            Fail($"阶段落点不符：期望阶段 {expectedStage + 1}，实际 {index + 1}");
            return true;
        }

        if (index == expectedStage)
        {
            return false;
        }

        StepTo(next);
        return true;
    }

    /// <summary>阶段读数检查（每个阶段首次进入时判一次）：
    /// ① 标题与目标行不得为空、不得残留占位符（补参与译文不匹配时玩家看到原样的 %s/%d）；
    /// ② 母舰停靠阶段的目标行必须报出改键后的键（J），不得再出现出厂键（H）；
    /// ③ 进入阶段时检查点已写入（续接落点的单源）。</summary>
    private void CheckStageReadout(int stage)
    {
        if (_checkedStage == stage)
        {
            return;
        }

        _checkedStage = stage;
        var text = _tutorial!.ObjectiveText();
        if (text.Length == 0 || _tutorial.TitleText().Length == 0)
        {
            Fail($"阶段 {stage + 1} 的标题或目标行为空（文案缺键时界面直接显示键名本身）");
            return;
        }

        if (PlaceholderPattern().IsMatch(text))
        {
            Fail($"阶段 {stage + 1} 的目标行残留占位符（补参与译文不匹配）：{text}");
            return;
        }

        if (KillReadout(text, out var killed, out var goal))
        {
            var expected = TutorialCurriculum.At(stage).TargetCount;
            if (goal <= 0 || killed > goal)
            {
                Fail($"阶段 {stage + 1} 的目标行读数越界（{killed}/{goal}）——补参错位或目标数写死");
                return;
            }

            if (goal != expected)
            {
                Fail($"阶段 {stage + 1} 的目标行分母与阶段表不符（文案 {goal}，阶段表 {expected}）");
                return;
            }

            if (killed != 0)
            {
                // 入场读数必须为零：同型补参对调（读数 ↔ 目标数）时形状判据看不出来，这一条能判
                Fail($"阶段 {stage + 1} 进入时目标读数不是 0（{killed}/{goal}）——补参与占位符错位");
                return;
            }
        }

        // 只断「报出了改后的键」：写死键名的实现那行里没有 J，照样红；再断「不含出厂键 H」会在
        // 英文文案（Hold …）下把正常文案判红——判据不得依赖某种语言的自然语言词形
        if (stage == 3 && !text.Contains(ReboundDockKey, System.StringComparison.Ordinal))
        {
            Fail($"母舰停靠阶段的目标行未跟随改键（应含 {ReboundDockKey}）：{text}");
            return;
        }

        if (GameState.Instance.TutorialStage != stage)
        {
            Fail($"进入阶段 {stage + 1} 时检查点未写入（tutorial_stage={GameState.Instance.TutorialStage}）");
        }
    }

    /// <summary>未替换的格式化占位符（`%d` / `%s` / `%.1f` / `%5d` 这类）。判据不能只看 `%`——
    /// 合法文案里有字面百分号（「需 25% 燃料」「召唤蓄力 40%…」），只看 `%` 会把正常文案判红。</summary>
    private static System.Text.RegularExpressions.Regex PlaceholderPattern()
        => new("%[.0-9]*[sdf]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>目标行里的击杀读数（「击落全部 5 架敌机（2/5）」里的 2/5）。按 `数字/数字` 形态解析，
    /// 不依赖括号全半角（文案语言可变）。</summary>
    private static bool KillReadout(string text, out int killed, out int goal)
    {
        killed = -1;
        goal = -1;
        var slash = text.IndexOf('/', System.StringComparison.Ordinal);
        if (slash <= 0)
        {
            return false;
        }

        var start = slash;
        while (start > 0 && char.IsAsciiDigit(text[start - 1]))
        {
            start--;
        }

        var end = slash + 1;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return int.TryParse(text[start..slash], out killed) && int.TryParse(text[(slash + 1)..end], out goal);
    }

    /// <summary>目标行里的击杀读数（非计数型阶段与解析失败返回 -1）。</summary>
    private int KillProgressShown()
    {
        KillReadout(_tutorial!.ObjectiveText(), out var killed, out _);
        return killed;
    }

    /// <summary>只击落场上第一架（阵亡前置要的是「部分进度」）。</summary>
    private void DamageOneEnemyOnField()
    {
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is Enemy enemy && GodotObject.IsInstanceValid(enemy))
            {
                enemy.TakeDamage(9999); // 生产伤害入口（与弹体命中同一条链）
                return;
            }
        }
    }

    private void DamageEnemiesOnField()
    {
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is Enemy enemy && GodotObject.IsInstanceValid(enemy))
            {
                enemy.TakeDamage(9999); // 生产伤害入口（与弹体命中同一条链）
            }
        }
    }

    private void DamageBossOnField() => FindBoss()?.TakeDamage(BossDamagePerHit, 1.0f);

    /// <summary>教程场上的首领（教程把 Boss 直接挂在场景根下；未在场返回 null）。</summary>
    private Boss? FindBoss()
    {
        foreach (var child in _tutorial!.GetChildren())
        {
            if (child is Boss boss && GodotObject.IsInstanceValid(boss))
            {
                return boss;
            }
        }

        return null;
    }

    private static void SetAction(string action, bool press)
    {
        var name = new StringName(action);
        if (press)
        {
            Input.ActionPress(name);
        }
        else
        {
            Input.ActionRelease(name);
        }
    }

    private void ReleaseKeys()
    {
        foreach (var action in new[] { "boost", "dash", "dock", "homecoming", "give_up" })
        {
            SetAction(action, press: false);
        }
    }

    /// <summary>当前场景上的教程节点（重载后是新实例；未就绪返回 null）。</summary>
    private Tutorial? CurrentTutorial()
    {
        var scene = GetTree().CurrentScene;
        if (scene is Tutorial tutorial && GodotObject.IsInstanceValid(tutorial))
        {
            return tutorial;
        }

        return null;
    }

    /// <summary>重载当前场景取一趟干净的教程（第二遍流程的起点；完成态的实例不能再跑）。
    /// 只请求一次——重载是帧末生效，逐帧重复请求会让场景永远在换；重载完成的判据是
    /// 「场景上出现了另一个教程实例」。</summary>
    private void DriveReload()
    {
        if (!_reloadRequested)
        {
            _reloadRequested = true;
            _reloadFrom = _tutorial;
            var err = GetTree().ReloadCurrentScene();
            if (err != Error.Ok)
            {
                Fail($"教程场景重载失败（{err}）——第二遍流程的判据取不到");
            }

            return;
        }

        var now = CurrentTutorial();
        if (now != null && !ReferenceEquals(now, _reloadFrom))
        {
            _reloadFrom = null;
            BeginSkipChain();
            return;
        }

        if (_stepFrame > ReloadBudgetFrames)
        {
            Fail($"重载后 {ReloadBudgetFrames} 帧内未取到新的教程实例");
        }
    }

    /// <summary>进入一个通用步：清场型阶段（期望阶段 + 走完后的下一站）用它注入参数，
    /// 多遍流程复用同一段驱动。</summary>
    private void Begin(Step step, int expectStage, Step next)
    {
        _expectStage = expectStage;
        _nextStep = next;
        StepTo(step);
    }

    /// <summary>步进并打一条诊断（教程阶段机是长链，红时必须能看出卡在哪一步；
    /// 只在步变化时打一行，不刷屏）。</summary>
    private void StepTo(Step next)
    {
        _step = next;
        _stepFrame = 0;
        GD.Print($"[tutorial-probe] 步 {next}（教程阶段 {(_tutorial?.StageIndex() ?? -1) + 1}）");
    }

    private void OverBudget(string what)
    {
        if (_stepFrame > StageBudgetFrames)
        {
            Fail($"{what}阶段 {StageBudgetFrames} 帧内未推进（阶段机卡住）");
        }
    }

    private void Fail(string message)
    {
        if (_failed)
        {
            return;
        }

        _failed = true;
        ReleaseKeys();
        GD.PushError($"[tutorial-probe] {message}");
    }

    private void PrintMarker()
    {
        if (_markerPrinted)
        {
            return;
        }

        _markerPrinted = true;
        GD.Print($"[tutorial-probe] 全周期完成（三遍流程共 {_totalFrames} 帧）");
    }
}
#endif
