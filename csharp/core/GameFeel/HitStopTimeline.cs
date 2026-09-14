namespace InfiAir.Core.GameFeel;

/// <summary>
/// 命中顿帧（hitstop / impact freeze）时序核心：按事件权重取冻结时长，逐帧以**真实时间**推进剩余量。
/// 纯 .NET、零 Godot 依赖，可独立单测。
///
/// 为什么按真实时间推进：顿帧的实现通常把全局时间缩放压到 0，此时引擎帧长同样为 0——
/// 用缩放后的 delta 推进剩余量会永不归零（冻死）。调用方须传入墙钟增量。
///
/// 档位取值由 balance.json 的 <c>effects.hit_stop</c> 提供（调用方按档读取后传入），
/// 随事件权重单调递增；同帧多次请求取较大者（不叠加），避免多目标命中把画面拖死。
/// </summary>
public sealed class HitStopTimeline
{
    private double _remaining;

    /// <summary>按档位取冻结时长（秒）；越界档位取 0（不冻结）。</summary>
    public static double DurationForTier(HitStopTier tier, double normal, double crit, double kill, double heavy)
    {
        var value = tier switch
        {
            HitStopTier.Normal => normal,
            HitStopTier.Crit => crit,
            HitStopTier.Kill => kill,
            HitStopTier.Heavy => heavy,
            _ => 0.0,
        };
        return double.IsFinite(value) && value > 0.0 ? value : 0.0;
    }

    /// <summary>当前是否处于顿帧（剩余 &gt; 0）。</summary>
    public bool Active => _remaining > 0.0;

    /// <summary>剩余冻结时长（秒）。</summary>
    public double Remaining => _remaining;

    /// <summary>请求一次顿帧：取 max（不叠加，防同帧多命中累加拖死画面）；非有限/非正值忽略。</summary>
    public void Request(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0.0)
        {
            return;
        }

        if (seconds > _remaining)
        {
            _remaining = seconds;
        }
    }

    /// <summary>按真实经过时间推进；归零即结束（不给负数）。</summary>
    public void Advance(double realDelta)
    {
        if (_remaining <= 0.0 || !double.IsFinite(realDelta) || realDelta <= 0.0)
        {
            return;
        }

        _remaining -= realDelta;
        if (_remaining < 0.0)
        {
            _remaining = 0.0;
        }
    }

    /// <summary>清空（场景切换/本局终态复位；残留会卡住下一局的时间缩放）。</summary>
    public void Clear() => _remaining = 0.0;
}
