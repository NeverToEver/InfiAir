using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 统一事件管理器集成测试（docs/EVENT_MANAGER.md）：
/// 场景1 注册表：遭遇事件经 main._ready 注册进统一注册表，6 事件齐全；
///    main.event()/main.formation() 与管理器注册表返回同一实例。
/// 场景2 遭遇强制触发 + 统一信号：force_trigger 启动编队事件 → event_started 广播、
///    状态推进；遭遇组单并发（编队进行中 force_trigger 精英被拒）。
/// 场景3 遭遇终止：end_active(GROUP_ENCOUNTER) → abort + event_ended 广播。
/// 场景4 spawner 门控：spawner.set_process(false) 后遭遇自动触发被禁用
///    （计时不推进；手动 start 不受影响）。
/// M7c：自 test/event_manager_test.gd 迁移（B09），全程 gs.Events（GameEventManager）typed 调用。
/// </summary>
public partial class EventManagerTest : Node
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
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget（约定 §Async）
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        GameState? gs = null;
        GameEventManager? manager = null;
        try
        {
            gs = GetNode<GameState>("/root/GameState");
            gs.DeleteSave();
            gs.SetDifficulty("medium");
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();
            AddChild(mainScene.Instantiate());
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);
            player.SetInvincible(999.0f);
            player.Position = new Vector2(960.0f, 800.0f);
            gs.SetMilestoneOverride(999999);  // 防止得分跨越里程碑弹 Buff 三选一暂停树
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);  // 场景 1-3 手动驱动，保证确定性
            manager = gs.Events;
            var main = GetNode<Main>("Main");

            // ================= 场景 1：注册表与实例同一性 =================
            var ids = manager.EventIds();
            Check(ids.Contains(new StringName("elite_turret")), "场景1：精英炮塔事件已注册进统一注册表");
            Check(ids.Contains(new StringName("formation_strike")), "场景1：轰炸编队事件已注册进统一注册表");
            Check(ids.Contains(new StringName("fake_enemies")) && ids.Contains(new StringName("mental_confusion")), "场景1：迷雾事件保留在注册表");
            Check(ids.Count == 6, "场景1：统一注册表共 6 事件（迷雾 4 + 遭遇 2）");
            Check(manager.Event(new StringName("elite_turret")).AsGodotObject()?.GetInstanceId() == main.Event()?.GetInstanceId(), "场景1：main.event() 与注册表同实例");
            Check(manager.Event(new StringName("formation_strike")).AsGodotObject()?.GetInstanceId() == main.Formation()?.GetInstanceId(), "场景1：main.formation() 与注册表同实例");

            // ================= 场景 2：遭遇强制触发 + 统一信号 + 组内单并发 =================
            var startedIds = new System.Collections.Generic.List<StringName>();
            var endedIds = new System.Collections.Generic.List<StringName>();
            manager.EventStarted += (id, d) => startedIds.Add(id);
            manager.EventEnded += (id) => endedIds.Add(id);
            var formation = (FormationStrikeEvent)main.Formation()!;
            formation.MinScore = 0;  // 压缩门槛（不动 balance.json）
            var ok = manager.ForceTrigger("formation_strike");
            Check(ok, "场景2：force_trigger 启动编队事件");
            Check(manager.ActiveId(manager.GROUP_ENCOUNTER) == new StringName("formation_strike"), "场景2：遭遇组 active_id 正确");
            Check(startedIds.Contains(new StringName("formation_strike")), "场景2：event_started 已广播编队事件");
            Check(formation.GetState() != FormationStrikeEvent.State.IDLE, "场景2：编队 FSM 已推进");
            Check(!manager.ForceTrigger("elite_turret"), "场景2：遭遇组单并发——编队进行中拒触发精英");
            // 等待 FSM 自行结束（FORMATION_ENTER 后转 TURN 需 ~0.2s，投弹/离场 ~数秒）
            var ended = false;
            for (var i = 0; i < 120; i++)  // 最多 ~12s 真实时间
            {
                await Coroutine.WaitSeconds(this, 0.1);
                if (formation.GetState() == FormationStrikeEvent.State.IDLE)
                {
                    ended = true;
                    break;
                }
            }
            Check(ended, "场景2：编队 FSM 自然结束回 IDLE");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(endedIds.Contains(new StringName("formation_strike")), "场景2：FSM 结束 → event_ended 已广播");
            Check(manager.ActiveId(manager.GROUP_ENCOUNTER) == new StringName(""), "场景2：遭遇组 active_id 复位");
            // 清理编队遗留炸弹
            foreach (var child in main.GetChildren())
            {
                if (child is FormationBomb)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 场景 3：end_active 终止 =================
            manager.ForceTrigger("formation_strike");
            Check(formation.GetState() != FormationStrikeEvent.State.IDLE, "场景3：事件再次启动");
            manager.EndActive(manager.GROUP_ENCOUNTER);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(formation.GetState() == FormationStrikeEvent.State.IDLE, "场景3：end_active → abort 回 IDLE");
            Check(endedIds.Contains(new StringName("formation_strike")), "场景3：终止广播 event_ended");
            Check(!spawner.WavesPaused(), "场景3：终止后普通波次恢复");
            foreach (var child in main.GetChildren())
            {
                if (child is FormationBomb)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 场景 4：spawner 处理门控 =================
            // spawner.set_process(false) 下遭遇自动触发禁用；恢复处理后才生效（镜像原 spawner 语义）
            gs.Score = 10000;  // 越过编队 min_score
            formation.SetCooldownLeft(0.0f);
            manager.ENCOUNTER_CONFIG[new StringName("formation_strike")].AsGodotDictionary()["chance"] = 1.0;  // 固定掷签（测试确定性）
            spawner.SetProcess(false);
            manager.SetEncounterTimerRemaining("formation_strike", 0.05f);
            await Coroutine.WaitSeconds(this, 0.2);
            Check(formation.GetState() == FormationStrikeEvent.State.IDLE, "场景4：spawner 停处理时遭遇自动触发被禁用");
            spawner.SetProcess(true);
            manager.SetEncounterTimerRemaining("formation_strike", 0.05f);
            var autoTriggered = false;
            for (var i = 0; i < 30; i++)  // 最多 ~3s
            {
                await Coroutine.WaitSeconds(this, 0.1);
                if (formation.GetState() != FormationStrikeEvent.State.IDLE)
                {
                    autoTriggered = true;
                    break;
                }
            }
            Check(autoTriggered, "场景4：spawner 恢复处理后遭遇自动触发生效");
            manager.EndActive(manager.GROUP_ENCOUNTER);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            spawner.SetProcess(false);
            foreach (var child in main.GetChildren())
            {
                if (child is FormationBomb)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 场景 5：fog 组经统一管理器 + 跨组并发 =================
            // fog 组生命周期由统一管理器接管；fog 信号经门面重发（player 消费面不变）
            gs.FogEvents.SetRunActive(true);
            var fogSeen = new System.Collections.Generic.List<StringName>();
            gs.FogEvents.FogEventStarted += (id, d) => fogSeen.Add(id);
            ok = manager.ForceTrigger("fake_enemies");
            Check(ok, "场景5：统一管理器可启动迷雾事件");
            Check(manager.ActiveId(manager.GROUP_FOG) == new StringName("fake_enemies"), "场景5：fog 组 active_id 正确");
            Check(fogSeen.Contains(new StringName("fake_enemies")), "场景5：fog_event_started 经门面重发");
            Check(gs.FogEvents.SpawnedFakes().Count > 0, "场景5：门面 spawned_fakes 委托生效");
            // 跨组并发：fog 进行中遭遇仍可并行启动（保持现状行为）
            ok = manager.ForceTrigger("formation_strike");
            Check(ok, "场景5：fog 进行中遭遇组仍可并行启动");
            Check(manager.ActiveId(manager.GROUP_ENCOUNTER) == new StringName("formation_strike"), "场景5：遭遇组 active");
            Check(manager.ActiveId(manager.GROUP_FOG) == new StringName("fake_enemies"), "场景5：fog 组保持 active（并行）");
            // end_all 双组复位
            manager.EndAll();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(manager.ActiveId(manager.GROUP_FOG) == new StringName(""), "场景5：end_all fog 组复位");
            Check(manager.ActiveId(manager.GROUP_ENCOUNTER) == new StringName(""), "场景5：end_all 遭遇组复位");
            Check(formation.GetState() == FormationStrikeEvent.State.IDLE, "场景5：end_all 遭遇回 IDLE");
            foreach (var child in main.GetChildren())
            {
                if (child is FormationBomb)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 场景 6：Q07/Q10/Q12/Q13（2026-08-05 全量修复批次） =================
            // Q13：end_active 打断后 event_ended 恒只发一次（原实现 abort 处 + 轮询双发）。
            // C# lambda 直接捕获引用类型变量（无 GDScript 值拷贝问题）
            var endedCount = 0;
            manager.EventEnded += (id) =>
            {
                if (id == new StringName("formation_strike"))
                {
                    endedCount++;
                }
            };
            // Q07：enabled=false 时 _process 自动触发路径惰性（掷签前短路，原实现照常触发）
            manager.FOG_ENABLED = false;
            manager.FOG_TRIGGER_CHANCE = 1.0f;  // 固定掷签（对照组用）
            manager.SetCooldownLeft(0.0f);
            manager.SetFirstDelayLeft(0.0f);
            manager.SetCheckTimerLeft(0.0f);
            manager.SetRunActive(false);  // 先退出再激活 → 触发 Q10/Q12 重置路径
            manager.SetRunActive(true);
            Check(
                Mathf.IsEqualApprox(manager.FirstDelayLeft(), manager.FOG_FIRST_DELAY),
                $"Q12：激活对局重置 fog first_delay = {manager.FOG_FIRST_DELAY:0}（实测 {manager.FirstDelayLeft():0.0}，原实现每进程一次、第二局开局即触发）"
            );
            Check(
                manager.EncounterTimerRemaining("formation_strike") >= 39.0f,
                $"Q10：激活对局重置遭遇计时回 interval 40（实测 {manager.EncounterTimerRemaining("formation_strike"):0.0}，原实现继承上局 ≤0 即触发）"
            );
            await Coroutine.WaitSeconds(this, 0.3);  // 覆盖 ≥1 个检查周期（check_timer 已压零）
            Check(manager.ActiveId(manager.GROUP_FOG) == new StringName(""), "Q07：enabled=false 时自动触发不启动");
            // Q07 对照组：enabled=true 且条件就绪 → 自动触发正常启动
            manager.FOG_ENABLED = true;
            manager.SetCooldownLeft(0.0f);
            manager.SetFirstDelayLeft(0.0f);
            manager.SetCheckTimerLeft(0.0f);
            var fogAuto = false;
            for (var i = 0; i < 10; i++)
            {
                await Coroutine.WaitSeconds(this, 0.1);
                if (manager.ActiveId(manager.GROUP_FOG) != new StringName(""))
                {
                    fogAuto = true;
                    break;
                }
            }
            Check(fogAuto, "Q07：enabled=true 且条件就绪时自动触发正常启动（对照组）");
            manager.EndAll();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Q13：打断后信号恰好 1 次且 FSM 回 IDLE
            manager.ForceTrigger("formation_strike");
            manager.EndActive(manager.GROUP_ENCOUNTER);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(endedCount == 1, $"Q13：end_active 打断后 event_ended 恰好 1 次（实测 {endedCount}，原实现双发）");
            Check(formation.GetState() == FormationStrikeEvent.State.IDLE, "Q13：打断后 FSM 回 IDLE");
            foreach (var child in main.GetChildren())
            {
                if (child is FormationBomb)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"EVENT MANAGER TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"EVENT MANAGER TEST DONE, failures = {_failures}");
            // P4（2026-08-05）：还原直写配置（测试不污染内存配置表；A7 同族）
            if (manager != null)
            {
                manager.FOG_ENABLED = true;
                manager.FOG_TRIGGER_CHANCE = 0.35f;
                manager.ENCOUNTER_CONFIG[new StringName("formation_strike")].AsGodotDictionary()["chance"] = 0.30;
            }
            gs?.DeleteSave();
            TestExit.Quit(_failures);
        }
    }
}
