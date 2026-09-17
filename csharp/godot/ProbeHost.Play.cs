using Godot;
using InfiAir.Core.Text;

// 模拟游玩探针（ProbeHost 的 partial 分文件）：把「一个不会累的玩家」接在生产输入面上，// 让整局游戏在无头固定步长下真跑一段——验证可玩性、做长局冒烟、给调参看遥测。
// 文件名带类型前缀是本类 partial 的分文件例外（栈内单文件 4800+ 行）。
//
// 设计口径：
//   - **真实规则**：不注入无敌、不直接改血量/得分/位置，全部经生产输入面（Input 动作 +
//     Player.AimPointOverride）驱动；死了就是死了（必死曲线下的正常收场），探针据此记录而非判失败。
//   - **判定只看模拟状态**（AGENTS §5）：帧数与时长取自 _frame（--fixed-fps 60 下帧数＝模拟秒），
//     不读墙钟、不读帧率。
//   - **随机口径**：遭遇事件保持生产开启（整局的一部分），迷雾随机事件按 §5 关闭（由 --fog-probe 专趟覆盖）；
//     本探针的判定（停摆、遥测）不依赖任何随机结果。
//   - 感知只用公开只读口：敌弹表（位置 + Direction/Speed 预测）、敌机表、GameState 遥测。
#if DEBUG || TOOLS
namespace InfiAir;

public partial class ProbeHost
{
    /// <summary>模拟游玩的默认时长（模拟秒）：8 分钟足够走过多档难度与多次遭遇。</summary>
    private const int PlayProbeDefaultSeconds = 480;

    /// <summary>遥测采样间隔（帧）：600 帧＝10 模拟秒。</summary>
    private const int PlayLogIntervalFrames = 600;

    /// <summary>避弹前瞻窗口（秒）：按候选落点与威胁在该时刻的预测位置打分。</summary>
    private const float PlayDodgeHorizon = 0.45f;

    /// <summary>避弹采样方向数（均匀取整圆方向，取最安全的一侧）。</summary>
    private const int PlayMoveSampleCount = 24;

    /// <summary>避弹安全半径（px）：弹心到本机落点的最小期望间距（已含弹与机身的半径余量）。</summary>
    private const float PlaySafeRadius = 34.0f;

    /// <summary>敌机安全半径（px）：撞机与接触伤害的规避距离。</summary>
    private const float PlayEnemySafeRadius = 96.0f;

    /// <summary>希望保持的纵向位置比例：0.72＝画面下方（纵向弹幕留出反应纵深）。</summary>
    private const float PlayPreferredBandRatio = 0.72f;

    /// <summary>停摆判定阈值（模拟秒）：存活且场上有敌机时，这么久没有任何得分/击杀变化即判停摆。</summary>
    private const int PlayStallSeconds = 60;

    /// <summary>弹反脉冲的触发距离（px）：威胁进入该圈就地弹反一次（脉冲式，非长按）。</summary>
    private const float PlayParryTriggerRadius = 78.0f;

    /// <summary>两次召唤母舰之间的最小间隔（模拟秒）：母舰是补弹匣/升火力的正常循环，隔一段叫一次。</summary>
    private const int PlayDockIntervalSeconds = 150;

    /// <summary>单次召唤的蓄力帧上限（mothership.dock_charge_time 3s = 180 帧，留 2 倍余量）。</summary>
    private const int PlayDockHoldFrames = 360;

    private static readonly StringName PlayActFire = new("fire");
    private static readonly StringName PlayActParry = new("parry");
    private static readonly StringName PlayActMoveLeft = new("move_left");
    private static readonly StringName PlayActMoveRight = new("move_right");
    private static readonly StringName PlayActMoveUp = new("move_up");
    private static readonly StringName PlayActMoveDown = new("move_down");

    private bool _playProbe;
    private int _playSeconds = PlayProbeDefaultSeconds;
    private int _playStage;
    private int _playStartFrame;
    private int _playLastLogFrame;
    private int _playLastProgressFrame;
    private int _playLastScore;
    private int _playLastKills;
    private int _playFireHeld;
    private int _playParryHold;
    private int _playMoveX; // -1/0/1：本帧已下发的横向输入（避免逐帧重复 ActionPress）
    private int _playMoveY;
    private bool _playDockHolding;
    private int _playDockHoldStart;
    private int _playLastDockFrame;
    private bool _playSawDeath;
    private bool _playStalled;
    private int _playDeathCount;
    private int _playSamples;
    private int _playMaxScore;
    private int _playMaxKills;
    private double _playMaxDifficulty;
    private bool _playSawBoss;
    private bool _playSawEncounter;
    private bool _playSawMothership;

