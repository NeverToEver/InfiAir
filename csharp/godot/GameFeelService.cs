using Godot;
using InfiAir.Core.GameFeel;

namespace InfiAir;

/// <summary>
/// 手感服务：命中顿帧（hitstop）与屏幕震动 trauma 的状态持有者与时间缩放合成器。
/// GameState 组合持有并做门面转发（与 ScoreService 等组合服务同构）。
///
/// **时间缩放的唯一写入者之一**：`Engine.TimeScale` 有两路来源——Boss 狂暴子弹时间
/// （Main 编排演出）与命中顿帧（本服务）。两处各自直写会互相覆盖（顿帧把子弹时间顶回 1.0、
/// 或子弹时间把顿帧顶掉），故合成收口在本服务：Main 只上报自己的演出倍率（SetEnrageTimeScale），
/// 实际写入 = 演出倍率 × 顿帧倍率。
///
/// 顿帧倍率取**严格正值**（effects.hit_stop.scale，默认 0.05）：引擎把 delta 按时间缩放后
/// 再传给 _Process，倍率为 0 时 delta 恒 0、剩余时长永远推不完（冻死）；且帧长被压到 0 后
/// 无法还原未缩放时长。取正值后可反解真实帧长（delta / 当前总倍率），固定步长下精确可重复。
///
/// 判定下沉：时序与 trauma 数学在 csharp/core/GameFeel/（`HitStopTimeline`/`TraumaShake` 等，零引擎依赖，有单测）；
/// 本类只做配置注入、合成与广播。
/// </summary>
public sealed partial class GameFeelService : RefCounted
{
    /// <summary>顿帧时序（剩余时长按真实帧长推进）。</summary>
    private readonly HitStopTimeline _hitStop = new();

    /// <summary>屏幕震动创伤值（位移 = 最大振幅 × trauma^2）。</summary>
    private readonly TraumaShake _shake = new();

    /// <summary>顿帧各档时长（秒；balance.json effects.hit_stop.*）。</summary>
    private double _normal = 0.03;

    private double _crit = 0.07;
    private double _kill = 0.11;
    private double _heavy = 0.16;

    /// <summary>顿帧期间的全局时间倍率（严格 &gt;0，见类注释；配置键 effects.hit_stop.freeze_scale）。</summary>
    private double _freezeScale = 0.06;

    /// <summary>顿帧强度（设置项 0..1）：乘到各档时长上，0 = 完全关闭。
    /// 用「缩时长」而非「改冻结倍率」表达强度——倍率逼近 0 会让帧长趋零、采样与输入一起失真，
    /// 越短时长则只减少冻结帧数，手感退化是单调的。</summary>
    private double _intensity = 1.0;

    /// <summary>Boss 狂暴子弹时间的演出倍率（Main 上报，默认 1.0 = 无演出）。</summary>
    private double _enrageScale = 1.0;

    /// <summary>上一帧实际写入的时间倍率（用于反解真实帧长）。</summary>
    private double _appliedScale = 1.0;

    /// <summary>trauma 每秒线性衰减速率（effects.shake.recovery）。</summary>
    private double _shakeRecovery = 1.5;

    /// <summary>trauma 归一化参考振幅（effects.shake.reference）：震源振幅 ÷ 该值 = 单次冲击增量。</summary>
    private double _shakeReference = 24.0;

    /// <summary>配置注入（ApplyBalance 调用；调用方已完成判型）。</summary>
    public void ApplyConfig(
        double normal, double crit, double kill, double heavy, double freezeScale, double shakeRecovery, double shakeReference)
    {
        _normal = normal;
        _crit = crit;
        _kill = kill;
        _heavy = heavy;
        // 倍率钳入 (0, 1]：≥1 使顿帧无视觉作用，≤0 使 delta 归零冻死（见类注释）
        _freezeScale = Mathf.Clamp((float)freezeScale, 0.01f, 1.0f);
        _shakeRecovery = shakeRecovery > 0.0 ? shakeRecovery : 1.5;
        _shakeReference = shakeReference > 1e-6 ? shakeReference : 24.0;
    }

