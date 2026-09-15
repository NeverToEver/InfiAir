using Godot;
using InfiAir.Core.Text;

// 探针宿主整体条件编译：仅编辑器/调试构建（Debug 定义 TOOLS、ExportDebug 定义 DEBUG）编入，
// 发布导出（ExportRelease 两者皆无）整类不进 InfiAir.dll——测试设施不随发布包分发。
// scene 侧 scenes/probe_host.tscn 已在 export_presets.cfg 排除，二者一致。
#if DEBUG || TOOLS
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

    /// <summary>迷雾探针强制指定的事件 id：fake_enemies 是无伤害/无碰撞的纯视觉干扰，
    /// 不会改动玩家血量/输入/子弹参数，避免其它三类迷雾把全周期判定搅成「玩家状态被打断」。</summary>
    private static readonly StringName FogProbeEventId = new("fake_enemies");

    /// <summary>全周期判定的帧级容差（s）：EventEnded 由管理器 Timer 在 autoload _Process 内发出，
    /// 早于探针本帧自增的帧计数，逐帧换算会出现不足一帧的差；容差取 0.1s（6 帧）兜住时序差，
    /// 同时远小于任何真实截断（截断至少丢掉整段事件时长）。</summary>
    private const double FogCycleToleranceSeconds = 0.1;

    /// <summary>返航宽限探针蓄力触发返航的帧预算：effects.home_charge_time 1.5s = 90 帧，留 3 倍余量。</summary>
    private const int ReturnProbeChargeFrames = 300;

    /// <summary>返航宽限探针在过场开播后推进的帧数（60 帧＝1 模拟秒）。取 90 帧（1.5 模拟秒 &gt; 默认宽限
    /// 1.2s）作「模拟时间足够越界、真实时间尚未越界」的判别窗口——见 TickReturnProbe 的说明。</summary>
    private const int ReturnProbeGraceWindowFrames = 90;

    /// <summary>返航宽限探针判定用的墙钟余量（ms）：越过宽限后多等一点，抵消边界抖动。</summary>
    private const ulong ReturnProbeGraceMarginMs = 250;

    /// <summary>返航蓄力动作名（与 project.godot 的 homecoming 映射一致，生产判定读同一动作）。</summary>
    private static readonly StringName ActHomecoming = new("homecoming");

    private Main _main = null!;
    private Player _player = null!;
    private Spawner _spawner = null!;
    private GameEventManager _events = null!;

    private bool _settingsProbe;
    private bool _startupProbe;
    private bool _fuelProbe;
    private bool _shotProbe;
    private bool _feelProbe;
    private bool _longProbe;
    private bool _fogProbe;
    private bool _returnProbe;
    private string _shotDir = "";
    private string _eventId = "";
    private bool _deathProbe;
    private bool _startupPrinted;
    private bool _triggerPosted;
    private bool _sawActive;
    private bool _killed;
    private bool _fogActive;
    private bool _fogSubscribed;
    private int _fogStartFrame;
    private double _fogDuration;
    private int _returnStage;
    private int _returnChargeFrames;
    private int _returnStartFrame;
    private ulong _returnWallStart;
    private float _returnGrace;
    private int _frame;
    private int _activeFrame;
    private int _fuelStep;
    private int _shotStep;
    private int _feelStep;
    private bool _feelSawHitStop;
    private bool _feelSawTrauma;

    /// <summary>截图序列：帧号 → 先切到哪一页（空＝不切）→ 捕获名（空＝只切不捕）。
    /// 固定帧捕获——序列本身即「要覆盖哪些视觉面」的清单。切页与捕获隔开若干帧，
    /// 等新页构建并渲染完成（捕获取的是已渲染帧，同帧切页会捕到上一帧）。
    /// 切页与捕获间留 ~0.5s，避开设置页交叉淡入/入场动画（捕在转场中段画面未成形）。</summary>
    private static readonly (int Frame, string Page, string Shot)[] ShotPlan =
    {
        (90, "", "hud"),
        (100, "gameplay", ""),
        (130, "", "settings-gameplay"),
        (140, "display", ""),
        (170, "", "settings-display"),
        (180, "audio", ""),
        (210, "", "settings-audio"),
        (220, "about", ""),
        (250, "", "settings-about"),
        (260, "controls", ""),
        (290, "", "settings-controls"),
    };

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
            else if (arg == "--shot-probe")
            {
                _shotProbe = true;
            }
            else if (arg == "--feel-probe")
            {
                _feelProbe = true;
            }
            else if (arg == "--long-probe")
            {
                _longProbe = true;
            }
            else if (arg == "--fog-probe")
            {
                _fogProbe = true;
            }
            else if (arg == "--return-probe")
            {
                _returnProbe = true;
            }
            else if (arg.StartsWith("--shot-dir=", System.StringComparison.Ordinal))
            {
                _shotDir = arg["--shot-dir=".Length..];
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

        if (_eventId.Length > 0 || _feelProbe || _longProbe || _fogProbe || _returnProbe)
        {
            // Main 嵌入宿主时按 current_scene 判定关闭了本局可驱动（防随机事件破坏宿主场景的确定性），
            // 探针即宿主，显式开启——遭遇触发链的资格/门槛/门控仍全部走生产判定。
            GameState.Instance.SetRunActive(true);
            _events.SetRunActive(true);
            // 遭遇/手感/长局探针一律关闭迷雾随机事件：首延迟（25s）过后被精英炮塔等长趟越过，
            // 此后每 3s 有 35% 概率触发迷雾（方向偏转会把无头局的玩家推入弹幕致死），
            // 探针红绿将取决于随机数——违反 §5「随机要么避免、要么走可注入取值源」。
            // 迷雾由 --fog-probe 专趟覆盖（该趟保持开启，仍走生产资格/门槛）。
            _events.FOG_ENABLED = _fogProbe;
            if (_fogProbe)
            {
                // 统一信号监听（完整周期判定：start→end）；退订在 _ExitTree 配对
                _events.EventStarted += OnFogEventStarted;
                _events.EventEnded += OnFogEventEnded;
                _fogSubscribed = true;
                if (!_events.RequestForcedFog(FogProbeEventId))
                {
                    GD.PushError("[fog-probe] 强制迷雾事件请求失败：id 未注册或非 fog 组");
                    _fogProbe = false;
                }
            }
        }
    }

    public override void _ExitTree()
    {
        if (_fogSubscribed)
        {
            _fogSubscribed = false;
            _events.EventStarted -= OnFogEventStarted;
            _events.EventEnded -= OnFogEventEnded;
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

        if (_feelProbe)
        {
            TickFeelProbe();
            return;
        }

        if (_longProbe)
        {
            TickLongProbe();
            return;
        }

        if (_fogProbe)
        {
            TickFogProbe();
            return;
        }

        if (_returnProbe)
        {
            TickReturnProbe();
            return;
        }

        if (_shotProbe)
        {
            TickShotProbe();
            return;
        }

        if (_eventId.Length > 0)
        {
            TickEventProbe();
        }
    }

    /// <summary>签名网格（粗）：只判「有没有画面内容」与「各页是否不同」，
    /// 不做像素级比对——占位内容（帧率/垂直同步读出、背景星空动画）本就随环境变化，
    /// 跨渲染器比像素必然误报。细粒度退化归 core 单测与断言探针。</summary>
    private const int SigW = 16;
    private const int SigH = 9;

    /// <summary>非空白判定：签名通道极差下限。真界面明暗跨度大（面板/文字/高光），
    /// 黑屏或渲染全坏时极差趋 0。</summary>
    private const int MinSpread = 24;

    /// <summary>页面可区分判定：五张设置页两两签名最大差的下限。页面切换失效
    /// （始终同一页/空白）时各页趋同。</summary>
    private const int MinPageDiff = 12;

    private readonly System.Collections.Generic.Dictionary<string, int[]> _shotSigs = new();
    private readonly System.Collections.Generic.List<string> _shots = new();

    /// <summary>截图驱动：按计划帧切页/捕获；序列结束后做两条廉价自检（非空白、页面互异），
    /// 全过才打完成标记（缺标记即门禁红）。</summary>
    private void TickShotProbe()
    {
        foreach (var step in ShotPlan)
        {
            if (step.Frame != _frame)
            {
                continue;
            }

            if (step.Page.Length > 0)
            {
                var settings = GetTree().GetFirstNodeInGroup("settings_ui") as SettingsUi;
                settings?.ShowSettings(null);
                settings?.ShowPage(new StringName(step.Page));
            }

            if (step.Shot.Length > 0)
            {
                CaptureShot(step.Shot);
            }

            return;
        }

        if (_frame > ShotPlan[^1].Frame && _shotProbe)
        {
            _shotProbe = false;
            VerifyShots();
        }
    }

    /// <summary>截图自检：计划张数全部入账 + 每张非空白 + 五张设置页两两可区分。任一不过就不打完成标记，
    /// 由门禁按「缺标记」判红（与其它探针同一口径）。
    ///
    /// 张数必须单独判：`_shots` 只收成功写出的图（取像/写出失败会 PushError 后跳过），
    /// 少了图时以下两个循环只是少比几对、`ok` 仍为 true——全靠已入账的图自证，零张时循环空转、
    /// 照样打完成标记（假绿）。期望张数从 ShotPlan 派生，不另写一份。</summary>
    private void VerifyShots()
    {
        var ok = true;
        var expected = 0;
        foreach (var step in ShotPlan)
        {
            if (step.Shot.Length > 0)
            {
                expected++;
            }
        }

        if (_shots.Count < expected)
        {
            GD.PushError(GdFormat.Format(
                "[shot-probe] 实际捕获 %d 张，少于计划 %d 张——取像或写出失败（日志里应有对应的失败行）",
                _shots.Count, expected));
            ok = false;
        }

        foreach (var name in _shots)
        {
            var sig = _shotSigs[name];
            var min = int.MaxValue;
            var max = int.MinValue;
            foreach (var v in sig)
            {
                if (v < min) { min = v; }
                if (v > max) { max = v; }
            }

            if (max - min < MinSpread)
            {
                GD.PushError(GdFormat.Format("[shot-probe] %s 画面近乎空白（极差 %d）", name, max - min));
                ok = false;
            }
        }

        for (var i = 0; i < _shots.Count; i++)
        {
            for (var j = i + 1; j < _shots.Count; j++)
            {
                var a = _shotSigs[_shots[i]];
                var b = _shotSigs[_shots[j]];
                var worst = 0;
                for (var k = 0; k < a.Length; k++)
                {
                    var d = System.Math.Abs(a[k] - b[k]);
                    if (d > worst) { worst = d; }
                }

                if (worst < MinPageDiff)
                {
                    GD.PushError(GdFormat.Format(
                        "[shot-probe] %s 与 %s 画面几乎相同（最大差 %d）——页面切换可能失效",
                        _shots[i], _shots[j], worst));
                    ok = false;
                }
            }
        }

        if (ok)
        {
            GD.Print(GdFormat.Format("[shot-probe] 截图序列完成（%d 张）", _shots.Count));
        }
    }

    /// <summary>捕获当前视口：存 PNG（生成截图用） + 记下粗签名（供上面的自检）。
    /// 目录由 <c>--shot-dir=</c> 指定（门禁侧传绝对路径）。</summary>
    private void CaptureShot(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        if (image == null)
        {
            GD.PushError(GdFormat.Format("[shot-probe] 视口取像失败：%s", name));
            return;
        }

        var png = _shotDir.Length > 0
            ? GdFormat.Format("%s/%s.png", _shotDir, name)
            : GdFormat.Format("user://%s.png", name);
        var err = image.SavePng(png);
        if (err != Error.Ok)
        {
            GD.PushError(GdFormat.Format("[shot-probe] 写出失败 %s：%s", png, err));
            return;
        }

        // 缩到粗网格后逐格取平均灰阶：只用于「有内容/各页不同」两条自检
        image.Resize(SigW, SigH, Image.Interpolation.Bilinear);
        var sig = new int[SigW * SigH];
        for (var i = 0; i < sig.Length; i++)
        {
            var c = image.GetPixel(i % SigW, i / SigW);
            sig[i] = (int)Mathf.Round(((c.R + c.G + c.B) / 3.0f) * 255.0f);
        }

        _shotSigs[name] = sig;
        _shots.Add(name);
    }

    /// <summary>长局难度曲线探针：把生产换算出的各量与**行业对齐的预期带**逐点比对。
    ///
    /// 为什么不用「跑 30 分钟再看」：难度映射是 D 的纯函数，长跑只是采样同一函数。
    /// 这里直接取 t = 5/10/20/30 分钟对应的 D（用生产曲线算），断言：
    ///   ① HP 与伤害乘区单调不减且斜率比在预期内（HP 快于伤害，与行业「HP 缩放缓于伤害但都低于玩家成长」一致）；
    ///   ② 速度乘区有硬顶（不得随 D 无限增长）；
    ///   ③ 波次间隔触底后不再缩、且不越过地板；
    ///   ④ 精英数量随 D 增长且有上限；
    ///   ⑤ Boss HP 乘区**慢于**完整 D（修正前等于 D，是 Boss 成墙的根因）；
    ///   ⑥ 时间项软上限之后斜率折减（挂机不再等比推高必死点）。
    /// 任一条不成立即 PushError 且不打完成标记（门禁按缺标记判红）。</summary>
    private void TickLongProbe()
    {
        if (_frame < 30)
        {
            return;
        }

        var cfg = GameState.Instance.Scaling();

        // 用**生产入口**求 t 时刻的难度乘数，而不是在探针里复刻公式：
        // 复刻会让「曲线公式改坏」时本探针仍测旧公式的形状、标记照打（护栏与生产脱钩）。
        // DifficultyCurve.Compute 就是 RunProgressionService.RecomputeDifficultyInternal 的同一函数。
        var timeStep = GameState.Instance.Cfg("progression.time_step_seconds", 30.0).AsDouble();
        var perTen = GameState.Instance.Cfg("progression.per_ten_minutes", 1.5).AsDouble();
        var perBoss = GameState.Instance.Cfg("progression.per_boss_kill", 0.6).AsDouble();

        double DifficultyAt(double minutes)
        {
            var raw = Core.Progression.DifficultyCurve.Compute(minutes * 60.0, timeStep, perTen, perBoss, 0);
            // 时间项折减与生产同源（RunProgressionService 消费的同一函数）
            var rawTimeTerm = raw - 1.0;
            var capped = Core.Progression.DifficultyScaling.SoftCappedTimeTerm(rawTimeTerm, cfg);
            return 1.0 + capped;
        }

        double prevHp = 0.0;
        double prevDmg = 0.0;
        foreach (var minutes in new[] { 5.0, 10.0, 20.0, 30.0 })
        {
            var d = DifficultyAt(minutes);
            var hp = Core.Progression.DifficultyScaling.EnemyHpRamp(d, cfg);
            var dmg = Core.Progression.DifficultyScaling.EnemyDamageRamp(d, cfg);
            var speed = Core.Progression.DifficultyScaling.EnemySpeedRamp(d, cfg);
            var bossHp = Core.Progression.DifficultyScaling.BossHpRamp(d, cfg);
            var wave = Core.Progression.DifficultyScaling.WaveInterval(4.0, d, cfg);
            var elites = Core.Progression.DifficultyScaling.EliteCount(d, cfg);

            if (hp < prevHp || dmg < prevDmg)
            {
                GD.PushError($"[long-probe] 难度乘区回退于 {minutes}min（hp={hp:0.###} dmg={dmg:0.###}）");
                _longProbe = false;
                return;
            }

            if (hp <= dmg)
            {
                GD.PushError($"[long-probe] {minutes}min 的 HP 乘区未超过伤害乘区（hp={hp:0.###} dmg={dmg:0.###}）");
                _longProbe = false;
                return;
            }

            if (speed > cfg.SpeedRampCap + 1e-6)
            {
                GD.PushError($"[long-probe] {minutes}min 速度乘区 {speed:0.###} 越过硬顶 {cfg.SpeedRampCap:0.###}");
                _longProbe = false;
                return;
            }

            // 硬顶必须真的「咬得住」：只判「不越顶」在把上限配成极大值时同样通过（等于没有上限）。
            // 末尾取极高 D，速度乘区必须恰好等于上限。
            if (minutes >= 30.0)
            {
                var pinned = Core.Progression.DifficultyScaling.EnemySpeedRamp(1000.0, cfg);
                if (Math.Abs(pinned - cfg.SpeedRampCap) > 1e-6)
                {
                    GD.PushError($"[long-probe] 速度硬顶未生效：D=1000 时 {pinned:0.###} ≠ 上限 {cfg.SpeedRampCap:0.###}");
                    _longProbe = false;
                    return;
                }
            }

            if (wave < cfg.WaveIntervalFloor - 1e-6)
            {
                GD.PushError($"[long-probe] {minutes}min 波次间隔 {wave:0.###}s 跌破地板 {cfg.WaveIntervalFloor:0.###}s");
                _longProbe = false;
                return;
            }

            if (elites < 1 || elites > cfg.EliteCountCap)
            {
                GD.PushError($"[long-probe] {minutes}min 精英数量 {elites} 越界 [1, {cfg.EliteCountCap}]");
                _longProbe = false;
                return;
            }

            // Boss HP 必须慢于完整 D（修正前 = D，斜率是杂兵 4 倍）
            if (bossHp >= d)
            {
                GD.PushError($"[long-probe] {minutes}min Boss HP 乘区未低于完整难度乘数（boss={bossHp:0.###} d={d:0.###}）");
                _longProbe = false;
                return;
            }

            // Boss 攻击密度必须随 D 增长（后期压力要有「密度」这一轴，而不只是更肉更痛）
            var density = Core.Progression.DifficultyScaling.BossDensityBonus(d, cfg);
            if (minutes >= 30.0 && density <= 0)
            {
                GD.PushError($"[long-probe] {minutes}min Boss 攻击密度追加为 0（密度未接难度）");
                _longProbe = false;
                return;
            }

            if (density > cfg.BossDensityBonusCap)
            {
                GD.PushError($"[long-probe] {minutes}min Boss 密度追加 {density} 越过上限 {cfg.BossDensityBonusCap}");
                _longProbe = false;
                return;
            }

            prevHp = hp;
            prevDmg = dmg;
        }

        // 精英数量必须真的随时间增长（否则「后期靠密度」根本没接上）
        var eliteEarly = Core.Progression.DifficultyScaling.EliteCount(DifficultyAt(1.0), cfg);
        var eliteLate = Core.Progression.DifficultyScaling.EliteCount(DifficultyAt(30.0), cfg);
        if (eliteLate <= eliteEarly)
        {
            GD.PushError($"[long-probe] 精英数量不随难度增长（1min={eliteEarly} 30min={eliteLate}）");
            _longProbe = false;
            return;
        }

        // 时间项软上限：折减后必须仍单调不减、且慢于不折减
        var raw = 20.0;
        var capped = Core.Progression.DifficultyScaling.SoftCappedTimeTerm(raw, cfg);
        if (!(capped > 0.0) || capped >= raw)
        {
            GD.PushError($"[long-probe] 时间项软上限未生效（raw={raw} capped={capped:0.###}）");
            _longProbe = false;
            return;
        }

        // 里程碑进度：开局应为 0（未得分），且必须落在 [0,1]——算错会让 HUD 条骗人
        var mp = GameState.Instance.MilestoneProgress();
        if (mp < 0.0 || mp > 1.0)
        {
            GD.PushError($"[long-probe] 里程碑进度越界：{mp:0.###}（应在 [0,1]）");
            _longProbe = false;
            return;
        }

        // 本局达成判定与命名档位（生产配置）：达成线必须可达、档位必须随 D 单调推进。
        // 达成判定坏掉的表现是「打了很久也没算赢」，无头下不崩不报错，只能靠断言。
        var goalCfg = new Core.Progression.RunGoalConfig
        {
            BossKillsTarget = GameState.Instance.GoalBossKills(),
            SurviveSeconds = GameState.Instance.GoalSurviveSeconds(),
        };
        if (goalCfg.BossKillsTarget > 0 && Core.Progression.RunGoal.Achieved(goalCfg.BossKillsTarget, 0.0, goalCfg) == false)
        {
            GD.PushError("[long-probe] 达不成线不可达：Boss 击杀数达到目标仍判未达成");
            _longProbe = false;
            return;
        }

        if (goalCfg.SurviveSeconds > 0.0 && !Core.Progression.RunGoal.Achieved(0, goalCfg.SurviveSeconds, goalCfg))
        {
            GD.PushError("[long-probe] 达不成线不可达：存活到目标时长仍判未达成");
            _longProbe = false;
            return;
        }

        if (Core.Progression.RunGoal.Achieved(0, 0.0, goalCfg))
        {
            GD.PushError("[long-probe] 开局即判达成（目标线配置失效）");
            _longProbe = false;
            return;
        }

        var tierEarly = GameState.Instance.DifficultyTierIndex();
        if (tierEarly != 0)
        {
            GD.PushError($"[long-probe] 开局难度档位应为 0，实为 {tierEarly}");
            _longProbe = false;
            return;
        }

        GD.Print("[long-probe] 难度曲线落在预期带");
        _longProbe = false;
    }

    /// <summary>手感探针：请求四档顿帧与一次震动，断言「时间缩放确实被压低 + trauma 确实累加 +
    /// 顿帧在真实时间下会自行结束」。
    ///
    /// 为什么不能只判「不崩」：顿帧写的是 Engine.TimeScale——写错（倍率写成 0 或按缩放 delta 推进）
    /// 的表现是**画面永久定格**，无头下不崩、也不报错，只有完成标记能抓住。
    ///
    /// **断言必须读引擎真值**：只读手感域自己算出的 `FeelTimeScale()` 会漏掉「算了但没落笔」这一整类
    /// 故障（实测：删掉 `GameFeelService.ApplyTimeScale` 的赋值，自算值仍返回正常值、探针照样全绿）。
    /// 故冻结中断言 `Engine.TimeScale` 真的 &lt; 1，复位后断言它真的回到 1。震动的读口读 trauma——
    /// 位移采样在 `CameraShake._Process`，而相机不在 headless 探针宿主内，故只断言到「trauma 确实被累加/衰减」。
    /// 顺序固定：先等场景稳定，再请求，最后断言引擎时间缩放已复位。</summary>
    private void TickFeelProbe()
    {
        // 前 30 帧等入场与稳定（入场窗口内玩家不可驱动、GameState 时钟未起）
        if (_frame < 30)
        {
            return;
        }

        if (GameState.Instance.HitStopActive())
        {
            // 冻结中：**引擎**的时间缩放必须真被压低（读 Engine.TimeScale，不读自算值）。
            // 探针宿主 ProcessMode=Always 且本驱动每帧都在跑，故这里能看到冻结窗口。
            if ((float)Engine.TimeScale >= 1.0f)
            {
                GD.PushError($"[feel-probe] 顿帧激活但引擎时间缩放未压低（Engine.TimeScale={(float)Engine.TimeScale:0.###}）");
                _feelProbe = false;
                return;
            }

            // trauma 由震动请求累加，与顿帧独立——两者都断言，任一路坏都要红
            if (GameState.Instance.ShakeTrauma() > 0.0)
            {
                _feelSawTrauma = true;
            }

            _feelSawHitStop = true;
            return;
        }

        if (_feelStep == 0)
        {
            // 直接走公开震动入口（生产无探针专用 API）；取值仍是生产配置里的最大档，
            // 保证 trauma 累加量足以被断言观测到
            GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_seq_final", 24.0).AsDouble());
            GameState.Instance.RequestHitStop(Core.GameFeel.HitStopTier.Heavy);
            _feelStep = 1;
            return;
        }

        // 顿帧结束：确认已真正观察到冻结、trauma 已累加、时间缩放已复位
        if (_feelStep == 1)
        {
            if (!_feelSawHitStop)
            {
                GD.PushError("[feel-probe] 从未观测到顿帧激活（请求被吞或时序未推进）");
                _feelProbe = false;
                return;
            }

            if (!_feelSawTrauma)
            {
                GD.PushError("[feel-probe] 震动未累加 trauma（Shake 入口未接到手感域）");
                _feelProbe = false;
                return;
            }

            // 复位断言同样读引擎真值：自算值说「复位了」而引擎仍被压死，正是本探针要抓的定格
            if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
            {
                GD.PushError($"[feel-probe] 顿帧已结束但引擎时间缩放未复位（Engine.TimeScale={(float)Engine.TimeScale:0.###}，画面将永久定格）");
                _feelProbe = false;
                return;
            }

            _feelStep = 2;
            return;
        }

        // trauma 自行衰减至 0：衰减链路（GameFeelService.Tick 按真实帧长推进）确实在跑
        if (_feelStep == 2)
        {
            if (GameState.Instance.ShakeTrauma() > 1e-4)
            {
                return;
            }

            GD.Print("[feel-probe] 顿帧与震动复位完成");
            _feelProbe = false;
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

    /// <summary>迷雾探针驱动：在宿主里请求一次**强制指定**的迷雾事件（fake_enemies，无伤害/无碰撞，
    /// 不扰动玩家血量/输入/子弹参数），等该 id 的完整周期（EventStarted → EventEnded）跑完再打标记。
    ///
    /// 为什么不能只靠自动触发：迷雾走「首延迟 25s + 每 3s 掷 35%」的随机链，常规冒烟趟跑不到，
    /// 而迷雾注册/context 构建/生命周期/效果清理是高密度出错区。
    /// **仍走生产门控**：强制入口只替换掷签与权重选取，TryTriggerGroup 内先过 CanTriggerGroup
    /// （接线/启用/本局活跃/组内无进行中/首延迟到点/冷却到点）——门控断线时本探针一并发红。
    /// 帧数只由 --fixed-fps 60 驱动，不等真实时间；截断（提前 EndActive）不满足完整周期，不打标记。</summary>
    private void TickFogProbe()
    {
        // 前 30 帧等入场与稳定（入场窗口内玩家不可驱动、生产时钟未起）
        if (_frame < 30 || _player.IsEntryPlaying() || !_spawner.IsProcessing())
        {
            return;
        }

        _player.SetInvincible(ProbeInvincibleSeconds); // 无头局玩家不操作，与存活解耦

        if (!_fogActive)
        {
            // 门控未到点（首延迟/冷却）时 TryTriggerGroup 返回 false——下帧再试，不绕过
            _events.TryTriggerGroup(GameEventManager.GroupFog);
        }
    }

    /// <summary>迷雾事件开始：只认强制指定的 id，记下起始帧与生产下发的 duration
    /// （完整周期判定用；duration 由管理器从 balance 读出，探针不复刻配置）。</summary>
    private void OnFogEventStarted(StringName eventId, float duration)
    {
        if (!_fogProbe)
        {
            return;
        }

        if (eventId != FogProbeEventId)
        {
            GD.PushError(GdFormat.Format("[fog-probe] 启动了非指定迷雾事件 %s（强制入口应只启动 %s）",
                eventId, FogProbeEventId));
            _fogProbe = false;
            return;
        }

        _fogActive = true;
        _fogStartFrame = _frame;
        _fogDuration = duration;
    }

    /// <summary>迷雾事件结束：跑满完整周期（实际时长 ≥ 生产 duration − 时序容差）才打完成标记；
    /// 截断/中途结束一律不打（门禁按缺标记判红）。</summary>
    private void OnFogEventEnded(StringName eventId)
    {
        if (!_fogProbe || !_fogActive || eventId != FogProbeEventId)
        {
            return;
        }

        _fogActive = false;
        var elapsed = (_frame - _fogStartFrame) / 60.0;
        if (elapsed + FogCycleToleranceSeconds < _fogDuration)
        {
            GD.PushError(GdFormat.Format(
                "[fog-probe] %s 周期被截断（实际 %.2fs < 生产 duration %.2fs）", eventId, elapsed, _fogDuration));
            _fogProbe = false;
            return;
        }

        GD.Print("[fog-probe] 迷雾全周期完成");
        _fogProbe = false;
    }

    /// <summary>返航宽限探针：走生产蓄力链触发返航（长按 homecoming 蓄满，不直调过场、不绕过输入判定），
    /// 再分三段确定性地覆盖输入宽限与跳过收尾。
    ///
    /// 为什么不能照搬旧测试「等 1.4s 真实时间越宽限」：`--fixed-fps 60` 下引擎不等真实时间，帧跑得远快于
    /// 墙钟（71e6324 记载过由此导致的静默跳过失效），等真实时间的写法在帧预算内既慢又不可靠。宽限本身
    /// 用真实时间是对的（见 DESIGN_BASELINE §2.10），故本探针**不靠等待**，改用两个确定性判据：
    ///   1) 开播即调跳过 → 必须被忽略（宽限存在）；
    ///   2) 推进 ReturnProbeGraceWindowFrames（90 帧 = 1.5 模拟秒 &gt; 生产宽限 1.2s）后仍必须被忽略——
    ///      若宽限被误改成模拟时间，此刻 1.5s &gt; 1.2s 会放行，判据即红。真实时间下这段墙钟远未越界，
    ///      探针另测墙钟做前置守卫（环境过慢则显式报错，绝不静默放过）。
    ///   3) 把宽限置 0（确定越过边界）后跳过必须生效，且收尾落在基地、树保持暂停。
    /// 段 2 的「仍被忽略」是真实时间基准的判别式；「是否用真实时间 API」另由 check_realtime_allowlist.sh 兜住。</summary>
    private void TickReturnProbe()
    {
        // 触发前的等待/蓄力阶段：等入场结束、spawner 可处理（生产蓄力链的前置）。
        // 注意这一段守卫**只**管「尚未开播的 stage 0」——过场一开播 Main 就 SetProcess(false) 停掉
        // spawner，若把 `!IsProcessing()` 继续套用会永远卡在 stage 0（实测 proc 会变 false）。
        if (_returnStage == 0 && !_main.IsReturnPlaying()
            && (_frame < 30 || _player.IsEntryPlaying() || !_spawner.IsProcessing()))
        {
            return;
        }

        _player.SetInvincible(ProbeInvincibleSeconds); // 无头局玩家不操作，与存活解耦

        if (_returnStage == 0)
        {
            if (_main.IsReturnPlaying())
            {
                Input.ActionRelease(ActHomecoming);
                var ret = _main.ReturnCinematic();
                if (ret == null)
                {
                    ReturnProbeFail("返航过场已开始但引用为空");
                    return;
                }

                _returnStartFrame = _frame;
                _returnWallStart = Time.GetTicksMsec();
                _returnGrace = ret.SKIP_GRACE; // 生产宽限值（判别与诊断用）
                _returnStage = 1;
                return;
            }

            _returnChargeFrames++;
            if (_returnChargeFrames > ReturnProbeChargeFrames)
            {
                Input.ActionRelease(ActHomecoming);
                ReturnProbeFail(GdFormat.Format("长按返航蓄力 %d 帧仍未触发过场", ReturnProbeChargeFrames));
                return;
            }

            Input.ActionPress(ActHomecoming); // 生产蓄力链：Main._Process 读同一动作
            return;
        }

        var cinematic = _main.ReturnCinematic();
        if (cinematic == null)
        {
            ReturnProbeFail("返航过场在收尾前意外消失");
            return;
        }

        if (_returnStage == 1)
        {
            _main.SkipReturn();
            if (!_main.IsReturnPlaying())
            {
                ReturnProbeFail("宽限期内跳过未被忽略（过场已销毁）");
                return;
            }

            _returnStage = 2;
            return;
        }

        if (_returnStage == 2)
        {
            if (_frame - _returnStartFrame < ReturnProbeGraceWindowFrames)
            {
                return;
            }

            var wallMs = Time.GetTicksMsec() - _returnWallStart;
            var graceMs = (ulong)(_returnGrace * 1000.0f);
            if (wallMs + ReturnProbeGraceMarginMs >= graceMs)
            {
                // 墙钟已追上宽限，本趟无法判别「真实时间 vs 模拟时间」——显式失败，不静默放过
                ReturnProbeFail(GdFormat.Format(
                    "环境过慢：%d 帧耗 %dms 已达生产宽限 %dms，无法判别时间基准",
                    ReturnProbeGraceWindowFrames, (long)wallMs, (long)graceMs));
                return;
            }

            _main.SkipReturn();
            if (!_main.IsReturnPlaying())
            {
                ReturnProbeFail(GdFormat.Format(
                    "推进 %d 帧后跳过被提前放行——宽限疑似按模拟时间计（应为真实时间）",
                    ReturnProbeGraceWindowFrames));
                return;
            }

            _returnStage = 3;
            return;
        }

        // 段 3：宽限置 0 确定越过边界 → 跳过必须生效，收尾落基地且树保持暂停
        cinematic.SKIP_GRACE = 0.0f;
        _main.SkipReturn();
        if (_main.IsReturnPlaying())
        {
            ReturnProbeFail("越过宽限后跳过未生效");
            return;
        }

        var baseUi = _main.GetNodeOrNull<BaseConsole>("BaseUI");
        if (baseUi == null || !baseUi.Visible)
        {
            ReturnProbeFail("跳过收尾未显示基地 UI");
            return;
        }

        if (!GetTree().Paused)
        {
            ReturnProbeFail("跳过收尾树未保持暂停（基地界面应为暂停态）");
            return;
        }

        GD.Print("[return-probe] 返航宽限与跳过收尾完成");
        _returnProbe = false;
    }

    /// <summary>返航宽限探针失败：报错并停探针（门禁按缺完成标记判红）。</summary>
    private void ReturnProbeFail(string reason)
    {
        GD.PushError("[return-probe] " + reason);
        _returnProbe = false;
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
#endif
