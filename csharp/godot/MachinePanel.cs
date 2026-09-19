using System;
using Godot;
using InfiAir.Core.Machines;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 初始机型面板（标题屏 M 与底部「机型」入口共用的一份 UI）：左列六行机型（名字 + 加成 / 代价两行）
/// + 右侧机体预览 + 底部提示行。选定即写 <c>GameState.SetMachine</c>（唯一写入口），面板**不自动关闭**
/// ——玩家可以逐型点过去比一比，Esc 退。
///
/// 形态是可挂载节点而非独立场景（同 <see cref="PracticePanel"/>）：名册、加成项与文案键的映射
/// 全在 core <see cref="MachineRoster"/> / <see cref="MachineTraitText"/>，本类只做适配，
/// 不在这层重写一遍名册或乘区反算——面板上写的百分比与实际起飞时的乘区必须同出一条求值路径。
///
/// **两个「当前」分开**：焦点行 = 预览中的那一型（还在比的候选），生效行 = 现在飞的那一型
/// （<c>GameState.MachineId</c>）。玩家要认的永远是后者，故它用行首琥珀条 + 强调色名字标出，
/// 打开面板时焦点也直接落在它上面；预览随聚焦行实时切换，不用确认。
/// </summary>
public partial class MachinePanel : CanvasLayer
{
    /// <summary>行数 = 名册长度（加一型只改名册一处，本文件不另存一份数）。</summary>
    public static int RowCount => MachineRoster.Count;

    /// <summary>面板画层：取值理由同 <see cref="PracticePanel"/>（高于全部页面 1–25、低于退出确认 40）。
    /// 本面板只从标题屏开，但它是可挂载节点——层高不该由「恰好从哪一屏打开」决定。</summary>
    private const int PanelLayer = 30;

    /// <summary>预览方框边长 = 机体贴图 254px × 1.1（放大一档看得清外形差异，又不压过左侧列表）。</summary>
    private const float PreviewPx = 254.0f * 1.1f;

    /// <summary>面板退场（取消后自关）；打开方据此清掉自己对面板的引用。</summary>
    public event Action? Closed;

    private ColorRect _dim = null!;
    private Control _plate = null!;
    private readonly Button[] _rowButtons = new Button[MachineRoster.Count];
    private readonly ColorRect[] _rowMarks = new ColorRect[MachineRoster.Count];
    private readonly Label[] _rowTraits = new Label[MachineRoster.Count];
    private readonly Label[] _rowPenalties = new Label[MachineRoster.Count];
    private TextureRect _preview = null!;
    private Label _previewTrait = null!;
    private Label _previewPenalty = null!;

    /// <summary>预览中的行号（焦点行；＝打开时的生效行）。</summary>
    private int _previewRow;

    public override void _Ready()
    {
        Layer = PanelLayer;
        // 可挂载节点一律自己 Always：挂到暂停的树上（同 PracticePanel 用于结算页的那一份）时
        // 默认继承的暂停态会让面板一个输入都收不到，而它看上去只是「按了没反应」
        ProcessMode = ProcessModeEnum.Always;
        _previewRow = MachineRoster.IndexOf(GameState.Instance.MachineId);

        var shell = UITheme.MakePageShell("MACHINE_TITLE");
        AddChild((Node)shell["root"].AsGodotObject());
        _dim = (ColorRect)shell["dim"].AsGodotObject();
        _plate = (Control)shell["panel"].AsGodotObject();
        _plate.CustomMinimumSize = new Vector2(940.0f, 560.0f);
        var content = (VBoxContainer)shell["content"].AsGodotObject();

        var columns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        columns.AddThemeConstantOverride("separation", 36);
        content.AddChild(columns);

        // 左列：六行机型。行高由按钮定，行内是「标记条 | 机型按钮 | 加成幅度」三段
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 10);
        columns.AddChild(list);
        for (var i = 0; i < MachineRoster.Count; i++)
        {
            AddRow(list, i);
        }

