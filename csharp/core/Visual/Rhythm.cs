namespace InfiAir.Core.Visual;

/// <summary>
/// 战斗动效的节奏算式（纯逻辑，零 Godot 依赖）：拍相位、拍点包络、呼吸频率与呼吸值、
/// 全屏振幅硬线。生产侧只做取值与适配（音乐播放位置 → 相位 → 包络 → 着色器参数），
/// 算式与护栏只有这一份。
///
/// 准入判据（`DESIGN_BASELINE` §2.12，前两条源自 XAG 118 的官方定义，是硬约束不是口味）：
///   - **全屏层不得构成「闪」**：XAG 118 把闪定义为 **≥10% 的亮度变化**，且失败判据里
///     面积（约 20% 屏）与频率（约 3 次/秒）并列。占屏 ≥20% 的元素因此只允许做峰谷差
///     低于 10% 的**慢呼吸**（低于定义线即不构成 flash，面积判据随之不适用）——
///     这就是 <see cref="FullScreenPeakToTroughCap"/> 的由来，全屏呼吸的取值一律经
///     <see cref="FullScreenHalfAmplitude"/> 收口，不在各处留本地系数。
///     拍点脉冲**一律下放局部元素**（弹体尾部 / 舰体流光 / 仪表 / 亮星，各自屏占远低于 20%），
///     故 <see cref="BeatPulse"/> 的振幅面不在全屏硬线内——生产侧的拍点振幅由各消费方自己的
///     幅值键承担（弹尾取 `effects.motion.bullet_tail_amp` 的拍点份额）再乘「动效强度」。
///   - **全屏周期脉冲频率 < 3Hz**：登记在 core <see cref="FlashBudget"/>（含减闪归零）。
///     <see cref="BeatHz"/> 是「BPM 越阈值」的唯一换算口，单测按 balance 的三档 BPM 判它——
///     改 BPM 让整拍越过 3Hz 时判红，而不是等画面开始闪。
///   - **相位基准是真实时间**（§2.10）：音乐播放位置属真实时间面，故本类的时间参数都是
///     「秒」而不是帧；无音乐（加载失败 / 无头 / 曲目缺失）时降级为模拟时间按默认 BPM 推进，
///     两条路径共用同一算式。
/// 全部入参非法时返回 0 而不是抛异常或 NaN：NaN 会顺着乘法污染整屏着色器输出（整块黑或白，
/// 引擎侧零报错），而「0」在语义上正好是「这一帧不动」。
/// </summary>
public static class Rhythm
{
    /// <summary>全屏层峰谷亮度差的上限（0.09）：XAG 118 把「闪」定义为 ≥10% 的亮度变化，
    /// 取 0.09 留一个百分点的安全余量——贴着 0.10 取会让实现与浮点误差一起越线。</summary>
    public const double FullScreenPeakToTroughCap = 0.09;

    /// <summary>全屏振幅钳制：请求值非有限或为负返回 0（无呼吸），超过硬线一半则钳回。
    /// 硬线约束的是**峰谷差**（最大值减最小值），而呼吸是绕中位上下摆动的，故上限取一半。</summary>
    public static double FullScreenHalfAmplitude(double requested)
        => double.IsFinite(requested) ? Math.Clamp(requested, 0.0, FullScreenPeakToTroughCap / 2.0) : 0.0;

    /// <summary>拍相位（0..1，拍内位置）。positionSeconds 是真实时间轴上的音乐播放位置，
    /// offsetSeconds 把相位基准整体平移（前奏空拍 / 动画对拍）。
    /// 负位置按同一算式取小数部分（-0.1s 在 120BPM 下落 0.8 拍），故相位恒在 [0,1)、不会出现负值；
    /// bpm ≤ 0 或任一入参非有限（含中间量溢出成 ∞）时返回 0——「停在拍首」是确定值，
    /// 而抛异常会把无音乐 / 播放位置抖动这类常态输入变成崩溃。</summary>
    public static double BeatPhase(double positionSeconds, double bpm, double offsetSeconds = 0.0)
    {
        if (!double.IsFinite(positionSeconds) || !double.IsFinite(bpm) || !double.IsFinite(offsetSeconds) || bpm <= 0.0)
        {
            return 0.0;
        }

        var beats = (positionSeconds - offsetSeconds) * (bpm / 60.0);
        if (!double.IsFinite(beats))
        {
            return 0.0;
        }

        return beats - Math.Floor(beats);
    }

