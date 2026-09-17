using Godot;
using InfiAir.Core.Talent;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 树状扇形平铺区（天赋面板右区）：当前大类下的支线以扇形辐射（根在底部中心，向上 135° 展开），
/// 节点沿支线由内向外排布（低级靠根、高级靠外）。节点卡片为真实 Control（ChamferedPanel）——
/// 悬停/焦点/点击进 GUI 相位；父子连线由本控件 _Draw 绘于卡片之下（已解锁亮色发光、未解锁暗色、
/// 上限被封路径红调）。布局几何来自 Core TalentFanLayout（纯函数，可独立单测）。
/// 状态样式（第三章节点视觉规范）：未解锁灰暗 / 已解锁亮色 / 可升级亮边 / 顶满金边 MAX /
/// 风险加点暗金锁 / 上限被封（互斥/路线）红调。
/// </summary>
public partial class TalentFanView : Control
{
    /// <summary>节点卡被点击/确认（Enter）。</summary>
    public event Action<StringName>? NodeActivated;

    /// <summary>悬停节点变化（null = 离开；联动左侧轮盘高亮）。</summary>
    public event Action<string?>? NodeHovered;

    private const float CardW = (float)TalentFanGeometry.CardSize;
    private const float CardH = (float)TalentFanGeometry.CardSize;
    private const float HoverScale = 1.07f;   // 悬停/焦点放大峰值
    private const float HoverTime = 0.12f;

    /// <summary>布局域 = 本控件实际尺寸（见 <see cref="OnResized"/>），初值取装配口径常量。
    /// 不可自持一份宽度——曾自持 1240 而控件装配宽 900，右端卡越界压进详情卡。</summary>
    private TalentFanLayout _layout = BuildLayout(TalentFanGeometry.ViewportWidth, TalentFanGeometry.ViewportHeight);
    private readonly Dictionary<string, ChamferedPanel> _cards = new();
    private readonly Dictionary<string, Label> _levelLabels = new();
    private readonly Dictionary<string, Label> _badgeLabels = new();
    private readonly Dictionary<string, Label> _valueLabels = new();
    private readonly Dictionary<string, int> _lastLevels = new(); // 加点前层级，供升级闪烁比对
    /// <summary>节点 id 的 StringName 每节点缓存一次（建卡时构造；_Process/_Draw/StyleCard 复用，
    /// 免逐帧 new StringName 的原生封送）。</summary>
    private readonly Dictionary<string, StringName> _cardIds = new();
    /// <summary>「存在可升级节点」聚合态缓存：建卡与 RefreshStates 时重算，_Process 只读它
    /// 决定呼吸脉冲，不再逐帧遍历卡片连查天赋字典。</summary>
    private bool _anyUpgradeable;
    /// <summary>本控件累计模拟时间（秒）：呼吸脉冲相位基准（替代墙钟，帧率/机器性能无关）。</summary>
    private float _simTime;
    private string? _categoryId;
    private string? _hovered;
    private string? _selected;
    private readonly FontFile _font = UITheme.Font;
    private bool _pulsePhase;

