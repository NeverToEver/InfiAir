using Godot;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：天赋缓存系统（替代里程碑三选一）。
/// 业务实现在 TalentService（csharp/godot/TalentService.cs，组合持有），本文件为门面：
/// 常用读取走转发（效果消费端零适配），面板等富交互经 Talent 属性直用服务
/// （与 FogEvents/Events 服务暴露先例同构）。
/// 信号：TalentService 的 C# 事件 CacheChanged/TalentsChanged 经此处订阅转发为
/// TalentCacheChanged/TalentsChanged（HUD 缓存指示器/天赋面板数据源；ResetRun
/// 由服务事件同源触发，无双发）。
/// </summary>
public partial class GameState : Node
{

    /// <summary>缓存池变化（有效点数, 原始点数）：HUD 缓存指示器刷新。</summary>
    [Signal]
    public delegate void TalentCacheChangedEventHandler(double effective, int raw);

    /// <summary>天赋配置变化（加点/路线/代币）：天赋面板与关联 UI 刷新。</summary>
    [Signal]
    public delegate void TalentsChangedEventHandler();

    /// <summary>天赋缓存域服务（面板等富交互入口）。</summary>
    public TalentService Talent => _talent;

    /// <summary>缓存原始点数（含已衰减点）。</summary>
    public int TalentRawCache => _talent.RawCache;

    /// <summary>节点已购层级。</summary>
    public int TalentLevel(StringName id) => _talent.Level(id);

    /// <summary>浮点有效层级（收益递减/路线/专注折算后）——乘算类效果桥。</summary>
    public double TalentEffLevel(StringName id) => _talent.EffLevel(id);

    /// <summary>节点生效上限（互斥/路线折算后）。</summary>
    public int TalentCap(StringName id) => _talent.CapFor(id);

    /// <summary>加点（上限档自动风险加点）；失败无副作用。</summary>
    public bool TalentUpgrade(StringName id) => _talent.Upgrade(id);

    // 事件转发（订阅点在 _Ready；触发点均为运行期对局事件/玩家操作，晚于订阅）
    private void OnTalentCacheChanged(double effective, int raw) => EmitSignal(SignalName.TalentCacheChanged, effective, raw);

    private void OnTalentsChanged() => EmitSignal(SignalName.TalentsChanged);
}
