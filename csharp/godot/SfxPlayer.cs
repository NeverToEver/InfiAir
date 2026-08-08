using Godot;

namespace InfiAir;

/// <summary>
/// 常驻音效播放器池（M2 全量迁移，2026-08-08 自 scripts/sfx_player.gd 迁移）。
/// 作为 GameState 的子节点挂在树（播放节点被 queue_free 时音效也不会中断）。
/// 不持有具体音频资源，只管理播放实例池；行为与原实现逐字节等价。
/// </summary>
public partial class SfxPlayer : Node
{
    private readonly Godot.Collections.Array<AudioStreamPlayer> _sfxPlayers = new();
    private int _sfxIndex;

    /// <summary>构建播放器池（GameState._ready 在 add_child 本节点后调用）。</summary>
    public void BuildPool(int size)
    {
        for (var i = 0; i < size; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            _sfxPlayers.Add(p);
        }
    }

    public void Play(AudioStream stream, float volumeDb = 0.0f, float pitchScale = 1.0f)
    {
        // headless dummy 音频驱动不混音：一次性 WAV 播放实例在退出时既不自然结束、
        // stop() 也不释放，必报 ObjectDB 泄漏噪音；无头路径直接不创建播放实例。
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        if (_sfxPlayers.Count == 0)
        {
            return; // G028：build_pool 未调用（如测试直接 new()）时防 index 越界/除零
        }

        var p = _sfxPlayers[_sfxIndex];
        _sfxIndex = (_sfxIndex + 1) % _sfxPlayers.Count;
        p.Stream = stream;
        p.VolumeDb = volumeDb;
        p.PitchScale = pitchScale; // 池化复用：每次播放都显式置位，避免上次变调残留
        p.Play();
    }

    /// <summary>停止池内全部播放器（带播未停时 AudioStreamPlayback 会在退出时泄漏）。</summary>
    public void StopAll()
    {
        foreach (var p in _sfxPlayers)
        {
            p.Stop();
        }
    }
}
