using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 无头探针宿主：门禁冒烟的测试开关与驱动全部收在这里，生产 main.tscn 与 Main 不再读任何测试开关
/// （AGENTS §5「测试/探测设施不进生产路径」）。仅由 <c>scenes/probe_host.tscn</c> 经 <c>--scene</c> 启动，
/// 以子节点嵌入 main.tscn——Main 见 CurrentScene 非自身即走「嵌入宿主」分支（不入场、不进标题屏），
/// 由本节点显式开启本局可驱动，再经生产触发链请求遭遇（资格/门槛/门控仍由生产判定，探针不得绕过）。
/// 开关、帧数与日志口径的单源在 scripts/ci/check_smoke.sh。
/// </summary>
public partial class ProbeHost : Node
{
    /// <summary>自然全周期探针的玩家无敌时长（s）：无头局玩家不操作，炮塔与炸弹会打死玩家使事件
    /// 中途打断——探针的绿不得依赖「玩家恰好活过事件时长」；死亡路径另行显式击杀。</summary>
    private const float ProbeInvincibleSeconds = 9999.0f;

    /// <summary>死亡探针在遭遇激活后等待的帧数（60 帧＝1s）：等事件推进到可清理状态再击杀，
    /// 覆盖打断的清理分支（精英炮塔升起到位后的炮塔回收），而非入场即打断。</summary>
    private const int DeathProbeDelayFrames = 240;

    private Main _main = null!;
    private Player _player = null!;
    private Spawner _spawner = null!;
    private GameEventManager _events = null!;

    private bool _settingsProbe;
    private bool _startupProbe;
    private bool _fuelProbe;
    private string _eventId = "";
    private bool _deathProbe;
    private bool _startupPrinted;
    private bool _triggerPosted;
    private bool _sawActive;
    private bool _killed;
    private int _frame;
    private int _activeFrame;
    private int _fuelStep;

    public override void _Ready()
    {
        // 死亡打断会暂停整棵树（结算页接管）；宿主须继续推进才能观测打断收尾
        ProcessMode = ProcessModeEnum.Always;
        _main = GetNode<Main>("Main");
        _player = _main.GetNode<Player>("Player");
        _spawner = GetTree().GetFirstNodeInGroup("spawner") as Spawner ?? _main.GetNode<Spawner>("Spawner");
        _events = GameState.Instance.Events;

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--settings-probe")
            {
                _settingsProbe = true;
            }
            else if (arg == "--startup-time")
            {
                _startupProbe = true;
            }
            else if (arg == "--fuel-probe")
            {
                _fuelProbe = true;
            }
            else if (arg.StartsWith("--event-probe-death=", System.StringComparison.Ordinal))
            {
                _eventId = arg["--event-probe-death=".Length..];
                _deathProbe = true;
            }
            else if (arg.StartsWith("--event-probe=", System.StringComparison.Ordinal))
            {
                _eventId = arg["--event-probe=".Length..];
            }
        }

