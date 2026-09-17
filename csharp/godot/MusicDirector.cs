using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using InfiAir.Core.Audio;

namespace InfiAir;

/// <summary>
/// 音乐编排：三首常驻曲目（默认战斗 / Boss 战 / 基地休整）之间的平滑切换。
///
/// 职责边界：**该播哪首**由 core <see cref="MusicSelection"/> 判定（本类只把「Boss 在场 / 在基地
/// 休整」两个上下文位传进去，不自己推优先级）；本类只做「按名取资源 → 播 / 停 → 交叉淡化」，以及
/// 与既有播放契约的对接：常驻主播放器挂在 BGM 总线（设置页音乐音量分路经该总线生效）、
/// 音量口径沿用既有取值（战斗 -18dB、基地 -30dB），返航过场仍按既有做法直接写主播放器的
/// <c>volume_db</c> 做镜头 7 淡出。
///
/// 淡化为什么用两枚播放器：单播放器只能「先淡出到静音 → 换流 → 再淡入」，中间必然留一段静音；
/// 退场播放器接住旧曲目（从主播放器当前位置续播，40s 循环里重头播是听得出来的断口），
/// 两侧音量同时反向推进即无静音断口。退场播放器只此一枚、按需创建，不随切换次数增长。
/// </summary>
public partial class MusicDirector : Node
{
    /// <summary>曲目目录（资源名主干由 core 给，本类只拼路径）。</summary>
    private const string AudioDir = "res://assets/audio/";

    /// <summary>战斗曲目电平（既有取值：BGM 总线之上的相对音量）。</summary>
    private const float BattleVolumeDb = -18.0f;

    /// <summary>基地休整曲目电平（沿用返航收尾既有取值，休整时段听感不变）。</summary>
    private const float BaseVolumeDb = -30.0f;

    /// <summary>交叉淡化时长（s）。</summary>
    private const float CrossfadeTime = 0.8f;

    /// <summary>淡出侧终点：远低于可闻阈值且不是 -inf（LinearToDb 那类无穷值不能写进属性）。</summary>
    private const float SilenceDb = -60.0f;

    private readonly Dictionary<MusicCue, AudioStreamWav> _tracks = new();
    private AudioStreamPlayer? _current;
    private AudioStreamPlayer? _outgoing;
    private Tween? _crossfade;

    /// <summary>最近一次请求的档位（上下文位组合而来）与正在播的档位：两者分开记——
    /// 加载未就绪时 SetContext 只更新请求，就绪后再补齐，不会出现「请求已记下、曲子始终没换」。</summary>
    private MusicCue _requested = MusicCue.Default;

    private MusicCue _playing = MusicCue.Default;

    /// <summary>常驻主播放器（未就绪 / 资源加载失败时为 null）：返航过场据此淡出，本类不代它改音量。</summary>
    public AudioStreamPlayer? Primary => _current;

    /// <summary>当前档位（请求档位一旦受理即更新；资源缺失或尚未装载时停在旧档）。
    /// 判据读它而不是 <see cref="Primary"/> 的流：后者只能证明播放器在放音，证明不了
    /// 「该播哪首」的上下文接线对——基线里播错曲（该切没切）只有听感不同，任何门禁都判不到。</summary>
    public MusicCue Playing => _playing;

    public override void _Ready()
    {
        _ = StartAsync();
    }

    /// <summary>上下文位 → 档位，仅在同档位时才早退。</summary>
    public void SetContext(bool bossEngaged, bool atBase)
    {
        _requested = MusicSelection.Select(bossEngaged, atBase);
        ApplyRequested();
    }

