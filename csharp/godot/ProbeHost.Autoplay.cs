using Godot;
using InfiAir.Core.Text;

// 自动游玩探针（autoplay，ProbeHost 的 partial 分文件）：把「一个不会累的玩家」接在生产输入面上，
// 让整局游戏在无头固定步长下真跑一段——判「能不能玩」、做长局冒烟、给调参看遥测。
// 文件名带类型前缀是本类 partial 的分文件例外（栈内单文件 4800+ 行）。
//
// 定位与纪律（AGENTS §5/§6）：
//   - **CI 趟只取短局**（`--autoplay-probe=180`，≈8s 墙钟）：判「能开局但玩不起来」这一类——
//     整局零击杀（火力/命中/刷怪链断线）、存活却连续 60 模拟秒无得分无击杀（停摆）、跑局中的引擎错误；
//     通过才打完成标记 `[autoplay-probe] 自动游玩完成`。
//   - **长局靠人工**（`--autoplay-probe=900`，≈40s 墙钟）：覆盖后半程难度、Boss、遭遇与母舰循环——
//     时长换覆盖，不塞进 CI。跑法＝`godot --headless --path . --fixed-fps 60 --quit-after <秒×60+余量>
//     --scene res://scenes/probe_host.tscn -- --autoplay-probe=<秒> --expect-user-dir=<临时目录>`，
//     并把 `APPDATA`/`XDG_DATA_HOME`/`HOME` 一并指向该临时目录；CI 短局覆盖不到的长时面按
//     AGENTS §5 登记人工项或 ROADMAP 开放项，不留口头。
//   - **真实规则**：不注入无敌、不直接改血量/得分/位置，全部经生产输入面（Input 动作 +
//     Player.AimPointOverride）驱动；死了就是死了（必死曲线下的正常收场），只写进汇总、不判失败。
//   - **必须在隔离用户目录里跑**：未带 `--expect-user-dir` 时探针拒绝启动（它会写 user://，
//     死亡还会删本局检查点，不能落在开发者真实存档上）。
//   - **判定只看模拟状态**（AGENTS §5）：帧数与时长取自 _frame（--fixed-fps 60 下帧数＝模拟秒），
//     不读墙钟、不读帧率。
//   - **随机口径**：遭遇事件保持生产开启（整局的一部分），迷雾随机事件按 §5 关闭（由 --fog-probe 专趟覆盖）；
//     本探针的判定（零击杀、停摆）不依赖任何随机结果。
//   - 感知只用公开只读口：敌弹表（位置 + Direction/Speed 预测）、敌机表、GameState 遥测。
#if DEBUG || TOOLS
namespace InfiAir;

public partial class ProbeHost
{
    /// <summary>模拟游玩的默认时长（模拟秒）：8 分钟足够走过多档难度与多次遭遇。</summary>
    private const int AutoplayProbeDefaultSeconds = 480;

    /// <summary>遥测采样间隔（帧）：600 帧＝10 模拟秒。</summary>
    private const int AutoplayLogIntervalFrames = 600;

    /// <summary>避弹前瞻窗口（秒）：按候选落点与威胁在该时刻的预测位置打分。</summary>
    private const float AutoplayDodgeHorizon = 0.45f;

    /// <summary>避弹采样方向数（均匀取整圆方向，取最安全的一侧）。</summary>
    private const int AutoplayMoveSampleCount = 24;

    /// <summary>避弹安全半径（px）：弹心到本机落点的最小期望间距（已含弹与机身的半径余量）。</summary>
    private const float AutoplaySafeRadius = 34.0f;

    /// <summary>敌机安全半径（px）：撞机与接触伤害的规避距离。</summary>
    private const float AutoplayEnemySafeRadius = 96.0f;

    /// <summary>希望保持的纵向位置比例：0.72＝画面下方（纵向弹幕留出反应纵深）。</summary>
    private const float AutoplayPreferredBandRatio = 0.72f;

    /// <summary>停摆判定阈值（模拟秒）：存活且场上有敌机时，这么久没有任何得分/击杀变化即判停摆。</summary>
    private const int AutoplayStallSeconds = 60;

    /// <summary>弹反脉冲的触发距离（px）：威胁进入该圈就地弹反一次（脉冲式，非长按）。</summary>
    private const float AutoplayParryTriggerRadius = 78.0f;

