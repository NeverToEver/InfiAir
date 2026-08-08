using Godot;

namespace InfiAir;

/// <summary>
/// 基地任务池（TaskPool，任务轮换核心）：无放回随机抽取任务定义。
/// 2026-08-07：抽取算法核心迁移 InfiAir.Core.Missions.TaskPool（csharp/core/Missions/TaskPool.cs），
/// 本壳保留公开签名（_init/draw）转发 C# 绑定壳 TaskPoolInterop，行为语义不变：
/// 单次 draw 内不重复；一次 draw 消耗完当前批次后若仍有名额且全池还有可用候选则重洗
/// 继续补足（跨 draw 尽量延迟复用，排除在场任务导致的提前耗尽不再截断——Q05）；
/// 排除覆盖全池时安全返回空。RNG 独立于 GDScript 全局随机源（性质等价、序列不等价——
/// 原 shuffle 依赖全局种子，无外部依赖具体序列）。
/// M7 全量迁移（2026-08-09 自 scripts/task_pool.gd）
/// </summary>
public partial class TaskPool : RefCounted
{
    private readonly TaskPoolInterop _interop = new();

    /// <summary>无参构造（M7：GDScript 脚本资源 new() 不支持带参——测试经 defs 属性注入）。</summary>
    public TaskPool()
        : this(new Godot.Collections.Array<Godot.Collections.Dictionary>())
    {
    }

    public TaskPool(Godot.Collections.Array<Godot.Collections.Dictionary> defs)
    {
        // Array<T> 不继承 untyped Array——经 Variant 桥转（同一原生数组，零拷贝）
        _interop.SetDefs(Variant.From(defs).AsGodotArray());
    }

    /// <summary>任务定义注入（M7：脚本资源 new() 无参后经此设置；C# 带参构造等价；untyped 接收 GDScript typed Array）。</summary>
    public Godot.Collections.Array defs
    {
        get => new Godot.Collections.Array(); // 定义已注入 interop（core TaskPool），无回读需求
        set => _interop.SetDefs(Variant.From(value).AsGodotArray());
    }

    /// <summary>抽取 count 个任务定义（无放回：单次 draw 内不重复、耗尽跨批补足直到可用候选抽完）。
    /// exclude_ids：排除这些 id（刷新时排除全部在场任务，防重号/覆盖保留任务）。
    /// 返回实际抽到的定义（排除覆盖全池时安全返回空，不抛错不死循环）。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> Draw(int count, Godot.Collections.Array<StringName> excludeIds)
    {
        var drawn = _interop.Draw(count, Variant.From(excludeIds).AsGodotArray());
        var outDefs = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var def in drawn)
        {
            outDefs.Add(def.AsGodotDictionary());
        }

        return outDefs;
    }

    /// <summary>可选参数重载：exclude_ids 缺省时为空数组（对齐 GDScript 默认参数 `= []`）。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> Draw(int count)
        => Draw(count, new Godot.Collections.Array<StringName>());

    // ---------------- GDScript 鸭子调用兼容桥（M7 过渡，删除前） ----------------
    // 调用方：autoload/game_state.gd（M7 并行迁移为 C# 后改 typed PascalCase）、
    // test/{base_task_refresh,task_pool_interop}_test.gd（GDScript 经脚本资源实例化后以
    // snake_case 名访问——桥保持原名精确匹配）。

    public Godot.Collections.Array<Godot.Collections.Dictionary> draw(int count, Godot.Collections.Array<StringName> excludeIds) => Draw(count, excludeIds);

    public Godot.Collections.Array<Godot.Collections.Dictionary> draw(int count) => Draw(count);
}