    public TalentFanView()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
        Resized += OnResized;
    }

    /// <summary>按视口尺寸重建布局域（尺寸变化即重排；布局域始终跟控件实际尺寸走）。</summary>
    private static TalentFanLayout BuildLayout(double width, double height)
        => new() { Width = width, Height = height };

    /// <summary>尺寸变化（含首次布局）：布局域跟进并重排卡位，再重绘连线与根芯片。</summary>
    private void OnResized()
    {
        var size = Size;
        if (size.X > 0.0f && size.Y > 0.0f
            && (Math.Abs(size.X - _layout.Width) > 0.5 || Math.Abs(size.Y - _layout.Height) > 0.5))
        {
            _layout = BuildLayout(size.X, size.Y);
            LayoutCards();
        }

        QueueRedraw();
    }

    /// <summary>切换展示大类（null = 调用方切概览模式并自行隐藏本控件）。</summary>
    public void SetCategory(string? categoryId)
    {
        _categoryId = categoryId;
        _hovered = null;
        _selected = null;
        RebuildCards();
        QueueRedraw();
    }

    public void SetSelected(string? nodeId)
    {
        _selected = nodeId;
        QueueRedraw();
    }

    public string? Selected => _selected;

    /// <summary>状态刷新（加点/路线/代币变化后）：重刷卡片样式与文案，不重建控件。
    /// 层级上涨的卡片追加一次闪烁，让「加点」有落点而非静默变样。</summary>
    public void RefreshStates()
    {
        var talent = GameState.Instance.Talent;
        List<string>? leveled = null;
        foreach (var kv in _cards)
        {
            var idSn = _cardIds[kv.Key];
            var level = talent.Level(idSn);
            if (_lastLevels.TryGetValue(kv.Key, out var prev) && level > prev)
            {
                (leveled ??= new List<string>()).Add(kv.Key);
            }

            _lastLevels[kv.Key] = level;
            StyleCard(kv.Key, kv.Value, idSn);
        }

        RefreshUpgradeableFlag();
        QueueRedraw();
        if (leveled == null)
        {
            return;
        }

        foreach (var nodeId in leveled)
        {
            FlashCard(nodeId);
        }
    }

    /// <summary>「存在可升级节点」聚合态一次性重算（建卡 / 加点 / 路线 / 代币变化时）：
    /// 判定式与 _Process 旧逐帧版本逐项一致；_Process 只读缓存，免逐帧 StringName 构造与字典连查。</summary>
    private void RefreshUpgradeableFlag()
    {
        var talent = GameState.Instance.Talent;
        var any = false;
        foreach (var kv in _cardIds)
        {
            var idSn = kv.Value;
            if (talent.Level(idSn) < talent.CapFor(idSn) && talent.PrerequisiteMet(idSn)
                && !talent.IsOvercharged(idSn) && talent.EffectiveCache >= talent.NextCost(idSn))
            {
                any = true;
                break;
            }
        }

        _anyUpgradeable = any;
    }

    private void RebuildCards()
    {
        foreach (var child in GetChildren())
        {
            child.Free(); // 立即释放：防同帧新旧卡片并存闪帧（Hud.RebuildAugmentDock 同款）
        }

        _cards.Clear();
        _levelLabels.Clear();
        _badgeLabels.Clear();
        _valueLabels.Clear();
        _lastLevels.Clear();
        _cardIds.Clear();
        _anyUpgradeable = false;
        if (_categoryId == null)
        {
            return;
        }

        var cat = TalentTree.Category(_categoryId);
        var talent = GameState.Instance.Talent;
        foreach (var line in cat.Lines)
        {
            foreach (var nodeId in line.NodeIds)
            {
                _cardIds[nodeId] = new StringName(nodeId);
                var card = MakeCard(nodeId);
                _cards[nodeId] = card;
                _lastLevels[nodeId] = talent.Level(_cardIds[nodeId]);
                AddChild(card);
            }
        }

        LayoutCards();
        RefreshUpgradeableFlag();
        // 键盘链路：焦点落首卡（方向键走 Godot 邻居焦点，Enter 经 GuiInput ui_accept）
        var first = _cards.Values.FirstOrDefault();
        first?.GrabFocus();
    }

    /// <summary>卡位 = 布局中心（钳进可用区）− 半个卡边：钳制保证整张卡落在扇形视口内
    /// （右端卡不越进详情栏、左端卡不探出视口左缘），扇形径向展开与节点排序不变。</summary>
    private void LayoutCards()
    {
        if (_categoryId == null)
        {
            return;
        }

        var cat = TalentTree.Category(_categoryId);
        var positions = _layout.Compute(cat.Lines.Select(l => (IReadOnlyList<string>)l.NodeIds).ToList());
        var size = Size;
        var width = size.X > 0.0f ? size.X : _layout.Width;
        var height = size.Y > 0.0f ? size.Y : _layout.Height;
        foreach (var pos in positions)
        {
            if (_cards.TryGetValue(pos.NodeId, out var card))
            {
                var (x, y) = TalentFanGeometry.ClampCardCenter(pos.X, pos.Y, width, height, CardW);
                card.Position = new Vector2((float)x - CardW / 2f, (float)y - CardH / 2f);
            }
        }
    }

    private ChamferedPanel MakeCard(string nodeId)
    {
        var idSn = _cardIds[nodeId];
        var card = new ChamferedPanel
        {
            CustomMinimumSize = new Vector2(CardW, CardH),
            Size = new Vector2(CardW, CardH),
            Brackets = false,
            FocusMode = Control.FocusModeEnum.All,
            PivotOffset = new Vector2(CardW / 2f, CardH / 2f),
        };

        // 定宽包裹 + FullRect：socket（自增自适应 ChamferedPanel）被容器持续撑宽时会棘轮抬升自身
        // 最小尺寸，反馈到卡片自适应再撑宽卡片；固定 44×44 包裹使棘轮稳定在目标尺寸
        var socketWrap = new Control
        {
            CustomMinimumSize = new Vector2(44.0f, 44.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        socketWrap.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        socketWrap.OffsetLeft = (CardW - 44f) / 2f;
        socketWrap.OffsetRight = -(CardW - 44f) / 2f;
        socketWrap.OffsetTop = 8.0f;
        socketWrap.OffsetBottom = 52.0f;
        var socket = UITheme.MakeAugmentSocket(idSn, 44.0f);
        socket.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        socketWrap.AddChild(socket);
        card.AddChild(socketWrap);

        var levelLabel = UITheme.MakeLabel("", UITheme.FontSmall, UITheme.Text, HorizontalAlignment.Center);
        levelLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        levelLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        levelLabel.OffsetTop = 62.0f;
        levelLabel.OffsetBottom = 84.0f;
        card.AddChild(levelLabel);
        _levelLabels[nodeId] = levelLabel;

        var nameLabel = UITheme.MakeLabel(Tr($"AUG_{nodeId.ToUpperInvariant()}_NAME"), 13, UITheme.TextDim, HorizontalAlignment.Center);
        nameLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        nameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        nameLabel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        nameLabel.OffsetTop = -24.0f;
        nameLabel.OffsetBottom = -6.0f;
        card.AddChild(nameLabel);

        // 右上角状态角标（MAX/锁/⚡）与右下角增幅数值（条件显示）
        var badge = UITheme.MakeLabel("", 13, UITheme.AccentGold, HorizontalAlignment.Right);
        badge.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        badge.OffsetLeft = -46.0f;
        badge.OffsetTop = 2.0f;
        badge.OffsetRight = -4.0f;
        card.AddChild(badge);
        _badgeLabels[nodeId] = badge;

        var value = UITheme.MakeLabel("", 13, UITheme.Accent, HorizontalAlignment.Right);
        value.MouseFilter = Control.MouseFilterEnum.Ignore;
        value.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        value.OffsetLeft = -50.0f;
        value.OffsetTop = -44.0f;
        value.OffsetRight = -6.0f;
        value.OffsetBottom = -26.0f;
        card.AddChild(value);
        _valueLabels[nodeId] = value;

        // 样式刷新依赖上方 label 注册表（层数/徽标/数值），必须在注册完成后执行
        StyleCard(nodeId, card, idSn);

        card.GuiInput += ev => OnCardGuiInput(ev, nodeId);
        card.MouseEntered += () => SetHover(nodeId);
        card.MouseExited += () => SetHover(_hovered == nodeId ? null : _hovered);
        card.FocusEntered += () => SetHover(nodeId);
        card.FocusExited += () => SetHover(_hovered == nodeId ? null : _hovered);
        return card;
    }

    private void OnCardGuiInput(InputEvent @event, string nodeId)
    {
        var activated = @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };
        // 键盘/手柄统一确认（对齐 BuffSelect 卡片：ui_accept 不限 InputEventKey，防手柄 A 被排除）
        if (@event.IsAction("ui_accept") && !(@event is InputEventKey { Echo: true })
            && @event is InputEventKey { Pressed: true } or InputEventJoypadButton { Pressed: true } or InputEventAction { Pressed: true })
        {
            activated = true;
        }

        if (activated)
        {
            _selected = nodeId;
            QueueRedraw();
            NodeActivated?.Invoke(nodeId);
            GetViewport().SetInputAsHandled();
        }
    }

    private void SetHover(string? nodeId)
    {
        if (_hovered == nodeId)
        {
            return;
        }

        var prev = _hovered;
        _hovered = nodeId;
        if (prev != null)
        {
            HoverMotion(prev, false);
        }

        if (nodeId != null)
        {
            HoverMotion(nodeId, true);
        }

        NodeHovered?.Invoke(nodeId);
        QueueRedraw();
    }

    /// <summary>悬停/焦点缩放（互斥 tween，快速进出不叠写）；pivot 已在建卡时置中心。</summary>
    private void HoverMotion(string nodeId, bool on)
    {
        if (!_cards.TryGetValue(nodeId, out var card) || !GodotObject.IsInstanceValid(card))
        {
            return;
        }

        if (card.HasMeta("hover_tween"))
        {
            var old = card.GetMeta("hover_tween").AsGodotObject() as Tween;
            if (old != null && old.IsValid())
            {
                old.Kill();
            }
        }

        // 命中目标选中卡已因闪烁保持放大时不再缩回原尺寸
        var target = on || _selected == nodeId ? HoverScale : 1.0f;
        var tw = card.CreateTween();
        card.SetMeta("hover_tween", Variant.From(tw));
        tw.TweenProperty(card, "scale", new Vector2(target, target), HoverTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>加点落点闪烁：亮度冲击 + 缩放脉冲（互斥 tween）。ReduceFlash 时只留缩放的轻微确认。</summary>
    private void FlashCard(string nodeId)
    {
        if (!_cards.TryGetValue(nodeId, out var card) || !GodotObject.IsInstanceValid(card))
        {
            return;
        }

        if (card.HasMeta("flash_tween"))
        {
            var old = card.GetMeta("flash_tween").AsGodotObject() as Tween;
            if (old != null && old.IsValid())
            {
                old.Kill();
            }
        }

        // ReduceFlash：不提亮，只留极轻缩放确认
        if (GameState.Instance.ReduceFlash)
        {
            UITheme.PunchScale(card, 1.04f, 0.16f);
            return;
        }

        var tw = card.CreateTween();
        card.SetMeta("flash_tween", Variant.From(tw));
        tw.TweenProperty(card, "modulate", new Color(1.6f, 1.45f, 1.15f), 0.09).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(card, "modulate", Colors.White, 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
    }

    /// <summary>节点卡片样式（状态色/徽标/层数/增幅值；数据源 TalentService 单一事实源）。
    /// idSn 由调用方从 _cardIds 取（免重复构造 StringName）。</summary>
    private void StyleCard(string nodeId, ChamferedPanel card, StringName idSn)
    {
        var talent = GameState.Instance.Talent;
        var level = talent.Level(idSn);
        var cap = talent.CapFor(idSn);
        var maxLevel = talent.MaxLevel(idSn);
        var prereqOk = talent.PrerequisiteMet(idSn);
        var overcharged = talent.IsOvercharged(idSn);
        var canPay = talent.EffectiveCache + 1e-9 >= talent.NextCost(idSn);
        var capSealed = cap < maxLevel && level >= cap && !overcharged; // 互斥/路线封顶
        var catColor = AugmentIcons.ColorFor(idSn);

        var (border, bg) = (overcharged, level, prereqOk) switch
        {
            (true, _, _) => (new Color(UITheme.AccentGold, 0.9f), new Color(0.09f, 0.075f, 0.045f, 0.92f)), // 已风险加点：暗金
            (_, > 0, _) => (new Color(catColor, capSealed ? 0.65f : 0.9f), new Color(0.105f, 0.092f, 0.075f, 0.95f)),
            (_, _, false) => (new Color(UITheme.PanelBorder, 0.28f), new Color(0.055f, 0.050f, 0.044f, 0.75f)), // 未解锁
            _ => (new Color(catColor, 0.55f), new Color(0.078f, 0.073f, 0.065f, 0.85f)),
        };
        if (!overcharged && level < cap && prereqOk && canPay)
        {
            border = UITheme.Accent; // 可升级（点数充足）：高亮边
        }

        card.BorderColor = _hovered == nodeId || _selected == nodeId ? UITheme.Accent
            : !overcharged && level < cap && prereqOk && canPay ? new Color(UITheme.Accent, _pulsePhase ? 1.0f : 0.55f)
            : border;
        card.BgColor = bg;
        card.InnerFrame = true;
        card.InnerFrameColor = new Color(border, 0.3f);

        _levelLabels[nodeId].Text = GdFormat.Format(Tr("TALENT_LV_FMT"), Math.Min(level, cap), cap);
        _levelLabels[nodeId].AddThemeColorOverride("font_color", level > 0 ? UITheme.Text : UITheme.TextDim);
        var badge = _badgeLabels[nodeId];
        if (overcharged)
        {
            badge.Text = Tr("TALENT_BADGE_OVERCHARGED");
            badge.AddThemeColorOverride("font_color", UITheme.AccentGold);
        }
        else if (level >= cap && cap >= maxLevel)
        {
            badge.Text = Tr("TALENT_BADGE_MAX");
            badge.AddThemeColorOverride("font_color", UITheme.AccentGold);
        }
        else if (!prereqOk)
        {
            badge.Text = Tr("TALENT_BADGE_LOCKED");
            badge.AddThemeColorOverride("font_color", UITheme.TextDim);
        }
        else if (capSealed)
        {
            badge.Text = Tr("TALENT_BADGE_SEALED");
            badge.AddThemeColorOverride("font_color", UITheme.Danger);
        }
        else
        {
            badge.Text = "";
        }

        // 右下角增幅值：乘算节点显示每级倍率（与详情面板同源换算）
        var valueLabel = _valueLabels[nodeId];
        var factorV = GameState.Instance.Cfg($"augments.{nodeId}.factor", 0.0);
        if (factorV.VariantType is Variant.Type.Int or Variant.Type.Float && factorV.AsDouble() > 0.0)
        {
            valueLabel.Text = GdFormat.Format(Tr("TALENT_VAL_FMT"), factorV.AsDouble());
            valueLabel.Visible = true;
        }
        else
        {
            valueLabel.Visible = false;
        }
    }

    /// <summary>可升级呼吸脉冲（点数充足的未满节点边框明暗交替；ReduceFlash 时静止）。
    /// 走势聚合态 _anyUpgradeable 在 RefreshStates/RebuildCards 时重算，本方法只读缓存；
    /// 相位基准为本控件累计的模拟时间（delta 来自 _Process），与墙钟/帧率无关。
    /// 可见性判 IsVisibleInTree 而非 Visible：面板关闭只把 CanvasLayer 置 Visible=false，
    /// 本 Control 的 Visible 仍为 true、_Process 照跑——只要有点数可加就会永久每约 1.57s 全卡重刷样式。</summary>
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _categoryId == null || GameState.Instance.ReduceFlash)
        {
            return;
        }

        if (!_anyUpgradeable)
        {
            if (_pulsePhase)
            {
                _pulsePhase = false;
                RefreshStates();
            }

            return;
        }

        _simTime += (float)delta;
        var phase = Mathf.Sin(_simTime * 2.0f) > 0.0; // 与墙钟版 GetTicksMsec()/500 同频（周期 ≈3.14s）
        if (phase != _pulsePhase)
        {
            _pulsePhase = phase;
            RefreshStates(); // 重刷卡片边框明暗 + 连线/根芯片呼吸
        }
    }

    public override void _Draw()
    {
        if (_categoryId == null)
        {
            return;
        }

        var cat = TalentTree.Category(_categoryId);
        var (rootX, rootY) = _layout.Root;
        var talent = GameState.Instance.Talent;
        // 根芯片（大类入口）
        var pulse = _pulsePhase ? 0.35f : 0.22f;
        DrawCircle(new Vector2((float)rootX, (float)rootY), 30f, new Color(UITheme.SlotDark, 0.95f));
        DrawArc(new Vector2((float)rootX, (float)rootY), 30f, 0f, Mathf.Tau, 40, new Color(UITheme.Accent, pulse), 2f, true);
        DrawString(_font, new Vector2((float)rootX - 100f, (float)rootY + 6f), Tr(cat.NameKey),
            HorizontalAlignment.Center, 200f, 14, UITheme.TextDim);

        foreach (var line in cat.Lines)
        {
            Vector2 prev = new((float)rootX, (float)rootY);
            foreach (var nodeId in line.NodeIds)
            {
                if (!_cards.TryGetValue(nodeId, out var card))
                {
                    continue;
                }

                var cur = card.Position + card.Size / 2f;
                var idSn = _cardIds[nodeId];
                var level = talent.Level(idSn);
                var cap = talent.CapFor(idSn);
                var lit = level > 0;
                var sealed_ = cap < talent.MaxLevel(idSn) && level >= cap;
                var col = sealed_ ? new Color(UITheme.Danger, 0.35f)
                    : lit ? new Color(AugmentIcons.ColorFor(idSn), 0.75f)
                    : new Color(UITheme.PanelBorder, 0.18f);
                // 通向悬停/选中节点的那一段提亮加粗：把「依赖来路」指给玩家
                var intoHighlight = nodeId == _hovered || nodeId == _selected;
                var width = intoHighlight ? 3f : 2f;
                if (intoHighlight)
                {
                    col = new Color(UITheme.AccentHot, 0.9f);
                }

                DrawLine(prev, cur, new Color(0f, 0f, 0f, 0.4f), width + 2.5f, true);
                DrawLine(prev, cur, col, width, true);
                prev = cur;
            }
        }

        // 悬停/选中卡片的外圈泛光（绘制在卡片之下的父层，读卡片实时位置）
        var hl = _hovered ?? _selected;
        if (hl != null && _cards.TryGetValue(hl, out var hCard))
        {
            var c = hCard.Position + hCard.Size / 2f;
            DrawArc(c, CardW * 0.62f, 0f, Mathf.Tau, 48, new Color(UITheme.Accent, _pulsePhase ? 0.35f : 0.2f), 2f, true);
        }
    }
}
