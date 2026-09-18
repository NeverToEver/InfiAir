using Godot;
using InfiAir.Core.Hud;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// SettingsUi partial：设置页「准星」。实时预览 + 档案管理（切换/新建副本/重命名/删除，
/// 至少保留一档）+ 形状四档 + 参数滑杆（大小/线宽/间距/不透明度/旋转）+ 细节开关
/// （中心点/点大小/T 形/描边/交战变色）+ 颜色（色板 + RGB 滑杆）+ 准星码导出导入。
/// 编辑即时写服务（内存），滑杆 DragEnded 与离页兜底统一落盘（_pendingPersist / joy 同款）；
/// 结构操作（切档/新建/重命名/删除/导入）即时落盘。档案名存文案键、显示口 Tr 过——
/// 无翻译命中时原样返回，玩家自建名不受影响。控件值刷新统一走 <see cref="RefreshCrosshairControls"/>
/// （结构变更后整组回读，刷新期屏蔽控件回写防自激）。
/// </summary>
public partial class SettingsUi
{
    private OptionButton _xhairProfileOption = null!;
    private readonly Dictionary<CrosshairShape, Button> _xhairShapeButtons = new();
    private readonly ButtonGroup _xhairShapeGroup = new();
    private readonly List<(HSlider Slider, Label Label, Func<CrosshairProfile, double> Read, string Format)>
        _xhairSliders = new();
    private readonly List<(Button Button, Func<CrosshairProfile, bool> Read)> _xhairSwitches = new();
    private Label _xhairHint = null!;
    private LineEdit _xhairCodeEdit = null!;
    private ConfirmationDialog? _xhairRenameDialog;
    private LineEdit? _xhairRenameEdit;
    private bool _xhairRefreshing;