    /// <summary>顿帧强度（设置项 0..1）：0 = 完全关闭；>0 时按比例缩短各档时长。</summary>
    public void SetHitStopScale(double scale)
    {
        _intensity = double.IsFinite(scale) && scale > 0.0 ? Math.Min(scale, 1.0) : 0.0;
    }

    /// <summary>请求一次命中顿帧（档位取时长；同帧多请求取较大者，不叠加）。</summary>
    public void RequestHitStop(HitStopTier tier)
    {
        if (_intensity <= 0.0)
        {
            return;
        }

        var baseDuration = HitStopTimeline.DurationForTier(tier, _normal, _crit, _kill, _heavy);
        _hitStop.Request(baseDuration * _intensity);
    }

    /// <summary>屏幕震动唯一累加入口：震源振幅 ÷ 参考振幅 = trauma 增量。
    /// **无障碍倍率不在此折算**——折算单口在 `GameState.Shake`（否则两处各乘一次，
    /// trauma ∝ scale²、再经 trauma² 映射使位移 ∝ scale⁴，滑杆中段严重非线性）。</summary>
    public void AddShake(double strength) => _shake.Add(strength / _shakeReference);

    /// <summary>当前 trauma（CameraShake 每帧读取换算位移）。</summary>
    public double ShakeTrauma() => _shake.Trauma;

    /// <summary>trauma 指定最大振幅下的位移量（CameraShake 用）。</summary>
    public double ShakeOffset(double maxAmplitude) => _shake.Offset(maxAmplitude);

    /// <summary>Boss 狂暴演出倍率上报（Main 调用；1.0 = 无演出）。</summary>
    public void SetEnrageTimeScale(double scale)
    {
        _enrageScale = scale > 0.0 && double.IsFinite(scale) ? scale : 1.0;
        ApplyTimeScale();
    }

    /// <summary>清顿帧残留（保持演出倍率与 trauma 不变）：暂停/模态入口调用——
    /// 冻结是「按真实时间流逝」的战斗现象，暂停中继续压低全局时间缩放会让 Always 的
    /// 暂停菜单/设置页慢放（实测 0.5s 的轮盘滑入变约 8s）。</summary>
    public void ClearHitStop()
    {
        _hitStop.Clear();
        ApplyTimeScale();
    }

    /// <summary>本帧期望的时间倍率（诊断/探针读口）。</summary>
    public double CurrentTimeScale() => _enrageScale * (_hitStop.Active ? _freezeScale : 1.0);

    /// <summary>顿帧是否进行中（诊断/探针读口）。</summary>
    public bool HitStopActive() => _hitStop.Active;

    /// <summary>逐帧推进（GameState._Process 调用）：按**未缩放**的真实帧长推进顿帧与 trauma 衰减，
    /// 随后合成时间倍率。realDelta 由调用方从缩放后 delta 反解（见类注释）。</summary>
    public void Tick(double realDelta)
    {
        _hitStop.Advance(realDelta);
        _shake.Advance(realDelta, _shakeRecovery);
        ApplyTimeScale();
    }

    /// <summary>复位（本局终态/场景切换）：清残留顿帧与 trauma 并把时间倍率交还演出侧。
    /// 残留不清会让下一局开局定格或相机持续偏移。</summary>
    public void ResetAll()
    {
        _hitStop.Clear();
        _shake.Clear();
        _enrageScale = 1.0;
        ApplyTimeScale();
    }

    /// <summary>时间倍率合成写入口（唯一写入 Engine.TimeScale 的顿帧路径）。</summary>
    private void ApplyTimeScale()
    {
        var scale = CurrentTimeScale();
        if (Mathf.IsEqualApprox((float)scale, (float)_appliedScale))
        {
            return;
        }

        _appliedScale = scale;
        Engine.TimeScale = (float)scale;
    }

    /// <summary>反解上一帧的真实（未缩放）帧长：引擎传入的 delta 已按时间缩放折算，
    /// 除以本服务上一帧写入的倍率即还原。倍率被外部直写（如场上无本服务路径的旧代码）时
    /// 仍按钳后的正值除法，不会除零。</summary>
    public double RealDelta(double scaledDelta)
    {
        var denom = _appliedScale > 1e-6 ? _appliedScale : 1.0;
        return scaledDelta / denom;
    }
}
