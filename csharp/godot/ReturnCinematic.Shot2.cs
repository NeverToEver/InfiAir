using Godot;

namespace InfiAir;

/// <summary>
/// 返航过场 镜头 2 构建器（文件粒度拆分自 ReturnCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class ReturnCinematic : CanvasLayer
{

    // ---------------- 镜头 2：传送端口撕裂（1.2s） ----------------

    /// <summary>竖直长圆环从一点撕开扩到全尺寸（0.5s），环缘 12 个 glow 点快速游走；
    /// 环内：口腔软辉光垫 + 双旋涡弧反向搅动 + 环缘粒子内流（环形发射域 + 负径向加速度）；
    /// 环内显现虚影站模糊景象（站体 α0.1 + 水平弥散抖动）；镜头稍推 1.0→1.06。</summary>
    private ReturnCinematicPortalShot BuildShot2()
    {
        var dur = _shotDurations[1];
        var root = new ReturnCinematicPortalShot { Name = "Shot2" };
        root.AddChild(new Starfield()); // M1 起 Starfield 为 C#，typed 实例化
        var push = new Node2D(); // 推镜容器
        root.AddChild(push);
        var pushTween = root.CreateTween();
        pushTween.TweenProperty(push, "scale", Vector2.One * 1.06f, dur).SetTrans(Tween.TransitionType.Sine);
        var center = new Vector2(1150.0f, 430.0f);
        root._center = center;
        // 端口口腔软辉光垫（椭圆压扁，垫在虚影站之下，随撕裂同步张开）
        var mouth = SoftGlow(32.0f, new Color(0.0f, 0.7f, 1.0f, 0.15f));
        mouth.Position = center;
        var mouthScale = new Vector2(root._rx * 0.95f / 32.0f, root._ry * 0.95f / 32.0f);
        mouth.Scale = mouthScale * 0.02f;
        push.AddChild(mouth);
        // 内部景象先露（环张开时透出）：虚影站极简版（小比例 + 低 alpha + 弥散抖动）
        var inner = DawnStation.Build(1); // DawnStation.Mode.PHANTOM（0=DESTROYED, 1=PHANTOM）
        inner.Scale = Vector2.One * 0.28f;
        inner.Position = center;
        var innerMod = inner.Modulate;
        innerMod.A = 0.35f;
        inner.Modulate = innerMod;
        push.AddChild(inner);
        root._inner_station = inner;
        root._inner_base_x = center.X;
        // 端口环：亮芯 + 叠加态外晕，从一点撕开（scale 0.02 → 1，0.5s）
        var ringPoints = new Vector2[64];
        for (var i = 0; i < 64; i++)
        {
            var a = Mathf.Tau * i / 64.0f;
            ringPoints[i] = new Vector2(Mathf.Cos(a) * root._rx, Mathf.Sin(a) * root._ry);
        }

        var ringGlow = Line(ringPoints, new Color(0.0f, 0.83f, 1.0f, 0.25f), 12.0f);
        ringGlow.Closed = true;
        ringGlow.Position = center;
        var glowMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        ringGlow.Material = glowMat;
        push.AddChild(ringGlow);
        var ring = Line(ringPoints, new Color(0.0f, 0.9f, 1.0f, 0.9f), 4.0f);
        ring.Closed = true;
        ring.Position = center;
        push.AddChild(ring);
        foreach (var r in new[] { ringGlow, ring })
        {
            r.Scale = Vector2.One * 0.02f;
        }

        // 环内旋涡弧 ×2（各约 200° 弧段，反向旋转，随撕裂同步张开；_process 仅累加 rotation）
        for (var k = 0; k < 2; k++)
        {
            var rr = 0.55f + 0.25f * k;
            var swirlPoints = new Vector2[28];
            for (var i = 0; i < 28; i++)
            {
                var a = -Mathf.Tau * 100.0f / 360.0f + Mathf.Tau * 200.0f / 360.0f * i / 27.0f;
                swirlPoints[i] = new Vector2(Mathf.Cos(a) * root._rx * rr, Mathf.Sin(a) * root._ry * rr);
            }

            var swirl = Line(swirlPoints, new Color(0.3f, 0.9f, 1.0f, 0.3f), 3.0f);
            swirl.Position = center;
            swirl.Scale = Vector2.One * 0.02f;
            var swirlMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            swirl.Material = swirlMat;
            push.AddChild(swirl);
            root._swirls.Add(swirl);
            root._swirl_speeds.Add(k == 0 ? 2.4f : -1.7f);
        }

        var tear = root.CreateTween().SetParallel(true);
        tear.TweenProperty(ring, "scale", Vector2.One, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tear.TweenProperty(ringGlow, "scale", Vector2.One, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tear.TweenProperty(mouth, "scale", mouthScale, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        foreach (var swirl in root._swirls)
        {
            tear.TweenProperty(swirl, "scale", Vector2.One, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }

        // 环缘能量翻涌：12 个小 glow 点（本镜头唯一 _process 逐帧换位）
        for (var i = 0; i < 12; i++)
        {
            var dot = Glow(3.5f, new Color(0.5f, 0.95f, 1.0f, 0.9f));
            push.AddChild(dot);
            root._dots.Add(dot);
        }

        // 环缘内流粒子：环形发射域（参考 DawnStation 数据流配方）+ 负径向加速度拉向环心；
        // 节点 y 压扁成椭圆贴合端口；撕裂近完成（0.45s）才开始发射
        var inflow = Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 40,
            ["lifetime"] = 0.9f,
            ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
            ["spread"] = 180.0f,
            ["vel_min"] = 0.0f,
            ["vel_max"] = 15.0f,
            ["scale_min"] = 2.0f,
            ["scale_max"] = 4.0f,
            ["color"] = new Color(0.5f, 0.9f, 1.0f, 0.45f),
        });
        var inflowMat = (ParticleProcessMaterial)inflow.ProcessMaterial;
        inflowMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
        inflowMat.EmissionRingAxis = new Vector3(0.0f, 0.0f, 1.0f);
        inflowMat.EmissionRingRadius = root._rx;
        inflowMat.EmissionRingInnerRadius = root._rx * 0.8f;
        inflowMat.EmissionRingHeight = 0.0f;
        inflowMat.RadialAccelMin = -140.0f;
        inflowMat.RadialAccelMax = -90.0f;
        inflow.Position = center;
        inflow.Scale = new Vector2(1.0f, root._ry / root._rx);
        inflow.Emitting = false;
        push.AddChild(inflow);
        Once(root, 0.45f, Callable.From(() => inflow.Emitting = true));
        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION, -12.0f, 0.5f); // 0.5 倍速低沉撕裂感
        return root;
    }
}
