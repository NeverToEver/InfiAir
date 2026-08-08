using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 视角缩放测试（M7c 迁移）：
/// 三档映射与切换信号、view_world_rect 可见区域计算、profile 持久化往返、
/// 设置页三选按钮 wiring、main 场景相机 zoom 应用与震动 offset 兼容、
/// 玩家边缘钳制 / 敌机与子弹出屏销毁 / 刷怪位置与预告线 / 敌机悬停锚点 /
/// Boss 巡航范围与战斗锚线随档收窄。
/// 结束时恢复 medium 档并落盘，避免污染其他测试进程。
/// </summary>
public partial class ViewZoomTest : Node
{
    private int _failures;
    private GameState _gs = null!;

    /// <summary>
    /// M7（2026-08-06 审计）：profile 快照还原——原测试经 _write_profile 部分覆写
    /// profile.json + load_profile 间接清零 pre-login 最高分/高分榜并落盘，无快照还原
    /// （L15 只修直写路径，未覆盖间接清零）；备份/还原防本地数据被永久销毁
    /// </summary>
    private Godot.Collections.Dictionary _profileBackup = new();

    private void Check(bool cond, string label)
    {
        if (cond)
        {
            GD.Print("[PASS] " + label);
        }
        else
        {
            _failures++;
            GD.PushError("[FAIL] " + label);
        }
    }

    private Godot.Collections.Dictionary ReadProfile()
    {
        var parsed = Json.ParseString(Godot.FileAccess.GetFileAsString(_gs.PROFILE_PATH));
        return parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : new Godot.Collections.Dictionary();
    }

    private void WriteProfile(Godot.Collections.Dictionary data)
    {
        var f = Godot.FileAccess.Open(_gs.PROFILE_PATH, Godot.FileAccess.ModeFlags.Write);
        f.StoreString(Json.Stringify(data));
        f.Close();
    }

    private void BackupProfile()
    {
        _profileBackup = new Godot.Collections.Dictionary();
        foreach (var f in new[] { _gs.PROFILE_PATH, _gs.PROFILE_PATH + ".corrupt" })
        {
            var exists = Godot.FileAccess.FileExists(f);
            _profileBackup[f] = new Godot.Collections.Dictionary
            {
                ["exists"] = exists,
                ["content"] = exists ? Godot.FileAccess.GetFileAsString(f) : "",
            };
        }
    }

    private void RestoreProfile()
    {
        foreach (var key in _profileBackup.Keys)
        {
            var path = key.AsString();
            var b = _profileBackup[key].AsGodotDictionary();
            if (b["exists"].AsBool())
            {
                var fh = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
                fh.StoreString(b["content"].AsString());
                fh.Close();
            }
            else if (Godot.FileAccess.FileExists(path))
            {
                DirAccess.RemoveAbsolute(path);
            }
        }
    }

    /// <summary>期望可见区域（相机固定 (960,540)，视口 1920×1080）</summary>
    private static Rect2 ExpectRect(double factor)
    {
        var size = new Vector2(1920.0f, 1080.0f) / (float)factor;
        return new Rect2(new Vector2(960.0f, 540.0f) - size * 0.5f, size);
    }