    /// <summary>两次召唤母舰之间的最小间隔（模拟秒）：母舰是补弹匣/升火力的正常循环，隔一段叫一次。</summary>
    private const int AutoplayDockIntervalSeconds = 150;

    /// <summary>单次召唤的蓄力帧上限（mothership.dock_charge_time 3s = 180 帧，留 2 倍余量）。</summary>
    private const int AutoplayDockHoldFrames = 360;

    private static readonly StringName AutoplayActFire = new("fire");
    private static readonly StringName AutoplayActParry = new("parry");
    private static readonly StringName AutoplayActMoveLeft = new("move_left");
    private static readonly StringName AutoplayActMoveRight = new("move_right");
    private static readonly StringName AutoplayActMoveUp = new("move_up");
    private static readonly StringName AutoplayActMoveDown = new("move_down");

    private bool _autoplayProbe;
    private int _autoplaySeconds = AutoplayProbeDefaultSeconds;
    private int _autoplayStage;
    private int _autoplayStartFrame;
    private int _autoplayLastLogFrame;
    private int _autoplayLastProgressFrame;
    private int _autoplayLastScore;
    private int _autoplayLastKills;
    private int _autoplayFireHeld;
    private int _autoplayParryHold;
    private int _autoplayMoveX; // -1/0/1：本帧已下发的横向输入（避免逐帧重复 ActionPress）
    private int _autoplayMoveY;
    private bool _autoplayDockHolding;
    private int _autoplayDockHoldStart;
    private int _autoplayLastDockFrame;
    private bool _autoplaySawDeath;
    private bool _autoplayStalled;
    private int _autoplayDeathCount;
    private int _autoplaySamples;
    private int _autoplayMaxScore;
    private int _autoplayMaxKills;
    private double _autoplayMaxDifficulty;
    private bool _autoplaySawBoss;
    private bool _autoplaySawEncounter;
    private bool _autoplaySawMothership;

    /// <summary>模拟游玩主循环：等生产链就绪 → 逐帧驱动 → 按模拟时长收尾（或停摆提前判失败）。</summary>
    private void TickAutoplayProbe()
    {
        if (_autoplayStage == 0)
        {
            if (_frame < 30 || _player.IsEntryPlaying() || !_spawner.IsProcessing())
            {
                return;
            }

            _autoplayStage = 1;
            _autoplayStartFrame = _frame;
            _autoplayLastLogFrame = _frame;
            _autoplayLastProgressFrame = _frame;
            _autoplayLastScore = GameState.Instance.Score;
            _autoplayLastKills = GameState.Instance.Kills;
            GD.Print(GdFormat.Format("[autoplay-probe] 模拟游玩开始（真实规则；预算 %ds）", _autoplaySeconds));
        }

        if (!_player.IsDead())
        {
            TickAutoplayInput();
        }
        else if (!_autoplaySawDeath)
        {
            _autoplaySawDeath = true;
            _autoplayDeathCount++;
            GD.Print(GdFormat.Format("[autoplay-probe] 玩家死亡 t=%.1fs（必死曲线下的正常收场）",
                (_frame - _autoplayStartFrame) / 60.0));
        }

        TrackAutoplayTelemetry();
        if (!_autoplaySawDeath && _frame - _autoplayLastProgressFrame > AutoplayStallSeconds * 60)
        {
            _autoplayStalled = true;
            GD.PushError(GdFormat.Format(
                "[autoplay-probe] 停摆：存活且在场敌机存在，但连续 %ds 既无得分也无击杀（刷怪/命中/结算疑似断链）",
                AutoplayStallSeconds));
        }

        if (_frame - _autoplayLastLogFrame >= AutoplayLogIntervalFrames)
        {
            ReportAutoplayTelemetry();
        }

        if (_autoplayStalled || _frame - _autoplayStartFrame >= _autoplaySeconds * 60)
        {
            FinishAutoplayProbe();
        }
    }

