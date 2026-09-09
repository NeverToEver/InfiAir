using Godot;

namespace InfiAir;

/// <summary>
/// 常驻音效库：目录表（下标 = (int)SfxId）集中定义每个音效的资源/基准音量/音高抖动/
/// 最小触发间隔/复音数，替代散在各调用点的裸 AudioStream 与魔法 dB。
/// 每个 id 独立声部池轮转：复音满时重触发本 id 最旧声部（旧全局 6 槽轮转会偷走别的
/// 音效播到一半的声音），同类音效叠峰从此有上界；冷却在声源处限频同帧/连帧重复触发。
/// 播放节点统一走 SFX 总线（挂限幅器兜底），BGM 走 BGM 总线（EnsureBuses 幂等建总线）。
/// </summary>
public partial class SfxPlayer : Node
{
    public const string SfxBus = "SFX";
    public const string BgmBus = "BGM";

    private static readonly string[] Paths =
    {
        "res://assets/audio/bullet_fire.wav",
        "res://assets/audio/bullet_fire_b.wav",
        "res://assets/audio/bullet_fire_c.wav",
        "res://assets/audio/explosion.wav",
        "res://assets/audio/explosion_big.wav",
        "res://assets/audio/player_hit.wav",
        "res://assets/audio/buff_pick.wav",
        "res://assets/audio/dash.wav",
        "res://assets/audio/resupply.wav",
        "res://assets/audio/heartbeat.wav",
    };

    // 基准音量：旧实现多数调用点裸 0dB 直打 Master，同类叠峰即削波炸耳；统一下调为校准起点
    private static readonly float[] BaseDb = { -10f, -10f, -11f, -8f, -7f, -7f, -7f, -8f, -6f, -10f };

    // 音高抖动半宽：多实例同采样同相叠加（梳状滤波）是刺耳主源之一；显式传 pitchScale 的
    // 调用（过场/Boss tell）是精确调音，不抖
    private static readonly float[] PitchJitter = { 0.05f, 0.05f, 0.04f, 0.08f, 0.05f, 0.03f, 0.04f, 0.04f, 0f, 0f };

    // 最小触发间隔 ms：波次同帧多杀/爆炸弹链/受击连击在声源处限频，不再逐事件全放
    private static readonly uint[] CooldownMs = { 25, 30, 40, 40, 60, 100, 60, 80, 250, 150 };

    // 复音数：同类音效同时发声上限
    private static readonly int[] VoiceCounts = { 2, 2, 2, 3, 2, 1, 2, 2, 1, 1 };

    private AudioStreamPlayer[][] _voices = [];
    private int[] _voiceNext = [];
    private ulong[] _lastTriggerMs = [];
    private AudioStream[] _streams = [];

    /// <summary>headless 判定缓存一次：dummy 音频驱动不混音，播放实例退出时既不自然结束也
    /// 无法 stop 释放，必报 ObjectDB 泄漏噪音；无头路径不建实例、不播。</summary>
    private static readonly bool IsHeadless = DisplayServer.GetName() == "headless";

    /// <summary>建 SFX/BGM 总线（幂等）：SFX 总线限幅器是混音叠峰的安全网。BuildPool 与
    /// Main 的 BGM 装配前各调一次；autoload 就绪先于场景，正常时序下总线已存在。</summary>
    public static void EnsureBuses()
    {
        if (AudioServer.GetBusIndex(SfxBus) < 0)
        {
            var idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, SfxBus);
            AudioServer.SetBusSend(idx, "Master");
            // 硬限幅（4.6 起替代 AudioEffectLimiter）：仅削 -1dB 以上叠峰的透明安全网，常态不压动态
            AudioServer.AddBusEffect(idx, new AudioEffectHardLimiter { CeilingDb = -1.0f });
        }

        if (AudioServer.GetBusIndex(BgmBus) < 0)
        {
            var idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, BgmBus);
            AudioServer.SetBusSend(idx, "Master");
        }
    }

    /// <summary>按目录表装载音效并为每个 id 建独立声部池（GameState._Ready 在 add_child 后调用）。</summary>
    public void BuildPool()
    {
        if (IsHeadless)
        {
            return;
        }

        EnsureBuses();
        var count = Paths.Length;
        _voices = new AudioStreamPlayer[count][];
        _voiceNext = new int[count];
        _lastTriggerMs = new ulong[count];
        _streams = new AudioStream[count];
        for (var i = 0; i < count; i++)
        {
            _streams[i] = GD.Load<AudioStream>(Paths[i]);
            var players = new AudioStreamPlayer[VoiceCounts[i]];
            for (var v = 0; v < players.Length; v++)
            {
                players[v] = new AudioStreamPlayer { Bus = SfxBus };
                AddChild(players[v]);
            }

            _voices[i] = players;
        }
    }

    /// <summary>volumeDb 缺省 = 目录基准；pitchScale 显式传入 = 精确音高（不抖），缺省按目录抖动。</summary>
    public void Play(SfxId id, double? volumeDb = null, double? pitchScale = null)
    {
        if (IsHeadless || _voices.Length == 0)
        {
            return; // G028：BuildPool 未调用（直接 new() 而未入树准备的路径）时防空引用
        }

        var i = (int)id;
        var now = Time.GetTicksMsec();
        if (now - _lastTriggerMs[i] < CooldownMs[i])
        {
            return;
        }

        _lastTriggerMs[i] = now;
        var players = _voices[i];
        var p = players[_voiceNext[i]];
        _voiceNext[i] = (_voiceNext[i] + 1) % players.Length;
        p.Stream = _streams[i];
        p.VolumeDb = (float)(volumeDb ?? BaseDb[i]);
        var jitter = PitchJitter[i];
        p.PitchScale = pitchScale is double pitch
            ? (float)pitch
            : 1.0f + (jitter > 0f ? (float)GD.RandRange(-jitter, jitter) : 0f);
        p.Play();
    }

    /// <summary>停止池内全部播放器（带播未停时 AudioStreamPlayback 会在退出时泄漏）。</summary>
    public void StopAll()
    {
        foreach (var players in _voices)
        {
            foreach (var p in players)
            {
                p.Stop();
            }
        }
    }
}
