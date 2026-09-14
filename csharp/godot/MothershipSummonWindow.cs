using System.Collections.Generic;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 母舰召唤·机库小窗（左侧竖长画中画通讯屏）：蓄力完成后由 Main 召唤流程弹出。
/// 时轴（不暂停本局，process_mode 跟随树）：
///   [0, OPEN_TIME)                    面板淡入
///   镜头 1 SHOT_DURATIONS[0]          机库剖面：3 条充能管线依次断开（MS_SEQ_CHARGE）
///   镜头 2 SHOT_DURATIONS[1]          两侧维护机械臂解除链接收回（MS_SEQ_ARMS）
///   镜头 3 SHOT_DURATIONS[2]          母舰弹射出仓 + 穿梭器蓝环点亮（MS_SEQ_LAUNCH）
///   收尾 CLOSE_TIME                   面板淡出，finished 信号并自销毁
/// 数值取 balance.json effects.mothership_summon.window，脚本默认值须保持一致。
/// Skip() 幂等：立即发 Finished（供流程直推）。
/// CanvasLayer 子类；结束信号为 [Signal] Finished。
/// </summary>
public partial class MothershipSummonWindow : CanvasLayer
{
    /// <summary>小窗演出结束（Main 连接：开穿梭门 + 母舰穿出）。</summary>
    [Signal]
    public delegate void FinishedEventHandler();

    private static readonly Color Cyan = new(1.0f, 0.78f, 0.34f);

    private static readonly Color WarpBlue = new Color(1.000f, 0.771f, 0.450f);

    private static readonly Vector2 PanelSize = new(560.0f, 840.0f); // 左侧竖长画中画

    private static readonly Vector2 ShipHome = new(280.0f, 380.0f); // 面板局部坐标

    private static readonly string[] ShotKeys = { "MS_SEQ_CHARGE", "MS_SEQ_ARMS", "MS_SEQ_LAUNCH" };

    /// <summary>相位轨节点短标签（三字）：完整描述由字幕给，轨上只需认得「走到哪」。</summary>
    private static readonly string[] PhaseRailKeys = { "MS_RAIL_CHARGE", "MS_RAIL_RELEASE", "MS_RAIL_LAUNCH" };

    /// <summary>母舰贴图：C# 静态字段禁止持有
    /// Godot 对象（退出 segfault 实测根因），改 GD.Load（命中资源缓存，仅构建期一次）。</summary>
    private static Texture2D ShipTexture => GD.Load<Texture2D>("res://assets/sprites/mothership.png");

    private float OpenTime = 0.25f;

    private float CloseTime = 0.25f;

    private float[] _shotDurations = { 0.8f, 0.6f, 0.7f };

    private float _t;

    private float _total;

    private int _shotIdx = -1;

    private bool _done;

    private ChamferedPanel _panel = null!;

    private Node2D _stage = null!; // 面板局部坐标舞台（960×520）

    private Sprite2D _ship = null!;

    private Line2D _shipTrail = null!;

    private Sprite2D _shipGlow = null!; // 弹射拖首软光（镜头 3 启用）

    private Line2D _warpRing = null!;

    private ColorRect _flash = null!;

    private Label _subtitle = null!;

    // ---------------- 实况屏铬件（抬头栏 / 相位轨 / 扫描线） ----------------

    /// <summary>抬头栏右端实况标记（闪烁琥珀点 + 已播秒数），把面板读作「通讯实况」而非静态贴图。</summary>
    private Label _feedLabel = null!;

    /// <summary>相位轨三联节点（镜头索引 → 节点），随推进点亮/锁定，玩家看得出流程走到哪一步。</summary>
    private readonly List<Polygon2D> _phaseNodes = new();

    private readonly List<Label> _phaseLabels = new();

    /// <summary>相位节点当前点亮态缓存（-1 = 未初始化），只在变化时改色，免逐帧写属性。</summary>
    private int _phaseLit = -1;

    /// <summary>扫描带（纵向缓移的横向光带，机库纵深信号的常驻动效）。</summary>
    private ColorRect _scanBand = null!;

