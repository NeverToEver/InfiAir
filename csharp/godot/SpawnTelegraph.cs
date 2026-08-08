using Godot;

namespace InfiAir;

/// <summary>
/// 敌机入场预告（M3b 全量迁移，2026-08-08 自 scripts/spawn_telegraph.gd 迁移）：
/// 可见区域顶部对应 x 位置的红色竖线 + 箭头，闪烁淡出后自毁。
/// 三角几何静态复用（P2：替代每帧构造 PackedVector2Array）。
/// </summary>
public partial class SpawnTelegraph : Node2D
{
    private const float DefaultDuration = 0.6f;
    private static readonly Vector2[] ArrowTriangle =
    {
        new(-8.0f, 70.0f), new(8.0f, 70.0f), new(0.0f, 86.0f),
    };

    /// <summary>GDScript 经脚本资源读取默认时长（C# 常量 GDScript 不可达——实测；
    /// spawner.gd 适配用，M6 后删除）。</summary>
    public static float GetDefaultDuration() => DefaultDuration;

    /// <summary>实例视觉寿命（spawner 注入 balance.json spawner.telegraph_duration）。</summary>
    public float Duration { get; set; } = DefaultDuration;

    private float _t;

    /// <summary>构造后由 spawner 设置 Position（原 GDScript _init(x, top) 参数经位置写入）。</summary>
    public SpawnTelegraph()
    {
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= Duration)
        {
            QueueFree();
        }
        else
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var alpha = 0.8f * (1.0f - _t / Duration) * (0.6f + 0.4f * Enemy.SinFast(_t * 30.0f));
        var color = new Color(1.0f, 0.2f, 0.2f, alpha);
        DrawRect(new Rect2(-2.0f, 0.0f, 4.0f, 70.0f), color);
        DrawColoredPolygon(ArrowTriangle, color);
    }
}
