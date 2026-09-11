using Godot;

namespace InfiAir;

/// <summary>
/// 战斗背景纵深层（纯视觉，不参与任何玩法判定）：行星远景 / 残骸中带 / 尘埃近景三层，
/// 挂在 main.tscn 世界层（z=-5：Starfield 星点层 z=-10 之上、游戏实体之下）。
/// 视差口径同 Starfield：相机不平移，各层以不同下漂速度模拟纵深——行星近乎静止
/// （慢速 + 加高回绕带，十余分钟才横越一次）、残骸中速漂落、尘埃高速下漂营造速度感。
/// 布局确定性：启动时固定种子 RNG 掷一次（同 Starfield），热路径原地写数组零分配。
/// 区域锚定 zoom 感知的 view_world_rect；视角档位切换按相对坐标重映射（同 Starfield
/// RebuildArea），避免「大档位建区后切回小档位，背景只剩中央一块」的残留。
/// </summary>
public partial class DeepSpaceBackdrop : Node2D
{
    private const ulong LayoutSeed = 20260911;

    // 行星回绕带余量：带高 = 区域高 + 2×Margin（大于行星半径 450，出入画无突现）
    private const float PlanetBandMargin = 620.0f;
    private const float DebrisBandMargin = 320.0f;

    // ---- 配置（默认值与 balance.json effects.backdrop.* 一致）----
    private float _planetSpeed = 3.0f;
    private int _debrisCount = 8;
    private float _debrisMinSpeed = 20.0f;
    private float _debrisMaxSpeed = 42.0f;
    private float _debrisAlpha = 0.5f;
    private int _dustCount = 40;
    private float _dustSpeed = 112.0f;
    private float _dustAlpha = 0.12f;

    // ---- 行星远景层：位置表驱动（x 相对区域宽，y0 相对回绕带高），避画面中心弹幕焦点区 ----
    private static readonly string[] PlanetTextures =
    {
        "res://assets/sprites/backdrop/planet_1.png",
        "res://assets/sprites/backdrop/planet_2.png",
    };

    // (x 相对宽, y0 相对带高, 缩放, 压暗 tint)：行星 1 右上区、残月左上区
    private static readonly (float RelX, float RelY, float Scale, Color Tint)[] PlanetLayout =
    {
        (0.86f, 0.10f, 1.05f, new Color(0.52f, 0.50f, 0.48f, 0.85f)),
        (0.07f, 0.58f, 0.70f, new Color(0.42f, 0.43f, 0.48f, 0.70f)),
    };

    private Sprite2D?[] _planets = System.Array.Empty<Sprite2D?>();
    private float _planetScroll;

    // ---- 残骸中带层：精灵池化，逐块速度/旋转/横漂 ----
    private static readonly string[] DebrisTextures =
    {
        "res://assets/sprites/backdrop/debris_1.png",
        "res://assets/sprites/backdrop/debris_2.png",
        "res://assets/sprites/backdrop/debris_3.png",
    };

    private Sprite2D?[] _debris = System.Array.Empty<Sprite2D?>();
    private float[] _debrisSpeed = System.Array.Empty<float>();
    private float[] _debrisDriftX = System.Array.Empty<float>();
    private float[] _debrisSpin = System.Array.Empty<float>();

    // ---- 尘埃近景层：软点颗粒 _Draw 合批绘制（原地写位置数组，零分配） ----
    private Texture2D? _dustTex;
    private Vector2[] _dust = System.Array.Empty<Vector2>();
    private float[] _dustSize = System.Array.Empty<float>();
    private float[] _dustSpeedK = System.Array.Empty<float>();
    private float[] _dustAlphaK = System.Array.Empty<float>();

    /// <summary>可见世界区域缓存（view_world_rect），不得硬编码 1920×1080。</summary>
    private Vector2 _areaSize = new(1920.0f, 1080.0f);
    private Vector2 _origin = Vector2.Zero;

    /// <summary>建区时生效的视角档位倍率（重映射判据，同 Starfield：轮询设置档位而非
    /// 视口 rect——DYING 呼吸缩放每帧改相机 Zoom，按 rect 判会逐帧抖动重映射）。</summary>
    private float _builtZoom = -1.0f;

