using Godot;
using InfiAir.Core.Combat;

namespace InfiAir;

/// <summary>
/// 遭遇事件·通讯浮层：
/// 屏幕左下角六边切角通讯框 + 打字机字幕，显示 3.5s 后淡出；
/// 不暂停游戏（process_mode 跟随本局）；新台词顶掉未播完的旧台词。
/// 强调色由事件经构造函数注入（精英炮塔＝品红、轰炸编队＝琥珀）——两个事件共用同一浮层，
/// 但各自的身份色要能一眼区分，否则「谁在说话」无从判断。
/// 打字机字间隔与停留时长只有一份（core FormationComms）：编队侧要用它们推算
/// 「战术提示最早可播时刻」（新提示顶掉仍在播的进度台词正是该组常量要防的），
/// 引擎侧再存一份副本时，只调观感就会让 core 的推算失真。</summary>
public partial class CommOverlay : CanvasLayer
{
    private const float FadeTime = 0.5f;

    // 面板几何（入场滑入与扫描线的静止基准）
    private const float PanelX = 24.0f;
    private const float PanelY = 760.0f;
    private const float PanelW = 760.0f;
    private const float PanelH = 96.0f;
    private const float ScanInset = 12.0f;

    // 入场/重入：滑入 + 受激提亮（提亮属亮度脉冲，受 ReduceFlash 约束）
    private const float EntranceFadeTime = 0.16f;
    private const float EntranceSlideTime = 0.22f;
    private const float EntranceSlidePx = 28.0f;
    private const float ReentrySlidePx = 14.0f;
    private const float ReentryTime = 0.14f;
    private const float PulsePeak = 1.6f; // self_modulate 峰值（>1 读作受激暖光）
    private const float PulseTime = 0.22f;

    // 台词停留期的慢扫描线（只在按住时循环，非亮度脉冲；ReduceFlash 下不启）
    private const float ScanSweepTime = 2.2f;
    private const float ScanAlpha = 0.10f;

    private Control _panel = null!;
    private ColorRect _scanline = null!;
    private Label _label = null!;
    private string _fullText = "";

