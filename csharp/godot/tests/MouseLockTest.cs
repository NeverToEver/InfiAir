using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 鼠标锁定窗口内设置项测试（M7c 迁移）：
/// 默认开启、切换与信号、profile 持久化往返、旧档兼容、设置页开关 wiring、MouseTrap 边界 clamp 纯函数。
/// headless 下窗口事件不可模拟，仅断言数据层/纯函数/UI wiring；结束时恢复默认并落盘，避免污染其他测试进程。
/// </summary>
public partial class MouseLockTest : Node
{
    private int _failures;

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

    /// <summary>M7（2026-08-06 审计）：profile 快照还原——原测试经 _write_profile 部分覆写
    /// profile.json + load_profile 间接清零 pre-login 最高分/高分榜并落盘，无快照还原
    /// （L15 只修直写路径，未覆盖间接清零）；备份/还原防本地数据被永久销毁</summary>
    private readonly Godot.Collections.Dictionary _profileBackup = new();

    private static Godot.Collections.Dictionary ReadProfile(string path)
    {
        var parsed = Json.ParseString(Godot.FileAccess.GetFileAsString(path));
        return parsed.VariantType == Variant.Type.Dictionary
            ? parsed.AsGodotDictionary()
            : new Godot.Collections.Dictionary();
    }

    private static void WriteProfile(string path, Godot.Collections.Dictionary data)
    {
        var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        f.StoreString(Json.Stringify(data));
        f.Close();
    }

