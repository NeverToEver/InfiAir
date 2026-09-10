using Godot;

namespace InfiAir;

/// <summary>
/// 共享「多段式飞行服乘员」构件工厂（开场/返航过场共用）：把原先两处近乎重复的
/// 简笔人物（圆头 + 棍状四肢 + 平板躯干）重建为有设计细节的宇航服形象——
/// 分件头盔（壳/面罩玻璃/颈环/侧通讯舱/天线/下颌护板）、分层胸甲与背带扣具、
/// 双筒维生背包（罐体/喷嘴/供气管/压力表/散热格栅）、肩部叠甲与铆钉、
/// 关节环/护膝/护腕/手套分指/战术靴底纹、全身边缘走线与琥珀状态灯。
///
/// 契约与旧 BuildPerson 完全一致（关节位置/长度逐位不变，动画相位公式照旧生效）：
/// 返回 {node, hips[2], knees[2], shoulders[2], elbows[2], torso, eyelid}。
/// 面朝 +x；眼睛面甲特写依赖 eyelid 的位置/尺寸不变。
/// 关节数不变 => 步行/握姿/呼吸驱动的公式与幅度无需改动。
/// </summary>
public static class CrewFigure
{
    // 套装分层（冷钢蓝灰为主，琥珀/青只作状态灯与面罩高光，双方镜头各自调性下都成立）
    private static readonly Color SuitNear = new(0.26f, 0.31f, 0.41f);
    private static readonly Color SuitFar = new(0.14f, 0.18f, 0.26f);
    private static readonly Color SuitMid = new(0.20f, 0.25f, 0.34f);
    private static readonly Color Plate = new(0.23f, 0.29f, 0.39f);
    private static readonly Color Metal = new(0.36f, 0.42f, 0.52f);
    private static readonly Color Edge = new(0.56f, 0.67f, 0.86f, 0.75f);
    private static readonly Color EdgeSoft = new(0.40f, 0.50f, 0.68f, 0.45f);
    private static readonly Color Seam = new(0.07f, 0.09f, 0.14f, 0.85f);
    private static readonly Color Glass = new(0.06f, 0.11f, 0.17f);
    private static readonly Color GlowCyan = new(0.5f, 0.9f, 1.0f, 0.9f);

