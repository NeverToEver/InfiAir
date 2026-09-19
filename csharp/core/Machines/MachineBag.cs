using System;
using System.Collections.Generic;

namespace InfiAir.Core.Machines;

/// <summary>
/// 标题屏悬挂展示的机型轮展袋（纯逻辑，零 Godot 依赖）：一袋 = 传入名册里每一型各一次，
/// 抽完重洗；重洗后若首个与上一次抽出**同型**，就与袋内第一个异型对调——跨袋也不连出同型。
///
/// 为什么不是每次独立掷签：六型里连抽两次同型的概率是 1/6，玩家一晚上回标题屏十几次必然撞见，
/// 而那一瞬间读到的不是「随机」而是「没换」。无放回洗牌把这件事变成不可能，代价只是
/// 展示顺序有周期（六次一轮），而周期长度恰好等于名册长度，玩家读不出规律。
///
/// 不连出同型的判据是**机型 id** 而不是袋内下标：名册里若出现重复 id（配置手改），
/// 按 id 比较才不会把两架同型机当成两次不同的展示。
///
/// RNG 是可注入种子的独立 <see cref="Random"/>（同 <c>TaskPool</c> 口径）：同种子逐位可复现，
/// 不依赖 Godot 的全局随机序列。
/// </summary>
public sealed class MachineBag
{
    private readonly MachineSpec[] _roster;
    private readonly Random _random;
    private int[] _order = System.Array.Empty<int>();
    private int _cursor;
    private string _lastId = string.Empty;

    /// <param name="roster">展示名册（null 取 <see cref="MachineRoster.All"/>；顺序即洗牌前的基准序）。
    /// 同一 id 出现两次时只保留首个：展示的身份是 id，重复项会让「一袋各一次」与「不连出同型」
    /// 两条保证同时失真（袋内两个同型项相邻时必然连出）。</param>
    /// <param name="seed">随机种子（null 用时间种子；单测固定种子复现序列）。</param>
    public MachineBag(IReadOnlyList<MachineSpec>? roster = null, int? seed = null)
    {
        _roster = Distinct(roster ?? MachineRoster.All);
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// 下一次展示的机型。名册为空时回退 <see cref="MachineRoster.Default"/>——
    /// 空名册只可能来自调用方塞错表，而标题屏不该因为一张空表就展示不出机体（不抛错）。
    /// </summary>
    public MachineSpec Next()
    {
        if (_roster.Length == 0)
        {
            return MachineRoster.Default;
        }

        if (_cursor >= _order.Length)
        {
            Refill();
        }

        var spec = _roster[_order[_cursor++]];
        _lastId = spec.Id;
        return spec;
    }

    /// <summary>按 id 去重（保序，首个胜出）。</summary>
    private static MachineSpec[] Distinct(IReadOnlyList<MachineSpec> roster)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<MachineSpec>(roster.Count);
        foreach (var spec in roster)
        {
            if (seen.Add(spec.Id))
            {
                result.Add(spec);
            }
        }

        return result.ToArray();
    }

    /// <summary>重洗一袋：Fisher–Yates 洗下标，再把「与上次同型」的首位换掉。</summary>
    private void Refill()
    {
        _order = new int[_roster.Length];
        for (var i = 0; i < _order.Length; i++)
        {
            _order[i] = i;
        }

        for (var i = _order.Length - 1; i > 0; i--)
        {
            Swap(i, _random.Next(i + 1));
        }

        // 袋是排列，首位同型时往后找第一个异型换上来即可；找得到就换、找不到（全袋同型）就原样
        // ——不设「无解即抛」的非法态，最坏情况退化成连出同型，而不是标题屏打不开。
        for (var k = 1; k < _order.Length; k++)
        {
            if (!IsLastId(_roster[_order[k]].Id))
            {
                Swap(0, k);
                break;
            }
        }

        _cursor = 0;
    }

    private void Swap(int a, int b)
    {
        if (a == b)
        {
            return;
        }

        (_order[a], _order[b]) = (_order[b], _order[a]);
    }

    private bool IsLastId(string id) => string.Equals(id, _lastId, StringComparison.Ordinal);
}
