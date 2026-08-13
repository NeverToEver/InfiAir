using Godot;

namespace InfiAir.Tests;

/// <summary>AutoplayTest UI 交互（partial 拆分自 AutoplayTest.cs，纯搬移零行为变化）。</summary>
public partial class AutoplayTest : Node
{
    /// <summary>退出确认窗「打开→取消」探针：覆盖 BackNavigator CANCEL_EXIT 分支（暂停→退出游戏→取消）</summary>
    private void ProbeExitConfirm()
    {
        var main = _main!;
        var exitConfirm = main.GetNode<ExitConfirm>("ExitConfirm");
        var pauseUi = main.GetNode<PauseUi>("PauseUI");
        pauseUi.Open();
        pauseUi.Quit();
        if (!exitConfirm.Visible)
        {
            Anomaly("exit_confirm_no_show", "暂停→退出游戏未弹出确认窗");
            pauseUi.Close();
            return;
        }
        main.GetNode<BackNavigator>("BackNavigator").GoBack();  // 确认窗可见 → CANCEL_EXIT
        if (exitConfirm.Visible)
        {
            Anomaly("exit_confirm_stuck", "退出确认窗取消后仍可见");
            return;
        }
        pauseUi.Close();
        _exitProbes++;
        Log($"退出确认窗 打开→取消 探针通过（第 {_exitProbes} 次）");
    }

