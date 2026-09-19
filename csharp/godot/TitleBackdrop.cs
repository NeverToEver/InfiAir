using Godot;

namespace InfiAir;

/// <summary>
/// 标题屏·深空纵深背景（纯视觉装饰，不参与任何输入与判定）：**远景行星 / 残骸带 / 尘埃近景 /
/// 航道浮标**四层，速度按纵深递增（行星近乎静止 → 残骸与浮标中速 → 尘埃最快）。
///
/// 为什么另起一层而不复用 `DeepSpaceBackdrop`（正局的战斗背景）：语境不同——正局那层挂在世界层、
/// 吃相机 zoom 与玩家横移微视差、行星位姿由固定表定死；标题屏是 1:1 画布，构图要让开左侧标题区
/// 与右侧悬挂展示位，行星还要每次进场随机换位。**视觉语言与做法沿用同一套**（速度按纵深递减、
/// 出区回绕、热路径原地写数组零分配、贴图实例级缓存），但布局与取值各成一份，
/// 不做「一套参数勉强伺候两屏」。
///
/// 随机**只在进场掷一次**（行星选哪张天体贴图与哪个构图位、残骸与浮标的数量与位姿），
/// 掷完即定：逐帧只推进位置，没有逐帧随机——标题屏停着看时，背景不该自己抖。
/// 出场顺序：<see cref="TitleScreen._Ready"/> 在星空之后、远景战场之前建本层（ZIndex = -6：
/// 星空 -10 之上、远景战场与机体展示 0 之下）。
/// </summary>
public partial class TitleBackdrop : Node2D
{
    /// <summary>行星回绕带余量：带高 = 区域高 + 2×余量（大于行星半径，出入画无突现）。</summary>
    private const float PlanetBandMargin = 620.0f;

    /// <summary>残骸回绕带余量（同正局口径：带比可见区高，避免在视口边缘突现）。</summary>
    private const float DebrisBandMargin = 300.0f;

    private static readonly string[] PlanetTextures =
    {
        "res://assets/sprites/backdrop/planet_1.png",
        "res://assets/sprites/backdrop/planet_2.png",
    };

    private static readonly string[] DebrisTextures =
    {
        "res://assets/sprites/backdrop/debris_1.png",
        "res://assets/sprites/backdrop/debris_2.png",
        "res://assets/sprites/backdrop/debris_3.png",
    };

    /// <summary>行星构图位（相对区域宽高的中心位）：中心一律落在画框外，只让天体露一段弧。
    /// 三个位都在右半幅或左下角——左侧标题区（约 x 140..740、y 330..660）里不出现天体本体，
    /// 标题文字的对比度因此不由随机数决定。</summary>
    private static readonly Vector2[] PlanetAnchors =
    {
        new(0.98f, 0.04f), // 右上角外
        new(1.02f, 0.72f), // 右缘外·中下
        new(0.02f, 1.00f), // 左下角外
    };

    /// <summary>可见区域（CanvasLayer 下 1:1 画布取全视口；不得硬编码 1920×1080）。</summary>
    private Vector2 _areaSize = new(1920.0f, 1080.0f);
    private Vector2 _origin = Vector2.Zero;

    // ---- 行星 ----
    private Sprite2D? _planet;
    private Vector2 _planetRel;
    private float _planetSpeed = 2.6f;
    private float _planetSpin;
    private float _planetScroll;
    private float _planetAngle;

    // ---- 残骸带 ----
    private Sprite2D?[] _debris = System.Array.Empty<Sprite2D?>();
    private float[] _debrisSpeed = System.Array.Empty<float>();
    private float[] _debrisDriftX = System.Array.Empty<float>();
    private float[] _debrisSpin = System.Array.Empty<float>();

    // ---- 尘埃近景（软点合批绘制）----
    private Texture2D? _dustTex;
    private Vector2[] _dust = System.Array.Empty<Vector2>();
    private float[] _dustSize = System.Array.Empty<float>();
    private float[] _dustSpeed = System.Array.Empty<float>();
    private float[] _dustAlpha = System.Array.Empty<float>();

    // ---- 航道浮标（成对软点：芯 + 外晕，错相闪）----
    private Node2D?[] _buoys = System.Array.Empty<Node2D?>();
    private float[] _buoySpeed = System.Array.Empty<float>();

    public override void _Ready()
    {
        ZIndex = -6;
        var view = GameState.Instance.GetViewport().GetVisibleRect();
        _areaSize = view.Size;
        _origin = view.Position;

        BuildPlanet();
        BuildDebris();
        BuildDust();
        BuildBuoys();
    }

    /// <summary>残骸与浮标用的取位：天空安全区（表与理由见 <see cref="SkyZones"/>）。</summary>
    private Vector2 RandomZoneSpot() => TitleScreen.RandomSkySpot() + _origin;

