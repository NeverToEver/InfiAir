using Godot;

namespace InfiAir;

/// <summary>
/// Buff 外观反馈（M3c 全量迁移，2026-08-08 自 scripts/player_buff_visuals.gd 迁移）：
/// 一次性构建全部附件（程序化 Polygon2D/Line2D/Sprite2D，无新增贴图），
/// 由 GameState.buffs_changed 信号驱动 Refresh() 切换显隐与层数强度。
/// 作为 Player 子节点随机体旋转；坐标按基准机体系数 BaseShipScale（0.65，贴图 254px ≈ 165px 翼展
/// 三角拦截机，机头朝 -Y）设计，Player 创建本节点时按实际 sprite 缩放等比放大。
/// 部位锚点与 scripts/tools/generate_player_sprite.py 头部注释的贴图坐标对应（偏移 × 0.65）。
/// Enemy.SinFast（M3b 已迁）为同命名空间静态方法，直接引用。
/// 迁移期动态访问：GameState（GDScript autoload）经 GameStateBridge；buff_count 事件驱动（非热路径）。
/// Player 为 InfiAir.Player（M3c 并行迁移；EngineTint 公开属性由其提供，编译期稍后统一验证）。
/// </summary>
public partial class PlayerBuffVisuals : Node2D
{
    private static readonly Color ColorCyan = new(0.45f, 0.9f, 1.0f);
    private static readonly Color ColorGold = new(1.0f, 0.85f, 0.35f);
    private static readonly Color ColorOrange = new(1.0f, 0.55f, 0.2f);
    private static readonly Color ColorGreen = new(0.45f, 1.0f, 0.6f);
    private static readonly Color ColorMagenta = new(0.9f, 0.35f, 0.7f);
    private static readonly Color ColorSteel = new(0.5f, 0.65f, 0.75f);
    /// <summary>附件几何的基准机体缩放（贴图 254px 时的设计机体系数）。</summary>
    public const float BaseShipScale = 0.65f;
    /// <summary>跨语言访问器（GDScript 不能经脚本资源读 C# 常量——实测，静态方法可调；
    /// player.gd 原 `PlayerBuffVisuals.BASE_SHIP_SCALE` 调用点迁 C# 后改直读常量）。</summary>
    public static float GetBaseShipScale() => BaseShipScale;
    /// <summary>尾焰染色（乘算基色）：高效推进偏绿 / 燃料再生偏金，双 buff 时色相自然混合。</summary>
    private static readonly Color TintEfficient = new(0.75f, 1.1f, 0.85f);
    private static readonly Color TintRecovery = new(1.15f, 1.05f, 0.75f);
    /// <summary>层数视觉封顶：无限叠加 buff 的外观强度只表达到第 5 层。</summary>
    private const int StackVisualCap = 5;

    private Player _player = null!;

    private Polygon2D _powerGlow = null!;
    private Node2D _rapidFins = null!;
    private readonly Godot.Collections.Array<Node2D> _spreadPods = new();
    private Polygon2D _pierceSpike = null!;
    private Polygon2D _explosiveGlow = null!;
    private Node2D _laserPod = null!;
    private Line2D _armorRing = null!;
    private Line2D _regenRing = null!;
    private Node2D _lifestealTips = null!;
    private Line2D _shieldHex = null!;
    private Sprite2D _evasionGhost = null!;
    private Node2D _dashFins = null!;
    private Line2D _slowRing = null!;
    private Polygon2D _beacon = null!;

    private readonly Callable _onBuffsChanged;

    public PlayerBuffVisuals()
    {
        _onBuffsChanged = Callable.From(Refresh);
    }

    // ---- 外观节点 getter（A7：测试/诊断白盒断言经公开接口） ----

    public Polygon2D PowerGlow() => _powerGlow;

    public Node2D RapidFins() => _rapidFins;

    public Godot.Collections.Array<Node2D> SpreadPods() => _spreadPods;

    public Polygon2D PierceSpike() => _pierceSpike;

    public Polygon2D ExplosiveGlow() => _explosiveGlow;

    public Node2D LaserPod() => _laserPod;

    public Line2D ArmorRing() => _armorRing;

    public Line2D RegenRing() => _regenRing;

    public Node2D LifestealTips() => _lifestealTips;

    public Line2D ShieldHex() => _shieldHex;

    public Sprite2D EvasionGhost() => _evasionGhost;

    public Node2D DashFins() => _dashFins;

    public Line2D SlowRing() => _slowRing;

    public Polygon2D Beacon() => _beacon;

