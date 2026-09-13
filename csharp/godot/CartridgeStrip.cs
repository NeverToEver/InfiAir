using Godot;

namespace InfiAir;

/// <summary>
/// 弹仓视窗条（集成仪表盘的「离散弹药」读数）：等宽的独立药槽，已装填亮、空槽暗。
/// 弹匣是离散量（整数发），故用分立视窗而非连续条——量纲决定形态，连续条会读成「还剩多少比例」，
/// 而这里玩家需要的是「还能打几发」。次数变化才重绘，静止零开销。
/// Control 子类，_Draw 程序化绘制（文件名与类型同名）。
/// </summary>
public partial class CartridgeStrip : Control
{
    /// <summary>槽间距（px）：分隔出「独立视窗」的读感。</summary>
    private const float Gap = 3.0f;

    /// <summary>槽内缩（px）：让每格有独立框边。</summary>
    private const float Inset = 1.5f;

    private int _count = 10;
    private int _filled = 10;

    /// <summary>槽总数（母舰弹仓上限）。</summary>
    public void SetCapacity(int capacity)
    {
        var next = Mathf.Max(capacity, 1);
        if (next == _count)
        {
            return;
        }

        _count = next;
        QueueRedraw();
    }

    /// <summary>当前装填数。</summary>
    public void SetFilled(int filled)
    {
        var next = Mathf.Clamp(filled, 0, _count);
        if (next == _filled)
        {
            return;
        }

        _filled = next;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_count <= 0 || Size.X <= 0.0f || Size.Y <= 0.0f)
        {
            return;
        }

        var slotW = (Size.X - Gap * (_count - 1)) / _count;
        if (slotW <= 1.0f)
        {
            return;
        }

        for (var i = 0; i < _count; i++)
        {
            var x = i * (slotW + Gap);
            var rect = new Rect2(x, 0.0f, slotW, Size.Y);
            var loaded = i < _filled;
            // 已装填：琥珀实芯 + 顶缘受光；空槽：暗底 + 细框（保留「有这一格」的位置信息）
            DrawRect(new Rect2(rect.Position + new Vector2(Inset, Inset),
                rect.Size - new Vector2(Inset * 2.0f, Inset * 2.0f)),
                loaded ? UITheme.Accent : new Color(UITheme.SlotDark, 0.85f));
            if (loaded && rect.Size.Y > 4.0f)
            {
                DrawRect(new Rect2(rect.Position + new Vector2(Inset, Inset), new Vector2(rect.Size.X - Inset * 2.0f, 1.0f)),
                    new Color(UITheme.SheenWhite, 0.35f));
            }

            DrawRect(rect, new Color(UITheme.PanelBorder, 0.55f), false, 1.0f);
        }
    }
}
