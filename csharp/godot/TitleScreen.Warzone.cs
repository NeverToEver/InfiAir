using Godot;

namespace InfiAir;

/// <summary>
/// 标题屏·远景实况战场部分：敌机编队视差掠过 + 远处爆炸闪光（低频）+ Boss 剪影巡航。
/// 全部小尺寸/低透明度（大气透视，只做背景氛围）；节点池 + Godot.Timer（OneShot + 随机重启），
/// _Process 仅原地推进活跃 sprite 位置，零分配。层级：WarzoneRoot 紧随星空（机体与 UI 之下）。
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    private const int WzShipPool = 9; // 编队同屏上限：~3 编队 × 2-3 架，池耗尽时静默少生成

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

    private Node2D _wzRoot = null!;
    private readonly Node2D[] _wzShips = new Node2D[WzShipPool];
    private readonly Sprite2D[] _wzBodies = new Sprite2D[WzShipPool];
    private readonly Vector2[] _wzVel = new Vector2[WzShipPool];
    private readonly Texture2D[] _enemyTexs = new Texture2D[4];
    private readonly Texture2D[] _bossTexs = new Texture2D[4];

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

        ScheduleWz(3.0f, 5.0f, 8.0f, SpawnFormation);
        ScheduleWz(2.5f, 4.5f, 7.5f, SpawnExplosion);
        ScheduleWz(9.0f, 22.0f, 32.0f, SpawnBossCruise);
    }

    /// <summary>OneShot Timer + 随机间隔重启（改 WaitTime 不碰当前周期，无当期歧义）。</summary>
    private void ScheduleWz(float firstDelay, float minInterval, float maxInterval, System.Action spawn)
    {
        var timer = new Godot.Timer { OneShot = true, WaitTime = firstDelay, Autostart = true };
        _wzRoot.AddChild(timer);
        timer.Timeout += () =>
        {
            spawn();
            timer.WaitTime = (float)GD.RandRange(minInterval, maxInterval);
            timer.Start();
        };
    }

    public override void _Process(double delta)
    {
        TickBalanceReload(delta);
        var d = (float)delta;
        for (var i = 0; i < WzShipPool; i++)
        {
            var ship = _wzShips[i];
            if (!ship.Visible)
            {
                continue;
            }

            ship.Position += _wzVel[i] * d;
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

    /// <summary>敌机编队：随机朝向/高度带/机型，远小慢近大快（视差纵深），纵向错列。</summary>
    private void SpawnFormation()
    {
        var toRight = GD.Randf() < 0.5f;
        var count = GD.Randf() < 0.4f ? 3 : 2;
        var tex = _enemyTexs[GD.RandRange(0, 3)];
        var scale = (float)GD.RandRange(0.2, 0.36);
        var speed = 40.0f + scale * 290.0f;
        var dir = toRight ? 1.0f : -1.0f;
        var y = (float)GD.RandRange(140.0, 700.0);
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

            var ship = _wzShips[idx];
            _wzBodies[idx].Texture = tex;
            _wzBodies[idx].Modulate = UITheme.HostileCool;
            ship.Scale = Vector2.One * scale;
            ship.Rotation = rot;
            var yOff = j == 0 ? 0.0f : (j == 1 ? 26.0f : -26.0f);
            ship.Position = new Vector2(x0 - dir * 78.0f * j, y + yOff);
            _wzVel[idx] = new Vector2(dir * speed, vy);
            ship.Visible = true;
        }
    }

    /// <summary>远处交火：暖色闪光涨落 + 60% 概率小型冲击环，自毁。</summary>
    private void SpawnExplosion()
    {
        var pos = new Vector2((float)GD.RandRange(160.0, 1760.0), (float)GD.RandRange(120.0, 560.0));
        var flash = CinematicFx.SoftGlow((float)GD.RandRange(60.0, 110.0), new Color(1.0f, 0.55f, 0.25f, 0.0f));
        flash.Position = pos;
        _wzRoot.AddChild(flash);
        var tw = flash.CreateTween();
        tw.TweenProperty(flash, "modulate:a", 0.5f, 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(flash, "modulate:a", 0.0f, 0.7).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(flash.QueueFree));

        if (GD.Randf() < 0.6f)
        {
            var shockwave = CinematicFx.Shockwave(new Godot.Collections.Dictionary
            {
                ["radius"] = (float)GD.RandRange(70.0, 120.0),
                ["time"] = 0.55f,
                ["color"] = new Color(1.0f, 0.6f, 0.3f, 0.32f),
                ["core_color"] = new Color(1.0f, 0.85f, 0.6f, 0.4f),
                ["width"] = 6.0f,
            });
            shockwave.Position = pos;
            _wzRoot.AddChild(shockwave);
        }
    }

    /// <summary>Boss 剪影：暗蓝调大机型缓慢横穿上部空域（~18s），自毁。
    /// 横向匀速之外附加纵向漂浮 + 微幅侧倾（分属性 tween 并行，互不覆盖）。</summary>
    private void SpawnBossCruise()
    {
        var tex = _bossTexs[GD.RandRange(0, 3)];
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
}