    /// <summary>BGM 延后到首帧之后启动：三首 WAV 逐首解码不占首帧关键路径（既有口径不变）。
    /// 三首一次装齐——切换点全是本局的紧要时刻（Boss 入场 / 返航收尾），那里再解码会卡在
    /// 最不该卡的一帧上。</summary>
    private async Task StartAsync()
    {
        try
        {
            // await 段异常统一 try/catch（约定 §Async）——恢复期节点释放/引擎错误不静默吞
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // await 后守卫——首帧前本节点被释放（场景早退/摘树路径）则不再操作 freed 实例
            if (!IsInsideTree())
            {
                return;
            }

            foreach (MusicCue cue in new[] { MusicCue.Default, MusicCue.Boss, MusicCue.Base })
            {
                var name = MusicSelection.TrackName(cue);
                // CACHE_MODE_REUSE 复用资源缓存——CACHE_MODE_IGNORE 会每次进本局重新 load + 解码
                //（静态音频，缓存复用无副作用；音频路径保持等价不变）
                var stream = ResourceLoader.Load(AudioDir + name + ".wav", "AudioStreamWAV", ResourceLoader.CacheMode.Reuse)
                    as AudioStreamWav;
                // 运行时 load 判空——打包漏资源/磁盘异常时降级静默而非空引用崩溃
                if (stream == null)
                {
                    GD.PushWarning($"音乐资源加载失败：{AudioDir}{name}.wav");
                    continue;
                }

                // 只设 loop_mode 即可整段循环；显式写 loop_begin/loop_end 会在退出时泄漏播放实例
                stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                _tracks[cue] = stream;
            }

            if (!_tracks.TryGetValue(MusicCue.Default, out var startup))
            {
                return; // 默认曲目缺失：不建播放器（与既有「加载失败即静默降级」同口径）
            }

            SfxPlayer.EnsureBuses(); // 与 SFX 总线解耦：音乐走独立总线，混音互不牵连
            _current = new AudioStreamPlayer
            {
                Stream = startup,
                VolumeDb = VolumeFor(MusicCue.Default),
                Bus = SfxPlayer.BgmBus,
            };
            AddChild(_current);
            _current.Play();
            _playing = MusicCue.Default;
            ApplyRequested(); // 装载期间到达的上下文请求在此补齐（时序上不该发生，但代价是一行）
        }
        catch (System.Exception ex)
        {
            GD.PushWarning("MusicDirector.StartAsync 异常：" + ex.Message);
        }
    }

    private void ApplyRequested()
    {
        if (_current == null || _requested == _playing)
        {
            return;
        }

        // 请求档位的曲目缺失（单首加载失败）：留在当前曲目继续播——停播是更糟的降级，
        // 且「该时段没音乐」与「切错曲」在运行期同样无从分辨
        if (!_tracks.TryGetValue(_requested, out var stream))
        {
            return;
        }

        var incoming = VolumeFor(_requested);
        var second = OutgoingPlayer();
        var previous = _current.Stream;
        var previousDb = _current.VolumeDb;
        var previousPos = _current.GetPlaybackPosition();

        // 旧曲目交给退场播放器续播（同一位置），新曲目从主播放器头部起——主播放器始终是
        // 「当前曲目」，返航过场 / 设置页音量对它的既有契约不变
        second.Stream = previous;
        second.VolumeDb = previousDb;
        second.Play(previousPos);

        _current.Stream = stream;
        _current.VolumeDb = SilenceDb;
        _current.Play();
        _playing = _requested;

        if (_crossfade != null && _crossfade.IsValid())
        {
            _crossfade.Kill(); // 前一次切换未收尾（连切）：先杀旧 tween，防同一属性双写
        }

        _crossfade = CreateTween();
        // 基地驻留期整树是暂停态（基地 UI 本就是暂停态 UI）：tween 必须按 Process 推进，
        // 否则回基地后先无声等待，直到继续出击才把淡化补完（返航收尾的既有做法同理）
        _crossfade.SetPauseMode(Tween.TweenPauseMode.Process);
        _crossfade.SetParallel(true);
        _crossfade.TweenProperty(_current, "volume_db", incoming, CrossfadeTime);
        _crossfade.TweenProperty(second, "volume_db", SilenceDb, CrossfadeTime);
        _crossfade.Chain().TweenCallback(Callable.From(StopOutgoing));
    }

    /// <summary>退场播放器（按需创建、常驻复用，不随切换次数增长）。</summary>
    private AudioStreamPlayer OutgoingPlayer()
    {
        if (_outgoing != null && GodotObject.IsInstanceValid(_outgoing))
        {
            return _outgoing;
        }

        _outgoing = new AudioStreamPlayer { Bus = SfxPlayer.BgmBus };
        AddChild(_outgoing);
        return _outgoing;
    }

    /// <summary>退场侧淡化收尾：停止播放（流留着不摘——三首曲目都是缓存资源，下一轮切换直接覆盖）。</summary>
    private void StopOutgoing()
    {
        if (_outgoing == null || !GodotObject.IsInstanceValid(_outgoing))
        {
            return;
        }

        _outgoing.Stop();
    }

    /// <summary>档位 → 电平（基地休整沿用既有 -30dB，其余为战斗电平）。</summary>
    private static float VolumeFor(MusicCue cue) => cue == MusicCue.Base ? BaseVolumeDb : BattleVolumeDb;
}
