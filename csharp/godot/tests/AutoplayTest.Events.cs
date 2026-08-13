using Godot;

namespace InfiAir.Tests;

/// <summary>AutoplayTest 信号回调（partial 拆分自 AutoplayTest.cs，纯搬移零行为变化）。</summary>
public partial class AutoplayTest : Node
{
    // ---------------- 事件 ----------------

    private void OnMilestone(int milestoneScore)
    {
        _milestones++;
        Log($"里程碑达成 score={milestoneScore}（第 {_milestones} 次）");
    }

    private void OnBossSpawned(Boss boss)
    {
        _boss = boss;
        _bossSince = (long)Time.GetTicksMsec();
        _bossTimeoutReported = false;
        Log($"Boss 出现 type={boss.BossType} hp={boss.MaxHp:0}");
        boss.Enraged += () =>
        {
            _bossEnrageCount++;
            Log($"Boss 狂暴 type={boss.BossType}（第 {_bossEnrageCount} 次）");
        };
        boss.PhaseChanged += OnBossPhaseChanged;
        boss.Died += () => OnBossDied(boss);
        boss.Escaped += () =>
        {
            _bossEscapes++;
            Log($"Boss 逃跑 type={boss.BossType}（第 {_bossEscapes} 次）");
            ClearBoss(boss);
        };
        boss.TreeExited += () => ClearBoss(boss);
    }

    private void OnBossPhaseChanged(int newPhase)
    {
        if (newPhase == FightP2)  // M3d：Boss.FightPhase.P2（C# 枚举直接引用，P1=0/P2=1/ENRAGE=2）
        {
            _bossP2Count++;
            Log($"Boss 进入 P2（第 {_bossP2Count} 次）");
        }
    }

    private void OnBossDied(Boss boss)
    {
        if (boss.IsEscaped)
        {
            return;  // 逃跑离场也会发 died（通知血条/生成器），非击杀
        }
        _totalBossKills++;
        Log($"Boss 击杀 type={boss.BossType}（本进程累计 {_totalBossKills}）");
        ClearBoss(boss);
    }

    private void ClearBoss(Boss boss)
    {
        if (_boss == boss)
        {
            _boss = null;
        }
    }

    private void OnPlayerDied()
    {
        _deaths++;
        _totalKills += _gs.Kills;
        _runScores.Add(_gs.Score);
        Log($"玩家死亡 run={_runIndex} score={_gs.Score} kills={_gs.Kills} boss_kills={_gs.BossKills}");
        ReleaseAllInputs();
        _menuReturnAt = 0;
        _restartAt = (long)Time.GetTicksMsec() + 3000;  // 留 3s 走到结算界面
    }

    private void OnHealthChanged(double newHealth)
    {
        long now = (long)Time.GetTicksMsec();
        if (_lastHp >= 0.0 && newHealth < _lastHp - 0.01 && now - _lastHitLogMsec > 1000)
        {
            _lastHitLogMsec = now;
            Log($"玩家受击 HP {_lastHp:0.0} -> {newHealth:0.0}");
        }
        _lastHp = newHealth;
    }

    /// <summary>B 梯队：受击触发 DDA 降档——记录时刻与触发次数（受击即计数：
    /// GameState 自连接先于本回调置位，用 dda_active 判触发会恒 false）</summary>
    private void OnPlayerDamaged(float amount, Vector2 fromPos)
    {
        _lastDamagedMsec = (long)Time.GetTicksMsec();
        _ddaTriggerCount++;
    }

}
