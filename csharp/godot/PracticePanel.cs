using System;
using Godot;
using InfiAir.Core.Practice;

namespace InfiAir;

/// <summary>
/// 练习设置面板（标题屏 P 与结算页轮盘「练习模式」共用的一份 UI）：三行直选项
/// （Boss 型别 / 起始难度 / 遭遇事件）+ 提示行 + 开始按钮。
///
/// 形态是可挂载节点而非独立场景：两处入口各在自己场景里挂一份，同一份 UI 只写一遍——
/// 两个入口各写一套的代价是「能选的东西不一样」，而它不会报错，只会让玩家觉得面板坏了。
/// 选项的「索引 → 文案键 / 事件 id」映射全在 core <see cref="PracticeSetup"/>，
/// 本类只做适配（建控件、读 Tr、把当前设置交给打开方），不在这层重写一遍循环或映射。
///
/// 打开方订阅 <see cref="StartRequested"/> 决定「开始练习」去哪：两处入口都调
/// <c>GameState.EnterPractice</c>（单口内含复位与切场景）。
/// 关卡切换/退出的返回键走既有 BackNavigator 口径（面板只消费 <c>ui_cancel</c> 与左右方向键）。
/// </summary>
public partial class PracticePanel : CanvasLayer
{
    /// <summary>行数（Boss 型别 / 起始难度 / 遭遇事件）。</summary>
    public const int RowCount = 3;

    /// <summary>面板画层：CanvasLayer 的默认层是 1，落到开出它的页之下就整块被压住——标题屏在层 1
    /// （同层靠树序，面板后入树故在上面，看着是好的），而结算页在层 20，死亡页开面板时面板被压在
    /// 结算页下：玩家按「练习模式」只看到轮盘消失、屏幕毫无反应，而面板其实已经收了输入
    /// （Esc/方向键/回车都生效）——这类静默坏点编译与冒烟都判不出来，只有画面判得出来。
    /// 取 30：高于全部页面（标题 1 / HUD 2 / 天赋 10 / 暂停 15 / 设置 16 / 结算 20 / 基地 25），
    /// 低于退出确认（40）。</summary>
    private const int PanelLayer = 30;

    /// <summary>开始请求（开始按钮与手柄 A/回车同一出口）。打开方负责 EnterPractice。</summary>
    public event Action<PracticeSetup>? StartRequested;

    /// <summary>面板退场（取消/开始后自关）；打开方据此恢复被它让位的控件（如结算页轮盘）。</summary>
    public event Action? Closed;

    private PracticeSetup _setup = PracticeSetup.Default;
    private ColorRect _dim = null!;
    private Control _plate = null!;
    private readonly Button[] _rowButtons = new Button[RowCount];

