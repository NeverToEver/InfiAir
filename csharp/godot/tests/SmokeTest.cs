using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 冒烟测试（M7c 迁移自 test/smoke_test.gd）：覆盖里程碑 Buff UI、Boss 生成、玩家死亡结算路径。
/// 模式与 GDScript 断言场景一致：_Ready fire-and-forget + Check + TestExit.Quit(failures)。
/// </summary>
public partial class SmokeTest : Node
{
    private const string EnemyScenePath = "res://scenes/enemy.tscn";
    private const string BulletScenePath = "res://scenes/bullet.tscn";

    private static readonly PackedScene EnemyScene = GD.Load<PackedScene>(EnemyScenePath);

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

    /// <summary>2026-08-06 审计：profile 全量快照还原（smoke 全程改难度/瞄准辅助/切换模式等
    /// profile 级设置并落盘；备份/还原防用户设置被覆盖为测试值）。</summary>
    private Godot.Collections.Dictionary _profileBackup = new();

    private void BackupProfile(GameState gs)
    {
        _profileBackup = new Godot.Collections.Dictionary();
        foreach (var f in new[] { gs.PROFILE_PATH, gs.PROFILE_PATH + ".corrupt" })
        {
            var exists = Godot.FileAccess.FileExists(f);
            _profileBackup[f] = new Godot.Collections.Dictionary
            {
                ["exists"] = exists,
                ["content"] = exists ? Godot.FileAccess.GetFileAsString(f) : "",
            };
        }
    }