    /// <summary>模拟游玩主循环：等生产链就绪 → 逐帧驱动 → 按模拟时长收尾（或停摆提前判失败）。</summary>
    private void TickPlayProbe()
    {
        if (_playStage == 0)
        {
            if (_frame < 30 || _player.IsEntryPlaying() || !_spawner.IsProcessing())
            {
                return;
            }

            _playStage = 1;
            _playStartFrame = _frame;
            _playLastLogFrame = _frame;
            _playLastProgressFrame = _frame;
            _playLastScore = GameState.Instance.Score;
            _playLastKills = GameState.Instance.Kills;
            GD.Print(GdFormat.Format("[play-probe] 模拟游玩开始（真实规则；预算 %ds）", _playSeconds));
        }

        if (!_player.IsDead())
        {
            TickPlayInput();
        }
        else if (!_playSawDeath)
        {
            _playSawDeath = true;
            _playDeathCount++;
            GD.Print(GdFormat.Format("[play-probe] 玩家死亡 t=%.1fs（必死曲线下的正常收场）",
                (_frame - _playStartFrame) / 60.0));
        }

        TrackPlayTelemetry();
        if (!_playSawDeath && _frame - _playLastProgressFrame > PlayStallSeconds * 60)
        {
            _playStalled = true;
            GD.PushError(GdFormat.Format(
                "[play-probe] 停摆：存活且在场敌机存在，但连续 %ds 既无得分也无击杀（刷怪/命中/结算疑似断链）",
                PlayStallSeconds));
        }

        if (_frame - _playLastLogFrame >= PlayLogIntervalFrames)
        {
            ReportPlayTelemetry();
        }

        if (_playStalled || _frame - _playStartFrame >= _playSeconds * 60)
        {
            FinishPlayProbe();
        }
    }

    /// <summary>逐帧驱动：移动（避弹）、瞄准（最近敌机）、开火（按住）、弹反（贴近脉冲）。</summary>
    private void TickPlayInput()
    {
        var pos = _player.GlobalPosition;
        var band = GameState.Instance.ViewWorldRect(0.0f);
        PlaySetMove(PlayChooseMoveDir(pos, band));

        var target = PlayNearestEnemy(pos);
        _player.AimPointOverride = target != null
            ? target.AimWorldPosition
            : new Vector2(pos.X, pos.Y - 100.0f);

        if (_playFireHeld == 0)
        {
            Input.ActionPress(PlayActFire);
            _playFireHeld = 1;
        }

        PlayPressPulse(PlayActParry, ref _playParryHold, PlayThreatDistance(pos) <= PlayParryTriggerRadius);
        TickPlayDock();
    }

    /// <summary>周期性召唤母舰（走生产蓄力链：长按 dock 到触发，资格由生产侧判）。持有期间照常避弹/开火。</summary>
    private void TickPlayDock()
    {
        if (_playDockHolding)
        {
            Input.ActionPress(ActDock);
            var arrived = _main.Mothership() != null;
            var timedOut = _frame - _playDockHoldStart > PlayDockHoldFrames;
            if (arrived || timedOut)
            {
                Input.ActionRelease(ActDock);
                _playDockHolding = false;
                _playLastDockFrame = _frame;
            }

            return;
        }

        var cooldownOk = _main.Mothership() == null && _main.DockCooldown() <= 0.0f;
        var encounterIdle = _events.ActiveId(_events.GROUP_ENCOUNTER).ToString().Length == 0;
        var due = _frame - _playLastDockFrame >= PlayDockIntervalSeconds * 60;
        if (cooldownOk && encounterIdle && due)
        {
            _playDockHolding = true;
            _playDockHoldStart = _frame;
            Input.ActionPress(ActDock); // 生产侧 Main._Process 读同一动作（蓄力 3s 触发召唤）
        }
    }

    /// <summary>候选方向采样避弹：对每个候选落点按「与威胁预测位置的间距」打分，取最安全且位置合适的一侧。</summary>
    private Vector2 PlayChooseMoveDir(Vector2 pos, Rect2 band)
    {
        var speed = (float)GameState.Instance.Cfg("player.max_speed", 420.0).AsDouble();
        var preferredY = band.Position.Y + (band.Size.Y * PlayPreferredBandRatio);
        var best = Vector2.Zero;
        var bestScore = float.NegativeInfinity;
        for (var i = 0; i < PlayMoveSampleCount; i++)
        {
            var angle = Mathf.Tau * i / PlayMoveSampleCount;
            var cand = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var landing = pos + (cand * speed * PlayDodgeHorizon);
            var score = -PlayThreatPenalty(landing);
            score -= Mathf.Abs(landing.Y - preferredY) / band.Size.Y;          // 回到偏爱的纵向带
            score -= Mathf.Abs(landing.X - band.GetCenter().X) / band.Size.X;  // 轻度居中，避免贴边
            if (!band.Grow(PlaySafeRadius).HasPoint(landing))
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

    /// <summary>威胁罚分：敌弹按 Direction/Speed 前推 PlayDodgeHorizon 后的位置、敌机按当前位置计。</summary>
    private float PlayThreatPenalty(Vector2 landing)
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

            var ahead = bullet.GlobalPosition + (bullet.Direction * (bullet.Speed * PlayDodgeHorizon));
            penalty += PlayRadiusPenalty(landing, ahead, PlaySafeRadius + Bullet.CollisionRadius, 4.0f);
        }

        var enemies = GameState.Instance.Enemies;
        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (!GodotObject.IsInstanceValid(enemy))
            {
                continue;
            }

            penalty += PlayRadiusPenalty(landing, enemy.GlobalPosition, PlayEnemySafeRadius, 1.0f);
        }