    /// <summary>由 Player._ready() 调用：构建附件、按当前 buff 刷新、监听后续变更。</summary>
    public void Init(Sprite2D shipSprite, Player player)
    {
        _player = player;
        _BuildAll(shipSprite.Texture);
        Refresh();
        var gs = GameStateBridge.Instance;
        if (gs != null && !gs.IsConnected("buffs_changed", _onBuffsChanged))
        {
            gs.Connect("buffs_changed", _onBuffsChanged);
        }
    }

    public override void _ExitTree()
    {
        // C22：显式断开 GameState 信号连接（C# [Signal]/Connect 连接不随接收方释放自动断开）
        var gs = GameStateBridge.Instance;
        if (gs != null && gs.IsConnected("buffs_changed", _onBuffsChanged))
        {
            gs.Disconnect("buffs_changed", _onBuffsChanged);
        }
    }

    public override void _Process(double _delta)
    {
        // 仅 Refresh() 判定有脉动件可见时才启用处理；动画全部按时间正弦，无每帧分配
        var t = Time.GetTicksMsec() / 1000.0;
        if (_regenRing.Visible)
        {
            _regenRing.Modulate = WithAlpha(_regenRing.Modulate, 0.2f + 0.35f * Mathf.Abs(Enemy.SinFast((float)(t * 2.0))));
        }

        if (_evasionGhost.Visible)
        {
            _evasionGhost.Modulate = WithAlpha(_evasionGhost.Modulate,
                0.1f + 0.18f * Mathf.Abs(Enemy.SinFast((float)(t * 3.0))));
        }

        if (_beacon.Visible)
        {
            _beacon.Modulate = WithAlpha(_beacon.Modulate,
                Enemy.SinFast((float)(t * 6.0)) > 0.0f ? 1.0f : 0.15f);
        }

        if (_slowRing.Visible)
        {
            // 软力场环允许节点缩放脉动（线宽 2→2.5 的失真在半透明环上不可辨）
            var k = 1.0f + 0.22f * (0.5f + 0.5f * Enemy.SinFast((float)(t * 1.5)));
            _slowRing.Scale = new Vector2(k, k);
        }
    }

    /// <summary>buff 显隐/强度刷新（GameState.buffs_changed 信号驱动 + init 首次调用）。</summary>
    public void Refresh()
    {
        var stacks = BuffCount(new StringName("power_shot"));
        _powerGlow.Visible = stacks > 0;
        if (stacks > 0)
        {
            var k = 1.0f + 0.12f * Mathf.Min(stacks, StackVisualCap);
            _powerGlow.Scale = new Vector2(k, k);
            _powerGlow.Modulate = WithAlpha(_powerGlow.Modulate, 0.45f + 0.09f * Mathf.Min(stacks, StackVisualCap));
        }

        stacks = BuffCount(new StringName("rapid_fire"));
        _rapidFins.Visible = stacks > 0;
        if (stacks > 0)
        {
            var finColor = stacks < 2 ? ColorCyan : new Color(0.3f, 0.8f, 1.0f);
            foreach (var child in _rapidFins.GetChildren())
            {
                ((Polygon2D)child).Color = finColor;
            }
        }

        stacks = Mathf.Min(BuffCount(new StringName("spread_shot")), _spreadPods.Count);
        for (var i = 0; i < _spreadPods.Count; i++)
        {
            _spreadPods[i].Visible = i < stacks;
        }

        _pierceSpike.Visible = BuffCount(new StringName("piercing")) > 0;
        _explosiveGlow.Visible = BuffCount(new StringName("explosive")) > 0;
        _laserPod.Visible = BuffCount(new StringName("laser_beam")) > 0;
        _lifestealTips.Visible = BuffCount(new StringName("lifesteal")) > 0;
        _shieldHex.Visible = BuffCount(new StringName("armor")) > 0;
        _evasionGhost.Visible = BuffCount(new StringName("evasion")) > 0;
        _dashFins.Visible = BuffCount(new StringName("phase_dash")) > 0;
        _slowRing.Visible = BuffCount(new StringName("slow_field")) > 0;
        _beacon.Visible = BuffCount(new StringName("mothership_recall")) > 0;

        stacks = BuffCount(new StringName("extra_life"));
        _armorRing.Visible = stacks > 0;
        if (stacks > 0)
        {
            _armorRing.Width = 2.0f + 0.6f * Mathf.Min(stacks, StackVisualCap);
        }

        _regenRing.Visible = BuffCount(new StringName("regen")) > 0;

        // 尾焰染色：player 每帧用 engine_tint 乘算基色
        var tint = Colors.White;
        if (BuffCount(new StringName("efficient_boost")) > 0)
        {
            tint *= TintEfficient;
        }

        if (BuffCount(new StringName("boost_recovery")) > 0)
        {
            tint *= TintRecovery;
        }

        _player.EngineTint = tint;

        SetProcess(_regenRing.Visible || _evasionGhost.Visible || _beacon.Visible || _slowRing.Visible);
    }

