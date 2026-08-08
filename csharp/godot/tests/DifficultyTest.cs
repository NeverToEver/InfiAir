using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 难度 / 里程碑 / 设置测试（迭代 3.4-A）：
/// 三档难度下敌机 HP/速度缩放、分数倍率 ×1/×2/×3、spread 同屏上限 1/2/3、
/// 刷怪间隔倍率、里程碑阈值曲线（8 档基础 + 循环 ×1.35 + 难度倍率）、
/// 难度进程曲线（线性击杀项 + 30s 量化时间档，去硬顶）、
/// 难度 profile 持久化往返、Ctrl/Shift 模式标志序列化（对局存档 + profile）。
/// 不加载 main 场景；spawner 以脚本实例挂载（停 process），敌机仅采样 setup 数值。
/// M7（2026-08-06 审计）：profile 快照还原——原测试经 _write_profile 部分覆写
/// profile.json + load_profile 间接清零 pre-login 最高分/高分榜并落盘，无快照还原
/// （L15 只修直写路径，未覆盖间接清零）；备份/还原防本地数据被永久销毁。
/// </summary>
public partial class DifficultyTest : Node
{
    private int _failures;
    private GameState _gs = null!;
    private PackedScene _enemyScene = null!;
    private readonly Godot.Collections.Dictionary _profileBackup = new();

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

    // 采样 count 架敌机（不入树，只取 setup 后的 hp/speed）
    private Godot.Collections.Array<Enemy> SampleBatch(Godot.Collections.Dictionary config, int count)
    {
        var outList = new Godot.Collections.Array<Enemy>();
        for (var i = 0; i < count; i++)
        {
            var e = _enemyScene.Instantiate<Enemy>();
            e.Setup(config, new StringName("straight"), 1.0f);
            outList.Add(e);
        }
        return outList;
    }

    private void FreeBatch(Godot.Collections.Array<Enemy> batch)
    {
        foreach (var e in batch)
        {
            e.Free();
        }
    }

    private double AvgHp(Godot.Collections.Array<Enemy> batch)
    {
        double total = 0.0;
        foreach (var e in batch)
        {
            total += e.Hp;
        }
        return total / batch.Count;
    }

    private Godot.Collections.Dictionary ReadProfile()
    {
        var parsed = Json.ParseString(Godot.FileAccess.GetFileAsString(_gs.PROFILE_PATH));
        return parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : new Godot.Collections.Dictionary();
    }

    private void WriteProfile(Godot.Collections.Dictionary data)
    {
        var f = Godot.FileAccess.Open(_gs.PROFILE_PATH, Godot.FileAccess.ModeFlags.Write);
        f.StoreString(Json.Stringify(data));
        f.Close();
    }

    private void BackupProfile()
    {
        _profileBackup.Clear();
        foreach (var path in new[] { _gs.PROFILE_PATH, _gs.PROFILE_PATH + ".corrupt" })
        {
            var exists = Godot.FileAccess.FileExists(path);
            _profileBackup[path] = new Godot.Collections.Dictionary
            {
                ["exists"] = exists,
                ["content"] = exists ? Godot.FileAccess.GetFileAsString(path) : "",
            };
        }
    }

    private void RestoreProfile()
    {
        foreach (var kv in _profileBackup)
        {
            var b = kv.Value.AsGodotDictionary();
            if (b["exists"].AsBool())
            {
                var fh = Godot.FileAccess.Open(kv.Key.AsString(), Godot.FileAccess.ModeFlags.Write);
                fh.StoreString(b["content"].AsString());
                fh.Close();
            }
            else if (Godot.FileAccess.FileExists(kv.Key.AsString()))
            {
                DirAccess.RemoveAbsolute(kv.Key.AsString());
            }
        }
    }

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            _gs = GetNode<GameState>("/root/GameState");
            _enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");
            // M7：profile 快照（须在任何覆写/落盘前捕获原始 pre-login 最高分与高分榜）
            BackupProfile();
            // 确定性起点：清存档，内存状态归位（难度/模式为 profile 级，reset_run 不清）
            _gs.DeleteSave();
            _gs.Difficulty = new StringName("medium");
            _gs.CtrlToggleMode = false;
            _gs.ShiftToggleMode = false;