    public override void _Ready()
    {
        Layer = PanelLayer;
        // 结算页的轮盘挂在暂停的树上（死亡即 SetTreePaused(true)）：面板必须自己 Always 才收得到输入
        ProcessMode = ProcessModeEnum.Always;
        // 初始值取上一次用过的设置（PendingPractice 在练习场景消费后仍留着值）：连着练同一个 Boss 时
        // 不必每局重选一遍；从未选过 = Default（中档难度、不指定 Boss 与遭遇）。
        _setup = GameState.Instance.PendingPractice;

        var shell = UITheme.MakePageShell("PRACTICE_TITLE");
        AddChild((Node)shell["root"].AsGodotObject());
        _dim = (ColorRect)shell["dim"].AsGodotObject();
        _plate = (Control)shell["panel"].AsGodotObject();
        _plate.CustomMinimumSize = new Vector2(760.0f, 480.0f);
        var content = (VBoxContainer)shell["content"].AsGodotObject();

        AddRow(content, "PRACTICE_BOSS", 0);
        AddRow(content, "PRACTICE_DIFFICULTY", 1);
        AddRow(content, "PRACTICE_ENCOUNTER", 2);

        // 提示行：练习与本局进度无关、也不碰玩家存档——玩家决定要不要练之前必须看到
        var note = UITheme.MakeLabel((string)Tr("PRACTICE_NOTE"), UITheme.FontCaption, UITheme.TextDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(680.0f, 0.0f);
        content.AddChild(note);

        content.AddChild(new Control { CustomMinimumSize = new Vector2(0.0f, 8.0f) });

        var start = UITheme.MakeButton((string)Tr("PRACTICE_START"), true);
        start.CustomMinimumSize = new Vector2(300.0f, 56.0f);
        start.Pressed += Confirm;
        content.AddChild(start);

        RefreshRows();
        UITheme.AnimateModalOpen(_dim, _plate, content);
        _rowButtons[0].GrabFocus();
    }

    /// <summary>一行：左侧固定宽度的行名 + 右侧显示当前取值并点击切换的按钮。
    /// 值按钮同时是键盘/手柄焦点位（左右方向键按当前焦点行切换，见 _UnhandledInput）。</summary>
    private void AddRow(Control content, string labelKey, int row)
    {
        var line = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        line.AddThemeConstantOverride("separation", 20);
        content.AddChild(line);

        var label = UITheme.MakeLabel((string)Tr(labelKey), UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        label.CustomMinimumSize = new Vector2(180.0f, 0.0f);
        line.AddChild(label);

        var button = UITheme.MakeButton(string.Empty);
        button.CustomMinimumSize = new Vector2(360.0f, 52.0f);
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var captured = row;
        button.Pressed += () => Cycle(captured, 1);
        line.AddChild(button);
        _rowButtons[row] = button;
    }

    /// <summary>当前设置（探针读口；打开方确认时也可取）。</summary>
    public PracticeSetup Current => _setup;

    /// <summary>三行此刻显示的文字（探针读口：与玩家看到的同一批控件同源，不另算一份）。</summary>
    public string[] RowTexts()
    {
        var texts = new string[RowCount];
        for (var i = 0; i < RowCount; i++)
        {
            texts[i] = _rowButtons[i].Text;
        }

        return texts;
    }

    /// <summary>环形切换某一行（行按钮点击与左右方向键共用；行号越界即无操作）。</summary>
    public void Cycle(int row, int step)
    {
        _setup = row switch
        {
            0 => _setup.CycleBoss(step),
            1 => _setup.CycleDifficulty(step),
            2 => _setup.CycleEncounter(step),
            _ => _setup,
        };
        RefreshRows();
    }

    /// <summary>确认开始（开始按钮与手柄 A/回车同一出口）。</summary>
    public void Confirm() => StartRequested?.Invoke(_setup);

    /// <summary>退场回上一级（Esc/手柄 B）：结算页轮盘在暂停态下继续可用，故退场即自关。</summary>
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

    private void RefreshRows()
    {
        _rowButtons[0].Text = (string)Tr(_setup.BossLabelKey);
        _rowButtons[1].Text = (string)Tr(_setup.DifficultyLabelKey);
        _rowButtons[2].Text = (string)Tr(_setup.EncounterLabelKey);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Close();
            return;
        }

        // 左右方向键 = 切换当前聚焦行的取值（键鼠与手柄同一条路）：键鼠没有「左右箭头即取值」之外
        // 的直觉，手柄的十字键同理；事件必须消费——否则焦点会被左右键挪到同一行的别的控件上，
        // 表现是「按右键没反应，反而跳走了」。
        if (@event.IsActionPressed("ui_left"))
        {
            CycleFocusedRow(-1);
        }
        else if (@event.IsActionPressed("ui_right"))
        {
            CycleFocusedRow(1);
        }
    }

    private void CycleFocusedRow(int step)
    {
        var focused = GetViewport()?.GuiGetFocusOwner();
        for (var i = 0; i < RowCount; i++)
        {
            if (!ReferenceEquals(_rowButtons[i], focused))
            {
                continue;
            }

            GetViewport().SetInputAsHandled();
            Cycle(i, step);
            return;
        }
    }
}
