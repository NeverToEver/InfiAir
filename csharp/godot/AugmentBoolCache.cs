using Godot;

namespace InfiAir;

/// <summary>增幅 层数→布尔缓存（AugmentsChanged 信号驱动；热路径禁字典约定）。
/// 3 处同构收敛（Enemy slow_field / Boss slow_field / LaserWeapon laser_beam）——
/// 每物理帧直读 GameState.AugmentLevel 字典查询改由信号事件驱动。
/// id 由构造注入；信号触发时内部刷新自身（零闭包捕获，NRT 安全）。</summary>
public sealed class AugmentBoolCache
{
    private readonly StringName _id;
    private readonly Callable _callable;
    private bool _value;

    public bool Value => _value;

    public AugmentBoolCache(StringName id)
    {
        _id = id;
        _callable = Callable.From(Refresh);
    }

    /// <summary>立即刷新（接入方 Connect 后初始化调用；AugmentsChanged 信号触发时自动调用）。</summary>
    public void Refresh() => _value = (int)GameState.Instance.AugmentLevel(_id) > 0;

    public void Connect(GameState gs)
    {
        if (gs != null && !gs.IsConnected(GameState.SignalName.AugmentsChanged, _callable))
        {
            gs.Connect(GameState.SignalName.AugmentsChanged, _callable);
        }
    }

    public void Disconnect(GameState gs)
    {
        if (gs != null && gs.IsConnected(GameState.SignalName.AugmentsChanged, _callable))
        {
            gs.Disconnect(GameState.SignalName.AugmentsChanged, _callable);
        }
    }

    /// <summary>只读探针口：当前是否已接到 AugmentsChanged。敌机池化复用的 reparent 会触发
    /// `_ExitTree`，连/断一旦错序，缓存整个活跃期不再刷新（表现是「买了力场没感觉」，零报错），
    /// 故连接态本身需要可判定——修复的构造性保证由本口变成可失败断言。</summary>
    public bool IsConnectedTo(GameState gs) =>
        gs != null && gs.IsConnected(GameState.SignalName.AugmentsChanged, _callable);
}
