using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 轨道打击清场动画测试（M7c 迁移自 test/orbital_strike_test.gd）：触发/_resume_from_base 接线/
/// 命中清场（Boss 保留、弹丸清除、逐机爆炸）/恢复对局（解除暂停、解锁输入、播战机入场动画、
/// spawner 延迟恢复）/动画自销毁。缩短 DURATION 后用真实 Timer 等待推进时轴。
/// </summary>
public partial class OrbitalStrikeTest : Node
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
            gs.DeleteSave();
            gs.ResetRun();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var main = GetNode<Main>("Main");
            var spawner = main.GetNode<Spawner>("Spawner");
            // 全程禁用刷怪与随机事件编排：本测试只验证轨道打击自身，排除波次/事件注册新敌人的时序干扰
            spawner.SetProcess(false);
            main.Event()!.SetProcess(false);
            main.Formation()!.SetProcess(false);

            // ---------- 1. 布置战场：3 台普通敌机 + 1 发弹丸 ----------
            for (int i = 0; i < 3; i++)
            {
                var cfg = spawner.ENEMY_TYPES[i % spawner.ENEMY_TYPES.Count];
                var e = GD.Load<PackedScene>("res://scenes/enemy.tscn").Instantiate<Enemy>();  // M3b：Enemy 迁 C#
                e.Setup(cfg, cfg["strategies"].AsGodotArray()[0].AsStringName(), 1.0f);
                e.Position = new Vector2(400.0f + i * 400.0f, 300.0f);
                main.AddChild(e);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var pool = (BulletPool)gs.BulletPool!;  // 随批次 C 重定型：Fire 返回判空语义不变
            var b = pool.Fire(Vector2.Down, 400.0f, 10, false);
            b!.Position = new Vector2(960.0f, 700.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(gs.Enemies.Count == 3, "布置：3 台敌机已注册");

            // ---------- 2. 模拟基地状态触发继续出击 ----------
            GetTree().Paused = true;
            main.SetHomecoming(true);
            main.Player()!.LockInput();
            main.Player()!.SetInvincible(999.0f);  // 驻留态无敌
            spawner.SetProcess(false);
            main.ResumeFromBase();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var strike = main.Strike();
            Check(strike != null, "触发：轨道打击节点已创建");
            Check(GetTree().Paused, "动画期间树保持暂停");
            main.ResumeFromBase();  // 幂等：播放中重复触发不叠加
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.Strike() == strike, "幂等：重复触发不叠加第二个动画");

            // ---------- 3. 缩短时轴推进到命中 ----------
            strike!.DURATION = 0.6f;
            var struckFired = false;
            strike!.Struck += () => struckFired = true;
            var reachedImpact = false;
            for (int i = 0; i < 60; i++)
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (struckFired)
                {
                    reachedImpact = true;
                    break;
                }
            }
            Check(reachedImpact, "时轴：struck 信号在命中帧发出");
            // queue_free 在帧尾执行：让出两帧再断言清场结果
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!GetTree().Paused, "命中：树恢复非暂停");
            Check(gs.Enemies.Count == 0, $"命中：敌机全部清除（残留 {gs.Enemies.Count} 台）");
            Check(!main.Player()!.IsInputLocked(), "命中：玩家输入解锁");
            Check(main.Player()!.IsEntryPlaying(), "命中：播战机入场动画（替代原地无敌闪现）");
            var inv = main.Player()!.InvincibleRemaining();
            Check(inv > 0.0f && inv <= main.Player()!.ENTRY_INVINCIBLE, "命中：驻留无敌被入场动画接管");
            Check(!spawner.IsProcessing(), "命中：敌机生成延迟（入场动画期间暂停）");
            Check(!main.IsHomecoming(), "命中：homecoming 标志复位");
            var bValid = GodotObject.IsInstanceValid(b);
            var bParent = bValid ? b!.GetParent().Name : new StringName("-");
            Check(!bValid, $"命中：既有弹丸被清除（valid={bValid} parent={bParent}）");
            // 等入场动画结束（约 1.65s），敌机生成恢复
            var tEntry = 0.0;
            while (main.Player()!.IsEntryPlaying() && tEntry < 3.0)
            {
                await Coroutine.WaitSeconds(this, 0.1);
                tEntry += 0.1;
            }
            Check(spawner.IsProcessing(), "入场动画结束：spawner 恢复");

            // ---------- 4. 动画播完自销毁 ----------
            for (int i = 0; i < 60; i++)
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (main.Strike() == null)
                {
                    break;
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.Strike() == null && !GodotObject.IsInstanceValid(strike), "收尾：动画自销毁并释放引用");

            gs.DeleteSave();
            gs.SaveProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"ORBITAL STRIKE TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"[DONE] failures={_failures}");
            TestExit.Quit(_failures > 0 ? 1 : 0);
        }
    }
}