    private void BackupProfile(string profilePath)
    {
        _profileBackup.Clear();
        foreach (var path in new[] { profilePath, profilePath + ".corrupt" })
        {
            var exists = Godot.FileAccess.FileExists(path);
            _profileBackup[path] = new Godot.Collections.Dictionary
            {
                ["exists"] = exists,
                ["content"] = exists ? Godot.FileAccess.GetFileAsString(path) : "",
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

    public override void _Ready()
    {
        Run();
    }

    private void Run()
    {
        var gs = GetNode<GameState>("/root/GameState");
        try
        {
            // M7：profile 快照（须在任何覆写/落盘前捕获原始 pre-login 最高分与高分榜）
            BackupProfile(gs.PROFILE_PATH);
            // 确定性起点：清存档，mouse_lock 归位默认
            gs.DeleteSave();
            gs.MouseLock = true;

            // ---------- 1. 默认值 ----------
            Check(gs.MouseLock, "mouse_lock 默认开启");

            // ---------- 2. 切换与信号 ----------
            var emitted = new List<bool>();
            gs.MouseLockChanged += OnMouseLockChanged;
            void OnMouseLockChanged(bool enabled) => emitted.Add(enabled);
            gs.SetMouseLock(false);
            Check(!gs.MouseLock, "set_mouse_lock(false) 关闭锁定");
            Check(emitted.Count == 1 && !emitted[0], "切换发出 mouse_lock_changed 信号");
            gs.SetMouseLock(false);
            Check(emitted.Count == 1, "同值重复设置不发信号");
            gs.SetMouseLock(true);
            Check(gs.MouseLock, "set_mouse_lock(true) 重新开启");
            Check(emitted.Count == 2 && emitted[1], "再次切换发出开启信号");
            gs.MouseLockChanged -= OnMouseLockChanged;

            // ---------- 3. profile 持久化 ----------
            gs.SetMouseLock(false);
            Check(ReadProfile(gs.PROFILE_PATH).GetValueOrDefault("mouse_lock", true).AsBool() == false, "mouse_lock 写入 profile");
            gs.MouseLock = true;  // 篡改内存（不经 setter，避免写盘）
            gs.LoadProfile();
            Check(!gs.MouseLock, "mouse_lock 从 profile 恢复");

            // ---------- 4. 旧档兼容 ----------
            WriteProfile(gs.PROFILE_PATH, new Godot.Collections.Dictionary { ["version"] = 1, ["high_score"] = 0 });
            gs.MouseLock = false;
            gs.LoadProfile();
            Check(!gs.MouseLock, "旧档（无 mouse_lock 字段）读取保留当前值");

            // ---------- 5. MouseTrap 边界 clamp 纯函数 ----------
            Check(MouseTrap.WarpTarget(new Vector2(-50, -50), new Vector2I(1920, 1080)) == new Vector2(1, 1),
                "clamp 左上越界到 (1,1)");
            Check(MouseTrap.WarpTarget(new Vector2(5000, 5000), new Vector2I(1920, 1080)) == new Vector2(1919, 1079),
                "clamp 右下越界到 (size-1)");
            Check(MouseTrap.WarpTarget(new Vector2(960, 540), new Vector2I(1920, 1080)) == new Vector2(960, 540),
                "窗口内点不变");
            Check(MouseTrap.WarpTarget(Vector2.Zero, new Vector2I(1920, 1080)) == new Vector2(1, 1),
                "原点 (0,0) clamp 到边缘内侧 (1,1)");

            // ---------- 6. 设置页开关 wiring ----------
            var settings = new SettingsUi();
            AddChild(settings);
            settings.ShowSettings();
            var lockBtn = settings.MouseLockButton();
            Check(lockBtn.ButtonPressed == gs.MouseLock, "设置页鼠标锁定按钮选中态 = 当前设置");
            lockBtn.ButtonPressed = false;
            lockBtn.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!gs.MouseLock, "鼠标锁定按钮点击关闭");
            lockBtn.ButtonPressed = true;
            lockBtn.EmitSignal(BaseButton.SignalName.Pressed);
            Check(gs.MouseLock, "鼠标锁定按钮再点开启");
            settings.QueueFree();
            gs.SetMouseLock(true);

            // ---------- 7. confine 放行判定纯函数（仅对局准星态生效；暂停/非准星态放行） ----------
            Check(MouseTrap.TrapEnabled(true, true, true, true, true, true),
                "对局准星活跃态 confine 生效");
            Check(!MouseTrap.TrapEnabled(true, true, true, true, false, true),
                "暂停态放行（暂停后可自由移出窗口，如点系统关闭按钮）");
            Check(!MouseTrap.TrapEnabled(true, true, true, true, true, false),
                "系统光标可见态放行（菜单/设置/基地等非准星态）");
            Check(!MouseTrap.TrapEnabled(false, true, true, true, true, true),
                "设置关闭不 confine");
            Check(!MouseTrap.TrapEnabled(true, false, true, true, true, true),
                "窗口不可见不 confine");
            Check(!MouseTrap.TrapEnabled(true, true, false, true, true, true),
                "窗口失焦不 confine");
            Check(!MouseTrap.TrapEnabled(true, true, true, false, true, true),
                "无窗口尺寸（headless）不 confine");

            // ---------- 8. warp 不引入准星跳变：warp 目标 ≈ 出框前最后位置（位移 ≤ 2px） ----------
            var edgePos = new Vector2(1918, 500);  // 右缘附近出框前最后内部位置
            Check((MouseTrap.WarpTarget(edgePos, new Vector2I(1920, 1080)) - edgePos).Length() <= 2.0f,
                "右缘出框 warp 位移 ≤ 2px（aim_point 平滑增量≈0，无准星跳变）");
            var edgeTop = new Vector2(500, 0);  // 上缘第 0 行（clamp 边界）
            Check((MouseTrap.WarpTarget(edgeTop, new Vector2I(1920, 1080)) - edgeTop).Length() <= 2.0f,
                "上缘出框 warp 位移 ≤ 2px（第 0 行仅 1px 回拉）");

            // 清理：恢复默认并落盘，避免污染其他测试进程
            gs.MouseLock = true;
            gs.ResetRun();
            gs.SaveProfile();
            gs.DeleteSave();
            // M7：还原原始 profile（最高分/高分榜/设置项），防本地数据被清零
            RestoreProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"MOUSE LOCK TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"MOUSE LOCK TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
