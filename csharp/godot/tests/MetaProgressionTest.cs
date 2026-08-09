using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 局外成长（Meta Progression，2026-08-09 计划 M5）集成断言场景：
/// 游客结算 no-op / 死亡结算入账（公式 = core PointsForRun）/ 升级消费与满级上限 /
/// 新局开局预置 buff 层数（ApplyMetaLoadout）/ 重登持久化往返 / 放弃路径（仅删档）不结算 /
/// 手改非法 meta 判型回默认。只操作 GameState autoload 与 UserDb，不加载 main 场景。
/// 用户文件备份/恢复（Q23 范式）：不污染开发者真实账户表。
/// </summary>
public partial class MetaProgressionTest : Node
{
    private int _failures;
    private GameState _gs = null!;
    private Godot.Collections.Dictionary _fileBackups = new();

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

    private void BackupUserFiles()
    {
        _fileBackups = new Godot.Collections.Dictionary();
        var files = new Godot.Collections.Array<string>
        {
            "user://users.json",
            "user://users.json.corrupt",
            "user://profile.json",
            "user://profile.json.corrupt",
            "user://savegame.json",
            "user://savegame.json.corrupt",
        };
        var dir = DirAccess.Open("user://");
        if (dir != null)
        {
            dir.ListDirBegin();
            var name = dir.GetNext();
            while (name != "")
            {
                if (name.StartsWith("savegame_") || name.EndsWith(".corrupt"))
                {
                    files.Add("user://" + name);
                }

                name = dir.GetNext();
            }

            dir.ListDirEnd();
        }

        foreach (var f in files)
        {
            var exists = Godot.FileAccess.FileExists(f);
            _fileBackups[f] = new Godot.Collections.Dictionary
            {
                ["exists"] = exists,
                ["content"] = exists ? Godot.FileAccess.GetFileAsString(f) : "",
            };
        }
    }

    private void RestoreUserFiles()
    {
        foreach (var key in _fileBackups.Keys)
        {
            var f = key.AsString();
            var b = _fileBackups[key].AsGodotDictionary();
            if (b["exists"].AsBool())
            {
                var fh = Godot.FileAccess.Open(f, Godot.FileAccess.ModeFlags.Write);
                fh.StoreString(b["content"].AsString());
                fh.Close();
            }
            else if (Godot.FileAccess.FileExists(f))
            {
                DirAccess.RemoveAbsolute(f);
            }
        }
    }

    private void WipeUserFiles()
    {
        foreach (var f in new[]
        {
            "user://users.json",
            "user://users.json.corrupt",
            "user://profile.json",
            "user://profile.json.corrupt",
            "user://savegame.json",
            "user://savegame.json.corrupt",
        })
        {
            if (Godot.FileAccess.FileExists(f))
            {
                DirAccess.RemoveAbsolute(f);
            }
        }
    }

    private void WipeUserSaves()
    {
        var dir = DirAccess.Open("user://");
        if (dir == null)
        {
            return;
        }

        dir.ListDirBegin();
        var name = dir.GetNext();
        while (name != "")
        {
            if (name.StartsWith("savegame_") || name.EndsWith(".corrupt"))
            {
                DirAccess.RemoveAbsolute("user://" + name);
            }

            name = dir.GetNext();
        }

        dir.ListDirEnd();
    }

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget（约定 §Async）
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            _gs = GetNode<GameState>("/root/GameState");
            BackupUserFiles();
            _gs.LogoutUser();
            _gs.DeleteSave();
            WipeUserFiles();
            WipeUserSaves();
            _gs.ReloadUserDb();
            _gs.ResetRun();

            // 1. 游客：结算 no-op、不预置（B7-8 不持久化）
            _gs.LoginGuest();
            _gs.ResetRun();
            _gs.Score = 5000;
            _gs.SettleTechPoints();
            Check(_gs.TechPoints == 0, "游客死亡结算不产生科技点");
            _gs.ApplyMetaLoadout();
            Check(_gs.Buffs.Count == 0, "游客新局不预置 buff（无局外成长档案）");

