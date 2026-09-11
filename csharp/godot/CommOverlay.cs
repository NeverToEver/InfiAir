using Godot;

namespace InfiAir;

/// <summary>
/// 遭遇事件·通讯浮层：
/// 屏幕左下角六边切角通讯框 + 打字机字幕，显示 3.5s 后淡出；
/// 不暂停游戏（process_mode 跟随本局）；新台词顶掉未播完的旧台词。
/// 强调色由事件经构造函数注入（精英炮塔＝品红、轰炸编队＝琥珀）——两个事件共用同一浮层，
/// 但各自的身份色要能一眼区分，否则「谁在说话」无从判断。</summary>
public partial class CommOverlay : CanvasLayer
{
    private const float CharInterval = 0.03f; // 打字机字间隔
    private const float HoldTime = 3.5f;
    private const float FadeTime = 0.5f;

    private Control _panel = null!;
    private Label _label = null!;
    private string _fullText = "";

    private float _charT;
    private int _shownChars;
    private float _holdLeft = -1.0f; // <0：打字中
    /// <summary>淡出 tween 缓存——ShowLine/Clear 必须 kill 进行中的淡出，
    /// 否则新台词恰落淡出窗口时被残留 tween 拉回 alpha=0 并 hide。</summary>
    private Tween? _fadeTween;

    private readonly Color _accent;

    /// <summary>构造函数注入强调色（默认品红＝精英炮塔旧观感，旧调用点语义不变）。</summary>
    public CommOverlay()
        : this(UITheme.EventMagenta)
    {
    }

    public CommOverlay(Color accent)
    {
        _accent = accent;
        Layer = 12;
        var panel = new ChamferedPanel
        {
            Position = new Vector2(24.0f, 760.0f),
            Size = new Vector2(760.0f, 96.0f),
            BgColor = UITheme.CommBgDark,
            BorderColor = new Color(accent, 0.6f),
            BracketColor = accent,
            Brackets = true,
            Visible = false,
        };
        _panel = panel;
        AddChild(panel);
        _label = UITheme.MakeLabel("", 22, UITheme.Text, HorizontalAlignment.Left);
        _label.Position = new Vector2(20.0f, 14.0f);
        _label.CustomMinimumSize = new Vector2(720.0f, 68.0f);
        _label.Size = new Vector2(720.0f, 68.0f);
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        panel.AddChild(_label);
    }

    /// <summary>播放一句台词（翻译键）：新台词顶掉未播完的旧台词。</summary>
    public void ShowLine(string key)
    {
        // 先取消进行中的淡出，避免新台词被残留 tween 拖回 alpha=0
        if (_fadeTween != null && _fadeTween.IsValid())
        {
            _fadeTween.Kill();
            _fadeTween = null;
        }

        _fullText = Tr(key);
        _shownChars = 0;
        _charT = 0.0f;
        _holdLeft = -1.0f;
        _label.Text = "";
        var m = _panel.Modulate;
        m.A = 1.0f;
        _panel.Modulate = m;
        _panel.Visible = true;
        GameState.Instance.PlaySfx(SfxId.FireC);
    }

    /// <summary>清空当前台词并隐藏（返航打断事件时调用，避免恢复本局后台词残留）。</summary>
    public void Clear()
    {
        // 取消进行中的淡出，防止 Clear 后 alpha 残留改变
        if (_fadeTween != null && _fadeTween.IsValid())
        {
            _fadeTween.Kill();
            _fadeTween = null;
        }

        _fullText = "";
        _shownChars = 0;
        _charT = 0.0f;
        _holdLeft = -1.0f;
        _label.Text = "";
        _panel.Visible = false;
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        if (!_panel.Visible)
        {
            return;
        }

        if (_holdLeft < 0.0f)
        {
            // 打字机（字符数未变时不 set_text，避免逐帧字形 shaping）
            var prev = _shownChars;
            _charT += d;
            while (_charT >= CharInterval && _shownChars < _fullText.Length)
            {
                _charT -= CharInterval;
                _shownChars += 1;
            }

            if (_shownChars != prev)
            {
                _label.Text = _fullText.Substring(0, _shownChars);
            }

            if (_shownChars >= _fullText.Length)
            {
                _holdLeft = HoldTime;
            }
        }
        else
        {
            _holdLeft -= d;
            if (_holdLeft <= 0.0f)
            {
                // 进入淡出段（复用同一计时；FADE_TIME+1.0 余量防淡出期间本分支重入——
                // 实际视觉 = HOLD_TIME 3.5s hold + 0.5s fade，与 ELITE_TURRET_EVENT 文档「3.5s then fade」一致）
                _holdLeft = FadeTime + 1.0f;
                _fadeTween = CreateTween();
                _fadeTween.TweenProperty(_panel, "modulate:a", 0.0f, FadeTime);
                _fadeTween.TweenCallback(Callable.From(() =>
                {
                    _fadeTween = null;
                    _panel.Hide();
                }));
            }
        }
    }

}