    private long _feedStartMs;

    private readonly List<Line2D> _chargeLines = new();

    private readonly List<Sprite2D> _chargeSparks = new(); // 断开点软火花

    private readonly List<GpuParticles2D> _sparkBursts = new(); // 断开瞬间一次性喷发

    private readonly bool[] _burstFired = new bool[3];

    private readonly List<Line2D> _arms = new(); // [左臂, 右臂]，各 3 点（基座/关节/末端）

    private readonly List<Vector2[]> _chargeLineOrigins = new(); // 充能管线构建期原始端点 [anchor, tip]

    private readonly List<Vector2[]> _armOrigins = new(); // 机械臂构建期原始端点 [base, joint, tip]

    public override void _Ready()
    {
        Layer = 24; // 世界层与 HUD 之上、基地 UI（25）之下（与 OrbitalStrike 同层）
        // open/close 时长钳下限——0 时 t/OpenTime 除零
        // 经 Clamp 兜底无 NaN，但面板淡入淡出退化为瞬现/瞬隐，_total 计算也失真
        OpenTime = Mathf.Max((float)GameState.Instance.Cfg("effects.mothership_summon.window.open_time", OpenTime).AsDouble(), 0.001f);
        CloseTime = Mathf.Max((float)GameState.Instance.Cfg("effects.mothership_summon.window.close_time", CloseTime).AsDouble(), 0.001f);
        // shot_durations 判型/判长回退——短数组/非数组时用默认，防 _ready 崩溃
        var durs = GameState.Instance.Cfg("effects.mothership_summon.window.shot_durations", Variant.From(_shotDurations));
        if (durs.VariantType == Variant.Type.Array && durs.AsGodotArray().Count >= 3)
        {
            var arr = durs.AsGodotArray();
            // 元素钳下限——0/负值经 st 循环整体吞掉，镜头段时序失真
            _shotDurations = new[]
            {
                Mathf.Max((float)arr[0].AsDouble(), 0.001f),
                Mathf.Max((float)arr[1].AsDouble(), 0.001f),
                Mathf.Max((float)arr[2].AsDouble(), 0.001f),
            };
        }
        else
        {
            _shotDurations = new[] { 0.8f, 0.6f, 0.7f };
        }

        _total = OpenTime + _shotDurations[0] + _shotDurations[1] + _shotDurations[2] + CloseTime;
        BuildPanel();
        BuildHangar();
        BuildScanlines(); // 必须在机库之后：叠加层要盖在不透明机库底色之上
        Update(0.0f);
    }