    private void RestoreProfile()
    {
        foreach (var key in _profileBackup.Keys)
        {
            var f = key.AsString();
            var b = _profileBackup[key].AsGodotDictionary();
            if (b["exists"].AsBool())
            {
                var fh = Godot.FileAccess.Open(f, Godot.FileAccess.ModeFlags.Write);
                fh.StoreString(b["content"].AsString());
                fh.Close();
            }
            else if (Godot.FileAccess.FileExists(f))
            {
                Godot.DirAccess.RemoveAbsolute(f);
            }
        }
    }

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget（约定 §Async）
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            // 2026-08-06 审计：profile 全量快照（结尾还原用户难度/瞄准辅助/切换模式等设置项，
            // 原"恢复默认难度"覆盖用户原档——持久化设置结尾恢复默认值而非用户原值）
            BackupProfile(gs);
            // 清理持久化状态，保证测试确定性（上一轮可能留下存档/最高分）
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            // 固定 easy 档（分数 ×1），保持本测试既有数值断言；结束时恢复 medium
            gs.SetDifficulty("easy");
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate<Main>());
            var main = GetNode<Main>("Main");
            // 玩家已改全自动开火：测试全程禁用，避免误伤敌机/Boss 或触发意外得分里程碑
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var buffUi = GetNode<BuffSelect>("Main/BuffUI");
            var spawner = GetNode<Spawner>("Main/Spawner");

            // 1. 里程碑触发 Buff UI（阈值已改曲线，测试用 override 固定 500 保证确定性）
            gs.SetMilestoneOverride(500);
            gs.AddScore(500);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(buffUi.Visible, "500 分触发 Buff 选择 UI");
            Check(GetTree().Paused, "Buff UI 弹出时游戏暂停");
            Check(buffUi.CurrentAvailable().Count == 3, "里程碑三选一候选数为 3（池未满）");

            // 2. 选择 buff 后恢复
            buffUi.PickBuff("power_shot");
            Check(!buffUi.Visible && !GetTree().Paused, "选择 buff 后关闭并恢复");
            Check(gs.BuffCount("power_shot") == 1, "buff 计入 GameState");

            // 3. Boss 生成与弹幕
            spawner.SpawnBoss();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Boss? boss = null;
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Boss b)
                {
                    boss = b;
                }
            }
            Check(boss != null, "Boss 已生成");
            Check(GetNode<Control>("Main/HUD/BossBar").Visible, "Boss 血条显示");
            // 无头模式帧不封顶，240 帧远不足 4 秒真实时间，改用真实时间等待
            await Coroutine.WaitSeconds(this, 4.0);
            Check(boss!.IsInFight(), "Boss 进入巡航阶段");
            boss!.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(gs.BossKills == 1, "Boss 击毁计数");
            // 进程曲线：1 + 0.6×击杀 + 时间轴每 30s +0.075（2026-08-04 深局校准定稿）
            var expectMult = 1.0 + 0.6 + Mathf.Floor(gs.RunTime / 30.0) * 0.075;
            Check(Mathf.Abs(gs.DifficultyMultiplier - expectMult) < 0.001, "难度乘数按公式更新");
            Check(!GetNode<Control>("Main/HUD/BossBar").Visible, "Boss 血条隐藏");
            // 里程碑曲线下后续阈值远高于当前分数，Buff UI 一般不再弹出；若弹出则关闭以便继续测试
            if (buffUi.Visible)
            {
                buffUi.PickBuff("rapid_fire");
            }
            Check(!buffUi.Visible && !GetTree().Paused, "里程碑 UI 可重复触发并关闭");
            // 停掉生成器并清场（敌机/敌弹），保证后续断言确定性
            spawner.SetProcess(false);
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Enemy || (child is Bullet b && !b.IsPlayerBullet))
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 后续各段需长时间真实等待，期间弹幕可能命中玩家：测试窗口内先开无敌
            player.SetInvincible(999.0f);

            // 3.1 新移动模式特征
            // spiral：横向振幅 + 整体下压（机动相位随机，采样窗口取最大偏离）
            var spiral = EnemyScene.Instantiate<Enemy>();
            spiral.Setup(spawner.ENEMY_TYPES[2], "spiral", 1.0f);
            spiral.CanShoot = false;
            spiral.Position = new Vector2(960.0f, 200.0f);
            GetNode("Main").AddChild(spiral);
            double maxDev = 0.0;
            for (int i = 0; i < 8; i++)
            {
                await Coroutine.WaitSeconds(this, 0.1);
                maxDev = Mathf.Max(maxDev, Mathf.Abs(spiral.Position.X - 960.0));
            }
            Check(maxDev > 20.0, "spiral 横向振幅");
            Check(spiral.Position.Y > 200.0f, "spiral 整体下压");
            spiral.QueueFree();

            // noise：横向速度不规则（采样位移变化量有显著差异）
            var noise = EnemyScene.Instantiate<Enemy>();
            noise.Setup(spawner.ENEMY_TYPES[3], "noise", 1.0f);
            noise.CanShoot = false;
            noise.Position = new Vector2(960.0f, 200.0f);
            GetNode("Main").AddChild(noise);
            var xs = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 6; i++)
            {
                await Coroutine.WaitSeconds(this, 0.3);
                xs.Add(noise.Position.X);
            }
            var dxs = new System.Collections.Generic.List<float>();
            for (int i = 0; i < xs.Count - 1; i++)
            {
                dxs.Add(xs[i + 1] - xs[i]);
            }
            float dxMax = float.MinValue;
            float dxMin = float.MaxValue;
            foreach (var d in dxs)
            {
                dxMax = Mathf.Max(dxMax, d);
                dxMin = Mathf.Min(dxMin, d);
            }
            Check(dxMax - dxMin > 1.0f, "noise 横向飘移不规则");
            noise.QueueFree();

            // hover：下行 → 到达锚点后停驻机动（不再净下降，直到寿命离场）
            var hov = EnemyScene.Instantiate<Enemy>();
            hov.Setup(spawner.ENEMY_TYPES[2], "hover", 1.0f);
            hov.CanShoot = false;
            hov.Position = new Vector2(960.0f, 250.0f);
            GetNode("Main").AddChild(hov);
            double tHover = 0.0;
            while (!hov.Hovering() && tHover < 4.0)
            {
                await Coroutine.WaitSeconds(this, 0.2);
                tHover += 0.2;
            }
            Check(hov.Hovering(), "hover 到达锚点后停驻");
            float hoverY = hov.Position.Y;
            await Coroutine.WaitSeconds(this, 0.5);
            Check(Mathf.Abs(hov.Position.Y - hoverY) < 15.0f, "hover 停驻期间位置稳定");
            // 停驻期间绕出生槽位水平慢摇摆（相位随机，采样窗口取最大偏离）
            double maxSway = 0.0;
            for (int i = 0; i < 6; i++)
            {
                await Coroutine.WaitSeconds(this, 0.2);
                maxSway = Mathf.Max(maxSway, Mathf.Abs(hov.Position.X - 960.0));
            }
            Check(maxSway > 10.0, "hover 停驻期间水平摇摆");
            Check(Mathf.Abs(hov.Position.Y - hov.AnchorY) <= hov.HOVER_BOB_AMP + 1.0f, "hover 停驻后不再净下降");
            hov.QueueFree();

            // 3.2 Boss 轮换：第 2 只（boss_kills=1）应为游击型
            spawner.SpawnBoss();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Boss? boss2 = null;
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Boss b)
                {
                    boss2 = b;
                }
            }
            Check(boss2 != null && boss2.BossType == 2, "Boss 轮换：第 2 只为游击型");
            boss2!.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (buffUi.Visible)
            {
                buffUi.PickBuff("rapid_fire");
            }

            // 3.3 Boss-3 母舰型召唤小怪
            spawner.SpawnBoss();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Boss? boss3 = null;
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Boss b)
                {
                    boss3 = b;
                }
            }
            Check(boss3 != null && boss3.BossType == 3, "Boss 轮换：第 3 只为母舰型");
            var b3 = boss3!;
            b3.Position = new Vector2(b3.Position.X, b3.FightAnchorY());  // 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
            await Coroutine.WaitSeconds(this, 7.0);  // 首次召唤在 6s
            var minionFound = false;
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Enemy)
                {
                    minionFound = true;
                }
            }
            Check(minionFound, "母舰型 Boss 召唤小怪");
            boss3!.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (buffUi.Visible)
            {
                buffUi.PickBuff("rapid_fire");
            }
            // 清理小怪与弹幕
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Enemy || (child is Bullet b && !b.IsPlayerBullet))
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 3.4 狂暴阶段：血量 <30% 触发，序列后射速 ×1.5
            spawner.SpawnBoss(1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Boss? boss4 = null;
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Boss b)
                {
                    boss4 = b;
                }
            }
            var b4 = boss4!;
            b4.Position = new Vector2(b4.Position.X, b4.FightAnchorY());
            await Coroutine.WaitSeconds(this, 0.5);
            b4.TakeDamage((int)(b4.MaxHp * 0.75f));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(boss4!.IsEnraged(), "Boss 血量 <30% 触发狂暴");
            Check(boss4!.BaseModulateColor() != Colors.White, "狂暴贴图变红");
            // 狂暴完整序列（锁血/冻结玩家/轨道攻击）断言见 boss_enrage_test；
            // 这里中止序列后验证永久射速倍率，并快进 main 子弹时间等 time_scale 恢复
            boss4!.AbortEnrageSequence();
            main.SetBulletTime(0.05f);
            for (int i = 0; i < 30; i++)
            {
                await ToSignal(GetTree().CreateTimer(0.1, true, false, true), SceneTreeTimer.SignalName.Timeout);
                if (Mathf.IsEqualApprox(Engine.TimeScale, 1.0f))
                {
                    break;
                }
            }
            // 狂暴射速：计时器流速 ×1.5 → 0.5s 墙钟消耗 0.75s 计时
            boss4!.SetFireTimer(1.6f);
            await Coroutine.WaitSeconds(this, 0.5);
            Check(boss4!.FireTimer() < 1.0f, "狂暴后射速提升");
            boss4!.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (buffUi.Visible)
            {
                buffUi.PickBuff("rapid_fire");
            }
            GetTree().Paused = false;
            // 清理弹幕
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Bullet b && !b.IsPlayerBullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 3.5 新 buff 抽查：穿透弹 / 爆炸弹作用于玩家子弹
            gs.AddBuff("piercing");
            gs.AddBuff("explosive");
            player.Fire(Vector2.Down);
            Bullet? fired = null;
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Bullet b && b.IsPlayerBullet)
                {
                    fired = b;
                }
            }
            Check(fired != null && fired.Pierce == 1 && fired.Explosive, "穿透/爆炸弹 buff 作用于子弹");
            if (fired != null)
            {
                fired.QueueFree();
            }

            // 3.6 慢速力场：全局敌机移速 ×0.8（A13，敌弹不受影响）
            var slowE = EnemyScene.Instantiate<Enemy>();
            slowE.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            slowE.CanShoot = false;
            slowE.Speed = 100.0f;
            slowE.Position = new Vector2(960.0f, 100.0f);
            GetNode("Main").AddChild(slowE);
            await Coroutine.WaitSeconds(this, 0.5);
            float slowD1 = slowE.Position.Y - 100.0f;
            slowE.QueueFree();
            gs.AddBuff("slow_field");
            var slowE2 = EnemyScene.Instantiate<Enemy>();
            slowE2.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            slowE2.CanShoot = false;
            slowE2.Speed = 100.0f;
            slowE2.Position = new Vector2(960.0f, 100.0f);
            GetNode("Main").AddChild(slowE2);
            await Coroutine.WaitSeconds(this, 0.5);
            float slowD2 = slowE2.Position.Y - 100.0f;
            slowE2.QueueFree();
            Check(slowD1 > 20.0f && slowD2 < slowD1 * 0.9f, "慢速力场全局敌机移速 ×0.8");

            // 3.7 相位冲刺：触发、无敌、位移、冷却
            gs.AddBuff("phase_dash");
            double healthBefore = gs.Health;
            player.SetSinceDamage(0.0f);  // 冻结被动回血，避免干扰 HP 断言
            var posBefore = player.Position;
            player.SetInvincible(0.0f);
            Input.ActionPress("dash");
            // 无头模式需等物理帧而非 idle 帧，just_pressed 才可靠到达 _physics_process
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease("dash");
            Check(player.IsDashing(), "相位冲刺触发");
            player.TakeDamage(1.0f, Vector2.Inf);
            Check(gs.Health == healthBefore, "冲刺期间无敌");
            await Coroutine.WaitSeconds(this, 0.4);
            Check(player.Position.DistanceTo(posBefore) > 100.0f, "冲刺位移约 200px");
            Check(player.DashCooldownRemaining() > 0.0f, "冲刺进入冷却");

            // 3.8 燃料：加速消耗 / 松手回复
            float fuelBefore = player.FuelAmount();
            Input.ActionPress("boost");
            Input.ActionPress("move_up");
            await Coroutine.WaitSeconds(this, 0.5);
            Input.ActionRelease("boost");
            Input.ActionRelease("move_up");
            Check(player.FuelAmount() < fuelBefore, "加速消耗燃料");
            float fuelAfterBoost = player.FuelAmount();
            await Coroutine.WaitSeconds(this, 0.5);
            Check(player.FuelAmount() > fuelAfterBoost, "燃料回复");
            Check(player.FuelDrainRate() == 35.0f, "无高效推进时消耗 35/s");
            gs.AddBuff("efficient_boost");
            Check(Mathf.IsEqualApprox(player.FuelDrainRate(), 35.0f * 0.75f), "高效推进消耗 -25%");

            // 3.9 精英击毁：高分奖励（得分制，无掉落物）
            var elite = EnemyScene.Instantiate<Enemy>();
            elite.Setup(spawner.ELITE_TYPES[0], "straight", 1.0f);
            elite.Position = new Vector2(960.0f, 400.0f);
            GetNode("Main").AddChild(elite);
            int scoreBeforeElite = gs.Score;
            elite.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(gs.Score >= scoreBeforeElite + spawner.ELITE_TYPES[0]["score"].AsInt32(), "精英击毁得分奖励");
            // 得分可能再次触发里程碑，关闭之
            if (buffUi.Visible)
            {
                buffUi.PickBuff("rapid_fire");
            }
            GetTree().Paused = false;

            // 3.10 母舰（原作对齐）：蓄力召唤 → 到位自动对接（点吸附）→ 驻留弹匣 → 提前离舰
            // 清理可能残留的敌弹，避免干扰生命断言
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Bullet b && !b.IsPlayerBullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            player.SetInvincible(0.0f);
            player.SetLastHitFrame(-1);
            player.TakeDamage(10.0f, Vector2.Inf);
            Check(gs.Health == 90.0, "母舰测试前置：受击 -10 HP");
            player.SetFuel(10.0f);
            // 长按蓄力：1s 松手取消，不进冷却
            Input.ActionPress("dock");
            await Coroutine.WaitSeconds(this, 1.0);
            Check(main.Charging() && main.Mothership() == null, "蓄力中未召唤");
            // 蓄力虚影：复用真实母舰场景实例（贴图/尺寸/炮塔一致），仅半透明预告、禁用状态机
            Check(main.ChargeGhost().Visible, "蓄力中显示母舰虚影");
            Check(
                main.ChargeGhost().GetNode<Sprite2D>("Sprite2D").Texture.ResourcePath == "res://assets/sprites/mothership.png",
                "虚影贴图 = 真实母舰贴图"
            );
            Check(main.ChargeGhost().HasNode("TurretL") && main.ChargeGhost().HasNode("TurretR"), "虚影含双炮塔");
            Check(!main.ChargeGhost().IsPhysicsProcessing(), "虚影禁用状态机（不移动/不对接）");
            Check(main.ChargeGhost().Modulate.A < 0.5f, "虚影半透明调制");
            Input.ActionRelease("dock");
            await Coroutine.WaitSeconds(this, 0.2);
            Check(!main.Charging() && main.DockCooldown() <= 0.0f, "松手取消蓄力不进冷却");
            // 蓄满 3s 召唤：弹出机库小窗 → 小窗结束后穿梭门+母舰穿出
            Input.ActionPress("dock");
            await Coroutine.WaitSeconds(this, 3.3);
            Input.ActionRelease("dock");
            Check(main.SummonWindow() != null, "蓄力满 3s 弹出机库小窗");
            main.SummonWindow()!.Skip();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.Mothership() != null, "小窗结束后召唤母舰");
            var ms = main.Mothership()!;
            ms.SetStateTimer(ms.WARP_IN_TIME);  // 快进穿梭入场（0.8s）
            // 到位即自动对接（无区域判定，点吸附补间）
            var tgt = EnemyScene.Instantiate<Enemy>();
            tgt.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            tgt.CanShoot = false;
            tgt.Hp = 9999;  // 靶机不死，保证场内始终有目标
            tgt.Position = new Vector2(960.0f, 500.0f);
            main.AddChild(tgt);
            await Coroutine.WaitSeconds(this, 0.5);
            Check(ms.GetState() == Mothership.State.DOCKING, "穿梭入场到位后自动对接");
            Check(tgt.SummonSlowTimer() > 0.0f, "减速带命中敌机（短时减速）");
            Check(player.IsInputLocked(), "对接开始即锁输入");
            Check(player.InvincibleRemaining() > 100.0f, "对接开始即无敌（无敌窗口前移）");
            // 回收牵引期火力掩护（DOCKING 态即开火，不耗驻留弹匣）
            var dockFire = false;
            foreach (var child in main.GetChildren())
            {
                if (child is Bullet b && b.IsPlayerBullet && b.ScoreScale < 1.0f)
                {
                    dockFire = true;
                }
            }
            Check(dockFire, "回收牵引期火力掩护开火");
            // 对接 1.5s + 补给 0.5s 后进入驻留
            await Coroutine.WaitSeconds(this, 2.0);
            Check(gs.Health == gs.MaxHealth(), "补给回满生命");
            Check(player.FuelAmount() == player.FuelMax, "补给回满燃料");
            Check(ms.GetState() == Mothership.State.STAY, "进入驻留状态");
            Check(ms.GetMagCells() == 10, "弹匣初始 10 格");
            Check(!player.Visible, "回收完成玩家进入保护舱（隐藏）");
            // 驻留火力：加特林弹丸（score_scale=1/3）+ 导弹（splash 标记）
            await Coroutine.WaitSeconds(this, 0.6);
            var gatlingFound = false;
            var missileFound = false;
            foreach (var child in main.GetChildren())
            {
                if (child is Bullet b && b.IsPlayerBullet && b.ScoreScale < 1.0f)
                {
                    gatlingFound = true;
                    if (b.SplashDamage > 0)
                    {
                        missileFound = true;
                    }
                }
            }
            Check(gatlingFound, "加特林扫射开火");
            Check(missileFound, "导弹齐射开火（A15）");
            // 驻留驾驶：WASD 移动母舰，玩家钉在对接点
            float msXBefore = ms.Position.X;
            Input.ActionPress("move_right");
            await Coroutine.WaitSeconds(this, 0.5);
            Input.ActionRelease("move_right");
            Check(ms.Position.X > msXBefore + 20.0f, "驻留期间 WASD 驾驶母舰");
            Check(player.GlobalPosition.DistanceTo(ms.GlobalPosition + new Vector2(0.0f, ms.DOCK_OFFSET_Y)) < 5.0f, "驾驶时玩家钉在对接点");
            // 驾驶边界钳制：持续左行被钳在视野内（x ≥ 视图左缘 + DRIVE_MARGIN_X）
            // small 档（zoom=1.0）视野最宽，1045→43 约 1000px @180px/s 需 ~5.6s，留足余量
            Input.ActionPress("move_left");
            await Coroutine.WaitSeconds(this, 6.5);
            Input.ActionRelease("move_left");
            Check(Mathf.Abs(ms.Position.X - (gs.ViewWorldRect().Position.X + ms.DRIVE_MARGIN_X)) < 30.0f, "母舰驾驶边界钳制");
            if (buffUi.Visible)
            {
                buffUi.PickBuff("rapid_fire");
            }
            GetTree().Paused = false;
            // 弹匣随时间消耗（驻留已累计 >2s）
            Check(ms.GetMagCells() < 10, "驻留弹匣消耗");
            // ≤4 格警告 + 警告 5s 后强制离舰计时
            ms.SetMagCells(5);
            ms.SetMagCellTimer(0.0f);
            await Coroutine.WaitSeconds(this, 2.3);
            Check(ms.GetMagCells() == 4, "弹匣消耗到 4 格");
            Check(ms.MagWarned(), "弹匣 ≤4 弹出警告");
            Check(ms.WarnEjectTimer() > 0.0f, "警告后启动强制离舰计时");
            // 提前离舰：长按 H 2s，冷却双机制折扣（4→3 格 r=0.3：60×0.88×0.85≈44.9）
            Input.ActionPress("dock");
            await Coroutine.WaitSeconds(this, 1.0);
            Check(main.Hud().EarlyLeaveBox().Visible, "提前离舰蓄力进度条显示");
            Check(main.Hud().EarlyLeaveFill().AnchorRight > 0.3f && main.Hud().EarlyLeaveFill().AnchorRight < 0.7f, "提前离舰进度条进度 ~50%");
            await Coroutine.WaitSeconds(this, 1.4);
            Input.ActionRelease("dock");
            Check((int)ms.GetState() >= Mothership.GetStateRelease(), "提前离舰触发");
            Check(!main.Hud().EarlyLeaveBox().Visible, "提前离舰后进度条隐藏");
            await Coroutine.WaitSeconds(this, 0.6);
            Check(player.InvincibleRemaining() > 1.0f && player.InvincibleRemaining() <= 2.0f, "释放后 2s 保护（重制版 QoL）");
            await Coroutine.WaitSeconds(this, 0.2);
            Check(main.DockCooldown() > 42.5f && main.DockCooldown() < 45.2f, "提前离舰冷却双机制折扣");
            Check(!player.IsInputLocked(), "脱离后输入解锁");
            Check(player.Visible, "释放后玩家出舱恢复显示");
            if (main.Mothership() != null)
            {
                main.Mothership()!.QueueFree();
            }
            // 母舰击杀 1/3 分（100 分敌机 → +33）
            // 组判定清场：FormationCraft/TurretBattery 非 Enemy 子类但注册 enemy 组，
            // 漏清会被 b33 抢先命中造成抖动；在飞流弹一并清掉
            foreach (var child in main.GetChildren())
            {
                if ((child.IsInGroup("enemy") && child is not Boss) || child is Bullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var e33 = EnemyScene.Instantiate<Enemy>();
            e33.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            e33.Hp = 1;
            e33.Position = new Vector2(960.0f, 400.0f);
            main.AddChild(e33);
            var b33 = GD.Load<PackedScene>(BulletScenePath).Instantiate<Bullet>();
            b33.Setup(Vector2.Down, 800.0f, 1, true);
            b33.ScoreScale = 1.0f / 3.0f;
            b33.Position = e33.Position;
            main.AddChild(b33);
            int scoreBefore33 = gs.Score;
            gs.ResetCombo(); // 连击基线归零：断言仅覆盖本次单杀（1/3 分），隔离前置击杀残留
            await Coroutine.WaitSeconds(this, 0.3);
            Check(gs.Score == scoreBefore33 + 33, "母舰击杀 1/3 分");
            if (buffUi.Visible)
            {
                buffUi.PickBuff("rapid_fire");
            }
            GetTree().Paused = false;
            // 警告横幅播完（5s）强制离舰：第二艘母舰，缩短计时确定性验证
            main.SetDockCooldown(0.0f);
            main.SummonMothership();
            main.SummonWindow()!.Skip();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ms2 = main.Mothership()!;
            ms2.SetStateTimer(ms2.WARP_IN_TIME);  // 快进穿梭入场
            await Coroutine.WaitSeconds(this, 2.5);  // 自动对接 + 补给 → 驻留
            Check(ms2.GetState() == Mothership.State.STAY, "第二艘母舰进入驻留");
            ms2.SetMagCells(5);
            ms2.SetMagCellTimer(0.0f);
            await Coroutine.WaitSeconds(this, 2.3);
            Check(ms2.MagWarned(), "第二艘母舰弹匣警告");
            ms2.SetWarnEjectTimer(0.5f);  // 缩短横幅等待，直接验证强制离舰
            await Coroutine.WaitSeconds(this, 1.0);
            Check((int)ms2.GetState() >= Mothership.GetStateRelease(), "警告播完强制离舰（对齐原作）");
            if (main.Mothership() != null)
            {
                main.Mothership()!.QueueFree();
            }

            // 3.11 对局存档：写入 → 清空 → 恢复（游客不存档，切真实用户验证；结束后恢复游客）
            if (!gs.UserExists("smoke_user"))
            {
                gs.CreateUser("smoke_user", "pass123");
            }
            gs.LoginUser("smoke_user");
            int savedScore = gs.Score;
            gs.AddBuff("power_shot");
            gs.Health = 66.0;
            gs.SaveRun(55.0, 12.0);
            Check(gs.HasSave(), "存档文件已写入");
            gs.Score = 0;
            gs.Health = 100.0;
            gs.Buffs.Clear();
            gs.ApplyRunSave(gs.LoadRunData());
            Check(gs.Score == savedScore, "存档恢复分数");
            Check(gs.BuffCount("power_shot") == 2, "存档恢复 buff 层数");
            Check(gs.Health == 66.0, "存档恢复 HP（v2 格式）");

            // 3.12 返航（局内中场整备）：蓄力 → 基地 → 维修 → 继续出击返回同局
            int scoreBeforeHc = gs.Score;
            int powerBefore = gs.BuffCount("power_shot");
            gs.AddRp(5);
            gs.Health = 50.0;
            // 蓄力松手取消
            Input.ActionPress("homecoming");
            await Coroutine.WaitSeconds(this, 0.6);
            Input.ActionRelease("homecoming");
            await Coroutine.WaitSeconds(this, 0.2);
            Check(!main.IsHomecoming() && !GetTree().Paused, "返航蓄力松手取消");
            // 蓄满 1.5s 触发
            Input.ActionPress("homecoming");
            await Coroutine.WaitSeconds(this, 1.7);
            Input.ActionRelease("homecoming");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.IsHomecoming(), "返航触发");
            Check(main.ReturnCinematic() != null && GetTree().Paused, "返航过场播放中（树暂停）");
            // 过场本体由 return_cinematic_test 专测；越过 1.2s 输入宽限后跳过直达基地 UI
            await Coroutine.WaitSeconds(this, 1.4);
            main.SkipReturn();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.BaseUi().Visible && GetTree().Paused, "进入基地整备界面");
            // 维修扣 RP 回满（对齐原作 2RP 回满）
            int rpBefore = gs.Rp;
            main.BaseUi().Repair();
            Check(gs.Rp == rpBefore - 2, "维修扣 2RP");
            Check(gs.Health == gs.MaxHealth(), "维修回满生命");
            // 放一个敌机 + 一枚编队炸弹（长引信不爆）验证轨道打击清场
            var orbitE = EnemyScene.Instantiate<Enemy>();
            orbitE.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            orbitE.CanShoot = false;
            orbitE.Position = new Vector2(400.0f, 300.0f);
            main.AddChild(orbitE);
            var bomb = new FormationBomb();
            bomb.Setup(new Vector2(0.0f, 300.0f), 30.0f, 20, 120.0f);
            bomb.Position = new Vector2(700.0f, 300.0f);
            main.AddChild(bomb);
            // 继续出击 → 轨道打击动画清场后返回同一局
            main.BaseUi().Resume();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 动画本体由 orbital_strike_test 专测；此处缩短时轴，等待命中清场并播完
            if (main.Strike() != null)
            {
                main.Strike()!.DURATION = 0.5f;
            }
            double tStrike = 0.0;
            while (main.Strike() != null && tStrike < 3.0)
            {
                await Coroutine.WaitSeconds(this, 0.1);
                tStrike += 0.1;
            }
            // 继续出击触发战机入场动画（约 1.65s）：等其结束，避免后续物理断言被入场移动锁干扰
            double tEntry = 0.0;
            while (player.IsEntryPlaying() && tEntry < 3.0)
            {
                await Coroutine.WaitSeconds(this, 0.1);
                tEntry += 0.1;
            }
            Check(!GetTree().Paused && !main.IsHomecoming(), "继续出击恢复游戏");
            Check(gs.Score == scoreBeforeHc, "返回同一局：分数保留");
            Check(gs.BuffCount("power_shot") == powerBefore, "返回同一局：buff 保留");
            Check(gs.HasSave(), "返航后存档保留");
            gs.LoginGuest();  // 恢复游客会话（§3.11 起的用户档断言已结束）
            // 注册表驱动清场：非 Boss 实体（Enemy/FormationCraft/事件残留）全清
            var enemyLeft = false;
            foreach (var e in gs.Enemies)
            {
                if (IsInstanceValid(e) && e is not Boss)
                {
                    enemyLeft = true;
                }
            }
            Check(!enemyLeft, "轨道打击清屏（注册表非 Boss 全清）");
            // 弹丸清场：敌弹与编队炸弹全清（FormationBomb 非 Bullet 类，原遍历式清场会漏）
            var bulletLeft = false;
            foreach (var child in main.GetChildren())
            {
                if ((child is Bullet b && !b.IsPlayerBullet) || child is FormationBomb)
                {
                    bulletLeft = true;
                }
            }
            Check(!bulletLeft, "轨道打击清弹（含编队炸弹）");
            // 恢复刷怪会干扰后续断言，重新停掉生成器并清场
            spawner.SetProcess(false);
            foreach (var child in main.GetChildren())
            {
                if (child is Enemy || (child is Bullet b && !b.IsPlayerBullet))
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 3.13 最高分
            gs.HighScore = 0;
            Check(gs.RecordScore(), "首次破纪录");
            Check(gs.HighScore == gs.Score, "最高分已更新");
            gs.Score = 5;
            Check(!gs.RecordScore(), "低分不覆盖最高分");
            gs.Score = gs.HighScore;

            // 3.14 Shift/Ctrl toggle 模式（按一下切换开/关）
            gs.SetShiftToggleMode(true);
            Input.ActionPress("boost");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease("boost");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(player.BoostToggleActive(), "toggle 模式按一下开启加速");
            Input.ActionPress("boost");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease("boost");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(!player.BoostToggleActive(), "toggle 模式再按一下关闭加速");
            gs.SetShiftToggleMode(false);
            gs.SetCtrlToggleMode(true);
            Input.ActionPress("fine_move");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease("fine_move");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(player.FineToggleActive(), "toggle 模式按一下开启微调");
            gs.SetCtrlToggleMode(false);
            player.SetFineToggle(false);

            // 4. 玩家受击至死 → 结算（此时存档存在，死亡应删档）
            player.SetInvincible(0.0f);
            player.SetLastHitFrame(-1);
            player.TakeDamage(9999.0f, Vector2.Inf);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!gs.HasSave(), "死亡后删除存档");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(GetNode<CanvasLayer>("Main/GameOverUI").Visible, "Game Over 面板显示");
            Check(GetTree().Paused, "Game Over 时游戏暂停");

            // 5. 暂停面板
            GetTree().Paused = false;
            var pauseUi = GetNode<PauseUi>("Main/PauseUI");
            pauseUi.Toggle();
            Check(pauseUi.Visible && GetTree().Paused, "Esc 暂停面板");
            pauseUi.Toggle();
            Check(!pauseUi.Visible && !GetTree().Paused, "Esc 恢复");

            // 5.1 设置面板 opener 回归：暂停面板打开设置时必须让位，返回/Esc 后恢复打开者
            var settingsUi = GetNode<SettingsUi>("Main/SettingsUI");
            pauseUi.Toggle();
            pauseUi.OpenSettings();
            Check(!pauseUi.Visible && settingsUi.Visible, "暂停→设置：暂停面板让位");
            Check(settingsUi.Opener() == pauseUi, "暂停→设置：opener 记录为暂停面板");
            settingsUi.Back();
            Check(pauseUi.Visible && !settingsUi.Visible, "设置返回：恢复暂停面板");
            pauseUi.Toggle();
            Check(!pauseUi.Visible, "设置回归：暂停面板已关闭");

            // 6. 迭代 3.3 玩家侧：瞄准辅助 / 冲刺耗燃料 / Ctrl 微调
            // 第 4 节玩家已受击至死：复活以便继续测试（不重开 hitbox，避免杂散碰撞）
            player.SetDead(false);
            player.SetInvincible(999.0f);
            player.Show();
            player.SetPhysicsProcess(true);
            gs.Health = gs.MaxHealth();
            GetNode<CanvasLayer>("Main/GameOverUI").Hide();
            GetTree().Paused = false;

            // 6.1 辅助瞄准（P1-1 新语义）：标记敌 + 准星入框 → 出膛弹追踪该敌；未入框 → 朝准星直射
            // （瞄准点用 aim_point_override 注入：相机震动 offset 会让合成鼠标事件的世界落点漂移）
            player.Position = new Vector2(960.0f, 800.0f);
            player.Velocity = Vector2.Zero;
            var aimE = EnemyScene.Instantiate<Enemy>();
            aimE.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            aimE.CanShoot = false;
            aimE.Hp = 9999;  // 防止被测试弹击毁触发里程碑
            aimE.aim_marked = true;  // 出生标记为 25% 随机掷点（mark_ratio 0.25），测试强制置位保证确定性
            aimE.Position = player.Position + new Vector2(0.0f, -300.0f);
            main.AddChild(aimE);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var frames = gs.AimFrameLayer as AimFrameLayer;  // 辅助框覆盖层已登记 GameState
            Check(frames != null, "辅助框覆盖层已登记 GameState");
            // 准星置入标记敌框内（框心偏移 20px，仍在 碰撞半径+frame_pad 内）
            player.AimPointOverride = aimE.GlobalPosition + new Vector2(20.0f, 0.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(frames!.MarkedTargetAt(player.AimPoint()) == aimE, "准星入框命中标记敌");
            player.SetAutoFire(true);
            player.ResetFireCooldown();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            player.SetAutoFire(false);
            Bullet? ab = null;
            foreach (var child in main.GetChildren())
            {
                if (child is Bullet b && b.IsPlayerBullet)
                {
                    ab = b;
                    break;
                }
            }
            Check(ab != null, "入框期间自动开火");
            if (ab != null)
            {
                Check(ab.HomingTarget == aimE, "入框出膛弹绑定追踪目标");
                var dir0 = ab.Direction;
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                // 直线弹方向恒定；方向发生偏转即追踪转向生效（lerp_angle 恒朝目标）
                Check(Mathf.Abs(Mathf.AngleDifference(ab.Direction.Angle(), dir0.Angle())) > 0.005f, "追踪弹出膛后向目标转向");
                ab.QueueFree();
            }
            // 准星不在任何标记框内 → 朝准星直射，无追踪绑定
            player.AimPointOverride = new Vector2(200.0f, 950.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(frames!.MarkedTargetAt(player.AimPoint()) == null, "准星出框无命中目标");
            player.SetAutoFire(true);
            player.ResetFireCooldown();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            player.SetAutoFire(false);
            Bullet? ab2 = null;
            foreach (var child in main.GetChildren())
            {
                if (child is Bullet b && b.IsPlayerBullet)
                {
                    ab2 = b;
                    break;
                }
            }
            Check(ab2 != null && ab2.HomingTarget == null, "未入框出膛弹无追踪目标");
            if (ab2 != null)
            {
                var want2 = (player.AimPoint() - player.GlobalPosition).Normalized();
                Check(ab2.Direction.Dot(want2) > 0.99f, "未入框子弹朝准星直射");
                ab2.QueueFree();
            }
            player.AimPointOverride = Vector2.Inf;
            aimE.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 6.1b 辅助瞄准强度三档：框内边距/追踪速率/入框吸附系数随档位切换，无关闭档（非法档位拒绝）
            float defaultPad = frames!.FramePad();
            double defaultTurn = player.AimAssistParams()["homing_turn_rate"].AsDouble();
            double defaultStick = player.AimAssistParams()["stick_factor"].AsDouble();
            gs.SetAimAssistLevel("low");
            Check(
                (
                    frames!.FramePad() < defaultPad
                    && player.AimAssistParams()["homing_turn_rate"].AsDouble() < defaultTurn
                    && player.AimAssistParams()["stick_factor"].AsDouble() > defaultStick
                ),
                "弱档：框内边距与追踪速率降低、吸附减弱"
            );
            gs.SetAimAssistLevel("high");
            Check(
                (
                    frames!.FramePad() > defaultPad
                    && player.AimAssistParams()["homing_turn_rate"].AsDouble() > defaultTurn
                    && player.AimAssistParams()["stick_factor"].AsDouble() < defaultStick
                ),
                "强档：框内边距与追踪速率提高、吸附增强"
            );
            gs.SetAimAssistLevel("off");
            Check(gs.AimAssistLevel == new StringName("high"), "辅助瞄准无关闭档（非法档位被拒绝）");
            gs.SetAimAssistLevel("medium");
            Check(
                (
                    Mathf.IsEqualApprox(frames!.FramePad(), defaultPad)
                    && Mathf.IsEqualApprox(player.AimAssistParams()["homing_turn_rate"].AsDouble(), defaultTurn)
                    && Mathf.IsEqualApprox(player.AimAssistParams()["stick_factor"].AsDouble(), defaultStick)
                ),
                "恢复中档后参数还原"
            );

            // 6.1c 辅助瞄准算法优化（P1-3）：准星磁吸 / 框外锥形弱追踪 / 输入反比 / 距离衰减
            var aimE2 = EnemyScene.Instantiate<Enemy>();
            aimE2.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            aimE2.CanShoot = false;
            aimE2.Hp = 9999;
            aimE2.aim_marked = true;
            aimE2.Position = player.Position + new Vector2(0.0f, -300.0f);
            main.AddChild(aimE2);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 档位梯度：磁吸/锥形参数 low < high，恢复 medium
            gs.SetAimAssistLevel("low");
            var paLow = player.AimAssistParams();
            gs.SetAimAssistLevel("high");
            var paHigh = player.AimAssistParams();
            Check(
                (
                    paLow["magnet_range"].AsDouble() < paHigh["magnet_range"].AsDouble()
                    && paLow["magnet_strength"].AsDouble() < paHigh["magnet_strength"].AsDouble()
                    && paLow["cone_angle_deg"].AsDouble() < paHigh["cone_angle_deg"].AsDouble()
                    && paLow["cone_strength"].AsDouble() < paHigh["cone_strength"].AsDouble()
                ),
                "弱/强档：磁吸与锥形参数梯度"
            );
            gs.SetAimAssistLevel("medium");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 磁吸 API（纯函数，合成输入增量；目标在玩家上方 300 < falloff.peak，衰减不干扰）
            var c2 = aimE2.GlobalPosition;
            float half2 = frames!.FrameHalfSize(aimE2);
            Check(frames!.MagnetPull(c2 + new Vector2(half2 + 40.0f, 0.0f), Vector2.Zero) == Vector2.Zero, "静止输入无磁吸");
            Check(frames!.MagnetPull(c2 + new Vector2(half2 + 40.0f, 0.0f), new Vector2(1.0f, 0.0f)) == Vector2.Zero, "微动低于阈值无磁吸");
            Check(frames!.MagnetPull(c2 + new Vector2(half2 + 40.0f, 0.0f), new Vector2(60.0f, 0.0f)) == Vector2.Zero, "高速输入无磁吸（输入优先）");
            Check(frames!.MagnetPull(c2 + new Vector2(10.0f, 0.0f), new Vector2(6.0f, 0.0f)) == Vector2.Zero, "框内点不触发磁吸（归 stickiness）");
            var pull = frames!.MagnetPull(c2 + new Vector2(half2 + 40.0f, 0.0f), new Vector2(6.0f, 0.0f));
            double lim = player.AimAssistParams()["magnet_max_speed"].AsDouble();
            Check(pull.Length() > 0.5 && pull.Length() <= lim, "慢速输入磁吸量级在 (0, 拉速上限]");
            Check(pull.Normalized().Dot(Vector2.Left) > 0.99f, "磁吸方向指向框外目标（目标在左侧）");
            var pullNear = frames!.MagnetPull(c2 + new Vector2(half2 + 10.0f, 0.0f), new Vector2(6.0f, 0.0f));
            var pullFar = frames!.MagnetPull(c2 + new Vector2(half2 + 90.0f, 0.0f), new Vector2(6.0f, 0.0f));
            Check(pullNear.Length() > pullFar.Length(), "磁吸随框沿距线性衰减");
            // 距离衰减：目标移至玩家上方 1200（falloff≈0.44），同输入与框沿距拉量显著下降
            aimE2.Position = player.Position + new Vector2(0.0f, -1200.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var pullFarD = frames!.MagnetPull(aimE2.GlobalPosition + new Vector2(half2 + 40.0f, 0.0f), new Vector2(6.0f, 0.0f));
            Check(pullFarD.Length() < pull.Length() * 0.6, "磁吸随玩家-目标距离衰减");
            Check(
                (
                    player.AimDistFalloff(300.0f) == 1.0f
                    && player.AimDistFalloff(900.0f) < 1.0f
                    && player.AimDistFalloff(900.0f) > player.AimDistFalloff(1300.0f)
                    && Mathf.IsEqualApprox(player.AimDistFalloff(1500.0f), (float)player.AimAssistParams()["falloff_min"].AsDouble())
                ),
                "距离衰减曲线单调（400 平台 / 1400 下限）"
            );
            // 框外锥形弱追踪：目标复位玩家上方 400（falloff=1.0），准星置框沿外测角距（中档锥角 6°）
            aimE2.Position = player.Position + new Vector2(0.0f, -400.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            double fullRate = player.AimAssistParams()["homing_turn_rate"].AsDouble();
            // 框沿外 2px（角距 ~4.5° < 6°）→ 弱绑定
            player.AimPointOverride = aimE2.GlobalPosition + new Vector2(half2 + 2.0f, 0.0f);
            var wdir = (player.AimPoint() - player.GlobalPosition).Normalized();
            player.ResetFireCooldown();
            player.Fire(wdir);
            Bullet? wb = null;
            foreach (var child in main.GetChildren())
            {
                if (child is Bullet b && b.IsPlayerBullet)
                {
                    wb = b;
                    break;
                }
            }
            Check(wb != null && wb.HomingTarget == aimE2, "框外锥内弱追踪绑定目标");
            float wbRate = wb != null ? wb.HomingTurnRate : 0.0f;
            if (wb != null)
            {
                Check(wbRate > 0.1f && wbRate < fullRate * 0.75, "弱追踪转向率介于 (0, 全追踪) 之间");
                wb.QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);  // queue_free 帧末才删除：等旧弹离场再扫下一发
            // 框沿外 8px（角距 ~5.4°）→ 转向率更低（角距渐变）
            player.AimPointOverride = aimE2.GlobalPosition + new Vector2(half2 + 8.0f, 0.0f);
            var wdir2 = (player.AimPoint() - player.GlobalPosition).Normalized();
            player.ResetFireCooldown();
            player.Fire(wdir2);
            Bullet? wb2 = null;
            foreach (var child in main.GetChildren())
            {
                if (child is Bullet b && b.IsPlayerBullet)
                {
                    wb2 = b;
                    break;
                }
            }
            // L16：弱断言修复——分支内断言补生成前置（弹未生成时用例不得静默通过）
            Check(wb2 != null, "框沿外 8px 处生成弱追踪弹");
            if (wb2 != null)
            {
                Check(wb2.HomingTurnRate < wbRate, "锥内转向率随角距渐变（框缘更低）");
                wb2.QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);  // 同上：等 wb2 离场
            // 框沿外 30px（角距 ~8.5° > 6°）→ 直射无追踪
            player.AimPointOverride = aimE2.GlobalPosition + new Vector2(half2 + 30.0f, 0.0f);
            var wdir3 = (player.AimPoint() - player.GlobalPosition).Normalized();
            player.ResetFireCooldown();
            player.Fire(wdir3);
            Bullet? wb3 = null;
            foreach (var child in main.GetChildren())
            {
                if (child is Bullet b && b.IsPlayerBullet)
                {
                    wb3 = b;
                    break;
                }
            }
            Check(wb3 != null && wb3.HomingTarget == null, "锥外出膛弹无追踪（直射）");
            if (wb3 != null)
            {
                wb3.QueueFree();
            }
            player.AimPointOverride = Vector2.Inf;
            aimE2.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 6.2 冲刺耗燃料：消耗满值的 25%，不足时禁用
            player.Position = new Vector2(960.0f, 540.0f);
            player.SetFuel(player.FuelMax);
            player.SetDashCooldown(0.0f);
            Input.ActionPress("dash");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease("dash");
            Check(player.IsDashing(), "燃料充足时冲刺可触发");
            Check(Mathf.Abs(player.FuelAmount() - player.FuelMax * 0.75f) < 3.0f, "冲刺消耗约 25% 燃料");
            await Coroutine.WaitSeconds(this, 0.4);  // 等冲刺结束
            player.SetFuel(player.FuelMax * 0.2f);
            player.SetDashCooldown(0.0f);
            Input.ActionPress("dash");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease("dash");
            Check(!player.IsDashing(), "燃料不足 25% 时禁用冲刺");

            // 6.3 Ctrl 微调：移速 ×0.35
            player.Position = new Vector2(960.0f, 540.0f);
            Input.ActionPress("move_right");
            await Coroutine.WaitSeconds(this, 0.5);
            float fullSpeed = player.Velocity.Length();
            Input.ActionPress("fine_move");
            await Coroutine.WaitSeconds(this, 0.5);
            float fineSpeed = player.Velocity.Length();
            Input.ActionRelease("fine_move");
            Input.ActionRelease("move_right");
            Check(fullSpeed > player.MaxSpeed * 0.9f, "无微调时接近满速");
            Check(Mathf.Abs(fineSpeed - player.MaxSpeed * 0.35f) < 25.0f, "Ctrl 按住移速 ×0.35");

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.LogoutUser();
            gs.DeleteSave();
            if (Godot.FileAccess.FileExists(gs.UserDbSavefileFor("smoke_user")))
            {
                Godot.DirAccess.RemoveAbsolute(gs.UserDbSavefileFor("smoke_user"));
            }
            // 2026-08-06 审计：还原原始 profile（难度/瞄准辅助/切换模式等设置项）——
            // 原「恢复默认难度」覆盖用户原档
            RestoreProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"SMOKE TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"SMOKE TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
