using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// Buff 外观反馈测试：PlayerBuffVisuals 附件随 buffs_changed 信号显隐/强度刷新，
/// 覆盖 16 种 buff 的外观映射、层数强度、天赋路线合并与重开清空。
/// </summary>
public partial class BuffVisualsTest : Node
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

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            var main = GetNode<Main>("Main");
            // 开场面板自显即暂停（冻结背景），先关闭解除
            var player = GetNode<Player>("Main/Player");
            var spawner = GetNode<Spawner>("Main/Spawner");
            // 关闭自动开火与刷怪，避免对局逻辑干扰外观断言
            player.SetAutoFire(false);
            player.SetInvincible(999.0f);
            spawner.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var child in main.GetChildren())
            {
                if (child is Enemy || child.HasMethod("TryGraze"))
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 0. 初始状态：外观节点存在且全部附件隐藏、尾焰染色为白
            PlayerBuffVisuals? visuals = null;
            foreach (var child in player.GetChildren())
            {
                if (child is PlayerBuffVisuals v)
                {
                    visuals = v;
                }
            }
            Check(visuals != null, "玩家挂有 PlayerBuffVisuals 外观节点");
            if (visuals == null)
            {
                // L15：还原用户最高分并落盘（收尾不污染用户 profile；本文件有两条退出路径，均还原）
                gs.HighScore = origHighScore;
                gs.SaveProfile();
                gs.DeleteSave();
                return;  // DONE 与退出统一走 finally
            }
            Check(
                !visuals.PowerGlow().Visible
                && !visuals.ShieldHex().Visible
                && !visuals.RegenRing().Visible
                && !visuals.Beacon().Visible,
                "初始无 buff 全部附件隐藏"
            );
            Check(visuals.SpreadPods().Count == 3 && !visuals.SpreadPods()[0].Visible, "散射炮舱初始隐藏");
            Check(player.EngineTint == Colors.White, "初始尾焰染色为白");

            // 1. 火力类外观：逐 buff 显隐 + 层数强度
            gs.AddBuff("power_shot");
            Check(visuals.PowerGlow().Visible, "power_shot 机头金色辉光可见");
            var glowScale1 = visuals.PowerGlow().Scale.X;
            for (int i = 0; i < 4; i++)
            {
                gs.AddBuff("power_shot");
            }
            Check(visuals.PowerGlow().Scale.X > glowScale1, "power_shot 层数提升辉光强度");
            gs.AddBuff("rapid_fire");
            Check(visuals.RapidFins().Visible, "rapid_fire 散热鳍可见");
            gs.AddBuff("spread_shot");
            Check(visuals.SpreadPods()[0].Visible && !visuals.SpreadPods()[1].Visible, "spread_shot 1 层 1 个炮舱");
            gs.AddBuff("spread_shot");
            gs.AddBuff("spread_shot");
            Check(visuals.SpreadPods()[2].Visible, "spread_shot 3 层 3 个炮舱");
            gs.AddBuff("piercing");
            Check(visuals.PierceSpike().Visible, "piercing 穿甲尖刺可见");
            gs.AddBuff("explosive");
            Check(visuals.ExplosiveGlow().Visible, "explosive 机腹辉光可见");
            gs.AddBuff("laser_beam");
            Check(visuals.LaserPod().Visible, "laser_beam 发射基座可见");

            // 2. 生存类外观
            gs.AddBuff("extra_life");
            Check(visuals.ArmorRing().Visible, "extra_life 装甲环可见");
            var ringWidth1 = visuals.ArmorRing().Width;
            gs.AddBuff("extra_life");
            Check(visuals.ArmorRing().Width > ringWidth1, "extra_life 层数加粗装甲环");
            gs.AddBuff("regen");
            Check(visuals.RegenRing().Visible, "regen 呼吸光环可见");
            gs.AddBuff("lifesteal");
            Check(visuals.LifestealTips().Visible, "lifesteal 翼尖可见");
            gs.AddBuff("armor");
            Check(visuals.ShieldHex().Visible, "armor 护盾弧可见");
            gs.AddBuff("evasion");
            Check(visuals.EvasionGhost().Visible, "evasion 残像覆盖层可见");

            // 3. 机动/系统类外观
            gs.AddBuff("phase_dash");
            Check(visuals.DashFins().Visible, "phase_dash 相位鳍可见");
            gs.AddBuff("slow_field");
            Check(visuals.SlowRing().Visible, "slow_field 力场环可见");
            gs.AddBuff("mothership_recall");
            Check(visuals.Beacon().Visible, "mothership_recall 信标可见");

            // 4. 尾焰染色：高效推进偏绿、叠加燃料再生偏金
            gs.AddBuff("efficient_boost");
            Check(player.EngineTint.G > player.EngineTint.R, "efficient_boost 尾焰偏绿");
            var tintREff = player.EngineTint.R;
            gs.AddBuff("boost_recovery");
            Check(player.EngineTint.R > tintREff && player.EngineTint.B < player.EngineTint.G, "叠加 boost_recovery 尾焰转金");

            // 5. 天赋路线合并：spread(3)+laser(1) 合入 laser_beam，散射炮舱隐藏、基座保留
            gs.ChooseRoute("offense", "laser_beam");
            Check(gs.BuffCount("laser_beam") == 4, "路线合并层数叠加");
            Check(!visuals.SpreadPods()[0].Visible, "路线锁定后散射炮舱隐藏");
            Check(visuals.LaserPod().Visible, "路线所选 buff 外观保留");

            // 6. 重开清空：reset_run 发 buffs_changed，全部附件隐藏、染色复位
            gs.ResetRun();
            Check(
                !visuals.PowerGlow().Visible
                && !visuals.ShieldHex().Visible
                && !visuals.SlowRing().Visible
                && !visuals.LaserPod().Visible,
                "重开后全部附件隐藏"
            );
            Check(player.EngineTint == Colors.White, "重开后尾焰染色复位");

            // 7. 存档恢复：apply_run_save 恢复 buffs 后外观同步
            gs.ApplyRunSave(new Godot.Collections.Dictionary { ["version"] = 2, ["buffs"] = new Godot.Collections.Dictionary { ["armor"] = 1, ["spread_shot"] = 2 } });
            Check(visuals.ShieldHex().Visible, "存档恢复 armor 护盾弧可见");
            Check(
                visuals.SpreadPods()[0].Visible && visuals.SpreadPods()[1].Visible && !visuals.SpreadPods()[2].Visible,
                "存档恢复 spread_shot 2 层 2 个炮舱"
            );

            // L15：还原用户最高分并落盘（收尾不污染用户 profile；本文件有两条退出路径，均还原）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.ResetRun();
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BUFF VISUALS TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BUFF VISUALS TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
