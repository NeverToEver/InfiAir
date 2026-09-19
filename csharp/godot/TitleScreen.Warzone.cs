using Godot;
using InfiAir.Core.Visual;

namespace InfiAir;

/// <summary>
/// 标题屏·远景实况战场部分：机库窗外的深空里，敌机编队、交火爆炸、曳光火网、大型舰巡航、
/// Boss 剪影与传感扫描轮番出现。全部小尺寸 / 低透明度 / 冷调（大气透视，只做背景氛围）；
/// 节点池 + Godot.Timer（OneShot + 随机重启），_Process 仅原地推进活跃 sprite 位置，零分配。
/// 层级：WarzoneRoot 紧随星空与纵深层（机体与 UI 之下）。
///
/// **下一刻演什么由 core 的 <see cref="AmbientDirector"/> 掷**（加权随机 + 逐类冷却 + 不连出同类），
/// 取代原先「每类各跑一条固定间隔计时器」：固定间隔读起来是节拍器，不是战场。
/// 表里的权重定疏密——编队与交火是常态，曳光居中，大型舰 / Boss / 母舰 / 扫描是稀客
/// （权重低 + 冷却长）；稀客的价值在于稀，连出两次就不再是背景了。
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    private const int WzShipPool = 9; // 编队同屏上限：~3 编队 × 2-3 架 + 护航，池耗尽时静默少生成

    // 编队附加灯：尾焰辉光 + 翼尖航行灯（贴图局部坐标，190px 原生；机头朝局部 +Y，尾在 -Y）
    private const float WzTrailRadius = 26.0f;
    private const float WzNavRadius = 7.0f;
    private static readonly Vector2 WzTrailPos = new(0.0f, -96.0f);
    private static readonly Vector2 WzNavPosL = new(-58.0f, 6.0f);
    private static readonly Vector2 WzNavPosR = new(58.0f, 6.0f);

    // Boss 巡航：横向匀速外附加纵向漂浮与微幅侧倾，剪影不再走直线
    private const float WzBossDriftY = 16.0f;
    private const float WzBossDriftPeriod = 7.0f;
    private const float WzBossBank = 0.05f;
    private const float WzBossBankPeriod = 6.0f;

    /// <summary>演出表：权重 = 疏密，冷却 = 「再喜欢也别连着来」的秒数。</summary>
    private static readonly AmbientAct[] WzActs =
    {
        new("formation", 3.0, 4.0),
        new("strike", 2.4, 3.5),
        new("tracers", 1.1, 9.0),
        new("cruiser", 0.7, 20.0),
        new("boss", 0.6, 26.0),
        new("mothership", 0.5, 40.0),
        new("sweep", 0.4, 30.0),
    };

    private const double WzGapMin = 2.8;
    private const double WzGapMax = 5.6;

    /// <summary>开场首演延迟（s）：机体 1.6s 落位、标题区 1.0–1.5s 淡入，第一场戏压在它们之后。</summary>
    private const double WzFirstDelay = 2.4;

    private Node2D _wzRoot = null!;
    private readonly Node2D[] _wzShips = new Node2D[WzShipPool];
    private readonly Sprite2D[] _wzBodies = new Sprite2D[WzShipPool];
    private readonly Vector2[] _wzVel = new Vector2[WzShipPool];
    private readonly float[] _wzBank = new float[WzShipPool]; // 侧倾幅度（rad；0 = 不侧倾）
    private readonly float[] _wzBankHz = new float[WzShipPool];
    private readonly float[] _wzBaseRot = new float[WzShipPool];
    private readonly Texture2D[] _enemyTexs = new Texture2D[4];
    private readonly Texture2D[] _bossTexs = new Texture2D[4];

    private AmbientDirector _wzDirector = null!;

    /// <summary>演出时钟（累计帧间隔，模拟时间）：冷却按它计，不取墙钟——否则掉帧时
    /// 「冷却还剩多久」会随机器性能变化，无头与窗口两趟也不是同一条时间线。</summary>
    private double _wzClock;

    /// <summary>侧倾相位用时钟（s）。</summary>
    private float _wzTime;

    /// <summary>在场的大型舰（冷却只有 20s，横穿却要 30–50s：不设在场判据会叠出第二艘）。</summary>
    private bool _wzCruiserActive;

    private void BuildWarzone()
    {
        _wzRoot = new Node2D { Name = "WarzoneRoot" };
        AddChild(_wzRoot);
        for (var i = 0; i < 4; i++)
        {
            _enemyTexs[i] = GD.Load<Texture2D>($"res://assets/sprites/enemy_ship_{i + 1}.png");
            _bossTexs[i] = GD.Load<Texture2D>($"res://assets/sprites/boss_ship_{i + 1}.png");
        }

        for (var i = 0; i < WzShipPool; i++)
        {
            // 容器承载机体 + 尾焰/航行灯：机体单独压冷色 modulate，灯不被冷色乘暗
            var ship = new Node2D { Visible = false };
            var body = new Sprite2D();
            ship.AddChild(body);
            var trail = CinematicFx.SoftGlow(WzTrailRadius, new Color(1.0f, 0.62f, 0.24f, 0.42f));
            trail.Position = WzTrailPos;
            trail.Scale = new Vector2(trail.Scale.X * 0.55f, trail.Scale.Y * 1.7f); // 沿尾向拉长成尾迹
            ship.AddChild(trail);
            var navL = CinematicFx.SoftGlow(WzNavRadius, new Color(UITheme.HoloPale, 0.85f));
            navL.Position = WzNavPosL;
            ship.AddChild(navL);
            var navR = CinematicFx.SoftGlow(WzNavRadius, new Color(UITheme.HoloPale, 0.85f));
            navR.Position = WzNavPosR;
            ship.AddChild(navR);
            _wzRoot.AddChild(ship);
            _wzShips[i] = ship;
            _wzBodies[i] = body;
        }

        _wzDirector = new AmbientDirector(WzActs, WzGapMin, WzGapMax);
        ScheduleWz();
    }

    /// <summary>一次抽一类演出：OneShot Timer + 随机间隔重启（改 WaitTime 不碰当前周期，无当期歧义）。</summary>
    private void ScheduleWz()
    {
        var timer = new Godot.Timer { OneShot = true, WaitTime = (float)WzFirstDelay, Autostart = true };
        _wzRoot.AddChild(timer);
        timer.Timeout += () =>
        {
            SpawnWz(_wzDirector.Next(_wzClock));
            timer.WaitTime = (float)_wzDirector.Gap();
            timer.Start();
        };
    }

    /// <summary>演出分派。未知 id 与空串（表被清空）一律静默跳过：这是装饰层，不设非法态。</summary>
    private void SpawnWz(string act)
    {
        switch (act)
        {
            case "formation":
                SpawnFormation();
                break;
            case "strike":
                SpawnStrike();
                break;
            case "tracers":
                SpawnTracers();
                break;
            case "cruiser":
                SpawnCruiser();
                break;
            case "boss":
                SpawnBossCruise();
                break;
            case "mothership":
                SpawnMothershipCruise();
                break;
            case "sweep":
                SpawnSweep();
                break;
        }
    }

    public override void _Process(double delta)
    {
        TickBalanceReload(delta);
        _wzClock += delta;
        var d = (float)delta;
        _wzTime += d;
        for (var i = 0; i < WzShipPool; i++)
        {
            var ship = _wzShips[i];
            if (!ship.Visible)
            {
                continue;
            }

            ship.Position += _wzVel[i] * d;
            // 侧倾：机体沿航向的缓摆（编队有机动感，而不是贴图平移）
            if (_wzBank[i] != 0.0f)
            {
                ship.Rotation = _wzBaseRot[i] + Mathf.Sin(_wzTime * _wzBankHz[i] * Mathf.Tau) * _wzBank[i];
            }

            if (ship.Position.X < -160.0f || ship.Position.X > 2080.0f)
            {
                ship.Visible = false; // 归还池位（Visible 即活跃标记）
            }
        }
    }

    private int TakeWzShip()
    {
        for (var i = 0; i < WzShipPool; i++)
        {
            if (!_wzShips[i].Visible)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>池内放飞一架：贴图 / 缩放 / 朝向 / 相位一次写全（池位不做增量状态，避免上一场的残留）。</summary>
    private void LaunchWzShip(int idx, int texture, Vector2 pos, Vector2 vel, float scale, float rot, float bank)
    {
        var ship = _wzShips[idx];
        _wzBodies[idx].Texture = _enemyTexs[texture];
        _wzBodies[idx].Modulate = UITheme.HostileCool;
        ship.Scale = Vector2.One * scale;
        ship.Rotation = rot;
        ship.Position = pos;
        _wzVel[idx] = vel;
        _wzBaseRot[idx] = rot;
        _wzBank[idx] = bank;
        _wzBankHz[idx] = (float)GD.RandRange(0.18, 0.34);
        ship.Visible = true;
    }

    /// <summary>敌机编队：随机朝向/高度带/机型，远小慢近大快（视差纵深），纵向错列 + 逐机侧倾。
    /// 贴着安全区外的上下带入场：编队从标题文字或机体前面横穿会打断正在读的东西。</summary>
    private void SpawnFormation()
    {
        var toRight = GD.Randf() < 0.5f;
        var count = GD.RandRange(2, 3);
        var tex = GD.RandRange(0, _enemyTexs.Length - 1);
        var scale = (float)GD.RandRange(0.2, 0.36);
        var speed = 40.0f + scale * 290.0f;
        var dir = toRight ? 1.0f : -1.0f;
        var spot = TitleScreen.RandomSkySpot();
        var y = spot.Y;
        var vy = (float)GD.RandRange(-8.0, 8.0);
        // 敌机原图机头朝下 → 旋转指向航向（同 enemy.tscn rotation=PI 转正的逆运算）
        var rot = (toRight ? 0.0f : Mathf.Pi) - Mathf.Pi * 0.5f;
        var x0 = toRight ? -110.0f : 2030.0f;
        for (var j = 0; j < count; j++)
        {
            var idx = TakeWzShip();
            if (idx < 0)
            {
                return;
            }

            var yOff = j == 0 ? 0.0f : (j % 2 == 1 ? 26.0f : -26.0f);
            LaunchWzShip(
                idx,
                tex,
                new Vector2(x0 - dir * 78.0f * j, y + yOff),
                new Vector2(dir * speed, vy),
                scale,
                rot,
                (float)GD.RandRange(0.04, 0.10));
        }
    }

    /// <summary>
    /// 远处交火：近距一档（闪光 + 概率小环）与「被打中一舰」一档（更大闪光 + 双环 + 溅射）。
    /// 两档的存在理由：只有一档时远景读不出打的是小船还是大舰，而尺寸是背景里唯一可读的战果。
    /// </summary>
    private void SpawnStrike()
    {
        var pos = TitleScreen.RandomSkySpot();
        var big = GD.Randf() < 0.3f;
        SpawnFlash(pos, big ? (float)GD.RandRange(150.0, 230.0) : (float)GD.RandRange(60.0, 110.0), big ? 0.62f : 0.5f);
        SpawnRing(pos, big ? 150.0f : (float)GD.RandRange(70.0, 120.0), 0.55f, big ? 7.0f : 6.0f);
        if (!big)
        {
            return;
        }

        SpawnRing(pos, 260.0f, 0.8f, 4.0f);
        var sparks = GD.RandRange(5, 9);
        for (var i = 0; i < sparks; i++)
        {
            SpawnSpark(pos);
        }
    }

    /// <summary>爆闪：暖色涨落。alpha 峰值由 <paramref name="peak"/> 给（大档更亮）。</summary>
    private void SpawnFlash(Vector2 pos, float radius, float peak)
    {
        var flash = CinematicFx.SoftGlow(radius, new Color(1.0f, 0.55f, 0.25f, 0.0f));
        flash.Position = pos;
        _wzRoot.AddChild(flash);
        var tw = flash.CreateTween();
        tw.TweenProperty(flash, "modulate:a", peak, 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(flash, "modulate:a", 0.0f, 0.7).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(flash.QueueFree));
    }

    private void SpawnRing(Vector2 pos, float radius, float time, float width)
    {
        var shockwave = CinematicFx.Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = radius,
            ["time"] = time,
            ["color"] = new Color(1.0f, 0.6f, 0.3f, 0.32f),
            ["core_color"] = new Color(1.0f, 0.85f, 0.6f, 0.4f),
            ["width"] = width,
        });
        shockwave.Position = pos;
        _wzRoot.AddChild(shockwave);
    }

    /// <summary>被击中舰体的溅射：几枚软点向外抛洒后熄灭（自毁）。</summary>
    private void SpawnSpark(Vector2 pos)
    {
        var spark = CinematicFx.SoftGlow((float)GD.RandRange(3.0, 5.0), new Color(1.0f, 0.78f, 0.42f, 0.7f));
        spark.Position = pos;
        _wzRoot.AddChild(spark);
        var dir = Vector2.Right.Rotated((float)GD.RandRange(0.0, Mathf.Tau));
        var reach = (float)GD.RandRange(60.0, 150.0);
        var tw = spark.CreateTween().SetParallel(true);
        tw.TweenProperty(spark, "position", pos + dir * reach, 0.4).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(spark, "modulate:a", 0.0f, 0.4);
        tw.Chain().TweenCallback(Callable.From(spark.QueueFree));
    }

    /// <summary>曳光火网：一批短促弹道亮线，落在安全区里（不在标题与机体上画弹道）。</summary>
    private void SpawnTracers()
    {
        var zone = TitleScreen.SkyZones[GD.RandRange(0, TitleScreen.SkyZones.Length - 1)];
        var tracers = new TitleTracers();
        tracers.Setup(
            GD.RandRange(12, 20),
            zone,
            new Color(1.0f, 0.80f, 0.42f),
            new Color(1.0f, 0.45f, 0.58f));
        _wzRoot.AddChild(tracers);
    }

    /// <summary>敌方大型舰巡航：运输/指挥舰缓速横穿 + 两架护航 + 三枚航行灯。
    /// 与 Boss 剪影的区别在于「有护航、有灯、更慢」——读作有组织的巡航，而不是孤零零一张剪影。</summary>
    private void SpawnCruiser()
    {
        if (_wzCruiserActive)
        {
            return;
        }

        var toRight = GD.Randf() < 0.5f;
        var speed = (float)GD.RandRange(45.0, 70.0);
        var band = TitleScreen.SkyZones[GD.RandRange(0, 1)]; // 顶带或底带：舰体不进中段
        var y = band.Position.Y + band.Size.Y * 0.5f;
        var scale = (float)GD.RandRange(0.22, 0.3);
        var body = BuildCruiserBody(
            "res://assets/sprites/strike_carrier.png",
            new Color(0.64f, 0.54f, 0.72f, 0.34f),
            scale,
            toRight,
            y,
            speed,
            new Color(1.0f, 0.55f, 0.85f, 0.5f),
            onDone: () => _wzCruiserActive = false);

        // 护航：跟着同一速度走，纵向错开（不是编队掠过的「路过」，是「随行」）
        var escortTex = GD.RandRange(0, _enemyTexs.Length - 1);
        var escortScale = scale * 0.62f;
        var rot = (toRight ? 0.0f : Mathf.Pi) - Mathf.Pi * 0.5f;
        for (var j = 0; j < 2; j++)
        {
            var idx = TakeWzShip();
            if (idx < 0)
            {
                break;
            }

            var side = j == 0 ? -1.0f : 1.0f;
            LaunchWzShip(
                idx,
                escortTex,
                body.Position + new Vector2(side * (toRight ? -120.0f : 120.0f), side * 76.0f),
                new Vector2(toRight ? speed : -speed, 0.0f),
                escortScale,
                rot,
                0.05f);
        }
    }

    /// <summary>母舰巡航：己方母舰以更慢的速度、更低的透明度从远处横过（能量青同行内母舰 tint）。</summary>
    private void SpawnMothershipCruise()
    {
        if (_wzCruiserActive)
        {
            return;
        }

        var toRight = GD.Randf() < 0.5f;
        var band = TitleScreen.SkyZones[GD.RandRange(0, 1)];
        BuildCruiserBody(
            "res://assets/sprites/mothership.png",
            new Color(0.52f, 0.68f, 0.82f, 0.26f),
            (float)GD.RandRange(0.22, 0.3),
            toRight,
            band.Position.Y + band.Size.Y * 0.5f,
            (float)GD.RandRange(30.0, 46.0),
            new Color(UITheme.MothershipCool, 0.45f),
            onDone: () => _wzCruiserActive = false);
    }

    /// <summary>
    /// 大型舰舰体（巡航 / 母舰共用）：一次建好机体 + 三枚错相航行的灯枪，横向匀速走完全程自毁。
    /// 归属标记（<see cref="_wzCruiserActive"/>）由 <paramref name="onDone"/> 在退场时清掉，
    /// 于是「同时在场的巡航舰最多一艘」这条判据不依赖时长估算。
    /// </summary>
    private Node2D BuildCruiserBody(string path, Color tint, float scale, bool toRight, float y, float speed, Color lightColor, System.Action onDone)
    {
        var tex = GD.Load<Texture2D>(path);
        var ship = new Node2D
        {
            Scale = Vector2.One * scale,
            // 舰体贴图机头朝上 → 朝右为 +90°、朝左为 −90°（与敌机贴图机头朝下的换算相反）
            Rotation = toRight ? Mathf.Pi * 0.5f : -Mathf.Pi * 0.5f,
            Position = new Vector2(toRight ? -320.0f : 2240.0f, y),
        };
        var halfW = tex != null ? tex.GetWidth() * 0.42f : 180.0f;
        var halfH = tex != null ? tex.GetHeight() * 0.36f : 90.0f;
        if (tex != null)
        {
            ship.AddChild(new Sprite2D { Texture = tex, Modulate = tint });
        }

        // 航行灯按贴图比例落位（两种舰体共用一条算式）：左右翼各一枚 + 舰首一枚。
        // 半径是**舰体局部坐标**（容器带 scale）：18 × 0.26 ≈ 5px 屏幕半径，看着是舰上的灯
        // 而不是船边飘着的光
        var spots = new[] { new Vector2(-halfW, halfH * 0.6f), new Vector2(halfW, halfH * 0.6f), new Vector2(0.0f, -halfH * 1.2f) };
        for (var i = 0; i < spots.Length; i++)
        {
            var light = CinematicFx.SoftGlow(18.0f, new Color(lightColor, 0.0f));
            light.Position = spots[i];
            ship.AddChild(light);
            var blink = light.CreateTween().SetLoops();
            blink.TweenInterval(0.35 * i); // 逐灯错相（同浮标口径：同时闪会读成一个脉冲）
            blink.TweenProperty(light, "modulate:a", 0.9f, 0.12).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            blink.TweenProperty(light, "modulate:a", 0.15f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        }

        _wzRoot.AddChild(ship);
        _wzCruiserActive = true;
        var tw = ship.CreateTween();
        // 匀速横穿（缓动会让舰体在屏缘停滞数秒才露头）
        tw.TweenProperty(ship, "position:x", toRight ? 2240.0f : -320.0f, (2240.0f + 320.0f) / speed);
        tw.TweenCallback(Callable.From(() =>
        {
            ship.QueueFree();
            onDone();
        }));
        return ship;
    }

    /// <summary>Boss 剪影：暗蓝调大机型缓慢横穿上部空域（~18s），自毁。
    /// 横向匀速之外附加纵向漂浮 + 微幅侧倾（分属性 tween 并行，互不覆盖）。</summary>
    private void SpawnBossCruise()
    {
        var tex = _bossTexs[GD.RandRange(0, _bossTexs.Length - 1)];
        var toRight = GD.Randf() < 0.5f;
        var y = (float)GD.RandRange(120.0, 250.0);
        var baseRot = (toRight ? 0.0f : Mathf.Pi) - Mathf.Pi * 0.5f; // 同敌机：原图机头朝下
        var boss = new Sprite2D
        {
            Texture = tex,
            Scale = Vector2.One * 0.55f,
            Modulate = new Color(0.3f, 0.42f, 0.58f, 0.4f),
            Rotation = baseRot,
            Position = new Vector2(toRight ? -280.0f : 2200.0f, y),
        };
        _wzRoot.AddChild(boss);
        var tw = boss.CreateTween();
        // 匀速横穿（缓动会让剪影在屏缘停滞数秒才露头）；只写 x，给纵向漂浮留出 y
        tw.TweenProperty(boss, "position:x", toRight ? 2200.0f : -280.0f, 18.0);
        tw.TweenCallback(Callable.From(boss.QueueFree));

        var half = WzBossDriftPeriod * 0.5f;
        var drift = boss.CreateTween().SetLoops();
        drift.TweenProperty(boss, "position:y", y - WzBossDriftY, half).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        drift.TweenProperty(boss, "position:y", y + WzBossDriftY, half).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        var bankHalf = WzBossBankPeriod * 0.5f;
        var bank = boss.CreateTween().SetLoops();
        bank.TweenProperty(boss, "rotation", baseRot + WzBossBank, bankHalf).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        bank.TweenProperty(boss, "rotation", baseRot - WzBossBank, bankHalf).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>传感扫描：从悬挂机体处向外扩的两圈薄环（读作「机库在扫这片空域」），
    /// 把纵深层 / 远景战场 / 机体三层在时间上缝到一起。环不填、低 alpha：是背景的一次呼吸，不是打击。</summary>
    private void SpawnSweep()
    {
        SpawnSweepRing(520.0f, 2.4f);
        var delay = CreateTween();
        delay.TweenInterval(0.45);
        delay.TweenCallback(Callable.From(() => SpawnSweepRing(720.0f, 2.8f)));
    }

    private void SpawnSweepRing(float radius, float time)
    {
        var wave = CinematicFx.Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = radius,
            ["time"] = time,
            ["color"] = new Color(UITheme.Holo, 0.10f),
            ["core_color"] = new Color(UITheme.HoloPale, 0.13f),
            ["width"] = 3.0f,
        });
        wave.Position = ShipAnchorPos;
        _wzRoot.AddChild(wave);
    }
}