            // ---------- 1. 分数倍率 ×1/×2/×3（add_score 统一乘算） ----------
            var scoreCases = new (StringName Difficulty, int Expected)[]
            {
                (new StringName("easy"), 100),
                (new StringName("medium"), 200),
                (new StringName("hard"), 300),
            };
            foreach (var c in scoreCases)
            {
                _gs.ResetRun();
                _gs.Difficulty = c.Difficulty;
                _gs.SetMilestoneOverride(999999999);  // 屏蔽里程碑干扰
                _gs.AddScore(100);
                Check(_gs.Score == c.Expected, $"难度 {_gs.DifficultyLabel()} 分数倍率：+100 → {c.Expected}");
            }

            // ---------- 2. 敌机 HP/速度缩放（同 seed 对比，randf 序列对齐） ----------
            var normalCfg = Spawner.BuildEnemyTypes()[0];  // hp 65-72, speed 115-145（K16：注释数值随 spawner 静态表修正）
            var eliteCfg = Spawner.BuildEliteTypes()[0];  // hp 190-210, speed 75-95
            GD.Seed(1001);
            _gs.Difficulty = new StringName("easy");
            var easyBatch = SampleBatch(normalCfg, 30);
            GD.Seed(1001);
            _gs.Difficulty = new StringName("medium");
            var medBatch = SampleBatch(normalCfg, 30);
            GD.Seed(1001);
            _gs.Difficulty = new StringName("hard");
            var hardBatch = SampleBatch(normalCfg, 30);
            // 同 seed 下同一次 randf 抽中的速度按比例缩放（精确关系）
            var speedRatioOk = true;
            var hpMonoOk = true;
            for (var i = 0; i < 30; i++)
            {
                if (System.Math.Abs((double)easyBatch[i].Speed / medBatch[i].Speed - 0.85) > 0.001)
                {
                    speedRatioOk = false;
                }
                if (System.Math.Abs((double)hardBatch[i].Speed / medBatch[i].Speed - 1.2) > 0.001)
                {
                    speedRatioOk = false;
                }
                if (!(easyBatch[i].Hp <= medBatch[i].Hp && medBatch[i].Hp <= hardBatch[i].Hp))
                {
                    hpMonoOk = false;
                }
            }
            Check(speedRatioOk, "敌机速度按难度缩放：easy ×0.85 / hard ×1.2");
            Check(hpMonoOk, "敌机 HP 单调：easy ≤ medium ≤ hard");
            var avgE = AvgHp(easyBatch);
            var avgM = AvgHp(medBatch);
            var avgH = AvgHp(hardBatch);
            Check(avgE < avgM && avgM < avgH, "敌机 HP 均值随难度递增");
            Check(System.Math.Abs(avgE / avgM - 0.75) < 0.1, "敌机 HP 均值比 ≈ ×0.75（easy）");
            Check(System.Math.Abs(avgH / avgM - 1.5) < 0.1, "敌机 HP 均值比 ≈ ×1.5（hard）");
            FreeBatch(easyBatch);
            FreeBatch(medBatch);
            FreeBatch(hardBatch);
            // 精英大 HP 池：三档区间互不重叠（easy 143-158 / medium 190-210 / hard 285-315）
            GD.Seed(2002);
            _gs.Difficulty = new StringName("easy");
            var eliteE = SampleBatch(eliteCfg, 30);
            GD.Seed(2002);
            _gs.Difficulty = new StringName("medium");
            var eliteM = SampleBatch(eliteCfg, 30);
            GD.Seed(2002);
            _gs.Difficulty = new StringName("hard");
            var eliteH = SampleBatch(eliteCfg, 30);
            var maxE = 0;
            var minM = 999999;
            var maxM = 0;
            var minH = 999999;
            for (var i = 0; i < 30; i++)
            {
                maxE = System.Math.Max(maxE, eliteE[i].Hp);
                minM = System.Math.Min(minM, eliteM[i].Hp);
                maxM = System.Math.Max(maxM, eliteM[i].Hp);
                minH = System.Math.Min(minH, eliteH[i].Hp);
            }
            Check(maxE < minM, "精英 HP easy 上限 < medium 下限（×0.75 生效）");
            Check(maxM < minH, "精英 HP medium 上限 < hard 下限（×1.5 生效）");
            FreeBatch(eliteE);
            FreeBatch(eliteM);
            FreeBatch(eliteH);

