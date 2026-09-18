using Godot;
using InfiAir.Core.Hud;

namespace InfiAir;

/// <summary>
/// 准星绘制器（游戏内 AimCrosshair 与设置页实时预览共用的一份形状几何，两处不得各写一份）。
/// 形状基准几何（×size）按 DESIGN_BASELINE 定稿：bracket 外接半宽 14 + gap、单臂 6；
/// cross 线长 12、内端距心 gap、T 形去顶线；circle 半径 12；中心点半径 DotSize。
/// 旋转只作用于 bracket/cross（绘制层 DrawSetTransform，与锁定自转的节点旋转互不干扰）；
/// 描边＝同几何先行一遍宽 2px 的暖黑底stroke，提高亮底可读性。交战变色在此合成：
/// state_tint 关闭则全程用户色（锁定旋转/收拢等非颜色线索由调用方另行保留）。
/// </summary>
public static class CrosshairRender
{
    private const float BracketHalfBase = 14.0f;
    private const float BracketArmBase = 6.0f;
    private const float CrossLengthBase = 12.0f;
    private const float CircleRadiusBase = 12.0f;
    private static readonly Color OutlineColor = UITheme.ShadowBlack;

    /// <summary>交战变色合成后的准星色：用户色为基，可攻击→红粉、锁定→金热（主题定稿色）；
    /// state_tint 关闭则全程用户色。锁定框与形状走同一合成，两指示器永不异色打架。</summary>
    public static Color ResolveColor(CrosshairProfile p, float hostileBlend, float lockedBlend)
    {
        var baseColor = new Color(p.R / 255.0f, p.G / 255.0f, p.B / 255.0f, p.A / 255.0f * p.Alpha);
        if (!p.StateTint)
        {
            return baseColor;
        }

        return baseColor.Lerp(UITheme.AimOnTarget, hostileBlend).Lerp(UITheme.AimAmberHot, lockedBlend);
    }

    /// <summary>形状外接半宽（锁定框收拢的起点基准；形状族各自的口径）。</summary>
    public static float OuterHalf(CrosshairProfile p) => p.Shape switch
    {
        CrosshairShape.Bracket => BracketHalfBase * p.Size + p.Gap,
        CrosshairShape.Cross => p.Gap + CrossLengthBase * p.Size,
        CrosshairShape.Circle => CircleRadiusBase * p.Size,
        _ => 0.0f,
    };

    /// <summary>完整画一份准星（形状 + 中心点 + 交战变色合成）。origin＝准星心在绘制空间的
    /// 落点（游戏内是节点原点零向量，设置页预览用面板中心）。</summary>
    public static void Draw(
        CanvasItem ci, CrosshairProfile p, float hostileBlend, float lockedBlend, float pulse,
        Vector2 origin = default)
    {
        var line = ResolveColor(p, hostileBlend, lockedBlend) * new Color(1.0f, 1.0f, 1.0f, pulse);
        if (p.Shape != CrosshairShape.Dot)
        {
            if (p.Outline)
            {
                DrawSegments(ci, p, OutlineColor, p.Thickness + 2.0f, origin);
            }

            DrawSegments(ci, p, line, p.Thickness, origin);
        }

        // 中心点：dot 形状恒画（它就是本体），其余形状按开关
        if (p.Shape == CrosshairShape.Dot || p.CenterDot)
        {
            var radius = p.DotSize;
            if (p.Outline)
            {
                ci.DrawCircle(origin, radius + 1.0f, OutlineColor * new Color(1.0f, 1.0f, 1.0f, pulse));
            }

            ci.DrawCircle(origin, radius, line);
        }
    }

    private static void DrawSegments(CanvasItem ci, CrosshairProfile p, Color c, float width, Vector2 origin)
    {
        ci.DrawSetTransform(origin, Mathf.DegToRad(p.RotationDeg), Vector2.One);
        var size = p.Size;
        switch (p.Shape)
        {
            case CrosshairShape.Bracket:
            {
                // 四角 bracket：外接半宽含 gap 外推，臂长随 size（与旧版常量 14/6 同基准）
                var half = BracketHalfBase * size + p.Gap;
                var arm = BracketArmBase * size;
                foreach (var sx in SignValues)
                {
                    foreach (var sy in SignValues)
                    {
                        var corner = new Vector2(sx * half, sy * half);
                        ci.DrawLine(corner, corner - new Vector2(sx * arm, 0.0f), c, width, true);
                        ci.DrawLine(corner, corner - new Vector2(0.0f, sy * arm), c, width, true);
                    }
                }

                break;
            }

            case CrosshairShape.Cross:
            {
                // 四线十字：内端距心 gap、线长 12×size；T 形去顶线（CS2 cl_crosshair_t 语汇）
                var len = CrossLengthBase * size;
                var g = p.Gap;
                ci.DrawLine(new Vector2(g, 0.0f), new Vector2(g + len, 0.0f), c, width, true);
                ci.DrawLine(new Vector2(-g, 0.0f), new Vector2(-g - len, 0.0f), c, width, true);
                if (!p.TShape)
                {
                    ci.DrawLine(new Vector2(0.0f, -g), new Vector2(0.0f, -g - len), c, width, true);
                }

                ci.DrawLine(new Vector2(0.0f, g), new Vector2(0.0f, g + len), c, width, true);
                break;
            }

            case CrosshairShape.Circle:
                ci.DrawArc(Vector2.Zero, CircleRadiusBase * size, 0.0f, Mathf.Tau, 48, c, width, true);
                break;
        }

        ci.DrawSetTransform(origin, 0.0f, Vector2.One);
    }

    private static readonly float[] SignValues = { -1.0f, 1.0f };
}