    private static bool RectClose(Rect2 a, Rect2 b, double tol = 0.5)
    {
        return a.Position.DistanceTo(b.Position) < tol && a.Size.DistanceTo(b.Size) < tol;
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var gs = GetNode<GameState>("/root/GameState");
        _gs = gs;
        try
        {
            // M7：profile 快照（须在任何覆写/落盘前捕获原始 pre-login 最高分与高分榜）
            BackupProfile();
            // 确定性起点：清存档，视角档位归位 medium（profile 级，reset_run 不清）
            gs.DeleteSave();
            gs.ViewZoom = "medium";
            gs.SetViewZoomFactor(1.35);

            // ---------- 1. 档位映射与切换 ----------
            gs.SetViewZoom("small");
            Check(gs.ViewZoom == "small" && gs.ViewZoomFactor() == 1.0, "small 档 zoom=1.0");
            gs.SetViewZoom("medium");
            Check(gs.ViewZoomFactor() == 1.35, "medium 档 zoom=1.35");
            gs.SetViewZoom("large");
            Check(gs.ViewZoomFactor() == 1.7, "large 档 zoom=1.7");
            var emitted = new Godot.Collections.Array<double>();
            GameState.ViewZoomChangedEventHandler onZoom = (f) => emitted.Add(f);
            gs.ViewZoomChanged += onZoom;
            gs.SetViewZoom("small");
            Check(emitted.Count == 1 && emitted[0] == 1.0, "切换档位发出 view_zoom_changed 信号");
            gs.SetViewZoom("small");
            Check(emitted.Count == 1, "同档重复设置不发信号");
            gs.SetViewZoom("huge");
            Check(gs.ViewZoom == "small", "非法档位被忽略");
            gs.ViewZoomChanged -= onZoom;

            // ---------- 2. 可见区域计算 ----------
            Check(RectClose(gs.ViewWorldRect(), ExpectRect(1.0)), "small 可见区域 = 全屏 1920×1080");
            gs.SetViewZoom("medium");
            Check(RectClose(gs.ViewWorldRect(), ExpectRect(1.35)), "medium 可见区域 ≈ 1422×800");
            gs.SetViewZoom("large");
            Check(RectClose(gs.ViewWorldRect(), ExpectRect(1.7)), "large 可见区域 ≈ 1131×635");
            gs.SetViewZoom("small");
            Check(RectClose(gs.ViewWorldRect(80.0), new Rect2(-80.0f, -80.0f, 2080.0f, 1240.0f)), "margin 外扩与旧子弹边界一致");

            // ---------- 3. profile 持久化 ----------
            gs.SetViewZoom("large");
            Check(ReadProfile().GetValueOrDefault("view_zoom", "").AsString() == "large", "视角档位写入 profile");
            gs.ViewZoom = "small";  // 篡改内存（不经 setter，避免写盘）
            gs.SetViewZoomFactor(1.0);
            gs.LoadProfile();
            Check(gs.ViewZoom == "large" && gs.ViewZoomFactor() == 1.7, "视角档位从 profile 恢复");
            // 旧档案无 view_zoom 字段：保留当前值
            WriteProfile(new Godot.Collections.Dictionary { ["version"] = 1, ["high_score"] = 0 });
            gs.ViewZoom = "small";
            gs.SetViewZoomFactor(1.0);
            gs.LoadProfile();
            Check(gs.ViewZoom == "small", "旧档（无 view_zoom 字段）读取保留当前档位");
            // 非法档位值：忽略并保持当前值
            WriteProfile(new Godot.Collections.Dictionary { ["version"] = 1, ["high_score"] = 0, ["view_zoom"] = "huge" });
            gs.LoadProfile();
            Check(gs.ViewZoom == "small", "profile 非法档位值被忽略");
            gs.SetViewZoom("medium");

            // ---------- 4. 设置页三选按钮 ----------
            var settings = new SettingsUi();
            AddChild(settings);
            settings.ShowSettings();
            Check(settings.ZoomButtons().Count == 3, "设置页视角三选按钮");
            var mediumBtn = settings.ZoomButtons()["medium"].As<Button>();
            Check(mediumBtn.ButtonPressed, "视角按钮选中态 = 当前档");
            var largeBtn = settings.ZoomButtons()["large"].As<Button>();
            largeBtn.EmitSignal(Button.SignalName.Pressed);
            Check(gs.ViewZoom == "large", "视角按钮点击切换档位");
            settings.QueueFree();
            gs.SetViewZoom("medium");

            // ---------- 5. main 场景：相机应用 + 震动兼容 ----------
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            var mainNode = GetNode<Main>("Main");
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);
            player.SetInvincible(999.0f);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var camera = GetNode<Camera2D>("Main/Camera2D");
            Check(camera.Zoom.DistanceTo(new Vector2(1.35f, 1.35f)) < 0.001f, "相机默认应用 medium 档 zoom=1.35");
            Check(gs.CameraRef == camera, "相机注册到 GameState.camera_ref");
            // M5（2026-08-06 审计）：zoom>1 时星点区域必须锚定可见区——原锚 (0,0) 铺
            // [0,1920/zoom]×[0,1080/zoom]，可见区右/下边缘 L 形带无星（C07 只改尺寸未改锚点）
            var starfield = GetNode<Starfield>("Main/Starfield");
            var sfRect = new Rect2(starfield.Origin(), starfield.AreaSize());
            Check(sfRect.Encloses(gs.ViewWorldRect()), "M5：星空覆盖区域包含可见区（锚点随 view 平移）");
            Check(sfRect.Position != Vector2.Zero, "M5：星空锚点非 (0,0)（zoom>1 可见区缩小平移）");
            gs.SetViewZoom("small");
            Check(camera.Zoom == Vector2.One, "切 small 相机 zoom=1.0");
            gs.SetViewZoom("large");
            Check(camera.Zoom.DistanceTo(new Vector2(1.7f, 1.7f)) < 0.001f, "切 large 相机 zoom=1.7");
            // 震动只写 offset：zoom 不受影响，衰减结束后 offset 归零
            gs.Shake(20.0);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(camera.Offset != Vector2.Zero, "震动产生 offset");
            Check(camera.Zoom.DistanceTo(new Vector2(1.7f, 1.7f)) < 0.001f, "震动期间 zoom 保持 1.7");
            await Coroutine.WaitSeconds(this, 1.0);
            Check(camera.Offset == Vector2.Zero, "震动衰减后 offset 归零");
            Check(camera.Zoom.DistanceTo(new Vector2(1.7f, 1.7f)) < 0.001f, "震动结束后 zoom 仍为 1.7");

            // ---------- 6. 玩家边缘钳制随档收窄（large：x 435.3..1484.7 / y 262.4..817.6） ----------
            var viewLarge = gs.ViewWorldRect();
            var lo = viewLarge.Position + new Vector2(40.0f, 40.0f);
            var hi = viewLarge.End - new Vector2(40.0f, 40.0f);
            player.Velocity = Vector2.Zero;
            player.Position = Vector2.Zero;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(player.Position.DistanceTo(lo) < 2.0f, "large 档玩家钳制左上 = 可见区域 +40");
            player.Position = new Vector2(9999.0f, 9999.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(player.Position.DistanceTo(hi) < 2.0f, "large 档玩家钳制右下 = 可见区域 -40");

            // ---------- 7. 敌机出屏销毁随档收窄 ----------
            // y=1000：small 销毁线 1140 存活；large 销毁线 ≈917.6 应销毁
            gs.SetViewZoom("small");
            var enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");
            var e = enemyScene.Instantiate<Enemy>();
            e.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            e.CanShoot = false;
            e.Hp = 9999;
            e.Position = new Vector2(600.0f, 1000.0f);
            mainNode.AddChild(e);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(GodotObject.IsInstanceValid(e), "small 档 y=1000 敌机存活（销毁线 1140）");
            gs.SetViewZoom("large");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(!GodotObject.IsInstanceValid(e), "large 档 y=1000 敌机出屏销毁（销毁线 ≈917.6）");

            // ---------- 8. 子弹出屏销毁随档收窄 ----------
            // x=100：small 边界 -80 存活；large 边界 ≈315 应销毁
            gs.SetViewZoom("small");
            var bulletScene = GD.Load<PackedScene>("res://scenes/bullet.tscn");
            var b = bulletScene.Instantiate<Bullet>();
            b.Setup(Vector2.Right, 400.0f, 1, true);
            b.Position = new Vector2(100.0f, 500.0f);
            mainNode.AddChild(b);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(GodotObject.IsInstanceValid(b), "small 档 x=100 子弹存活（边界 -80）");
            gs.SetViewZoom("large");
            // C04 后子弹位移/销毁走物理帧，等待 physics_frame 而非 process_frame
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(!GodotObject.IsInstanceValid(b), "large 档 x=100 子弹出屏销毁（边界 ≈315）");

            // ---------- 9. 刷怪位置/预告线/悬停锚点随档收窄（当前为 large 档） ----------
            spawner.SpawnEnemy();  // 异步：先挂预告线，0.6s 后出机
            await Coroutine.WaitSeconds(this, 0.2);
            SpawnTelegraph? tel = null;
            for (var i = 0; i < mainNode.GetChildCount(); i++)
            {
                if (mainNode.GetChild(i) is SpawnTelegraph t)
                {
                    tel = t;
                }
            }
            Check(tel != null, "入场预告线已生成");
            if (tel != null)
            {
                Check(Mathf.Abs(tel.Position.Y - gs.ViewWorldRect().Position.Y) < 1.0f, "预告线贴在可见区域顶部");
                var view = gs.ViewWorldRect();
                Check(tel.Position.X > view.Position.X && tel.Position.X < view.End.X, "预告线 x 在可见区域内");
            }
            await Coroutine.WaitSeconds(this, 0.7);
            // flake 修复（2026-08-03 CI 门禁）：敌机入场到达锚点后围绕锚点水平机动，固定延迟
            // 后检查会测到机动后的 x（可越出 30px 边距）——改为轮询等敌机出现后立即检查
            // （出机位置 = 预告线 x，垂直下降阶段 x 不变）
            Enemy? spawned = null;
            for (var i = 0; i < 60; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                for (var j = 0; j < mainNode.GetChildCount(); j++)
                {
                    if (mainNode.GetChild(j) is Enemy en)
                    {
                        spawned = en;
                        break;
                    }
                }
                if (spawned != null)
                {
                    break;
                }
            }
            Check(spawned != null, "敌机已刷出");
            if (spawned != null)
            {
                var view = gs.ViewWorldRect();
                Check(spawned.Position.X > view.Position.X + 30.0f && spawned.Position.X < view.End.X - 30.0f, "刷怪 x 在可见区域内（60px 边距）");
                Check(Mathf.Abs(spawned.Position.Y - (view.Position.Y - 60.0f)) < 100.0f, "刷怪 y 在可见区域顶上方");
                Check(spawned.AnchorY >= view.Position.Y, "large 档刷怪锚点 ≥ 可见顶（spawner 分配加 view 基线）");
                spawned.QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 锚点 fallback：spawner 未分配时 _resolve_anchor 自取，钳入「view 顶 + 悬停带」
            var eFb = enemyScene.Instantiate<Enemy>();
            eFb.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            eFb.CanShoot = false;
            eFb.Hp = 9999;
            eFb.Position = new Vector2(600.0f, gs.ViewWorldRect().Position.Y + 10.0f);
            mainNode.AddChild(eFb);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(eFb.AnchorY >= gs.ViewWorldRect().Position.Y + eFb.HoverBand.X, "large 档敌机自取锚点 ≥ 可见顶 + 悬停带顶缘偏移");
            eFb.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ---------- 10. Boss 出场位置与巡航范围 ----------
            spawner.SpawnBoss(1);
            Boss? boss = null;
            for (var i = 0; i < mainNode.GetChildCount(); i++)
            {
                if (mainNode.GetChild(i) is Boss b2)
                {
                    boss = b2;
                }
            }
            Check(boss != null, "Boss 已生成");
            if (boss != null)
            {
                // 出场 y 在降入移动前断言（不等帧，避免 ENTER_SPEED 位移干扰）
                Check(Mathf.Abs(boss.Position.Y - (gs.ViewWorldRect().Position.Y - 160.0f)) < 1.0f, "Boss 出场 y 在可见区域顶上方");
                boss.QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var rangeBoss = GD.Load<PackedScene>("res://scenes/boss.tscn").Instantiate<Boss>();
            gs.SetViewZoom("small");
            var smallRange = rangeBoss.StrafeRange();
            Check(smallRange == new Vector2(300.0f, 1620.0f), "small 档 Boss 巡航范围 = 配置 300..1620");
            Check(Mathf.Abs(rangeBoss.FightAnchorY() - rangeBoss.FightY) < 0.001f, "small 档 Boss 战斗锚线 = FIGHT_Y（view.position.y=0 行为不变）");
            gs.SetViewZoom("large");
            var largeRange = rangeBoss.StrafeRange();
            var expectLo = gs.ViewWorldRect().Position.X + 300.0f;
            var expectHi = gs.ViewWorldRect().End.X - 300.0f;
            Check(Mathf.Abs(largeRange.X - expectLo) < 1.0f && Mathf.Abs(largeRange.Y - expectHi) < 1.0f, "large 档 Boss 巡航范围随可见区域收窄");
            var viewAnchor = gs.ViewWorldRect();
            var anchorLarge = rangeBoss.FightAnchorY();
            Check(Mathf.Abs(anchorLarge - (viewAnchor.Position.Y + rangeBoss.FightY)) < 0.001f, "large 档 Boss 战斗锚线 = 可见顶 + FIGHT_Y");
            Check(anchorLarge > viewAnchor.Position.Y && anchorLarge < viewAnchor.End.Y, "large 档 Boss 战斗锚线落在可见区域内");
            rangeBoss.Free();

            // ---------- 11. 母舰召唤位置（小窗演出直推，母舰穿梭入场于停驻点） ----------
            mainNode.SummonMothership();
            Check(mainNode.SummonWindow() != null, "召唤小窗已弹出");
            if (mainNode.SummonWindow() != null)
            {
                mainNode.SummonWindow()!.Skip();  // 幂等直推：finished → main 开穿梭门并实例化母舰
            }
            Check(mainNode.Mothership() != null, "母舰已召唤");
            if (mainNode.Mothership() != null)
            {
                var mothership = mainNode.Mothership()!;
                Check(Mathf.Abs(mothership.Position.X - gs.ViewWorldRect().GetCenter().X) < 1.0f, "母舰出场 x = 可见区域中心");
                var warpDrop = gs.Cfg("effects.mothership_summon.warp_in_drop", 260.0).AsDouble();
                Check(
                    Mathf.Abs(mothership.Position.Y - (gs.Cfg("mothership.hover_y", 270.0).AsDouble() - warpDrop * gs.WorldScale)) < 1.0,
                    "母舰出场 y = 停驻点上方 warp_in_drop × world_scale（穿梭滑入起点）"
                );
                mothership.QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"VIEW ZOOM TEST 异常: {e}");
        }
        finally
        {
            // 清理：恢复 medium 档并落盘，避免污染其他测试进程
            gs.SetViewZoom("medium");
            gs.ResetRun();
            gs.SaveProfile();
            gs.DeleteSave();
            // M7：还原原始 profile（最高分/高分榜/设置项），防本地数据被清零
            RestoreProfile();
            GD.Print($"VIEW ZOOM TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
