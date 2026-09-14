namespace InfiAir.Core.GameFeel;

/// <summary>命中权重档位：普通命中 / 暴击 / 击杀 / 重击（Boss 转场、狂暴等）。</summary>
public enum HitStopTier
{
    Normal = 0,
    Crit = 1,
    Kill = 2,
    Heavy = 3,
}
