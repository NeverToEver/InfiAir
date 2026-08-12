using Godot;

namespace InfiAir;

/// <summary>
/// 返航过场 镜头 6 构建器（文件粒度拆分自 ReturnCinematic.cs，2026-08-12；逻辑零改动）。
/// </summary>
public partial class ReturnCinematic : CanvasLayer
{

    // ---------------- 镜头 6：通道步行 + 舱门（1.6s） ----------------

    /// <summary>侧视走廊：天花板/地面透视线 + 舱壁管线；顶部感应灯带 12 节点随主角行进亮起；
    /// 尽头休息室舱门双扇滑开 + 门缝泄光。主角步行 ~90px/s，镜头跟随（主角固定左 1/3）。</summary>
    private ReturnCinematicWalkShot BuildShot6()
    {
        var dur = _shotDurations[5];
        var root = new ReturnCinematicWalkShot { Name = "Shot6" };
        root._stop_scroll = 140.0f * (dur / 2.4f); // 步行距离按镜头时长等比压缩（步速 90px/s 不变）
        root._time_u = dur / 2.4f;
        root.AddChild(BgRect(new Color(0.02f, 0.02f, 0.05f)));
        var world = new Node2D();
        root.AddChild(world);
        root._world = world;
        // 天花板/地面透视线 + 舱壁管线（远景透出虚影站结构微光）
        world.AddChild(Line(new[] { new Vector2(0.0f, 340.0f), new Vector2(2600.0f, 340.0f) }, new Color(0.16f, 0.22f, 0.32f, 0.7f), 3.0f));
        world.AddChild(Line(new[] { new Vector2(0.0f, 820.0f), new Vector2(2600.0f, 820.0f) }, new Color(0.16f, 0.22f, 0.32f, 0.7f), 3.0f));
        // 舱壁肋板 ×12：宽窄/深浅交替，破除等距重复感
        for (var i = 0; i < 12; i++)
        {
            var wide = i % 2 == 0;
            var rib = RectPoly(wide ? 22.0f : 14.0f, 480.0f, wide ? new Color(0.07f, 0.09f, 0.13f) : new Color(0.05f, 0.065f, 0.10f));
            rib.Position = new Vector2(120.0f + 220.0f * i, 580.0f);
            world.AddChild(rib);
        }

        // 肋板间舱壁小壁板（带顶部刻线，通道纵深细节）
        for (var i = 0; i < 6; i++)
        {
            var px = 230.0f + 440.0f * i;
            var panel = RectPoly(84.0f, 56.0f, new Color(0.06f, 0.08f, 0.12f));
            panel.Position = new Vector2(px, 560.0f);
            world.AddChild(panel);
            world.AddChild(Line(new[] { new Vector2(px - 42.0f, 531.0f), new Vector2(px + 42.0f, 531.0f) }, new Color(0.2f, 0.3f, 0.42f, 0.5f), 1.5f));
        }

        foreach (var pipe in new[] { new[] { 360.0f, 6.0f }, new[] { 382.0f, 4.0f } })
        {
            world.AddChild(Line(new[] { new Vector2(0.0f, pipe[0]), new Vector2(2600.0f, pipe[0]) }, new Color(0.2f, 0.26f, 0.36f), pipe[1]));
        }

        // 顶灯光锥 ×3（叠加态低 alpha，挂在世界容器随滚动视差）
        foreach (var cx in new[] { 500.0f, 1200.0f, 1900.0f })
        {
            var lampCone = new Polygon2D
            {
                Polygon = new[]
                {
                    new Vector2(cx - 26.0f, 356.0f),
                    new Vector2(cx + 26.0f, 356.0f),
                    new Vector2(cx + 150.0f, 820.0f),
                    new Vector2(cx - 150.0f, 820.0f),
                },
                Color = new Color(0.6f, 0.9f, 1.0f, 0.05f),
            };
            var coneMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            lampCone.Material = coneMat;
            world.AddChild(lampCone);
        }

        // 地面反光提示条（灯带光在地板上的长条泛光）
        var floorHint = Line(new[] { new Vector2(0.0f, 812.0f), new Vector2(2600.0f, 812.0f) }, new Color(0.5f, 0.8f, 1.0f, 0.10f), 2.5f);
        var floorHintMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        floorHint.Material = floorHintMat;
        world.AddChild(floorHint);
        // 远处结构微光（虚影站内部）
        var farGlow = Glow(300.0f, new Color(0.0f, 0.5f, 1.0f, 0.06f));
        farGlow.Position = new Vector2(2300.0f, 540.0f);
        world.AddChild(farGlow);
        // 顶部感应灯带：12 节点分段（初始暗，随主角 x 阈值点亮）
        for (var i = 0; i < 12; i++)
        {
            var lx = 200.0f + 200.0f * i;
            var seg = Line(new[] { new Vector2(lx - 80.0f, 352.0f), new Vector2(lx + 80.0f, 352.0f) }, new Color(0.6f, 0.95f, 1.0f, 0.08f), 5.0f);
            var segMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            seg.Material = segMat;
            world.AddChild(seg);
            root._lights.Add(seg);
            root._light_x.Add(lx);
        }

        // 尽头休息室舱门：门框 + 左右双扇门片 + 门缝泄光（门高 280px，明显高于 185px 人物）
        var doorX = 920.0f;
        var frame = Line(
            new[]
            {
                new Vector2(doorX - 76.0f, 540.0f),
                new Vector2(doorX + 76.0f, 540.0f),
                new Vector2(doorX + 76.0f, 820.0f),
                new Vector2(doorX - 76.0f, 820.0f),
            },
            new Color(0.0f, 0.83f, 1.0f, 0.4f),
            2.5f
        );
        frame.Closed = true;
        world.AddChild(frame);
        var leak = new ColorRect
        {
            Color = new Color(0.6f, 0.95f, 1.0f, 0.0f),
            Position = new Vector2(doorX - 66.0f, 546.0f),
            Size = new Vector2(132.0f, 268.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        world.AddChild(leak);
        root._door_leak = leak;
        var doorL = RectPoly(76.0f, 272.0f, new Color(0.10f, 0.13f, 0.18f));
        doorL.Position = new Vector2(doorX - 39.0f, 676.0f);
        world.AddChild(doorL);
        root._door_l = doorL;
        var doorR = RectPoly(76.0f, 272.0f, new Color(0.10f, 0.13f, 0.18f));
        doorR.Position = new Vector2(doorX + 39.0f, 676.0f);
        world.AddChild(doorR);
        root._door_r = doorR;
        // 主角步行（固定画面左 1/3，世界反向平移）
        var person = BuildPerson();
        var pnode = (Node2D)person["node"].AsGodotObject();
        pnode.Scale = Vector2.One * 1.6f;
        pnode.Position = new Vector2(640.0f, 746.0f);
        PoseStand(person);
        root.AddChild(pnode);
        root._person = person;
        root._bob_base_y = pnode.Position.Y;
        // 脚步声 ×6（0.4s 间隔，极轻短促）
        var stepCount = 0;
        var stepTimer = new Godot.Timer { WaitTime = 0.4f, Autostart = true };
        root.AddChild(stepTimer);
        stepTimer.Timeout += () =>
        {
            stepCount += 1;
            if (stepCount > 6 || !root._walking)
            {
                stepTimer.Stop();
                return;
            }

            GameState.Instance.PlaySfx(GameState.Instance.SFX_BUFF_PICK, -20.0f);
        };
        return root;
    }
}
