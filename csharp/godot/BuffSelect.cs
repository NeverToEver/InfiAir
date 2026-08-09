using Godot;

namespace InfiAir;

/// <summary>
/// 里程碑 Buff 三选一：达到里程碑阈值触发，暂停游戏并弹出 3 张卡片。
/// M5 全量迁移（2026-08-08 自 scripts/buff_select.gd）。
/// </summary>
public partial class BuffSelect : CanvasLayer
{
    // U07：静态 Godot 集合改实例字段（退出 segfault 实测教训，Hud.cs:70）
    private readonly Godot.Collections.Array<Godot.Collections.Dictionary> _buffPool = new()
    {
        new() { ["id"] = new StringName("power_shot"), ["max"] = 5 },
        new() { ["id"] = new StringName("rapid_fire"), ["max"] = 4 },
        new() { ["id"] = new StringName("spread_shot"), ["max"] = 3 },
        new() { ["id"] = new StringName("extra_life"), ["max"] = 10 },
        new() { ["id"] = new StringName("regen"), ["max"] = 1 },
        new() { ["id"] = new StringName("piercing"), ["max"] = 2 },
        new() { ["id"] = new StringName("explosive"), ["max"] = 1 },
        new() { ["id"] = new StringName("lifesteal"), ["max"] = 1 },
        new() { ["id"] = new StringName("armor"), ["max"] = 1 },
        new() { ["id"] = new StringName("evasion"), ["max"] = 1 },
        new() { ["id"] = new StringName("phase_dash"), ["max"] = 3 },
        new() { ["id"] = new StringName("slow_field"), ["max"] = 1 },
        new() { ["id"] = new StringName("efficient_boost"), ["max"] = 2 },
        new() { ["id"] = new StringName("laser_beam"), ["max"] = 1 },
        new() { ["id"] = new StringName("boost_recovery"), ["max"] = 2 },
        new() { ["id"] = new StringName("mothership_recall"), ["max"] = 2 },
        new() { ["id"] = new StringName("crit_shot"), ["max"] = 3 },
        new() { ["id"] = new StringName("shield"), ["max"] = 2 },
        new() { ["id"] = new StringName("bullet_speed"), ["max"] = 3 },
    };

    private static readonly StringName Explosive = new("explosive");
    private static readonly StringName ExtraLife = new("extra_life");

    private CenterContainer _center = null!;
    private HBoxContainer _cards = null!;
    private Label _titleLabel = null!;
    private Godot.Collections.Array _currentAvailable = new();
    private bool _closing; // 选取确认动效播放中：屏蔽再次点选

    private readonly Callable _onMilestoneReached;
    private readonly Callable _onLocaleChanged;

    public BuffSelect()
    {
        _onMilestoneReached = Callable.From<long>(OnMilestoneReached);
        _onLocaleChanged = Callable.From(OnLocaleChanged);
    }

