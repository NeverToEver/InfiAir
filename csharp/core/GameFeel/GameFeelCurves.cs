namespace InfiAir.Core.GameFeel;

/// <summary>
/// 命中顿帧（hitstop / impact freeze）时序核心：按事件权重取冻结时长，逐帧以**真实时间**推进剩余量。
/// 纯 .NET、零 Godot 依赖，可独立单测。
///
/// 为什么按真实时间推进：顿帧的实现通常把全局时间缩放压到 0，此时引擎帧长同样为 0——
/// 用缩放后的 delta 推进剩余量会永不归零（冻死）。调用方须传入墙钟增量。
///
/// 取值为行业惯例档位（普通命中 40ms / 暴击 90ms / 击杀 160ms / 重击 200ms），
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

/// <summary>命中权重档位：普通命中 / 暴击 / 击杀 / 重击（Boss 转场、狂暴等）。</summary>
public enum HitStopTier
{
    Normal = 0,
    Crit = 1,
    Kill = 2,
    Heavy = 3,
}

/// <summary>
/// 屏幕震动 trauma 核心（Eiserloh《Juicing Your Cameras With Math》惯例）：
/// 冲击只累加「创伤值」，实际位移按 trauma^exponent 映射——小额事件几乎无感、
/// 大额事件才猛烈，天然抑制高频抖动疲劳。创伤随时间线性衰减。
/// 纯 .NET、零 Godot 依赖，可独立单测；噪声采样留在引擎适配层。
/// </summary>
public sealed class TraumaShake
{
    /// <summary>创伤上限（惯例恒 1.0）。</summary>
    public const double MaxTrauma = 1.0;

    /// <summary>位移映射指数：offset = maxOffset × trauma^exponent（惯例 2）。</summary>
    private readonly double _exponent;

    private double _trauma;

    public TraumaShake(double exponent = 2.0)
    {
        _exponent = double.IsFinite(exponent) && exponent > 0.0 ? exponent : 2.0;
    }

    /// <summary>当前创伤值（0..1）。</summary>
    public double Trauma => _trauma;

    /// <summary>是否仍在震动（含极小尾值阈值）。</summary>
    public bool Active => _trauma > 1e-4;

    /// <summary>累加冲击强度（饱和到上限）；非有限/非正值忽略。</summary>
    public void Add(double stress)
    {
        if (!double.IsFinite(stress) || stress <= 0.0)
        {
            return;
        }

        _trauma = Math.Min(_trauma + stress, MaxTrauma);
    }

    /// <summary>线性衰减（每秒 recoveryPerSecond）；非有限/非正速率视为不衰减（防静默清零）。</summary>
    public void Advance(double delta, double recoveryPerSecond)
    {
        if (_trauma <= 0.0 || !double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        if (!double.IsFinite(recoveryPerSecond) || recoveryPerSecond <= 0.0)
        {
            return;
        }

        _trauma -= recoveryPerSecond * delta;
        if (_trauma < 0.0)
        {
            _trauma = 0.0;
        }
    }

    /// <summary>位移映射量（trauma^exponent）；调用方乘各自的最大振幅。</summary>
    public double Magnitude() => Math.Pow(_trauma, _exponent);

    /// <summary>指定最大振幅下的实际位移量。</summary>
    public double Offset(double maxAmplitude) => maxAmplitude * Magnitude();

    /// <summary>清空（场景切换复位）。</summary>
    public void Clear() => _trauma = 0.0;
}
