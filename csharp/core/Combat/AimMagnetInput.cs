namespace InfiAir.Core.Combat;

/// <summary>磁吸输入窗口的输入路。两路「本帧位移」的来源不同，换算回窗口单位所需的帧长也不同
/// （同一条表达式不可能对两路同时正确，这是单位口径混用而非单一 bug）。</summary>
public enum AimInputPath
{
    /// <summary>鼠标：本帧位移是真实手部位移，**不随 Engine.TimeScale 缩放**。</summary>
    Mouse,

    /// <summary>手柄右摇杆：本帧位移由那个缩放后的帧长积分而来，乘回同一比例即与缩放无关。</summary>
    Stick,
}

/// <summary>
/// 辅助瞄准「磁吸输入窗口」的帧长归一（纯逻辑，零 Godot 依赖）。
///
/// 窗口（balance.json player.aim_assist.input.magnet_input_min / magnet_input_full）的口径是
/// **每 1/60 秒的位移**，而调用侧拿到的摇杆增量与鼠标物理增量都是「本帧」位移。帧率档可被玩家
/// 改（30/45/60/120/…/不限制），同一手速在不同档下进窗口的量就不同——fps30 下摇杆满推
/// ≈46.7 ≥ 窗口上界 40，磁吸直接失效；fps45 权重掉到 ≈0.13；鼠标路反向（高帧率下每帧位移更小，
/// 本应退出辅助的甩枪反而留在辅助内）。乘上本函数给出的比例把两路都换回「每 1/60s 位移」的
/// 统一口径：60fps（默认档）逐位不变，其余档对齐。
///
/// **时间缩放**：帧率归一之后还有一层单位口径——`Engine.TimeScale` 只缩放引擎 delta，
/// 不缩放玩家的手。摇杆位移是缩放 delta 积出来的（乘回同一系数即消掉），鼠标物理增量是真实
/// 手部位移（必须按真实帧长换算）。故换算按输入路取帧长，见 <see cref="FrameScale(AimInputPath, double, double)"/>。
/// </summary>
public static class AimMagnetInput
{
    /// <summary>窗口口径的参考帧长（秒）：1/60。窗口取值即以此为单位定义。</summary>
    public const double ReferenceFrame = 1.0 / 60.0;

    /// <summary>把「本帧位移」换算成「每 1/60s 位移」的比例。
    /// 帧长非有限或非正（暂停/异常帧）时返回 1.0——不做缩放，避免除零把输入放大成无穷。</summary>
    public static double FrameScale(double frameDelta)
    {
        if (!double.IsFinite(frameDelta) || frameDelta <= 0.0)
        {
            return 1.0;
        }

        return ReferenceFrame / frameDelta;
    }

    /// <summary>按输入路取帧长的窗口归一：鼠标路用真实帧长，摇杆路用积分它的那个缩放帧长。
    /// 鼠标路若吃缩放帧长，会在子弹时间/顿帧下把进窗口的量放大 1/TimeScale 倍——Boss 狂暴
    /// （TS=0.24）里窗口上界 40 实际等价真实手速 9.6px/帧、顿帧（TS=0.06）等价 2.4px/帧，
    /// 磁吸完全失效（MagnetPull 在 ilen ≥ full 时直接返回 ZERO）。</summary>
    public static double FrameScale(AimInputPath path, double frameDelta, double realFrameDelta)
        => path == AimInputPath.Mouse ? FrameScale(realFrameDelta) : FrameScale(frameDelta);
}
