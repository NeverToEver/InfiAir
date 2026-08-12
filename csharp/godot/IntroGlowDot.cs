using Godot;

namespace InfiAir;


/// <summary>软圆点（原 GDScript intro_cinematic.gd 内嵌类 _GlowDot，迁移为同文件顶层类；
/// C# 源生成器不支持内嵌类，BaseConsoleScanlines 同款处理）。</summary>
public partial class IntroGlowDot : Node2D
{
    public float Radius = 8.0f;

    public Color DotColor = Colors.White;

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, DotColor);
    }
}
