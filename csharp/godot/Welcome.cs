using Godot;
using InfiAir.Core;

namespace InfiAir;

/// <summary>
/// welcome 主场景（2026-08-04 账户系统 T3）。
/// 登录阶段：左栏账号面板（注册/登录/游客/删除 + 下拉）；登录/游客放行后切主区 =
/// 左缘圆盘菜单（出击/教程/设置/研究所/退出）+ 右栏难度档位。
/// 进入 main 后由 main 依存档自动继续。
/// ESC 层级：关游客/删除确认 → 关下拉 → 退出确认（welcome 是首场景）。
/// </summary>
public partial class Welcome : RadialMenuLayer
{
    private enum Stage
    {
        Login,
        Main,
    }

    private const int DropdownMax = 4;
    private const int UsernameMax = 16;
    private const int PasswordMax = 16;

    // UserDB 长度约束常量（scripts/user_db.gd const NAME_MIN/NAME_MAX/PASSWORD_MIN/PASSWORD_MAX）——
    // GDScript 常量 C# 不可 typed 直读，经脚本资源 GetScriptConstantMap 动态取（保持与 user_db.gd 单一事实源）
    // M7：UserDB 迁 C#，常量 typed 直读（原经脚本资源 GetScriptConstantMap）
    private static readonly int UserDbNameMin = UserDB.NameMin;
    private static readonly int UserDbNameMax = UserDB.NameMax;
    private static readonly int UserDbPasswordMin = UserDB.PasswordMin;
    private static readonly int UserDbPasswordMax = UserDB.PasswordMax;

    private Stage _stage = Stage.Login;
    private ColorRect _dim = null!;
    private ChamferedPanel _loginPanel = null!;
    private VBoxContainer _loginContent = null!;
    private LineEdit _usernameLine = null!;
    private LineEdit _passwordLine = null!;
    private Label _msgLabel = null!;
    private Panel? _dropdown;
    /// <summary>FocusExited 延迟裁决的目标下拉：防止回调执行前又打开了新下拉而被误关。</summary>
    private Panel? _focusExitDropdown;
    private readonly System.Collections.Generic.List<Button> _dropdownButtons = new();
    private VBoxContainer _mainZone = null!;
    private Label _corruptLabel = null!; // 损坏警告区（登录前也可见；.corrupt 提示是开始屏契约）
    private bool _isUser; // 当前主区是否登录用户（研究所入口显隐）
    private CanvasLayer _labOverlay = null!;
    private Button _labClose = null!;
    private ResearchLab _labRows = null!;
    /// <summary>2026-08-09 审计：难度按钮表 typed 化（原 Variant Dictionary + 运行时强转）。</summary>
    private readonly System.Collections.Generic.Dictionary<StringName, Button> _diffButtons = new();
    private readonly ButtonGroup _diffGroup = new();
    // 模态引用（2026-08-09 审计：原 Variant Dictionary {"layer","ok","cancel"} + 类内 15 处强转 → typed）
    private ModalParts _guestConfirm = null!;
    private ModalParts _deleteConfirm = null!;
    private ModalParts _exitConfirm = null!;

    /// <summary>轻量模态引用（MakeModal 产物；Layer=遮罩层，Ok/Cancel=按钮）。</summary>
    private sealed class ModalParts
    {
        public CanvasLayer Layer = null!;
        public Button Ok = null!;
        public Button Cancel = null!;
    }
    // U01（2026-08-09 审计）：LocaleChanged 连接缓存 Callable 供 _ExitTree 配对断开——
    // welcome→main 切换后残留连接回调已释放实例（Hud.cs:456 实测先例可致退出 segfault）
    private readonly Callable _onLocaleChanged;

    public Welcome()
    {
        _onLocaleChanged = Callable.From(RefreshTexts);
    }

    public override void _Ready()
    {
        Visible = true;
        // 全遮光标题屏（不透明对局背景；welcome 是独立场景，无冻结背景语义）
        _dim = new ColorRect { Color = new Color(0.018f, 0.03f, 0.055f, 1.0f) };
        _dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_dim);
        var backdrop = new StartBackdrop();
        AddChild(backdrop);