    private float _charT;
    private int _shownChars;
    private float _holdLeft = -1.0f; // <0：打字中
    /// <summary>淡出 tween 缓存——ShowLine/Clear 必须 kill 进行中的淡出，
    /// 否则新台词恰落淡出窗口时被残留 tween 拉回 alpha=0 并 hide。</summary>
    private Tween? _fadeTween;
    private Tween? _introTween; // 入场/重入滑入与提亮，新台词重入前必须 kill
    private Tween? _scanTween; // 扫描线循环

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
            Position = new Vector2(PanelX, PanelY),
            Size = new Vector2(PanelW, PanelH),
            BgColor = UITheme.CommBgDark,
            BorderColor = new Color(accent, 0.6f),
            BracketColor = accent,
            Brackets = true,
            Visible = false,
        };
        _panel = panel;
        AddChild(panel);
        // 扫描线先于字幕入树：绘制序在底衬之上、文字之下，不遮字
        _scanline = new ColorRect
        {
            Position = new Vector2(ScanInset, ScanInset),
            Size = new Vector2(PanelW - (ScanInset * 2.0f), 2.0f),
            Color = new Color(accent, ScanAlpha),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        panel.AddChild(_scanline);
        _label = UITheme.MakeLabel("", 22, UITheme.Text, HorizontalAlignment.Left);
        _label.Position = new Vector2(20.0f, 14.0f);
        _label.CustomMinimumSize = new Vector2(720.0f, 68.0f);
        _label.Size = new Vector2(720.0f, 68.0f);
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        panel.AddChild(_label);
    }

    /// <summary>播放一句台词（翻译键）：新台词顶掉未播完的旧台词。
    /// 打字机/停留/淡出计时本身不变，仅叠加不改变时序的入场表现。</summary>
    public void ShowLine(string key)
    {
        var replacing = _panel.Visible; // 旧句仍在场：走重入表现，否则整段入场
        // 先取消进行中的淡出，避免新台词被残留 tween 拖回 alpha=0
        if (_fadeTween != null && _fadeTween.IsValid())
        {
            _fadeTween.Kill();
            _fadeTween = null;
        }

        KillIntro();
        KillScan();

        _fullText = Tr(key);
        _shownChars = 0;
        _charT = 0.0f;
        _holdLeft = -1.0f;
        _label.Text = "";
        var m = _panel.Modulate;
        m.A = replacing ? 1.0f : 0.0f;
        _panel.Modulate = m;
        _panel.Visible = true;
        PlayEnter(replacing);
        GameState.Instance.PlaySfx(SfxId.FireC);
    }

    /// <summary>入场（首次出现：滑入 + 淡入 + 受激提亮）与重入（顶掉旧句：轻推 + 提亮）。
    /// 提亮与扫描线受 ReduceFlash 约束（减弱/关闭），滑动属位移不触发频闪。</summary>
    private void PlayEnter(bool replacing)
    {
        var reduceFlash = GameState.Instance.ReduceFlash;
        _panel.Position = new Vector2(PanelX - (replacing ? ReentrySlidePx : EntranceSlidePx), PanelY);
        _panel.SelfModulate = reduceFlash ? Colors.White : new Color(PulsePeak, PulsePeak * 0.86f, PulsePeak * 0.6f);
        _introTween = _panel.CreateTween();
        _introTween.SetParallel(true);
        if (!replacing)
        {
            _introTween.TweenProperty(_panel, "modulate:a", 1.0f, EntranceFadeTime)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        _introTween.TweenProperty(_panel, "position:x", PanelX, replacing ? ReentryTime : EntranceSlideTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        if (!reduceFlash)
        {
            _introTween.TweenProperty(_panel, "self_modulate", Colors.White, PulseTime)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        if (!reduceFlash)
        {
            StartScan();
        }
    }

    /// <summary>慢扫描线：上下来回循环（tween 循环，无每帧逻辑）。</summary>
    private void StartScan()
    {
        _scanline.Visible = true;
        _scanline.Color = new Color(_accent, ScanAlpha);
        _scanTween = _scanline.CreateTween().SetLoops();
        _scanTween.TweenProperty(_scanline, "position:y", PanelH - ScanInset, ScanSweepTime)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _scanTween.TweenProperty(_scanline, "position:y", ScanInset, ScanSweepTime)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void KillIntro()
    {
        if (_introTween != null && _introTween.IsValid())
        {
            _introTween.Kill();
        }

        _introTween = null;
    }

    private void KillScan()
    {
        if (_scanTween != null && _scanTween.IsValid())
        {
            _scanTween.Kill();
        }

        _scanTween = null;
        _scanline.Visible = false;
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

        KillIntro();
        KillScan();
        // 复位入场残留（半途被打断时面板可能停在滑入位/提亮态）
        _panel.Position = new Vector2(PanelX, PanelY);
        _panel.SelfModulate = Colors.White;
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
            while (_charT >= FormationComms.CharInterval && _shownChars < _fullText.Length)
            {
                _charT -= FormationComms.CharInterval;
                _shownChars += 1;
            }

            if (_shownChars != prev)
            {
                _label.Text = _fullText.Substring(0, _shownChars);
            }

            if (_shownChars >= _fullText.Length)
            {
                _holdLeft = FormationComms.HoldTime;
            }
        }
        else
        {
            _holdLeft -= d;
            if (_holdLeft <= 0.0f)
            {
                // 进入淡出段（复用同一计时；FADE_TIME+1.0 余量防淡出期间本分支重入——
                // 实际视觉 = FormationComms.HoldTime 停留 + 0.5s fade，与 ELITE_TURRET_EVENT 文档「3.5s then fade」一致）
                _holdLeft = FadeTime + 1.0f;
                // 淡出期间停掉扫描线/复位提亮，避免残影里还有元素在动
                KillIntro();
                KillScan();
                _panel.SelfModulate = Colors.White;
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