        // 右列：机体预览 + 该型加成说明。预览框走 ChamferedPanel（全站唯一的面板形状语汇），
        // 尺寸给死——面板自身的「内容自适应」只按子节点最小尺寸撑大，而 TextureRect 的
        // IgnoreSize 让贴图不参与最小尺寸计算，故方框边长即贴图绘制边长（254px 的 1.1 倍）
        var side = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        side.AddThemeConstantOverride("separation", 14);
        columns.AddChild(side);
        var frame = new ChamferedPanel
        {
            CustomMinimumSize = new Vector2(PreviewPx, PreviewPx),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        side.AddChild(frame);
        _preview = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _preview.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        frame.AddChild(_preview);
        _previewTrait = UITheme.MakeLabel(string.Empty, UITheme.FontBody, UITheme.AccentGold);
        _previewTrait.CustomMinimumSize = new Vector2(PreviewPx, 0.0f);
        side.AddChild(_previewTrait);
        // 代价一行与加成同处一块面板、字号同档但压暗一档：读得到，但不与「这型强在哪」抢视线
        _previewPenalty = UITheme.MakeLabel(string.Empty, UITheme.FontBody, UITheme.TextDim);
        _previewPenalty.CustomMinimumSize = new Vector2(PreviewPx, 0.0f);
        side.AddChild(_previewPenalty);

        // 提示行：机型既是本局的起飞参数也是偏好，玩家在按 Esc 之前必须看到「选了会怎样」
        var note = UITheme.MakeLabel((string)Tr("MACHINE_NOTE"), UITheme.FontCaption, UITheme.TextDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(820.0f, 0.0f);
        content.AddChild(note);

        RefreshRows();
        PreviewRow(_previewRow);
        UITheme.AnimateModalOpen(_dim, _plate, content);
        // 焦点落在生效行：玩家一进来先看到「现在飞的是这一型」，再按上下比较其它型
        _rowButtons[_previewRow].GrabFocus();
    }

    /// <summary>一行：行首生效标记条 + 机型按钮（显示名，点击或回车/A 选定）+ 右侧「加成 / 代价」两行。
    /// 两行同列上下排（不是并排）：并排时六行的文字宽度不齐，扫描列会被打散；代价压暗一档与加成区分。
    /// 标记条恒占位（改 alpha 而非 Visible）——换标记时行内不重排，否则六行文字会左右抖一下。</summary>
    private void AddRow(Control list, int row)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        list.AddChild(line);

        var mark = new ColorRect
        {
            Color = new Color(UITheme.Accent, 0.0f),
            CustomMinimumSize = new Vector2(4.0f, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        line.AddChild(mark);

        var button = UITheme.MakeButton(string.Empty);
        button.CustomMinimumSize = new Vector2(300.0f, 48.0f);
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // 显式声明可聚焦：手柄 dpad/摇杆靠焦点链移动，焦点环由 ApplyButton 给的那枚
        button.FocusMode = Control.FocusModeEnum.All;
        var captured = row;
        button.Pressed += () => Select(captured);
        button.FocusEntered += () => PreviewRow(captured);
        line.AddChild(button);

        var traits = new VBoxContainer { CustomMinimumSize = new Vector2(240.0f, 0.0f) };
        traits.AddThemeConstantOverride("separation", 0);
        var bonus = UITheme.MakeLabel(string.Empty, UITheme.FontCaption, UITheme.Text, HorizontalAlignment.Right);
        var penalty = UITheme.MakeLabel(string.Empty, UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Right);
        traits.AddChild(bonus);
        traits.AddChild(penalty);
        line.AddChild(traits);

        _rowButtons[row] = button;
        _rowMarks[row] = mark;
        _rowTraits[row] = bonus;
        _rowPenalties[row] = penalty;
    }

    /// <summary>当前生效的机型 id（探针读口：与 GameState 同源，不另存一份）。</summary>
    public string CurrentMachineId() => GameState.Instance.MachineId;

    /// <summary>预览中的机型 id（探针读口：断「聚焦行联动预览」）。</summary>
    public string PreviewMachineId() => MachineRoster.At(_previewRow).Id;

    /// <summary>六行此刻显示的文字（探针读口：与玩家看到的同一批控件同源，不另算一份）。</summary>
    public string[] RowTexts()
    {
        var texts = new string[MachineRoster.Count];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = _rowButtons[i].Text;
        }

        return texts;
    }

    /// <summary>选定某一型（行按钮点击与手柄 A 同一出口）：走 GameState 唯一写入口，
    /// 归一 id → 落偏好（settings.json）→ 结算血上限 → 广播 MachineChanged
    /// （标题屏悬挂展示据此立刻换外观）。生效行随刷新移到新的一型；预览不动——它跟的是焦点行。</summary>
    public void Select(int row)
    {
        GameState.Instance.SetMachine(MachineRoster.At(row).Id);
        RefreshRows();
    }

    /// <summary>退场回标题屏（Esc/手柄 B）。</summary>
    public void Close()
    {
        var fired = false;
        UITheme.AnimateModalClose(this, _dim, _plate, () =>
        {
            if (fired)
            {
                return;
            }

            fired = true;
            Closed?.Invoke();
            QueueFree();
        });
    }

    /// <summary>把预览切到第 <paramref name="row"/> 型（焦点进入行按钮时调用）：只动预览，
    /// 不动选定——在面板上走一遍不等于改了机型，「选中」必须是一次明确的按下。</summary>
    private void PreviewRow(int row)
    {
        _previewRow = row;
        var spec = MachineRoster.At(row);
        // TextureFilter 不覆盖：别处用到机体贴图的两处（机内 Player、标题屏悬挂展示）都不设过滤，
        // 预览处单开一档会让同一张贴图在两处观感不一致
        _preview.Texture = GD.Load<Texture2D>(MachineRoster.SpritePath(spec));
        _previewTrait.Text = TraitText(spec.Trait, spec.Id);
        // 标准型没有代价：留空而不是把「无加成 · 基准配置」印两遍（同一句话连写两行像排版事故）
        _previewPenalty.Text = spec.Penalty == MachineTrait.None ? string.Empty : TraitText(spec.Penalty, spec.Id);
    }

    private void RefreshRows()
    {
        var current = GameState.Instance.MachineId;
        for (var i = 0; i < MachineRoster.Count; i++)
        {
            var spec = MachineRoster.All[i];
            var isCurrent = string.Equals(spec.Id, current, StringComparison.Ordinal);
            _rowButtons[i].Text = (string)Tr(spec.NameKey);
            // 标记条 + 强调色名字两处并存：条答「哪一行」，色答「一眼扫过去哪一型」——
            // 只靠条时玩家仍要逐行读字才能确认。font_focus_color 一并覆盖，否则聚焦瞬间
            // 名字退回引擎默认灰（焦点环仍是「我在哪」的提示）
            var nameColor = isCurrent ? UITheme.AccentHot : UITheme.Text;
            _rowMarks[i].Color = isCurrent ? UITheme.Accent : new Color(UITheme.Accent, 0.0f);
            _rowButtons[i].AddThemeColorOverride("font_color", nameColor);
            _rowButtons[i].AddThemeColorOverride("font_focus_color", nameColor);
            _rowTraits[i].Text = TraitText(spec.Trait, spec.Id);
            // 标准型没有代价：留空串而不是「代价 +0%」——后者读起来像一项负面读数
            _rowPenalties[i].Text = spec.Penalty == MachineTrait.None ? string.Empty : TraitText(spec.Penalty, spec.Id);
        }
    }

    /// <summary>某一轴（加成或代价）的文案：百分比与**正负号**都由实际生效的乘区反算
    /// （数值被调过、甚至被调成反向，面板上的数字与符号都跟着走），标准型那条没有占位符——
    /// 多传的参数由 GdFormat 忽略，不必在这一层分支。</summary>
    private string TraitText(MachineTrait trait, string machineId)
        => GdFormat.Format(
            Tr(MachineTraitText.Key(trait)),
            MachineTraitText.SignedPercent(trait, GameState.Instance.MachineModsFor(machineId)));

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Close();
            return;
        }

        // 手柄 A：引擎默认映射里 ui_accept 含 A，那条路在 GUI 相位被行按钮吃掉、到不了这里；
        // 映射被改过时 A 会漏下来，故显式补一发（标题屏的入口确认同款写法）。
        // 上下方向键不在这里消费：引擎焦点链在 GUI 相位自行移动焦点，重复消费会让它移不动。
        if (@event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A })
        {
            GetViewport().SetInputAsHandled();
            Select(_previewRow);
        }
    }
}
