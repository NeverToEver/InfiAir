using System;
using System.Collections.Generic;

namespace InfiAir.Core.Visual;

/// <summary>
/// 一类环境演出（标题屏远景战场的一种）：身份 id + 权重 + 冷却（秒）。
/// <see cref="Weight"/> ≤ 0 表示该类不参与抽取（留作开关，不必从表里删行）。
/// </summary>
public sealed record AmbientAct(string Id, double Weight, double Cooldown);

/// <summary>
/// 环境演出抽取器（纯逻辑，零 Godot 依赖）：加权随机 + 逐类冷却 + 尽力不连出同类。
///
/// 它替掉的是「每类演出各跑一条固定间隔计时器」那种写法——固定间隔读起来是节拍器，
/// 不是战场。改为一次抽一类之后，间隔与内容都随机，疏密由权重决定；而**冷却**保证
/// 稀有演出不被运气连发（Boss 剪影连出三次，背景就不再是背景了）。
///
/// 三条约束相撞时的优先序：**权重 0 &gt; 冷却 &gt; 不连出同类**。
/// 前两条是调用方的明确意图（开关与疏密），第三条只是观感偏好，故：
/// 权重 0 的类永不被抽中；正权重类全在冷却时取最早就绪的那一个（确定、不抛错、不死循环
/// ——标题屏的装饰层，演出断档比某一类出得略密更难堪）；就绪的正权重候选只有一个时
/// 允许连出同类（没有第二个可换）。
///
/// 时刻由调用方给（秒，单调递增，通常是累计的帧间隔之和）：本类不持有任何时钟，
/// 无头与窗口下同一条时间线，单测可任意喂时刻。
/// RNG 是可注入种子的独立 <see cref="Random"/>（同 <c>TaskPool</c> / <c>MachineBag</c> 口径）。
/// </summary>
public sealed class AmbientDirector
{
    private readonly AmbientAct[] _acts;
    private readonly double[] _readyAt; // 各类可再抽的时刻（上次抽中时刻 + 冷却）
    private readonly Random _random;
    private readonly double _minGap;
    private readonly double _maxGap;
    private string _lastId = string.Empty;

