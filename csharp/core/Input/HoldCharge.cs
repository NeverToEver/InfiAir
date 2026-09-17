namespace InfiAir.Core.Input;

/// <summary>长按蓄力的本帧相位（调用方按相位接线：推进/亮条/触发/收条）。</summary>
public enum HoldChargePhase
{
    /// <summary>未按住，且上一帧也没按住（无进度可收）。</summary>
    Idle,

    /// <summary>蓄力中（按住但未达阈值，或已达阈值待复位）。</summary>
    Charging,

    /// <summary>本帧恰达阈值——同一次按住只报一次，避免按住期间逐帧重复触发。</summary>
    Triggered,

    /// <summary>本帧由按住转松手（进度已复位）：调用方据此收 HUD 进度条、恢复提示文案。</summary>
    Released,
}

/// <summary>
/// 长按蓄力状态机（纯逻辑，零 Godot 依赖）：按住累加 → 达阈值触发一次 → 松手复位。
///
/// 背景：这条状态机原先在六处各写一遍（主路径母舰召唤/返航/放弃出击、驻留母舰的提前离舰、
/// 教程的召唤与返航两段），每处的「达阈值判据、松手清零、进度条归一化」各写各的——
/// 阈值比较写成 `&gt;` 还是 `&gt;=`、松手是否同时清 HUD 条、进度是否钳 1，都要逐处对照才看得出差异，
/// 而差异只在长按边界上显形（不到阈值不触发 / 到了不松手重复触发 / 进度条常驻）。
/// 故把状态收敛到本类一份，调用方只保留「门控条件 + 触发动作 + 表现」。
///
/// 阈值由引擎侧从 balance 注入并自行钳下限（core 不产生数值）：阈值非正时首帧即触发，
/// 阈值非有限时永不触发——两种退化都由调用方的钳制负责挡住（与既有比较语义一致）。
/// </summary>
public sealed class HoldCharge
{
    public HoldCharge(float threshold) => Threshold = threshold;

    /// <summary>达成阈值所需的按住时长（秒）。由引擎侧装载配置后写入（core 不产生数值）。</summary>
    public float Threshold { get; set; }

    /// <summary>本次按住已累计的时长（秒）；达阈值后随按住继续增长。</summary>
    public float Elapsed { get; private set; }

    /// <summary>当前是否处于按住态（含已达阈值待复位）。</summary>
    public bool Holding { get; private set; }

    /// <summary>本次按住是否已触发过（触发锁；松手或 <see cref="Reset"/> 解除）。</summary>
    public bool Triggered { get; private set; }

    /// <summary>进度 0..1（HUD 进度条用；超过阈值后恒为 1）。</summary>
    public float Progress
    {
        get
        {
            if (!(Threshold > 0.0f))
            {
                return 1.0f;
            }

            var t = Elapsed / Threshold;
            return t < 0.0f ? 0.0f : (t > 1.0f ? 1.0f : t);
        }
    }

    /// <summary>推进一帧。<paramref name="held"/> 为调用方合成后的「本帧应蓄力」条件
    /// （按键按住 **且** 该通道的门控通过）——门控失效与松手同义，都会复位进度。</summary>
    public HoldChargePhase Tick(float dt, bool held)
    {
        if (!held)
        {
            if (!Holding)
            {
                return HoldChargePhase.Idle;
            }

            Reset();
            return HoldChargePhase.Released;
        }

        Holding = true;
        if (Triggered)
        {
            return HoldChargePhase.Charging;
        }

        Elapsed += dt;
        if (Elapsed < Threshold)
        {
            return HoldChargePhase.Charging;
        }

        Triggered = true;
        return HoldChargePhase.Triggered;
    }

    /// <summary>复位进度与触发锁（松手、门控失效、终局清理都用它）。</summary>
    public void Reset()
    {
        Elapsed = 0.0f;
        Holding = false;
        Triggered = false;
    }

    /// <summary>白盒写入累计时长（实机调参观察面：把进度摆到指定位置试触发；生产路径不写）。</summary>
    public void SetElapsed(float seconds) => Elapsed = seconds;
}
