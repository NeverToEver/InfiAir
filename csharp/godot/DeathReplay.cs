using Godot;
using System.Collections.Generic;

namespace InfiAir;

/// <summary>
/// B 梯队（fair plan §8）：死亡回放——环形缓冲录制最近 RECORD_SECONDS 秒的敌弹位置轨迹，
/// 玩家死亡后以幽灵弹幕重放（死因可见，最强公平感信号；只重放不结算，零碰撞）。
/// 录制在 main._process（存活期渲染帧采样；死亡后树暂停，main._process 冻结自然停止）；
/// 重放演出节点 process_mode=ALWAYS，暂停树中照常播放，播完自毁。
/// P0-1（2026-08-05 审计）：录制数据源从 main.get_children() 改为 GameState.enemy_bullets
/// 注册表（零 cast 遍历）；帧缓冲固定容量环形缓冲（索引取模写入，删除 pop_front O(n) 整表
/// 移位）；内层 [x,y] 改交错存储（槽复用 clear 保留容量，录制循环零分配）。
/// M5 全量迁移（2026-08-08 自 scripts/death_replay.gd）。
/// 原 GDScript 内嵌类 DeathReplayPlayer 迁移为独立顶层类 csharp/godot/DeathReplayPlayer.cs
/// （GDScript 不能经 C# 引用内嵌类；测试判型改经脚本资源，见该文件头注释）。
/// </summary>
public partial class DeathReplay : RefCounted
{
    private const float RecordSeconds = 3.0f;
    private const float RecordFps = 60.0f;
    private const int MaxFrames = (int)(RecordSeconds * RecordFps);

    /// <summary>重放演出帧间隔（重放时钟与录制 60fps 对齐，独立于引擎帧率）</summary>
    internal const float FrameDuration = 1.0f / RecordFps;

    /// <summary>幽灵弹池大小（敌弹场上峰值上限；超出部分不显示——回放只求死因可见）</summary>
    internal const int GhostCount = 200;

    /// <summary>环形缓冲：固定 MaxFrames 槽，每槽 List&lt;float&gt;（[x0,y0,x1,y1,...] 交错）；
    /// _writeIdx = 下一写槽，_frameCount = 已录制帧数（&lt; MaxFrames 时从头读，写满后最老帧在 _writeIdx）。
    /// 槽复用 Clear 保留容量（对齐 PackedFloat32Array.clear 语义），录制循环零分配。
    /// GodotSharp 无 PackedFloat32Array，用 List&lt;float&gt; 语义等价（append/size/clear+容量保留）。</summary>
    private List<float>[] _frames = System.Array.Empty<List<float>>();
    private int _writeIdx;
    private int _frameCount;
    private bool _recording;

    /// <summary>P0-1：敌弹注册表包装缓存（begin 时取一次；包装共享底层数组，内容实时可读，
    private Godot.Collections.Array _bulletRegistry = new();

    /// <summary>开始录制（main 新对局入口调用；幂等——重复调用清缓冲重录）</summary>
    public void Begin()
    {
        _recording = true;
        _frameCount = 0;
        _writeIdx = 0;
        if (_frames.Length != MaxFrames)
        {
            _frames = new List<float>[MaxFrames];
            for (int i = 0; i < MaxFrames; i++)
            {
                _frames[i] = new List<float>();
            }
        }

        _bulletRegistry = (Godot.Collections.Array)GameState.Instance.EnemyBullets;
    }

    /// <summary>停止录制（死亡/结算后调用；之后 record 零开销早退）</summary>
    public void Stop() => _recording = false;

    /// <summary>每渲染帧采样（main._process 存活期调用）：从敌弹注册表录制位置轨迹（环形覆盖最旧帧）。
    /// 帧槽 clear 复用（容量保留），录制循环内零分配。</summary>
    public void Record()
    {
        if (!_recording)
        {
            return;
        }

        var frame = _frames[_writeIdx];
        frame.Clear();
        foreach (var b in _bulletRegistry)
        {
            var bullet = b.AsGodotObject() as Bullet;
            if (bullet == null || !GodotObject.IsInstanceValid(bullet))
            {
                continue; // 注销延迟/销毁竞态的悬空引用防御
            }

            frame.Add(bullet.GlobalPosition.X);
            frame.Add(bullet.GlobalPosition.Y);
        }

        _writeIdx = (_writeIdx + 1) % MaxFrames;
        if (_frameCount < MaxFrames)
        {
            _frameCount += 1;
        }
    }

    /// <summary>生成重放演出节点（main 死亡流程调用；节点由调用方挂树，播完自毁）</summary>
    public Node2D Play()
    {
        _recording = false;
        var player = new DeathReplayPlayer();
        // 环形缓冲顺序化：未写满从头读，写满从最老帧（_writeIdx）起环绕读——引用传递零拷贝
        var ordered = new List<float>[_frameCount];
        var start = 0;
        if (_frameCount == MaxFrames)
        {
            start = _writeIdx;
        }

        for (int i = 0; i < _frameCount; i++)
        {
            ordered[i] = _frames[(start + i) % MaxFrames];
        }

        player.Setup(ordered);
        return player;
    }

    /// <summary>已录制帧数（测试观测）</summary>
    public int FrameCount() => _frameCount;

    // ---------------- GDScript 鸭子调用兼容桥（M5 过渡，M7 删除） ----------------
    // 调用方：main.gd（begin/record/play；main.gd:34 的 DeathReplay.new() 由主代理改 load().new()）。

    public void begin() => Begin();

    public void stop() => Stop();

    public void record() => Record();

    public Node2D play() => Play();

}