        return penalty;
    }

    private static float PlayRadiusPenalty(Vector2 landing, Vector2 threat, float safe, float weight)
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
    private float PlayThreatDistance(Vector2 pos)
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
    private IAimTarget? PlayNearestEnemy(Vector2 pos)
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
    private void PlaySetMove(Vector2 dir)
    {
        var x = dir.X > 0.35f ? 1 : (dir.X < -0.35f ? -1 : 0);
        var y = dir.Y > 0.35f ? 1 : (dir.Y < -0.35f ? -1 : 0);
        if (x != _playMoveX)
        {
            Input.ActionRelease(PlayActMoveLeft);
            Input.ActionRelease(PlayActMoveRight);
            if (x < 0)
            {
                Input.ActionPress(PlayActMoveLeft);
            }
            else if (x > 0)
            {
                Input.ActionPress(PlayActMoveRight);
            }

            _playMoveX = x;
        }

        if (y != _playMoveY)
        {
            Input.ActionRelease(PlayActMoveUp);
            Input.ActionRelease(PlayActMoveDown);
            if (y < 0)
            {
                Input.ActionPress(PlayActMoveUp);
            }
            else if (y > 0)
            {
                Input.ActionPress(PlayActMoveDown);
            }

            _playMoveY = y;
        }
    }

    /// <summary>按下-松手的二帧脉冲（just-pressed 语义的动作都走它；hold 非 0 表示脉冲未收尾）。</summary>
    private static void PlayPressPulse(StringName action, ref int hold, bool wanted)
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
    private void TrackPlayTelemetry()
    {
        var gs = GameState.Instance;
        _playMaxScore = Mathf.Max(_playMaxScore, gs.Score);
        _playMaxKills = Mathf.Max(_playMaxKills, gs.Kills);
        _playMaxDifficulty = Mathf.Max(_playMaxDifficulty, gs.DifficultyMultiplier);
        _playSawBoss |= _spawner.IsBossActive();
        _playSawEncounter |= _events.ActiveId(_events.GROUP_ENCOUNTER).ToString().Length > 0;
        _playSawMothership |= _main.Mothership() != null;

        if (gs.Score != _playLastScore || gs.Kills != _playLastKills)
        {
            _playLastScore = gs.Score;
            _playLastKills = gs.Kills;
            _playLastProgressFrame = _frame;
        }
    }

    /// <summary>遥测采样行（10 模拟秒一条；日志里能直接看出局势怎么走）。</summary>
    private void ReportPlayTelemetry()
    {
        _playLastLogFrame = _frame;
        _playSamples++;
        var gs = GameState.Instance;
        var mothership = _main.Mothership();
        GD.Print(GdFormat.Format(
            "[play-probe] t=%.1fs HP=%.0f/%.0f 分=%d 杀=%d 连击=%dx D=%.2f 敌=%d 敌弹=%d 事件=%s Boss=%s 母舰=%s",
            (_frame - _playStartFrame) / 60.0,
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

    /// <summary>收尾：释放所有注入的输入，打印汇总与完成标记（停摆则不打标记，让门禁/读者看得见失败）。</summary>
    private void FinishPlayProbe()
    {
        ReleasePlayInputs();
        var gs = GameState.Instance;
        var survived = (_frame - _playStartFrame) / 60.0;
        GD.Print(GdFormat.Format(
            "[play-probe] 汇总：模拟 %.1fs、采样 %d 次、峰值 分=%d 杀=%d D=%.2f、死亡 %d 次、"
            + "见过 Boss=%s 遭遇=%s 母舰=%s",
            survived, _playSamples, _playMaxScore, _playMaxKills, _playMaxDifficulty, _playDeathCount,
            _playSawBoss ? "是" : "否", _playSawEncounter ? "是" : "否", _playSawMothership ? "是" : "否"));

        if (_playStalled)
        {
            GD.Print("[play-probe] 模拟游玩中断（停摆，见上方 ::error）");
        }
        else
        {
            GD.Print("[play-probe] 模拟游玩完成");
        }

        _playProbe = false;
    }

    /// <summary>释放全部注入状态（探针结束/退出树都要收—否则动作残留会影响同进程的后续场景）。</summary>
    private void ReleasePlayInputs()
    {
        Input.ActionRelease(PlayActFire);
        Input.ActionRelease(PlayActParry);
        Input.ActionRelease(ActDock);
        Input.ActionRelease(PlayActMoveLeft);
        Input.ActionRelease(PlayActMoveRight);
        Input.ActionRelease(PlayActMoveUp);
        Input.ActionRelease(PlayActMoveDown);
        _playFireHeld = 0;
        _playParryHold = 0;
        _playDockHolding = false;
        _playMoveX = 0;
        _playMoveY = 0;
        if (_player != null && GodotObject.IsInstanceValid(_player))
        {
            _player.AimPointOverride = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        }
    }
}
#endif
