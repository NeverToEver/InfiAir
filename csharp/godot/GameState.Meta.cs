using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（局外成长 Meta Progression，2026-08-09 计划 M3）：
/// 科技点死亡结算 / 升级消费 / 新局开局预置 buff 层数。
/// 数据模型与公式在 InfiAir.Core.Meta（纯逻辑，xUnit 直测）；本文件为 Godot 绑定层：
/// UserDb meta 档案读写 + balance.json meta 节配置缓存 + Buffs 预置应用。
/// 仅登录用户（游客不持久化，B7-8 口径延伸）；结算唯一入口 = SettleRun（死亡）——
/// AC26（2026-08-11 审计订正）：ExitConfirm 删档不结算；K 键自毁（give_up）经 PlayerDied
/// 按死亡正常结算——防刷点口径仅限删档退出（与实现/DESIGN_BASELINE 一致）。
/// 详见 docs/archive/2026-08-09-meta-progression-plan.md。
/// 第三轮拆域（2026-08-11）：职责迁至 MetaService（csharp/godot/MetaService.cs，组合持有），
/// 本文件为门面转发——公开 API 签名/语义不变（测试与 UI 经此处零适配调用）；TechPointsChanged
/// 信号由 MetaService 的 C# 事件经 GameState 订阅重发，消费方（ResearchLab 等）不变。
/// </summary>
public partial class GameState : Node
{

    // ---------------- 局外成长（2026-08-09） ----------------

    /// <summary>科技点余额（登录用户会话态；每次变更即落盘 UserDb）——MetaService 转发。</summary>
    public long TechPoints => _meta.TechPoints;

    /// <summary>会话 meta 档案加载（LoadSessionSettings/登出/游客切换调用；非登录用户清空内存态）
    /// ——第七轮拆域：UserSessionService 跨域经 Instance 调用（LoadMeta 可见性提升，
    /// 与第六轮 RefreshRegenCache private→public 先例同款）。</summary>
    public void LoadMeta() => _meta.LoadMeta();

    /// <summary>meta 节配置缓存（ApplyBalance 调用；键走 Cfg 静态调用 → BALANCE_MAP 收录）</summary>
    private void LoadMetaConfig() => _meta.LoadMetaConfig();

    /// <summary>死亡结算科技点（SettleRun 内调用，本局唯一结算入口；游客/未登录/零收益跳过）。
    /// 公式见 core MetaProgression.PointsForRun；任务按已领取（claimed）计。</summary>
    public void SettleTechPoints() => _meta.SettleTechPoints();

    /// <summary>升级项 id 列表（UI 列表构建；顺序 = balance.json meta.upgrades 声明序）。</summary>
    public Godot.Collections.Array<StringName> MetaUpgradeIds() => _meta.MetaUpgradeIds();

    /// <summary>升级项满级等级（UI 显示 Lv.x/y；未知项回 0）。</summary>
    public int MetaMaxLevel(StringName id) => _meta.MetaMaxLevel(id);

    /// <summary>当前等级（未购/未知项回 0）。</summary>
    public int MetaLevel(StringName id) => _meta.MetaLevel(id);

    /// <summary>升到下一级的费用（已满级/未知项回 0）。</summary>
    public long MetaUpgradeCost(StringName id) => _meta.MetaUpgradeCost(id);

    /// <summary>是否可升级（未登录用户仅读余额/等级，消费走 SpendTechPoints 守卫）。</summary>
    public bool CanUpgradeMeta(StringName id) => _meta.CanUpgradeMeta(id);

    /// <summary>消费科技点升级（未登录/余额不足/已满级返回 false）；成功即时落盘 UserDb。</summary>
    public bool SpendTechPoints(StringName id) => _meta.SpendTechPoints(id);

    /// <summary>新局开局预置：已购升级 → Buffs 初始层数（Main.ApplyNewRun 调用；
    /// tutorial/存档恢复路径不经过——教程隔离、继续对局 buffs 从存档恢复，均不预置）。</summary>
    public void ApplyMetaLoadout() => _meta.ApplyMetaLoadout();
}