    // ---------------- GDScript 鸭子调用兼容桥（M3c 过渡，M7 删除） ----------------
    // buff_visuals_test.gd 等 GDScript 调用方经动态派发以 snake_case 访问外观 getter；
    // 其类型注解（`child is PlayerBuffVisuals` / `: PlayerBuffVisuals`）在调用点适配批次改
    // untyped（GDScript 不能以类名引用 C# 类）。M7 全量迁移后删除本段。

    public void init(Sprite2D shipSprite, Player player) => Init(shipSprite, player);

    public void refresh() => Refresh();

    public Polygon2D power_glow() => PowerGlow();

    public Node2D rapid_fins() => RapidFins();

    public Godot.Collections.Array<Node2D> spread_pods() => SpreadPods();

    public Polygon2D pierce_spike() => PierceSpike();

    public Polygon2D explosive_glow() => ExplosiveGlow();

    public Node2D laser_pod() => LaserPod();

    public Line2D armor_ring() => ArmorRing();

    public Line2D regen_ring() => RegenRing();

    public Node2D lifesteal_tips() => LifestealTips();

    public Line2D shield_hex() => ShieldHex();

    public Sprite2D evasion_ghost() => EvasionGhost();

    public Node2D dash_fins() => DashFins();

    public Line2D slow_ring() => SlowRing();

    public Polygon2D beacon() => Beacon();

    // ---------------- 内部实现 ----------------

    /// <summary>GameState.buff_count 事件驱动访问（非热路径；buff 变更频率极低）。</summary>
    private static int BuffCount(StringName name) => (int)GameStateBridge.Call("buff_count", name).AsInt64();

    /// <summary>保色相只改 alpha（原 GDScript `modulate.a = x` 链式赋值语义）。</summary>
    private static Color WithAlpha(Color c, float a) => new(c.R, c.G, c.B, a);

