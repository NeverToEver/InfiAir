using Godot;

namespace InfiAir;

/// <summary>
/// 蓄力辉光圆点（过场 _glow 配方：叠加态圆点 + scale/alpha tween）。
/// 原 BossAttacks 内部类（scripts/boss_attacks.gd，2026-08-08 全量迁移）；Godot 4 C#
/// 源生成器要求 Godot 类为顶层类型，故独立成文件（类名 = 文件名）。scale/alpha 由
/// BossAttacks.charge_glow 的 tween 驱动，本类只负责圆形绘制。
/// </summary>
public partial class GlowDot : Node2D
{
    /// <summary>辉光圆半径（px，charge_glow 按设计值 × world_scale 写入）。</summary>
    public float Radius { get; set; } = 8.0f;

    /// <summary>圆点颜色（charge_glow 传入；叠加态材质由调用方挂载）。</summary>
    public Color DotColor { get; set; } = Colors.White;

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, DotColor);
    }

}