    public void Skip()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        EmitSignal(SignalName.Finished);
        QueueFree();
    }

    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        _t += (float)delta;
        if (_t >= _total)
        {
            Skip();
            return;
        }

        Update(_t, (float)delta);
    }

    private void BuildPanel()
    {
        _panel = new ChamferedPanel();
        // 左侧竖长：贴左缘垂直居中（1920×1080 设计坐标）
        _panel.Position = new Vector2(24.0f, (1080.0f - PanelSize.Y) * 0.5f);
        _panel.Size = PanelSize;
        _panel.BgColor = new Color(0.038f, 0.032f, 0.026f, 0.92f);
        _panel.BorderColor = new Color(UITheme.Accent, 0.7f);
        _panel.BracketColor = UITheme.Accent;
        _panel.Brackets = true;
        AddChild(_panel);
        _stage = new Node2D();
        _panel.AddChild(_stage);
        // 抬头标题 + 实况标记（右端）
        var title = UITheme.MakeLabel((string)Tr("MS_SEQ_TITLE"), UITheme.FontSmall, UITheme.TextDim, HorizontalAlignment.Left);
        title.Position = new Vector2(20.0f, 10.0f);
        _panel.AddChild(title);
        _feedLabel = UITheme.MakeLabel("", UITheme.FontSmall, UITheme.Accent, HorizontalAlignment.Right);
        _feedLabel.Position = new Vector2(PanelSize.X - 200.0f, 10.0f);
        _feedLabel.Size = new Vector2(180.0f, 0.0f);
        _feedLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.AddChild(_feedLabel);
        // 字幕
        _subtitle = UITheme.MakeLabel("", UITheme.FontHudL, UITheme.Text, HorizontalAlignment.Center);
        _subtitle.Position = new Vector2(24.0f, PanelSize.Y - 58.0f);
        _subtitle.Size = new Vector2(PanelSize.X - 48.0f, 40.0f);
        _panel.AddChild(_subtitle);
        BuildPhaseRail();
    }

    /// <summary>相位轨：三节点 + 连接线，标出「充能管线 → 释放机械臂 → 弹射出仓」的推进位置。
    /// 节点用菱形（与全站 chip 菱形灯语一致），已过/当前点亮、未到暗置。
    /// 节点标签用三字短键（完整描述由字幕承担）——长句在三节点间距里必然重叠（实测「弹射出击 ·
    /// 穿梭器启动」压住中段标签）。</summary>
    private void BuildPhaseRail()
    {
        var xs = new[] { 120.0f, 280.0f, 440.0f };
        const float RailY = 730.0f;
        // 连接底线（贯穿三节点的暗轨）
        var track = new Line2D
        {
            Width = 2.0f,
            DefaultColor = new Color(UITheme.Accent, 0.22f),
            Points = new[] { new Vector2(xs[0], RailY), new Vector2(xs[2], RailY) },
        };
        _panel.AddChild(track);
        for (var i = 0; i < xs.Length; i++)
        {
            var node = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(0.0f, -7.0f),
                    new Vector2(7.0f, 0.0f),
                    new Vector2(0.0f, 7.0f),
                    new Vector2(-7.0f, 0.0f),
                },
                Position = new Vector2(xs[i], RailY),
                Color = new Color(UITheme.Accent, 0.18f),
            };
            _panel.AddChild(node);
            _phaseNodes.Add(node);
            var label = UITheme.MakeLabel((string)Tr(PhaseRailKeys[i]), UITheme.FontSmall, UITheme.TextDim, HorizontalAlignment.Center);
            label.Position = new Vector2(xs[i] - 60.0f, RailY + 12.0f);
            label.Size = new Vector2(120.0f, 0.0f);
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            _panel.AddChild(label);
            _phaseLabels.Add(label);
        }
    }

    /// <summary>扫描线叠加：机库区每 4px 一条 1px 暖线 + 一条纵向缓移亮带（实况屏质感；
    /// 单节点自绘，1 draw call）。挂 _panel 而非 _stage，且须在机库底色之后构建——
    /// 挂 _stage 会被后建的不透明机库底完全盖住。</summary>
    private void BuildScanlines()
    {
        var overlay = new SummonScanlines
        {
            Position = new Vector2(14.0f, 40.0f),
            Size = PanelSize - new Vector2(28.0f, 110.0f),
        };
        _panel.AddChild(overlay);
        // 插到 _stage 之后、文字/相位轨之前：扫描线只叠机库画面，不扫过标题与相位轨
        _panel.MoveChild(overlay, 1);
        _scanBand = new ColorRect
        {
            Color = new Color(UITheme.Accent, 0.055f),
            Position = new Vector2(14.0f, 40.0f),
            Size = new Vector2(PanelSize.X - 28.0f, 46.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddChild(_scanBand);
        _panel.MoveChild(_scanBand, 2);
    }

    /// <summary>机库剖面：深色内场 + 顶部滑轨 + 母舰剪影 + 充能管线 ×3 + 维护臂 ×2（竖向纵深构图）</summary>
    private void BuildHangar()
    {
        var bg = new ColorRect
        {
            Color = new Color(0.028f, 0.024f, 0.020f, 1.0f),
            Position = new Vector2(14.0f, 40.0f),
            Size = PanelSize - new Vector2(28.0f, 110.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _stage.AddChild(bg);
        // 后景龙门架剪影（深色结构层，增加纵深）
        var gantryColor = new Color(0.058f, 0.050f, 0.042f, 1.0f);
        foreach (var gx in new[] { 110.0f, 280.0f, 450.0f })
        {
            var strut = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(gx - 10.0f, 70.0f),
                    new Vector2(gx + 10.0f, 70.0f),
                    new Vector2(gx + 16.0f, 700.0f),
                    new Vector2(gx - 16.0f, 700.0f),
                },
                Color = gantryColor,
            };
            _stage.AddChild(strut);
        }

        var crossBeam = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(40.0f, 250.0f),
                new Vector2(520.0f, 250.0f),
                new Vector2(520.0f, 262.0f),
                new Vector2(40.0f, 262.0f),
            },
            Color = gantryColor,
        };
        _stage.AddChild(crossBeam);
        // 顶部滑轨（充能管线挂载点）
        foreach (var y in new[] { 58.0f, 66.0f })
        {
            var rail = new Line2D
            {
                Width = 2.0f,
                DefaultColor = new Color(UITheme.Accent, 0.25f),
                Points = new[] { new Vector2(40.0f, y), new Vector2(PanelSize.X - 40.0f, y) },
            };
            _stage.AddChild(rail);
        }

        // 顶灯 ×5（软光点 + 淡光锥，沿滑轨排布）
        foreach (var lx in new[] { 90.0f, 190.0f, 290.0f, 390.0f, 490.0f })
        {
            var lamp = CinematicFx.SoftGlow(13.0f, new Color(1.0f, 0.85f, 0.55f, 0.55f));
            lamp.Position = new Vector2(lx, 62.0f);
            _stage.AddChild(lamp);
            var cone = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(lx - 8.0f, 68.0f),
                    new Vector2(lx + 8.0f, 68.0f),
                    new Vector2(lx + 42.0f, 240.0f),
                    new Vector2(lx - 42.0f, 240.0f),
                },
                Color = new Color(1.0f, 0.9f, 0.6f, 0.045f),
            };
            _stage.AddChild(cone);
        }

        // 机库地线
        var floorLine = new Line2D
        {
            Width = 2.0f,
            DefaultColor = new Color(UITheme.Accent, 0.2f),
            Points = new[] { new Vector2(40.0f, 700.0f), new Vector2(PanelSize.X - 40.0f, 700.0f) },
        };
        _stage.AddChild(floorLine);
        // 地面警示条纹（黄/暗相间小段，沿地线排布）
        for (var si = 0; si < 13; si++)
        {
            var sx = 44.0f + 36.0f * si;
            var stripe = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(sx, 704.0f),
                    new Vector2(sx + 28.0f, 704.0f),
                    new Vector2(sx + 24.0f, 712.0f),
                    new Vector2(sx - 4.0f, 712.0f),
                },
                Color = si % 2 == 0 ? new Color(0.85f, 0.7f, 0.15f, 0.4f) : new Color(0.05f, 0.07f, 0.12f, 0.9f),
            };
            _stage.AddChild(stripe);
        }

        // 充能管线 ×3（顶部 → 舰体挂点）+ 断开软火花与一次性喷发
        var attach = new[] { new Vector2(-35.0f, -42.0f), new Vector2(0.0f, -48.0f), new Vector2(35.0f, -42.0f) };
        for (var i = 0; i < 3; i++)
        {
            var anchor = new Vector2(180.0f + 100.0f * i, 62.0f);
            var line = new Line2D
            {
                Width = 3.0f,
                DefaultColor = Cyan,
                Points = new[] { anchor, ShipHome + attach[i] },
            };
            _stage.AddChild(line);
            _chargeLines.Add(line);
            _chargeLineOrigins.Add(new[] { anchor, ShipHome + attach[i] });
            var spark = CinematicFx.SoftGlow(16.0f, new Color(Cyan, 0.0f));
            spark.Position = ShipHome + attach[i];
            _stage.AddChild(spark);
            _chargeSparks.Add(spark);
            var burst = CinematicFx.Particles(new Godot.Collections.Dictionary
            {
                ["amount"] = 14,
                ["lifetime"] = 0.45,
                ["explosiveness"] = 1.0,
                ["one_shot"] = true,
                ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
                ["spread"] = 70.0,
                ["vel_min"] = 40.0,
                ["vel_max"] = 130.0,
                ["scale_min"] = 1.5,
                ["scale_max"] = 3.5,
                ["color"] = new Color(1.000f, 0.875f, 0.700f, 0.9f),
            });
            burst.Position = ShipHome + attach[i];
            burst.Emitting = false;
            _stage.AddChild(burst);
            _sparkBursts.Add(burst);
        }

        // 维护机械臂 ×2（基座/关节/末端三点折线，末端初始咬合舰体两侧）
        var armDefs = new[]
        {
            new[] { new Vector2(60.0f, 700.0f), new Vector2(140.0f, 600.0f), ShipHome + new Vector2(-68.0f, 22.0f) },
            new[] { new Vector2(500.0f, 700.0f), new Vector2(420.0f, 600.0f), ShipHome + new Vector2(68.0f, 22.0f) },
        };
        foreach (var pts in armDefs)
        {
            var arm = new Line2D
            {
                Width = 6.0f,
                DefaultColor = new Color(0.750f, 0.667f, 0.550f),
                Points = pts,
            };
            _stage.AddChild(arm);
            _arms.Add(arm);
            _armOrigins.Add(pts);
        }

        // 母舰剪影（缩略比例：机库全景中的小舰体）
        _ship = new Sprite2D { Texture = ShipTexture, Scale = new Vector2(0.32f, 0.32f), Position = ShipHome };
        _stage.AddChild(_ship);
        // 弹射拖尾（镜头 3 启用）
        _shipTrail = new Line2D { Width = 6.0f };
        var grad = new Gradient();
        grad.SetColor(0, new Color(WarpBlue, 0.0f));
        grad.SetColor(1, new Color(WarpBlue, 0.8f));
        _shipTrail.Gradient = grad;
        _shipTrail.Points = new Vector2[] { Vector2.Zero, Vector2.Zero }; // 预分配，帧内只写元素
        _stage.AddChild(_shipTrail);
        // 拖首软光（镜头 3 随弹射点亮，贴在舰尾）
        _shipGlow = CinematicFx.SoftGlow(30.0f, new Color(WarpBlue, 0.0f));
        _shipGlow.Position = ShipHome + new Vector2(0.0f, 26.0f);
        _stage.AddChild(_shipGlow);
        // 穿梭器光环（镜头 3 点亮）
        _warpRing = new Line2D
        {
            Width = 3.0f,
            Closed = true,
            DefaultColor = new Color(WarpBlue, 0.0f),
            Points = CirclePoints(1.0f, 48), // 预建单位点集，帧内仅写 scale
        };
        _stage.AddChild(_warpRing);
        // 镜头 3 起步白闪
        _flash = new ColorRect
        {
            Color = new Color(1.0f, 1.0f, 1.0f, 0.0f),
            Position = new Vector2(14.0f, 40.0f),
            Size = PanelSize - new Vector2(28.0f, 110.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _stage.AddChild(_flash);
    }

    /// <summary>进度驱动全部视觉（t 秒）</summary>
    private void Update(float t) => Update(t, 0.0f);

    private void Update(float t, float delta)
    {
        // 面板淡入/淡出
        var fadeIn = Mathf.Clamp(t / OpenTime, 0.0f, 1.0f);
        var fadeOut = Mathf.Clamp((_total - t) / CloseTime, 0.0f, 1.0f);
        var panelMod = _panel.Modulate;
        panelMod.A = Mathf.Min(fadeIn, fadeOut);
        _panel.Modulate = panelMod;
        // 镜头定位
        var st = t - OpenTime;
        var idx = 0;
        while (idx < _shotDurations.Length && st >= _shotDurations[idx])
        {
            st -= _shotDurations[idx];
            idx += 1;
        }

        if (idx >= _shotDurations.Length)
        {
            // 收尾淡出段：保持末帧画面，但实况标记/扫描带继续推进（否则面板在收尾里冻住）
            UpdateChrome(t, _shotDurations.Length - 1);
            return;
        }

        if (idx != _shotIdx)
        {
            _shotIdx = idx;
            _subtitle.Text = (string)Tr(ShotKeys[idx]);
            if (idx == 2)
            {
                GameState.Instance.PlaySfx(SfxId.Dash, -4.0, 0.6);
                _flash.Color = new Color(WarpBlue, 0.55f);
                // 弹射起步冲击环（出仓点，一次性自毁）
                var launchSw = CinematicFx.Shockwave(new Godot.Collections.Dictionary
                {
                    ["radius"] = 170.0,
                    ["time"] = 0.55,
                    ["color"] = new Color(WarpBlue, 0.5f),
                    ["core_color"] = new Color(1.000f, 0.938f, 0.850f, 0.9f),
                    ["width"] = 9.0,
                });
                launchSw.Position = ShipHome;
                _stage.AddChild(launchSw);
            }
        }

        var p = Mathf.Clamp(st / _shotDurations[idx], 0.0f, 1.0f);
        switch (idx)
        {
            case 0:
                UpdateChargeLines(p);
                break;
            case 1:
                UpdateArms(p);
                break;
            case 2:
                UpdateLaunch(p);
                break;
        }

        UpdateChrome(t, idx);

        var flashMod = _flash.Color;
        flashMod.A = Mathf.Max(flashMod.A - 2.2f * delta, 0.0f);
        _flash.Color = flashMod;
    }

    /// <summary>镜头 1：3 条充能管线依次断开（挂点回缩 + 软火花闪灭 + 断开瞬间喷发）</summary>
    private void UpdateChargeLines(float p)
    {
        for (var i = 0; i < 3; i++)
        {
            var at = 0.15f + 0.3f * i;
            var lp = Mathf.Clamp((p - at) / 0.25f, 0.0f, 1.0f);
            var line = _chargeLines[i];
            // 插值基准取构建期原始端点——points[1] 逐帧被改写，读当前值会累积失真（帧率相关）
            var orig = _chargeLineOrigins[i];
            var anchor = orig[0];
            var tip = orig[1];
            line.SetPointPosition(1, tip.Lerp(anchor, lp * lp));
            line.DefaultColor = new Color(Cyan, 1.0f - 0.6f * lp);
            var spark = _chargeSparks[i];
            spark.Modulate = lp > 0.0f ? new Color(Cyan, 0.9f * Mathf.Sin(Mathf.Pi * lp)) : new Color(Cyan, 0.0f);
            if (lp > 0.0f && !_burstFired[i])
            {
                _burstFired[i] = true;
                _sparkBursts[i].Restart();
            }
        }
    }

    /// <summary>镜头 2：维护臂解除链接，末端+关节同步收回基座</summary>
    private void UpdateArms(float p)
    {
        var e = 1.0f - (1.0f - p) * (1.0f - p); // 缓出（ease-out）
        for (var i = 0; i < _arms.Count; i++)
        {
            var arm = _arms[i];
            // 插值基准取构建期原始端点——points[1]/points[2] 逐帧被改写，读当前值会累积失真
            var orig = _armOrigins[i];
            var basePos = orig[0];
            var joint = orig[1];
            var tip = orig[2];
            arm.SetPointPosition(2, tip.Lerp(basePos, e));
            arm.SetPointPosition(1, joint.Lerp(basePos, e * 0.6f));
            arm.DefaultColor = new Color(0.750f, 0.667f, 0.550f, 1.0f - 0.7f * e);
        }
    }

    /// <summary>镜头 3：母舰加速弹出（向上出仓）+ 穿梭器蓝环扩散 + 拖首软光</summary>
    private void UpdateLaunch(float p)
    {
        var e = p * p; // ease-in 加速
        _ship.Position = ShipHome + new Vector2(0.0f, -560.0f * e);
        // 点集已预分配，经 set_point_position 原地写（points[i]= 是值语义副本不生效）
        _shipTrail.SetPointPosition(0, _ship.Position + new Vector2(0.0f, 26.0f));
        _shipTrail.SetPointPosition(1, _ship.Position + new Vector2(0.0f, 26.0f + 220.0f * e));
        _shipTrail.DefaultColor = new Color(WarpBlue, 0.8f * p);
        _shipGlow.Position = _ship.Position + new Vector2(0.0f, 26.0f);
        _shipGlow.Modulate = new Color(WarpBlue, 0.75f * p);
        _shipGlow.Scale = Vector2.One * (30.0f / 32.0f) * (0.6f + 0.9f * e);
        var ringP = Mathf.Clamp(p / 0.6f, 0.0f, 1.0f);
        LayoutWarpRing(Mathf.Lerp(20.0f, 200.0f, ringP));
        _warpRing.Position = _ship.Position;
        _warpRing.DefaultColor = new Color(WarpBlue, 0.9f * (1.0f - ringP));
    }

    private Vector2[] CirclePoints(float radius, int count)
    {
        var pts = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var a = Mathf.Tau * i / count;
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        return pts;
    }

    /// <summary>原地写穿梭器环点集（预分配数组 + set_point_position，零分配、线宽不随 scale 变）</summary>
    private void LayoutWarpRing(float radius)
    {
        for (var i = 0; i < 48; i++)
        {
            var a = Mathf.Tau * i / 48.0f;
            _warpRing.SetPointPosition(i, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    /// <summary>实况标记与相位轨刷新：抬头右端秒数 + 闪烁点，相位节点按当前镜头点亮。
    /// 只在节点状态变化时改色（每帧写属性会打断 tween 且徒增开销）。</summary>
    private void UpdateChrome(float t, int idx)
    {
        var secs = (long)t;
        _feedLabel.Text = GdFormat.Format(Tr("MS_SEQ_FEED_FMT"), secs);
        // 实况点闪烁：与相位推进解耦的常驻低频脉动（ReduceFlash 下静止不闪）。
        // 下限抬到 0.62——原 0.55/0.45 摆到谷底几乎读不出文字（实测看不清「实况」标记）。
        _feedLabel.Modulate = new Color(1.0f, 1.0f, 1.0f,
            GameState.Instance.ReduceFlash ? 1.0f : 0.82f + 0.18f * Enemy.SinFast(t * 3.4f));

        // 扫描带缓移（机库纵深信号）：三角波上下往复，ReduceFlash 下停在中位
        var bandRange = PanelSize.Y - 150.0f - 46.0f;
        var bandY = GameState.Instance.ReduceFlash
            ? 40.0f + bandRange * 0.5f
            : 40.0f + bandRange * (0.5f - 0.5f * Mathf.Cos(t * 0.55f));
        _scanBand.Position = new Vector2(_scanBand.Position.X, bandY);

        if (idx == _phaseLit)
        {
            return;
        }

        _phaseLit = idx;
        for (var i = 0; i < _phaseNodes.Count; i++)
        {
            // 已完成/当前：实心琥珀；当前额外更亮；未到：暗空心
            var reached = i <= idx;
            _phaseNodes[i].Color = !reached
                ? new Color(UITheme.Accent, 0.18f)
                : (i == idx ? UITheme.AccentHot : new Color(UITheme.Accent, 0.75f));
            _phaseLabels[i].AddThemeColorOverride("font_color", reached ? UITheme.Text : UITheme.TextDim);
        }
    }

}

/// <summary>召唤小窗扫描线叠加层：每 4px 一条 1px 暖线，单节点自绘 1 draw call。</summary>
public partial class SummonScanlines : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var y = 2.0f;
        while (y < Size.Y)
        {
            DrawLine(new Vector2(0.0f, y), new Vector2(Size.X, y), UITheme.PhantomScan, 1.0f);
            y += 4.0f;
        }
    }
}
