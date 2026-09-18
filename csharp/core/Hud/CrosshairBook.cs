namespace InfiAir.Core.Hud;

/// <summary>
/// 准星档案簿（纯逻辑，零 Godot 依赖）：多档案并存 + 活跃索引。不可变——每个操作返回新簿，
/// 调用方（SettingsService）整体替换后存盘广播，不需要逐字段通知协议。
/// 两条硬规则：至少保留 <see cref="MinProfiles"/> 档（删光会让「活跃档案」失去所指）；
/// 删除/切换后的活跃索引必须重绑回界内（越界索引是绘制侧读崩的静默源）。
/// 空输入（旧档缺键）回退单条默认档。
/// </summary>
public sealed class CrosshairBook
{
    public const int MinProfiles = 1;

    private readonly List<CrosshairEntry> _entries;

    public CrosshairBook(IReadOnlyList<CrosshairEntry> entries, int activeIndex)
    {
        _entries = entries.Count > 0
            ? new List<CrosshairEntry>(entries)
            : [new CrosshairEntry(string.Empty, CrosshairProfile.Default)];
        ActiveIndex = activeIndex >= 0 && activeIndex < _entries.Count ? activeIndex : 0;
    }

    public IReadOnlyList<CrosshairEntry> Entries => _entries;

    public int ActiveIndex { get; }

    public CrosshairEntry Active => _entries[ActiveIndex];

    public CrosshairBook WithActive(int index) => ValidIndex(index) ? new CrosshairBook(_entries, index) : this;

    /// <summary>整档替换（编辑器写回；样式先归一，与读入口同口；活跃档不变）。</summary>
    public CrosshairBook WithProfileReplaced(int index, CrosshairProfile profile)
        => ValidIndex(index)
            ? WithEntryReplaced(index, _entries[index] with { Profile = profile.Normalized() })
            : this;

    public CrosshairBook WithRenamed(int index, string name)
        => ValidIndex(index) ? WithEntryReplaced(index, _entries[index] with { Name = name }) : this;

    /// <summary>追加并激活新档（新建副本/准星码导入共用语义：加完即可见）。</summary>
    public CrosshairBook WithAdded(string name, CrosshairProfile profile)
    {
        var next = new List<CrosshairEntry>(_entries)
        {
            new CrosshairEntry(name, profile.Normalized()),
        };
        return new CrosshairBook(next, next.Count - 1);
    }

    /// <summary>删除（下限守卫：仅剩一档时不删）。删除位之前的条目移动使活跃索引前移一位，
    /// 删除的正是活跃档时钳到尾档。</summary>
    public CrosshairBook WithRemoved(int index)
    {
        if (!ValidIndex(index) || _entries.Count <= MinProfiles)
        {
            return this;
        }

        var next = new List<CrosshairEntry>(_entries);
        next.RemoveAt(index);
        var active = ActiveIndex;
        if (index < active)
        {
            active -= 1;
        }
        else if (index == active && active > next.Count - 1)
        {
            active = next.Count - 1;
        }

        return new CrosshairBook(next, active);
    }

    private CrosshairBook WithEntryReplaced(int index, CrosshairEntry entry)
    {
        var next = new List<CrosshairEntry>(_entries)
        {
            [index] = entry,
        };
        return new CrosshairBook(next, ActiveIndex);
    }

    private bool ValidIndex(int index) => index >= 0 && index < _entries.Count;
}
