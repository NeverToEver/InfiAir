using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 波次化刷怪与悬停机动测试：普通波成组均布入场、敌机锚点悬停、精英特殊槽节奏
/// （3~4 普通波一个精英波）、Boss 激活暂停普通波次、精英/Boss 击杀追加休整波次。
/// </summary>
public partial class WavePacingTest : Node
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

    private Godot.Collections.Array<Enemy> Enemies()
    {
        var result = new Godot.Collections.Array<Enemy>();
        foreach (var child in GetNode<Main>("Main").GetChildren())
        {
            if (child is Enemy e)
            {
                result.Add(e);
            }
        }
        return result;
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
            // 开场面板自显即暂停（冻结背景），先关闭解除
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);  // 禁用自动开火，避免误伤与意外得分里程碑
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);  // 停掉自动刷怪/Boss 调度，保证确定性
            // 隔离 Boss 触发链（分数/时间门），本测试只关心波次节奏
            spawner.SetNextBossScore(999999999);
            spawner.BOSS_TIME_LIMIT = 999999.0f;

            // 1. 普通波成组刷出，x 落在各自均分槽位内，锚点在悬停带内
            var view = gs.ViewWorldRect();
            var n = spawner.WaveSize();
            spawner.SpawnNormalWave();
            await Coroutine.WaitSeconds(this, 1.0);  // 0.6s 预告后进场
            var wave = Enemies();
            Check(wave.Count == n, $"普通波成组刷出（{n} 架）");
            var slotW = (view.Size.X - 120.0f) / n;
            var band = new Vector2(
                view.Position.Y + spawner.HoverBand().X,
                view.Position.Y + spawner.HoverBand().Y
            );  // 悬停带为 view 顶缘偏移（2026-07-30 view 适配）
            var slotsOk = true;
            var anchorOk = true;
            foreach (var e in wave)
            {
                var rel = e.Position.X - (view.Position.X + 60.0f);
                var idx = (int)(rel / slotW);
                if (idx < 0 || idx >= n)
                {
                    slotsOk = false;
                }
                if (e.AnchorY < band.X || e.AnchorY > band.Y)
                {
                    anchorOk = false;
                }
            }
            Check(slotsOk, "普通波 x 均匀分布（各机落在均分槽位内）");
            Check(anchorOk, "普通波锚点位于悬停带内");

            // 2. 敌机到达锚点后悬停机动：y 不再净下降（绕锚点 ±HOVER_BOB_AMP 浮动）
            Enemy? hoverE = null;
            foreach (var e in wave)
            {
                if (e.Strategy == "straight" || e.Strategy == "hover")
                {
                    hoverE = e;
                    break;
                }
            }
            hoverE ??= wave[0];
            var tWait = 0.0;
            while (GodotObject.IsInstanceValid(hoverE) && !hoverE.Hovering() && tWait < 6.0)
            {
                await Coroutine.WaitSeconds(this, 0.2);
                tWait += 0.2;
            }
            Check(GodotObject.IsInstanceValid(hoverE) && hoverE.Hovering(), "敌机到达锚点转入悬停");
            if (GodotObject.IsInstanceValid(hoverE) && hoverE.Hovering())
            {
                var maxDev = 0.0f;
                for (int i = 0; i < 5; i++)
                {
                    await Coroutine.WaitSeconds(this, 0.2);
                    if (GodotObject.IsInstanceValid(hoverE))
                    {
                        maxDev = Mathf.Max(maxDev, Mathf.Abs(hoverE.Position.Y - hoverE.AnchorY));
                    }
                }
                Check(maxDev <= hoverE.HoverBobAmp + 1.0f, "悬停期间绕锚点浮动（无净下降）");
            }
            foreach (var e in wave)
            {
                if (GodotObject.IsInstanceValid(e))
                {
                    e.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 3. Boss 激活期间普通波次计时冻结（Boss 占用波次槽）
            spawner.SetBossActive(true);
            spawner.SetWaveTimer(3.0f);
            spawner.SetProcess(true);
            await Coroutine.WaitSeconds(this, 0.5);
            Check(spawner.WaveTimer() == 3.0f, "Boss 激活期间波次计时不推进");
            spawner.SetBossActive(false);
            spawner.SetProcess(false);

            // 4. 精英节奏：连续 3~4 个普通波后出现精英波（计数归零）
            spawner.WAVE_INTERVAL_START = 0.05f;
            spawner.WAVE_INTERVAL_END = 0.05f;
            spawner.INTERVAL_MIN = 0.05f;
            spawner.SetWaveTimer(0.05f);
            spawner.SetWavesSinceSpecial(0);
            var gap = -1;
            var prev = 0;
            spawner.SetProcess(true);
            var t4 = 0.0;
            while (gap < 0 && t4 < 10.0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                t4 += GetProcessDeltaTime();
                var cur = spawner.WavesSinceSpecial();
                if (prev > 0 && cur == 0)
                {
                    gap = prev;
                }
                prev = cur;
            }
            spawner.SetProcess(false);  // 精英波已触发（计数归零），立即冻结节奏断言现场
            // 精英有 0.6s 入场预告，等其实际进场
            var eliteSeen = false;
            var t5 = 0.0;
            while (!eliteSeen && t5 < 2.0)
            {
                await Coroutine.WaitSeconds(this, 0.1);
                t5 += 0.1;
                foreach (var e in Enemies())
                {
                    if (e.IsElite)
                    {
                        eliteSeen = true;
                    }
                }
            }
            Check(gap >= spawner.SPECIAL_GAP_MIN && gap <= spawner.SPECIAL_GAP_MAX, $"精英节奏：{gap} 个普通波后出精英波");
            Check(eliteSeen, "精英波产出精英敌机");
            Check(spawner.WavesSinceSpecial() == 0, "精英波后特殊槽计数归零");
            foreach (var e in Enemies())
            {
                if (GodotObject.IsInstanceValid(e))
                {
                    e.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 5. 休整：精英/Boss 击杀后追加 REST_WAVES_AFTER_KILL 个普通波（计数置负）
            spawner.NotifySpecialKilled();
            Check(spawner.WavesSinceSpecial() == -spawner.REST_WAVES_AFTER_KILL, "精英击杀后进入休整波次");
            spawner.SetWavesSinceSpecial(0);
            spawner.NotifyBossDied();
            Check(spawner.WavesSinceSpecial() == -spawner.REST_WAVES_AFTER_KILL, "Boss 击杀后进入休整波次");

            // 6. H1（2026-08-06 审计）：Boss 已在场时 clear_pending 不得复位 _boss_active——
            // 返航链明确保留 Boss（_next_boss_score 仅击杀推进、_boss_timer 战时持续增长），
            // 复位后 continue 分数门控立即满足 → 出第二个同型 Boss 双 Boss 同场
            spawner.SetBossActive(true);
            spawner.SpawnBoss();  // Boss 直接生成并入注册表（跳过 2s 预警）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            spawner.ClearPending();
            Check(spawner.IsBossActive(), "H1：Boss 在场时 clear_pending 保持占用（防双 Boss）");
            // 清掉场上 Boss 后再验证对照分支（G01 原语义）：无存活 Boss 时解除占用
            foreach (var child in GetNode<Main>("Main").GetChildren())
            {
                if (child is Boss)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            spawner.SetBossActive(true);
            spawner.ClearPending();
            Check(!spawner.IsBossActive(), "H1：无存活 Boss 时 clear_pending 解除占用（G01）");

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"WAVE PACING TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"WAVE PACING TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
