using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 迷雾事件系统测试（2026-08-05，docs/FOG_EVENTS.md §2）：
/// 管理器挂载与信号 / 单事件并发 / Duration 到期自动清除 / MinInterval 冷却门控 /
/// 概率触发（try_trigger）/ 4 种事件效果与玩家侧信号联动（输入反转/子弹偏移/方向脉冲/
/// 伪敌机无碰撞）/ 返航清除。
/// 加载 main 场景（player 信号联动需要真实 Player 实例）。
/// </summary>
public partial class FogEventTest : Node
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

    /// <summary>M4：测试事件类迁 C#（GDScript 不能继承 C# 类）——直接 new 实例。</summary>
    private SelfEndTestEvent MakeSelfEndEvent() => new();

    private MinimalTestEvent MakeMinimalEvent() => new();

    /// <summary>真实时间等待（不受 time_scale 影响）。</summary>
    private async Task WaitReal(double sec)
    {
        var timer = GetTree().CreateTimer(sec, true, false, true);
        await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>轮询等事件结束（最多 timeout 秒真实时间）。</summary>
    private async Task<bool> WaitIdle(FogEventManager manager, double timeout = 5.0)
    {
        var left = timeout;
        while (left > 0.0)
        {
            if (manager.ActiveId() == new StringName())
            {
                return true;
            }

            await WaitReal(0.1);
            left -= 0.1;
        }

        return manager.ActiveId() == new StringName();
    }

    private Godot.Collections.Array<Bullet> PlayerBullets()
    {
        var outArr = new Godot.Collections.Array<Bullet>();
        foreach (Node child in GetNode<Main>("Main").GetChildren())
        {
            if (child is Bullet b && b.IsPlayerBullet)
            {
                outArr.Add(b);
            }
        }

        return outArr;
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
            gs.SetDifficulty("medium");
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();
            AddChild(mainScene.Instantiate());
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);
            player.SetInvincible(999.0f);
            player.Position = new Vector2(960.0f, 800.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false); // 手动驱动，保证确定性
            var manager = gs.FogEvents;

            // 1. 管理器挂载与对局活跃开关（测试上下文 main 非 current_scene，自动触发默认关闭；
            // 本用例需要，显式开启——真实对局由 main._ready 自动开启）
            Check(manager != null && manager.GetParent() == gs, "初始化：FogEventManager 挂 GameState 下");
            manager!.SetRunActive(true);
            Check(manager.IsRunActive(), "初始化：run_active 开启（测试显式）");
            Check(manager.EventIds().Count == 4, "初始化：4 种迷雾事件注册");
            // 测试期间禁用概率自动触发（全部走 force_trigger/try_trigger 显式路径，保证确定性）
            manager.TRIGGER_CHANCE = 0.0f;

            // 2. 单事件并发 + 强制触发 + 信号联动
            // 2a. fake_enemies：伪敌机生成，且无伤害/无碰撞（不入注册表/不入组/无碰撞体）
            Check(manager.ForceTrigger("fake_enemies"), "强制触发 fake_enemies");
            Check(manager.ActiveId() == new StringName("fake_enemies"), "事件进行中 active_id 正确");
            Check(!manager.ForceTrigger("mental_confusion"), "事件进行中不可触发新事件（单并发）");
            Check(
                manager.SpawnedFakes().Count == gs.Cfg("fog_events.fake_enemies.count", 5).AsInt32(),
                "伪敌机生成数量 = count 档位");
            // 2026-08-06 审计 M3：出生深度（顶缘上 20~260px）不得触发 280px 出屏销毁余量——
            // 原 80px 余量使约 75% 个体首个物理帧即被销毁；等待出生销毁窗口后断言全部存活
            await WaitReal(0.2);
            var fakesAlive = true;
            foreach (var fake in manager.SpawnedFakes())
            {
                if (!GodotObject.IsInstanceValid(fake) || !fake.IsInsideTree())
                {
                    fakesAlive = false;
                }
            }

            Check(fakesAlive, "伪敌机出生后全部存活（出生深度在出屏销毁余量内）");
            var fakeClean = true;
            foreach (var fake in manager.SpawnedFakes())
            {
                if (!GodotObject.IsInstanceValid(fake) || fake.IsInGroup("enemy") || gs.Enemies.Contains(fake))
                {
                    fakeClean = false;
                }

                if (fake is Area2D)
                {
                    fakeClean = false;
                }
            }

            Check(fakeClean, "伪敌机不入 enemy 组/不进敌机注册表/无碰撞体（纯视觉幽灵）");
            // 玩家子弹穿过伪敌机：伪敌机不是 Area2D，无 overlap 结算（结构上保证）
            manager.EndActive(); // 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(manager.SpawnedFakes().Count == 0, "事件结束伪敌机统一清除");
            Check(manager.CooldownLeft() > 0.0f, "事件结束进入 MinInterval 冷却");

            // 2b. mental_confusion：输入反转 + 变色覆盖层
            manager.SetCooldownLeft(0.0f);
            Check(manager.ForceTrigger("mental_confusion"), "强制触发 mental_confusion");
            Check(player.FogInvertActive(), "精神错乱：玩家输入反转标记生效");
            Check(manager.ActiveRemaining() > 0.0f, "事件有明确剩余时长（Duration 驱动）");
            manager.EndActive(); // 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!player.FogInvertActive(), "精神错乱结束：输入反转复位");

            // 2c. bullet_malfunction：子弹偏移/射速异常参数注入 + 出膛弹轨迹偏移
            manager.SetCooldownLeft(0.0f);
            Check(manager.ForceTrigger("bullet_malfunction"), "强制触发 bullet_malfunction");
            Check(Mathf.IsEqualApprox(player.FogBulletJitter(), 20.0f), "子弹错误：角度偏移档位 20°（balance.json）");
            Check(Mathf.IsEqualApprox(player.FogMisfireChance(), 0.15f), "子弹错误：失误弹概率 0.15");
            player.Position = new Vector2(960.0f, 800.0f);
            var deviated = 0;
            var misfired = 0;
            var shotDir = Vector2.Up;
            for (int i = 0; i < 40; i++)
            {
                player.Fire(shotDir);
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var b in PlayerBullets())
            {
                if (Mathf.Abs(Mathf.AngleDifference(b.Direction.Angle(), shotDir.Angle())) > 0.1f)
                {
                    deviated++;
                }

                if (b.Speed < 0.8f * 1800.0f)
                {
                    misfired++;
                }
            }

            Check(deviated >= 1, "子弹错误：40 发出膛弹至少 1 发轨迹偏移（20° 抖动生效）");
            Check(misfired >= 1, "子弹错误：40 发出膛弹至少 1 发失误慢速弹");
            manager.EndActive(); // 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(
                player.FogBulletJitter() == 0.0f && player.FogMisfireChance() == 0.0f,
                "子弹错误结束：偏移/失误参数复位");

            // 2d. direction_shift：短间隔随机方向脉冲（开始即脉冲 + 周期刷新）
            manager.SetCooldownLeft(0.0f);
            Check(manager.ForceTrigger("direction_shift"), "强制触发 direction_shift");
            Check(player.FogForcedHold() > 0.0f, "方向偏转：事件开始立即收到脉冲（hold > 0）");
            await WaitReal(0.9); // shift_interval=0.7s：越过一次脉冲周期
            Check(player.FogForcedHold() > 0.0f, "方向偏转：周期脉冲持续刷新 hold");
            Check(player.FogForcedDir().Length() > 0.99f, "方向偏转：强制方向为单位向量");
            manager.EndActive(); // 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(player.FogForcedHold() == 0.0f, "方向偏转结束：强制方向复位");

            // 3. Duration 到期自动清除（压缩时长，不动 balance.json）
            manager.EVENT_DURATIONS[new StringName("fake_enemies")] = 0.5f;
            manager.SetCooldownLeft(0.0f);
            manager.ForceTrigger("fake_enemies");
            Check(await WaitIdle(manager, 3.0), "Duration 到期自动结束事件");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(manager.SpawnedFakes().Count == 0, "到期后伪敌机自动清除");
            Check(manager.CooldownLeft() > 0.0f, "到期后进入冷却");

            // 4. MinInterval 冷却门控（压缩时长）
            manager.MIN_INTERVAL = 0.3f;
            manager.SetCooldownLeft(0.3f);
            manager.SetFirstDelayLeft(0.0f);
            Check(!manager.CanTrigger(), "冷却期内不可触发");
            await WaitReal(0.5);
            Check(manager.CooldownLeft() <= 0.0f && manager.CanTrigger(), "冷却结束后恢复可触发");

            // 5. 概率触发路径（try_trigger，确定性：chance=1.0）
            manager.TRIGGER_CHANCE = 1.0f;
            manager.SetCooldownLeft(0.0f);
            Check(manager.TryTrigger(), "概率触发：chance=1 时掷签必触发");
            Check(manager.ActiveId() != new StringName(), "概率触发后事件进行中");
            manager.EndActive(); // 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
            // chance=0 永不触发
            manager.TRIGGER_CHANCE = 0.0f;
            manager.SetCooldownLeft(0.0f);
            Check(!manager.TryTrigger(), "概率触发：chance=0 时不触发");
            Check(manager.ActiveId() == new StringName(), "未触发则无进行中事件");
            manager.TRIGGER_CHANCE = (float)gs.Cfg("fog_events.trigger_chance", 0.35).AsDouble(); // 还原档位

            // 6. 非活跃态（对局结束）自动清除 + 不再自动触发
            manager.SetCooldownLeft(0.0f);
            manager.ForceTrigger("mental_confusion");
            Check(player.FogInvertActive(), "触发后反转生效");
            manager.SetRunActive(false);
            Check(manager.ActiveId() == new StringName(), "run_active=false 立即结束进行中事件");
            Check(!player.FogInvertActive(), "非活跃结束事件：玩家效果复位");
            Check(!manager.CanTrigger(), "非活跃态不可触发");

            // 7. 返航清除：进行中事件被 main 清除
            manager.SetRunActive(true);
            // 2026-08-06 审计（Q12 同族遗漏）：重新激活对局时 fog 冷却必须清零——上局事件
            // 结束残留的 _fog_cooldown_left 会额外推迟新局首个迷雾事件（最晚 12s）
            manager.SetCooldownLeft(5.0f);
            manager.SetRunActive(false);
            manager.SetRunActive(true);
            Check(manager.CooldownLeft() == 0.0f, "重新激活对局时 fog 冷却清零");
            manager.SetFirstDelayLeft(0.0f);
            manager.SetCooldownLeft(0.0f);
            manager.ForceTrigger("bullet_malfunction");
            Check(player.FogBulletJitter() > 0.0f, "返航前子弹错误参数生效");
            GetNode<Main>("Main").StartHomecoming();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(manager.ActiveId() == new StringName(), "返航清除进行中的迷雾事件");
            Check(player.FogBulletJitter() == 0.0f, "返航后玩家效果复位");
            await WaitReal(1.4); // 越过过场输入宽限
            GetNode<Main>("Main").SkipReturn();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetNode<Main>("Main").BaseUi().Resume();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 等待轨道打击命中解冻（struck → 恢复对局）——后续 §8-10 断言依赖 _process 正常运行
            for (int i = 0; i < 40; i++)
            {
                if (!GetTree().Paused)
                {
                    break;
                }

                await WaitReal(0.1);
            }

            Check(!GetTree().Paused, "收尾：轨道打击后对局已解冻（_process 正常运行）");

            Check(Mathf.IsEqualApprox(Engine.TimeScale, 1.0f), "收尾：time_scale = 1.0");
            await WaitReal(0.5); // 让特效 tween 播完，避免退出时对象泄漏

            // 8. 事件类健壮性（GameEvent 生命周期守卫 + context 防御路径，2026-08-05 审计）
            var ge = new FakeEnemiesEvent();
            Check(ge.EventId() == new StringName("fake_enemies"), "事件类：event_id 正确");
            Check(!ge.IsActive, "事件类：初始未活跃");
            ge.Start(new Godot.Collections.Dictionary(), 0.5f); // 空 context（缺 fake_container 键）：降级空转不崩
            Check(ge.IsActive, "事件类：start 后活跃");
            Check(ge.SpawnedFakes().Count == 0, "事件类：缺容器时伪敌机零生成（防御路径）");
            var c1 = new Node2D();
            ge.Start(new Godot.Collections.Dictionary { ["fake_container"] = c1 }, 0.5f); // 重复 start：先清理旧状态再重启（不叠加）
            Check(
                ge.SpawnedFakes().Count == gs.Cfg("fog_events.fake_enemies.count", 5).AsInt32(),
                "事件类：重复 start 自愈后正常生成（无叠加）");
            var beforeTick = ge.SpawnedFakes().Count;
            ge.Tick(0.1f);
            Check(ge.SpawnedFakes().Count == beforeTick, "事件类：tick 不重复生成（生命周期由 _on_* 钩子驱动）");
            ge.End();
            ge.End();
            Check(!ge.IsActive, "事件类：end 幂等");
            ge.Tick(0.1f);
            Check(!ge.IsActive, "事件类：未活跃 tick 不派发");
            ge.Start(new Godot.Collections.Dictionary { ["fake_container"] = c1 }, -1.0f);
            Check(ge.Duration == 0.0f, "事件类：负 duration 钳制为 0");
            // context 浅拷贝隔离：编排器改原字典不影响已注入的事件
            var ctxIso = new Godot.Collections.Dictionary { ["fake_container"] = c1 };
            ge.Start(ctxIso, 1.0f);
            ctxIso["fake_container"] = new Node2D();
            Check(ge.FakeContainer() == c1, "事件类：context 浅拷贝隔离编排器后续修改");
            ge.End();
            c1.QueueFree();

            // 9. 编排器健壮性（空注册表/非 Callable 条目防御，2026-08-05 审计）
            var savedFactories = manager.EVENT_FACTORIES;
            manager.EVENT_FACTORIES = new Godot.Collections.Dictionary();
            manager.SetCooldownLeft(0.0f);
            manager.SetFirstDelayLeft(0.0f);
            manager.TRIGGER_CHANCE = 1.0f;
            Check(!manager.TryTrigger(), "编排器：空注册表 try_trigger 安全返回 false（不越界不触发）");
            Check(manager.ActiveId() == new StringName(), "编排器：空注册表不触发任何事件");
            Check(!manager.ForceTrigger("fake_enemies"), "编排器：空注册表 force_trigger 拒绝未注册 id");
            manager.EVENT_FACTORIES = savedFactories;
            manager.TRIGGER_CHANCE = 1.0f;
            Check(manager.TryTrigger(), "编排器：注册表恢复后可正常触发（chance=1）");
            manager.TRIGGER_CHANCE = 0.0f;
            manager.EndActive();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 10. 事件宽容性（简单/复杂事件同一接口，2026-08-05 调研后设计）
            // 10a. 复杂事件：内部目标达成可主动 request_end 提前结束（不等 duration）
            // M4：工厂注册用方法引用 Callable（本类私有方法，避免 C# lambda 跨语言目标丢失）
            manager.EVENT_FACTORIES[new StringName("_self_end_test")] = Callable.From(MakeSelfEndEvent);
            manager.SetCooldownLeft(0.0f);
            manager.SetFirstDelayLeft(0.0f);
            Check(manager.ForceTrigger("_self_end_test"), "宽容性：复杂事件（request_end）注册并可触发");
            var selfEndEvent = manager.ActiveEvent().AsGodotObject() as SelfEndTestEvent;
            Check(selfEndEvent != null && selfEndEvent.IsActive, "宽容性：复杂事件进行中");
            for (int i = 0; i < 3; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            Check(manager.ActiveId() == new StringName(), "宽容性：复杂事件 2 tick 后主动 request_end 提前结束");
            Check(manager.ActiveEvent().VariantType == Variant.Type.Nil, "宽容性：结束后事件对象已清理");
            manager.EVENT_FACTORIES.Remove(new StringName("_self_end_test"));

            // 10b. 极简事件：只实现 event_id 也能走通 start→duration→end 全生命周期
            manager.EVENT_FACTORIES[new StringName("_minimal_test")] = Callable.From(MakeMinimalEvent);
            manager.EVENT_DURATIONS[new StringName("_minimal_test")] = 0.3f;
            manager.SetCooldownLeft(0.0f);
            Check(manager.ForceTrigger("_minimal_test"), "宽容性：极简事件（仅 event_id）注册并可触发");
            Check(manager.ActiveId() == new StringName("_minimal_test"), "宽容性：极简事件进行中");
            Check(await WaitIdle(manager, 3.0), "宽容性：极简事件按 duration 自然结束");
            manager.EVENT_FACTORIES.Remove(new StringName("_minimal_test"));
            manager.EVENT_DURATIONS.Remove(new StringName("_minimal_test"));

            // 10c. 宽容性辅助：get_ctx 缺键回默认 / request_end 缺回调降级（不崩、按 duration 继续）
            var ctxEv = new MinimalTestEvent();
            ctxEv.Start(new Godot.Collections.Dictionary(), 1.0f);
            Check(ctxEv.GetCtx(new StringName("missing"), 42).AsInt32() == 42, "宽容性：get_ctx 缺键返回 default");
            Check(ctxEv.GetCtx(new StringName("missing")).VariantType == Variant.Type.Nil, "宽容性：get_ctx 无 default 返回 null");
            ctxEv.RequestEnd(); // 无 request_end 回调：push_warning 降级，不崩
            Check(ctxEv.IsActive, "宽容性：request_end 缺回调时事件继续（按 duration 结束）");
            ctxEv.End();
            Check(manager.ActiveId() == new StringName(), "宽容性：测试事件清理后编排器空闲");

            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"FOG EVENT TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"FOG EVENT TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
