using Godot;
using System.Collections.Generic;

namespace InfiAir;

/// <summary>
/// 死亡回放演出：暂停树中以 process_mode=ALWAYS 重放录制的敌弹轨迹——
/// 幽灵红点按快照逐帧出现/移动（与录制时弹生成/销毁一致），播完自毁。纯演出无碰撞。
/// M5 全量迁移（2026-08-08 自 scripts/death_replay.gd 内嵌类 DeathReplayPlayer）。
/// 原 GDScript 内嵌类不能经 C# 引用（autoplay_test/buff33_test 以类名判型），
/// 迁移为独立顶层类/独立文件——判型改经脚本资源 load("res://csharp/godot/DeathReplayPlayer.cs")
/// （由主代理统一改写测试）。
/// </summary>
public partial class DeathReplayPlayer : Node2D
{
    private List<float>[] _frames = System.Array.Empty<List<float>>();
    private int _frameIdx;
    private float _accum;
    private readonly List<Polygon2D> _ghosts = new();

    public void Setup(List<float>[] frames)
    {
        _frames = frames;
        ProcessMode = Node.ProcessModeEnum.Always;
        ZIndex = 30; // 场景实体之上、HUD 之下（结算面板为 CanvasLayer 不受 z 影响）
        for (int i = 0; i < DeathReplay.GhostCount; i++)
        {
            var g = new Polygon2D();
            var pts = new Vector2[10];
            for (int k = 0; k < 10; k++)
            {
                var a = Mathf.Tau * k / 10.0f;
                pts[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6.0f;
            }

            g.Polygon = pts;
            g.Color = new Color(1.0f, 0.25f, 0.25f, 0.7f);
            g.Visible = false;
            AddChild(g);
            _ghosts.Add(g);
        }
    }

    public override void _Process(double delta)
    {
        _accum += (float)delta;
        while (_accum >= DeathReplay.FrameDuration && _frameIdx < _frames.Length)
        {
            _accum -= DeathReplay.FrameDuration;
            ApplyFrame(_frames[_frameIdx]);
            _frameIdx += 1;
        }

        if (_frameIdx >= _frames.Length)
        {
            QueueFree(); // 播完自毁
        }
    }

    private void ApplyFrame(List<float> frame)
    {
        for (int i = 0; i < _ghosts.Count; i++)
        {
            var j = i * 2;
            if (j + 1 < frame.Count)
            {
                _ghosts[i].GlobalPosition = new Vector2(frame[j], frame[j + 1]);
                _ghosts[i].Visible = true;
            }
            else
            {
                _ghosts[i].Visible = false;
            }
        }
    }
}
