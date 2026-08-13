using Godot;

namespace InfiAir.Tests;

/// <summary>AutoplayTest 快照与不变量检查（partial 拆分自 AutoplayTest.cs，纯搬移零行为变化）。</summary>
public partial class AutoplayTest : Node
{
    /// <summary>母舰状态变化日志 + 卡死 episode 跟踪</summary>
    private void TrackMothership(long now)
    {
        var ms = (_main != null && IsInstanceValid(_main)) ? _main.Mothership() : null;
        int state = ms == null ? -1 : (int)ms.GetState();
        if (state != _msLastState)
        {
            if (_msLastState >= 0 || state >= 0)
            {
                string fromS = _msLastState < 0 ? "NONE" : MsStateNames[_msLastState];
                string toS = state < 0 ? "NONE" : MsStateNames[state];
                Log($"母舰状态 {fromS} -> {toS}");
            }
            if (_msLastState == (int)Mothership.State.STAY && state == (int)Mothership.State.RELEASE)
            {
                if (_stayUntilEject && !_earlyHolding)
                {
                    _forcedEjects++;
                    Log($"驻留超时强制弹射（第 {_forcedEjects} 次）");
                }
                _stayUntilEject = false;
            }
            _msLastState = state;
            _msStateSince = now;
            _msStuckReported = false;
        }
        else if (state >= 0 && !_msStuckReported)
        {
            if (state < MsStateTimeouts.Length && now - _msStateSince > MsStateTimeouts[state])
            {
                _msStuckReported = true;
                Anomaly("mothership_stuck", $"母舰状态 {MsStateNames[state]} 超过 {MsStateTimeouts[state] / 1000}s 未推进");
            }
        }
    }

    // ---------------- 快照与不变量检查 ----------------

    /// <summary>单次遍历 Main 直接子节点：统计玩家/敌弹数，并检测死亡回放演出节点。</summary>
    private static void ScanMain(Main main, out int pBullets, out int eBullets, out Node? replayNode)
    {
        pBullets = 0;
        eBullets = 0;
        replayNode = null;
        foreach (var child in main.GetChildren())
        {
            if (child is Bullet b)
            {
                if (b.IsPlayerBullet)
                {
                    pBullets++;
                }
                else
                {
                    eBullets++;
                }
            }
            else if (child is DeathReplayPlayer)
            {
                replayNode = child;
            }
        }
    }

