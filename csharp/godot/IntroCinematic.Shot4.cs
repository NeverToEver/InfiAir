using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 开场过场 镜头 4 构建器（文件粒度拆分自 IntroCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class IntroCinematic : CanvasLayer
{

    // ---------------- 镜头 4：操作台紧急启动（2.5s） ----------------

    /// <summary>驾驶舱前景框架（含舱壁缝线）+ 两侧仪表副屏（玻璃高光；左副屏带雷达距圈/扫掠针/回波亮点）+ 三分区航电控制台
    /// （斜切梯形台体：推进区按钮簇+节流阀滑槽 / 导航区旋钮+按钮排 / 武器区拨杆开关，LED 指示排+分区铭牌）+
    /// 4 指+拇指手形剪影带按下起伏地点按 + 主屏红色倒计时 3→2→1（bezel 边框）与警告行闪烁 + 两侧金属把手；
    /// 倒计时结束五指扣合把手，结尾 0.5s 整体后仰 -3° + 短促震动 + 屏幕白光渐强。</summary>
    private IntroConsoleShot BuildShot4()
    {
        var dur = _shotDurations[3];
        var root = new IntroConsoleShot { Name = "Shot4" };
        root.AddChild(BgRect(new Color(0.02f, 0.03f, 0.05f)));
        // 舱体前景框架（左右楔 + 顶梁）
        var frameColor = new Color(0.05f, 0.06f, 0.09f);
        var leftWedge = new Polygon2D
        {
            Polygon = new[] { new Vector2(0.0f, 0.0f), new Vector2(280.0f, 0.0f), new Vector2(180.0f, 1080.0f), new Vector2(0.0f, 1080.0f) },
            Color = frameColor,
        };
        root.AddChild(leftWedge);
        var rightWedge = new Polygon2D
        {
            Polygon = new[] { new Vector2(1920.0f, 0.0f), new Vector2(1640.0f, 0.0f), new Vector2(1740.0f, 1080.0f), new Vector2(1920.0f, 1080.0f) },
            Color = frameColor,
        };
        root.AddChild(rightWedge);
        var topBeam = new Polygon2D
        {
            Polygon = new[] { new Vector2(0.0f, 0.0f), new Vector2(1920.0f, 0.0f), new Vector2(1920.0f, 120.0f), new Vector2(0.0f, 170.0f) },
            Color = frameColor,
        };
        root.AddChild(topBeam);
        // 舱壁缝线（框架上的细结构线，增加舱内细节密度）
        var seamColor = new Color(0.14f, 0.17f, 0.24f);
        for (var i = 0; i < 4; i++)
        {
            root.AddChild(Line(
                new[] { new Vector2(40.0f + 50.0f * i, 200.0f + 180.0f * i), new Vector2(210.0f - 20.0f * i, 240.0f + 180.0f * i) },
                seamColor,
                1.5f));
            root.AddChild(Line(
                new[] { new Vector2(1880.0f - 50.0f * i, 200.0f + 180.0f * i), new Vector2(1710.0f + 20.0f * i, 240.0f + 180.0f * i) },
                seamColor,
                1.5f));
        }

        root.AddChild(Line(new[] { new Vector2(0.0f, 150.0f), new Vector2(1920.0f, 105.0f) }, seamColor, 1.5f));
        // 顶梁青色灯带：宽淡底光 + 窄亮芯
        var stripGlow = Line(new[] { new Vector2(280.0f, 138.0f), new Vector2(1640.0f, 112.0f) }, new Color(0.0f, 0.83f, 1.0f, 0.12f), 8.0f);
        stripGlow.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        root.AddChild(stripGlow);
        root.AddChild(Line(new[] { new Vector2(280.0f, 138.0f), new Vector2(1640.0f, 112.0f) }, new Color(0.0f, 0.83f, 1.0f, 0.35f), 2.5f));
        // 两侧仪表副屏：青色小屏 + 迷你波形/柱状线
        var sides = new[] { new[] { 330.0f, 250.0f }, new[] { 1390.0f, 250.0f } };
        foreach (var side in sides)
        {
            var subScreen = RectPoly(200.0f, 130.0f, new Color(0.02f, 0.1f, 0.14f));
            subScreen.Position = new Vector2(side[0] + 100.0f, side[1] + 65.0f);
            root.AddChild(subScreen);
            var subBorder = Line(
                new[]
                {
                    new Vector2(side[0], side[1]),
                    new Vector2(side[0] + 200.0f, side[1]),
                    new Vector2(side[0] + 200.0f, side[1] + 130.0f),
                    new Vector2(side[0], side[1] + 130.0f),
                },
                UITheme.AccentDim,
                2.0f);
            subBorder.Closed = true;
            root.AddChild(subBorder);
            // 副屏玻璃高光斜纹
            var subGlass = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(side[0] + 40.0f, side[1]),
                    new Vector2(side[0] + 80.0f, side[1]),
                    new Vector2(side[0] + 36.0f, side[1] + 130.0f),
                    new Vector2(side[0] + 6.0f, side[1] + 130.0f),
                },
                Color = new Color(1.0f, 1.0f, 1.0f, 0.05f),
                Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
            };
            root.AddChild(subGlass);
            var wavePoints = new Vector2[9];
            for (var w = 0; w < 9; w++)
            {
                wavePoints[w] = new Vector2(side[0] + 18.0f + 20.0f * w, side[1] + 65.0f + Mathf.Sin(w * 1.4f) * 32.0f);
            }

            root.AddChild(Line(wavePoints, new Color(0.0f, 0.83f, 1.0f, 0.7f), 2.0f));
            for (var bar = 0; bar < 4; bar++)
            {
                var b = RectPoly(14.0f, 20.0f + 14.0f * bar, new Color(0.0f, 0.83f, 1.0f, 0.4f));
                b.Position = new Vector2(side[0] + 30.0f + 30.0f * bar, side[1] + 118.0f - 7.0f * bar);
                root.AddChild(b);
            }
        }

        // 左副屏雷达：静态距圈 + 旋转扫掠针（叠加态）+ 2 枚回波亮点（扫过点亮，_ConsoleShot._process 驱动）
        var radarC = new Vector2(430.0f, 315.0f);
        var radarRingPts = new Vector2[24];
        for (var rr = 0; rr < 24; rr++)
        {
            var ra = Mathf.Tau * rr / 24.0f;
            radarRingPts[rr] = radarC + new Vector2(Mathf.Cos(ra), Mathf.Sin(ra)) * 52.0f;
        }

        var radarRing = Line(radarRingPts, new Color(0.0f, 0.83f, 1.0f, 0.3f), 1.5f);
        radarRing.Closed = true;
        root.AddChild(radarRing);
        var sweep = Line(new[] { Vector2.Zero, new Vector2(52.0f, 0.0f) }, new Color(0.4f, 0.95f, 1.0f, 0.8f), 2.5f);
        sweep.Position = radarC;
        sweep.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        root.AddChild(sweep);
        root.RadarSweep = sweep;
        var blipAngles = new List<float>();
        var blipEs = new List<float>();
        foreach (var bA in new[] { 0.9f, 3.8f })
        {
            var blip = CinematicFx.SoftGlow(4.0f, new Color(0.5f, 1.0f, 1.0f, 0.9f));
            blip.Position = radarC + new Vector2(Mathf.Cos(bA), Mathf.Sin(bA)) * 34.0f;
            root.AddChild(blip);
            root.RadarBlips.Add(blip);
            blipAngles.Add(bA);
            blipEs.Add(0.0f);
        }

        root.RadarBlipAngles = blipAngles.ToArray();
        root.RadarBlipE = blipEs.ToArray();
        // 控制台：斜切梯形台体 + 三功能分区（推进/导航/武器），控制件成组分布（构图避开底部 letterbox）
        var consoleBody = new Polygon2D
        {
            Polygon = new[] { new Vector2(320.0f, 700.0f), new Vector2(1600.0f, 700.0f), new Vector2(1700.0f, 1080.0f), new Vector2(220.0f, 1080.0f) },
            Color = new Color(0.08f, 0.1f, 0.14f),
        };
        root.AddChild(consoleBody);
        // 台面沿口高光 + 侧棱线 + 台面缝线
        root.AddChild(Line(new[] { new Vector2(320.0f, 700.0f), new Vector2(1600.0f, 700.0f) }, UITheme.PanelBorder, 2.0f));
        root.AddChild(Line(new[] { new Vector2(320.0f, 700.0f), new Vector2(220.0f, 1080.0f) }, new Color(0.14f, 0.17f, 0.24f), 1.5f));
        root.AddChild(Line(new[] { new Vector2(1600.0f, 700.0f), new Vector2(1700.0f, 1080.0f) }, new Color(0.14f, 0.17f, 0.24f), 1.5f));
        root.AddChild(Line(new[] { new Vector2(340.0f, 1000.0f), new Vector2(1580.0f, 1000.0f) }, new Color(0.14f, 0.17f, 0.24f), 1.5f));
        // 分区隔线（随台体透视微斜）
        foreach (var zx in new[] { 770.0f, 1150.0f })
        {
            root.AddChild(Line(new[] { new Vector2(zx, 720.0f), new Vector2(zx - 20.0f, 1000.0f) }, new Color(0.18f, 0.22f, 0.3f), 1.5f));
        }

        // 分区铭牌条：暗底 + 顶部 accent 细线 + 小字
        var zones = new (float X, string Key)[]
        {
            (450.0f, "INTRO_ZONE_PROP"),
            (960.0f, "INTRO_ZONE_NAV"),
            (1380.0f, "INTRO_ZONE_WPN"),
        };
        foreach (var zd in zones)
        {
            var plate = RectPoly(110.0f, 22.0f, new Color(0.03f, 0.12f, 0.16f));
            plate.Position = new Vector2(zd.X, 737.0f);
            root.AddChild(plate);
            root.AddChild(Line(new[] { new Vector2(zd.X - 55.0f, 726.0f), new Vector2(zd.X + 55.0f, 726.0f) }, new Color(0.0f, 0.83f, 1.0f, 0.4f), 1.6f));
            var plateLabel = UITheme.MakeLabel((string)Tr(zd.Key), UITheme.FontCaption, UITheme.Accent);
            plateLabel.AddThemeFontSizeOverride("font_size", 15);
            plateLabel.Position = new Vector2(zd.X - 55.0f, 726.0f);
            plateLabel.Size = new Vector2(110.0f, 22.0f);
            plateLabel.HorizontalAlignment = HorizontalAlignment.Center;
            root.AddChild(plateLabel);
        }

        // LED 指示排（每区一排，青/红交替）
        foreach (var lz in new[] { 400.0f, 900.0f, 1250.0f })
        {
            for (var lI = 0; lI < 5; lI++)
            {
                var led = new IntroGlowDot
                {
                    Radius = 3.0f,
                    DotColor = lI % 2 == 0 ? new Color(0.0f, 0.83f, 1.0f, 0.85f) : new Color(0.9f, 0.2f, 0.25f, 0.85f),
                    Position = new Vector2(lz + 14.0f * lI, 766.0f),
                };
                root.AddChild(led);
            }
        }

        var cells = new List<Polygon2D>();
        // 按钮簇：背板 + rows×cols 小按钮（闪烁重着色，也是双手点按目标池）+ 板角状态 LED
        Action<float, float, int, int> cluster = (cx, cy, cols, rows) =>
        {
            var plate = RectPoly(28.0f * cols + 16.0f, 26.0f * rows + 14.0f, new Color(0.04f, 0.05f, 0.08f));
            plate.Position = new Vector2(cx, cy);
            root.AddChild(plate);
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var btn = RectPoly(22.0f, 18.0f, new Color(0.0f, 0.3f, 0.4f, 0.5f));
                    btn.Position = new Vector2(cx - 14.0f * (cols - 1) + 28.0f * col, cy - 13.0f * (rows - 1) + 26.0f * row);
                    root.AddChild(btn);
                    cells.Add(btn);
                }
            }

            for (var ledI = 0; ledI < 2; ledI++)  // 簇板顶边两角状态灯（静态，青/红各一）
            {
                var clLed = new IntroGlowDot
                {
                    Radius = 2.5f,
                    DotColor = ledI == 0 ? new Color(0.0f, 0.83f, 1.0f, 0.9f) : new Color(0.9f, 0.2f, 0.25f, 0.9f),
                    Position = new Vector2(cx + 14.0f * cols * (-1.0f + 2.0f * ledI), cy - 13.0f * rows),
                };
                root.AddChild(clLed);
            }
        };
        cluster(450.0f, 855.0f, 4, 2);  // 推进区按钮簇
        cluster(960.0f, 950.0f, 4, 1);  // 导航区按钮排
        cluster(1445.0f, 855.0f, 2, 2);  // 武器区按钮簇
        root.Cells = cells;
        // 推进区：双节流阀滑槽（轨道 + 刻度 + 手柄 + 红色标识线）
        for (var sI = 0; sI < 2; sI++)
        {
            var sx = 640.0f + 60.0f * sI;
            root.AddChild(Line(new[] { new Vector2(sx, 790.0f), new Vector2(sx, 930.0f) }, new Color(0.2f, 0.25f, 0.34f), 4.0f));
            for (var tick = 0; tick < 4; tick++)
            {
                root.AddChild(Line(
                    new[] { new Vector2(sx + 8.0f, 802.0f + 32.0f * tick), new Vector2(sx + 16.0f, 802.0f + 32.0f * tick) },
                    new Color(0.3f, 0.36f, 0.46f, 0.6f),
                    1.2f));
            }

            var handle = RectPoly(22.0f, 12.0f, new Color(0.5f, 0.55f, 0.62f));
            handle.Position = new Vector2(sx, 830.0f + 44.0f * sI);
            root.AddChild(handle);
            root.AddChild(Line(
                new[] { new Vector2(sx - 8.0f, 830.0f + 44.0f * sI), new Vector2(sx + 8.0f, 830.0f + 44.0f * sI) },
                UITheme.Danger,
                2.0f));
        }

        // 导航区：双旋钮（底座圆 + 刻度环 + 指针）
        for (var kI = 0; kI < 2; kI++)
        {
            var kx = 880.0f + 160.0f * kI;
            var knob = new IntroGlowDot { Radius = 22.0f, DotColor = new Color(0.05f, 0.07f, 0.1f), Position = new Vector2(kx, 855.0f) };
            root.AddChild(knob);
            var knobRingPoints = new Vector2[20];
            for (var pI = 0; pI < 20; pI++)
            {
                var pA = Mathf.Tau * pI / 20.0f;
                knobRingPoints[pI] = new Vector2(kx + Mathf.Cos(pA) * 27.0f, 855.0f + Mathf.Sin(pA) * 27.0f);
            }

            var knobRing = Line(knobRingPoints, new Color(0.3f, 0.38f, 0.5f), 2.0f);
            knobRing.Closed = true;
            root.AddChild(knobRing);
            var pointerA = -0.6f + 1.9f * kI;
            root.AddChild(Line(
                new[] { new Vector2(kx, 855.0f), new Vector2(kx + Mathf.Cos(pointerA) * 19.0f, 855.0f + Mathf.Sin(pointerA) * 19.0f) },
                UITheme.Accent,
                3.0f));
        }

        // 武器区：三拨杆开关（槽位 + 拨杆 + 状态灯头）
        for (var tI = 0; tI < 3; tI++)
        {
            var tx = 1230.0f + 70.0f * tI;
            var slot = RectPoly(10.0f, 34.0f, new Color(0.03f, 0.04f, 0.06f));
            slot.Position = new Vector2(tx, 855.0f);
            root.AddChild(slot);
            var leverUp = tI != 1;
            var tipY = leverUp ? 843.0f : 867.0f;
            root.AddChild(Line(new[] { new Vector2(tx, 855.0f), new Vector2(tx + 6.0f, tipY) }, new Color(0.6f, 0.65f, 0.72f), 4.0f));
            var tip = Glow(3.0f, leverUp ? new Color(0.9f, 0.2f, 0.25f, 0.9f) : new Color(0.0f, 0.83f, 1.0f, 0.9f));
            tip.Position = new Vector2(tx + 6.0f, tipY);
            root.AddChild(tip);
        }

        var blink = new Godot.Timer { WaitTime = 0.09f, Autostart = true };  // 构建期未入树，用 autostart
        root.AddChild(blink);
        blink.Timeout += () =>
        {
            var palette = new[]
            {
                new Color(0.0f, 0.83f, 1.0f, 0.9f),
                new Color(1.0f, 0.6f, 0.15f, 0.9f),
                new Color(0.0f, 0.3f, 0.4f, 0.4f),
                new Color(0.9f, 0.2f, 0.25f, 0.9f),
            };
            for (var k = 0; k < 6; k++)
            {
                cells[(int)(GD.Randi() % (uint)cells.Count)].Color = palette[(int)(GD.Randi() % (uint)palette.Length)];
            }
        };
        // 主屏：bezel 边框底板 + 红底倒计时 + 进度环/扫描弧 + 警告行闪烁 + 滚动状态日志
        var bezel = RectPoly(560.0f, 360.0f, new Color(0.04f, 0.05f, 0.08f));
        bezel.Position = new Vector2(960.0f, 380.0f);
        root.AddChild(bezel);
        var screen = RectPoly(520.0f, 320.0f, new Color(0.15f, 0.03f, 0.05f));
        screen.Position = new Vector2(960.0f, 380.0f);
        root.AddChild(screen);
        var screenBorder = Line(
            new[]
            {
                new Vector2(700.0f, 220.0f),
                new Vector2(1220.0f, 220.0f),
                new Vector2(1220.0f, 540.0f),
                new Vector2(700.0f, 540.0f),
            },
            UITheme.Danger,
            3.0f);
        screenBorder.Closed = true;
        root.AddChild(screenBorder);
        // 主屏玻璃高光斜纹
        var glass = new Polygon2D
        {
            Polygon = new[] { new Vector2(780.0f, 220.0f), new Vector2(890.0f, 220.0f), new Vector2(800.0f, 540.0f), new Vector2(710.0f, 540.0f) },
            Color = new Color(1.0f, 1.0f, 1.0f, 0.05f),
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        root.AddChild(glass);
        // 倒计时外圈：静态进度环 + 每秒一圈的红色扫描弧
        var ringPoints = new Vector2[48];
        for (var rI = 0; rI < 48; rI++)
        {
            var rA = Mathf.Tau * rI / 48.0f;
            ringPoints[rI] = new Vector2(960.0f + Mathf.Cos(rA) * 95.0f, 312.0f + Mathf.Sin(rA) * 95.0f);
        }

        var cdRing = Line(ringPoints, new Color(1.0f, 0.25f, 0.25f, 0.35f), 3.0f);
        cdRing.Closed = true;
        root.AddChild(cdRing);
        var arcPoints = new Vector2[12];
        for (var aI = 0; aI < 12; aI++)
        {
            var aA = -Mathf.Pi * 0.5f + aI / 12.0f;
            arcPoints[aI] = new Vector2(Mathf.Cos(aA) * 95.0f, Mathf.Sin(aA) * 95.0f);
        }

        var cdArc = Line(arcPoints, new Color(1.0f, 0.45f, 0.4f, 0.9f), 5.0f);
        cdArc.Position = new Vector2(960.0f, 312.0f);
        root.AddChild(cdArc);
        var arcSweep = root.CreateTween().SetLoops();
        arcSweep.TweenProperty(cdArc, "rotation", Mathf.Tau, 0.6).SetTrans(Tween.TransitionType.Linear);
        var countdown = UITheme.MakeLabel("3", UITheme.FontDisplay, UITheme.Danger);
        countdown.Position = new Vector2(860.0f, 252.0f);
        countdown.Size = new Vector2(200.0f, 120.0f);
        countdown.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(countdown);
        var warning = UITheme.MakeLabel((string)Tr("INTRO_WARNING"), UITheme.FontHeader, UITheme.Danger);
        warning.Position = new Vector2(760.0f, 395.0f);
        warning.Size = new Vector2(400.0f, 44.0f);
        warning.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(warning);
        // 滚动状态日志：INTRO_LOG_1..4 四条 i18n 键，Timer 回调只换 text（零逐帧分配）
        var logLines = new List<Label>();
        for (var li = 0; li < 3; li++)
        {
            var logLine = UITheme.MakeLabel((string)Tr(GdFormat.Format("INTRO_LOG_%d", li + 1)), UITheme.FontCaption, new Color(1.0f, 0.55f, 0.5f));
            logLine.Position = new Vector2(720.0f, 452.0f + 26.0f * li);
            logLine.Size = new Vector2(480.0f, 24.0f);
            root.AddChild(logLine);
            logLines.Add(logLine);
        }

        var remain = new int[] { 3 };
        var countTimer = new Godot.Timer { WaitTime = 0.6f, Autostart = true };
        root.AddChild(countTimer);
        countTimer.Timeout += () =>
        {
            remain[0] -= 1;
            if (remain[0] <= 0)
            {
                countTimer.Stop();
                countdown.Text = "!";
            }
            else
            {
                countdown.Text = remain[0].ToString();
            }
        };
        var warnBlink = new Godot.Timer { WaitTime = 0.4f, Autostart = true };
        root.AddChild(warnBlink);
        warnBlink.Timeout += () => warning.Visible = !warning.Visible;
        // 日志轮换：0.7s 步进，INTRO_LOG_1..4 四条键在三行间滚动
        var logStep = new int[] { 0 };
        var logTimer = new Godot.Timer { WaitTime = 0.7f, Autostart = true };
        root.AddChild(logTimer);
        logTimer.Timeout += () =>
        {
            logStep[0] = (logStep[0] + 1) % 4;
            for (var li = 0; li < 3; li++)
            {
                logLines[li].Text = (string)Tr(GdFormat.Format("INTRO_LOG_%d", (logStep[0] + li) % 4 + 1));
            }
        };
        // 两侧金属把手
        var handleL = RectPoly(36.0f, 160.0f, new Color(0.5f, 0.55f, 0.62f));
        handleL.Position = new Vector2(380.0f, 560.0f);
        root.AddChild(handleL);
        var handleR = RectPoly(36.0f, 160.0f, new Color(0.5f, 0.55f, 0.62f));
        handleR.Position = new Vector2(1540.0f, 560.0f);
        root.AddChild(handleR);
        root.Handles = new List<Vector2> { handleL.Position, handleR.Position };
        // 双手：4 指+拇指手形剪影（前臂自下方伸入），点按带按下-抬起起伏；结尾五指扣合把手
        for (var h = 0; h < 2; h++)
        {
            var hand = new Node2D { Position = new Vector2(700.0f + 500.0f * h, 940.0f) };
            // 前臂（斜向下方伸出画面）
            var forearm = RectPoly(18.0f, 260.0f, new Color(0.16f, 0.2f, 0.28f));
            forearm.Position = new Vector2(-40.0f + 80.0f * h, 130.0f);
            forearm.Rotation = 0.35f - 0.7f * h;
            hand.AddChild(forearm);
            // 张开手形：腕→掌背→4 指节阶梯→拇指侧缘
            var openShape = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-9.0f, 12.0f),
                    new Vector2(-10.0f, -6.0f),
                    new Vector2(-8.0f, -18.0f),
                    new Vector2(-5.0f, -20.0f),
                    new Vector2(-4.0f, -10.0f),
                    new Vector2(-1.0f, -22.0f),
                    new Vector2(0.0f, -11.0f),
                    new Vector2(3.0f, -23.0f),
                    new Vector2(4.0f, -11.0f),
                    new Vector2(7.0f, -20.0f),
                    new Vector2(8.0f, -6.0f),
                    new Vector2(13.0f, -1.0f),
                    new Vector2(11.0f, 5.0f),
                    new Vector2(7.0f, 6.0f),
                    new Vector2(9.0f, 12.0f),
                },
                Color = new Color(0.2f, 0.25f, 0.34f),
            };
            hand.AddChild(openShape);
            // 扣合手形：握拳剪影（指节凹槽线朝把手外侧，初始隐藏）
            var gripShape = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(-14.0f, -26.0f),
                    new Vector2(14.0f, -26.0f),
                    new Vector2(22.0f, -18.0f),
                    new Vector2(22.0f, 18.0f),
                    new Vector2(14.0f, 26.0f),
                    new Vector2(-14.0f, 26.0f),
                    new Vector2(-22.0f, 18.0f),
                    new Vector2(-22.0f, -18.0f),
                },
                Color = openShape.Color,
                Visible = false,
            };
            var grooveSign = -1.0f + 2.0f * h;  // 左手凹槽在 -x 侧，右手镜像
            for (var gI = 0; gI < 3; gI++)
            {
                var groove = Line(
                    new[] { new Vector2(grooveSign * 22.0f, -10.0f + 10.0f * gI), new Vector2(grooveSign * 9.0f, -10.0f + 10.0f * gI) },
                    new Color(0.1f, 0.13f, 0.19f),
                    2.0f);
                gripShape.AddChild(groove);
            }

            hand.AddChild(gripShape);
            var palmRim = CinematicFx.SoftGlow(15.0f, new Color(0.0f, 0.83f, 1.0f, 0.15f));
            hand.AddChild(palmRim);
            root.AddChild(hand);
            root.Hands.Add(hand);
            root.Targets.Add(hand.Position);
            root.OpenShapes.Add(openShape);
            root.GripShapes.Add(gripShape);
        }

        // 结尾 0.5s：双手抓把手 + 整体后仰 + 短促震动 + 屏幕白光渐强
        var white = BgRect(new Color(1.0f, 1.0f, 1.0f, 0.0f));
        root.AddChild(white);
        var endTimer = new Godot.Timer { OneShot = true, WaitTime = Mathf.Max(dur - 0.5f, 0.1f), Autostart = true };
        root.AddChild(endTimer);
        endTimer.Timeout += () =>
        {
            root.Grabbing = true;
            for (var hI = 0; hI < 2; hI++)
            {
                root.OpenShapes[hI].Visible = false;
                root.GripShapes[hI].Visible = true;  // 换握拳剪影扣上把手（不动前臂朝向）
            }

            var tween = root.CreateTween().SetParallel(true);
            tween.TweenProperty(root, "rotation", Mathf.DegToRad(-3.0f), 0.5);
            tween.TweenProperty(white, "color:a", 0.9f, 0.5);
            // 顿悟瞬间的短促震动：±5px 快速抖动 6 次
            var shake = root.CreateTween();
            for (var sI = 0; sI < 6; sI++)
            {
                shake.TweenProperty(root, "position", new Vector2((float)GD.RandRange(-5.0, 5.0), (float)GD.RandRange(-5.0, 5.0)), 0.06);
            }

            shake.TweenProperty(root, "position", Vector2.Zero, 0.08);
        };
        return root;
    }
}
