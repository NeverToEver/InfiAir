using Godot;

namespace InfiAir;

/// <summary>
/// Buff 程序化字形图标库：19 种 buff 各一个几何字形（24 单位坐标系按尺寸缩放），
/// 供 HUD 图标格与 Buff 三选一卡片图标位共用（经 UITheme.MakeBuffSocket 统一槽位样式）。
/// 分类配色：进攻=ACCENT 青，维生=SUCCESS 绿，通用=ACCENT_GOLD 金。
/// M5 全量迁移（2026-08-08 自 scripts/ui_buff_icons.gd）：RefCounted + 静态工厂；
/// 字形内嵌 Glyph（Control 子类）经 MakeGlyph 实例化。
/// </summary>
public partial class BuffIcons : RefCounted
{
    private static readonly StringName[] Offense =
    {
        new("power_shot"), new("rapid_fire"), new("spread_shot"), new("piercing"), new("explosive"),
        new("laser_beam"), new("crit_shot"), new("bullet_speed"),
    };

    private static readonly StringName[] Sustain =
    {
        new("extra_life"), new("regen"), new("lifesteal"), new("armor"), new("evasion"), new("shield"),
    };

    // 其余归入通用：phase_dash / slow_field / efficient_boost / boost_recovery / mothership_recall

    /// <summary>分类色：进攻青 / 维生绿 / 通用金。</summary>
    public static Color ColorFor(StringName id)
    {
        foreach (var o in Offense)
        {
            if (o == id)
            {
                return UITheme.Accent;
            }
        }

        foreach (var s in Sustain)
        {
            if (s == id)
            {
                return UITheme.Success;
            }
        }

        return UITheme.AccentGold;
    }