    private void Snapshot(long now)
    {
        var main = _main!;
        ScanMain(main, out int pBullets, out int eBullets, out _);
        int mainNodes = main.GetChildCount();
        int totalNodes = CountNodes(GetTree().Root);
        _maxNodes = Mathf.Max(_maxNodes, totalNodes);
        _maxEnemyBullets = Mathf.Max(_maxEnemyBullets, eBullets);
        _maxPlayerBullets = Mathf.Max(_maxPlayerBullets, pBullets);
        _maxEnemies = Mathf.Max(_maxEnemies, _gs.Enemies.Count);
        // 引擎级监控器（GDScript Performance.OBJECT_COUNT 常量 → C# Performance.Monitor 枚举）
        double objCount = Performance.GetMonitor(Performance.Monitor.ObjectCount);
        double nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        double orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        double resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        double memStatic = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        double fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        _maxOrphans = Mathf.Max(_maxOrphans, orphans);
        _maxResources = Mathf.Max(_maxResources, resources);
        // 池规模
        int bulletPoolN = -1;
        int enemyPoolN = -1;
        var bulletPool = _gs.BulletPool as Node;
        if (bulletPool != null && IsInstanceValid(bulletPool))
        {
            bulletPoolN = bulletPool.GetChildCount();
            _maxBulletPool = Mathf.Max(_maxBulletPool, bulletPoolN);
        }
        var enemyPool = _gs.EnemyPool as Node;
        if (enemyPool != null && IsInstanceValid(enemyPool))
        {
            enemyPoolN = enemyPool.GetChildCount();
            _maxEnemyPool = Mathf.Max(_maxEnemyPool, enemyPoolN);
        }
        // 帧耗时（真实 ms/帧，含 time_scale=2 的放大效应）
        double frameMs = 0.0;
        int frames = _frameCount;
        _frameCount = 0;
        if (frames > 0)
        {
            frameMs = (double)(now - _frameSnapMsec) / frames;
        }
        _frameSnapMsec = now;
        _maxFrameMs = Mathf.Max(_maxFrameMs, frameMs);
        string bossS = "none";
        if (_boss != null && IsInstanceValid(_boss))
        {
            bossS = $"type{_boss.BossType} hp={_boss.Hp:0}/{_boss.MaxHp:0}" + (_boss.IsEnraged() ? "(enraged)" : "");
        }
        var ms = main.Mothership();
        string msS = ms == null ? "none" : MsStateNames[(int)ms.GetState()];
        string ddaS = _gs.DdaActive() ? "on" : "-";  // B 梯队：DDA 降档状态
        Log(
            $"SNAP run={_runIndex} t_game={_gs.RunTime:0}s score={_gs.Score} hp={_gs.Health:0}/{_gs.MaxHealth():0} kills={_gs.Kills} enemies={_gs.Enemies.Count} "
            + $"bullets(p={pBullets},e={eBullets}) boss={bossS} ms={msS} dda={ddaS} diff={_gs.DifficultyMultiplier:0.00} elapsed={_spawner!.Elapsed():0}s "
            + $"nodes(main={mainNodes},total={totalNodes}) ts={Engine.TimeScale:0.00} paused={BoolStr(GetTree().Paused)} "
            + $"perf(obj={objCount:0},nodes={nodeCount:0},orphan={orphans:0},res={resources:0},mem={memStatic / 1048576.0:0.0}MB,fps={fps:0},fms={frameMs:0.00}) pool(b={bulletPoolN},e={enemyPoolN})"
        );
        // 孤儿节点：任何非零值都是泄漏信号（比节点直方图更灵敏）
        if (orphans > 0.0)
        {
            AnomalyRl("orphan_nodes", $"孤儿节点数 {orphans:0}", now);
        }
        // 池规模上界
        if (bulletPoolN > MaxBulletPool)
        {
            AnomalyRl("pool_growth", $"子弹池闲置实例 {bulletPoolN} 超过 {MaxBulletPool}", now);
        }
        if (enemyPoolN > MaxEnemyPool)
        {
            AnomalyRl("pool_growth", $"敌机池闲置实例 {enemyPoolN} 超过 {MaxEnemyPool}", now);
        }
        // 节点泄漏趋势：连续 3 个快照上涨且超过基线 3 倍
        if (_nodeBaseline == 0)
        {
            _nodeBaseline = totalNodes;
            _nodePrev = totalNodes;
            _objBaseline = objCount;
            _objPrev = objCount;
            return;
        }
        if (totalNodes > _nodePrev)
        {
            _nodeRiseStreak++;
        }
        else
        {
            _nodeRiseStreak = 0;
        }
        _nodePrev = totalNodes;
        if (totalNodes < _nodeBaseline * 2)
        {
            _nodeLeakArmed = true;
        }
        if (_nodeLeakArmed && _nodeRiseStreak >= 3 && totalNodes > _nodeBaseline * 3)
        {
            _nodeLeakArmed = false;
            Anomaly("node_leak", $"节点数连续上涨 {_nodeBaseline} -> {totalNodes}（基线 {_nodeBaseline}）");
            DumpNodeHistogram();
        }
        // 对象数泄漏趋势（池复用失效时对象只进不出，比节点数更早显现）
        if (objCount > _objPrev)
        {
            _objRiseStreak++;
        }
        else
        {
            _objRiseStreak = 0;
        }
        _objPrev = objCount;
        if (objCount < _objBaseline * 1.3)
        {
            _objLeakArmed = true;
        }
        if (_objLeakArmed && _objRiseStreak >= ObjectLeakStreak && objCount > _objBaseline * ObjectLeakRatio)
        {
            _objLeakArmed = false;
            Anomaly("object_leak", $"对象数连续上涨 {_objBaseline:0} -> {objCount:0}（基线 {_objBaseline:0}）");
            DumpNodeHistogram();
        }
        // 帧耗时恶化：前 5 个快照取最小值作基线（避免初始恰逢演出/弹幕高峰抬高基线
        // 而掩盖真实悬崖），持续 3 倍即报（难度升高后的性能悬崖）
        if (frameMs > 0.0)
        {
            _frameSnaps++;
            if (_frameSnaps <= 5)
            {
                _frameMsBaseline = _frameMsBaseline <= 0.0 ? frameMs : Mathf.Min(_frameMsBaseline, frameMs);
            }
            else if (_frameMsBaseline > 0.0 && frameMs > _frameMsBaseline * 3.0)
            {
                _frameSlowStreak++;
                if (_frameSlowStreak >= 3)
                {
                    _frameSlowStreak = 0;
                    Anomaly("frame_time", $"帧耗时恶化 {frameMs:0.00}ms（基线 {_frameMsBaseline:0.00}ms，enemies={_gs.Enemies.Count} bullets={pBullets + eBullets}）");
                }
            }
            else
            {
                _frameSlowStreak = 0;
            }
        }
    }

