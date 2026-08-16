using Godot;

namespace InfiAir;

/// <summary>
/// 开始页装饰背景（StartBackdrop）：全屏静态星空 + 两条全息扫描装饰线。
/// 与对局 Starfield 无关的纯界面元素（种子固定、不滚动），配合全遮光罩把
/// 开始页与实际游玩画面完全隔开，避免「暂停后继续玩」的错觉。
/// M5 全量迁移（2026-08-08 自 scripts/start_backdrop.gd）。
/// </summary>
public partial class StartBackdrop : Control
{
    private static readonly Color _color = new(0.0f, 0.83f, 1.0f); // 原 const COLOR：全息青

    /// <summary>U20（2026-08-05）：每次 _Draw 新建 RNG 属无谓分配；改字段复用，但 _Draw 首行重置 seed——
    /// 保持原「每次重绘同序列」的确定性（新建+固定 seed 语义），行为逐位一致。</summary>
    private static readonly RandomNumberGenerator _rng = new();

    /// <summary>是否已订阅视口尺寸变化（仅 CanvasLayer 直挂路径需要手动铺满并跟随视口）。</summary>
    private bool _viewportSizeConnected;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
        if (GetParent() is Control)
        {
            SetAnchorsPreset(Control.LayoutPreset.FullRect);
            return;
        }

        // Welcome 将本节点直接挂在 CanvasLayer 下（parent 非 Control）。该布局下
        // 锚点没有父 Control 矩形可参照，入树后才调用 SetAnchorsPreset 不会触发
        // resize，控件保持 0×0 —— 开始页星空与全息线整层缺失。这里以视口可见矩形
        // 兜底铺满，并跟随视口尺寸变化重排（普通 Control 父节点仍走锚点布局）。
        _viewportSizeConnected = true;
        FitToViewport();
        GetViewport().SizeChanged += OnViewportSizeChanged;
    }

    public override void _ExitTree()
    {
        if (!_viewportSizeConnected)
        {
            return;
        }

        var viewport = GetViewport();
        if (viewport != null)
        {
            viewport.SizeChanged -= OnViewportSizeChanged;
        }

        _viewportSizeConnected = false;
    }

    private void OnViewportSizeChanged() => FitToViewport();

    private void FitToViewport()
    {
        var rect = GetViewport().GetVisibleRect();
        Position = rect.Position;
        Size = rect.Size;
        QueueRedraw();
    }

    public override void _Draw()
    {
        _rng.Seed = 20260731;  // 每次重绘重置：内容确定性一致
        var rect = GetRect();
        // 三层静态星点：暗底噪 / 中亮 / 少量亮星带十字微光
        for (var i = 0; i < 160; i++)
        {
            var p = new Vector2(_rng.Randf() * rect.Size.X, _rng.Randf() * rect.Size.Y);
            DrawCircle(p, _rng.RandfRange(0.6f, 1.4f), new Color(1.0f, 1.0f, 1.0f, _rng.RandfRange(0.05f, 0.16f)));
        }

        for (var i = 0; i < 60; i++)
        {
            var p = new Vector2(_rng.Randf() * rect.Size.X, _rng.Randf() * rect.Size.Y);
            DrawCircle(p, _rng.RandfRange(1.0f, 1.8f), new Color(0.75f, 0.92f, 1.0f, _rng.RandfRange(0.15f, 0.3f)));
        }

        for (var i = 0; i < 8; i++)
        {
            var p = new Vector2(_rng.Randf() * rect.Size.X, _rng.Randf() * rect.Size.Y);
            var a = _rng.RandfRange(0.3f, 0.5f);
            DrawCircle(p, 2.0f, new Color(0.85f, 0.95f, 1.0f, a));
            DrawLine(p + new Vector2(-5.0f, 0.0f), p + new Vector2(5.0f, 0.0f), new Color(_color, a * 0.6f), 1.0f, true);
            DrawLine(p + new Vector2(0.0f, -5.0f), p + new Vector2(0.0f, 5.0f), new Color(_color, a * 0.6f), 1.0f, true);
        }

        // 上下两条全息细线（标题页框感）
        DrawLine(new Vector2(0.0f, 96.0f), new Vector2(rect.Size.X, 96.0f), new Color(_color, 0.10f), 1.0f, true);
        DrawLine(new Vector2(0.0f, rect.Size.Y - 80.0f), new Vector2(rect.Size.X, rect.Size.Y - 80.0f), new Color(_color, 0.08f), 1.0f, true);
    }
}