            // 2. 注册 + 登录：meta 默认空档案
            Check(_gs.CreateUser("meta1", "pass123"), "注册测试用户");
            _gs.LoginUser("meta1");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_gs.TechPoints == 0, "新用户科技点为 0");
            Check(_gs.MetaUpgradeIds().Count > 0, "升级项列表非空（balance.json meta.upgrades）");
            Check(_gs.MetaLevel("rapid_fire") == 0, "新用户未购升级等级为 0");

            // 3. 死亡结算入账：3 局 × (score 12500 / 1000 = 12) = 36 点
            // （SettleTechPoints 幂等累计；正常路径 SettleRun 每局一次）
            for (var i = 0; i < 3; i++)
            {
                _gs.ResetRun();
                _gs.Score = 12500;
                _gs.SettleTechPoints();
            }

            Check(_gs.TechPoints == 36, "三局死亡累计结算 36 点（公式：score/1000）");

            // 4. 升级消费与满级上限（rapid_fire：一级 10、二级 16、max_level 2）
            var cost1 = _gs.MetaUpgradeCost("rapid_fire");
            Check(cost1 == 10, $"rapid_fire 一级费用 = {cost1}");
            Check(_gs.CanUpgradeMeta("rapid_fire"), "未购项可升级");
            Check(_gs.SpendTechPoints("rapid_fire"), "消费科技点升级成功");
            Check(_gs.TechPoints == 26, "升级后余额扣减正确（36 - 10）");
            Check(_gs.MetaLevel("rapid_fire") == 1, "rapid_fire 升到 1 级");
            Check(!_gs.SpendTechPoints(new StringName("nonexistent")), "未知项消费失败");
            var cost2 = _gs.MetaUpgradeCost("rapid_fire");
            Check(cost2 == 16, $"rapid_fire 二级费用 = {cost2}");
            Check(_gs.SpendTechPoints("rapid_fire"), "二级消费成功");
            Check(_gs.TechPoints == 10, "二级升级后余额 = 10");
            Check(!_gs.SpendTechPoints("rapid_fire"), "满级后拒绝升级");
            Check(_gs.MetaUpgradeCost("rapid_fire") == 0, "满级后费用为 0");

            // 5. 新局开局预置：已购 2 级 rapid_fire
            _gs.ResetRun();
            _gs.ApplyMetaLoadout();
            Check((int)_gs.Buffs.GetValueOrDefault("rapid_fire", 0).AsInt64() == 2, "新局预置 rapid_fire 2 层");
            Check(_gs.Buffs.Count == 1, "预置仅含已购项");

            // 6. 存档往返：重登后 meta 持久化
            _gs.LogoutUser();
            _gs.LoginUser("meta1");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_gs.MetaLevel("rapid_fire") == 2, "重登后升级等级持久化");
            Check(_gs.TechPoints == 10, "重登后科技点余额持久化");

            // 7. 放弃路径不结算：放弃 = 删档 + 退出（无 SettleRun），科技点不变
            _gs.Score = 9000;
            var before = _gs.TechPoints;
            _gs.DeleteSave();
            Check(_gs.TechPoints == before, "放弃路径（仅删档）不结算科技点");

            // 8. 手改非法 meta 判型回默认（防御；对齐 U15/Q17）
            _gs.UpdateUserData("meta1", new Godot.Collections.Dictionary { ["meta"] = "garbage" });
            _gs.LogoutUser();
            _gs.LoginUser("meta1");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_gs.TechPoints == 0 && _gs.MetaLevel("rapid_fire") == 0, "手改非法 meta 回默认空档案");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"META PROGRESSION TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"META PROGRESSION TEST DONE, failures = {_failures}");
            _gs?.DeleteSave();
            RestoreUserFiles();
            TestExit.Quit(_failures);
        }
    }
}
