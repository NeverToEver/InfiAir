using Godot;

namespace InfiAir;

/// <summary>
/// 开场过场 镜头 1 构建器（文件粒度拆分自 IntroCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class IntroCinematic : CanvasLayer
{

    // ---------------- 镜头 1：远景推近（2.8s） ----------------
    /// <summary>环形空间站深空爆炸：星空底 + 远处淡星云 + 弧段/舱段拼装环体（分块/外廓细节线），
    /// 容器 0.7→1.0 匀速推近；爆炸核心辉光暗红→橙白放大 + 冲击波扩散环，
    /// 碎片外抛 + 前景尘埃双层粒子 + 破口剥落碎块。</summary>
    private Node2D BuildShot1()
    {
        var dur = _shotDurations[0];
        var root = new Node2D { Name = "Shot1" };
        root.AddChild(new Starfield());  // M1 起 Starfield 为 C#，typed 实例化（原经脚本资源，M6 重定型）
        // 远处星云（比镜头 6 更淡，只铺层次；软径向光晕消除硬边）
        var neb1 = CinematicFx.SoftGlow(520.0f, new Color(0.2f, 0.12f, 0.4f, 0.08f));
        neb1.Position = new Vector2(1560.0f, 260.0f);
        root.AddChild(neb1);
        var neb2 = CinematicFx.SoftGlow(420.0f, new Color(0.08f, 0.18f, 0.4f, 0.08f));
        neb2.Position = new Vector2(300.0f, 840.0f);
        root.AddChild(neb2);

        // 站体构件抽为 DawnStation 共享构建函数（开场=实体毁灭态，纯提取不改视觉；
        // 返航/基地背景复用虚影态，docs/RETURN_HOME_CINEMATIC.md §5）
        var station = DawnStation.Build(DawnStation.Mode.Destroyed);
        station.Position = new Vector2(960.0f, 470.0f);
        station.Scale = Vector2.One * 0.7f;
        root.AddChild(station);

        // 爆炸核心：破口处叠加辉光，暗红 → 橙白并放大（软光晕；scale tween 以 soft_glow 基准缩放为底）
        var blastPos = new Vector2(Mathf.Cos(0.85f), Mathf.Sin(0.85f)) * 260.0f;
        var halo = CinematicFx.SoftGlow(150.0f, new Color(0.6f, 0.2f, 0.08f, 0.35f));
        halo.Position = blastPos;
        var haloBase = halo.Scale;
        station.AddChild(halo);
        var core = CinematicFx.SoftGlow(80.0f, new Color(1.0f, 1.0f, 1.0f, 0.95f));
        core.Position = blastPos;
        core.Modulate = new Color(0.5f, 0.1f, 0.05f);
        var coreBase = core.Scale;
        station.AddChild(core);
        var tween = root.CreateTween().SetParallel(true);
        tween.TweenProperty(station, "scale", Vector2.One, dur).SetTrans(Tween.TransitionType.Linear);
        tween.TweenProperty(core, "modulate", new Color(1.0f, 0.9f, 0.7f), dur * 0.8f);
        tween.TweenProperty(core, "scale", coreBase * 2.4f, dur);
        tween.TweenProperty(halo, "scale", haloBase * 1.8f, dur);

        // 舱段模块舷窗灯 ×8：随爆炸吞噬站体，按距破口角距由近到远逐盏熄灭（错峰 tween 延迟贯穿全镜头）
        var lightSeq = new[] { 1.571f, 0.0f, 2.356f, 5.497f, 3.142f, 4.712f, 3.927f, -1.0f };  // 舱段角（rad），-1 = 中心毂
        for (var lI = 0; lI < lightSeq.Length; lI++)
        {
            var lampA = lightSeq[lI];
            var lamp = Glow(4.5f, new Color(1.0f, 0.75f, 0.4f, 0.85f));
            lamp.Position = lampA < 0.0f ? Vector2.Zero : new Vector2(Mathf.Cos(lampA), Mathf.Sin(lampA)) * 260.0f;
            station.AddChild(lamp);
            var lampT = root.CreateTween();
            lampT.TweenInterval(dur * (0.22f + 0.07f * lI));
            lampT.TweenProperty(lamp, "modulate:a", 0.05f, 0.35);
        }

        // 冲击波扩散环：爆心薄环急速扩大并淡出（叠加态，错开 0.3s 两波）
        var waveShakeState = new Godot.Collections.Array { default(Variant) };  // 两波主爆颤动共享刷新
        for (var wave = 0; wave < 2; wave++)
        {
            var wavePoints = new Vector2[40];
            for (var i = 0; i < 40; i++)
            {
                var a = Mathf.Tau * i / 40.0f;
                wavePoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 60.0f;
            }

            var waveRing = Line(wavePoints, new Color(1.0f, 0.7f, 0.35f, 0.7f), 5.0f);
            waveRing.Closed = true;
            waveRing.Position = blastPos;
            waveRing.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            waveRing.Scale = Vector2.One * 0.2f;
            station.AddChild(waveRing);
            var wt = root.CreateTween();
            wt.TweenInterval(0.2f + 0.3f * wave);
            // 2026-08-03 审计：扩散与淡出同步起播（原 parallel 模式把淡出从 tween 起点开始，
            // 第二波环在扩散中段即不可见）；改为 interval 后 scale/alpha 并行（与镜头 2 ripple 同款）
            wt.TweenProperty(waveRing, "scale", Vector2.One * 4.5f, 0.9).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            wt.Parallel().TweenProperty(waveRing, "modulate:a", 0.0f, 0.8);
            // 起爆同步一次主爆颤动（幅度略大于镜头 2 单节点）
            var kt = root.CreateTween();
            kt.TweenInterval(0.2f + 0.3f * wave);
            kt.TweenCallback(Callable.From(() => KickShake(root, 6.0f, waveShakeState)));
        }

        // 二次殉爆（dur*0.45 起）：破口对侧环缘小型闪爆——软闪光核 + 冲击波扩散环 + 颤动 + 递减音量
        var blast2Pos = new Vector2(Mathf.Cos(3.8f), Mathf.Sin(3.8f)) * 240.0f;
        var boom2 = new Godot.Timer { OneShot = true, WaitTime = dur * 0.45f, Autostart = true };
        root.AddChild(boom2);
        boom2.Timeout += () =>
        {
            var flash2 = CinematicFx.SoftGlow(56.0f, new Color(1.0f, 0.85f, 0.6f, 0.9f));
            flash2.Position = blast2Pos;
            var flash2Base = flash2.Scale;
            flash2.Scale = Vector2.Zero;
            station.AddChild(flash2);
            var f2t = root.CreateTween();
            f2t.TweenProperty(flash2, "scale", flash2Base * 1.3f, 0.1);
            f2t.TweenProperty(flash2, "modulate:a", 0.0f, 0.4);
            f2t.TweenCallback(Callable.From(flash2.QueueFree));
            var wave2 = CinematicFx.Shockwave(new Godot.Collections.Dictionary
            {
                ["radius"] = 200.0f,
                ["time"] = 0.7f,
                ["color"] = new Color(1.0f, 0.6f, 0.25f, 0.55f),
                ["core_color"] = new Color(1.0f, 0.9f, 0.7f, 0.85f),
                ["width"] = 10.0f,
            });
            wave2.Position = blast2Pos;
            station.AddChild(wave2);
            KickShake(root, 5.0f, waveShakeState);
            GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -8.0f + AudioVolOffset, AudioPitch);
        };

        // 余烬：全镜头持续的橙色慢速上飘细屑（低透明度，燃烧余韵层）
        var embers = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 40,
            ["lifetime"] = 3.2f,
            ["vel_min"] = 12.0f,
            ["vel_max"] = 40.0f,
            ["spread"] = 35.0f,
            ["scale_min"] = 3.0f,
            ["scale_max"] = 6.0f,
            ["color"] = new Color(1.0f, 0.5f, 0.15f, 0.3f),
        });
        embers.Position = new Vector2(960.0f, 500.0f);
        root.AddChild(embers);

        // 碎片（高速外抛）+ 尘埃（慢速前景低透明度）
        var debris = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 48,
            ["lifetime"] = 2.2f,
            ["vel_min"] = 160.0f,
            ["vel_max"] = 420.0f,
            ["damping_min"] = 60.0f,
            ["damping_max"] = 140.0f,
            ["scale_min"] = 3.0f,
            ["scale_max"] = 7.0f,
            ["color"] = new Color(1.0f, 0.55f, 0.15f),
        });
        debris.Position = new Vector2(960.0f, 470.0f) + blastPos * 0.7f;
        root.AddChild(debris);
        var dust = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 40,
            ["lifetime"] = 3.5f,
            ["vel_min"] = 20.0f,
            ["vel_max"] = 60.0f,
            ["scale_min"] = 6.0f,
            ["scale_max"] = 12.0f,
            ["color"] = new Color(0.6f, 0.5f, 0.4f, 0.12f),
        });
        dust.Position = new Vector2(960.0f, 540.0f);
        root.AddChild(dust);
        // 前景碎片层：深色大块残骸横向漂移 + 翻滚（比站体更快的近景视差）
        for (var k = 0; k < 4; k++)
        {
            var driftShard = new Polygon2D
            {
                Polygon = new[] { new Vector2(-26.0f, -10.0f), new Vector2(20.0f, -18.0f), new Vector2(30.0f, 12.0f), new Vector2(-12.0f, 20.0f) },
                Color = new Color(0.04f, 0.04f, 0.07f),
                Position = new Vector2(260.0f + 470.0f * k, 180.0f + 700.0f * (k % 2)),
                Scale = Vector2.One * (0.8f + 0.25f * (k % 3)),
            };
            root.AddChild(driftShard);
            driftShard.AddChild(Line(new[] { new Vector2(-26.0f, -10.0f), new Vector2(20.0f, -18.0f) }, new Color(1.0f, 0.6f, 0.25f, 0.4f), 2.0f));
            // 顶缘暖色软轮廓光（朝向爆心一侧的反射光，把剪影从深空里托出来）
            var shardRim = CinematicFx.SoftGlow(18.0f, new Color(1.0f, 0.55f, 0.2f, 0.22f));
            shardRim.Position = new Vector2(0.0f, -12.0f);
            driftShard.AddChild(shardRim);
            var spin = root.CreateTween().SetLoops();
            spin.TweenProperty(driftShard, "rotation", driftShard.Rotation + Mathf.Tau, 9.0f + 3.0f * k);
            var move = root.CreateTween().SetLoops();
            move.TweenProperty(driftShard, "position", driftShard.Position + new Vector2(140.0f + 60.0f * k, -30.0f), 4.0f + k).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            move.TweenProperty(driftShard, "position", driftShard.Position, 4.0f + k).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION_BIG, AudioVolOffset, AudioPitch);
        return root;
    }
}