    /// <summary>生成字形控件（px 见方，线宽随尺寸缩放）。</summary>
    public static Control MakeGlyph(StringName id, Color color, float px = 22.0f)
    {
        var glyph = new Glyph
        {
            GlyphId = id,
            GlyphColor = color,
            CustomMinimumSize = new Vector2(px, px),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        return glyph;
    }

    /// <summary>单个字形：_draw 内以 24 单位坐标系绘制，u = size/24 为缩放因子。</summary>
    public partial class Glyph : Control
    {
        public StringName GlyphId { get; set; } = new StringName();

        public Color GlyphColor { get; set; }

        public override void _Draw()
        {
            var u = Size.X / 24.0f;
            var c = GlyphColor;
            // 线宽下限 2px：小尺寸（HUD 瓦片 26px）下字形不糊，卡片大尺寸随 u 自然放大
            var w = Mathf.Max(2.0f * u, 2.0f);
            switch (GlyphId)
            {
                case "power_shot": // 大弹头上指 + 底托线
                    DrawColoredPolygon(Pts(u, new[] { 12f, 4f, 5f, 16f, 19f, 16f }), c);
                    Line(u, 7, 20, 17, 20, c, w);
                    break;
                case "rapid_fire": // 三道竖条（高射速）
                    foreach (var x in new[] { 6.0f, 12.0f, 18.0f })
                    {
                        Line(u, x, 5, x, 19, c, w);
                    }

                    break;
                case "spread_shot": // 同一点三发散射线
                    Line(u, 12, 20, 4, 4, c, w);
                    Line(u, 12, 20, 12, 3, c, w);
                    Line(u, 12, 20, 20, 4, c, w);
                    break;
                case "extra_life": // 菱形急救十字
                    DrawPolyline(Pts(u, new[] { 12f, 3f, 21f, 12f, 12f, 21f, 3f, 12f, 12f, 3f }), c, w, true);
                    Line(u, 12, 8, 12, 16, c, w);
                    Line(u, 8, 12, 16, 12, c, w);
                    break;
                case "regen": // 圆环 + 内十字（持续修复）
                    DrawArc(new Vector2(12, 12) * u, 9 * u, 0.0f, Mathf.Tau, 24, c, w, true);
                    Line(u, 12, 8, 12, 16, c, w);
                    Line(u, 8, 12, 16, 12, c, w);
                    break;
                case "piercing": // 箭穿隔板
                    Line(u, 3, 12, 20, 12, c, w);
                    Line(u, 20, 12, 15, 8, c, w);
                    Line(u, 20, 12, 15, 16, c, w);
                    Line(u, 10, 6, 10, 18, c, w);
                    break;
                case "explosive": // 八角星芒 + 中心点
                    for (var i = 0; i < 8; i++)
                    {
                        var a = Mathf.Tau * i / 8.0f;
                        var from = new Vector2(12, 12) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 4.0f;
                        var to = new Vector2(12, 12) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 9.0f;
                        DrawLine(from * u, to * u, c, w, true);
                    }

                    DrawCircle(new Vector2(12, 12) * u, 2.0f * u, c);
                    break;
                case "lifesteal": // 血滴：圆弧 + 顶部两侧收拢
                    DrawArc(new Vector2(12, 14) * u, 5 * u, 0.0f, Mathf.Tau, 20, c, w, true);
                    Line(u, 12, 3, 7.5f, 11.5f, c, w);
                    Line(u, 12, 3, 16.5f, 11.5f, c, w);
                    break;
                case "armor": // 盾形轮廓
                    DrawPolyline(Pts(u, new[] { 5f, 4f, 19f, 4f, 19f, 11f, 12f, 20f, 5f, 11f, 5f, 4f }), c, w, true);
                    break;
                case "evasion": // 双残影方框
                    DrawPolyline(Pts(u, new[] { 4f, 8f, 4f, 18f, 14f, 18f, 14f, 8f, 4f, 8f }), new Color(c, 0.55f), w, true);
                    DrawPolyline(Pts(u, new[] { 10f, 5f, 20f, 5f, 20f, 15f, 10f, 15f, 10f, 5f }), c, w, true);
                    break;
                case "phase_dash": // 双箭头快进
                    DrawPolyline(Pts(u, new[] { 4f, 4f, 11f, 12f, 4f, 20f }), c, w, true);
                    DrawPolyline(Pts(u, new[] { 11f, 4f, 18f, 12f, 11f, 20f }), c, w, true);
                    break;
                case "slow_field": // 沙漏（上下横杠 + 交叉斜线）
                    Line(u, 6, 4, 18, 4, c, w);
                    Line(u, 6, 20, 18, 20, c, w);
                    Line(u, 6, 4, 18, 20, c, w);
                    Line(u, 18, 4, 6, 20, c, w);
                    break;
                case "efficient_boost": // 上箭头 + 基线（高效推进）
                    DrawPolyline(Pts(u, new[] { 5f, 15f, 12f, 7f, 19f, 15f }), c, w, true);
                    Line(u, 7, 20, 17, 20, c, w);
                    break;
                case "boost_recovery": // 闪电（快速充能）
                    DrawColoredPolygon(Pts(u, new[] { 13f, 3f, 7f, 13f, 11f, 13f, 9f, 21f, 17f, 10f, 12.5f, 10f }), c);
                    break;
                case "mothership_recall": // 向下箭头落入托盘（召回）
                    Line(u, 12, 3, 12, 13, c, w);
                    DrawPolyline(Pts(u, new[] { 8f, 9f, 12f, 13f, 16f, 9f }), c, w, true);
                    DrawPolyline(Pts(u, new[] { 4f, 11f, 4f, 19f, 20f, 19f, 20f, 11f }), c, w, true);
                    break;
                case "laser_beam": // 发射镜 + 光束
                    DrawPolyline(Pts(u, new[] { 5f, 8.5f, 8.5f, 12f, 5f, 15.5f, 1.5f, 12f, 5f, 8.5f }), c, w, true);
                    DrawLine(new Vector2(10, 12) * u, new Vector2(22, 12) * u, c, 3.5f * u, true);
                    break;
                case "crit_shot": // 十字准星 + 中心点（命中要害，与 explosive 放射星芒区分）
                    DrawCircle(new Vector2(12, 12) * u, 2.0f * u, c);
                    Line(u, 12, 4, 12, 9, c, w);
                    Line(u, 12, 15, 12, 20, c, w);
                    Line(u, 4, 12, 9, 12, c, w);
                    Line(u, 15, 12, 20, 12, c, w);
                    break;
                case "shield": // 圆盾：外环 + 中央菱形脊（吸收伤害，与 armor 三角盾区分）
                    DrawArc(new Vector2(12, 12) * u, 8.0f * u, 0.0f, Mathf.Tau, 24, c, w, true);
                    Line(u, 12, 5, 8.5f, 12, c, w);
                    Line(u, 8.5f, 12, 12, 19, c, w);
                    Line(u, 12, 5, 15.5f, 12, c, w);
                    Line(u, 15.5f, 12, 12, 19, c, w);
                    break;
                case "bullet_speed": // 水平飞行弹头（右尖）+ 三条速度线
                    DrawColoredPolygon(Pts(u, new[] { 7f, 12f, 13f, 6f, 21f, 12f, 13f, 18f }), c);
                    Line(u, 3, 9, 6, 9, c, w);
                    Line(u, 1.5f, 12, 6, 12, c, w);
                    Line(u, 3, 15, 6, 15, c, w);
                    break;
                default: // 未登记字形回退：圆环
                    DrawArc(new Vector2(12, 12) * u, 8 * u, 0.0f, Mathf.Tau, 24, c, w, true);
                    break;
            }
        }

        /// <summary>单位坐标数组 → 像素坐标点列（[x0, y0, x1, y1, ...]）。</summary>
        private Vector2[] Pts(float u, float[] flat)
        {
            var pts = new Vector2[flat.Length / 2];
            for (var i = 0; i < flat.Length; i += 2)
            {
                pts[i / 2] = new Vector2(flat[i] * u, flat[i + 1] * u);
            }

            return pts;
        }

        private void Line(float u, float x0, float y0, float x1, float y1, Color c, float w)
            => DrawLine(new Vector2(x0, y0) * u, new Vector2(x1, y1) * u, c, w, true);
    }

    // ---------------- GDScript 鸭子调用兼容桥（M 批次过渡，M7 删除） ----------------



}