    /// <param name="acts">演出表（顺序即同权重时的优先序）。</param>
    /// <param name="minGap">两次抽取之间的最短间隔（秒，钳 ≥ 0）。</param>
    /// <param name="maxGap">最长间隔（秒；小于 minGap 时按 minGap 收口——表写反了不该让调用方等到负数）。</param>
    /// <param name="seed">随机种子（null 用时间种子；单测固定种子复现序列）。</param>
    public AmbientDirector(IEnumerable<AmbientAct> acts, double minGap, double maxGap, int? seed = null)
    {
        _acts = acts as AmbientAct[] ?? new List<AmbientAct>(acts).ToArray();
        _readyAt = new double[_acts.Length];
        _minGap = double.IsFinite(minGap) ? Math.Max(0.0, minGap) : 0.0;
        _maxGap = double.IsFinite(maxGap) ? Math.Max(_minGap, maxGap) : _minGap;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>演出表长度（探针 / 单测读口）。</summary>
    public int Count => _acts.Length;

    /// <summary>
    /// 抽下一刻演哪一类，并把该类记进冷却（自 <paramref name="now"/> 起算）。
    /// 空表返回空串（调用方据此跳过生成本次演出，不抛错）。
    /// </summary>
    public string Next(double now)
    {
        if (_acts.Length == 0)
        {
            return string.Empty;
        }

        var picked = PickReady(now);
        _readyAt[picked] = now + CooldownOf(picked);
        _lastId = _acts[picked].Id;
        return _lastId;
    }

    /// <summary>下一次抽取的间隔（秒），均匀落在 [minGap, maxGap]。</summary>
    public double Gap() => _minGap + _random.NextDouble() * (_maxGap - _minGap);

    /// <summary>
    /// 选一个类，优先级自上而下：
    /// 1. 正权重且已过冷却的类里加权抽（这类候选多于两个时排除上一次抽中的类 = 不连出同类）；
    /// 2. 正权重候选全空（都在冷却）→ 在正权重类里取最早就绪者（同为最早时取表序在前者）；
    /// 3. 整表权重都 ≤ 0 → 取最早就绪者。
    /// 零权重优先于「不连出同类」：权重 0 是调用方明确的「这一类先别出」（开关语义），
    /// 而连出同类只是观感偏好，两条冲突时前者说了算。
    /// </summary>
    private int PickReady(double now)
    {
        var anyReady = false;
        var multiReady = false;
        for (var i = 0; i < _acts.Length; i++)
        {
            if (!IsReady(i, now) || WeightOf(i) <= 0.0)
            {
                continue;
            }

            if (anyReady)
            {
                multiReady = true;
                break;
            }

            anyReady = true;
        }

        if (!anyReady)
        {
            return EarliestReady(now);
        }

        // 候选正好一个时不必考虑「不连出同类」（没有第二个可换）；多于一个才把上一次的排除掉
        var excludeLast = multiReady ? _lastId : string.Empty;
        return WeightedPick(now, excludeLast);
    }

    /// <summary>按权重在「正权重 / 已过冷却 / 非排除项」的候选里抽一个。
    /// 排除后候选全空（除上一次外只剩冷却中的类）时退回不排除——宁可连出同类也不能不出。</summary>
    private int WeightedPick(double now, string excludeLast)
    {
        var total = 0.0;
        for (var i = 0; i < _acts.Length; i++)
        {
            if (IsCandidate(i, now, excludeLast))
            {
                total += WeightOf(i);
            }
        }

        if (total <= 0.0)
        {
            return WeightedPick(now, string.Empty);
        }

        var roll = _random.NextDouble() * total;
        for (var i = 0; i < _acts.Length; i++)
        {
            if (!IsCandidate(i, now, excludeLast))
            {
                continue;
            }

            roll -= WeightOf(i);
            if (roll < 0.0)
            {
                return i;
            }
        }

        // 浮点累减的末位误差兜底：走到这里说明 roll 只差最后一类的零头，取最后一个候选即可。
        for (var i = _acts.Length - 1; i >= 0; i--)
        {
            if (IsCandidate(i, now, excludeLast))
            {
                return i;
            }
        }

        return EarliestReady(now);
    }

    private bool IsCandidate(int i, double now, string excludeLast) =>
        IsReady(i, now) && WeightOf(i) > 0.0 && !IsExcluded(i, excludeLast);

    /// <summary>最早就绪的那一类（正权重优先；整表权重都 ≤ 0 时才落到全表的读法上）。</summary>
    private int EarliestReady(double now)
    {
        var best = -1;
        for (var i = 0; i < _acts.Length; i++)
        {
            if (WeightOf(i) > 0.0 && (best < 0 || _readyAt[i] < _readyAt[best]))
            {
                best = i;
            }
        }

        if (best >= 0)
        {
            return best;
        }

        best = 0;
        for (var i = 1; i < _acts.Length; i++)
        {
            if (_readyAt[i] < _readyAt[best])
            {
                best = i;
            }
        }

        return best; // _acts 非空（Next 已早退空表），故这里必为合法下标
    }

    private bool IsReady(int i, double now) => now >= _readyAt[i];

    private bool IsExcluded(int i, string excludeId) =>
        excludeId.Length > 0 && string.Equals(_acts[i].Id, excludeId, StringComparison.Ordinal);

    private double WeightOf(int i)
    {
        var w = _acts[i].Weight;
        return double.IsFinite(w) && w > 0.0 ? w : 0.0;
    }

    private double CooldownOf(int i)
    {
        var c = _acts[i].Cooldown;
        return double.IsFinite(c) && c > 0.0 ? c : 0.0;
    }
}