    /// <summary>逐帧驱动：移动（避弹）、瞄准（最近敌机）、开火（按住）、弹反（贴近脉冲）。</summary>
    private void TickAutoplayInput()
    {
        var pos = _player.GlobalPosition;
        var band = GameState.Instance.ViewWorldRect(0.0f);
        AutoplaySetMove(AutoplayChooseMoveDir(pos, band));

        var target = AutoplayNearestEnemy(pos);
        _player.AimPointOverride = target != null
            ? target.AimWorldPosition
            : new Vector2(pos.X, pos.Y - 100.0f);

        if (_autoplayFireHeld == 0)
        {
            Input.ActionPress(AutoplayActFire);
            _autoplayFireHeld = 1;
        }

        AutoplayPressPulse(AutoplayActParry, ref _autoplayParryHold, AutoplayThreatDistance(pos) <= AutoplayParryTriggerRadius);
        TickAutoplayDock();
    }

    /// <summary>周期性召唤母舰（走生产蓄力链：长按 dock 到触发，资格由生产侧判）。持有期间照常避弹/开火。</summary>
    private void TickAutoplayDock()
    {
        if (_autoplayDockHolding)
        {
            Input.ActionPress(ActDock);
            var arrived = _main.Mothership() != null;
            var timedOut = _frame - _autoplayDockHoldStart > AutoplayDockHoldFrames;
            if (arrived || timedOut)
            {
                Input.ActionRelease(ActDock);
                _autoplayDockHolding = false;
                _autoplayLastDockFrame = _frame;
            }

            return;
        }

        var cooldownOk = _main.Mothership() == null && _main.DockCooldown() <= 0.0f;
        var encounterIdle = _events.ActiveId(_events.GROUP_ENCOUNTER).ToString().Length == 0;
        var due = _frame - _autoplayLastDockFrame >= AutoplayDockIntervalSeconds * 60;
        if (cooldownOk && encounterIdle && due)
        {
            _autoplayDockHolding = true;
            _autoplayDockHoldStart = _frame;
            Input.ActionPress(ActDock); // 生产侧 Main._Process 读同一动作（蓄力 3s 触发召唤）
        }
    }

    /// <summary>候选方向采样避弹：对每个候选落点按「与威胁预测位置的间距」打分，取最安全且位置合适的一侧。</summary>
    private Vector2 AutoplayChooseMoveDir(Vector2 pos, Rect2 band)
    {
        var speed = (float)GameState.Instance.Cfg("player.max_speed", 420.0).AsDouble();
        var preferredY = band.Position.Y + (band.Size.Y * AutoplayPreferredBandRatio);
        var best = Vector2.Zero;
        var bestScore = float.NegativeInfinity;
        for (var i = 0; i < AutoplayMoveSampleCount; i++)
        {
            var angle = Mathf.Tau * i / AutoplayMoveSampleCount;
            var cand = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var landing = pos + (cand * speed * AutoplayDodgeHorizon);
            var score = -AutoplayThreatPenalty(landing);
            score -= Mathf.Abs(landing.Y - preferredY) / band.Size.Y;          // 回到偏爱的纵向带
            score -= Mathf.Abs(landing.X - band.GetCenter().X) / band.Size.X;  // 轻度居中，避免贴边
            if (!band.Grow(AutoplaySafeRadius).HasPoint(landing))
            {
                score -= 100.0f; // 出框：直接否决（出框=被墙卡住，弹幕贴脸无解）
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = cand;
            }
        }

        return best;
    }

    /// <summary>威胁罚分：敌弹按 Direction/Speed 前推 AutoplayDodgeHorizon 后的位置、敌机按当前位置计。</summary>
    private float AutoplayThreatPenalty(Vector2 landing)
    {
        var penalty = 0.0f;
        var bullets = GameState.Instance.EnemyBullets;
        for (var i = 0; i < bullets.Count; i++)
        {
            var bullet = bullets[i];
            if (!GodotObject.IsInstanceValid(bullet))
            {
                continue;
            }

            var ahead = bullet.GlobalPosition + (bullet.Direction * (bullet.Speed * AutoplayDodgeHorizon));
            penalty += AutoplayRadiusPenalty(landing, ahead, AutoplaySafeRadius + Bullet.CollisionRadius, 4.0f);
        }

        var enemies = GameState.Instance.Enemies;
        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (!GodotObject.IsInstanceValid(enemy))
            {
                continue;
            }

            penalty += AutoplayRadiusPenalty(landing, enemy.GlobalPosition, AutoplayEnemySafeRadius, 1.0f);
        }