        // 左上品牌区
        var hero = new VBoxContainer();
        hero.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        hero.Position = new Vector2(140.0f, 130.0f);
        hero.CustomMinimumSize = new Vector2(800.0f, 0.0f);
        hero.AddThemeConstantOverride("separation", 10);
        AddChild(hero);
        var title = UITheme.MakeLabel("InfiAir", UITheme.FontDisplay, UITheme.Accent, HorizontalAlignment.Left);
        // 大标题软辉光：同色 shadow 外扩（outline_size 撑开阴影模糊半径），深底上更具品牌质感
        title.AddThemeColorOverride("font_shadow_color", new Color(UITheme.Accent, 0.4f));
        title.AddThemeConstantOverride("shadow_offset_x", 0);
        title.AddThemeConstantOverride("shadow_offset_y", 0);
        title.AddThemeConstantOverride("shadow_outline_size", 10);
        hero.AddChild(title);
        var accent = new ColorRect
        {
            Color = UITheme.Accent,
            CustomMinimumSize = new Vector2(120.0f, 4.0f),
        };
        accent.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        hero.AddChild(accent);
        BuildLoginPanel();
        BuildMainZone();
        BuildOverlays();
        BuildChrome();
        SetContentAnchor(() => _mainZone);
        SetWheelActive(false, dimActive: false); // chrome dim 常开会压暗整个不透明欢迎页
        Wheel.Confirmed += OnMenuConfirmed;

        GameState.Instance!.Connect("LocaleChanged", _onLocaleChanged);
        RefreshTexts();
        PrefillLastLogin();

