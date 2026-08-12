using Godot;

namespace InfiAir;

/// <summary>
/// 返航过场 镜头 1 构建器（文件粒度拆分自 ReturnCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class ReturnCinematic : CanvasLayer
{

    // ---------------- 镜头 1：曲率充能（1.6s） ----------------
    /// <summary>深空战机悬停画面中心偏下；喷口辉光暗红 (0.5,0.1,0.05) → 炽白 (1,0.95,0.85) 并放大 1→1.8；
    /// 机身周围能量粒子向内收束（负速度）；结尾 0.4s 同心细环微缩脉动（空间扭曲前兆）。</summary>
    private Node2D BuildShot1()
    {
        var dur = _shotDurations[0];
        var root = new Node2D { Name = "Shot1" };
        var starfield = new Starfield(); // M1 起 Starfield 为 C#，typed 实例化
        starfield.Warp(12.0f); // 承接对局 _starfield.Warp(18.0) 的星光拉伸（自身 lerp 衰减回 1）
        root.AddChild(starfield);
        var neb1 = Glow(480.0f, new Color(0.08f, 0.18f, 0.4f, 0.05f));
        neb1.Position = new Vector2(360.0f, 800.0f);
        root.AddChild(neb1);
        var neb2 = Glow(420.0f, new Color(0.1f, 0.3f, 0.45f, 0.05f));
        neb2.Position = new Vector2(1520.0f, 280.0f);
        root.AddChild(neb2);
        // 战机尾部视角悬停（同开场镜头 5 摆位）
        var ship = new Sprite2D
        {
            Texture = _playerShip,
            Scale = Vector2.One * 1.4f,
            Position = new Vector2(960.0f, 560.0f),
        };
        root.AddChild(ship);
        // 喷口充能：双层软辉光暗红 → 炽白放大
        foreach (var side in new[] { -46.0f, 46.0f })
        {
            var halo = SoftGlow(42.0f, new Color(0.9f, 0.4f, 0.15f, 0.2f));
            halo.Position = new Vector2(960.0f + side, 648.0f);
            root.AddChild(halo);
            var nozzle = SoftGlow(15.0f, new Color(1.0f, 1.0f, 1.0f, 0.9f));
            nozzle.Position = new Vector2(960.0f + side, 644.0f);
            nozzle.Modulate = new Color(0.5f, 0.1f, 0.05f);
            root.AddChild(nozzle);
            var nozzleBase = nozzle.Scale;
            var haloBase = halo.Scale;
            var charge = root.CreateTween().SetParallel(true);
            charge.TweenProperty(nozzle, "modulate", new Color(1.0f, 0.95f, 0.85f), dur * 0.8f);
            charge.TweenProperty(nozzle, "scale", nozzleBase * 1.8f, dur * 0.8f);
            charge.TweenProperty(halo, "scale", haloBase * 1.5f, dur * 0.8f);
        }

        // 能量粒子向内收束（负速度朝发射点汇聚）
        var inbound = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 48,
            ["lifetime"] = 1.2f,
            ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
            ["spread"] = 180.0f,
            ["vel_min"] = -70.0f,
            ["vel_max"] = -25.0f,
            ["scale_min"] = 1.5f,
            ["scale_max"] = 3.0f,
            ["color"] = new Color(0.6f, 0.85f, 1.0f, 0.5f),
        });
        inbound.Position = new Vector2(960.0f, 580.0f);
        root.AddChild(inbound);
        // 充能峰值（结尾 0.4s）：两道错位同心冲击环掠过观察者（曲率引擎点火感）
        Once(root, dur - 0.4f, Callable.From(() =>
        {
            var wave1 = CinematicFx.Shockwave(new Godot.Collections.Dictionary
            {
                ["radius"] = 700.0f,
                ["time"] = 0.5f,
                ["ry_ratio"] = 0.6f,
                ["color"] = new Color(0.0f, 0.83f, 1.0f, 0.35f),
                ["core_color"] = new Color(0.7f, 0.97f, 1.0f, 0.7f),
                ["width"] = 12.0f,
            });
            wave1.Position = new Vector2(960.0f, 570.0f);
            root.AddChild(wave1);
            Once(root, 0.14f, Callable.From(() =>
            {
                var wave2 = CinematicFx.Shockwave(new Godot.Collections.Dictionary
                {
                    ["radius"] = 900.0f,
                    ["time"] = 0.5f,
                    ["ry_ratio"] = 0.6f,
                    ["color"] = new Color(0.0f, 0.83f, 1.0f, 0.3f),
                    ["core_color"] = new Color(0.6f, 0.95f, 1.0f, 0.6f),
                    ["width"] = 10.0f,
                });
                wave2.Position = new Vector2(960.0f, 570.0f);
                root.AddChild(wave2);
            }));
        }));
        // 结尾 0.4s：画面中心同心细环微缩脉动（空间扭曲）
        for (var k = 0; k < 3; k++)
        {
            var ringPoints = new Vector2[40];
            for (var i = 0; i < 40; i++)
            {
                var a = Mathf.Tau * i / 40.0f;
                ringPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (40.0f + 30.0f * k);
            }

            var ring = Line(ringPoints, new Color(0.6f, 0.9f, 1.0f, 0.0f), 2.0f);
            ring.Closed = true;
            ring.Position = new Vector2(960.0f, 480.0f);
            var ringMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            ring.Material = ringMat;
            root.AddChild(ring);
            var warpIn = root.CreateTween();
            warpIn.TweenInterval(dur - 0.4f + 0.08f * k);
            warpIn.TweenProperty(ring, "modulate:a", 0.55f, 0.15f);
            warpIn.Parallel().TweenProperty(ring, "scale", Vector2.One * 0.85f, 0.25f);
        }

        GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -6.0f, 0.6f); // 0.6 倍速拉长为充能上升感
        return root;
    }
}