    public override void _Ready()
    {
        Visible = false;
        var dim = new ColorRect { Color = UITheme.DimBg };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        _center = new CenterContainer();
        _center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_center);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 24);
        _center.AddChild(vbox);

        var title = UITheme.MakeLabel(Tr("BUFF_TITLE"), UITheme.FontTitle, UITheme.AccentGold);
        _titleLabel = title;
        vbox.AddChild(title);

        _cards = new HBoxContainer();
        _cards.AddThemeConstantOverride("separation", 24);
        _cards.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(_cards);

        var gs = GameState.Instance;
        if (gs != null)
        {
            gs.Connect("MilestoneReached", _onMilestoneReached);
            gs.Connect("LocaleChanged", _onLocaleChanged);
        }
    }

    public override void _ExitTree()
    {
        // C22：显式断开 GameState 信号连接（C# Connect 连接不随接收方释放自动断开）
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (gs.IsConnected("MilestoneReached", _onMilestoneReached))
            {
                gs.Disconnect("MilestoneReached", _onMilestoneReached);
            }
            if (gs.IsConnected("LocaleChanged", _onLocaleChanged))
            {
                gs.Disconnect("LocaleChanged", _onLocaleChanged);
            }
        }
    }

    /// <summary>抽卡候选池：未满层 + 未被路线锁定；explosive 需 boss_kills 达 buffs.explosive.unlock_boss_kills 解锁（原作 gating）。
    /// 层数上限可用 balance.json 的 buffs.&lt;id&gt;.max_stacks 覆盖（缺省用池内值）。</summary>
    private Godot.Collections.Array<Godot.Collections.Dictionary> _AvailableBuffs()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var b in _buffPool)
        {
            var id = b["id"].AsStringName();
            if ((int)GameState.Instance.BuffCount(id)
                < (int)GameState.Instance.Cfg("buffs." + (string)id + ".max_stacks", b["max"]).AsInt64()
                && !(bool)GameState.Instance.IsBuffLocked(id)
                && (Explosive != id
                    || (int)GameState.Instance.BossKills
                    >= (int)GameState.Instance.Cfg("buffs.explosive.unlock_boss_kills", 3).AsInt64()))
            {
                result.Add(b);
            }
        }

        return result;
    }

    private void OnMilestoneReached(long _milestoneScore)
    {
        if (Visible || GameState.Instance.Health <= 0.0)
        {
            return;
        }

        var available = _AvailableBuffs();
        // 所有 buff 已满层：直接跳过本次里程碑
        if (available.Count == 0)
        {
            return;
        }

        available.Shuffle();
        _currentAvailable = (Godot.Collections.Array)available.Slice(0, 3); // slice end 排他：取前 3 张候选
        BuildCards();
        GetTree().Paused = true;
        Visible = true;
        _closing = false;
        _center.Modulate = new Color(_center.Modulate, 1.0f); // 复位选取动效留下的整体淡出
        // 三卡错峰淡入（标题直接可见）
        UITheme.StaggerOpen(_cards);
        // 键盘导航链路：焦点落在第一张卡（方向键切换，Enter 选取）
        if (_cards.GetChildCount() > 0)
        {
            ((Control)_cards.GetChild(0)).GrabFocus();
        }
    }

    private void BuildCards()
    {
        foreach (var child in _cards.GetChildren())
        {
            child.Free(); // 立即释放：避免 stagger_open 把待释放旧卡计入错峰序号
        }

        foreach (var buff in _currentAvailable)
        {
            _cards.AddChild(MakeCard(buff.AsGodotDictionary()));
        }
    }

    private void OnLocaleChanged()
    {
        _titleLabel.Text = Tr("BUFF_TITLE");
        if (Visible)
        {
            BuildCards();
            if (_closing)
            {
                // H20（健壮性审核）：卡片重建会 free 旧卡 → 关闭动效 tween 被杀 → finished 永不
                // 触发 → _closing + paused 软锁；重建时直接完成关闭语义（对齐 _on_pick_close_finished）
                Visible = false;
                _center.Modulate = new Color(_center.Modulate, 1.0f); // 复位选取动效残留的淡出
                _closing = false;
                GetTree().Paused = false;
            }
            else
            {
                // P3：locale 重建后焦点回落到第一张卡（键盘导航链路不因换语言中断）
                if (_cards.GetChildCount() > 0)
                {
                    ((Control)_cards.GetChild(0)).GrabFocus();
                }
            }
        }
    }

    private Control MakeCard(Godot.Collections.Dictionary buff)
    {
        var id = buff["id"].AsStringName();
        var kindColor = BuffIcons.ColorFor(id); // 分类配色：与 HUD 图标坞同一套
        var card = new ChamferedPanel
        {
            CustomMinimumSize = new Vector2(340.0f, 312.0f), // 三卡统一尺寸
            Brackets = true,
            FocusMode = Control.FocusModeEnum.All,
            // hover/focus 缩放以卡片中心为轴（ChamferedPanel 会按内容放大，resized 后重设中心）
            PivotOffset = new Vector2(340.0f, 312.0f) / 2.0f,
        };
        card.Resized += () => OnCardResized(card);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        card.AddChild(margin);

        var vbox = new VBoxContainer();
        margin.AddChild(vbox);
        vbox.AddThemeConstantOverride("separation", 8);

        var stacks = (int)GameState.Instance.BuffCount(id);
        // 上限口径与 _available_buffs() 相同：balance.json 可覆盖池内值
        var maxStacks = (int)GameState.Instance.Cfg("buffs." + (string)id + ".max_stacks", buff["max"]).AsInt64();

        // 顶部大字形槽：坞瓦片同款 socket（分类色描边 + 内框，76×76），三卡统一槽位高度，保证垂直节奏一致
        var glyphSlot = new CenterContainer { CustomMinimumSize = new Vector2(0.0f, 80.0f) };
        glyphSlot.AddChild(UITheme.MakeBuffSocket(id, 76.0f));
        vbox.AddChild(glyphSlot);

        // 名称行：名称 + NEW! 徽标（首次获得时）
        var nameRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        nameRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(nameRow);
        nameRow.AddChild(UITheme.MakeLabel(Tr($"BUFF_{((string)id).ToUpper()}_NAME"), UITheme.FontHeader, UITheme.Accent));
        if (stacks == 0)
        {
            nameRow.AddChild(UITheme.MakeLabel(Tr("BUFF_NEW_BADGE"), UITheme.FontSmall, UITheme.AccentGold));
        }

        // 层数 pip 槽：固定高度（无 pip 也占位），●=已有层 ○=空位；上限 >8（如 extra_life 10）退化为文字格式
        var pipSlot = new CenterContainer { CustomMinimumSize = new Vector2(0.0f, 22.0f) };
        vbox.AddChild(pipSlot);
        if (maxStacks <= 8)
        {
            var pipRow = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
            };
            pipRow.AddThemeConstantOverride("separation", 2);
            pipSlot.AddChild(pipRow);
            if (stacks > 0)
            {
                pipRow.AddChild(UITheme.MakeLabel(new string('●', stacks), UITheme.FontCaption, UITheme.AccentGold));
            }

            if (stacks < maxStacks)
            {
                pipRow.AddChild(UITheme.MakeLabel(new string('○', maxStacks - stacks), UITheme.FontCaption, UITheme.TextDim));
            }
        }
        else if (stacks > 0)
        {
            pipSlot.AddChild(UITheme.MakeLabel(GsFormat(Tr("BUFF_STACKS_FMT"), stacks), UITheme.FontCaption, UITheme.AccentGold));
        }

        // 分隔线 + 来源分类小标签（进攻/机动/通用，分类色）
        var dividerWrap = new CenterContainer();
        vbox.AddChild(dividerWrap);
        var divider = new ColorRect
        {
            Color = UITheme.AccentDim,
            CustomMinimumSize = new Vector2(220.0f, 1.0f),
        };
        dividerWrap.AddChild(divider);
        var kindKey = "BUFF_KIND_GENERAL";
        var routeLines = GameState.Instance.ROUTE_LINES;
        if (routeLines["offense"].AsGodotArray().Contains(id))
        {
            kindKey = "BUFF_KIND_OFFENSE";
        }
        else if (routeLines["mobility"].AsGodotArray().Contains(id))
        {
            kindKey = "BUFF_KIND_MANEUVER";
        }

        vbox.AddChild(UITheme.MakeLabel(Tr(kindKey), UITheme.FontSmall, kindColor));

        // 描述：固定最小高度，三卡底缘对齐
        var descLabel = UITheme.MakeLabel(Tr($"BUFF_{((string)id).ToUpper()}_DESC"), UITheme.FontBody, UITheme.TextDim);
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        descLabel.CustomMinimumSize = new Vector2(0.0f, 62.0f);
        vbox.AddChild(descLabel);

        card.GuiInput += (ev) => OnCardGuiInput(ev, id, card);
        // hover/focus 提亮边框（键盘导航焦点可见）
        card.MouseEntered += () => SetCardHighlight(card, true);
        card.MouseExited += () => SetCardHighlight(card, false);
        card.FocusEntered += () => SetCardHighlight(card, true);
        card.FocusExited += () => SetCardHighlight(card, false);
        // hover/focus 1.05 缩放（与边框高亮共存）
        card.MouseEntered += () => TweenCardScale(card, true);
        card.MouseExited += () => TweenCardScale(card, false);
        card.FocusEntered += () => TweenCardScale(card, true);
        card.FocusExited += () => TweenCardScale(card, false);
        return card;
    }

    private void OnCardResized(ChamferedPanel card)
    {
        card.PivotOffset = card.Size / 2.0f;
    }

    // hover/focus 缩放反馈：150ms tween 到 1.05，离开/失焦回 1.0（树暂停时 BuffUI 层 process_mode=Always 可播）
    private void TweenCardScale(ChamferedPanel card, bool on)
    {
        if (card.HasMeta("hover_tween"))
        {
            ((Tween)card.GetMeta("hover_tween").AsGodotObject()).Kill();
        }

        var tween = card.CreateTween();
        card.SetMeta("hover_tween", Variant.From(tween));
        tween.TweenProperty(card, "scale", on ? new Vector2(1.05f, 1.05f) : Vector2.One, 0.15);
    }

    private void SetCardHighlight(ChamferedPanel card, bool on)
    {
        card.BorderColor = on ? UITheme.Accent : UITheme.PanelBorder;
        card.BracketColor = on ? UITheme.AccentGold : UITheme.Accent;
    }

    // A7：测试/诊断经公开接口——直接选取指定 buff（无卡片上下文，等价 _on_card_gui_input 的 card==null 路径）
    public void PickBuff(StringName id)
    {
        if (_closing)
        {
            return;
        }

        GameState.Instance.PlaySfx(GameState.Instance.SFX_BUFF_PICK);
        GameState.Instance.AddBuff(id);
        if (id == ExtraLife)
        {
            GameState.Instance.Heal(GameState.Instance.Cfg("buffs.extra_life.heal_on_pick", 30).AsDouble());
        }

        Visible = false;
        GetTree().Paused = false;
    }

    public Godot.Collections.Array CurrentAvailable() => _currentAvailable;

    public HBoxContainer Cards() => _cards;

    public bool Closing() => _closing;

    public Godot.Collections.Array<Godot.Collections.Dictionary> AvailableBuffs() => _AvailableBuffs();

    private void OnCardGuiInput(InputEvent @event, StringName id, Control card)
    {
        if (_closing)
        {
            return;
        }

        var picked = @event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left;
        // 键盘/手柄统一选取：ui_accept（Enter/Space/手柄 A）——卡片是自定义 Control，
        // Godot 把 action 以原始事件类型路由给焦点控件，若限 InputEventKey 则手柄 A 被排除（手柄软锁）
        if (@event.IsAction("ui_accept") && IsPressed(@event) && !(@event is InputEventKey key && key.Echo))
        {
            picked = true;
        }

        if (picked)
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_BUFF_PICK);
            GameState.Instance.AddBuff(id);
            if (id == ExtraLife)
            {
                // 对齐原作：选取瞬时 +30 HP（上限 +50 由 max_health() 按层数自动生效）
                GameState.Instance.Heal(GameState.Instance.Cfg("buffs.extra_life.heal_on_pick", 30).AsDouble());
            }

            // 2026-08-03 审计：card == null 死分支删除——唯一连接点恒传非空 card，直调走 pick_buff()
            // 选取确认动效：被选中卡缩至 0.95 + 金色提亮，随后整体 ~200ms 内淡出关闭（其余两卡随面板隐藏）
            _closing = true;
            if (card.HasMeta("hover_tween"))
            {
                ((Tween)card.GetMeta("hover_tween").AsGodotObject()).Kill();
            }

            var tween = card.CreateTween();
            tween.TweenProperty(card, "scale", new Vector2(0.95f, 0.95f), 0.1);
            tween.Parallel().TweenProperty(card, "modulate", UITheme.AccentGold.Lightened(0.4f), 0.1);
            tween.TweenProperty(_center, "modulate:a", 0.0, 0.1);
            tween.Finished += OnPickCloseFinished;
        }
    }

    // 确认动效结束：关闭面板并恢复对局（与原收尾语义一致）
    private void OnPickCloseFinished()
    {
        Visible = false;
        _center.Modulate = new Color(_center.Modulate, 1.0f);
        _closing = false;
        GetTree().Paused = false;
    }

    /// <summary>GDScript 鸭子属性 event.pressed 的等价：覆盖全部带 pressed 属性的 InputEvent 类型。</summary>
    private static bool IsPressed(InputEvent e)
    {
        return e switch
        {
            InputEventAction a => a.Pressed,
            InputEventKey k => k.Pressed,
            InputEventMouseButton b => b.Pressed,
            InputEventJoypadButton j => j.Pressed,
            InputEventScreenTouch t => t.Pressed,
            _ => false,
        };
    }

    /// <summary>GDScript `%` 格式化最小等价（%d/%s/%f/%%；翻译串占位符为 GDScript 风格，string.Format 不识别）。
    /// GameOverUi 复用（同批次迁移，M7 前随 UI 层整体收敛）。</summary>
    internal static string GsFormat(string fmt, params object[] args)
    {
        var sb = new System.Text.StringBuilder();
        var argIndex = 0;
        for (var i = 0; i < fmt.Length; i++)
        {
            var ch = fmt[i];
            if (ch == '%' && i + 1 < fmt.Length)
            {
                var next = fmt[i + 1];
                i += 1;
                switch (next)
                {
                    case '%':
                        sb.Append('%');
                        break;
                    case 's':
                        sb.Append(argIndex < args.Length ? args[argIndex++]?.ToString() ?? "" : "");
                        break;
                    case 'd':
                        sb.Append(argIndex < args.Length ? Convert.ToInt64(args[argIndex++]) : 0);
                        break;
                    case 'f':
                        sb.Append(argIndex < args.Length ? Convert.ToDouble(args[argIndex++]) : 0.0);
                        break;
                    default:
                        sb.Append('%').Append(next);
                        break;
                }
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

}