    private VBoxContainer BuildCrosshairPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 14);

        // 实时预览：形状几何与游戏内同源（CrosshairRender），改动即时可见
        var preview = new CrosshairPreview();
        preview.CustomMinimumSize = new Vector2(0.0f, 130.0f);
        preview.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        page.AddChild(preview);

        // ---------------- 档案 ----------------
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_XHAIR_PROFILES")));
        var profileRow = new HBoxContainer();
        profileRow.AddThemeConstantOverride("separation", 10);
        page.AddChild(profileRow);
        _xhairProfileOption = new OptionButton();
        _xhairProfileOption.CustomMinimumSize = new Vector2(220.0f, 44.0f);
        UITheme.ApplyOptionButton(_xhairProfileOption);
        _xhairProfileOption.ItemSelected += idx =>
        {
            GameState.Instance.SetCrosshairActive((int)idx);
            RefreshCrosshairControls();
        };
        profileRow.AddChild(_xhairProfileOption);
        var newButton = UITheme.MakeButton(Tr("SET_XHAIR_NEW"));
        newButton.Pressed += OnXhairDuplicate;
        profileRow.AddChild(newButton);
        var renameButton = UITheme.MakeButton(Tr("SET_XHAIR_RENAME"));
        renameButton.Pressed += OnXhairRenamePressed;
        profileRow.AddChild(renameButton);
        var deleteButton = UITheme.MakeButton(Tr("SET_XHAIR_DELETE"));
        deleteButton.Pressed += OnXhairDeletePressed;
        profileRow.AddChild(deleteButton);

        // ---------------- 形状 ----------------
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_XHAIR_SHAPE")));
        var shapeRow = new HBoxContainer();
        shapeRow.AddThemeConstantOverride("separation", 10);
        page.AddChild(shapeRow);
        foreach (var shape in new[] { CrosshairShape.Bracket, CrosshairShape.Cross, CrosshairShape.Circle, CrosshairShape.Dot })
        {
            var key = "SET_XHAIR_SHAPE_" + CrosshairProfile.ShapeName(shape).ToUpperInvariant();
            var button = UITheme.MakeToggleButton(Tr(key), _xhairShapeGroup);
            button.Pressed += () =>
            {
                MutateActiveCrosshair(p => p with { Shape = shape });
                PersistCrosshairUi();
            };
            shapeRow.AddChild(button);
            _xhairShapeButtons[shape] = button;
        }

        // ---------------- 参数 ----------------
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_XHAIR_PARAMS")));
        MakeCrosshairSlider(page, Tr("SET_XHAIR_SIZE"),
            CrosshairProfile.SizeMin, CrosshairProfile.SizeMax, CrosshairProfile.SizeStep, "%.2f×",
            p => p.Size, (v, p) => p with { Size = (float)v });
        MakeCrosshairSlider(page, Tr("SET_XHAIR_THICKNESS"),
            CrosshairProfile.ThicknessMin, CrosshairProfile.ThicknessMax, 1.0, "%d",
            p => p.Thickness, (v, p) => p with { Thickness = (int)v });
        MakeCrosshairSlider(page, Tr("SET_XHAIR_GAP"),
            0.0, CrosshairProfile.GapMax, 0.5, "%.1f",
            p => p.Gap, (v, p) => p with { Gap = (float)v });
        MakeCrosshairSlider(page, Tr("SET_XHAIR_OPACITY"),
            CrosshairProfile.AlphaMin * 100.0, 100.0, 1.0, "%d%%",
            p => p.Alpha * 100.0, (v, p) => p with { Alpha = (float)(v / 100.0) });
        MakeCrosshairSlider(page, Tr("SET_XHAIR_ROTATION"),
            0.0, CrosshairProfile.RotationMax, CrosshairProfile.RotationStep, "%d°",
            p => p.RotationDeg, (v, p) => p with { RotationDeg = (int)v });

        // ---------------- 细节 ----------------
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_XHAIR_DETAILS")));
        MakeSwitchRow(page, Tr("SET_XHAIR_CENTER_DOT"), p => p.CenterDot, (on, p) => p with { CenterDot = on });
        MakeCrosshairSlider(page, Tr("SET_XHAIR_DOT_SIZE"),
            CrosshairProfile.DotSizeMin, CrosshairProfile.DotSizeMax, 1.0, "%d px",
            p => p.DotSize, (v, p) => p with { DotSize = (int)v });
        MakeSwitchRow(page, Tr("SET_XHAIR_TSHAPE"), p => p.TShape, (on, p) => p with { TShape = on });
        MakeSwitchRow(page, Tr("SET_XHAIR_OUTLINE"), p => p.Outline, (on, p) => p with { Outline = on });
        MakeSwitchRow(page, Tr("SET_XHAIR_STATE_TINT"), p => p.StateTint, (on, p) => p with { StateTint = on });
        page.AddChild(UITheme.MakeLabel(
            Tr("SET_XHAIR_STATE_TINT_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));

        // ---------------- 颜色 ----------------
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_XHAIR_COLOR")));
        var swatchRow = new HBoxContainer();
        swatchRow.AddThemeConstantOverride("separation", 10);
        page.AddChild(swatchRow);
        foreach (var (r, g, b, a) in new (byte R, byte G, byte B, byte A)[]
        {
            (255, 194, 77, 242), // 主题琥珀（默认档基色）
            (255, 255, 255, 255),
            (255, 92, 117, 242), // 敌机同族红粉
            (232, 193, 112, 255),
            (90, 220, 255, 242),
            (120, 230, 120, 242),
        })
        {
            MakeSwatch(swatchRow, r, g, b, a);
        }

        MakeCrosshairSlider(page, "R", 0.0, 255.0, 1.0, "%d", p => p.R, (v, p) => p with { R = (byte)v });
        MakeCrosshairSlider(page, "G", 0.0, 255.0, 1.0, "%d", p => p.G, (v, p) => p with { G = (byte)v });
        MakeCrosshairSlider(page, "B", 0.0, 255.0, 1.0, "%d", p => p.B, (v, p) => p with { B = (byte)v });

        // ---------------- 准星码 ----------------
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_XHAIR_CODE")));
        page.AddChild(UITheme.MakeLabel(
            Tr("SET_XHAIR_CODE_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        var codeRow = new HBoxContainer();
        codeRow.AddThemeConstantOverride("separation", 10);
        page.AddChild(codeRow);
        var exportButton = UITheme.MakeButton(Tr("SET_XHAIR_EXPORT"));
        exportButton.Pressed += OnXhairExportPressed;
        codeRow.AddChild(exportButton);
        _xhairCodeEdit = new LineEdit
        {
            PlaceholderText = Tr("SET_XHAIR_IMPORT_PLACEHOLDER"),
            CustomMinimumSize = new Vector2(300.0f, 44.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        UITheme.ApplyLineEdit(_xhairCodeEdit);
        codeRow.AddChild(_xhairCodeEdit);
        var importButton = UITheme.MakeButton(Tr("SET_XHAIR_IMPORT"));
        importButton.Pressed += OnXhairImportPressed;
        codeRow.AddChild(importButton);
        _xhairHint = UITheme.MakeLabel(string.Empty, UITheme.FontCaption, UITheme.AccentGold, HorizontalAlignment.Left);
        page.AddChild(_xhairHint);

        RefreshCrosshairControls();
        return page;
    }

    /// <summary>准星样式编辑统一入口：写当前激活档（服务侧即时生效不落盘），随后刷新显示。</summary>
    private void MutateActiveCrosshair(Func<CrosshairProfile, CrosshairProfile> mutate)
    {
        var gs = GameState.Instance;
        gs.UpdateCrosshairProfile(gs.CrosshairActiveIndex, mutate(gs.ActiveCrosshair));
    }

    /// <summary>单次点击类编辑（形状/开关/色板/结构操作）完成后立即落盘。</summary>
    private void PersistCrosshairUi()
    {
        _pendingPersist = false;
        GameState.Instance.SaveSettings();
    }

    /// <summary>参数滑杆行（标题 + HSlider + 数值标签）。取值与写回都以当前激活档为基准，
    /// 拖动即时生效；落盘走 DragEnded（PersistPending，离页兜底覆盖）。</summary>
    private void MakeCrosshairSlider(
        Container parent, string title, double minValue, double maxValue, double step, string format,
        Func<CrosshairProfile, double> read, Func<double, CrosshairProfile, CrosshairProfile> mutate)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        parent.AddChild(row);
        var label = UITheme.MakeLabel(title, UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        label.CustomMinimumSize = new Vector2(LabelColumnWidth, 0.0f);
        row.AddChild(label);
        var gs = GameState.Instance;
        var slider = new UITheme.ChamferSlider
        {
            MinValue = minValue,
            MaxValue = maxValue,
            Step = step,
            Value = read(gs.ActiveCrosshair),
            CustomMinimumSize = new Vector2(240.0f, 0.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        UITheme.ApplySlider(slider);
        row.AddChild(slider);
        var valueLabel = UITheme.MakeLabel(GdFormat.Format(format, read(gs.ActiveCrosshair)), UITheme.FontBody, UITheme.TextDim);
        valueLabel.CustomMinimumSize = new Vector2(70.0f, 0.0f);
        row.AddChild(valueLabel);
        slider.ValueChanged += v =>
        {
            valueLabel.Text = GdFormat.Format(format, v);
            if (_xhairRefreshing)
            {
                return; // 刷新回读不得写回服务（自激防线）
            }

            _pendingPersist = true;
            MutateActiveCrosshair(p => mutate(v, p));
        };
        slider.DragEnded += _ => PersistPending();
        _xhairSliders.Add((slider, valueLabel, read, format));
    }

    /// <summary>开关行：单次点击＝完整编辑（即时生效并落盘）。</summary>
    private void MakeSwitchRow(
        Container parent, string title, Func<CrosshairProfile, bool> read,
        Func<bool, CrosshairProfile, CrosshairProfile> mutate)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        parent.AddChild(row);
        var label = UITheme.MakeLabel(title, UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        label.CustomMinimumSize = new Vector2(LabelColumnWidth, 0.0f);
        row.AddChild(label);
        var button = new Button
        {
            ToggleMode = true,
            ButtonPressed = read(GameState.Instance.ActiveCrosshair),
            CustomMinimumSize = new Vector2(72.0f, 40.0f),
        };
        UITheme.ApplyButton(button);
        button.Toggled += on =>
        {
            MutateActiveCrosshair(p => mutate(on, p));
            PersistCrosshairUi();
        };
        row.AddChild(button);
        _xhairSwitches.Add((button, read));
    }

    private void MakeSwatch(Container row, byte r, byte g, byte b, byte a)
    {
        var button = new Button { CustomMinimumSize = new Vector2(44.0f, 32.0f) };
        UITheme.AttachButtonSfx(button); // 色板是自绘样式盒的按钮（不走 ApplyButton），交互音单挂
        var style = new StyleBoxFlat { BgColor = new Color(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f) };
        style.SetCornerRadiusAll(4);
        button.AddThemeStyleboxOverride("normal", style);
        var hot = new StyleBoxFlat { BgColor = new Color(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f) };
        hot.SetCornerRadiusAll(4);
        hot.BorderColor = UITheme.AccentHot;
        hot.SetBorderWidthAll(2);
        button.AddThemeStyleboxOverride("hover", hot);
        button.AddThemeStyleboxOverride("focus", hot);
        button.AddThemeStyleboxOverride("pressed", hot);
        button.Pressed += () =>
        {
            MutateActiveCrosshair(p => p with { R = r, G = g, B = b, A = a });
            PersistCrosshairUi();
        };
        row.AddChild(button);
    }

    // ---------------- 档案结构操作 ----------------

    private void OnXhairDuplicate()
    {
        var gs = GameState.Instance;
        var entry = gs.CrosshairProfiles[gs.CrosshairActiveIndex];
        gs.AddCrosshairProfile(UniqueCopyName(entry.Name), entry.Profile);
        RefreshCrosshairControls();
    }

    /// <summary>副本名：基名 + 序号，跳过重名（列表里同名档分不清谁是谁）。</summary>
    private string UniqueCopyName(string baseName)
    {
        var names = new HashSet<string>();
        foreach (var entry in GameState.Instance.CrosshairProfiles)
        {
            names.Add(entry.Name);
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void OnXhairRenamePressed()
    {
        EnsureXhairRenameDialog();
        var gs = GameState.Instance;
        _xhairRenameEdit!.Text = gs.CrosshairProfiles[gs.CrosshairActiveIndex].Name;
        _xhairRenameDialog!.PopupCentered();
        _xhairRenameEdit.GrabFocus();
        _xhairRenameEdit.SelectAll();
    }

    /// <summary>重命名弹窗惰性构建一次后复用（文案每次打开时刷新，语言切换不残留旧语言）。</summary>
    private void EnsureXhairRenameDialog()
    {
        if (_xhairRenameDialog != null)
        {
            return;
        }

        _xhairRenameDialog = new ConfirmationDialog { Title = Tr("SET_XHAIR_RENAME_TITLE") };
        _xhairRenameDialog.OkButtonText = Tr("SET_CONFIRM_OK");
        _xhairRenameDialog.CancelButtonText = Tr("SET_CONFIRM_CANCEL");
        _xhairRenameEdit = new LineEdit();
        UITheme.ApplyLineEdit(_xhairRenameEdit);
        _xhairRenameDialog.AddChild(_xhairRenameEdit);
        _xhairRenameDialog.Confirmed += OnXhairRenameConfirmed;
        AddChild(_xhairRenameDialog);
    }

    private void OnXhairRenameConfirmed()
    {
        var gs = GameState.Instance;
        var name = _xhairRenameEdit!.Text.StripEdges();
        if (name.Length == 0)
        {
            return; // 空名不改（列表里空行不可读）
        }

        gs.RenameCrosshairProfile(gs.CrosshairActiveIndex, name);
        RefreshCrosshairControls();
    }

    private void OnXhairDeletePressed()
    {
        var gs = GameState.Instance;
        if (gs.CrosshairProfiles.Count <= CrosshairBook.MinProfiles)
        {
            _xhairHint.Text = Tr("SET_XHAIR_DELETE_LAST");
            UITheme.PlayUiSfx(SfxId.UiDeny); // 被拒动作：提示行只说「为什么」，回绝音说「按不成」
            return;
        }

        ConfirmDestructive("SET_XHAIR_DELETE_CONFIRM", () =>
        {
            GameState.Instance.RemoveCrosshairProfile(GameState.Instance.CrosshairActiveIndex);
            RefreshCrosshairControls();
        });
    }

    // ---------------- 准星码 ----------------

    private void OnXhairExportPressed()
    {
        DisplayServer.ClipboardSet(CrosshairCode.Encode(GameState.Instance.ActiveCrosshair));
        SetXhairHint(Tr("SET_XHAIR_COPIED"));
    }

    private void OnXhairImportPressed()
    {
        var result = GameState.Instance.ImportCrosshairCode(_xhairCodeEdit.Text);
        switch (result)
        {
            case CrosshairCodeResult.Ok:
                _xhairCodeEdit.Clear();
                SetXhairHint(Tr("SET_XHAIR_IMPORT_OK"));
                break;
            case CrosshairCodeResult.BadChecksum:
                SetXhairHint(Tr("SET_XHAIR_ERR_CHECKSUM"));
                break;
            case CrosshairCodeResult.UnsupportedVersion:
                SetXhairHint(Tr("SET_XHAIR_ERR_VERSION"));
                break;
            case CrosshairCodeResult.OutOfRange:
                SetXhairHint(Tr("SET_XHAIR_ERR_RANGE"));
                break;
            default:
                SetXhairHint(Tr("SET_XHAIR_ERR_MALFORMED"));
                break;
        }

        if (result != CrosshairCodeResult.Ok)
        {
            UITheme.PlayUiSfx(SfxId.UiDeny); // 导入被拒（校验失败一律不落档）：回绝音与提示行同步
        }
    }

    private void SetXhairHint(string text) => _xhairHint.Text = text;

    // ---------------- 控件值整组回读 ----------------

    /// <summary>结构变更（切档/新建/重命名/删除/导入）后整组控件从服务回读；
    /// 刷新期 ValueChanged 只更新标签不写回（防自激）。</summary>
    private void RefreshCrosshairControls()
    {
        if (_xhairProfileOption == null)
        {
            return;
        }

        _xhairRefreshing = true;
        var gs = GameState.Instance;
        _xhairProfileOption.Clear();
        foreach (var entry in gs.CrosshairProfiles)
        {
            _xhairProfileOption.AddItem(Tr(entry.Name));
        }

        _xhairProfileOption.Select(gs.CrosshairActiveIndex);
        var profile = gs.ActiveCrosshair;
        foreach (var (shape, button) in _xhairShapeButtons)
        {
            button.SetPressedNoSignal(shape == profile.Shape);
        }

        foreach (var (slider, valueLabel, read, format) in _xhairSliders)
        {
            var value = read(profile);
            slider.SetValueNoSignal(value);
            valueLabel.Text = GdFormat.Format(format, value);
        }

        foreach (var (button, read) in _xhairSwitches)
        {
            button.SetPressedNoSignal(read(profile));
        }

        _xhairRefreshing = false;
    }
}

/// <summary>准星预览画布：暗槽底 + 主题细边，形状几何与游戏内同源（CrosshairRender）。
/// 档案引用变更才重绘（服务每次编辑都换新 record 实例，引用比对即可判变更）。</summary>
public sealed partial class CrosshairPreview : Control
{
    private CrosshairProfile? _lastProfile;

    public override void _Process(double delta)
    {
        var profile = GameState.Instance.ActiveCrosshair;
        if (!ReferenceEquals(profile, _lastProfile))
        {
            _lastProfile = profile;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, UITheme.SlotDark);
        DrawRect(rect, UITheme.AccentDim, false, 1.0f);
        CrosshairRender.Draw(this, GameState.Instance.ActiveCrosshair, 0.0f, 0.0f, 1.0f, Size * 0.5f);
    }
}