    /// <summary>远景行星：随机贴图 + 随机构图位，极慢下漂 + 极慢自转，重度压暗（大气透视）。
    /// 贴图缺失即整层跳过——少一层背景，不该让标题屏开不了机。</summary>
    private void BuildPlanet()
    {
        var tex = GD.Load<Texture2D>(PlanetTextures[GD.RandRange(0, PlanetTextures.Length - 1)]);
        if (tex == null)
        {
            return;
        }

        _planet = new Sprite2D
        {
            Texture = tex,
            Scale = Vector2.One * (float)GD.RandRange(0.55, 0.95),
            // 大气透视压暗 + 冷调：远景天体的职责是给出纵深，不是抢标题与机体的视线
            // （取值实测口径：0.32–0.46 下天体的弧线在标题屏的暗底上读得出，仍低于标题辉光的亮度）
            Modulate = new Color(0.50f, 0.49f, 0.50f, (float)GD.RandRange(0.32, 0.46)),
            Rotation = (float)GD.RandRange(0.0, Mathf.Tau),
        };
        var anchor = PlanetAnchors[GD.RandRange(0, PlanetAnchors.Length - 1)];
        _planetRel = anchor + new Vector2((float)GD.RandRange(-0.03, 0.03), (float)GD.RandRange(-0.02, 0.02));
        _planetSpeed = (float)GD.RandRange(1.8, 3.4);
        _planetSpin = (float)GD.RandRange(0.004, 0.011) * (GD.Randf() < 0.5f ? -1.0f : 1.0f);
        _planetAngle = _planet.Rotation;
        AddChild(_planet);
        PlacePlanet();
    }

    private void PlacePlanet()
    {
        if (_planet == null)
        {
            return;
        }

        var band = _areaSize.Y + PlanetBandMargin * 2.0f;
        _planet.Position = new Vector2(
            _origin.X + _planetRel.X * _areaSize.X,
            _origin.Y - PlanetBandMargin + Mathf.PosMod(_planetRel.Y * band + _planetScroll, band));
        _planet.Rotation = _planetAngle;
    }

    /// <summary>残骸带：中速下漂 + 慢自旋 + 微横漂，出带回绕（横漂出界同基线回绕）。</summary>
    private void BuildDebris()
    {
        var count = GD.RandRange(7, 11);
        var band = _areaSize.Y + DebrisBandMargin * 2.0f;
        _debris = new Sprite2D?[count];
        _debrisSpeed = new float[count];
        _debrisDriftX = new float[count];
        _debrisSpin = new float[count];
        for (var i = 0; i < count; i++)
        {
            var tex = GD.Load<Texture2D>(DebrisTextures[GD.RandRange(0, DebrisTextures.Length - 1)]);
            if (tex == null)
            {
                continue;
            }

            var spot = RandomZoneSpot();
            var s = new Sprite2D
            {
                Texture = tex,
                // 比正局的残骸小一档（贴图原生 300–480px）：安全区最窄处只有 170px 高，
                // 大块头压到标题文字上就不是「纵深」了。这一层要的是碎屑的密度，不是单块的体量
                Scale = Vector2.One * (float)GD.RandRange(0.22, 0.45),
                Rotation = (float)GD.RandRange(0.0, Mathf.Tau),
                // 冷调压暗 + 低透明度：残骸是「别处的战场遗物」，不许比远景战场更抢眼
                Modulate = new Color(0.46f, 0.48f, 0.54f, (float)GD.RandRange(0.16, 0.30)),
                Position = new Vector2(
                    spot.X,
                    _origin.Y - DebrisBandMargin + Mathf.PosMod(spot.Y - _origin.Y + DebrisBandMargin, band)),
            };
            _debrisSpeed[i] = (float)GD.RandRange(9.0, 20.0);
            _debrisDriftX[i] = (float)GD.RandRange(-5.0, 5.0);
            _debrisSpin[i] = (float)GD.RandRange(-0.06, 0.06);
            _debris[i] = s;
            AddChild(s);
        }
    }

    /// <summary>
    /// 尘埃近景：最快的下漂层（纵深最近）+ 随机粒径与透明度，_Draw 合批绘制。
    /// 这一层不避让安全区——单颗只有 2–6px、最亮 alpha 0.11，压在标题文字上也只是空气感；
    /// 把它挡在安全区外反而会让「近景」这层在画面上缺一块。
    /// </summary>
    private void BuildDust()
    {
        _dustTex = CinematicFx.SoftTexture();
        var count = GD.RandRange(30, 46);
        _dust = new Vector2[count];
        _dustSize = new float[count];
        _dustSpeed = new float[count];
        _dustAlpha = new float[count];
        for (var i = 0; i < count; i++)
        {
            _dust[i] = new Vector2(
                _origin.X + (float)GD.RandRange(0.0, _areaSize.X),
                _origin.Y + (float)GD.RandRange(0.0, _areaSize.Y));
            _dustSize[i] = (float)GD.RandRange(2.0, 6.0);
            _dustSpeed[i] = (float)GD.RandRange(55.0, 120.0);
            _dustAlpha[i] = (float)GD.RandRange(0.05, 0.11);
        }
    }