            // ---------- 3. spread 同屏上限 1/2/3 ----------
            _gs.Difficulty = new StringName("easy");
            Check(_gs.SpreadEnemyCap() == 1, "spread 上限 easy=1");
            _gs.Difficulty = new StringName("medium");
            Check(_gs.SpreadEnemyCap() == 2, "spread 上限 medium=2");
            _gs.Difficulty = new StringName("hard");
            Check(_gs.SpreadEnemyCap() == 3, "spread 上限 hard=3");
            var spawner = new Spawner();
            AddChild(spawner);
            spawner.SetProcess(false);  // 只用其抽取/计数逻辑，不自动刷怪
            var spreadFighters = new Godot.Collections.Array<Enemy>();
            for (var i = 0; i < 3; i++)
            {
                var e = _enemyScene.Instantiate<Enemy>();
                e.Setup(normalCfg, new StringName("straight"), 1.0f);
                e.bullet_type = new StringName("spread");
                e.CanShoot = false;
                e.Position = new Vector2(400.0f + 400.0f * i, 300.0f);
                spreadFighters.Add(e);
            }
            // easy（上限 1）：1 架在场即退化
            _gs.Difficulty = new StringName("easy");
            AddChild(spreadFighters[0]);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(spawner.CountSpreadEnemies() == 1, "spread 敌机同屏计数为 1");
            var easyDegenerate = true;
            for (var i = 0; i < 20; i++)
            {
                if (spawner.PickBulletType(Spawner.BuildEnemyTypes()[1]) != new StringName("single"))
                {
                    easyDegenerate = false;
                }
            }
            Check(easyDegenerate, "spread 上限 1（easy）：普通机退化为 single");
            // medium（上限 2）：1 架在场未满可出 spread，2 架满则退化
            _gs.Difficulty = new StringName("medium");
            var sawSpreadM = false;
            for (var i = 0; i < 40; i++)
            {
                if (spawner.PickBulletType(Spawner.BuildEnemyTypes()[1]) == new StringName("spread"))
                {
                    sawSpreadM = true;
                }
            }
            Check(sawSpreadM, "spread 上限 2（medium）：1 架在场仍可出 spread");
            AddChild(spreadFighters[1]);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var medDegenerate = true;
            var medEliteDegenerate = true;
            for (var i = 0; i < 20; i++)
            {
                if (spawner.PickBulletType(Spawner.BuildEnemyTypes()[1]) != new StringName("single"))
                {
                    medDegenerate = false;
                }
                if (spawner.PickBulletType(Spawner.BuildEliteTypes()[2]) != new StringName("laser"))
                {
                    medEliteDegenerate = false;
                }
            }
            Check(medDegenerate, "spread 上限 2（medium）：满 2 架普通机退化为 single");
            Check(medEliteDegenerate, "spread 上限 2（medium）：满 2 架精英退化为 laser");
            // hard（上限 3）：2 架在场仍可出 spread，3 架满则退化
            _gs.Difficulty = new StringName("hard");
            var sawSpreadH = false;
            for (var i = 0; i < 40; i++)
            {
                if (spawner.PickBulletType(Spawner.BuildEnemyTypes()[1]) == new StringName("spread"))
                {
                    sawSpreadH = true;
                }
            }
            Check(sawSpreadH, "spread 上限 3（hard）：2 架在场仍可出 spread");
            AddChild(spreadFighters[2]);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var hardDegenerate = true;
            for (var i = 0; i < 20; i++)
            {
                if (spawner.PickBulletType(Spawner.BuildEnemyTypes()[1]) != new StringName("single"))
                {
                    hardDegenerate = false;
                }
            }
            Check(hardDegenerate, "spread 上限 3（hard）：满 3 架普通机退化为 single");
            foreach (var e in spreadFighters)
            {
                if (GodotObject.IsInstanceValid(e))
                {
                    e.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ---------- 4. 刷怪间隔倍率 ×1.25/×1/×0.8 ----------
            // 钉住难度进程曲线：间隔断言基于难度乘数 ×1.0，排除时间轴档位漂移
            _gs.RunTime = 0.0;
            _gs.RecomputeDifficulty();
            spawner.SetElapsed(0.0f);
            _gs.Difficulty = new StringName("easy");
            var ivEasy = spawner.CurrentInterval();
            _gs.Difficulty = new StringName("medium");
            var ivMedium = spawner.CurrentInterval();
            _gs.Difficulty = new StringName("hard");
            var ivHard = spawner.CurrentInterval();
            Check(System.Math.Abs(ivEasy - 8.75f) < 0.01f, "波次间隔 easy ×1.25（7.0s → 8.75s）");
            Check(System.Math.Abs(ivMedium - 7.0f) < 0.01f, "波次间隔 medium ×1（7.0s 不变）");
            Check(System.Math.Abs(ivHard - 5.6f) < 0.01f, "波次间隔 hard ×0.8（7.0s → 5.6s）");
            Check(ivEasy > ivMedium && ivMedium > ivHard, "波次间隔随难度递减（越难越密）");

            // ---------- 4b. 难度进程曲线：线性击杀项 + 时间轴档位（去硬顶，无限段修订） ----------
            _gs.Difficulty = new StringName("medium");
            _gs.ResetRun();
            Check(_gs.DifficultyMultiplier == 1.0, "进程曲线：开局 ×1.0");
            _gs.BossKills = 2;
            _gs.RecomputeDifficulty();
            Check(System.Math.Abs(_gs.DifficultyMultiplier - 2.2) < 0.001, "进程曲线：2 杀 ×2.2（线性 0.6/杀）");
            _gs.RunTime = 65.0;
            _gs.RecomputeDifficulty();
            Check(System.Math.Abs(_gs.DifficultyMultiplier - 2.35) < 0.001, "进程曲线：65s 量化两档 +0.15（30s/档，per_ten_minutes 1.5）");
            _gs.BossKills = 20;
            _gs.RecomputeDifficulty();
            Check(_gs.DifficultyMultiplier > 11.0, "进程曲线：20 杀无硬顶（>×11，原 ×8 封顶废弃）");
            _gs.ResetRun();
            Check(_gs.DifficultyMultiplier == 1.0, "进程曲线：reset_run 归位 ×1.0");

            // ---------- 5. 里程碑阈值曲线 ----------
            _gs.Difficulty = new StringName("medium");
            int[] baseThresholds = { 3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000 };
            var firstCycleOk = true;
            for (var i = 0; i < baseThresholds.Length; i++)
            {
                if (_gs.MilestoneThreshold(i) != baseThresholds[i])
                {
                    firstCycleOk = false;
                }
            }
            Check(firstCycleOk, "里程碑首循环 8 档：3000→80000");
            Check(_gs.MilestoneThreshold(8) == 84050, "循环增长：第 9 档 80000+3000×1.35");
            Check(_gs.MilestoneThreshold(9) == 90800, "循环增长：第 10 档 +5000×1.35");
            Check(_gs.MilestoneThreshold(15) == 188000, "循环增长：第二循环末 80000+80000×1.35");
            Check(_gs.MilestoneThreshold(16) > _gs.MilestoneThreshold(15), "循环阈值单调不回退");
            _gs.Difficulty = new StringName("easy");
            Check(_gs.MilestoneThreshold(0) == 3000, "阈值难度倍率 easy ×1（首档 3000）");
            Check(_gs.MilestoneThreshold(7) == 80000, "阈值难度倍率 easy ×1（末档 80000）");
            _gs.Difficulty = new StringName("hard");
            Check(_gs.MilestoneThreshold(0) == 4500, "阈值难度倍率 hard ×1.5（首档 4500）");
            Check(_gs.MilestoneThreshold(7) == 120000, "阈值难度倍率 hard ×1.5（末档 120000）");
            Check(_gs.MilestoneThreshold(8) == 126075, "阈值难度倍率 hard ×1.5（循环档同步缩放）");

            // _next_milestone 机制：reset 后从首档开始，触发后沿曲线推进
            _gs.Difficulty = new StringName("medium");
            _gs.ResetRun();
            Check(_gs.NextMilestone() == 3000, "reset_run 后下一里程碑为 3000");
            var fired = 0;
            _gs.MilestoneReached += _ => fired++;
            _gs.AddScore(1500);  // ×2 = 3000，触发第 1 档
            Check(fired == 1 && _gs.NextMilestone() == 8000, "到达 3000 触发里程碑并推进 8000");
            _gs.AddScore(2500);  // ×2 = 5000，累计 8000
            Check(fired == 2 && _gs.NextMilestone() == 15000, "到达 8000 触发里程碑并推进 15000");
            _gs.SetMilestoneOverride(100);
            _gs.AddScore(50);  // ×2 = 100，触发 override
            Check(fired == 3 && _gs.NextMilestone() == 25000, "override 阈值触发后回到曲线档位");
            _gs.Difficulty = new StringName("hard");
            _gs.ResetRun();
            Check(_gs.NextMilestone() == 4500, "hard 档 reset_run 后下一里程碑为 4500");

            // 存档恢复：按分数定位曲线档位
            _gs.Difficulty = new StringName("medium");
            _gs.ResetRun();
            _gs.SetMilestoneOverride(999999999);
            _gs.AddScore(5000);  // ×2 = 10000，处于 8000~15000 之间
            _gs.SaveRun(50.0, 1.0);
            _gs.ApplyRunSave(_gs.LoadRunData());
            Check(_gs.NextMilestone() == 15000, "存档恢复后里程碑定位到 15000");

            // A 审计验证：apply_run_save 大分数 + 极小 cycle_mult 不应挂死（迭代上限保护）
            // cycle_mult 钳至 0.01 时阈值增量收敛，大分数下原 while 无界 → 挂死；10000 迭代上限保护
            var origCycleMult = _gs.MilestoneCycleMult;
            _gs.MilestoneCycleMult = 0.01;
            _gs.Score = 999999999;  // 远超阈值上限（~80808），原实现会无限循环
            _gs.ApplyRunSave(new Godot.Collections.Dictionary { ["version"] = 2, ["score"] = 999999999 });
            Check(_gs.Score == 999999999, "A审计：大分数 apply_run_save 不挂死（迭代上限保护）");
            // milestone_threshold 极大 index 不溢出（pow 钳至 finite）
            var mtHuge = _gs.MilestoneThreshold(99999);
            Check(mtHuge >= 0 && mtHuge != 2147483647, "A审计：极大 index milestone_threshold 不溢出 UB");
            _gs.MilestoneCycleMult = origCycleMult;
            _gs.ResetRun();

            // ---------- 6. 难度持久化（profile 往返 + 旧档兼容） ----------
            _gs.SetDifficulty(new StringName("hard"));
            Check(ReadProfile().GetValueOrDefault("difficulty", "").AsString() == "hard", "难度写入 profile");
            _gs.Difficulty = new StringName("easy");  // 篡改内存（不经 setter，避免写盘）
            _gs.LoadProfile();
            Check(_gs.Difficulty == new StringName("hard"), "难度从 profile 恢复");
            // 旧档案无 difficulty 字段：不覆盖当前值、不报错
            WriteProfile(new Godot.Collections.Dictionary { ["version"] = 1, ["high_score"] = 0 });
            _gs.Difficulty = new StringName("easy");
            _gs.LoadProfile();
            Check(_gs.Difficulty == new StringName("easy"), "旧档（无 difficulty 字段）读取保留当前难度");
            // 非法档位：忽略并保持当前值
            WriteProfile(new Godot.Collections.Dictionary { ["version"] = 1, ["high_score"] = 0, ["difficulty"] = "nightmare" });
            _gs.LoadProfile();
            Check(_gs.Difficulty == new StringName("easy"), "非法难度值被忽略");
            _gs.SetDifficulty(new StringName("medium"));
            Check(_gs.Difficulty == new StringName("medium"), "难度恢复 medium");

            // Q04（2026-08-05）：load_profile 恢复存档难度后必须刷新被动回血缓存——
            // 原实现缓存仅 set_difficulty/启动刷新，重启后 hard 玩家按 medium(2.0/4.0) 回血。
            // 注：set_difficulty 会写 profile，故先用 setter 置 medium 缓存、再直写档为 hard，
            // 确保 load_profile 前缓存与档案不一致（修复前缓存残留 medium 值）
            _gs.SetDifficulty(new StringName("medium"));
            WriteProfile(new Godot.Collections.Dictionary { ["version"] = 1, ["high_score"] = 0, ["difficulty"] = "hard" });
            _gs.Difficulty = new StringName("easy");  // 篡改内存（不经 setter，缓存仍为 medium 值）
            _gs.LoadProfile();  // 档案 difficulty=hard
            Check(_gs.Difficulty == new StringName("hard"), "Q04：难度从 profile 恢复 hard");
            Check(
                Mathf.IsEqualApprox(_gs.PassiveRegenRate(), 0.67),
                $"Q04：恢复后 regen_rate=0.67（实测 {_gs.PassiveRegenRate():0.000}，修复前残留 medium 2.0）"
            );
            Check(
                Mathf.IsEqualApprox(_gs.PassiveRegenDelay(), 5.0),
                $"Q04：恢复后 regen_delay=5.0（实测 {_gs.PassiveRegenDelay():0.0}，修复前残留 medium 4.0）"
            );
            _gs.SetDifficulty(new StringName("medium"));

            // ---------- 7. Ctrl/Shift 模式标志序列化 ----------
            Check(!_gs.CtrlToggleMode && !_gs.ShiftToggleMode, "设置模式默认均为按住");
            // 对局存档往返
            _gs.ResetRun();
            _gs.CtrlToggleMode = true;
            _gs.ShiftToggleMode = true;
            _gs.SaveRun(50.0, 1.0);
            _gs.CtrlToggleMode = false;
            _gs.ShiftToggleMode = false;
            _gs.ApplyRunSave(_gs.LoadRunData());
            Check(_gs.CtrlToggleMode, "Ctrl 切换模式随对局存档往返");
            Check(_gs.ShiftToggleMode, "Shift 切换模式随对局存档往返");
            // 旧存档无字段：保持当前值不报错
            _gs.ApplyRunSave(new Godot.Collections.Dictionary());
            Check(_gs.CtrlToggleMode && _gs.ShiftToggleMode, "旧存档（无模式字段）恢复保持当前值");
            // profile 往返
            _gs.SetCtrlToggleMode(true);
            _gs.SetShiftToggleMode(true);
            var profile = ReadProfile();
            Check(
                profile.GetValueOrDefault("ctrl_toggle_mode", false).AsBool()
                    && profile.GetValueOrDefault("shift_toggle_mode", false).AsBool(),
                "设置模式写入 profile"
            );
            _gs.CtrlToggleMode = false;
            _gs.ShiftToggleMode = false;
            _gs.LoadProfile();
            Check(_gs.CtrlToggleMode && _gs.ShiftToggleMode, "设置模式从 profile 恢复");
            // reset_run 不清难度与设置模式（profile 级偏好）
            _gs.Difficulty = new StringName("hard");
            _gs.ResetRun();
            Check(_gs.Difficulty == new StringName("hard") && _gs.CtrlToggleMode, "reset_run 保留难度与设置模式");

            // ---------- 8. Boss 触发最小间隔（BOSS_MIN_INTERVAL，防分数暴涨期连出 Boss） ----------
            _gs.Difficulty = new StringName("medium");
            _gs.ResetRun();
            _gs.SetMilestoneOverride(999999999);
            spawner.SetBossActive(false);
            spawner.SetBossFrozen(false);
            spawner.SetBossPending(false);
            spawner.SetNextBossScore(spawner.BOSS_SCORE_STEP);
            _gs.Score = spawner.BOSS_SCORE_STEP;  // 分数已跨步进（直接赋值，避开倍率/里程碑）
            spawner.SetBossTimer(10.0f);  // 距上次 Boss 仅 10s（模拟 Boss 刚死、分数立刻跨步进）
            spawner.SetProcess(true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!spawner.IsBossActive(), "Boss 最小间隔：分数跨步进但间隔 <80s 不触发");
            spawner.SetBossTimer(spawner.BOSS_MIN_INTERVAL + 1.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(spawner.IsBossActive(), "Boss 最小间隔：越过 80s 后分数触发生效");
            // 清理：停主循环并撤掉已排程的 Boss 降入 Timer（不真正生成 Boss）
            spawner.SetProcess(false);
            foreach (var c in spawner.GetChildren())
            {
                if (c is Godot.Timer timer)
                {
                    timer.QueueFree();
                }
            }
            spawner.SetBossActive(false);
            spawner.SetBossTimer(0.0f);
            _gs.Score = 0;

            // ---------- 8b. G01：Boss 预警 2s 窗口内 clear_pending（返航）必须解除占用 ----------
            spawner.SetBossTimer(spawner.BOSS_MIN_INTERVAL + 1.0f);
            spawner.TriggerBoss();
            Check(spawner.IsBossActive(), "G01：Boss 预警中占用波次/Boss/事件槽");
            spawner.ClearPending();
            Check(!spawner.IsBossActive(), "G01：预警取消（返航）解除占用——continue 后 Boss 门可再触发");
            foreach (var c in spawner.GetChildren())
            {
                if (c is Godot.Timer timer)
                {
                    timer.QueueFree();
                }
            }
            spawner.SetBossActive(false);
            spawner.SetBossTimer(0.0f);
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"DIFFICULTY TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"DIFFICULTY TEST DONE, failures = {_failures}");
            // 清理：恢复默认并落盘，避免污染其他测试进程
            if (_gs != null)
            {
                _gs.Difficulty = new StringName("medium");
                _gs.CtrlToggleMode = false;
                _gs.ShiftToggleMode = false;
                _gs.ResetRun();
                _gs.SaveProfile();
                _gs.DeleteSave();
            }
            // M7：还原原始 profile（最高分/高分榜/设置项），防本地数据被清零
            RestoreProfile();
            TestExit.Quit(_failures);
        }
    }
}