    /// <summary>构建乘员。accent = 状态灯/装备提示色（默认琥珀，返航可传冷青）。</summary>
    public static Godot.Collections.Dictionary Build(Color? accent = null)
    {
        var lamp = accent ?? new Color(1.0f, 0.72f, 0.28f);
        var person = new Node2D { Name = "Crew" };
        var hips = new Godot.Collections.Array();
        var knees = new Godot.Collections.Array();
        var shoulders = new Godot.Collections.Array();
        var elbows = new Godot.Collections.Array();

        // ---------------- 腿部（远侧先画） ----------------
        foreach (var sideI in new[] { 1, 0 })
        {
            var c = sideI == 0 ? SuitNear : SuitFar;
            var hip = new Node2D { Position = new Vector2(2.0f - 4.0f * sideI, -4.0f + 2.0f * sideI) };
            person.AddChild(hip);
            // 大腿：主段 + 外侧护板 + 分件走线 + 髋侧装备袋
            var thigh = CinematicFx.RectPoly(6.5f, 22.0f, c);
            thigh.Position = new Vector2(0.0f, 11.0f);
            hip.AddChild(thigh);
            thigh.AddChild(CinematicFx.Line(new[] { new Vector2(2.6f, -9.0f), new Vector2(2.6f, 9.0f) }, Edge, 1.2f));
            var thighPlate = CinematicFx.RectPoly(7.4f, 12.0f, sideI == 0 ? SuitMid : SuitFar);
            thighPlate.Position = new Vector2(-0.4f, 4.0f);
            thighPlate.Color = new Color(thighPlate.Color, 0.6f);
            thigh.AddChild(thighPlate);
            thigh.AddChild(CinematicFx.Line(new[] { new Vector2(-2.6f, -8.0f), new Vector2(-2.6f, 8.0f) }, Seam, 1.0f));
            var hipPouch = CinematicFx.RectPoly(3.4f, 5.0f, SuitFar);
            hipPouch.Position = new Vector2(-4.0f, 3.0f);
            hip.AddChild(hipPouch);

            var knee = new Node2D { Position = new Vector2(0.0f, 22.0f) };
            hip.AddChild(knee);
            // 护膝：胶囊状装甲盖 + 顶缘受光线
            var kneeCap = CinematicFx.RectPoly(6.0f, 5.0f, Plate);
            kneeCap.Position = new Vector2(0.4f, 0.4f);
            knee.AddChild(kneeCap);
            knee.AddChild(CinematicFx.Line(new[] { new Vector2(-2.4f, -1.6f), new Vector2(3.0f, -1.6f) }, EdgeSoft, 1.0f));
            var shin = CinematicFx.RectPoly(5.0f, 20.0f, c);
            shin.Position = new Vector2(0.0f, 10.0f);
            knee.AddChild(shin);
            // 胫甲 + 关节环 + 踝箍
            var shinGuard = CinematicFx.RectPoly(5.6f, 13.0f, sideI == 0 ? SuitMid : SuitFar);
            shinGuard.Position = new Vector2(0.5f, 11.0f);
            shinGuard.Color = new Color(shinGuard.Color, 0.7f);
            knee.AddChild(shinGuard);
            knee.AddChild(CinematicFx.Line(new[] { new Vector2(-2.5f, 3.0f), new Vector2(2.5f, 3.0f) }, Seam, 1.0f));
            knee.AddChild(CinematicFx.Line(new[] { new Vector2(-2.5f, 16.5f), new Vector2(2.5f, 16.5f) }, Seam, 1.2f));
            // 战术靴：靴身 + 鞋头 + 靴底齿纹 + 踝侧扣
            var boot = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-4.0f, 16.0f), new Vector2(7.0f, 16.0f), new Vector2(10.0f, 20.5f),
                    new Vector2(9.5f, 22.5f), new Vector2(-4.5f, 22.5f),
                },
                Color = c,
            };
            knee.AddChild(boot);
            var toe = new Polygon2D
            {
                Polygon = new[] { new Vector2(6.0f, 20.0f), new Vector2(10.0f, 20.5f), new Vector2(9.5f, 22.5f), new Vector2(5.5f, 22.5f) },
                Color = Metal,
            };
            knee.AddChild(toe);
            knee.AddChild(CinematicFx.Line(new[] { new Vector2(-4.5f, 22.6f), new Vector2(9.6f, 22.6f) }, Edge, 1.6f));
            for (var t = 0; t < 3; t++)
            {
                knee.AddChild(CinematicFx.Line(
                    new[] { new Vector2(-3.5f + 4.0f * t, 22.6f), new Vector2(-2.5f + 4.0f * t, 21.4f) }, Seam, 1.0f));
            }

            var ankleBuckle = new GlowDot { Radius = 1.6f, DotColor = new Color(lamp, 0.85f), Position = new Vector2(-3.6f, 18.0f) };
            knee.AddChild(ankleBuckle);

            hips.Add(Variant.From(hip));
            knees.Add(Variant.From(knee));
        }

        // ---------------- 躯干组 ----------------
        var torsoGrp = new Node2D();
        person.AddChild(torsoGrp);

        // 骨盆护甲 + 腰封 + 髋铰环
        var pelvis = new Polygon2D
        {
            Polygon = new[] { new Vector2(-9.0f, -2.0f), new Vector2(7.0f, -4.0f), new Vector2(9.0f, -14.0f), new Vector2(-7.0f, -14.0f) },
            Color = SuitNear,
        };
        torsoGrp.AddChild(pelvis);
        torsoGrp.AddChild(CinematicFx.Line(new[] { new Vector2(-8.5f, -3.0f), new Vector2(8.0f, -5.0f) }, EdgeSoft, 1.1f));
        var belt = CinematicFx.RectPoly(17.0f, 4.0f, SuitMid);
        belt.Position = new Vector2(0.0f, -12.0f);
        torsoGrp.AddChild(belt);
        var buckle = CinematicFx.RectPoly(4.0f, 3.0f, Metal);
        buckle.Position = new Vector2(6.0f, -12.0f);
        torsoGrp.AddChild(buckle);

        // 胸廓 + 分层胸甲 + 背带 + 扣具 + 胸包（含状态灯）
        var chestPoints = new[] { new Vector2(-7.0f, -14.0f), new Vector2(13.0f, -16.0f), new Vector2(17.0f, -42.0f), new Vector2(-3.0f, -46.0f) };
        var chest = new Polygon2D { Polygon = chestPoints, Color = SuitNear };
        torsoGrp.AddChild(chest);
        var chestPlate = new Polygon2D
        {
            Polygon = new[] { new Vector2(-3.0f, -18.0f), new Vector2(13.5f, -20.0f), new Vector2(16.0f, -38.0f), new Vector2(0.0f, -41.0f) },
            Color = new Color(Plate, 0.85f),
        };
        torsoGrp.AddChild(chestPlate);
        torsoGrp.AddChild(CinematicFx.Line(new[] { new Vector2(13.0f, -16.0f), new Vector2(17.0f, -42.0f) }, Edge, 1.6f));
        torsoGrp.AddChild(CinematicFx.Line(new[] { new Vector2(-3.0f, -20.0f), new Vector2(12.0f, -22.0f) }, Seam, 1.1f));
        // 背带：肩至腰两条 + 横扣
        torsoGrp.AddChild(CinematicFx.Line(new[] { new Vector2(2.0f, -44.0f), new Vector2(3.5f, -16.0f) }, Seam, 2.4f));
        torsoGrp.AddChild(CinematicFx.Line(new[] { new Vector2(11.0f, -45.0f), new Vector2(9.0f, -14.0f) }, Seam, 2.0f));
        torsoGrp.AddChild(CinematicFx.Line(new[] { new Vector2(1.0f, -30.0f), new Vector2(12.0f, -31.0f) }, EdgeSoft, 1.2f));
        var chestPack = CinematicFx.RectPoly(6.0f, 9.0f, new Color(0.30f, 0.38f, 0.50f));
        chestPack.Position = new Vector2(11.0f, -28.0f);
        torsoGrp.AddChild(chestPack);
        torsoGrp.AddChild(CinematicFx.Line(new[] { new Vector2(8.4f, -32.0f), new Vector2(8.4f, -24.0f) }, EdgeSoft, 1.0f));
        for (var b = 0; b < 3; b++)
        {
            var bl = new GlowDot { Radius = 1.3f, DotColor = new Color(lamp, 0.9f), Position = new Vector2(9.6f, -31.5f + 3.0f * b) };
            torsoGrp.AddChild(bl);
        }

        // 肩部叠甲（两层）+ 铆钉
        var shoulderPad2 = new Polygon2D
        {
            Polygon = new[] { new Vector2(-6.0f, -53.0f), new Vector2(11.0f, -55.0f), new Vector2(13.0f, -46.0f), new Vector2(-4.0f, -44.0f) },
            Color = SuitMid,
        };
        torsoGrp.AddChild(shoulderPad2);
        var shoulderPad = new Polygon2D
        {
            Polygon = new[] { new Vector2(-4.0f, -52.0f), new Vector2(10.0f, -54.0f), new Vector2(12.0f, -44.0f), new Vector2(-2.0f, -43.0f) },
            Color = Plate,
        };
        torsoGrp.AddChild(shoulderPad);
        torsoGrp.AddChild(CinematicFx.Line(new[] { new Vector2(-3.0f, -51.0f), new Vector2(11.0f, -53.0f) }, Edge, 1.2f));
        foreach (var bp in new[] { new Vector2(-2.0f, -50.5f), new Vector2(2.0f, -51.2f), new Vector2(6.0f, -52.0f), new Vector2(10.0f, -52.8f) })
        {
            torsoGrp.AddChild(new GlowDot { Radius = 0.9f, DotColor = new Color(0.75f, 0.82f, 0.92f, 0.8f), Position = bp });
        }

        // 颈环 + 颈段
        var collar = CinematicFx.RectPoly(8.0f, 3.0f, SuitMid);
        collar.Position = new Vector2(7.5f, -48.0f);
        torsoGrp.AddChild(collar);
        var neck = CinematicFx.RectPoly(4.0f, 7.0f, SuitNear);
        neck.Position = new Vector2(8.0f, -51.0f);
        torsoGrp.AddChild(neck);

        // ---------------- 头盔（分件：壳 / 面罩 / 颈环 / 侧舱 / 天线 / 下颌护板） ----------------
        var helmetGrp = new Node2D { Position = new Vector2(11.0f, -62.0f) };
        torsoGrp.AddChild(helmetGrp);
        // 壳：略带棱面的圆（12 边）+ 后脑高盖
        var shellPts = new Vector2[12];
        for (var i = 0; i < 12; i++)
        {
            var a = Mathf.Tau * i / 12.0f;
            var r = 10.6f + (Mathf.Cos(a + 0.6f) > 0.0f ? 0.0f : 0.5f);
            shellPts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.98f);
        }

        var shell = new Polygon2D { Polygon = shellPts, Color = SuitNear };
        helmetGrp.AddChild(shell);
        helmetGrp.AddChild(CinematicFx.Line(new[] { shellPts[8], shellPts[9], shellPts[10], shellPts[11] }, Edge, 1.3f));
        // 顶部脊线
        helmetGrp.AddChild(CinematicFx.Line(new[] { new Vector2(-8.5f, -5.5f), new Vector2(0.0f, -10.6f), new Vector2(8.5f, -5.5f) }, EdgeSoft, 1.2f));
        // 面罩玻璃（前半，深色 + 高光斜条）：覆盖 -x 到 +x 前缘
        var visorGlass = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-3.0f, -8.0f), new Vector2(8.0f, -6.0f), new Vector2(10.4f, 0.0f),
                new Vector2(7.5f, 6.5f), new Vector2(-2.0f, 7.5f), new Vector2(-3.5f, 0.0f),
            },
            Color = Glass,
        };
        helmetGrp.AddChild(visorGlass);
        var visorHi = new Polygon2D
        {
            Polygon = new[] { new Vector2(-1.0f, -6.5f), new Vector2(2.0f, -6.0f), new Vector2(-1.5f, 6.5f), new Vector2(-3.0f, 3.0f) },
            Color = new Color(0.55f, 0.78f, 1.0f, 0.16f),
            Material = CinematicFx.AdditiveMaterial(),
        };
        helmetGrp.AddChild(visorHi);
        // 面罩框缘
        helmetGrp.AddChild(CinematicFx.Line(new[] { new Vector2(-3.0f, -8.0f), new Vector2(8.0f, -6.0f), new Vector2(10.4f, 0.0f), new Vector2(7.5f, 6.5f) }, Edge, 1.2f));
        // 颈环（底座）
        var helmRing = CinematicFx.RectPoly(11.0f, 2.4f, SuitMid);
        helmRing.Position = new Vector2(1.0f, 9.8f);
        helmetGrp.AddChild(helmRing);
        // 侧通讯舱（左右各一）+ 后舱
        var podL = CinematicFx.RectPoly(3.2f, 4.2f, Plate);
        podL.Position = new Vector2(-6.5f, -1.0f);
        helmetGrp.AddChild(podL);
        var podR = CinematicFx.RectPoly(3.0f, 4.0f, Plate);
        podR.Position = new Vector2(6.8f, -4.6f);
        helmetGrp.AddChild(podR);
        var podLight = new GlowDot { Radius = 1.2f, DotColor = new Color(lamp, 0.95f), Position = new Vector2(-7.4f, 1.0f) };
        helmetGrp.AddChild(podLight);
        // 天线：后上斜出 + 顶端信号灯
        helmetGrp.AddChild(CinematicFx.Line(new[] { new Vector2(-7.5f, -6.0f), new Vector2(-13.0f, -15.0f) }, Metal, 1.6f));
        helmetGrp.AddChild(new GlowDot { Radius = 1.4f, DotColor = new Color(lamp, 0.95f), Position = new Vector2(-13.2f, -15.2f) });
        // 下颌护板
        var chin = new Polygon2D
        {
            Polygon = new[] { new Vector2(-1.0f, 6.0f), new Vector2(6.5f, 6.8f), new Vector2(4.5f, 10.0f), new Vector2(-0.5f, 9.2f) },
            Color = SuitMid,
        };
        helmetGrp.AddChild(chin);
        // 面罩反光点（青色，保留原 visor 语义）
        var visor = new GlowDot { Radius = 3.5f, DotColor = GlowCyan, Position = new Vector2(8.0f, -2.0f) };
        helmetGrp.AddChild(visor);

        // 眼睑（面部特写闭眼）：位置/尺寸与旧版一致（返回契约依赖）
        var eyelid = CinematicFx.RectPoly(9.0f, 7.0f, new Color(0.1f, 0.13f, 0.2f));
        eyelid.Position = new Vector2(17.5f, -67.5f);
        eyelid.Scale = new Vector2(1.0f, 0.0f);
        torsoGrp.AddChild(eyelid);

        // ---------------- 维生背包（双筒 + 喷嘴 + 供气管 + 压力表 + 格栅） ----------------
        var pack = new Node2D { Position = new Vector2(-11.0f, -30.0f) };
        torsoGrp.AddChild(pack);
        var packBody = CinematicFx.RectPoly(12.0f, 24.0f, SuitFar);
        pack.AddChild(packBody);
        pack.AddChild(CinematicFx.Line(new[] { new Vector2(-6.0f, -12.0f), new Vector2(-6.0f, 12.0f) }, Seam, 1.1f));
        // 双罐
        foreach (var tx in new[] { -2.5f, 2.5f })
        {
            var tank = CinematicFx.RectPoly(3.4f, 15.0f, Metal);
            tank.Position = new Vector2(tx, -3.0f);
            pack.AddChild(tank);
            var cap = CinematicFx.RectPoly(3.8f, 2.0f, SuitMid);
            cap.Position = new Vector2(tx, -11.0f);
            pack.AddChild(cap);
        }

        // 供气管：背包顶 → 头盔侧
        pack.AddChild(CinematicFx.Line(
            new[] { new Vector2(4.0f, -12.0f), new Vector2(8.0f, -22.0f), new Vector2(14.0f, -28.0f) }, EdgeSoft, 1.6f));
        // 压力表：小圆 + 刻度 + 指针
        var gauge = new GlowDot { Radius = 3.2f, DotColor = new Color(0.05f, 0.07f, 0.11f), Position = new Vector2(-1.0f, 7.0f) };
        pack.AddChild(gauge);
        pack.AddChild(CinematicFx.Line(new[] { new Vector2(-3.4f, 7.0f), new Vector2(-0.4f, 6.0f) }, new Color(lamp, 0.9f), 1.2f));
        var gaugeRingPts = new Vector2[14];
        for (var i = 0; i < 14; i++)
        {
            var a = Mathf.Tau * i / 14.0f;
            gaugeRingPts[i] = new Vector2(-1.0f + Mathf.Cos(a) * 3.3f, 7.0f + Mathf.Sin(a) * 3.3f);
        }

        pack.AddChild(CinematicFx.Line(gaugeRingPts, new Color(0.45f, 0.52f, 0.62f), 1.0f));
        // 底部散热格栅 + 喷嘴
        for (var v = 0; v < 3; v++)
        {
            pack.AddChild(CinematicFx.Line(new[] { new Vector2(-4.0f, 15.0f + 2.4f * v), new Vector2(4.0f, 15.0f + 2.4f * v) }, Seam, 1.0f));
        }

        var nozzle = CinematicFx.RectPoly(4.0f, 3.0f, Metal);
        nozzle.Position = new Vector2(0.0f, 14.0f);
        pack.AddChild(nozzle);
        var packLight = new GlowDot { Radius = 2.0f, DotColor = new Color(UITheme.Holo, 0.8f), Position = new Vector2(-2.0f, 6.0f) };
        pack.AddChild(packLight);

        // ---------------- 手臂（远侧先画） ----------------
        foreach (var sideI in new[] { 1, 0 })
        {
            var c = sideI == 0 ? SuitNear : SuitFar;
            var shoulder = new Node2D { Position = new Vector2(8.0f - 6.0f * sideI, -42.0f + 2.0f * sideI) };
            torsoGrp.AddChild(shoulder);
            // 肩关节球
            var joint = new GlowDot { Radius = 3.0f, DotColor = c, Position = Vector2.Zero };
            shoulder.AddChild(joint);
            var upper = CinematicFx.RectPoly(5.0f, 16.0f, c);
            upper.Position = new Vector2(0.0f, 8.0f);
            shoulder.AddChild(upper);
            // 上臂甲片 + 走线
            var upperPlate = CinematicFx.RectPoly(5.6f, 8.0f, sideI == 0 ? SuitMid : SuitFar);
            upperPlate.Position = new Vector2(0.4f, 7.0f);
            upperPlate.Color = new Color(upperPlate.Color, 0.75f);
            shoulder.AddChild(upperPlate);

            var elbow = new Node2D { Position = new Vector2(0.0f, 16.0f) };
            shoulder.AddChild(elbow);
            // 肘关节环
            var elbowRing = new GlowDot { Radius = 2.6f, DotColor = Plate, Position = Vector2.Zero };
            elbow.AddChild(elbowRing);
            var forearm = CinematicFx.RectPoly(4.5f, 15.0f, c);
            forearm.Position = new Vector2(0.0f, 7.5f);
            elbow.AddChild(forearm);
            forearm.AddChild(CinematicFx.Line(new[] { new Vector2(1.8f, -6.0f), new Vector2(1.8f, 6.0f) }, Edge, 1.2f));
            // 护腕 + 腕箍
            var bracer = CinematicFx.RectPoly(5.4f, 6.0f, Plate);
            bracer.Position = new Vector2(0.2f, 9.0f);
            forearm.AddChild(bracer);
            forearm.AddChild(CinematicFx.Line(new[] { new Vector2(-2.4f, 12.0f), new Vector2(2.4f, 12.0f) }, Seam, 1.1f));
            // 手套：掌 + 分指 + 拇指（保留原手部位置语义）
            var palm = CinematicFx.RectPoly(4.6f, 5.6f, c);
            palm.Position = new Vector2(0.4f, 15.4f);
            elbow.AddChild(palm);
            for (var f = 0; f < 3; f++)
            {
                elbow.AddChild(CinematicFx.Line(
                    new[] { new Vector2(-1.0f + 1.8f * f, 17.5f), new Vector2(-1.4f + 1.8f * f, 20.2f) }, c, 1.6f));
            }

            shoulders.Add(Variant.From(shoulder));
            elbows.Add(Variant.From(elbow));
        }

        // 躯干暖/冷色边缘光（胸廓描边副本，叠加态微偏移）——把剪影从暗舱里托出
        var rimTorso = new Polygon2D
        {
            Polygon = chestPoints,
            Color = new Color(0.5f, 0.62f, 0.85f, 0.28f),
            Position = new Vector2(2.0f, -2.0f),
            Material = CinematicFx.AdditiveMaterial(),
        };
        torsoGrp.AddChild(rimTorso);

        return new Godot.Collections.Dictionary
        {
            ["node"] = Variant.From(person),
            ["hips"] = Variant.From(hips),
            ["knees"] = Variant.From(knees),
            ["shoulders"] = Variant.From(shoulders),
            ["elbows"] = Variant.From(elbows),
            ["torso"] = Variant.From(torsoGrp),
            ["eyelid"] = Variant.From(eyelid),
        };
    }
}
