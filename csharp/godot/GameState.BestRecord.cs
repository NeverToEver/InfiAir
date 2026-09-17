using Godot;
using InfiAir.Core.Progression;
using InfiAir.Core.Storage;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：本局结果记录（user://best.json，跨局保留）。
///
/// 边界（DESIGN_BASELINE §1.16）：只记**局末结果读数**——存活时长 / Boss 击杀 / 最高难度档 /
/// 本局目标是否达成；**不含战力、不含分数**（计分仍是隐藏进度引擎），故不构成局外成长。
/// 与本局存档分区：死亡删档只作用于 run.json，本档案随之**更新**而非删除。
///
/// 落盘时机＝本局终结（死亡 / 放弃重开），与终结删档同两处调用点；仅在实际有改善时写，
/// 且只有「盘上记录暂时读不出」时不落盘——用一份读不出原因的旧档覆盖玩家更好的成绩，是静默丢记录。
/// 损坏档（已隔离）与版本不符档按无档处理，**可写**：它们不构成「可能更好的在盘记录」，
/// 一律挡住的结果是一次坏档让整场会话的记录静默丢失（与 run.json 读档口径一致）。
/// 字段编解码单源在 core <see cref="BestRecordCodec"/>（写读键名与判型口径只写在那里）。
/// </summary>
public partial class GameState : Node
{
    private const string BestPathValue = "user://best.json";

    /// <summary>跨局最好成绩（未取到可信记录时为 <see cref="BestRecord.Empty"/>）。</summary>
    public BestRecord Best { get; private set; } = BestRecord.Empty;

    /// <summary>盘上是否已取到可信记录（无档＝可信的空记录）。只有「暂时读不出」为 false——
    /// 那时盘上的记录可能是**比内存更好**的成绩，覆盖它就是静默丢记录。损坏档已被隔离
    /// （原路径上已无此档）、版本不符档按无档处理（与 run.json 读档同口径：版本不符＝不认这本档，
    /// 写侧不设卡），两者都不构成「可能更好的在盘记录」，故可写——否则一次损坏就让整场会话的
    /// 记录永不落盘（版本不符更是每一次都写不进去）。该位同时是结算页/标题屏那行的显示门控：
    /// 读不出（Unreadable）时留空，而不是把「读不出」说成「没打过」。</summary>
    public bool BestKnown { get; private set; }

    /// <summary>本局是否刷新了记录（本局终结时定格；结算页据此打「新纪录」）。
    /// 该位描述的是**当前这一局**，故两个复位点缺一不可：<see cref="ResetRun"/>（新一局从零起）
    /// 与「终结但不计入记录」（练习局/非本局，见 <see cref="RecordRunResult"/>）——后者是唯一
    /// 不给判定值的终结，漏清就让上一局的判定渗进这一局的结算页。</summary>
    public bool BestImprovedThisRun { get; private set; }

    /// <summary>读盘取记录（启动时调用；探针走同一入口复算坏档下的写门槛——重读路径与启动路径
    /// 共用本方法，不各写一套三态判定）。写门槛见 <see cref="RecordRunResult"/>。</summary>
    public void LoadBestRecord()
    {
        var result = LoadJsonCore(BestPathValue);
        if (result.Status == SaveLoadStatus.Ok && result.Tree is not null
            && BestRecordCodec.VersionMatches(result.Tree))
        {
            Best = BestRecordCodec.FromFields(result.Tree);
            BestKnown = true;
            return;
        }

        Best = BestRecord.Empty;
        BestKnown = result.Status != SaveLoadStatus.Unreadable;
    }

    /// <summary>本局终结时把本局结果并进记录。门控与本局存档的终结删档同源（<see cref="_runActive"/>）：
    /// 教程与标题屏的玩家实体死亡不是本局，不得写记录。练习局同属「不是本局」——
    /// 它按生产语义活跃（_runActive 为真），故必须另有 <see cref="PracticeActive"/> 这一条守卫，
    /// 否则练一次就把练习读数写进玩家的跨局记录。</summary>
    private void RecordRunResult()
    {
        if (!_runActive || PracticeActive)
        {
            // 不产生判定的终结必须当场清位：练习局（或教程/标题屏这类非本局）死亡的结算页
            // 若读到上一局留下的「刷新记录」，会把玩家上一局的最好读数当本局成绩打出来。
            BestImprovedThisRun = false;
            return;
        }

        var candidate = new BestRecord(RunTime, BossKills, DifficultyMultiplier, GoalAchieved());
        BestImprovedThisRun = Best.IsImprovedBy(candidate);
        if (!BestImprovedThisRun || !BestKnown)
        {
            return;
        }

        Best = Best.Merge(candidate);
        if (!_saveManager.Save(BestPathValue,
                VariantBridge.ToVariant(BestRecordCodec.ToFields(Best)).AsGodotDictionary()))
        {
            GD.PushWarning($"InfiAir: 写入 {BestPathValue} 失败——本局记录未落盘（下次终结再试）");
        }
    }
}