    /// <summary>节点泄漏诊断：打印 Main 子树及根节点的直接子节点类型分布</summary>
    private void DumpNodeHistogram()
    {
        var main = _main!;
        Log("--- 节点直方图（泄漏诊断） ---");
        HistogramLine(main, "  ");
        foreach (var child in main.GetChildren())
        {
            if (child.GetChildCount() > 3)
            {
                HistogramLine(child, "    ");
            }
        }
    }

    private void HistogramLine(Node node, string indent)
    {
        var byClass = new Godot.Collections.Dictionary();
        foreach (var child in node.GetChildren())
        {
            string cls = child.GetClass();
            var script = child.GetScript();
            if (script.VariantType != Variant.Type.Nil && script.AsGodotObject() is Script s)
            {
                cls = s.ResourcePath.GetFile();
            }
            byClass[cls] = byClass.GetValueOrDefault(cls, 0).AsInt32() + 1;
        }
        var parts = new System.Collections.Generic.List<string>();
        foreach (var kv in byClass)
        {
            parts.Add($"{kv.Key}: {kv.Value.AsInt32()}");
        }
        Log($"{indent}{node.Name} <{node.GetClass()}> children={node.GetChildCount()} {{{string.Join(", ", parts)}}}");
    }

    /// <summary>注册表差集诊断用：节点类名（有脚本的取脚本文件名，同 _histogram_line 口径）</summary>
    private static string ClassLabel(GodotObject obj)
    {
        if (obj is Node n)
        {
            var script = n.GetScript();
            if (script.VariantType != Variant.Type.Nil && script.AsGodotObject() is Script s)
            {
                return s.ResourcePath.GetFile().GetBaseName();
            }
        }
        return obj.GetClass();
    }

