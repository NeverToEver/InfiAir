namespace InfiAir.Core.Tutorial;

/// <summary>
/// 单个阶段内的目标进度与达成判据（纯逻辑，零 Godot 依赖）：节点只喂事件
/// （击杀 / 按下加速 / 触发突进 / 蓄力达成 / 首领狂暴），判据与「还差多少」在此收口。
///
/// 为什么要单独一份：达成判据此前散在节点的三处分支里（击杀计数比较、机动计数比较、
/// 蓄力触发即过关），补刷兜底又各自再算一遍「还差几个」——两处算同一件事时，改成
/// 「达标后不再补刷」或「剩余数取整方向不同」不会有任何信号，表现是补刷多余目标或永远差一个。
///
/// 计数按目标数封顶：文案补参取 <see cref="KillCount"/> 这类读数，玩家不会看到 4/3。
/// </summary>
public sealed class TutorialProgress
{
    /// <summary>当前阶段定义（构造后由 <see cref="EnterStage"/> 改写）。</summary>
    public TutorialStage Stage { get; private set; } = TutorialCurriculum.Stages[0];

    /// <summary>已击杀数（训练靶与实战阶段使用）。</summary>
    public int Kills { get; private set; }

    /// <summary>已弹反次数（弹反阶段使用）。</summary>
    public int Parries { get; private set; }

    /// <summary>已加速次数（机动阶段使用）。</summary>
    public int Boosts { get; private set; }

    /// <summary>已相位突进次数（机动阶段使用）。</summary>
    public int Dashes { get; private set; }

    /// <summary>蓄力达标是否已达成（停靠与返航阶段使用）。</summary>
    public bool Charged { get; private set; }

    /// <summary>增幅面板是否已实际打开（返航阶段使用：返航到基地后还需开一次面板）。</summary>
    public bool PanelOpened { get; private set; }

    /// <summary>首领是否已进入狂暴（首领阶段使用）。</summary>
    public bool Enraged { get; private set; }

    /// <summary>进入阶段：换定义并清空全部计数（重开本阶段与跳过都走这里，语义与首次进入一致）。</summary>
    public void EnterStage(TutorialStage stage)
    {
        Stage = stage;
        Kills = 0;
        Parries = 0;
        Boosts = 0;
        Dashes = 0;
        Charged = false;
        PanelOpened = false;
        Enraged = false;
    }

    public void AddKill() => Kills += 1;

    public void AddParry() => Parries += 1;

    public void AddBoost() => Boosts += 1;

    public void AddDash() => Dashes += 1;

    public void MarkCharged() => Charged = true;

    public void MarkPanelOpened() => PanelOpened = true;

    public void MarkEnraged() => Enraged = true;

    /// <summary>目标计数（单次触发型阶段恒为 1）。</summary>
    public int Goal => Stage.TargetCount;

    /// <summary>加速目标次数（机动阶段与补参用；其余形态同 <see cref="Goal"/>）。</summary>
    public int BoostGoal => Stage.Goal == TutorialGoalKind.Maneuver ? Stage.TargetCount : Goal;

    /// <summary>相位突进目标次数（机动阶段与补参用；其余形态同 <see cref="Goal"/>）。</summary>
    public int DashGoal => BoostGoal;

    /// <summary>文案补参用的击杀读数（按目标封顶）。</summary>
    public int KillCount => Cap(Kills);

    /// <summary>文案补参用的弹反读数（按目标封顶）。</summary>
    public int ParryCount => Cap(Parries);

    /// <summary>文案补参用的加速读数（按目标封顶）。</summary>
    public int BoostCount => Cap(Boosts);

    /// <summary>文案补参用的突进读数（按目标封顶）。</summary>
    public int DashCount => Cap(Dashes);

    /// <summary>当前阶段目标是否达成。返航段是双条件：蓄力返航**且**实际打开过一次增幅面板——
    /// 只返航不开面板时阶段停在后续目标行上，玩家还没摸到要教的那一面。</summary>
    public bool IsComplete => Stage.Goal switch
    {
        TutorialGoalKind.Marksmanship or TutorialGoalKind.Combat => Kills >= Stage.TargetCount,
        TutorialGoalKind.Maneuver => Boosts >= Stage.TargetCount && Dashes >= Stage.TargetCount,
        TutorialGoalKind.Parry => Parries >= Stage.TargetCount,
        TutorialGoalKind.Dock => Charged,
        TutorialGoalKind.Homecoming => Charged && PanelOpened,
        TutorialGoalKind.BossEnrage => Enraged,
        _ => false,
    };

    /// <summary>还差多少才达成（补刷目标数用）：计数型阶段返回剩余击杀数，其余形态未达成返回
    /// <see cref="Goal"/>、已达成返回 0——补刷兜底只需看「是否为零」，不必各阶段各判一次。</summary>
    public int Remaining
    {
        get
        {
            if (IsComplete)
            {
                return 0;
            }

            return Stage.Goal switch
            {
                TutorialGoalKind.Marksmanship or TutorialGoalKind.Combat => Stage.TargetCount - Kills,
                _ => Stage.TargetCount,
            };
        }
    }

    private int Cap(int value) => value > Stage.TargetCount ? Stage.TargetCount : value;
}