    /// <summary>一次性构建全部附件（部位锚点注释对应 generate_player_sprite.py 贴图坐标）。</summary>
    private void _BuildAll(Texture2D shipTexture)
    {
        // 机头炮口金色辉光（power_shot）
        _powerGlow = _MakeCircle(14.0f, WithAlpha(ColorGold, 0.55f));
        _powerGlow.Position = new Vector2(0.0f, -74.0f);
        AddChild(_powerGlow);

        // 引擎舱散热鳍（rapid_fire）
        _rapidFins = new Node2D();
        var finL = _MakePoly(new Vector2[] { new(-2, -12), new(-10, 10), new(2, 10) }, ColorCyan);
        finL.Position = new Vector2(-18.0f, 50.0f);
        _rapidFins.AddChild(finL);
        var finR = _MakePoly(new Vector2[] { new(2, -12), new(10, 10), new(-2, 10) }, ColorCyan);
        finR.Position = new Vector2(18.0f, 50.0f);
        _rapidFins.AddChild(finR);
        AddChild(_rapidFins);

        // 翼面挂架炮舱（spread_shot，每层 1 个，左右交替后居中）
        var podPositions = new[] { new Vector2(-40.0f, 16.0f), new Vector2(40.0f, 16.0f), new Vector2(0.0f, -56.0f) };
        foreach (var podPos in podPositions)
        {
            var pod = new Node2D { Position = podPos };
            var body = _MakePoly(new Vector2[] { new(-7, -10), new(7, -10), new(7, 10), new(-7, 10) }, ColorSteel);
            pod.AddChild(body);
            var barrel = _MakePoly(new Vector2[] { new(-2, -22), new(2, -22), new(2, -10), new(-2, -10) }, ColorCyan);
            pod.AddChild(barrel);
            AddChild(pod);
            _spreadPods.Add(pod);
        }

        // 机头穿甲尖刺（piercing）
        _pierceSpike = _MakePoly(new Vector2[] { new(0, -102), new(6, -74), new(-6, -74) }, new Color(0.55f, 0.95f, 1.0f));
        AddChild(_pierceSpike);

        // 机腹弹舱辉光（explosive，压底不盖机体）
        _explosiveGlow = _MakeCircle(18.0f, WithAlpha(ColorOrange, 0.45f));
        _explosiveGlow.Position = new Vector2(0.0f, 34.0f);
        _explosiveGlow.ZIndex = -1;
        AddChild(_explosiveGlow);

        // 背部激光发射基座（laser_beam，座舱后方脊线）
        _laserPod = new Node2D { Position = new Vector2(0.0f, 10.0f) };
        var podBody = _MakePoly(
            new Vector2[] { new(-6, -11), new(6, -11), new(6, 11), new(-6, 11) }, new Color(0.35f, 0.45f, 0.55f));
        _laserPod.AddChild(podBody);
        var lens = _MakeCircle(3.5f, new Color(0.6f, 0.95f, 1.0f));
        lens.Position = new Vector2(0.0f, -11.0f);
        _laserPod.AddChild(lens);
        AddChild(_laserPod);

        // 装甲环（extra_life，层数加粗）
        _armorRing = _MakeRing(78.0f, 2.0f, new Color(0.6f, 0.85f, 1.0f, 0.55f));
        AddChild(_armorRing);

        // 呼吸光环（regen）
        _regenRing = _MakeRing(88.0f, 2.0f, WithAlpha(ColorGreen, 0.4f));
        AddChild(_regenRing);

        // 翼尖三角（lifesteal）
        _lifestealTips = new Node2D();
        var tipL = _MakePoly(new Vector2[] { new(-10, 0), new(0, -7), new(0, 7) }, ColorMagenta);
        tipL.Position = new Vector2(-72.0f, 46.0f);
        _lifestealTips.AddChild(tipL);
        var tipR = _MakePoly(new Vector2[] { new(10, 0), new(0, -7), new(0, 7) }, ColorMagenta);
        tipR.Position = new Vector2(72.0f, 46.0f);
        _lifestealTips.AddChild(tipR);
        AddChild(_lifestealTips);

        // 六边形护盾弧（armor）
        _shieldHex = _MakeRing(96.0f, 2.0f, new Color(0.5f, 0.9f, 1.0f, 0.3f), 6);
        AddChild(_shieldHex);

        // 残像覆盖层（evasion）：独立 Sprite2D，不占用主 sprite 的无敌帧 alpha
        _evasionGhost = new Sprite2D
        {
            Texture = shipTexture,
            Scale = Vector2.One * (BaseShipScale + 0.02f),
            Modulate = new Color(0.6f, 0.95f, 1.0f, 0.2f),
            ZIndex = -1,
        };
        AddChild(_evasionGhost);

        // 尾部相位鳍（phase_dash）
        _dashFins = new Node2D();
        var dfinL = _MakePoly(new Vector2[] { new(-8, 12), new(0, -6), new(2, 12) }, new Color(0.4f, 0.8f, 1.0f));
        dfinL.Position = new Vector2(-14.0f, 64.0f);
        _dashFins.AddChild(dfinL);
        var dfinR = _MakePoly(new Vector2[] { new(8, 12), new(0, -6), new(-2, 12) }, new Color(0.4f, 0.8f, 1.0f));
        dfinR.Position = new Vector2(14.0f, 64.0f);
        _dashFins.AddChild(dfinR);
        AddChild(_dashFins);

        // 慢速力场环（slow_field，半径脉动在 _process）
        _slowRing = _MakeRing(104.0f, 2.0f, new Color(0.55f, 0.8f, 1.0f, 0.35f));
        AddChild(_slowRing);

        // 机顶信标（mothership_recall，座舱前方）
        _beacon = _MakeCircle(4.0f, new Color(1.0f, 0.4f, 0.35f));
        _beacon.Position = new Vector2(0.0f, -36.0f);
        AddChild(_beacon);

        // 初始全部隐藏，由 Refresh() 统一驱动
        foreach (var node in new Node2D[]
        {
            _powerGlow, _rapidFins, _pierceSpike, _explosiveGlow, _laserPod, _armorRing, _regenRing,
            _lifestealTips, _shieldHex, _evasionGhost, _dashFins, _slowRing, _beacon,
        })
        {
            node.Visible = false;
        }

        foreach (var pod in _spreadPods)
        {
            pod.Visible = false;
        }
    }

    /// <summary>程序化圆多边形（Polygon2D，默认 16 段）。</summary>
    private Polygon2D _MakeCircle(float radius, Color color, int segments = 16)
    {
        var pts = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var a = Mathf.Tau * i / segments;
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        return _MakePoly(pts, color);
    }

    /// <summary>程序化多边形（Polygon2D）。</summary>
    private Polygon2D _MakePoly(Vector2[] points, Color color)
    {
        return new Polygon2D { Polygon = points, Color = color };
    }

    /// <summary>程序化圆环（Line2D 闭合，默认 28 段）。</summary>
    private Line2D _MakeRing(float radius, float width, Color color, int segments = 28)
    {
        var ring = new Line2D { Width = width, DefaultColor = color, Closed = true };
        for (var i = 0; i < segments; i++)
        {
            var a = Mathf.Tau * i / segments;
            ring.AddPoint(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }

        return ring;
    }
}