    /// <summary>「类名×n, ...」格式化（注册表差集消息明细）</summary>
    private static string FmtClassCounts(Godot.Collections.Dictionary counts)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var kv in counts)
        {
            parts.Add($"{kv.Key}×{kv.Value.AsInt32()}");
        }
        return string.Join(", ", parts);
    }

    private static int CountNodes(Node root)
    {
        int n = 1;
        foreach (var child in root.GetChildren())
        {
            n += CountNodes(child);
        }
        return n;
    }

    private void Checks(long now)
    {
        var main = _main!;
        // 数值越界
        if (_gs.Health < -0.01 || _gs.Health > _gs.MaxHealth() + 0.01)
        {
            AnomalyRl("hp_bounds", $"HP 越界 {_gs.Health:0.00}（上限 {_gs.MaxHealth():0.00}）", now);
        }
        if (_gs.Score < 0)
        {
            AnomalyRl("negative_score", $"分数为负 {_gs.Score}", now);
        }
        // 实体爆增
        ScanMain(main, out int pBullets, out int eBullets, out Node? replayFound);
        if (pBullets > MaxPlayerBullets)
        {
            AnomalyRl("entity_explosion", $"玩家子弹数 {pBullets} 超过 {MaxPlayerBullets}", now);
        }
        if (eBullets > MaxEnemyBullets)
        {
            AnomalyRl("entity_explosion", $"敌方子弹数 {eBullets} 超过 {MaxEnemyBullets}", now);
        }
        if (_gs.Enemies.Count > MaxEnemies)
        {
            AnomalyRl("entity_explosion", $"敌机注册数 {_gs.Enemies.Count} 超过 {MaxEnemies}", now);
        }
        // 注册表一致性：enemy 组集合与注册表双向差集比对
        // （四类注册者 Enemy/Boss/TurretBattery/FormationCraft 组语义与注册表一致；
        // 两侧都跳过 _active==false 的池化 Enemy——deactivate 同步注销、deferred reparent 亚帧窗口）
        var sceneSet = new Godot.Collections.Dictionary();  // Node -> true
        foreach (var n in GetTree().GetNodesInGroup("enemy"))
        {
            var node = n as Node;
            if (node == null || !main.IsAncestorOf(node))
            {
                continue;
            }
            var en = node as Enemy;
            if (en != null && !en.IsActive())
            {
                continue;
            }
            sceneSet[node] = true;
        }
        var registrySet = new Godot.Collections.Dictionary();  // 有效实例 -> true
        bool staleFound = false;
        foreach (var e in _gs.Enemies)
        {
            if (!IsInstanceValid(e))
            {
                staleFound = true;
                continue;  // 失效实例归 registry_stale 管，不参与差集
            }
            var re = e as Enemy;
            if (re != null && !re.IsActive())
            {
                continue;
            }
            registrySet[e] = true;
        }
        if (staleFound)
        {
            AnomalyRl("registry_stale", "GameState.enemies 含失效实例", now);
        }
        var regExtra = new Godot.Collections.Dictionary();  // 类名 -> 计数
        foreach (var kv in registrySet)
        {
            if (!sceneSet.ContainsKey(kv.Key))
            {
                string k = ClassLabel(kv.Key.AsGodotObject());
                regExtra[k] = regExtra.GetValueOrDefault(k, 0).AsInt32() + 1;
            }
        }
        if (regExtra.Count > 0)
        {
            AnomalyRl("registry_mismatch", $"注册表多出: {FmtClassCounts(regExtra)}（注册表 {registrySet.Count} vs 场景 {sceneSet.Count}）", now);
        }
        var sceneExtra = new Godot.Collections.Dictionary();
        foreach (var kv in sceneSet)
        {
            if (!registrySet.ContainsKey(kv.Key))
            {
                string k = ClassLabel(kv.Key.AsGodotObject());
                sceneExtra[k] = sceneExtra.GetValueOrDefault(k, 0).AsInt32() + 1;
            }
        }
        if (sceneExtra.Count > 0)
        {
            AnomalyRl("registry_mismatch", $"场景多出: {FmtClassCounts(sceneExtra)}（注册表 {registrySet.Count} vs 场景 {sceneSet.Count}）", now);
        }
        // 引用有效性：player_ref / 对象池
        if (_player != null && IsInstanceValid(_player) && _gs.PlayerRef != _player)
        {
            AnomalyRl("player_ref_mismatch", "GameState.player_ref 未指向当前玩家", now);
        }
        var bulletPool = _gs.BulletPool as Node;
        if (bulletPool == null || !IsInstanceValid(bulletPool))
        {
            AnomalyRl("pool_ref_invalid", "GameState.bullet_pool 引用失效", now);
        }
        else if (bulletPool.GetParent() != main)
        {
            AnomalyRl("pool_ref_invalid", "GameState.bullet_pool 父节点不是当前 Main（残留旧对局池）", now);
        }
        var enemyPool = _gs.EnemyPool as Node;
        if (enemyPool == null || !IsInstanceValid(enemyPool))
        {
            AnomalyRl("pool_ref_invalid", "GameState.enemy_pool 引用失效", now);
        }
        else if (enemyPool.GetParent() != main)
        {
            AnomalyRl("pool_ref_invalid", "GameState.enemy_pool 父节点不是当前 Main（残留旧对局池）", now);
        }
        // Buff UI 卡死
        if (_buffUi != null && IsInstanceValid(_buffUi) && _buffUi.Visible)
        {
            if (_buffOpenSince > 0 && now - _buffOpenSince > BuffStuckMs && !_buffStuckReported)
            {
                _buffStuckReported = true;
                Anomaly("buff_ui_stuck", $"Buff UI 可见超过 {BuffStuckMs / 1000}s 未关闭");
            }
        }
        // Boss 超时
        if (_boss != null && IsInstanceValid(_boss) && !_bossTimeoutReported)
        {
            if (now - _bossSince > BossTimeoutMs)
            {
                _bossTimeoutReported = true;
                Anomaly("boss_timeout", $"Boss type={_boss.BossType} 在场超过 {BossTimeoutMs / 1000}s");
            }
        }
        // 返航/基地卡死（返航过场播放期不计时：过场真实时长可达数十秒，计时起点顺延到结束）
        if (main.IsHomecoming())
        {
            if (main.ReturnCinematic() != null)
            {
                _homecomingPendingSinceMs = now;
                _homeStuckReported = false;
            }
            else if (_homecomingPendingSinceMs == 0)
            {
                _homecomingPendingSinceMs = now;
                _homeStuckReported = false;
            }
            else if (!_homeStuckReported && !main.BaseUi().Visible && now - _homecomingPendingSinceMs > HomeStuckMs)
            {
                _homeStuckReported = true;
                Anomaly("homecoming_stuck", $"返航过场结束 {HomeStuckMs / 1000}s 后基地 UI 仍未显示");
            }
        }
        else
        {
            _homecomingPendingSinceMs = 0;
        }
        if (main.BaseUi().Visible && _baseSince > 0 && now - _baseSince > BaseStuckMs && !_baseStuckReported)
        {
            _baseStuckReported = true;
            Anomaly("base_ui_stuck", $"基地 UI 可见超过 {BaseStuckMs / 1000}s 未关闭");
        }
        // 狂暴减速残留：玩家仍减速但无狂暴 Boss（Boss 离场/死亡后未复位），持续 15s episode 报一次
        bool bossEnraged = _boss != null && IsInstanceValid(_boss) && _boss.IsEnraged();
        if (_player != null && IsInstanceValid(_player) && Mathf.Abs(_player.EnrageSlow() - 1.0f) > 0.001f && !bossEnraged)
        {
            if (_slowSince == 0)
            {
                _slowSince = now;
            }
            else if (!_slowReported && now - _slowSince > SlowStuckMs)
            {
                _slowReported = true;
                Anomaly("enrage_slow_stuck", $"玩家狂暴减速 {_player.EnrageSlow():0.00} 持续 {SlowStuckMs / 1000}s 但无狂暴 Boss");
            }
        }
        else
        {
            _slowSince = 0;
            _slowReported = false;
        }
        // 弹反有效窗观察：ACTIVE 跃迁各 +1（覆盖统计口径，确认 parry 探针真实进入判定窗）
        bool parryActive = _player != null && IsInstanceValid(_player) && _player.ParryPhase() == PlayerParry.GetPhaseActive();
        if (parryActive && !_parryWasActive)
        {
            _parryActiveSeen++;
        }
        _parryWasActive = parryActive;
        // 事件触发计数：非活跃 -> 活跃跃迁各 +1（500ms 轮询事件状态机）
        var elite = main.Event() as EliteTurretEvent;
        bool turretActive = elite != null && elite.GetState() != EliteTurretEvent.State.IDLE;
        if (turretActive && !_eventWasActive)
        {
            _turretEventCount++;
            Log($"精英炮塔事件触发（第 {_turretEventCount} 次）");
        }
        _eventWasActive = turretActive;
        var formation = main.Formation() as FormationStrikeEvent;
        bool formationActive = formation != null && formation.GetState() != FormationStrikeEvent.State.IDLE;
        if (formationActive && !_formationWasActive)
        {
            _formationEventCount++;
            Log($"轰炸编队事件触发（第 {_formationEventCount} 次）");
        }
        _formationWasActive = formationActive;
        // B 梯队：DDA 降档卡死——无受击超时仍激活（受击刷新计时，持续受击不算；恢复后复位）
        if (_gs.DdaActive())
        {
            // 2026-08-13：暂停期 DDA 计时按设计冻结（RunProgressionService._Process 随树暂停），
            // 返航过场/基地整备期按真实时间判定必误报；狂暴子弹时间（ts=0.24）下计时按 delta
            // 慢速耗尽，真实 9s 阈值同样必误报——仅在正常时间流且未暂停时判定
            if (!GetTree().Paused && main.BulletTime() <= 0.0f && main.TimeScaleRamp() < 0.0f
                && _lastDamagedMsec >= 0 && now - _lastDamagedMsec > DdaStuckMs && !_ddaStuckReported)
            {
                _ddaStuckReported = true;
                Anomaly("dda_stuck", $"DDA 降档激活超 {DdaStuckMs / 1000}s 无受击（未按时恢复）");
            }
        }
        else
        {
            _ddaStuckReported = false;
        }
        // B 梯队：死亡回放演出节点跟踪——出现即计时，超时未自毁 = 泄漏（3s 播完、5s 兜底）
        if (replayFound != null)
        {
            if (_replayNode != replayFound)
            {
                _replayNode = replayFound;
                _replaySince = now;
                _replaySeenCount++;
                _replayStuckReported = false;
            }
            else if (!_replayStuckReported && now - _replaySince > ReplayStuckMs)
            {
                _replayStuckReported = true;
                Anomaly("replay_stuck", $"死亡回放演出节点存活超 {ReplayStuckMs / 1000}s（未自毁）");
            }
        }
        else if (_replayNode != null)
        {
            _replayNode = null;
            _replaySince = 0;
        }
        // Phase 0 L13：母舰×事件互斥——母舰在场期精英炮塔/编队事件不得触发
        // （can_trigger 组查询互斥；探针交叉验证事件状态机与母舰在场）
        var ms = main.Mothership();
        if (ms != null && IsInstanceValid(ms) && (turretActive || formationActive))
        {
            AnomalyRl("ms_event_mutex", $"母舰在场期事件触发（elite={BoolStr(turretActive)} formation={BoolStr(formationActive)}）", now);
        }
        // 轨道打击监控：基地 Resume 后触发清场动画，应出现并在合理时间内自毁
        var strike = main.Strike();
        if (strike != null)
        {
            if (!_strikeWasActive)
            {
                _strikeCount++;
                _strikeSince = now;
                Log($"轨道打击触发（第 {_strikeCount} 次）");
            }
            else if (now - _strikeSince > StrikeStuckMs)
            {
                AnomalyRl("strike_stuck", $"轨道打击动画存活超 {StrikeStuckMs / 1000}s 未自毁", now);
            }
            _strikeWasActive = true;
        }
        else
        {
            _strikeWasActive = false;
        }
        // 迷雾事件监控：fog 组状态跃迁计数 + 活跃超时未结束告警
        bool fogActive = _gs.Events.ActiveEvent(GameEventManager.GroupFog).VariantType != Variant.Type.Nil;
        if (fogActive)
        {
            if (!_fogWasActive)
            {
                _fogCount++;
                _fogActiveSince = now;
                Log($"迷雾事件触发（第 {_fogCount} 次）");
            }
            else if (now - _fogActiveSince > FogStuckMs)
            {
                AnomalyRl("fog_stuck", $"迷雾事件活跃超 {FogStuckMs / 1000}s 未结束", now);
            }
            _fogWasActive = true;
        }
        else
        {
            _fogWasActive = false;
        }
        // UI 状态一致性：结算面板与基地面板同显 / 玩家死亡但游戏未停且无结算面板
        var gameOverUi = main.GetNode<GameOverUi>("GameOverUI");
        if (gameOverUi.Visible && main.BaseUi().Visible)
        {
            AnomalyRl("ui_overlap", "GameOverUI 与基地 UI 同时可见", now);
        }
        if (_player != null && _player.IsDead() && !GetTree().Paused && !gameOverUi.Visible)
        {
            AnomalyRl("dead_no_gameover", "玩家已死亡、游戏未暂停且结算面板不可见", now);
        }
        // 分数停滞 + 场上无敌机（疑似不刷怪/不结算）
        if (_gs.Score != _lastScore)
        {
            _lastScore = _gs.Score;
            _scoreChangeMsec = now;
            _scoreStagReported = false;
        }
        else if (!_scoreStagReported && now - _scoreChangeMsec > ScoreStagnantMs
            && _gs.Enemies.Count == 0 && _boss == null && !GetTree().Paused
            && !main.IsHomecoming() && !main.IsGameOver())
        {
            _scoreStagReported = true;
            Anomaly("score_stagnant", $"分数 {ScoreStagnantMs / 1000}s 未增长且场上无敌机（疑似不刷怪）");
        }
    }

}
