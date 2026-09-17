using Godot;
using InfiAir.Core.Practice;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：练习局（口径见 DESIGN_BASELINE §1.16）。
///
/// 边界＝**独立于本局**：不写 user://run.json、不写 user://best.json（跨局记录）、不删检查点；
/// 练习局的 RunTime / 难度 / 里程碑照常在内存里推进（练习的手感必须与正局一致——连击窗口、
/// 受击喘息、难度时间档都是本局节奏的一部分，停掉它们等于练一个不存在的游戏），
/// 但它们只活在本局内存里：随下一次 ResetRun 丢弃，且因下面三处守卫**永不落盘**。
/// 因此「不影响玩家的任何进度门」在「玩家的盘上文件读不到练习的任何痕迹」这一条上是可判的。
///
/// 写入点一共三处，守卫就这三处（新增写入点时必须一并接上，否则练习会在无人察觉处写档案）：
/// - <see cref="SaveRun"/>（回基地自动存 / 退出确认「保存并退出」）：练习局不落盘；
/// - <see cref="DeleteRunSave"/>（死亡删档 / 放弃重开）：练习局不删档——玩家真实检查点原样保留；
/// - <see cref="RecordRunResult"/>（跨局记录）：练习局不写记录。
///
/// 死亡删档的本局门控（<c>_runActive</c>）在练习态下为真（练习局按生产语义活跃），
/// 故练习的「不删档」**靠本文件的守卫成立**，不能靠 _runActive——后者是另一条独立判据。
/// </summary>
public partial class GameState : Node
{
    private const string PracticeScenePath = "res://scenes/practice.tscn";

    /// <summary>待进入练习局的设置：练习面板置位，练习场景宿主在 _EnterTree 消费；从未选过时即
    /// <see cref="PracticeSetup.Default"/>（面板据此「记住上次的选择」，无待定与待定为默认值同解）。</summary>
    public PracticeSetup PendingPractice { get; private set; } = PracticeSetup.Default;

    /// <summary>本局是否练习局。置位期间三处进度写入点全部早退（见类头）。</summary>
    public bool PracticeActive { get; private set; }

    /// <summary>进行中练习局的设置（非练习时 = <see cref="PracticeSetup.Default"/>）。</summary>
    public PracticeSetup Practice { get; private set; } = PracticeSetup.Default;

    /// <summary>进入练习场景单口（标题屏面板 / 结算页轮盘共用）：复位暂停与时间缩放 + 清本局状态 +
    /// 记下待开始的设置 + 切场景。与 <see cref="ExitToTitle"/>/<see cref="RestartRun"/> 同构——
    /// 场景切换前的复位口径收在一个出口，新增入口直调即安全（漏复位会让练习局继承上一局的暂停态）。</summary>
    public void EnterPractice(PracticeSetup setup)
    {
        ResetTimeScale();
        SetTreePaused(false);
        ResetRun();
        PendingPractice = setup;
        ((SceneTree?)Engine.GetMainLoop())?.ChangeSceneToFile(PracticeScenePath);
    }

    /// <summary>取出待开始的练习设置（练习场景宿主 _EnterTree 调用；无待定时回默认）。
    /// **不清待定值**：同一个值就是练习面板「记住上次的选择」的来源（<c>PracticePanel</c> 直接读
    /// <see cref="PendingPractice"/>），清掉会把面板默认档打回出厂值；而「待定」也没有独立状态
    /// （值与默认值同解），重开一局由 <see cref="RearmPractice"/> 重新置位。</summary>
    public PracticeSetup ConsumePendingPractice() => PendingPractice;

    /// <summary>练习局开始：置练习态 + 应用起始难度档。
    /// 难度走读档同一条「只改内存、不写盘、不发信号」的应用口——面板上选的难度是**这一局的条件**，
    /// 不是玩家的设置，写进 settings.json 会让「练一次难档」变成永久改档。
    /// 在场景子节点 _Ready 之前调用（宿主 _EnterTree），HUD 首帧才读得到正确档位。</summary>
    public void BeginPractice(PracticeSetup setup)
    {
        Practice = setup;
        PracticeActive = true;
        ApplyLoadedDifficulty(new StringName(setup.DifficultyName));
    }

    /// <summary>练习局结束（回标题屏 / 重开下一局前由宿主自行重设）。</summary>
    public void EndPractice()
    {
        PracticeActive = false;
        Practice = PracticeSetup.Default;
    }

    /// <summary>练习重开：把当前设置重新置为待定，供场景重载后 _EnterTree 再消费
    /// （否则「重新出击」会掉进默认设置，练到一半的条件被静默换掉）。</summary>
    public void RearmPractice()
    {
        if (!PracticeActive)
        {
            return;
        }

        PendingPractice = Practice;
    }
}