    public override void _Ready()
    {
        ZIndex = -5;
        LoadCfg();

        var view = GameState.Instance.ViewWorldRect();
        _areaSize = view.Size;
        _origin = view.Position;
        _builtZoom = (float)GameState.Instance.ViewZoomFactor();

        var rng = new RandomNumberGenerator();
        rng.Seed = LayoutSeed;

        BuildPlanets();
        BuildDebris(rng);
        BuildDust(rng);
    }

    /// <summary>数值配置缓存（启动一次读入；判型 + 钳制，手改非法值不崩，同 Starfield 口径）。</summary>
    private void LoadCfg()
    {
        var gs = GameState.Instance;
        _planetSpeed = Mathf.Max(0.0f, CfgF(gs, "effects.backdrop.planet_speed", _planetSpeed));
        _debrisCount = CfgCount(gs, "effects.backdrop.debris_count", _debrisCount);
        _debrisMinSpeed = Mathf.Max(0.0f, CfgF(gs, "effects.backdrop.debris_min_speed", _debrisMinSpeed));
        _debrisMaxSpeed = Mathf.Max(_debrisMinSpeed, CfgF(gs, "effects.backdrop.debris_max_speed", _debrisMaxSpeed));
        _debrisAlpha = Mathf.Clamp(CfgF(gs, "effects.backdrop.debris_alpha", _debrisAlpha), 0.0f, 1.0f);
        _dustCount = CfgCount(gs, "effects.backdrop.dust_count", _dustCount);
        _dustSpeed = Mathf.Max(0.0f, CfgF(gs, "effects.backdrop.dust_speed", _dustSpeed));
        _dustAlpha = Mathf.Clamp(CfgF(gs, "effects.backdrop.dust_alpha", _dustAlpha), 0.0f, 1.0f);
    }

    private static float CfgF(GameState gs, string key, float def)
    {
        var v = gs.Cfg(key, def);
        return v.VariantType is Variant.Type.Float or Variant.Type.Int ? (float)v.AsDouble() : def;
    }

    /// <summary>数量键：判型 + [0, 256] 钳制（上界防手改巨值分配失控，同 Starfield 口径）。</summary>
    private static int CfgCount(GameState gs, string key, int def)
    {
        var v = gs.Cfg(key, def);
        return v.VariantType == Variant.Type.Int && v.AsInt64() >= 0 ? (int)Mathf.Min(v.AsInt64(), 256) : def;
    }

    private void BuildPlanets()
    {
        _planets = new Sprite2D?[PlanetLayout.Length];
        for (var i = 0; i < PlanetLayout.Length; i++)
        {
            var tex = GD.Load<Texture2D>(PlanetTextures[i]);
            if (tex == null)
            {
                continue;
            }

            var (relX, relY, scale, tint) = PlanetLayout[i];
            var s = new Sprite2D
            {
                Texture = tex,
                Scale = Vector2.One * scale,
                Modulate = tint, // 大气透视压暗：远景天体不得抢弹幕焦点
                ShowBehindParent = true, // 尘埃（本节点 _Draw）须压行星/残骸之上
            };
            _planets[i] = s;
            AddChild(s);
            PlacePlanet(i, relX, relY);
        }
    }

    /// <summary>行星世界坐标：x 相对区域宽（重映射自动跟随），y 沿加高回绕带取模。</summary>
    private void PlacePlanet(int i, float relX, float relY)
    {
        var s = _planets[i];
        if (s == null)
        {
            return;
        }

        var band = _areaSize.Y + PlanetBandMargin * 2.0f;
        var y = _origin.Y - PlanetBandMargin + Mathf.PosMod(relY * band + _planetScroll, band);
        s.Position = new Vector2(_origin.X + relX * _areaSize.X, y);
    }