        // 初始场景入场动画：恢复 StartPanel 时代的开场淡入/错峰入场表现——
        // 登录面板先淡入，面板内容与左上品牌区逐项错峰淡入。
        UITheme.AnimateOpen(_loginPanel);
        UITheme.StaggerOpen(_loginContent);
        UITheme.StaggerOpen(hero);
    }

    // ---------------- 登录面板（左栏） ----------------

    private void BuildLoginPanel()
    {
        _loginPanel = new ChamferedPanel
        {
            CustomMinimumSize = new Vector2(520.0f, 460.0f),
            Brackets = true,
            EdgeRivets = true,
        };
        // 绝对定位（同 hero）：520×460 面板（高度贴内容，防空腔失衡）在 1080 视口内光学居中。
        // 禁用「CenterLeft 锚点 + Position」惯用法——Position 在入树前写入的是裸偏移，
        // 入树后会叠加 0.5×1080 锚点基线，把面板压到视口底缘（历史缺陷，欢迎页无视觉门禁长期未暴露）。
        _loginPanel.Position = new Vector2(140.0f, 300.0f);
        AddChild(_loginPanel);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        _loginPanel.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 14);
        margin.AddChild(content);
        _loginContent = content;

        content.AddChild(UITheme.MakeSectionHeader(Tr("WELCOME_ACCOUNT")));
        _usernameLine = MakeLineEdit(Tr("WELCOME_USERNAME"), false);
        content.AddChild(_usernameLine);
        _passwordLine = MakeLineEdit(Tr("WELCOME_PASSWORD"), true);
        content.AddChild(_passwordLine);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 12);
        content.AddChild(actions);
        var loginButton = UITheme.MakeButton(Tr("WELCOME_LOGIN"), true);
        loginButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        loginButton.Pressed += DoLogin;
        actions.AddChild(loginButton);
        var registerButton = UITheme.MakeButton(Tr("WELCOME_REGISTER"));
        registerButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        registerButton.Pressed += DoRegister;
        actions.AddChild(registerButton);

        var secondRow = new HBoxContainer();
        secondRow.AddThemeConstantOverride("separation", 12);
        content.AddChild(secondRow);
        var guestButton = UITheme.MakeButton(Tr("WELCOME_GUEST"));
        guestButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        guestButton.Pressed += ShowGuestConfirm;
        secondRow.AddChild(guestButton);
        var deleteButton = UITheme.MakeButton(Tr("WELCOME_DELETE"));
        deleteButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        deleteButton.Pressed += ShowDeleteConfirm;
        secondRow.AddChild(deleteButton);

        _msgLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.Danger, HorizontalAlignment.Left);
        _msgLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_msgLabel);

        _usernameLine.TextChanged += (_) => OpenDropdown();
        _usernameLine.FocusEntered += OpenDropdown;
        _usernameLine.FocusExited += OnUsernameFocusExited;
        _usernameLine.MaxLength = UsernameMax;
        _passwordLine.MaxLength = PasswordMax;
        // 密码框获得焦点时关闭下拉（B3）
        _passwordLine.FocusEntered += CloseDropdown;
        // Q24（2026-08-05）：输入框内按 Enter 直接提交（text_submitted）——原实现依赖
        // _unhandled_input 的 ui_accept 分派，但焦点在输入框时 Enter 被 LineEdit 消费，
        // 键盘玩家在输入框内按 Enter 永远无法登录（B7-13「输入框 = 登录路径」承诺未达成）
        _usernameLine.TextSubmitted += (_) => DoLogin();
        _passwordLine.TextSubmitted += (_) => DoLogin();
    }

    private LineEdit MakeLineEdit(string placeholder, bool secret)
    {
        var line = new LineEdit
        {
            PlaceholderText = placeholder,
            Secret = secret,
            CustomMinimumSize = new Vector2(0.0f, 52.0f),
        };
        line.AddThemeFontOverride("font", UITheme.Font);
        line.AddThemeFontSizeOverride("font_size", UITheme.FontBody);
        UITheme.ApplyMetalLineEdit(line);
        return line;
    }

    /// <summary>用户下拉：list_usernames 前 4 项（B7-13 修复：选中即关闭；点密码框/失焦关闭）</summary>
    private void OpenDropdown()
    {
        CloseDropdown();
        var names = GameState.Instance.ListUsernames();
        if (names.Count == 0)
        {
            return;
        }

        var shown = names.Slice(0, Mathf.Min(DropdownMax, names.Count));
        _dropdown = new Panel
        {
            Position = _usernameLine.GlobalPosition + new Vector2(0.0f, _usernameLine.Size.Y + 4.0f),
            Size = new Vector2(_usernameLine.Size.X, shown.Count * 44.0f + 8.0f),
            ZIndex = 50,
        };
        _dropdown.AddThemeStyleboxOverride("panel", UITheme.MakeMetalPanelStyle());
        AddChild(_dropdown);
        var list = new VBoxContainer();
        list.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        list.AddThemeConstantOverride("separation", 0);
        _dropdown.AddChild(list);
        _dropdownButtons.Clear();
        foreach (var nameV in shown)
        {
            var name = nameV;
            var b = new Button { Text = name, Alignment = HorizontalAlignment.Left };
            b.AddThemeFontOverride("font", UITheme.Font);
            b.AddThemeFontSizeOverride("font_size", UITheme.FontBody);
            UITheme.ApplyButton(b);
            b.Pressed += () => OnDropdownPick(name);
            list.AddChild(b);
            _dropdownButtons.Add(b);
        }
    }

    // 鼠标点选下拉项时视口抢焦会先触发 FocusExited，但 FocusExited 回调中读到的
    // GuiGetFocusOwner 未必已是新焦点（窗口实测仍是输入框/空），立即 CloseDropdown 会
    // QueueFree 整个下拉层，按钮收不到 release、OnDropdownPick 永不触发。改为帧末裁决：
    // 新焦点是下拉后代则保留下拉；键盘路径（Escape/回车/点密码框）由调用方直接关闭，不受影响。
    private void OnUsernameFocusExited()
    {
        _focusExitDropdown = _dropdown;
        if (_focusExitDropdown == null)
        {
            return;
        }

        CallDeferred(nameof(CloseDropdownAfterFocusChange));
    }

    private void CloseDropdownAfterFocusChange()
    {
        var dropdown = _focusExitDropdown;
        _focusExitDropdown = null;
        if (dropdown == null || _dropdown != dropdown)
        {
            return; // 裁决前下拉已被手动关闭/重建，本回调不再越权处理新下拉
        }

        var owner = GetViewport().GuiGetFocusOwner();
        if (owner != null && dropdown.IsAncestorOf(owner))
        {
            return;
        }

        CloseDropdown();
    }

    private void CloseDropdown()
    {
        if (_dropdown != null)
        {
            _dropdown.QueueFree();
            _dropdown = null;
        }

        _dropdownButtons.Clear();
    }

    private void OnDropdownPick(string name)
    {
        _usernameLine.Text = name;
        _passwordLine.Clear();
        CloseDropdown();
        _passwordLine.GrabFocus(); // B3：选中填入 → 焦点到密码框
    }

    /// <summary>最近登录用户预填（B3）：last-login 预填用户名、焦点落密码框</summary>
    private void PrefillLastLogin()
    {
        var last = GameState.Instance.GetLastLoginUser();
        if (last != "")
        {
            _usernameLine.Text = last;
            _passwordLine.GrabFocus();
        }
        else
        {
            _usernameLine.GrabFocus();
        }
    }

    private int _msgGen; // Q11：消息代次计数（防 2s 内连发消息被旧计时器回调清空）

    private void ShowMsg(string text, bool isError)
    {
        _msgLabel.Text = text;
        _msgLabel.AddThemeColorOverride("font_color", isError ? UITheme.Danger : UITheme.AccentGold);
        // 2s 自动清除（B3 对齐 120 帧）；Q11（2026-08-05）：SceneTreeTimer 无法取消、
        // 旧回调无条件清空会让连发消息被清——代次计数只让最新一代的清空生效
        _msgGen += 1;
        var gen = _msgGen;
        var timer = GetTree().CreateTimer(2.0);
        timer.Timeout += () =>
        {
            // 2026-08-09 审计：SceneTreeTimer 不随场景释放——消息后 2s 内切场景
            // （welcome→main/tutorial）回调将触碰已释放 _msgLabel，须判活
            if (gen == _msgGen && GodotObject.IsInstanceValid(_msgLabel))
            {
                _msgLabel.Text = "";
            }
        };
    }

    // ---------------- 登录/注册/游客/删除 动作 ----------------

    /// <summary>ENTER 登录路径（B7-5 修复：任一字段为空 → 游客确认框，默认焦点「返回」）</summary>
    private void DoLogin()
    {
        var name = _usernameLine.Text.Trim();
        var password = _passwordLine.Text;
        if (name == "" || password == "")
        {
            ShowGuestConfirm();
            return;
        }

        if (!GameState.Instance.VerifyUser(name, password))
        {
            ShowMsg(Tr("WELCOME_MSG_BAD_CRED"), true);
            return;
        }

        EnterMainZone(true, name);
    }

    private void DoRegister()
    {
        var name = _usernameLine.Text.Trim();
        var password = _passwordLine.Text;
        if (name == "" || password == "")
        {
            ShowMsg(Tr("WELCOME_MSG_EMPTY_CRED"), true);
            return;
        }

        if (name.Length < UserDbNameMin || name.Length > UserDbNameMax)
        {
            ShowMsg(Tr("WELCOME_MSG_NAME_LEN"), true);
            return;
        }

        if (password.Length < UserDbPasswordMin || password.Length > UserDbPasswordMax)
        {
            ShowMsg(Tr("WELCOME_MSG_PASS_LEN"), true);
            return;
        }

        if (!GameState.Instance.CreateUser(name, password))
        {
            ShowMsg(Tr("WELCOME_MSG_NAME_TAKEN"), true);
            return;
        }

        ShowMsg(Tr("WELCOME_MSG_REGISTER_OK"), false);
        _passwordLine.Clear(); // B7-9：注册成功保留刚注册的用户名，只清密码
    }

    private void ShowGuestConfirm()
    {
        _guestConfirm.Layer.Visible = true;
        _guestConfirm.Cancel.GrabFocus(); // B7-5 默认焦点「返回」
    }

    private void OnConfirmGuest()
    {
        _guestConfirm.Layer.Visible = false;
        EnterMainZone(false, "");
    }

    private void ShowDeleteConfirm()
    {
        CloseDropdown();
        var name = _usernameLine.Text.Trim();
        if (name == "")
        {
            ShowMsg(Tr("WELCOME_MSG_DELETE_EMPTY"), true);
            return;
        }

        if (_passwordLine.Text == "")
        {
            ShowMsg(Tr("WELCOME_MSG_DELETE_PASS"), true); // B7-13：确认时先验密码非空
            return;
        }

        _deleteConfirm.Layer.Visible = true;
        _deleteConfirm.Cancel.GrabFocus(); // 默认焦点「取消」
    }

    private void OnConfirmDelete()
    {
        _deleteConfirm.Layer.Visible = false;
        var name = _usernameLine.Text.Trim();
        if (GameState.Instance.DeleteUser(name, _passwordLine.Text))
        {
            _usernameLine.Clear();
            _passwordLine.Clear();
            CloseDropdown();
            ShowMsg(Tr("WELCOME_MSG_DELETED"), false);
        }
        else
        {
            ShowMsg(Tr("WELCOME_MSG_BAD_CRED"), true);
        }
    }

    /// <summary>登录/游客放行：隐藏登录面板，显示主区（B1）</summary>
    private void EnterMainZone(bool isUser, string name)
    {
        if (isUser)
        {
            GameState.Instance.LoginUser(name);
        }
        else
        {
            GameState.Instance.LoginGuest();
        }

        _stage = Stage.Main;
        _isUser = isUser;
        _loginPanel.Visible = false;
        _mainZone.Visible = true;
        // 研究所仅登录用户可用（游客无持久化档案，B7-8）——菜单重建时动态收录
        SetWheelActive(true, dimActive: false);
        RebuildMenu();
        PlayWheelEntrance();
        RaiseWheel(); // 主区面板在 chrome 之后入树：轮盘/引线/取景括弧保持其上
        RefreshTexts();
    }

    // ---------------- 主区（登录后） ----------------

    private void BuildMainZone()
    {
        // 损坏警告区：常显（不随登录态切换）——损坏存档提示是开始屏契约，登录前必须可见
        var recordsBox = new VBoxContainer();
        recordsBox.Position = new Vector2(620.0f, 130.0f);
        recordsBox.CustomMinimumSize = new Vector2(1100.0f, 0.0f);
        recordsBox.AddThemeConstantOverride("separation", 8);
        AddChild(recordsBox);
        _corruptLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.Danger, HorizontalAlignment.Left);
        _corruptLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        recordsBox.AddChild(_corruptLabel);

        // 难度区（登录放行后显示）
        _mainZone = new VBoxContainer();
        _mainZone.Position = new Vector2(620.0f, 300.0f);
        _mainZone.CustomMinimumSize = new Vector2(1100.0f, 0.0f);
        _mainZone.AddThemeConstantOverride("separation", 14);
        _mainZone.Visible = false;
        AddChild(_mainZone);

        var diffHeader = UITheme.MakeSectionHeader(Tr("START_DIFFICULTY"));
        _mainZone.AddChild(diffHeader);
        var diffRow = new HBoxContainer();
        diffRow.AddThemeConstantOverride("separation", 12);
        _mainZone.AddChild(diffRow);
        var order = GameState.Instance.DIFFICULTY_ORDER;
        foreach (var dV in order)
        {
            var d = dV;
            var b = new Button
            {
                Text = Tr("DIFF_" + d.ToString().ToUpper()),
                ToggleMode = true,
                ButtonGroup = _diffGroup,
                CustomMinimumSize = new Vector2(120.0f, 52.0f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            b.AddThemeFontOverride("font", UITheme.Font);
            b.AddThemeFontSizeOverride("font_size", UITheme.FontBody);
            UITheme.ApplyButton(b);
            b.Pressed += () => OnDifficultyPressed(d);
            diffRow.AddChild(b);
            _diffButtons[d] = b;
        }

        // 难度档位行之后：主区导航全部收口左缘圆盘（见 RebuildMenu），右栏不再放按钮列
    }

    /// <summary>圆盘主菜单（进主区/语言切换/存档态变化时重装；出击子层按存档态动态显隐「继续对局」）。</summary>
    private void RebuildMenu()
    {
        var hasSave = GameState.Instance.HasSave();
        var sortieChildren = new List<RadialWheelOption>();
        if (hasSave)
        {
            sortieChildren.Add(new RadialWheelOption { Id = "continue", Label = Tr("START_CONTINUE"), Glyph = RadialGlyph.Triangle });
        }

        sortieChildren.Add(new RadialWheelOption
        {
            Id = "new",
            Label = hasSave ? Tr("START_NEW") : Tr("START_BEGIN"),
            Glyph = RadialGlyph.Bolt,
        });
        var roots = new List<RadialWheelOption>
        {
            new() { Id = "sortie", Label = Tr("WELCOME_MENU_SORTIE"), Glyph = RadialGlyph.Triangle, Children = sortieChildren },
            new() { Id = "tutorial", Label = Tr("START_TUTORIAL"), Glyph = RadialGlyph.Diamond },
            new() { Id = "settings", Label = Tr("START_SETTINGS"), Glyph = RadialGlyph.Cross },
        };
        if (_isUser)
        {
            roots.Add(new RadialWheelOption { Id = "lab", Label = Tr("META_TITLE"), Glyph = RadialGlyph.Hex });
        }

        roots.Add(new RadialWheelOption { Id = "quit", Label = Tr("MENU_QUIT_GAME"), Glyph = RadialGlyph.Star });
        LoadMenu(roots, string.Empty);
    }

    private void OnMenuConfirmed(RadialWheelOption option)
    {
        switch (option.Id)
        {
            case "continue":
                OnContinuePressed();
                break;
            case "new":
                OnNewGamePressed();
                break;
            case "tutorial":
                OnTutorialPressed();
                break;
            case "settings":
                OnSettingsPressed();
                break;
            case "lab":
                OpenLab();
                break;
            case "quit":
                ShowExitConfirm();
                break;
        }
    }

    private void ShowExitConfirm()
    {
        _exitConfirm.Layer.Visible = true;
        _exitConfirm.Cancel.GrabFocus();
    }

    /// <summary>主按钮重获焦点（设置返回/退出确认取消后）。settings_ui.gd 经 has_method+grab_primary_focus 动态调用；
    /// 圆盘版主区无焦点控件，仅登录阶段归还输入框焦点。</summary>
    public void GrabPrimaryFocus()
    {
        if (_stage == Stage.Login)
        {
            _usernameLine.GrabFocus();
        }
    }

    private void OnDifficultyPressed(StringName d)
    {
        GameState.Instance.SetDifficulty(d);
    }

    private void OnContinuePressed()
    {
        GotoMain();
    }

    private void OnNewGamePressed()
    {
        GameState.Instance.DeleteSave();
        GotoMain();
    }

    private void OnTutorialPressed()
    {
        // E02/G03：存在进行中存档时禁入教程（UI 已禁用按钮，此处兜底）
        if (GameState.Instance.HasSave())
        {
            return;
        }

        GetTree().ChangeSceneToFile("res://scenes/tutorial.tscn");
    }

    private void OnSettingsPressed()
    {
        var settings = GetTree().GetFirstNodeInGroup("settings_ui");
        if (settings == null)
        {
            return;
        }

        Visible = false; // 面板遮挡：先隐藏自己（对齐 StartPanel 行为）
        SetWheelActive(false, dimActive: false);
        (settings as SettingsUi)?.ShowSettings(this);
    }

    private void GotoMain()
    {
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }

    // ---------------- Overlay：研究所 / 游客确认 / 删除确认 / 退出确认 ----------------

    private void BuildOverlays()
    {
        // 研究所（局外成长，2026-08-09）：科技点余额 + 升级列表；打开时重建（ResearchLab 自刷新）
        _labOverlay = new CanvasLayer { Layer = 50, Visible = false };
        AddChild(_labOverlay);
        var labShell = UITheme.MakePageShell("META_TITLE");
        _labOverlay.AddChild((Node)labShell["root"].AsGodotObject());
        ((ChamferedPanel)labShell["panel"].AsGodotObject()).CustomMinimumSize = new Vector2(620.0f, 660.0f);
        _labRows = new ResearchLab();
        _labRows.AddThemeConstantOverride("separation", 8);
        ((VBoxContainer)labShell["content"].AsGodotObject()).AddChild(_labRows);
        var labClose = UITheme.MakeButton(Tr("LEAD_CLOSE"));
        labClose.CustomMinimumSize = new Vector2(200.0f, 48.0f);
        labClose.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        labClose.Pressed += OnCloseLab;
        _labClose = labClose;
        ((VBoxContainer)labShell["content"].AsGodotObject()).AddChild(labClose);
        ((VBoxContainer)labShell["content"].AsGodotObject()).AddThemeConstantOverride("separation", 12);

        // 游客确认（B7-6：游客按钮与 ENTER 路径统一走确认框）
        _guestConfirm = MakeModal(
            "WELCOME_GUEST_CONFIRM_TITLE", "WELCOME_GUEST_CONFIRM", "WELCOME_CONFIRM_GO", "WELCOME_CONFIRM_BACK", OnConfirmGuest
        );
        // 删除确认（B7-2/3：ESC 关闭确认框、鼠标键盘双模态）
        _deleteConfirm = MakeModal(
            "WELCOME_DELETE_CONFIRM_TITLE", "WELCOME_DELETE_CONFIRM", "WELCOME_CONFIRM_YES", "WELCOME_CONFIRM_CANCEL", OnConfirmDelete
        );
        // 退出确认（welcome 是首场景，ESC=退出游戏；battle=false 保留存档）
        _exitConfirm = MakeModal("EXIT_TITLE", "WELCOME_EXIT_MSG", "EXIT_OK", "EXIT_CANCEL", OnExitOk);
    }

    /// <summary>轻量模态工厂：page_shell 风格 + 确认/取消行；返回 typed 引用结构。</summary>
    private ModalParts MakeModal(
        string titleKey, string msgKey, string okKey, string cancelKey, System.Action okCb
    )
    {
        var layer = new CanvasLayer { Layer = 60, Visible = false };
        AddChild(layer);
        var shell = UITheme.MakePageShell(titleKey);
        layer.AddChild((Node)shell["root"].AsGodotObject());
        ((ChamferedPanel)shell["panel"].AsGodotObject()).CustomMinimumSize = new Vector2(560.0f, 300.0f);
        ((VBoxContainer)shell["content"].AsGodotObject()).AddThemeConstantOverride("separation", 18);
        var msg = UITheme.MakeLabel(Tr(msgKey), UITheme.FontBody, UITheme.Text);
        msg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ((VBoxContainer)shell["content"].AsGodotObject()).AddChild(msg);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        ((VBoxContainer)shell["content"].AsGodotObject()).AddChild(row);
        var cancelButton = UITheme.MakeButton(Tr(cancelKey));
        cancelButton.CustomMinimumSize = new Vector2(200.0f, 56.0f);
        row.AddChild(cancelButton);
        var okButton = UITheme.MakeButton(Tr(okKey));
        okButton.CustomMinimumSize = new Vector2(200.0f, 56.0f);
        okButton.Pressed += () => okCb();
        row.AddChild(okButton);
        var parts = new ModalParts
        {
            Layer = layer,
            Ok = okButton,
            Cancel = cancelButton,
        };
        cancelButton.Pressed += () => CloseModalRef(parts);
        return parts;
    }

    private void CloseModalRef(ModalParts modal)
    {
        modal.Layer.Visible = false;
        // 焦点还给来源：主区主按钮 / 登录用户名框
        GrabPrimaryFocus();
    }

    private void OpenLab()
    {
        _labRows.Refresh(); // 打开时重建（余额/等级/费用即时）
        _labOverlay.Visible = true;
        _labClose.GrabFocus();
    }

    private void OnCloseLab()
    {
        _labOverlay.Visible = false;
        GrabPrimaryFocus();
    }

    private void OnExitOk()
    {
        _exitConfirm.Layer.Visible = false;
        GameState.Instance.SaveProfile(); // 登录用户设置落盘（battle=false 保留存档）
        GetTree().Quit();
    }

    // ---------------- 输入 / 文本刷新 ----------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            // Q09（2026-08-05）：设置页打开时 Esc 关闭设置页——welcome 无 BackNavigator，
            // 原实现 Esc 落到 welcome 的隐藏层 _exit_confirm（被 grab_focus 的不可见按钮），
            // 设置页永远关不掉（与 EXIT_FLOW「settings back = Esc」矛盾）
            var settings = GetTree().GetFirstNodeInGroup("settings_ui");
            if (settings is CanvasLayer settingsLayer && settingsLayer.Visible)
            {
                (settingsLayer as SettingsUi)?.Back();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_labOverlay.Visible)
            {
                OnCloseLab();
            }
            else if (_guestConfirm.Layer.Visible)
            {
                CloseModalRef(_guestConfirm);
            }
            else if (_deleteConfirm.Layer.Visible)
            {
                CloseModalRef(_deleteConfirm);
            }
            else if (_exitConfirm.Layer.Visible)
            {
                CloseModalRef(_exitConfirm);
            }
            else if (_dropdown != null)
            {
                CloseDropdown();
            }
            else if (_stage == Stage.Main)
            {
                ShowExitConfirm();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        // ENTER 分派（B7-13 修复：键盘 ENTER 按焦点分派；输入框 = 登录/游客路径，按钮自处理）
        if (_stage == Stage.Login && @event.IsActionPressed("ui_accept"))
        {
            if (_usernameLine.HasFocus() || _passwordLine.HasFocus())
            {
                DoLogin();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void RefreshTexts()
    {
        // 2026-08-10 健壮性审查：users.json 损坏同列提示（账号表被隔离重建，.corrupt 备份保留）
        var gs = GameState.Instance;
        _corruptLabel.Visible = gs.SaveCorrupt || gs.ProfileCorrupt || gs.UserDbCorrupt;
        _corruptLabel.Text = gs.UserDbCorrupt
            ? Tr("START_USERS_CORRUPT")
            : (
                gs.ProfileCorrupt && !gs.SaveCorrupt
                    ? Tr("START_PROFILE_CORRUPT")
                    : Tr("START_SAVE_CORRUPT")
            );
        if (_stage == Stage.Main && Wheel.Visible)
        {
            RebuildMenu(); // 出击子层（继续对局显隐）随存档变化重装
        }

        foreach (var pair in _diffButtons)
        {
            var d = pair.Key;
            var b = pair.Value;
            b.Text = Tr("DIFF_" + d.ToString().ToUpper());
            b.SetPressedNoSignal(GameState.Instance.Difficulty == d);
        }
    }

    public override void _ExitTree()
    {
        // U01：C22 模式配对断开（welcome 此前为全分区唯一无 _ExitTree 的常驻场景）
        var gs = GameState.Instance;
        if (gs == null)
        {
            return;
        }

        if (gs.IsConnected("LocaleChanged", _onLocaleChanged))
        {
            gs.Disconnect("LocaleChanged", _onLocaleChanged);
        }
    }

    // ---------------- 测试/诊断公开接口（A7 约定） ----------------

    public LineEdit UsernameLine() => _usernameLine;

    public Label CorruptLabel() => _corruptLabel;

    public LineEdit PasswordLine() => _passwordLine;

    public void PressLogin() => DoLogin();

    public void PressRegister() => DoRegister();

    public void PressGuest() => ShowGuestConfirm();

    public void ConfirmGuest() => OnConfirmGuest();

    public void PressDelete() => ShowDeleteConfirm();

    public void ConfirmDelete() => OnConfirmDelete();

    public void PressLab() => OpenLab(); // 局外成长：测试钩子

    public void PressNewGame() => OnNewGamePressed();

    public void PressContinue() => OnContinuePressed();

    public void PressTutorial() => OnTutorialPressed();

    public void PressSettings() => OnSettingsPressed();

    public bool MainZoneVisible() => _stage == Stage.Main;

    public CanvasLayer GuestConfirm() => _guestConfirm.Layer;

    public CanvasLayer DeleteConfirm() => _deleteConfirm.Layer;

    public CanvasLayer ExitConfirmLayer() => _exitConfirm.Layer;

}