    /// <summary>
    /// 航道浮标：成对软点（小芯 + 外晕）随纵深慢慢漂，各自错相闪。
    /// 闪法是「一短亮 + 一长暗」的航行灯节奏而不是周期正弦：等周期闪烁读起来像故障，
    /// 短亮长暗才读得出「这是一排灯」。减少闪光时改成恒亮——灯还在，只是不闪
    /// （同悬挂展示的喷口怠速微闪口径）。
    /// </summary>
    private void BuildBuoys()
    {
        var count = GD.RandRange(3, 5);
        var reduce = GameState.Instance.ReduceFlash;
        _buoys = new Node2D?[count];
        _buoySpeed = new float[count];
        for (var i = 0; i < count; i++)
        {
            var spot = RandomZoneSpot();
            var buoy = new Node2D { Position = spot };
            var core = CinematicFx.SoftGlow(6.0f, new Color(UITheme.HoloPale, reduce ? 0.85f : 0.5f));
            buoy.AddChild(core);
            var halo = CinematicFx.SoftGlow(22.0f, new Color(UITheme.Accent, reduce ? 0.35f : 0.22f));
            buoy.AddChild(halo);
            _buoySpeed[i] = (float)GD.RandRange(8.0, 18.0);
            _buoys[i] = buoy;
            AddChild(buoy);

            if (reduce)
            {
                continue;
            }

            var blink = buoy.CreateTween().SetLoops();
            blink.TweenInterval(0.22 * i); // 逐标错相：一排灯同时闪会读成一个整体的脉冲
            blink.TweenProperty(core, "modulate:a", 1.0f, 0.10).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            blink.Parallel().TweenProperty(halo, "modulate:a", 0.55f, 0.10);
            blink.TweenProperty(core, "modulate:a", 0.45f, 1.35).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
            blink.Parallel().TweenProperty(halo, "modulate:a", 0.18f, 1.35);
        }
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        _planetScroll += _planetSpeed * d;
        _planetAngle += _planetSpin * d;
        PlacePlanet();

        var band = _areaSize.Y + DebrisBandMargin * 2.0f;
        var wrapY = _origin.Y + _areaSize.Y + DebrisBandMargin;
        var wrapX = _origin.X + _areaSize.X + 120.0f;
        for (var i = 0; i < _debris.Length; i++)
        {
            var s = _debris[i];
            if (s == null)
            {
                continue;
            }

            var p = s.Position + new Vector2(_debrisDriftX[i] * d, _debrisSpeed[i] * d);
            if (p.Y > wrapY)
            {
                p.Y -= band;
            }

            if (p.X > wrapX)
            {
                p.X -= _areaSize.X + 240.0f;
            }
            else if (p.X < _origin.X - 120.0f)
            {
                p.X += _areaSize.X + 240.0f;
            }

            s.Position = p;
            s.Rotation += _debrisSpin[i] * d;
        }

        var dustWrapY = _origin.Y + _areaSize.Y;
        for (var i = 0; i < _dust.Length; i++)
        {
            var p = _dust[i] + new Vector2(0.0f, _dustSpeed[i] * d);
            if (p.Y > dustWrapY)
            {
                p.Y -= _areaSize.Y;
            }

            _dust[i] = p;
        }

        for (var i = 0; i < _buoys.Length; i++)
        {
            var buoy = _buoys[i];
            if (buoy == null || !GodotObject.IsInstanceValid(buoy))
            {
                continue;
            }

            var p = buoy.Position + new Vector2(0.0f, _buoySpeed[i] * d);
            if (p.Y > dustWrapY + 40.0f)
            {
                p.Y -= _areaSize.Y + 80.0f;
            }

            buoy.Position = p;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_dustTex == null)
        {
            return;
        }

        for (var i = 0; i < _dust.Length; i++)
        {
            var half = _dustSize[i] * 0.5f;
            DrawTextureRect(
                _dustTex,
                new Rect2(_dust[i] - new Vector2(half, half), new Vector2(_dustSize[i], _dustSize[i])),
                false,
                new Color(0.85f, 0.88f, 1.0f, _dustAlpha[i]));
        }
    }
}
