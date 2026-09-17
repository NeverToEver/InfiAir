namespace InfiAir.Core.Audio;

/// <summary>常驻曲目档位（切换单位：一档一首）。</summary>
public enum MusicCue
{
    /// <summary>默认战斗曲（本局里除 Boss 战与基地休整以外的全部时段）。</summary>
    Default = 0,

    /// <summary>Boss 战曲（Boss 在场期间）。</summary>
    Boss = 1,

    /// <summary>基地休整曲（基地控制台驻留期间）。</summary>
    Base = 2,
}

/// <summary>
/// 曲目选择判定（零 Godot 依赖，可单测）：给定「Boss 是否在场 / 是否在基地休整」→ 该播哪首。
///
/// 存在理由：这条映射落在 Godot 侧就只是散在各切换点的一段三元——「返航保留 Boss 时基地优先还是
/// Boss 优先」的取舍没有回归面，写反了表现为「带 Boss 回基地后基地曲目整段不播」（不崩不报错，
/// 冒烟与截图都判不到，只有听感不同）。判定放这里，Godot 侧只做「按名取资源 + 播 / 停 / 淡入淡出」。
/// </summary>
public static class MusicSelection
{
    /// <summary>默认战斗曲资源名（不含目录与扩展名；路径拼接属 Godot 适配层）。</summary>
    private const string DefaultTrack = "bgm_loop";

    /// <summary>Boss 战曲资源名。</summary>
    private const string BossTrack = "bgm_boss";

    /// <summary>基地休整曲资源名。</summary>
    private const string BaseTrack = "bgm_base";

    /// <summary>上下文 → 档位。基地休整优先于 Boss：返航刻意保留 Boss（本局继续），若 Boss 优先，
    /// 带 Boss 回基地时基地曲目整段不播，且此后只要该 Boss 不死就再也不播（休整时段被 Boss 曲占满）。
    /// 两者皆否 = 缺省时段，回默认曲目。</summary>
    public static MusicCue Select(bool bossEngaged, bool atBase)
    {
        if (atBase)
        {
            return MusicCue.Base;
        }

        return bossEngaged ? MusicCue.Boss : MusicCue.Default;
    }

    /// <summary>档位 → 资源名。越界档位（枚举被强转 / 跨版本读入）回落默认曲目：宁可播默认曲目，
    /// 也不静默停播——静默无声与「播错曲」在运行期都无从分辨，只能在这里收口。</summary>
    public static string TrackName(MusicCue cue) => cue switch
    {
        MusicCue.Boss => BossTrack,
        MusicCue.Base => BaseTrack,
        _ => DefaultTrack,
    };
}