        if (_eventId.Length > 0)
        {
            // Main 嵌入宿主时按 current_scene 判定关闭了本局可驱动（防随机事件破坏宿主场景的确定性），
            // 探针即宿主，显式开启——遭遇触发链的资格/门槛/门控仍全部走生产判定。
            GameState.Instance.SetRunActive(true);
            _events.SetRunActive(true);
        }
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_startupProbe && !_startupPrinted)
        {
            _startupPrinted = true;
            GD.Print(GdFormat.Format("[startup] boot → first frame: %d ms",
                (long)Time.GetTicksMsec() - GameState.Instance.BootTicksMsec));
        }

        if (_settingsProbe)
        {
            _settingsProbe = false;
            RunSettingsProbe();
            return;
        }

        if (_fuelProbe)
        {
            TickFuelProbe();
            return;
        }

        if (_eventId.Length > 0)
        {
            TickEventProbe();
        }
    }

    /// <summary>燃料量槽探针：把液位从满油扫到见底，逼 <c>FuelTank._Draw</c> 在每个液位各画一次
    /// （含每档掉液触发的晃动叠加＝最大波幅）。无头局玩家不操作不掉油，该绘制路径平时根本走不到。
    /// 判定靠冒烟错误正则捕获「Invalid polygon data」——自交/退化多边形整块不画且不崩，
    /// 只判「不崩」抓不到（低油量燃料槽整块消失即此类）。</summary>
    private void TickFuelProbe()
    {
        // 21 档 × 8 帧（> HUD 0.1s 轮询 + 液罐追赶/晃动），扫满一整段液位行程
        if (_frame % 8 != 0)
        {
            return;
        }

        _player.SetFuel(_player.FuelMax * (1.0f - _fuelStep / 20.0f));
        _fuelStep++;
        if (_fuelStep > 20)
        {
            GD.Print("[fuel-probe] 液位满扫完成");
            _fuelProbe = false;
        }
    }

    /// <summary>设置页探针：开页并逐页切过——五页内容都在 ShowSettings 之后才构建，
    /// 平时的 300 帧基线碰不到它们（玩家点开即崩的写法在这里暴露）。
    /// 找不到设置节点就不打完成标记——空转同样「零错误退出」，缺标记即红。</summary>
    private void RunSettingsProbe()
    {
        var settings = GetTree().GetFirstNodeInGroup("settings_ui") as SettingsUi;
        if (settings == null)
        {
            GD.PushError("[settings-probe] 未找到设置页节点，无法覆盖五页构建");
            return;
        }

        settings.ShowSettings(null);
        foreach (var page in new[] { "gameplay", "display", "audio", "about", "controls" })
        {
            settings.ShowPage(new StringName(page));
        }

        GD.Print("[settings-probe] 五页切换完成");
    }

    /// <summary>遭遇探针驱动：等可驱动 → 补分数 + 请求掷签必中（仍走生产触发链）→ 观测收场。
    /// 死亡探针在激活后延迟若干帧显式击杀玩家，走管理器 EndActive 与事件 Abort 的死亡路径；
    /// 收场以「事件回 IDLE」为据，死亡探针另断波次与 Boss 互斥已归还。</summary>
    private void TickEventProbe()
    {
        var key = new StringName(_eventId);
        if (!_sawActive)
        {
            if (_player.IsEntryPlaying() || !_spawner.IsProcessing())
            {
                return;
            }

            if (!_triggerPosted)
            {
                _player.SetInvincible(ProbeInvincibleSeconds); // 自然探针与玩家存活解耦（死亡探针另行显式击杀）
                GameState.Instance.AddScore(System.Math.Max(_events.EncounterMinScore(key), 1));
                _triggerPosted = true;
                if (!_events.RequestForcedTrigger(key))
                {
                    GD.PushError($"[event-probe] 请求启动失败：{_eventId} 未注册");
                    _eventId = "";
                }

                return;
            }

            if (_events.EncounterInstance(key)?.IsActive() == true)
            {
                _sawActive = true;
                _activeFrame = _frame;
            }

            return;
        }

        if (_deathProbe && !_killed && _frame - _activeFrame >= DeathProbeDelayFrames)
        {
            _killed = true;
            _player.Die(); // 显式击杀（绕过无敌）——死亡路径 = 管理器 EndActive + 事件 Abort
            GameState.Instance.SetTreePaused(false); // 结算页会冻住事件撤离，探针需观测打断完成
            return;
        }

        var ev = _events.EncounterInstance(key);
        if (ev == null)
        {
            GD.PushError($"[event-probe] {_eventId} 实例已失效，无法判定收场");
            _eventId = "";
            return;
        }

        if (ev.IsActive())
        {
            return;
        }

        if (_deathProbe && (_spawner.BossFrozen() || _spawner.WavesPaused()))
        {
            GD.PushError($"[event-probe] {_eventId} 死亡打断后波次/Boss 互斥未归还");
            _eventId = "";
            return;
        }

        GD.Print(GdFormat.Format(
            _deathProbe ? "[event-probe] %s 死亡打断完成" : "[event-probe] %s 全周期完成", _eventId));
        _eventId = "";
    }
}
