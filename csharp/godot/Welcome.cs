using Godot;

namespace InfiAir;

/// <summary>
/// welcome 主场景（2026-08-04 账户系统 T3；规格 PORTING_PARITY 附录 B B1-B3/B6/B7 去 bug 清单）。
/// 登录阶段：左栏账号面板（注册/登录/游客/删除 + 下拉）+ 右栏难度/教程/设置/排行榜/最高分；
/// 登录/游客放行后切主区：继续对局（有档）/ 开始游戏 + 新游戏。进入 main 后由 main 依存档自动继续。
/// ESC 层级（对齐 B3/B7-1/2/3）：关排行榜 → 关游客/删除确认 → 关下拉 → 退出确认（welcome 是首场景）。
/// M5 全量迁移（2026-08-08 自 scripts/welcome.gd）：CanvasLayer 子类，挂 scenes/welcome.tscn。
/// 迁移期：UITheme/ChamferedPanel 为 C# 类 typed 直调；GameState/UserDB 经 GameStateBridge + 脚本资源访问。
/// </summary>
public partial class Welcome : CanvasLayer
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
    private static readonly int UserDbNameMin = ReadUserDbConst("NAME_MIN");
    private static readonly int UserDbNameMax = ReadUserDbConst("NAME_MAX");
    private static readonly int UserDbPasswordMin = ReadUserDbConst("PASSWORD_MIN");
    private static readonly int UserDbPasswordMax = ReadUserDbConst("PASSWORD_MAX");

    private Stage _stage = Stage.Login;
    private ColorRect _dim = null!;
    private ChamferedPanel _loginPanel = null!;
    private LineEdit _usernameLine = null!;
    private LineEdit _passwordLine = null!;
    private Label _msgLabel = null!;
    private Panel? _dropdown;
    private readonly System.Collections.Generic.List<Button> _dropdownButtons = new();
    private VBoxContainer _mainZone = null!;
    private Button _continueButton = null!;
    private Button _newButton = null!;
    private Button _tutorialButton = null!;
    private Button _leaderboardButton = null!;
    private Button _settingsButton = null!;
    private readonly Godot.Collections.Dictionary _diffButtons = new();
    private readonly ButtonGroup _diffGroup = new();
    private Label _highScoreLabel = null!;
    private Label _boardLabel = null!;
    private Label _corruptLabel = null!;
    private CanvasLayer _leaderboardOverlay = null!;
    private Button _leaderboardClose = null!; // Q21：排行榜关闭按钮（打开时 grab_focus）
    private VBoxContainer _leaderboardRows = null!;
    // 模态结构：{"layer": CanvasLayer, "ok": Button, "cancel": Button}
    private Godot.Collections.Dictionary _guestConfirm = new();
    private Godot.Collections.Dictionary _deleteConfirm = new();
    private Godot.Collections.Dictionary _exitConfirm = new();

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
        hero.AddChild(title);
        var accent = new ColorRect
        {
            Color = UITheme.Accent,
            CustomMinimumSize = new Vector2(120.0f, 4.0f),
        };
        accent.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        hero.AddChild(accent);
        _highScoreLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.AccentGold, HorizontalAlignment.Left);
        hero.AddChild(_highScoreLabel);
        _boardLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left);
        hero.AddChild(_boardLabel);
        _corruptLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.Danger, HorizontalAlignment.Left);
        hero.AddChild(_corruptLabel);

        BuildLoginPanel();
        BuildMainZone();
        BuildOverlays();
        BuildEscHint();

        GameStateBridge.Instance!.Connect("locale_changed", Callable.From(RefreshTexts));
        RefreshTexts();
        PrefillLastLogin();
    }

    // ---------------- 登录面板（左栏） ----------------

    private void BuildLoginPanel()
    {
        _loginPanel = new ChamferedPanel
        {
            CustomMinimumSize = new Vector2(520.0f, 560.0f),
            Brackets = true,
        };
        _loginPanel.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
        _loginPanel.Position = new Vector2(140.0f, -20.0f);
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
        _usernameLine.FocusExited += CloseDropdown;
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
        return line;
    }

    /// <summary>用户下拉：list_usernames 前 4 项（B7-13 修复：选中即关闭；点密码框/失焦关闭）</summary>
    private void OpenDropdown()
    {
        CloseDropdown();
        var names = GameStateBridge.Call("list_usernames").AsGodotArray();
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
        AddChild(_dropdown);
        var list = new VBoxContainer();
        list.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        list.AddThemeConstantOverride("separation", 0);
        _dropdown.AddChild(list);
        _dropdownButtons.Clear();
        foreach (var nameV in shown)
        {
            var name = nameV.AsString();
            var b = new Button { Text = name, Alignment = HorizontalAlignment.Left };
            b.AddThemeFontOverride("font", UITheme.Font);
            b.AddThemeFontSizeOverride("font_size", UITheme.FontBody);
            UITheme.ApplyButton(b);
            b.Pressed += () => OnDropdownPick(name);
            list.AddChild(b);
            _dropdownButtons.Add(b);
        }
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
        var last = GameStateBridge.Call("get_last_login_user").AsString();
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
            if (gen == _msgGen)
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

        if (!GameStateBridge.Call("verify_user", name, password).AsBool())
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

        if (!GameStateBridge.Call("create_user", name, password).AsBool())
        {
            ShowMsg(Tr("WELCOME_MSG_NAME_TAKEN"), true);
            return;
        }

        ShowMsg(Tr("WELCOME_MSG_REGISTER_OK"), false);
        _passwordLine.Clear(); // B7-9：注册成功保留刚注册的用户名，只清密码
    }

    private void ShowGuestConfirm()
    {
        ((CanvasLayer)_guestConfirm["layer"].AsGodotObject()).Visible = true;
        ((Button)_guestConfirm["cancel"].AsGodotObject()).GrabFocus(); // B7-5 默认焦点「返回」
    }

    private void OnConfirmGuest()
    {
        ((CanvasLayer)_guestConfirm["layer"].AsGodotObject()).Visible = false;
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

        ((CanvasLayer)_deleteConfirm["layer"].AsGodotObject()).Visible = true;
        ((Button)_deleteConfirm["cancel"].AsGodotObject()).GrabFocus(); // 默认焦点「取消」
    }

    private void OnConfirmDelete()
    {
        ((CanvasLayer)_deleteConfirm["layer"].AsGodotObject()).Visible = false;
        var name = _usernameLine.Text.Trim();
        if (GameStateBridge.Call("delete_user", name, _passwordLine.Text).AsBool())
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
            GameStateBridge.Call("login_user", name);
        }
        else
        {
            GameStateBridge.Call("login_guest");
        }

        _stage = Stage.Main;
        _loginPanel.Visible = false;
        _mainZone.Visible = true;
        RefreshTexts();
        GrabMainFocus();
    }

    // ---------------- 主区（登录后） ----------------

    private void BuildMainZone()
    {
        _mainZone = new VBoxContainer();
        _mainZone.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
        _mainZone.Position = new Vector2(140.0f, 260.0f);
        _mainZone.CustomMinimumSize = new Vector2(520.0f, 0.0f);
        _mainZone.AddThemeConstantOverride("separation", 14);
        _mainZone.Visible = false;
        AddChild(_mainZone);

        var diffHeader = UITheme.MakeSectionHeader(Tr("START_DIFFICULTY"));
        _mainZone.AddChild(diffHeader);
        var diffRow = new HBoxContainer();
        diffRow.AddThemeConstantOverride("separation", 12);
        _mainZone.AddChild(diffRow);
        var order = GameStateBridge.Get("DIFFICULTY_ORDER").AsGodotArray();
        foreach (var dV in order)
        {
            var d = dV.AsStringName();
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
            _diffButtons[d] = Variant.From(b);
        }

        _continueButton = UITheme.MakeButton(Tr("START_CONTINUE"), true);
        _continueButton.CustomMinimumSize = new Vector2(0.0f, 64.0f);
        _continueButton.Pressed += OnContinuePressed;
        _mainZone.AddChild(_continueButton);
        _newButton = UITheme.MakeButton(Tr("START_BEGIN"));
        _newButton.CustomMinimumSize = new Vector2(0.0f, 56.0f);
        _newButton.Pressed += OnNewGamePressed;
        _mainZone.AddChild(_newButton);
        _tutorialButton = UITheme.MakeButton(Tr("START_TUTORIAL"));
        _tutorialButton.CustomMinimumSize = new Vector2(0.0f, 56.0f);
        _tutorialButton.Pressed += OnTutorialPressed;
        _mainZone.AddChild(_tutorialButton);
        _settingsButton = UITheme.MakeButton(Tr("START_SETTINGS"));
        _settingsButton.CustomMinimumSize = new Vector2(0.0f, 56.0f);
        _settingsButton.Pressed += OnSettingsPressed;
        _mainZone.AddChild(_settingsButton);
        _leaderboardButton = UITheme.MakeButton(Tr("WELCOME_LEADERBOARD"));
        _leaderboardButton.CustomMinimumSize = new Vector2(0.0f, 56.0f);
        _leaderboardButton.Pressed += OpenLeaderboard;
        _mainZone.AddChild(_leaderboardButton);
    }

    private void GrabMainFocus()
    {
        if (_continueButton.Visible)
        {
            _continueButton.GrabFocus();
        }
        else
        {
            _newButton.GrabFocus();
        }
    }

    /// <summary>主按钮重获焦点（设置返回/退出确认取消后）。settings_ui.gd 经 has_method+grab_primary_focus 动态调用。</summary>
    public void GrabPrimaryFocus()
    {
        if (_stage == Stage.Main)
        {
            GrabMainFocus();
        }
        else
        {
            _usernameLine.GrabFocus();
        }
    }

    private void OnDifficultyPressed(StringName d)
    {
        GameStateBridge.Call("set_difficulty", d);
    }

    private void OnContinuePressed()
    {
        GotoMain();
    }

    private void OnNewGamePressed()
    {
        GameStateBridge.Call("delete_save");
        GotoMain();
    }

    private void OnTutorialPressed()
    {
        // E02/G03：存在进行中存档时禁入教程（UI 已禁用按钮，此处兜底）
        if (GameStateBridge.Call("has_save").AsBool())
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
        settings.Call("show_settings", this);
    }

    private void GotoMain()
    {
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }

    // ---------------- Overlay：排行榜 / 游客确认 / 删除确认 / 退出确认 ----------------

    private void BuildOverlays()
    {
        // 排行榜（B6）：遮罩 + 520×580 面板 + 最多 10 行 + 页脚 + ×关闭；打开时重新读取（B7-13）
        _leaderboardOverlay = new CanvasLayer { Layer = 50, Visible = false };
        AddChild(_leaderboardOverlay);
        var shell = UITheme.MakePageShell("LEAD_TITLE");
        _leaderboardOverlay.AddChild((Node)shell["root"].AsGodotObject());
        ((ChamferedPanel)shell["panel"].AsGodotObject()).CustomMinimumSize = new Vector2(520.0f, 580.0f);
        _leaderboardRows = new VBoxContainer();
        _leaderboardRows.AddThemeConstantOverride("separation", 6);
        ((VBoxContainer)shell["content"].AsGodotObject()).AddChild(_leaderboardRows);
        var footer = UITheme.MakeLabel(Tr("LEAD_FOOTER"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Center);
        ((VBoxContainer)shell["content"].AsGodotObject()).AddChild(footer);
        var closeButton = UITheme.MakeButton(Tr("LEAD_CLOSE"));
        closeButton.CustomMinimumSize = new Vector2(200.0f, 48.0f);
        closeButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        closeButton.Pressed += OnCloseLeaderboard;
        _leaderboardClose = closeButton;
        ((VBoxContainer)shell["content"].AsGodotObject()).AddChild(closeButton);
        ((VBoxContainer)shell["content"].AsGodotObject()).AddThemeConstantOverride("separation", 12);

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

    /// <summary>轻量模态工厂：page_shell 风格 + 确认/取消行；返回 {"layer", "ok", "cancel"} 引用结构</summary>
    private Godot.Collections.Dictionary MakeModal(
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
        var result = new Godot.Collections.Dictionary
        {
            ["layer"] = Variant.From(layer),
            ["ok"] = Variant.From(okButton),
            ["cancel"] = Variant.From(cancelButton),
        };
        cancelButton.Pressed += () => CloseModalRef(result);
        return result;
    }

    private void CloseModalRef(Godot.Collections.Dictionary modal)
    {
        ((CanvasLayer)modal["layer"].AsGodotObject()).Visible = false;
        // 焦点还给来源：主区主按钮 / 登录用户名框
        GrabPrimaryFocus();
    }

    private void OpenLeaderboard()
    {
        // B7-13 修复：overlay 每次打开重新读取榜单（不作 10s 缓存）
        foreach (var child in _leaderboardRows.GetChildren())
        {
            child.QueueFree();
        }

        var board = GameStateBridge.Call("get_leaderboard").AsGodotArray();
        if (board.Count == 0)
        {
            _leaderboardRows.AddChild(
                UITheme.MakeLabel(Tr("LEAD_EMPTY"), UITheme.FontBody, UITheme.TextDim, HorizontalAlignment.Center)
            );
        }

        // Q20（2026-08-05）：条目级判型——手改 users.json 的非 Dictionary 条目/字符串 score 跳过
        // （原实现 as Dictionary 直接解引用崩溃、字符串 score 静默转 0）
        var rowIdx = 0;
        foreach (var entry in board)
        {
            if (rowIdx >= 10)
            {
                break;
            }

            if (entry.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            var dict = entry.AsGodotDictionary();
            var score = dict.GetValueOrDefault("score", Variant.From(0));
            if (score.VariantType != Variant.Type.Int && score.VariantType != Variant.Type.Float)
            {
                continue;
            }

            var color = UITheme.Text;
            if (rowIdx == 0)
            {
                color = UITheme.AccentGold;
            }
            else if (rowIdx == 1)
            {
                color = UITheme.Accent;
            }
            else if (rowIdx == 2)
            {
                color = UITheme.TextDim;
            }

            var line = UITheme.MakeLabel(
                $"{rowIdx + 1}. {dict.GetValueOrDefault("player_name", "").AsString()}  {score.AsInt32()}",
                UITheme.FontBody,
                color,
                HorizontalAlignment.Left
            );
            _leaderboardRows.AddChild(line);
            rowIdx += 1;
        }

        _leaderboardOverlay.Visible = true;
        _leaderboardClose.GrabFocus(); // Q21：模态打开聚焦关闭按钮（原无 grab_focus，焦点停留被遮挡按钮，Enter 重复打开）
    }

    private void OnCloseLeaderboard()
    {
        _leaderboardOverlay.Visible = false;
        GrabPrimaryFocus();
    }

    private void OnExitOk()
    {
        ((CanvasLayer)_exitConfirm["layer"].AsGodotObject()).Visible = false;
        GameStateBridge.Call("save_profile"); // 登录用户设置落盘（battle=false 保留存档）
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
                settingsLayer.Call("back");
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_leaderboardOverlay.Visible)
            {
                CloseLeaderboard();
            }
            else if (((CanvasLayer)_guestConfirm["layer"].AsGodotObject()).Visible)
            {
                CloseModalRef(_guestConfirm);
            }
            else if (((CanvasLayer)_deleteConfirm["layer"].AsGodotObject()).Visible)
            {
                CloseModalRef(_deleteConfirm);
            }
            else if (((CanvasLayer)_exitConfirm["layer"].AsGodotObject()).Visible)
            {
                CloseModalRef(_exitConfirm);
            }
            else if (_dropdown != null)
            {
                CloseDropdown();
            }
            else
            {
                ((CanvasLayer)_exitConfirm["layer"].AsGodotObject()).Visible = true;
                ((Button)_exitConfirm["cancel"].AsGodotObject()).GrabFocus();
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
        var hasSave = GameStateBridge.Call("has_save").AsBool();
        _highScoreLabel.Visible = GameStateBridge.Get("high_score").AsInt32() > 0;
        _highScoreLabel.Text = Tr("WELCOME_HIGH_SCORE").Replace("%d", GameStateBridge.Get("high_score").AsInt32().ToString());
        var board = GameStateBridge.Call("highscores_text", 3).AsString();
        _boardLabel.Visible = board != "";
        _boardLabel.Text = Tr("START_BOARD") + "\n" + board;
        _corruptLabel.Visible = GameStateBridge.Get("save_corrupt").AsBool() || GameStateBridge.Get("profile_corrupt").AsBool();
        _corruptLabel.Text = (
            GameStateBridge.Get("profile_corrupt").AsBool() && !GameStateBridge.Get("save_corrupt").AsBool()
                ? Tr("START_PROFILE_CORRUPT")
                : Tr("START_SAVE_CORRUPT")
        );
        _continueButton.Visible = _stage == Stage.Main && hasSave;
        _newButton.Text = hasSave ? Tr("START_NEW") : Tr("START_BEGIN");
        if (_stage == Stage.Main)
        {
            // 主按钮层级：有存档=继续对局 primary；无存档=开始游戏 primary
            if (hasSave)
            {
                UITheme.ApplyPrimaryButton(_continueButton);
                UITheme.ApplyButton(_newButton);
                _newButton.AddThemeFontSizeOverride("font_size", UITheme.FontBody);
            }
            else
            {
                UITheme.ApplyPrimaryButton(_newButton);
            }
        }

        // E02/G03 + P1-6：进行中存档时禁用教程按钮（重进会删档）；已通关无存档时放行
        _tutorialButton.Disabled = _stage == Stage.Main && hasSave;
        foreach (var key in _diffButtons.Keys)
        {
            var d = key.AsStringName();
            var b = (Button)_diffButtons[key].AsGodotObject();
            b.Text = Tr("DIFF_" + d.ToString().ToUpper());
            b.SetPressedNoSignal(GameStateBridge.Get("difficulty").AsStringName() == d);
        }
    }

    private void BuildEscHint()
    {
        var escHint = UITheme.MakeLabel(
            Tr("START_ESC_HINT") + "    " + Tr("WELCOME_TAB_HINT"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Right
        );
        escHint.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        escHint.Position = new Vector2(-420.0f, -50.0f);
        escHint.CustomMinimumSize = new Vector2(360.0f, 0.0f);
        AddChild(escHint);
        GameStateBridge.Instance!.Connect("locale_changed", Callable.From(() =>
        {
            escHint.Text = Tr("START_ESC_HINT") + "    " + Tr("WELCOME_TAB_HINT");
        }));
    }

    private static int ReadUserDbConst(string name)
    {
        var script = GD.Load<GDScript>("res://scripts/user_db.gd");
        return script.GetScriptConstantMap()[name].AsInt32();
    }

    // ---------------- 测试/诊断公开接口（A7 约定） ----------------

    public LineEdit UsernameLine() => _usernameLine;

    public Label CorruptLabel() => _corruptLabel;

    public Button ContinueButton() => _continueButton;

    public Button NewButton() => _newButton;

    public Button TutorialButton() => _tutorialButton;

    public LineEdit PasswordLine() => _passwordLine;

    public void PressLogin() => DoLogin();

    public void PressRegister() => DoRegister();

    public void PressGuest() => ShowGuestConfirm();

    public void ConfirmGuest() => OnConfirmGuest();

    public void PressDelete() => ShowDeleteConfirm();

    public void ConfirmDelete() => OnConfirmDelete();

    public void PressLeaderboard() => OpenLeaderboard();

    public void CloseLeaderboard() => OnCloseLeaderboard();

    public void PressNewGame() => OnNewGamePressed();

    public void PressContinue() => OnContinuePressed();

    public void PressTutorial() => OnTutorialPressed();

    public void PressSettings() => OnSettingsPressed();

    public bool MainZoneVisible() => _stage == Stage.Main;

    public CanvasLayer LeaderboardOverlay() => _leaderboardOverlay;

    public CanvasLayer GuestConfirm() => (CanvasLayer)_guestConfirm["layer"].AsGodotObject();

    public CanvasLayer DeleteConfirm() => (CanvasLayer)_deleteConfirm["layer"].AsGodotObject();

    public CanvasLayer ExitConfirmLayer() => (CanvasLayer)_exitConfirm["layer"].AsGodotObject();

    // ---------------- GDScript 鸭子调用兼容桥（M5 过渡，M7 删除） ----------------
    // 调用方：test/welcome_flow_test.gd、test/startup_flow_test.gd（A7 白盒断言）、
    // scripts/settings_ui.gd（grab_primary_focus 经 has_method 探测）。

    public LineEdit username_line() => UsernameLine();

    public Label corrupt_label() => CorruptLabel();

    public Button continue_button() => ContinueButton();

    public Button new_button() => NewButton();

    public Button tutorial_button() => TutorialButton();

    public LineEdit password_line() => PasswordLine();

    public void press_login() => DoLogin();

    public void press_register() => DoRegister();

    public void press_guest() => ShowGuestConfirm();

    public void confirm_guest() => ConfirmGuest();

    public void press_delete() => ShowDeleteConfirm();

    public void confirm_delete() => ConfirmDelete();

    public void press_leaderboard() => OpenLeaderboard();

    public void close_leaderboard() => CloseLeaderboard();

    public void press_new_game() => OnNewGamePressed();

    public void press_continue() => OnContinuePressed();

    public void press_tutorial() => OnTutorialPressed();

    public void press_settings() => OnSettingsPressed();

    public bool main_zone_visible() => MainZoneVisible();

    public CanvasLayer leaderboard_overlay() => LeaderboardOverlay();

    public CanvasLayer guest_confirm() => GuestConfirm();

    public CanvasLayer delete_confirm() => DeleteConfirm();

    public CanvasLayer exit_confirm_layer() => ExitConfirmLayer();

    public void grab_primary_focus() => GrabPrimaryFocus();
}
