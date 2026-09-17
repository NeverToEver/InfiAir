using Godot;
using InfiAir.Core.Practice;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 练习局直选内容的落地驱动：补门槛分 + 逐帧请求所选 Boss / 遭遇 / 迷雾。
///
/// 练习宿主（<c>scenes/practice.tscn</c>）与练习探针共用本类——探针若另写一遍请求逻辑，
/// 覆盖到的就不是生产路径了；而这条路径的坏法是静默的：面板上选了 Boss 却永远等不到它，
/// 不崩不报错，只有完成标记能抓住。
///
/// 三件事的判定都不在本类另立一套：
/// - 起始分数补多少 → core <see cref="PracticeSetup.ScoreSeed"/>（门槛值现取生产配置）；
/// - 请求谁 → core <see cref="PracticeSetup"/>（Boss 型别 / 事件 id 由它派生）；
/// - 请求能不能发 → 生产链自己判（<c>Spawner.RequestBossForPractice</c> /
///   <c>GameEventManager.RequestForcedTrigger</c> / <c>RequestForcedFog</c>）：
///   资格、分数门槛、组内互斥、入场窗口、迷雾首延迟与冷却全部照判，本类只负责
///   「什么时候按下去」与「被拒就下一帧重试，直到受理为止（不静默丢弃）」。
/// </summary>
public sealed class PracticeDriver
{
    private readonly PracticeSetup _setup;
    private readonly Player _player;
    private readonly Spawner _spawner;
    private readonly GameEventManager _events;

    public PracticeDriver(PracticeSetup setup, Player player, Spawner spawner, GameEventManager events)
    {
        _setup = setup;
        _player = player;
        _spawner = spawner;
        _events = events;
    }

    /// <summary>所选 Boss 的请求是否已受理（受理 ≠ 已出场：生产链还要走 2s 预警与降入演出）。</summary>
    public bool BossRequested { get; private set; }

    /// <summary>所选事件（遭遇/迷雾）的请求是否已受理。</summary>
    public bool EventRequested { get; private set; }

    /// <summary>补门槛分：把所选内容的生产分数门槛补足（口径与理由见 core PracticeSetup.ScoreSeed——
    /// 是「让门槛被满足」而不是绕过）。遭遇门槛取自事件管理器注册时固化的生产配置（与触发判定读同一份），
    /// Boss 门槛取自生成器生效的 boss_score_step——本类不抄第二份数字。幂等：重复调用只补差额。</summary>
    public void SeedScore()
    {
        var encounterMin = _setup.HasEncounter && !_setup.EncounterIsFog
            ? _events.EncounterMinScore(new StringName(_setup.EncounterId))
            : 0;
        var seed = _setup.ScoreSeed(encounterMin, _spawner.BOSS_SCORE_STEP);
        var missing = seed - GameState.Instance.Score;
        if (missing > 0)
        {
            GameState.Instance.AddScore(missing);
        }
    }

    /// <summary>逐帧驱动：等玩家入场演出结束 → 请求 Boss → 请求事件。
    /// 返回 true = 所选内容全部受理完毕（调用方可以停掉自己的逐帧驱动）。</summary>
    public bool Tick()
    {
        // 入场演出窗口内请求会被生产链拒（spawner 未处理中），故等演出结束再按；
        // 被拒即下一帧重试——不把 Boss/遭遇塞进正在入场的战场，也不静默丢掉玩家的选择。
        if (_player.IsEntryPlaying() || !_spawner.IsProcessing())
        {
            return false;
        }

        if (!BossRequested && _setup.HasBoss)
        {
            BossRequested = _spawner.RequestBossForPractice(_setup.BossType);
            if (BossRequested)
            {
                GD.Print(GdFormat.Format("[practice] 已按直选请求 Boss 型别 %d（走生产出场链）", _setup.BossType));
            }
        }

        if (!EventRequested && _setup.HasEncounter)
        {
            var id = new StringName(_setup.EncounterId);
            // 迷雾与遭遇是两条启动链（迷雾组走接线/启用/首延迟/冷却，遭遇组走分数门槛/组内互斥/Boss 槽），
            // 故各走自己那条生产请求入口；两者都只替换「随机掷签」这一步。
            EventRequested = _setup.EncounterIsFog
                ? _events.RequestForcedFog(id)
                : _events.RequestForcedTrigger(id);
            if (EventRequested)
            {
                GD.Print(GdFormat.Format("[practice] 已按直选请求事件 %s（走生产触发链，门槛与门控真判）", id));
            }
            else if (_eventRejected)
            {
                // 第一次被拒可能只是事件尚未注册（注册在 Main._Ready），持续被拒才是真断线：
                // 报错一次并停手，由完成标记的缺失把这一趟判红（不无限重试掩盖）。
                GD.PushError(GdFormat.Format(
                    "[practice] 直选事件 %s 的请求入口持续拒绝（id 未注册/非本组）——练习局不会等到它", id));
                EventRequested = true;
            }
            else
            {
                _eventRejected = true;
            }
        }

        return (BossRequested || !_setup.HasBoss) && (EventRequested || !_setup.HasEncounter);
    }

    /// <summary>事件请求是否已被拒过一次（下一次仍拒即判定为真断线）。</summary>
    private bool _eventRejected;
}