        return penalty;
    }

    private static float AutoplayRadiusPenalty(Vector2 landing, Vector2 threat, float safe, float weight)
    {
        var distance = landing.DistanceTo(threat);
        if (distance >= safe)
        {
            return 0.0f;
        }

        var closeness = (safe - distance) / safe;
        return closeness * closeness * weight;
    }

    /// <summary>最近威胁（敌弹优先）的距离：供弹反触发与遥测用。</summary>
    private float AutoplayThreatDistance(Vector2 pos)
    {
        var nearest = float.PositiveInfinity;
        var bullets = GameState.Instance.EnemyBullets;
        for (var i = 0; i < bullets.Count; i++)
        {
            var bullet = bullets[i];
            if (GodotObject.IsInstanceValid(bullet))
            {
                nearest = Mathf.Min(nearest, pos.DistanceTo(bullet.GlobalPosition));
            }
        }

        return nearest;
    }

    /// <summary>最近的敌机（含 Boss 与遭遇单位；Boss 同在某注册表里）。只取契约上可瞄的单位
    /// （AimTargetable：存活、在册、非升起/收回态）——瞄一个打不动的目标等于空放。</summary>
    private IAimTarget? AutoplayNearestEnemy(Vector2 pos)
    {
        IAimTarget? best = null;
        var bestDistance = float.PositiveInfinity;
        var enemies = GameState.Instance.Enemies;
        for (var i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] is not IAimTarget target || !target.AimTargetable)
            {
                continue;
            }

            var distance = pos.DistanceSquaredTo(target.AimWorldPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = target;
            }
        }

        return best;
    }

    /// <summary>把方向下发成四个移动动作（只在变化沿重发，避免逐帧重复 ActionPress）。</summary>
    private void AutoplaySetMove(Vector2 dir)
    {
        var x = dir.X > 0.35f ? 1 : (dir.X < -0.35f ? -1 : 0);
        var y = dir.Y > 0.35f ? 1 : (dir.Y < -0.35f ? -1 : 0);
        if (x != _autoplayMoveX)
        {
            Input.ActionRelease(AutoplayActMoveLeft);
            Input.ActionRelease(AutoplayActMoveRight);
            if (x < 0)
            {
                Input.ActionPress(AutoplayActMoveLeft);
            }
            else if (x > 0)
            {
                Input.ActionPress(AutoplayActMoveRight);
            }

            _autoplayMoveX = x;
        }

        if (y != _autoplayMoveY)
        {
            Input.ActionRelease(AutoplayActMoveUp);
            Input.ActionRelease(AutoplayActMoveDown);
            if (y < 0)
            {
                Input.ActionPress(AutoplayActMoveUp);
            }
            else if (y > 0)
            {
                Input.ActionPress(AutoplayActMoveDown);
            }

            _autoplayMoveY = y;
        }
    }

    /// <summary>按下-松手的二帧脉冲（just-pressed 语义的动作都走它；hold 非 0 表示脉冲未收尾）。</summary>
    private static void AutoplayPressPulse(StringName action, ref int hold, bool wanted)
    {
        if (hold == 0 && wanted)
        {
            Input.ActionPress(action);
            hold = 2;
            return;
        }

        if (hold > 0)
        {
            hold--;
            if (hold == 0)
            {
                Input.ActionRelease(action);
            }
        }
    }

    /// <summary>遥测累积（每帧，零分配）：峰值与「见过哪些系统」的布尔量。</summary>
    private void TrackAutoplayTelemetry()
    {
        var gs = GameState.Instance;
        _autoplayMaxScore = Mathf.Max(_autoplayMaxScore, gs.Score);
        _autoplayMaxKills = Mathf.Max(_autoplayMaxKills, gs.Kills);
        _autoplayMaxDifficulty = Mathf.Max(_autoplayMaxDifficulty, gs.DifficultyMultiplier);
        _autoplaySawBoss |= _spawner.IsBossActive();
        _autoplaySawEncounter |= _events.ActiveId(_events.GROUP_ENCOUNTER).ToString().Length > 0;
        _autoplaySawMothership |= _main.Mothership() != null;

        if (gs.Score != _autoplayLastScore || gs.Kills != _autoplayLastKills)
        {
            _autoplayLastScore = gs.Score;
            _autoplayLastKills = gs.Kills;
            _autoplayLastProgressFrame = _frame;
        }
    }

    /// <summary>遥测采样行（10 模拟秒一条；日志里能直接看出局势怎么走）。</summary>
    private void ReportAutoplayTelemetry()
    {
        _autoplayLastLogFrame = _frame;
        _autoplaySamples++;
        var gs = GameState.Instance;
        var mothership = _main.Mothership();
        GD.Print(GdFormat.Format(
            "[autoplay-probe] t=%.1fs HP=%.0f/%.0f 分=%d 杀=%d 连击=%dx D=%.2f 敌=%d 敌弹=%d 事件=%s Boss=%s 母舰=%s",
            (_frame - _autoplayStartFrame) / 60.0,
            gs.Health,
            gs.MaxHealth(),
            gs.Score,
            gs.Kills,
            gs.Combo,
            gs.DifficultyMultiplier,
            gs.Enemies.Count,
            gs.EnemyBullets.Count,
            _events.ActiveId(_events.GROUP_ENCOUNTER).ToString() is { Length: > 0 } id ? id : "无",
            _spawner.IsBossActive() ? "在场" : "无",
            mothership == null ? "无" : mothership.GetState().ToString()));
    }

    /// <summary>收尾：释放注入的输入、打印汇总，并只在**真正通过**时打完成标记——
    /// 停摆（存活却长时间无得分无击杀）与整局零击杀都判红且不打标记：这些是「能开局但玩不起来」
    /// 的判别形态（火力/命中/刷怪链路断线），只判「不崩」抓不到（AGENTS §5 完成信号）。
    /// 死亡不算失败：必死曲线下死在预算内是正常收场，只会写进汇总。</summary>
    private void FinishAutoplayProbe()
    {
        ReleaseAutoplayInputs();
        var survived = (_frame - _autoplayStartFrame) / 60.0;
        GD.Print(GdFormat.Format(
            "[autoplay-probe] 汇总：模拟 %.1fs、采样 %d 次、峰值 分=%d 杀=%d D=%.2f、死亡 %d 次、"
            + "见过 Boss=%s 遭遇=%s 母舰=%s",
            survived, _autoplaySamples, _autoplayMaxScore, _autoplayMaxKills, _autoplayMaxDifficulty, _autoplayDeathCount,
            _autoplaySawBoss ? "是" : "否", _autoplaySawEncounter ? "是" : "否", _autoplaySawMothership ? "是" : "否"));

        if (_autoplayMaxKills == 0)
        {
            GD.PushError(GdFormat.Format(
                "[autoplay-probe] 整局零击杀（模拟 %.1fs）：火力/命中/刷怪链路疑似断线——探针持续开火并瞄最近目标，"
                + "一次命中都没有说明这条链整条不通",
                survived));
            _autoplayStalled = true;
        }

        if (_autoplayStalled)
        {
            GD.Print("[autoplay-probe] 自动游玩中断（见上方 ::error；不打完成标记）");
        }
        else
        {
            // 标记带预算值：与开关同源（check_gate_wiring 断「带值开关的值必须出现在标记里」）——
            // 改 --autoplay-probe 的值却忘改断言会被判红，不会静默判到别的打印点
            GD.Print(GdFormat.Format("[autoplay-probe] 自动游玩完成（预算 %ds）", _autoplaySeconds));
        }

        _autoplayProbe = false;
    }

    /// <summary>释放全部注入状态（探针结束/退出树都要收—否则动作残留会影响同进程的后续场景）。</summary>
    private void ReleaseAutoplayInputs()
    {
        Input.ActionRelease(AutoplayActFire);
        Input.ActionRelease(AutoplayActParry);
        Input.ActionRelease(ActDock);
        Input.ActionRelease(AutoplayActMoveLeft);
        Input.ActionRelease(AutoplayActMoveRight);
        Input.ActionRelease(AutoplayActMoveUp);
        Input.ActionRelease(AutoplayActMoveDown);
        _autoplayFireHeld = 0;
        _autoplayParryHold = 0;
        _autoplayDockHolding = false;
        _autoplayMoveX = 0;
        _autoplayMoveY = 0;
        if (_player != null && GodotObject.IsInstanceValid(_player))
        {
            _player.AimPointOverride = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        }
    }
}
#endif
