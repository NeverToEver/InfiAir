namespace InfiAir.Core.GameFeel;

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
