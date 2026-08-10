using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 死亡结算面板：DISPLAY 级大分数 + 新纪录标记 + 击杀统计，按 R 重开。
/// M5 全量迁移（2026-08-08 自 scripts/game_over_ui.gd）。
/// </summary>
public partial class GameOverUi : CanvasLayer
{
    private Label _scoreLabel = null!;
    private Label _scoreTagLabel = null!;
    private Label _statsLabel = null!;
    private Label _recordLabel = null!;
    private Label _rankLabel = null!;
    private Label _boardTitleLabel = null!;
    private Label _boardLabel = null!;
    private Label _titleLabel = null!;
    private Label _hintLabel = null!;
    private ChamferedPanel _plate = null!;
    private ColorRect _dim = null!;
    private VBoxContainer _content = null!;
    private int _lastRank;

    private readonly Callable _onPlayerDied;
    private readonly Callable _onLocaleChanged;

    public GameOverUi()
    {
        _onPlayerDied = Callable.From(OnPlayerDied);
        _onLocaleChanged = Callable.From(OnLocaleChanged);
    }

    public override void _Ready()
    {
        Visible = false;
        var shell = UITheme.MakePageShell("GO_TITLE");
        AddChild((Node)shell["root"].AsGodotObject());
        _dim = (ColorRect)shell["dim"].AsGodotObject();
        _plate = (ChamferedPanel)shell["panel"].AsGodotObject();
        _plate.CustomMinimumSize = new Vector2(640.0f, 600.0f);
        _titleLabel = (Label)shell["title"].AsGodotObject();
        _content = (VBoxContainer)shell["content"].AsGodotObject();

        // 大分数：DISPLAY 级金色数字 + 小 caption 标签
        _scoreTagLabel = UITheme.MakeLabel(Tr("UI_SCORE_TAG"), UITheme.FontCaption, UITheme.TextDim);
        _content.AddChild(_scoreTagLabel);
        _scoreLabel = UITheme.MakeLabel("0", UITheme.FontDisplay, UITheme.AccentGold);
        _content.AddChild(_scoreLabel);

        _recordLabel = UITheme.MakeLabel(Tr("GO_RECORD"), UITheme.FontHeader, UITheme.AccentGold);
        _recordLabel.Visible = false;
        _content.AddChild(_recordLabel);

        _statsLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.Text);
        _content.AddChild(_statsLabel);

        // P0-3 本地排行榜：本局名次 + 历史最佳 Top5
        _rankLabel = UITheme.MakeLabel("", UITheme.FontHeader, UITheme.AccentGold);
        _rankLabel.Visible = false;
        _content.AddChild(_rankLabel);
        _boardTitleLabel = UITheme.MakeLabel(Tr("GO_BOARD"), UITheme.FontCaption, UITheme.TextDim);
        _content.AddChild(_boardTitleLabel);
        _boardLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.Text);
        _content.AddChild(_boardLabel);

        _hintLabel = UITheme.MakeLabel(Tr("GO_RESTART"), UITheme.FontCaption, UITheme.TextDim);
        _content.AddChild(_hintLabel);

        var gs = GameState.Instance;
        if (gs != null)
        {
            // 2026-08-10 健壮性审查：C22 IsConnected 守卫（对齐 PauseUi/Hud）——未走
            // _ExitTree 的重入树路径会重复订阅，结算回调双跑（SettleRun 双执行 → 重复上榜/结算）
            if (!gs.IsConnected("PlayerDied", _onPlayerDied))
            {
                gs.Connect("PlayerDied", _onPlayerDied);
            }

            if (!gs.IsConnected("LocaleChanged", _onLocaleChanged))
            {
                gs.Connect("LocaleChanged", _onLocaleChanged);
            }
        }
    }

    public override void _ExitTree()
    {
        // C22：显式断开 GameState 信号连接（C# Connect 连接不随接收方释放自动断开）
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (gs.IsConnected("PlayerDied", _onPlayerDied))
            {
                gs.Disconnect("PlayerDied", _onPlayerDied);
            }

            if (gs.IsConnected("LocaleChanged", _onLocaleChanged))
            {
                gs.Disconnect("LocaleChanged", _onLocaleChanged);
            }
        }
    }

    private void OnLocaleChanged()
    {
        _titleLabel.Text = Tr("GO_TITLE");
        _scoreTagLabel.Text = Tr("UI_SCORE_TAG");
        _recordLabel.Text = Tr("GO_RECORD");
        _rankLabel.Text = GdFormat.Format(Tr("GO_RANK"), _lastRank);
        _boardTitleLabel.Text = Tr("GO_BOARD");
        _hintLabel.Text = Tr("GO_RESTART");
        // 2026-08-03 审计：去掉 if visible 恒假包裹（死亡态无语言切换入口），刷新不可见文本无害
        _statsLabel.Text = GdFormat.Format(
            Tr("GO_BEST") + "\n" + Tr("GO_KILLS") + "\n" + Tr("GO_BOSS_KILLS"),
            GameState.Instance.HighScore,
            GameState.Instance.Kills,
            GameState.Instance.BossKills);
        _boardLabel.Text = GameState.Instance.HighscoresText(5);
    }

    private void OnPlayerDied()
    {
        // 2026-08-09 Y 系列：结算编排下沉 GameState.SettleRun（原子链 + 快照）；
        // UI 表现（文本/SFX/面板）留本层，PlayerDied 订阅者角色不变
        var (newRecord, rank) = GameState.Instance.SettleRun();
        _lastRank = rank;
        _scoreLabel.Text = GameState.Instance.Score.ToString();
        _statsLabel.Text = GdFormat.Format(
            Tr("GO_BEST") + "\n" + Tr("GO_KILLS") + "\n" + Tr("GO_BOSS_KILLS"),
            GameState.Instance.HighScore,
            GameState.Instance.Kills,
            GameState.Instance.BossKills);
        _rankLabel.Text = GdFormat.Format(Tr("GO_RANK"), _lastRank);
        _rankLabel.Visible = _lastRank > 0;
        _boardLabel.Text = GameState.Instance.HighscoresText(5);
        _recordLabel.Visible = newRecord;
        if (newRecord)
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_BUFF_PICK);
        }

        GetTree().Paused = true;
        Visible = true;
        UITheme.AnimateModalOpen(_dim, _plate, _content);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event.IsActionPressed("restart"))
        {
            GetTree().Paused = false;
            GameState.Instance.ResetRun();
            GetTree().ReloadCurrentScene();
        }
    }
}