    private void HandleBuffUi(long now)
    {
        if (_buffUi == null || !IsInstanceValid(_buffUi))
        {
            return;
        }
        if (_buffUi.Visible)
        {
            if (_buffUi.Closing())
            {
                // 确认动效播放中：不重复 pick（动效结束才 visible=false）；动效期也纳入卡死计时
                if (_buffOpenSince == 0)
                {
                    _buffOpenSince = now;
                    _buffStuckReported = false;
                }
                return;
            }
            if (_buffOpenSince == 0)
            {
                _buffOpenSince = now;
                _buffStuckReported = false;
                _buffPickAt = now + 400 + (long)(GD.Randi() % 600);  // 模拟真人看牌时间
                var ids = new System.Collections.Generic.List<StringName>();
                foreach (var b in _buffUi.CurrentAvailable())
                {
                    ids.Add(b.AsGodotDictionary()["id"].AsStringName());
                }
                Log($"Buff 三选一弹出 candidates=[{string.Join(", ", ids)}]");
            }
            else if (now >= _buffPickAt)
            {
                var avail = _buffUi.CurrentAvailable();
                if (avail.Count > 0)
                {
                    // 优先选本进程尚未拥有过的种类，争取覆盖全部 buff 效果代码
                    var unseen = new Godot.Collections.Array();
                    foreach (var v in avail)
                    {
                        var b = v.AsGodotDictionary();
                        if (!_buffsSeen.ContainsKey(b["id"]))
                        {
                            unseen.Add(v);
                        }
                    }
                    var pool = unseen.Count > 0 ? unseen : avail;
                    var pick = pool[(int)(GD.Randi() % (uint)pool.Count)].AsGodotDictionary();
                    int pickIdx = -1;  // 候选卡在 _cards 中的索引（顺序与 _current_available 对应）
                    for (int i = 0; i < avail.Count; i++)
                    {
                        if (avail[i].AsGodotDictionary()["id"].AsString() == pick["id"].AsString())
                        {
                            pickIdx = i;
                            break;
                        }
                    }
                    var ev = new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left };
                    // 10% 走真实三参确认动效路径：合成鼠标左键事件发给目标卡片
                    // （card.gui_input → _on_card_gui_input 的 card!=null 分支 → _closing 动效，
                    // ~200ms 后 _on_pick_close_finished 才关闭面板并恢复对局）；其余走两参立即关闭
                    Control? card = null;
                    bool animated = false;
                    if (GD.Randf() < 0.10f && pickIdx >= 0 && _buffUi.Cards().GetChildCount() > pickIdx)
                    {
                        card = _buffUi.Cards().GetChild(pickIdx) as Control;
                    }
                    if (card != null)
                    {
                        card.EmitSignal(Control.SignalName.GuiInput, ev);
                        animated = true;
                    }
                    else
                    {
                        _buffUi.PickBuff(pick["id"].AsStringName());
                    }
                    _buffPicks++;
                    if (animated)
                    {
                        _buffAnimatedPicks++;
                    }
                    _buffsSeen[pick["id"]] = true;
                    // 选取后立即校验层数上限（口径同 BuffSelect.cs：cfg 覆盖池内默认）
                    int poolMax = 1;
                    if (BuffPoolMax.ContainsKey(pick["id"]))
                    {
                        poolMax = BuffPoolMax[pick["id"]].AsInt32();
                    }
                    int cap = _gs.Cfg($"buffs.{pick["id"].AsStringName()}.max_stacks", poolMax).AsInt32();
                    if (_gs.BuffCount(pick["id"].AsStringName()) > cap)
                    {
                        AnomalyRl("buff_over_cap", $"Buff {pick["id"].AsStringName()} 层数 {_gs.BuffCount(pick["id"].AsStringName())} 超过上限 {cap}", now);
                    }
                    Log($"Buff 选择: {pick["id"].AsStringName()}（层数 {_gs.BuffCount(pick["id"].AsStringName())}，已覆盖种类 {_buffsSeen.Count}/{BuffPoolSize}" + (animated ? "，动效路径" : "") + "）");
                }
                _buffOpenSince = 0;
            }
        }
        else
        {
            _buffOpenSince = 0;
        }
    }

    /// <summary>基地控制台全模块：维修 → 补给燃料 → 天赋路线选择 → 任务领奖 → 继续出击</summary>
    private void HandleBaseUi(long now)
    {
        if (_main == null || !IsInstanceValid(_main))
        {
            return;
        }
        var baseUi = _main.BaseUi();
        if (baseUi.Visible)
        {
            if (_baseSince == 0)
            {
                _baseSince = now;
                _baseStage = 0;
                _baseStuckReported = false;
                Log($"进入基地整备（RP={_gs.Rp} HP={_gs.Health:0}）");
                return;
            }
            long t = now - _baseSince;
            if (_baseStage == 0 && t >= 600)
            {
                _baseStage = 1;
                if (_gs.Rp >= _gs.RP_REPAIR_COST && _gs.Health < _gs.MaxHealth())
                {
                    baseUi.Repair();
                    _baseRepairs++;
                    Log($"基地维修：HP -> {_gs.Health:0}（RP={_gs.Rp}）");
                }
            }
            else if (_baseStage == 1 && t >= 1000)
            {
                _baseStage = 2;
                if (_gs.Rp >= _gs.RP_RECHARGE_COST && _player != null && _player.FuelAmount() < _player.FuelMax - 1.0f)
                {
                    baseUi.Recharge();
                    _baseRecharges++;
                    Log($"基地补给燃料：-> {_player.FuelAmount():0}（RP={_gs.Rp}）");
                }
            }
            else if (_baseStage == 2 && t >= 1400)
            {
                _baseStage = 3;
                foreach (var kv in _gs.ROUTE_LINES)
                {
                    var line = kv.Key.AsStringName();
                    if (_gs.ChosenRoutes.ContainsKey(line))
                    {
                        continue;  // 每线每局限选一次
                    }
                    var options = _gs.ROUTE_LINES[line].AsGodotArray();
                    int total = _gs.BuffCount(options[0].AsStringName()) + _gs.BuffCount(options[1].AsStringName());
                    if (total == 0)
                    {
                        continue;
                    }
                    var opt = options[(int)(GD.Randi() % (uint)options.Count)].AsStringName();
                    if (_gs.IsBuffLocked(opt))
                    {
                        opt = options[1].AsStringName() == opt ? options[0].AsStringName() : options[1].AsStringName();
                    }
                    if (_gs.IsBuffLocked(opt))
                    {
                        continue;
                    }
                    baseUi.ChooseRoute(line, opt);
                    _routeChoices++;
                    Log($"天赋路线选择：{line} -> {opt}（合并后 {_gs.BuffCount(opt)} 层）");
                }
            }
            else if (_baseStage == 3 && t >= 1800)
            {
                _baseStage = 4;
                // 任务轮换：领取在场已完成任务（active_mission_ids，非固定 MISSION_DEFS）
                foreach (var id in _gs.ActiveMissionIds())
                {
                    if (_gs.IsMissionDone(id) && !_gs.IsMissionClaimed(id))
                    {
                        baseUi.ClaimMission(id);
                        _missionClaims++;
                        Log($"领取任务奖励：{id}（RP={_gs.Rp}）");
                    }
                }
            }
            else if (_baseStage == 4 && t >= 2600)
            {
                baseUi.Resume();
                Log("继续出击，返回对局");
                _baseSince = 0;
            }
        }
        else
        {
            _baseSince = 0;
        }
    }

    /// <summary>随机暂停：Esc 打开暂停菜单（走 BackNavigator 真实路由）</summary>
    private void UpdatePause(long now)
    {
        if (now < _nextPauseConsider)
        {
            return;
        }
        _nextPauseConsider = now + PauseGapMs + (long)(GD.Randi() % 20000);
        if (GD.Randf() < 0.6f)
        {
            var main = _main!;
            main.GetNode<BackNavigator>("BackNavigator").GoBack();  // 战斗中 → OPEN_PAUSE
            if (main.PauseUi().Visible)
            {
                _pauseOpenSince = now;
                _pauseStage = 0;
                Log("暂停：Esc 打开暂停菜单");
            }
        }
    }

    /// <summary>暂停菜单链路：「保存进度」写档 →（50% 打开设置页再返回）→ 恢复对局（或重进 main 走自动读档）</summary>
    private void HandlePauseUi(long now)
    {
        if (_pauseOpenSince == 0)
        {
            return;
        }
        var main = _main!;
        var pauseUi = main.PauseUi();
        var settingsUi = main.GetNode<SettingsUi>("SettingsUI");
        if (!pauseUi.Visible && !settingsUi.Visible)
        {
            _pauseOpenSince = 0;  // 未打开成功或已被其他路径关闭
            return;
        }
        long t = now - _pauseOpenSince;
        if (_pauseStage == 0 && t >= 600)
        {
            _pauseStage = 1;
            if (GD.Randf() < 0.75f)
            {
                pauseUi.Save();
                _pauseSaves++;
                Log($"暂停菜单：保存进度（第 {_pauseSaves} 次）");
            }
        }
        else if (_pauseStage == 1 && t >= 1300)
        {
            // 50% 打开设置页（pause_ui 隐藏、SettingsUI 显示），随后 go_back 走
            // BackNavigator CLOSE_SETTINGS 真实路由返回（opener 恢复可见）
            if (GD.Randf() < 0.5f)
            {
                pauseUi.OpenSettings();
                if (settingsUi.Visible)
                {
                    _settingsOpenSince = now;
                    _pauseStage = 2;
                    Log("暂停菜单：打开设置页");
                }
                else
                {
                    _pauseStage = 3;
                }
            }
            else
            {
                _pauseStage = 3;
            }
        }
        else if (_pauseStage == 2 && now - _settingsOpenSince >= 900)
        {
            _pauseStage = 3;
            _settingsOpens++;
            main.GetNode<BackNavigator>("BackNavigator").GoBack();  // 设置页 → CLOSE_SETTINGS
            Log($"暂停菜单：设置页返回（第 {_settingsOpens} 次）");
        }
        else if (_pauseStage == 3 && t >= 2400)
        {
            _pauseStage = 4;
            _pauseOpenSince = 0;
            if (GD.Randf() < 0.35f && _gs.HasSave())
            {
                // 重进 main → 启动自动读档恢复（与新游戏不同的代码路径）
                pauseUi.Close();
                _menuReturnAt = now + MenuReturnDelayMs;
                Log("暂停恢复，稍后重进 main 走自动读档继续");
            }
            else
            {
                main.GetNode<BackNavigator>("BackNavigator").GoBack();  // 暂停中 → RESUME_GAME
                Log("暂停：Esc 恢复对局");
            }
        }
    }

    /// <summary>对局中轮换设置项（视角/窗口/语言/难度），数秒后切回：压测热路径上的信号处理器</summary>
    private void UpdateSettings(long now)
    {
        if (_settingRestoreAt > 0)
        {
            if (now >= _settingRestoreAt)
            {
                var restoreKind = _settingRestore["kind"].AsStringName();
                var restoreOld = _settingRestore["old"];
                ApplySetting(restoreKind, restoreOld);
                Log($"设置切回：{restoreKind} -> {Str(restoreOld)}");
                _settingRestoreAt = 0;
                _settingRestore = new Godot.Collections.Dictionary();
            }
            return;
        }
        if (now < _nextSettingAt)
        {
            return;
        }
        _nextSettingAt = now + SettingGapMs + (long)(GD.Randi() % 15000);
        var kind = SettingKinds[(int)(GD.Randi() % (uint)SettingKinds.Count)];
        Variant old;
        Variant newVal;
        switch (kind.ToString())
        {
            case "view_zoom":
                old = _gs.ViewZoom;
                newVal = PickOther(ToUntyped(new Godot.Collections.Array<Variant>(_gs.VIEW_ZOOM_LEVELS.Keys)), old);
                break;
            case "window_size":
                old = _gs.WindowSize;
                newVal = PickOther(ToUntyped(new Godot.Collections.Array<Variant>(_gs.WINDOW_SIZE_LEVELS.Keys)), old);
                break;
            case "locale":
                old = _gs.Locale;
                newVal = old.AsString() == "zh" ? "en" : "zh";
                break;
            case "difficulty":
                old = _gs.Difficulty;
                newVal = PickOther(ToUntyped(_gs.DIFFICULTY_ORDER), old);
                break;
            case "aim_assist":
                old = _gs.AimAssistLevel;
                newVal = PickOther(ToUntyped(_gs.AIM_ASSIST_ORDER), old);
                break;
            case "reduce_flash":
                old = _gs.ReduceFlash;
                newVal = !old.AsBool();
                break;
            case "ctrl_toggle":
                old = _gs.CtrlToggleMode;
                newVal = !old.AsBool();
                break;
            case "shift_toggle":
                old = _gs.ShiftToggleMode;
                newVal = !old.AsBool();
                break;
            default:
                return;  // 不可达（kind 恒来自 SETTING_KINDS）
        }
        if (newVal.VariantType == Variant.Type.Nil || newVal.Equals(old))
        {
            return;
        }
        _settingRestore = new Godot.Collections.Dictionary { ["kind"] = kind, ["old"] = old };
        _settingRestoreAt = now + SettingRestoreMs;
        _settingSwitches++;
        ApplySetting(kind, newVal);
        Log($"设置切换：{kind} {Str(old)} -> {Str(newVal)}（{SettingRestoreMs}ms 后切回）");
    }

    private static Variant PickOther(Godot.Collections.Array options, Variant current)
    {
        var others = new Godot.Collections.Array();
        foreach (var o in options)
        {
            if (!o.Equals(current))
            {
                others.Add(o);
            }
        }
        if (others.Count == 0)
        {
            return default;
        }
        return others[(int)(GD.Randi() % (uint)others.Count)];
    }

    private static Godot.Collections.Array ToUntyped(Godot.Collections.Array<Variant> items)
    {
        var result = new Godot.Collections.Array();
        foreach (var v in items)
        {
            result.Add(v);
        }
        return result;
    }

    private static Godot.Collections.Array ToUntyped(Godot.Collections.Array<StringName> items)
    {
        var result = new Godot.Collections.Array();
        foreach (var s in items)
        {
            result.Add(s);
        }
        return result;
    }

    private void ApplySetting(StringName kind, Variant value)
    {
        switch (kind.ToString())
        {
            case "view_zoom":
                _gs.SetViewZoom(value.AsStringName());
                break;
            case "window_size":
                _gs.SetWindowSize(value.AsStringName());
                break;
            case "locale":
                _gs.SetLocale(value.AsString());
                break;
            case "difficulty":
                _gs.SetDifficulty(value.AsStringName());
                break;
            case "aim_assist":
                _gs.SetAimAssistLevel(value.AsStringName());
                break;
            case "reduce_flash":
                _gs.SetReduceFlash(value.AsBool());
                break;
            case "ctrl_toggle":
                _gs.SetCtrlToggleMode(value.AsBool());
                break;
            case "shift_toggle":
                _gs.SetShiftToggleMode(value.AsBool());
                break;
        }
    }

}