    /// <summary>整拍频率（Hz）＝ BPM/60：全屏节拍频率判据（<see cref="FlashBudget.HzLimit"/>）的
    /// 唯一换算口，也是「先算频率再判阈值」的落地点（换成拍周期去比较是另一套口径，别处不许再算一遍）。
    /// bpm ≤ 0 或非有限返回 0。</summary>
    public static double BeatHz(double bpm) => double.IsFinite(bpm) && bpm > 0.0 ? bpm / 60.0 : 0.0;

    /// <summary>拍点包络（0..1）：phase 为拍内相位（<see cref="BeatPhase"/> 的返回值），
    /// attackFraction / releaseFraction 是攻段与放段占一拍的比例（快攻慢放：攻段短、放段长）。
    /// 拍首起振、攻段末到峰、放段衰减到 0，窗口（两段之和）之外一律 0——拍点的「静止几乎不动、
    /// 变化时才动」就靠这条窗口：常驻非零会把节拍读成整体亮度漂移。
    /// 两段用平滑曲线（smoothstep）而非线性：线性折点在逐帧观感里是可见的拐角，且窗口两端有跳变。
    /// 非法参数（两段之和 ≤ 0、任一段为负、任一入参非有限）返回 0——「窗口为零」的语义是
    /// 关掉拍点，不是把拍点变成常亮 1（后者会让动效强度 0 与减少闪光双双失效）。
    /// 单侧为零仍合法：零攻段＝拍首即峰，零放段＝到达峰值即归零。</summary>
    public static double BeatPulse(double phase, double attackFraction, double releaseFraction)
    {
        if (!double.IsFinite(phase) || !double.IsFinite(attackFraction) || !double.IsFinite(releaseFraction)
            || attackFraction < 0.0 || releaseFraction < 0.0)
        {
            return 0.0;
        }

        var span = attackFraction + releaseFraction;
        if (span <= 0.0 || !double.IsFinite(span))
        {
            return 0.0;
        }

        // 拍内相位域是 [0,1)：负相位与 ≥1 的相位都属窗口之外（每一拍按自己的相位取包络）
        if (phase < 0.0 || phase >= 1.0 || phase >= span)
        {
            return 0.0;
        }

        if (attackFraction <= 0.0)
        {
            return 1.0 - Smoothstep(phase / releaseFraction);
        }

        if (phase < attackFraction)
        {
            return Smoothstep(phase / attackFraction);
        }

        return releaseFraction <= 0.0 ? 0.0 : 1.0 - Smoothstep((phase - attackFraction) / releaseFraction);
    }

    /// <summary>呼吸频率（Hz）：danger01 是危险度（钳到 0..1），在 hzMin/hzMax 之间线性映射——
    /// 越危险呼吸越快，静止 / 满血时慢到几乎不动。上下限写反时返回的仍是两者之间的值（内部排序），
    /// 频率也不可能为负（全负区间钳到 0）；任一入参非有限返回 0。</summary>
    public static double BreathHz(double danger01, double hzMin, double hzMax)
    {
        if (!double.IsFinite(danger01) || !double.IsFinite(hzMin) || !double.IsFinite(hzMax))
        {
            return 0.0;
        }

        var low = Math.Max(0.0, Math.Min(hzMin, hzMax));
        var high = Math.Max(0.0, Math.Max(hzMin, hzMax));
        return low + (high - low) * Math.Clamp(danger01, 0.0, 1.0);
    }

    /// <summary>呼吸值（0..1，t=0 落谷、半周期落峰）：消费方按它乘振幅得到绕中位的偏移。
    /// 相位按周期数取小数部分后再进 cos——长时间播放（曲目已播几百秒）下直接算 2π·hz·t
    /// 会因大数精度损失让呼吸出现台阶。
    /// hz ≤ 0 或非有限入参返回 0（无呼吸＝零偏移；返回 0.5 会被读成「偏移一半」）。</summary>
    public static double BreathValue(double timeSeconds, double hz)
    {
        if (!double.IsFinite(timeSeconds) || !double.IsFinite(hz) || hz <= 0.0)
        {
            return 0.0;
        }

        var cycles = hz * timeSeconds;
        if (!double.IsFinite(cycles))
        {
            return 0.0;
        }

        return 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * (cycles - Math.Floor(cycles)));
    }

    /// <summary>平滑插值（3t²-2t³）：两端导数为 0，故接进包络的攻段/放段在峰与窗口边界都连续。
    /// 调用点已保证 t 落在 [0,1]。</summary>
    private static double Smoothstep(double t) => t * t * (3.0 - 2.0 * t);
}
