using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 窗口尺寸档位测试：
/// 三档映射与切换信号、profile 持久化往返、非法/缺失字段回退、设置页三选按钮 wiring。
/// headless 下窗口 API 为 dummy，仅断言数据层；结束时恢复默认档并落盘，避免污染其他测试进程。
/// M7（2026-08-06 审计）：profile 快照还原——原测试经 _write_profile 部分覆写
/// profile.json + load_profile 间接清零 pre-login 最高分/高分榜并落盘，无快照还原
/// （L15 只修直写路径，未覆盖间接清零）；备份/还原防本地数据被永久销毁。
/// </summary>
public partial class WindowSizeTest : Node
{
    private int _failures;
    private GameState _gs = null!;
    private readonly Godot.Collections.Dictionary _profileBackup = new();

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

    /// <summary>M7：profile 快照（须在任何覆写/落盘前捕获原始 pre-login 最高分与高分榜）</summary>
    private void BackupProfile()
    {
        _profileBackup.Clear();
        foreach (var path in new[] { _gs.PROFILE_PATH, _gs.PROFILE_PATH + ".corrupt" })
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
        foreach (var kv in _profileBackup)
        {
            var b = kv.Value.AsGodotDictionary();
            if (b["exists"].AsBool())
            {
                var fh = Godot.FileAccess.Open(kv.Key.AsString(), Godot.FileAccess.ModeFlags.Write);
                fh.StoreString(b["content"].AsString());
                fh.Close();
            }
            else if (Godot.FileAccess.FileExists(kv.Key.AsString()))
            {
                DirAccess.RemoveAbsolute(kv.Key.AsString());
            }
        }
    }

    public override void _Ready()
    {
        Run();
    }

    private void Run()
    {
        try
        {
            _gs = GetNode<GameState>("/root/GameState");
            // M7：profile 快照（须在任何覆写/落盘前捕获原始 pre-login 最高分与高分榜）
            BackupProfile();
            // 确定性起点：清存档，窗口档位归位 large（profile 级，reset_run 不清）
            _gs.DeleteSave();
            _gs.WindowSize = new StringName("large");

            // ---------- 1. 档位映射 ----------
            Check(_gs.WINDOW_SIZE_LEVELS[new StringName("small")].AsVector2I() == new Vector2I(1280, 720), "small 档 = 1280×720");
            Check(_gs.WINDOW_SIZE_LEVELS[new StringName("medium")].AsVector2I() == new Vector2I(1600, 900), "medium 档 = 1600×900");
            Check(_gs.WINDOW_SIZE_LEVELS[new StringName("large")].AsVector2I() == new Vector2I(1920, 1080), "large 档 = 1920×1080");
            var order = _gs.WINDOW_SIZE_ORDER;
            Check(order.Count == 3 && order[0] == new StringName("small") && order[1] == new StringName("medium") && order[2] == new StringName("large"),
                "档位顺序 small/medium/large");

            // ---------- 2. 切换与信号 ----------
            _gs.SetWindowSize(new StringName("small"));
            Check(_gs.WindowSize == new StringName("small"), "set_window_size 切到 small");
            _gs.SetWindowSize(new StringName("medium"));
            Check(_gs.WindowSize == new StringName("medium"), "set_window_size 切到 medium");
            _gs.SetWindowSize(new StringName("large"));
            Check(_gs.WindowSize == new StringName("large"), "set_window_size 切到 large");
            var emitted = new Godot.Collections.Array<StringName>();
            _gs.WindowSizeChanged += level => emitted.Add(level);
            _gs.SetWindowSize(new StringName("small"));
            Check(emitted.Count == 1 && emitted[0] == new StringName("small"), "切换档位发出 window_size_changed 信号");
            _gs.SetWindowSize(new StringName("small"));
            Check(emitted.Count == 1, "同档重复设置不发信号");
            _gs.SetWindowSize(new StringName("huge"));
            Check(_gs.WindowSize == new StringName("small"), "非法档位被忽略");

            // ---------- 3. profile 持久化 ----------
            _gs.SetWindowSize(new StringName("medium"));
            Check(ReadProfile().GetValueOrDefault("window_size", "").AsString() == "medium", "窗口档位写入 profile");
            _gs.WindowSize = new StringName("small");  // 篡改内存（不经 setter，避免写盘）
            _gs.LoadProfile();
            Check(_gs.WindowSize == new StringName("medium"), "窗口档位从 profile 恢复");
            // 旧档案无 window_size 字段：保留当前值（回默认 large 语义由默认值保证）
            WriteProfile(new Godot.Collections.Dictionary { ["version"] = 1, ["high_score"] = 0 });
            _gs.WindowSize = new StringName("small");
            _gs.LoadProfile();
            Check(_gs.WindowSize == new StringName("small"), "旧档（无 window_size 字段）读取保留当前档位");
            // 非法档位值：忽略并保持当前值
            WriteProfile(new Godot.Collections.Dictionary { ["version"] = 1, ["high_score"] = 0, ["window_size"] = "huge" });
            _gs.LoadProfile();
            Check(_gs.WindowSize == new StringName("small"), "profile 非法档位值被忽略");
            _gs.SetWindowSize(new StringName("large"));

            // ---------- 4. 设置页三选按钮 ----------
            var settings = new SettingsUi();
            AddChild(settings);
            settings.ShowSettings();
            var buttons = settings.WindowButtons();
            Check(buttons.Count == 3, "设置页窗口大小三选按钮");
            Check(((Button)buttons[new StringName("large")].AsGodotObject()).ButtonPressed, "窗口大小按钮选中态 = 当前档");
            ((Button)buttons[new StringName("medium")].AsGodotObject()).EmitSignal(BaseButton.SignalName.Pressed);
            Check(_gs.WindowSize == new StringName("medium"), "窗口大小按钮点击切换档位");
            settings.QueueFree();
            _gs.SetWindowSize(new StringName("large"));
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"WINDOW SIZE TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"WINDOW SIZE TEST DONE, failures = {_failures}");
            // 清理：恢复默认档并落盘，避免污染其他测试进程
            _gs?.SetWindowSize(new StringName("large"));
            _gs?.ResetRun();
            _gs?.SaveProfile();
            _gs?.DeleteSave();
            // M7：还原原始 profile（最高分/高分榜/设置项），防本地数据被清零
            RestoreProfile();
            TestExit.Quit(_failures);
        }
    }
}
