using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// Meta HUD 血量/受击反馈测试（docs/META_HUD_DESIGN.md §7）：
/// 1 take_damage 信号携带 amount/from_pos、无敌帧期不发射；2 hit_pulse max 池化不累积；
/// 3 血量-裂纹曲线采样（pow(x,1.6)：x=0.25/0.50/0.75/0.90 → 0.11/0.33/0.63/0.84，±0.02）；
/// 4 状态机下行快入/上行慢出 + 修复期 _heal_jitter 0→0.35→0 全程；
/// 5 DYING 心率 [1.0,1.2]Hz、breath_scale ∈ [0.985,1.015]、减少闪光禁呼吸；
/// 6 LOD1 时 hud 旧晕影回退、LOD0 移交 MetaFX；7 满血静止 60 帧早退零参数上传（D5）。
/// </summary>
public partial class MetaHealthFxTest : Node
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

    /// <summary>重置玩家受击状态并关闭被动回血（_since_damage=0 < 4s 延迟，计时窗内不回血）</summary>
    private void ResetHitState(Player player)
    {
        player.SetInvincible(0.0f);
        player.SetLastHitFrame(-1);
        player.SetSinceDamage(0.0f);
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

            // 清理持久化状态，保证测试确定性（含 reduce_flash 默认关、跳过欢迎页暂停）
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.ReduceFlash = false;
            gs.SaveProfile();
            AddChild(GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate());
            var main = GetNode<Main>("Main");
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetTree().Paused = false;  // 开始面板/欢迎页路径可能带暂停态
            GetNode<Spawner>("Main/Spawner").SetProcess(false);  // 停掉自动刷怪/Boss 调度，保证确定性
            foreach (var child in main.GetChildren())
            {
                // 随批次 C/M3b 重定型：C# 类（Bullet/Enemy）直接 typed 判定
                if (child is Enemy || child is Bullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            player.Position = new Vector2(960.0f, 800.0f);
            var fx = main.MetaFx();
            Check(fx != null, "0：main._ready 创建 MetaHealthFX");
            Check(gs.MetaFxLod == 0, "0：LOD0 时 GameState.meta_fx_lod 置 0（hud 移交晕影）");

            // ================= 1：player_damaged 信号 =================
            var records = new Godot.Collections.Array<Godot.Collections.Array>();
            GameState.PlayerDamagedEventHandler handler = (amount, fromPos) =>
            {
                records.Add(new Godot.Collections.Array { amount, fromPos });
            };
            gs.PlayerDamaged += handler;
            gs.Health = 100.0;
            ResetHitState(player);
            player.TakeDamage(10.0f, new Vector2(400.0f, 300.0f));
            Check(records.Count == 1, "1：受击发射 player_damaged");
            Check(records.Count == 1 && records[0][0].AsDouble() == 10.0 && records[0][1].AsVector2() == new Vector2(400.0f, 300.0f),
                "1：信号携带 amount 与 from_pos");
            player.TakeDamage(10.0f);  // 无敌帧期内
            Check(records.Count == 1, "1：无敌帧期不发射");
            gs.PlayerDamaged -= handler;

            // ================= 2：hit_pulse max 池化 =================
            fx!.SetTestState(new Godot.Collections.Dictionary { ["hit_pulse"] = 0.0 });  // 清掉测试 1 的 0.25 残留，隔离验证池化
            for (int i = 0; i < 10; i++)
            {
                ResetHitState(player);
                gs.Health = 100.0;
                player.TakeDamage(5.0f);  // r=0.05 → clamp(0.125, 0.15, 1.0)=0.15
            }
            Check(fx.HitPulse() <= 0.151f, "2：连续 10 次 r=0.05 伤害 _hit_pulse 不累积（≤0.15）");
            Check(fx.HitPulse() >= 0.149f, "2：max 池化取到单次峰值 0.15");

            // ================= 3：血量-裂纹映射曲线采样 =================
            fx.SetTestState(new Godot.Collections.Dictionary { ["hit_pulse"] = 0.0 });
            var curveCases = new Godot.Collections.Array<Godot.Collections.Array>
            {
                new Godot.Collections.Array { 0.25, 0.11 },
                new Godot.Collections.Array { 0.50, 0.33 },
                new Godot.Collections.Array { 0.75, 0.63 },
                new Godot.Collections.Array { 0.90, 0.84 },
            };
            var curveOk = true;
            foreach (var c in curveCases)
            {
                fx.SetTestState(new Godot.Collections.Dictionary { ["damage_x"] = c[0] });
                if (Mathf.Abs(fx.CrackProgress() - c[1].AsDouble()) > 0.02)
                {
                    curveOk = false;
                }
            }
            Check(curveOk, "3：x=0.25/0.50/0.75/0.90 → crack_progress≈0.11/0.33/0.63/0.84（±0.02）");

            // ================= 4：状态机快入慢出 + 修复错峰消散 =================
            gs.Health = 100.0;
            gs.EmitSignal(GameState.SignalName.HealthChanged, 100.0);
            fx.SetTestState(new Godot.Collections.Dictionary { ["damage_x"] = 0.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["state"] = MetaHealthFX.GetStateNormal() });
            ResetHitState(player);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            gs.LoseHealth(90.0);  // x 目标 0.9，tau=0.10 快入
            await Coroutine.WaitSeconds(this, 0.4);
            Check(fx.DamageX() > 0.8f, "4：下行快入（0.4s 趋近 x=0.9）");
            Check(fx.State() == MetaHealthFX.GetStateDying(), "4：跨过全部阈值进 DYING");
            gs.Heal(999.0);  // 上行慢出 tau=0.80
            var maxJitter = 0.0f;
            for (int i = 0; i < 18; i++)  // 1.8s 观测窗（覆盖末次跨阈值后的 0.7s 消散全程）
            {
                await Coroutine.WaitSeconds(this, 0.1);
                maxJitter = Mathf.Max(maxJitter, fx.HealJitter());
            }
            Check(maxJitter > 0.3f, "4：修复期 _heal_jitter 经历 0→0.35 峰值");
            Check(fx.HealJitter() < 0.02f, "4：0.7s 全程后 _heal_jitter 回落 0");
            Check(fx.CrackProgress() < 0.2f, "4：上行修复后 crack_progress 回落");

            // ================= 5：DYING 临界层 =================
            gs.Health = 15.0;
            gs.EmitSignal(GameState.SignalName.HealthChanged, 15.0);  // x=0.85 → 心率 lerp(1.0,1.2,0.25)=1.05
            ResetHitState(player);
            await Coroutine.WaitSeconds(this, 0.6);  // 快入趋稳
            Check(fx.State() == MetaHealthFX.GetStateDying(), "5：hp<20% 进 DYING");
            Check(fx.HeartRate() >= 1.0f && fx.HeartRate() <= 1.2f, "5：心率 ∈ [1.0,1.2]Hz");
            var bmin = 1.0f;
            var bmax = 1.0f;
            var t0 = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - t0 < 600)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                bmin = Mathf.Min(bmin, fx.BreathScale());
                bmax = Mathf.Max(bmax, fx.BreathScale());
            }
            Check(bmin >= 0.984f && bmax <= 1.016f, "5：breath_scale() ∈ [0.985,1.015]");
            Check(bmax > 1.005f || bmin < 0.995f, "5：呼吸确实在摆动");
            Check(fx.BreathActive(), "5：DYING 呼吸激活");
            gs.SetReduceFlash(true);
            Check(!fx.BreathActive(), "5：减少闪光后 breath_active()==false");
            gs.SetReduceFlash(false);

            // ================= 6：LOD1 时 hud 旧晕影回退（D2） =================
            var hud = GetNode<Hud>("Main/HUD");
            gs.Health = 10.0;
            gs.EmitSignal(GameState.SignalName.HealthChanged, 10.0);
            fx.SetLod(1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(hud.Vignette().Modulate.A > 0.0f, "6：LOD1 时 hud 低血晕影回退生效");
            fx.SetLod(0);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(hud.Vignette().Modulate.A == 0.0f, "6：LOD0 时 hud 晕影移交 MetaFX（恒 0）");

            // ================= 7：满血静止早退零参数上传（D5/D10） =================
            gs.Heal(999.0);
            fx.SetTestState(new Godot.Collections.Dictionary { ["damage_x"] = 0.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["target_x"] = 0.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["state"] = MetaHealthFX.GetStateNormal() });
            fx.SetTestState(new Godot.Collections.Dictionary { ["hit_pulse"] = 0.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["ripple_t"] = 2.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["heart_phase"] = -1.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["heart_env"] = 0.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["heal_t"] = -1.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["heal_jitter"] = 0.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["grow_boost"] = 0.0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["breath"] = 1.0 });
            for (int i = 0; i < 5; i++)  // 吸收残留过渡，进入稳态
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            fx.SetTestState(new Godot.Collections.Dictionary { ["upload_count"] = 0 });
            fx.SetTestState(new Godot.Collections.Dictionary { ["early_out_count"] = 0 });
            for (int i = 0; i < 60; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Check(fx.UploadCount() == 0, "7：满血静止 60 帧零参数上传（D5）");
            Check(fx.EarlyOutCount() >= 60, "7：早退命中（_process 早退）");
            Check(!fx.Rect().Visible, "7：满血稳态隐藏全屏 ColorRect（零 GPU）");

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.DeleteSave();
            gs.ReduceFlash = false;
            gs.SaveProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"META HEALTH FX TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"META HEALTH FX TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