    private void BuildDebris(RandomNumberGenerator rng)
    {
        var band = _areaSize.Y + DebrisBandMargin * 2.0f;
        _debris = new Sprite2D?[_debrisCount];
        _debrisSpeed = new float[_debrisCount];
        _debrisDriftX = new float[_debrisCount];
        _debrisSpin = new float[_debrisCount];
        for (var i = 0; i < _debrisCount; i++)
        {
            var tex = GD.Load<Texture2D>(DebrisTextures[i % DebrisTextures.Length]);
            if (tex == null)
            {
                continue;
            }

            _debrisSpeed[i] = rng.RandfRange(_debrisMinSpeed, _debrisMaxSpeed);
            _debrisDriftX[i] = rng.RandfRange(-9.0f, 9.0f);
            _debrisSpin[i] = rng.RandfRange(-0.10f, 0.10f);
            var scale = rng.RandfRange(0.55f, 1.0f);
            var alpha = _debrisAlpha * rng.RandfRange(0.7f, 1.0f);
            var s = new Sprite2D
            {
                Texture = tex,
                Scale = Vector2.One * scale,
                Rotation = rng.Randf() * Mathf.Tau,
                Modulate = new Color(0.46f, 0.48f, 0.54f, alpha), // 冷调压暗 + 低透明度
                ShowBehindParent = true,
                Position = new Vector2(
                    _origin.X + rng.Randf() * _areaSize.X,
                    _origin.Y - DebrisBandMargin + rng.Randf() * band),
            };
            _debris[i] = s;
            AddChild(s);
        }
    }

    private void BuildDust(RandomNumberGenerator rng)
    {
        _dustTex = CinematicFx.SoftTexture();
        _dust = new Vector2[_dustCount];
        _dustSize = new float[_dustCount];
        _dustSpeedK = new float[_dustCount];
        _dustAlphaK = new float[_dustCount];
        for (var i = 0; i < _dustCount; i++)
        {
            _dust[i] = new Vector2(_origin.X + rng.Randf() * _areaSize.X, _origin.Y + rng.Randf() * _areaSize.Y);
            _dustSize[i] = rng.RandfRange(3.0f, 9.0f);
            _dustSpeedK[i] = rng.RandfRange(0.7f, 1.15f);
            _dustAlphaK[i] = rng.RandfRange(0.5f, 1.0f);
        }
    }

    /// <summary>视角档位切换后的区域重映射（世界语境）：残骸/尘埃按旧区相对坐标原地映射进
    /// 新区（同 Starfield.RebuildArea 口径）；行星每帧由相对表 + 回绕带取模定位，自动跟随。</summary>
    private void RebuildArea()
    {
        var view = GameState.Instance.ViewWorldRect();
        _builtZoom = (float)GameState.Instance.ViewZoomFactor();
        if (view.Size == _areaSize && view.Position == _origin)
        {
            return;
        }

        for (var i = 0; i < _debris.Length; i++)
        {
            var s = _debris[i];
            if (s != null)
            {
                s.Position = Remap(s.Position, view);
            }
        }

        for (var i = 0; i < _dust.Length; i++)
        {
            _dust[i] = Remap(_dust[i], view);
        }

        _areaSize = view.Size;
        _origin = view.Position;
    }

    private Vector2 Remap(Vector2 p, Rect2 view)
    {
        var rel = (p - _origin) / _areaSize;
        return new Vector2(view.Position.X + rel.X * view.Size.X, view.Position.Y + rel.Y * view.Size.Y);
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        if ((float)GameState.Instance.ViewZoomFactor() != _builtZoom)
        {
            RebuildArea();
        }

        // 行星远景：滚动量累积，每帧按回绕带取模重定位（近乎静止的极慢视差）
        _planetScroll += _planetSpeed * d;
        for (var i = 0; i < _planets.Length; i++)
        {
            PlacePlanet(i, PlanetLayout[i].RelX, PlanetLayout[i].RelY);
        }

        // 残骸中带：下漂 + 微横漂 + 慢自旋，出带回绕（横漂出界同基线回绕）
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

        // 尘埃近景：高速下漂，出区回绕（回绕基线随区域锚点，同 Starfield）
        var dustWrapY = _origin.Y + _areaSize.Y;
        for (var i = 0; i < _dust.Length; i++)
        {
            var p = _dust[i] + new Vector2(0.0f, _dustSpeed * _dustSpeedK[i] * d);
            if (p.Y > dustWrapY)
            {
                p.Y -= _areaSize.Y;
            }

            _dust[i] = p;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        // 行星/残骸为 ShowBehindParent 子精灵，绘于本层之下；这里只画最近的尘埃颗粒
        if (_dustTex == null)
        {
            return;
        }

        for (var i = 0; i < _dust.Length; i++)
        {
            var size = _dustSize[i];
            var half = size * 0.5f;
            var a = _dustAlpha * _dustAlphaK[i];
            DrawTextureRect(_dustTex, new Rect2(_dust[i] - new Vector2(half, half), new Vector2(size, size)), false, new Color(0.85f, 0.88f, 1.0f, a));
        }
    }
}
