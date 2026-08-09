using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 全局状态与信号总线：分数、击杀、生命、难度乘数、已选 buff。
/// M7 全量迁移（2026-08-09 自 autoload/game_state.gd）：唯一 autoload（project.godot 由主代理
/// 切换为 *res://csharp/godot/GameState.cs）。
/// 公开 API：PascalCase 主体；UPPER_SNAKE 常量同时提供同名实例属性（GDScript 经实例零适配
/// 可读）与静态 GetXxx() 访问器（规则 19：静态字段禁持 Godot 对象——集合常量访问器每次
/// 内部服务全部 C# typed 直调（U13：SaveManager/UserDB/TaskPool 的 snake 动态派发已清零，
/// snake 桥已删除——原"M7 过渡"注记失效）。
/// 信号迁移：C# [Signal] 注册名为 PascalCase（ScoreChanged 等），GDScript/csharp 连接方
/// （test/*.gd 的 connect 与 csharp 侧 Connect("snake")）需改连 PascalCase 名（主代理集中适配）。
/// 数值精度：GDScript float 为 64 位——纯标量（health/difficulty_multiplier 等）用 double
/// 逐位等价；仅与引擎 32 位 API（Vector2/Rect2/音量等）交互处显式 (float) 转换。
/// </summary>
public partial class GameState : Node
{
    // ---------------- 信号（原 GDScript snake 名 → C# PascalCase 注册名） ----------------

    /// <summary>得分变化。</summary>
    [Signal]
    public delegate void ScoreChangedEventHandler(int newScore);

    [Signal]
    public delegate void HealthChangedEventHandler(double newHealth);

    [Signal]
    public delegate void DifficultyChangedEventHandler(double newMultiplier);

    [Signal]
    public delegate void DifficultySelectedEventHandler(StringName difficulty);

    [Signal]
    public delegate void MilestoneReachedEventHandler(int score);

    [Signal]
    public delegate void PlayerDiedEventHandler();

    /// <summary>玩家实际结算受击（无敌/闪避/单帧守卫未结算不发）：Meta HUD 受击层数据源。
    /// U16：参数统一 float（发射/监听均为 float，原 double 声明不一致）。</summary>
    [Signal]
    public delegate void PlayerDamagedEventHandler(float amount, Vector2 fromPos);

    [Signal]
    public delegate void ScreenShakeEventHandler(double strength);

    /// <summary>RP 经济/任务/路线信号：暂无消费方（base_console 拉取驱动），保留 API 供未来事件驱动</summary>
    [Signal]
    public delegate void RpChangedEventHandler(int newRp);

    [Signal]
    public delegate void MissionCompletedEventHandler(StringName id);

    [Signal]
    public delegate void RouteChosenEventHandler(StringName line, StringName buffId);

    [Signal]
    public delegate void KeyBindingsChangedEventHandler();

    [Signal]
    public delegate void LocaleChangedEventHandler();

    [Signal]
    public delegate void ViewZoomChangedEventHandler(double factor);

    [Signal]
    public delegate void WindowSizeChangedEventHandler(StringName level);

    [Signal]
    public delegate void AimAssistChangedEventHandler(StringName level);

    [Signal]
    public delegate void ReduceFlashChangedEventHandler(bool enabled);

    [Signal]
    public delegate void MouseLockChangedEventHandler(bool enabled);

    /// <summary>P0-1 手柄设置：右摇杆瞄准灵敏度 + 摇杆死区（profile 持久化；变更时广播供 player 重读）</summary>
    [Signal]
    public delegate void JoySettingsChangedEventHandler(double aimSpeed, double deadzone);

    /// <summary>buff 层数任何变动（选取/路线合并/存档恢复/重开清空）后发出，驱动外观刷新</summary>
    [Signal]
    public delegate void BuffsChangedEventHandler();

    /// <summary>实体注册信号转发（docs/ENTITY_MANAGER.md：新功能订阅口，监听 EntityManager）</summary>
    [Signal]
    public delegate void EntityRegisteredEventHandler(Node node);

    [Signal]
    public delegate void EntityUnregisteredEventHandler(Node node);

    /// <summary>触屏虚拟控件开关变化（mobile touch；Main 联动 VirtualControls 启用）</summary>
    [Signal]
    public delegate void TouchControlsChangedEventHandler(bool enabled);

    /// <summary>PS 布局适配（P0-1 延伸）：SDL 标准位置（JOY_BUTTON_A=底部等）跨 Xbox/PS 一致，
    /// 仅物理标签不同——按已连接手柄 GUID/名称检测布局，供 UI/文档显示对应标签</summary>
    [Signal]
    public delegate void JoyLayoutChangedEventHandler(StringName layout);

    /// <summary>刷新点数（RefreshPoints）变化。</summary>
    [Signal]
    public delegate void RefreshPointsChangedEventHandler(int points);

    // ---------------- 常量（private const + 同名 UPPER_SNAKE 实例属性 + 静态 GetXxx() 访问器） ----------------

    /// <summary>得分总量上限（P4 防御：手改 difficulty score 倍率防 int64 溢出；正常对局远达不到）</summary>
    private const int ScoreCapValue = 1_000_000_000;

    /// <summary>难度档位表（开始面板选择，profile 持久化；对齐原作 settings.py DIFFICULTY_SETTINGS）
    /// hp/speed/spawn 为敌机数值与刷怪间隔倍率；score 为分数倍率（add_score 统一乘算）；
    /// spread_cap 为 spread 弹种敌机同屏上限；milestone 为里程碑阈值倍率
    /// （原作阈值与分数同倍 ×1/×2/×3，此处按设计取 ×1/×1/×1.5，避免高难 Buff 节奏过稀）；
    /// regen_delay/regen_rate 为被动回血（对齐原作 settings.py HEALTH_REGEN）：
    /// 距上次受伤 regen_delay 秒起每秒回 regen_rate HP（原作延迟不重置为疑似 bug，本版受伤即重置）。</summary>
    public Godot.Collections.Dictionary DIFFICULTY_DEFS { get; set; } = BuildDifficultyDefs();

    /// <summary>难度档位顺序（开始面板选择顺序）。</summary>
    public Godot.Collections.Array<StringName> DIFFICULTY_ORDER { get; } = new()
    {
        new StringName("easy"),
        new StringName("medium"),
        new StringName("hard"),
    };

    /// <summary>里程碑阈值曲线（对齐原作 constants.py GameBalanceConstants 算法）：
    /// 首循环 8 档基础阈值，之后每循环的档差按 ×1.35^cycle 放大（阈值单调不回退）。</summary>
    public Godot.Collections.Array<int> MILESTONE_BASE { get; } = new()
    {
        3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000,
    };

    private const double MilestoneCycleMultValue = 1.35;

    // ---------------- 全局数值配置中心 ----------------
    // A2 阶段 1：balance.json 的加载/查询/纯数值 ramp 已剥离到 BalanceService（组合委托）。
    // 缺失/损坏时全部回退脚本默认值；访问统一走 GameState.cfg("分层.路径", 默认值)；
    // 热路径在各自 _ready 缓存进成员变量。

    private const string BalancePathValue = "res://data/balance.json";
    public string BALANCE_PATH => BalancePathValue;

    /// <summary>A2 组合服务（均非 autoload，保持"唯一 autoload：GameState"约定；GameState 委托）
    /// M2（2026-08-08）：BalanceService/SfxPlayer/EntityManager 迁 C#，C# 侧 typed 直调
    /// （原 GDScript 经脚本资源实例化）。</summary>
    private readonly BalanceService _balanceService = new();

    /// <summary>V 系列（U19 注释失实清理）：M7 已 typed 直调（原「GDScript 薄壳」描述删除）。</summary>
    private readonly SaveManager _saveManager = new();

    private readonly SfxPlayer _sfxPlayer = new();

    /// <summary>实体注册信号转发（docs/ENTITY_MANAGER.md：新功能订阅口，监听 EntityManager）。</summary>
    /// <summary>统一实体管理器（C# typed；docs/ENTITY_MANAGER.md）。</summary>
    private readonly EntityManager _registry = new();

    /// <summary>迷雾事件管理器（2026-08-05 任务轮换/迷雾事件系统）：全局单例，挂 GameState 下
    /// 维持唯一 autoload 约定；对局中概率触发干扰事件（触发纪律/信号解耦见脚本头注释）</summary>
    private readonly FogEventManager _fogEvents = new();

    /// <summary>统一游戏事件管理器（docs/EVENT_MANAGER.md）：批量管理全部随机游戏事件（迷雾 +
    /// 遭遇）；fog 组经迷雾门面接线，encounter 组由 main 注册——见 scripts/event_manager.gd</summary>
    private readonly GameEventManager _events = new();

    /// <summary>2026-08-04 账户系统：本地用户数据库（M7 已 typed 直调；原「GDScript 薄壳」描述删除）。</summary>
    private readonly UserDB _userDb = new();

    /// <summary>2026-08-07：进程曲线 C# 桥（ProgressionInterop → InfiAir.Core.Progression 纯函数）——
    /// milestone_threshold / _recompute_difficulty / apply_run_save 批量推进转发，语义逐位等价</summary>
    private readonly ProgressionInterop _progression = new();

    /// <summary>生效的里程碑表（默认值见 const，可被 balance.json 覆盖）</summary>
    public Godot.Collections.Array<int> MilestoneBase { get; set; } = new()
    {
        3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000,
    };

    public double MilestoneCycleMult { get; set; } = 1.35;

    /// <summary>全局机体尺寸缩放（balance.json 顶层 world_scale；0.4 = 当前默认观感，2026-07-31 由 1/3 上调）。
    /// 机体尺寸族数值（贴图 scale/碰撞 radius/机体偏移/随机体特效比例）在 json/tscn/脚本回退中
    /// 一律存设计值（1.0 基准），实体在 _ready()/setup() 统一乘本系数后应用；游戏性范围族不乘。
    /// 回退默认值须与 balance.json 一致（损坏/缺键时全局比例不错位）。</summary>
    public double WorldScale { get; set; } = 0.4;

    private void LoadBalance()
    {
        _balanceService.Load(BalancePathValue);
    }

    /// <summary>配置字典是否已加载（缺失/损坏 JSON 时为 false，全部回退脚本默认值；测试/诊断用）</summary>
    public bool HasBalance() => !_balanceService.IsEmpty();

    /// <summary>A7 遗留清理：重新加载并应用 balance.json（测试/诊断注入用；运行时只 _ready 走一次）</summary>
    public void ReloadBalance()
    {
        LoadBalance();
        ApplyBalance();
        // P4（2026-08-05）：事件管理器配置联动重载——原实现只刷平衡缓存，事件触发策略/
        // fog 配置停留旧值，诊断/测试注入路径与运行时不一致
        if (_events != null && GodotObject.IsInstanceValid(_events))
        {
            _events.ReloadConfig();
        }
    }

    /// <summary>统一配置访问：路径如 "player.fuel.drain"。缺键/类型不符回退 default。委托 BalanceService。</summary>
    public Variant Cfg(string path, Variant defaultValue) => _balanceService.Cfg(path, defaultValue);

    private void ApplyBalance()
    {
        // H16（健壮性审核）：world_scale 域校验——0/负值使机体贴图/碰撞归零或镜像翻转，钳制下限
        WorldScale = Mathf.Max(Cfg("world_scale", WorldScale).AsDouble(), 0.01);
        // C03 修复：milestones.base 须为非空数组，否则下游 milestone_threshold 除零
        // C18：显式转 Array[int]（cfg 返回 Variant，typed 赋值需转换）
        var baseV = Cfg("milestones.base", BuildMilestoneBase());
        var baseArr = new Godot.Collections.Array<int>();
        if (baseV.VariantType == Variant.Type.Array && baseV.AsGodotArray().Count > 0)
        {
            foreach (var v in baseV.AsGodotArray())
            {
                // 元素级判型（2026-08-03 审计）：int(v) 对字符串返回 0（阈值全 0 → 里程碑风暴）、
                // 对 Array/Dict 抛运行时错误（启动即崩）；非数字元素跳过，与「损坏回退默认」宣称一致
                if (v.VariantType is Variant.Type.Int or Variant.Type.Float)
                {
                    baseArr.Add(Mathf.Max((int)v.AsInt64(), 1));
                }
            }
        }

        MilestoneBase = baseArr.Count > 0 ? baseArr : BuildMilestoneBase();
        // H03（健壮性审核）补全：milestones.cycle_mult 全局域校验——≤0 使阈值曲线平台化，
        // apply_run_save 的 while 里程碑推进永不退出（挂死）。difficulty 子表无 cycle_mult 键
        // （原 _valid_difficulty_defs 内检查恒真为死代码），此处对全局键钳制下限（同 world_scale 款）
        MilestoneCycleMult = Mathf.Max(Cfg("milestones.cycle_mult", MilestoneCycleMultValue).AsDouble(), 0.01);
        // 难度进程曲线参数：负值会使难度乘数随时间/Boss 击杀下行，钳制 ≥0 保曲线单调不减
        _progPerBossKill = Mathf.Max(Cfg("progression.per_boss_kill", 0.6).AsDouble(), 0.0);
        _progPerTenMinutes = Mathf.Max(Cfg("progression.per_ten_minutes", 1.5).AsDouble(), 0.0);
        _progTimeStepSeconds = Mathf.Max(Cfg("progression.time_step_seconds", 30.0).AsDouble(), 0.1); // H15：=0 除零挂死
        // C03 修复：难度表仅在校验 easy/medium/hard 三子键齐全后覆盖，否则回退脚本默认值
        // （缺子键时 DIFFICULTY_DEFS[difficulty]["score"] 会 KeyError，与"损坏回退默认"宣称冲突）
        var diff = Cfg("difficulty", new Godot.Collections.Dictionary());
        if (ValidDifficultyDefs(diff))
        {
            DIFFICULTY_DEFS = diff.AsGodotDictionary();
        }

        // P0-2：回血链数值一次性缓存（热路径禁 cfg 约定）
        RefreshRegenCache();
        // B 梯队：DDA 降档参数缓存（热路径禁 cfg 约定；=0 时段长无效——钳制下限）
        DDA_DURATION = Mathf.Max(Cfg("dda.duration", DDA_DURATION).AsDouble(), 0.1);
        DDA_FACTOR = Mathf.Max(Cfg("dda.factor", DDA_FACTOR).AsDouble(), 1.0);
        _maxHpBase = Mathf.Max(Cfg("player.max_health", _maxHpBase).AsDouble(), 0.1); // H15 同款：≤0 使 max_health 归零/负值，玩家秒死
        // 2026-08-03 审计：与 _max_hp_base 钳制对称——负值使 extra_life 叠层反而降血上限（生存轴收紧意图相悖）
        _maxHpBonus = Mathf.Max(Cfg("buffs.extra_life.max_hp_bonus", _maxHpBonus).AsDouble(), 0.0);
        // 2026-08-03 审计：吸血比例缓存（击杀帧免 cfg 路径解析，P0-2 同款）
        _lifestealFraction = Mathf.Max(Cfg("buffs.lifesteal.max_hp_fraction", 0.1).AsDouble(), 0.0);
        // 基地任务轮换：刷新点数经济（≤0 钳制下限，防免费无限刷新）
        REFRESH_COST = Mathf.Max((int)Cfg("base_task.refresh_cost", REFRESH_COST).AsInt64(), 1);
        GRANT_PER_VISIT = Mathf.Max((int)Cfg("base_task.grant_per_visit", GRANT_PER_VISIT).AsInt64(), 0);
    }

    /// <summary>C03/E03 修复：难度表结构校验——顶层 Dictionary、含 easy/medium/hard 三个子字典，
    /// 且每个子字典含全部数值键（缺子键时下游 8 处 DIFFICULTY_DEFS[difficulty][...] 访问 KeyError，
    /// 部分损坏 JSON 通过后敌方 0 HP 秒死/得分倍率 0，违背「损坏回退默认」宣称）。
    /// label 键已由 D04 改走 tr() 不再消费，不纳入校验。</summary>
    private static readonly string[] DifficultyDefKeys = new[]
    {
        "hp",
        "speed",
        "spawn",
        "score",
        "spread_cap",
        "milestone",
        "regen_delay",
        "regen_rate",
    };

    private static bool ValidDifficultyDefs(Variant diff)
    {
        if (diff.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        var d = diff.AsGodotDictionary();
        foreach (var key in new[] { new StringName("easy"), new StringName("medium"), new StringName("hard") })
        {
            if (!d.ContainsKey(key) || d[key].VariantType != Variant.Type.Dictionary)
            {
                return false;
            }

            var def = d[key].AsGodotDictionary();
            foreach (var k in DifficultyDefKeys)
            {
                var v = def.GetValueOrDefault(k, new Variant()); // 缺键时 get 返回 null，一并落入类型校验
                // L04（2026-08-03 审查）：bool 是 int 子类需显式排除（E21 已修 spawner 同型
                // 遗漏）——"score": false 通过校验后得分倍率恒 0，里程碑永不触发（Buff 系统软锁）
                if ((v.VariantType != Variant.Type.Int && v.VariantType != Variant.Type.Float) || v.VariantType == Variant.Type.Bool)
                {
                    return false;
                }
            }

            // H03（健壮性审核）：数值域校验——milestone ≤ 0 会破坏阈值单调性，
            // 导致 continue_run 的 while 里程碑推进永不退出（挂死）或对局内里程碑风暴。
            // 原 cycle_mult 检查为死代码：difficulty 子表无 cycle_mult 键（get 恒返回默认 1.0），
            // 全局 milestones.cycle_mult 的 >0 域校验已移至 _apply_balance
            if (def.GetValueOrDefault("milestone", 1.0).AsDouble() <= 0.0)
            {
                return false;
            }

            // 2026-08-03 审计：hp/speed/spawn/score/spread_cap 负值会使敌机 0 HP 秒死/反向移动/负得分倍率，
            // 与 milestone 同款域校验——任一负值整表回退默认（「损坏回退默认」宣称）
            foreach (var k2 in new[] { "hp", "speed", "spawn", "score", "spread_cap" })
            {
                if (def.GetValueOrDefault(k2, 0.0).AsDouble() < 0.0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ---------------- RP（征用点数）经济：对齐原作 RequisitionConstants ----------------

    private const int RpBossKillValue = 5;
    private const int RpMissionRewardValue = 3;
    private const int RpRepairCostValue = 2;
    public int RP_REPAIR_COST => RpRepairCostValue;
    private const int RpRechargeCostValue = 2;
    public int RP_RECHARGE_COST => RpRechargeCostValue;

    // 常驻基地任务（对齐原作 base_talent_console 三任务）：
    // 初始手牌 = MISSION_DEFS 三项（保持既有 id 语义）；刷新（refresh_missions）从
    // MISSION_POOL 无放回重抽 3 个槽位。kind 决定进度来源（kill=击杀数 / survive=存活
    // 秒 / boss=Boss 击杀数），goal 为各自目标——任务轮换后 id 变化，进度源按 kind 分发。
    // 显示文本全走 tr()（翻译表 MISSION_* 键），此处不保留 name/desc（2026-08-05 P4 去双源）。

    /// <summary>常驻基地任务初始手牌（对齐原作 base_talent_console 三任务）。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> MISSION_DEFS { get; } = BuildMissionDefs();

    /// <summary>基地任务池（TaskPool 数据源，任务轮换随机抽取）：9 项 = 3 类 × 3 档目标</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> MISSION_POOL { get; } = BuildMissionPool();

    /// <summary>在场任务槽位数（刷新后重抽的目标数量）。</summary>
    private const int MissionSlotsValue = 3;
    public int MISSION_SLOTS => MissionSlotsValue;

    /// <summary>刷新点数（RefreshPoints）经济：进基地每次 +GRANT_PER_VISIT，刷新任务消耗 REFRESH_COST
    /// （balance.json base_task 段覆盖；默认 1 点/次进基地、2 点/次刷新 = 攒两次基地换一次刷新）</summary>
    public int RefreshPoints { get; set; } = 0;

    /// <summary>刷新任务消耗（balance.json base_task.refresh_cost 覆盖；≥1 钳制）。</summary>
    public int REFRESH_COST { get; set; } = 2;

    /// <summary>进基地发放刷新点数（balance.json base_task.grant_per_visit 覆盖；≥0 钳制）。</summary>
    public int GRANT_PER_VISIT { get; set; } = 1;

    /// <summary>任务池实例（_init_missions 重建，保证每次对局从全新洗牌序列开始；M7 已 typed）。</summary>
    private TaskPool? _taskPool;

    /// <summary>kind -> 池内全部该类型任务 id（进度按 kind 分发，任务轮换后 id 变化仍可推进）</summary>
    private readonly Godot.Collections.Dictionary _missionsByKind = new();

    // 互斥天赋路线：line -> 两个候选 buff（对齐原作 talent_balance_manager）

    /// <summary>互斥天赋路线表：line -> 两个候选 buff。</summary>
    public Godot.Collections.Dictionary ROUTE_LINES { get; } = BuildRouteLines();

    /// <summary>音效资源（原 GDScript const preload；规则 19 禁静态持 Godot 对象——实例只读属性）。</summary>
    public AudioStream SFX_EXPLOSION { get; } = GD.Load<AudioStream>("res://assets/audio/explosion.wav");

    public AudioStream SFX_EXPLOSION_BIG { get; } = GD.Load<AudioStream>("res://assets/audio/explosion_big.wav");

    public AudioStream SFX_PLAYER_HIT { get; } = GD.Load<AudioStream>("res://assets/audio/player_hit.wav");

    public AudioStream SFX_BUFF_PICK { get; } = GD.Load<AudioStream>("res://assets/audio/buff_pick.wav");

    public AudioStream SFX_DASH { get; } = GD.Load<AudioStream>("res://assets/audio/dash.wav");

    public AudioStream SFX_RESUPPLY { get; } = GD.Load<AudioStream>("res://assets/audio/resupply.wav");

    public AudioStream SFX_HEARTBEAT { get; } = GD.Load<AudioStream>("res://assets/audio/heartbeat.wav");

    /// <summary>常驻音效播放器池大小。</summary>
    private const int SfxPoolSizeValue = 6;

    private const string SavePathValue = "user://savegame.json";
    public string SAVE_PATH => SavePathValue;
    private const string ProfilePathValue = "user://profile.json";
    public string PROFILE_PATH => ProfilePathValue;

    /// <summary>v2：3 命制 lives 字段废弃，改 100 HP 制 health（v1 存档 health 回默认满血）</summary>
    private const int PersistVersionValue = 2;

    /// <summary>2026-08-04 账户系统：当前用户会话——"" = 未登录（welcome 前/测试兼容，档案走旧 profile.json 路径）、
    /// "Guest" = 游客（设置仅内存、不存档、不写统计，B7-8）、否则为已登录用户名（档案/存档走 user_db）。</summary>
    public string CurrentUser { get; set; } = "";

    /// <summary>profile.json 退役迁移缓存：启动时存在旧 profile 且用户表为空 → 首个注册用户合并后删除（B5）</summary>
    private Godot.Collections.Dictionary _pendingLegacyProfile = new();

    public int HighScore { get; set; } = 0;

    /// <summary>P0-1：手柄默认绑定装配标志（幂等，避免重载重复追加）</summary>
    private bool _joypadBound;

    /// <summary>P0-1 手柄设置：右摇杆瞄准灵敏度 px/s（默认取 balance player.aim_assist.joy_speed）与摇杆死区。
    /// 存储承载于 snake 字段（GDScript 直读写；桥，M7 过渡，删除前）——属性转发字段。</summary>
    public double JoyAimSpeed { get; set; } = 1400.0;

    public double JoyDeadzone { get; set; } = 0.5;

    /// <summary>手柄布局（默认 Xbox/SDL 标准名；检测到 Sony 手柄切 &amp;"ps"）。</summary>
    public StringName JoyLayout { get; set; } = new StringName("xbox");

    /// <summary>Xbox/SDL 布局手柄按钮物理标签（SDL 标准位置）。</summary>
    public Godot.Collections.Dictionary XBOX_BUTTON_LABELS { get; } = new()
    {
        [0] = "A",
        [1] = "B",
        [2] = "X",
        [3] = "Y",
        [4] = "LB",
        [5] = "RB",
        [6] = "LS",
        [7] = "RS",
    };

    /// <summary>PS 布局手柄按钮物理标签。</summary>
    public Godot.Collections.Dictionary PS_BUTTON_LABELS { get; } = new()
    {
        [0] = "✕",
        [1] = "○",
        [2] = "□",
        [3] = "△",
        [4] = "L1",
        [5] = "R1",
        [6] = "L3",
        [7] = "R3",
    };

    /// <summary>手柄相关动作清单（死区应用与装配共用）</summary>
    public Godot.Collections.Array<StringName> JOYPAD_ACTIONS { get; } = new()
    {
        new StringName("move_up"),
        new StringName("move_down"),
        new StringName("move_left"),
        new StringName("move_right"),
        new StringName("aim_left"),
        new StringName("aim_right"),
        new StringName("aim_up"),
        new StringName("aim_down"),
        new StringName("dash"),
        new StringName("boost"),
        new StringName("fine_move"),
        new StringName("dock"),
        new StringName("homecoming"),
        new StringName("give_up"),
        new StringName("buff_panel"),
        new StringName("restart"),
        new StringName("parry"),
    };

    /// <summary>竞品调研 P0-3：本地高分榜（降序，上限 HIGHSCORE_LIMIT 条，profile 持久化）</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> Highscores { get; set; } = new();

    private const int HighscoreLimitValue = 10;
    public int HIGHSCORE_LIMIT => HighscoreLimitValue;

    public bool TutorialDone { get; set; } = false;

    public int Score { get; set; } = 0;

    public int Kills { get; set; } = 0;

    public int BossKills { get; set; } = 0;

    /// <summary>玩家当前 HP（100 制，对齐原作 MAX_HEALTH；上限见 max_health()）。
    /// double（GDScript float 64 位逐位等价——BaseConsole smoke flake 根因）。</summary>
    public double Health { get; set; } = 100.0;

    public double DifficultyMultiplier { get; set; } = 1.0;

    /// <summary>难度档位（profile 持久化，默认 medium）。
    /// 存储承载于 snake 字段 difficulty（GDScript 直读写；桥，M7 过渡，删除前）——属性转发字段。</summary>
    public StringName Difficulty { get; set; } = new StringName("medium");

    /// <summary>设置项：Ctrl 微调 / Shift 加速的模式（false=按住，true=切换；player.gd 侧接入由集成阶段完成）</summary>
    public bool CtrlToggleMode { get; set; } = false;

    public bool ShiftToggleMode { get; set; } = false;

    /// <summary>触屏虚拟控件开关（profile 持久化，默认关；Main 挂载 VirtualControls 联动）</summary>
    public bool TouchControls { get; set; } = false;

    /// <summary>视角档位（profile 持久化，默认 small=原始视角；相机 zoom = VIEW_ZOOM_LEVELS[view_zoom]）</summary>
    public StringName ViewZoom { get; set; } = new StringName("small");

    /// <summary>窗口尺寸档位（profile 持久化，默认 large=1920×1080；尺寸表见 WINDOW_SIZE_LEVELS）</summary>
    public StringName WindowSize { get; set; } = new StringName("large");

    /// <summary>瞄准辅助强度档位（profile 持久化，默认 medium；常驻不可关，无 off 档；数值见 AIM_ASSIST_ORDER 注释）</summary>
    public StringName AimAssistLevel { get; set; } = new StringName("medium");

    /// <summary>Meta HUD 当前 LOD（由 MetaHealthFX._ready 从 effects.meta_health.lod 写入；0=MetaFX 接管
    /// 低血晕影，hud 旧晕影恒 0；非 0=回退路径，hud 保留低血脉动。MetaFX 离场时置 1）</summary>
    public int MetaFxLod { get; set; } = 1;

    /// <summary>无障碍：减少闪光（profile 持久化；开启后色差 ×0.4、禁呼吸/抖动/心跳视觉脉冲，音效保留）</summary>
    public bool ReduceFlash { get; set; } = false;

    /// <summary>鼠标锁定窗口内（profile 持久化，默认开启；开启后窗口聚焦期间鼠标移出内容区即被拉回，
    /// 防止准星跟随鼠标出框后位置冻结/跳变；窗口失焦自动放行，不阻碍切换应用）</summary>
    public bool MouseLock { get; set; } = true;

    /// <summary>buff id -> 已选层数</summary>
    public Godot.Collections.Dictionary Buffs { get; set; } = new();

    /// <summary>征用点数（基地经济）</summary>
    public int Rp { get; set; } = 0;

    /// <summary>对局存活秒数（survive_180 任务进度来源）</summary>
    public double RunTime { get; set; } = 0.0;

    /// <summary>任务 id -> {"progress": int, "claimed": bool}</summary>
    public Godot.Collections.Dictionary Missions { get; set; } = new();

    /// <summary>天赋路线 line -> 所选 buff id</summary>
    public Godot.Collections.Dictionary ChosenRoutes { get; set; } = new();

    /// <summary>天赋路线 line -> 被锁定的未选 buff id（不进奖励池）</summary>
    public Godot.Collections.Dictionary LockedRoutes { get; set; } = new();

    private int _nextMilestone = 3000; // = MILESTONE_BASE[0]

    private int _milestoneCount;

    /// <summary>难度进程曲线参数（_apply_balance 从 balance.json progression 段读取缓存，热路径免查 JSON）</summary>
    private double _progPerBossKill = 0.6;

    private double _progPerTenMinutes = 1.5;

    private double _progTimeStepSeconds = 30.0;

    /// <summary>任务进度整秒缓存（_process 热路径免每帧字典访问）</summary>
    private int _surviveSecCached = -1;

    /// <summary>B 梯队（fair plan §8）：DDA 弹幕密度降档——玩家受击后短暂拉长敌弹/波次间隔
    /// （只拉间隔不降收益，分数公平）；_apply_balance 从 balance.json dda 段缓存</summary>
    public double DDA_DURATION { get; set; } = 5.0;

    public double DDA_FACTOR { get; set; } = 1.3;

    private double _ddaTimer;

    /// <summary>回血链热路径缓存（P0-2）：max_health 基础值 _apply_balance 缓存；regen 档位难度变更时刷新。
    /// 默认值须与脚本默认 difficulty=medium 档一致（medium: regen_delay=4.0, regen_rate=2.0）。</summary>
    private double _maxHpBase = 100.0;

    private double _maxHpBonus = 50.0;

    private double _regenDelay = 4.0;

    private double _regenRate = 2.0;

    /// <summary>已计入难度乘数的时间档位（按 time_step_seconds 量化步进，避免连续漂移）</summary>
    private int _difficultyTimeStep;

    /// <summary>启动计时基准（autoload 最早生命周期点；--startup-time 时由 main 打印分段耗时）</summary>
    public int BootTicksMsec { get; set; } = 0;

    /// <summary>启动/读档时检测到损坏并已隔离备份（开始面板据此提示；读取正常后置回 false）</summary>
    public bool SaveCorrupt { get; set; } = false;

    public bool ProfileCorrupt { get; set; } = false;

    /// 静态缓存——autoload 在 root 下恒存在，测试进程同样适用）</summary>
    private static GameState? _instance;

    public static GameState Instance
    {
        get
        {
            if (_instance != null && GodotObject.IsInstanceValid(_instance))
            {
                return _instance;
            }

            var tree = (SceneTree?)Engine.GetMainLoop();
            _instance = tree?.Root?.GetNodeOrNull<GameState>("GameState");
            if (_instance == null)
            {
                throw new InvalidOperationException("GameState autoload 不可用");
            }

            return _instance;
        }
    }

    public override void _EnterTree()
    {
        BootTicksMsec = (int)Time.GetTicksMsec();
    }

    /// <summary>实体管理器（A2 阶段 4 起数据归 EntityManager，2026-08-05 演进：绑定样板/生命周期信号/
    /// 批量操作 API；docs/ENTITY_MANAGER.md。属性转发保持外部语法不变；M2 起内部改 C# PascalCase）。
    /// 热路径缓存，避免每帧 get_nodes_in_group 分配。
    /// enemy/boss 在 _ready/_exit_tree 时注册/注销，player 单独缓存引用。</summary>
    public Godot.Collections.Array<Node> Enemies => _registry.Enemies;

    /// <summary>P0-1（2026-08-05 审计）：敌弹注册表转发（death_replay 录制数据源，替代 get_children 遍历）</summary>
    public Godot.Collections.Array<GodotObject> EnemyBullets => _registry.EnemyBullets;

    public Node2D? PlayerRef
    {
        get => _registry.PlayerRef;
        set => _registry.PlayerRef = value;
    }

    /// <summary>玩家受击 Hitbox（player._ready/_exit_tree 维护；敌机/Boss 撞击逐帧轮询用）</summary>
    public Area2D? PlayerHitbox
    {
        get => _registry.PlayerHitbox;
        set => _registry.PlayerHitbox = value;
    }

    /// <summary>子弹对象池实例（由 bullet_pool.gd 在 _ready 时登记）</summary>
    public BulletPool? BulletPool
    {
        get => _registry.BulletPool;
        set => _registry.BulletPool = value;
    }

    /// <summary>敌机对象池实例（由 enemy_pool.gd 在 _ready 时登记）</summary>
    public GodotObject? EnemyPool
    {
        get => _registry.EnemyPool;
        set => _registry.EnemyPool = value;
    }

    /// <summary>辅助瞄准框覆盖层实例（由 aim_frame_layer.gd 在 _ready 时登记；player._fire 查询框内标记敌）</summary>
    public GodotObject? AimFrameLayer
    {
        get => _registry.AimFrameLayer;
        set => _registry.AimFrameLayer = value;
    }

    /// <summary>触屏虚拟输入层实例（mobile touch，由 main.gd 在 _ready 时创建并登记；
    /// player.aim_point 查询触屏瞄准基准）</summary>
    public GodotObject? VirtualControls
    {
        get => _registry.VirtualControls;
        set => _registry.VirtualControls = value;
    }

    /// <summary>迷雾事件管理器转发（全局单例访问口；挂本节点下，_ready 时 add_child）</summary>
    public FogEventManager FogEvents => _fogEvents;

    /// <summary>统一事件管理器转发（全局单例访问口；挂本节点下，_ready 时 add_child）</summary>
    public GameEventManager Events => _events;

    public void RegisterEnemy(Node node) => _registry.RegisterEnemy(node);

    /// <summary>统一单位绑定样板（docs/ENTITY_MANAGER.md）：add_to_group("enemy") + 注册 + entity_registered</summary>
    public void BindEnemy(Node node) => _registry.BindEnemy(node);

    /// <summary>统一单位解绑（_exit_tree 调用；注销 + entity_unregistered）</summary>
    public void UnbindEnemy(Node node) => _registry.UnbindEnemy(node);

    /// <summary>批量遍历注册表（失效实例跳过；谓词可选过滤）。清场/索敌/冻结等非热路径统一入口
    /// M2 过渡：Callable 参数化迭代留在 GDScript facade 层（GDScript lambda 跨语言传参不可靠），
    /// 直接迭代 C# 侧注册表集合（_registry.Enemies）；随调用方迁移（M3-M6）后由 C# 实现</summary>
    /// <summary>Callable 空判定（Godot C# Callable 无 IsValid 属性——空 callable 的 Method 为空 StringName，
    /// 替代 GDScript predicate.is_valid()）。</summary>
    private static bool IsEmptyCallable(Variant v) => v.VariantType != Variant.Type.Callable || v.AsCallable().Method == new StringName();

    public void ForEachEnemy(Variant action, Variant predicate = default)
    {
        foreach (var node in _registry.Enemies)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            if (!IsEmptyCallable(predicate) && !predicate.AsCallable().Call(node).AsBool())
            {
                continue;
            }

            action.AsCallable().Call(node);
        }
    }

    /// <summary>批量清除注册表实体（predicate 为保留项过滤，如 Boss）；返回清除数</summary>
    public int ClearEnemies(Variant predicate = default)
    {
        var cleared = 0;
        // 泛型 Duplicate() 返回 Array&lt;Node&gt;（浅拷贝）
        foreach (var node in _registry.Enemies.Duplicate())
        {
            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            if (!IsEmptyCallable(predicate) && predicate.AsCallable().Call(node).AsBool())
            {
                continue;
            }

            node.QueueFree();
            cleared += 1;
        }

        return cleared;
    }

    /// <summary>计数（谓词可选过滤）。spread 上限/统计用</summary>
    public int CountEnemies(Variant predicate = default)
    {
        var count = 0;
        foreach (var node in _registry.Enemies)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            if (!IsEmptyCallable(predicate) && !predicate.AsCallable().Call(node).AsBool())
            {
                continue;
            }

            count += 1;
        }

        return count;
    }

    /// <summary>P0-1：敌弹注册/注销转发（bullet.gd 维护；M3a 起 Bullet 为 C# 类）</summary>
    public void RegisterEnemyBullet(GodotObject b) => _registry.RegisterEnemyBullet(b);

    public void UnregisterEnemyBullet(GodotObject b) => _registry.UnregisterEnemyBullet(b);

    /// <summary>G010：注册表存在性判定 O(1)（追踪弹热路径，替代 enemies.has() 线性扫描）</summary>
    public bool EnemiesHas(Node node) => _registry.HasEnemy(node);

    public void UnregisterEnemy(Node node) => _registry.UnregisterEnemy(node);

    private void OnRegistryEntityRegistered(Node node) => EmitSignal(SignalName.EntityRegistered, node);

    private void OnRegistryEntityUnregistered(Node node) => EmitSignal(SignalName.EntityUnregistered, node);

    public override void _Ready()
    {
        LoadBalance();
        ApplyBalance();
        // 实体生命周期信号转发（EntityManager 非 Node，无树内信号；GameState 收口转发）
        // M2：C# [Signal] 以 PascalCase 注册，GDScript 侧同名连接
        _registry.EntityRegistered += OnRegistryEntityRegistered;
        _registry.EntityUnregistered += OnRegistryEntityUnregistered;
        // 常驻音效播放器池：播放节点被 queue_free 时音效也不会中断（SfxPlayer 子节点挂本节点）
        AddChild(_sfxPlayer);
        _sfxPlayer.BuildPool(SfxPoolSizeValue);
        // 迷雾事件管理器挂载（balance 已在 _apply_balance 就绪；管理器 _ready 读 cfg）
        AddChild(_fogEvents);
        // 统一事件管理器挂载（fog 组经迷雾门面 wire() 接线；encounter 组由 main._ready 注册）
        AddChild(_events);
        _fogEvents.Wire(_events);
        CaptureDefaultBindings();
        InitMissions();
        LoadProfile();
        MaybeMigrateLegacyProfile(); // 账户系统：旧 profile.json 迁移缓存（首个注册用户合并）
        ApplyWindowSize(); // 无 profile 时 load_profile 不会应用窗口尺寸，这里补一次默认档位
        var trZh = GD.Load<Translation>("res://data/translations.zh.translation");
        var trEn = GD.Load<Translation>("res://data/translations.en.translation");
        if (trZh != null)
        {
            TranslationServer.AddTranslation(trZh);
        }

        if (trEn != null)
        {
            TranslationServer.AddTranslation(trEn);
        }

        TranslationServer.SetLocale(Locale);
        ApplyKeyBindings();
        BindJoypadDefaults();
        // PS 布局检测：监听手柄插拔并刷新布局（标签显示用）
        Input.JoyConnectionChanged += OnJoyConnectionChanged;
        DetectJoyLayout();
        _nextMilestone = MilestoneThreshold(0);
        // B 梯队：受击触发 DDA 降档（player_damaged 为减免后信号，Meta HUD 受击层同源）
        PlayerDamaged += OnPlayerDamagedDda;
    }

    // 暂停（Buff/结算 UI）时不计存活时间
    public override void _Process(double delta)
    {
        RunTime += delta;
        // 整秒边界才推进任务（缓存秒值：避免每帧 int(run_time) + missions 字典访问——热路径禁字典约定）
        var surviveSec = (int)RunTime;
        if (surviveSec != _surviveSecCached)
        {
            _surviveSecCached = surviveSec;
            SetKindProgress("survive", surviveSec);
        }

        // 时间轴难度档：跨过量化步进边界时重算难度乘数（去硬顶曲线的时间分量）
        if ((int)Mathf.Floor(RunTime / _progTimeStepSeconds) != _difficultyTimeStep)
        {
            if (RecomputeDifficultyInternal())
            {
                EmitSignal(SignalName.DifficultyChanged, DifficultyMultiplier);
            }
        }

        // DDA 降档计时（受击触发；暂停时 process 冻结，与对局节奏一致）
        if (_ddaTimer > 0.0)
        {
            _ddaTimer -= delta;
        }
    }

    public void PlaySfx(AudioStream stream, double volumeDb = 0.0, double pitchScale = 1.0)
    {
        // headless 短路与池化复用逻辑在 SfxPlayer（A2 阶段 3）
        _sfxPlayer.Play(stream, (float)volumeDb, (float)pitchScale);
    }

    /// <summary>退出前停止所有仍在播放的音效：带播未停时 AudioStreamPlayback 会在退出时泄漏</summary>
    public void StopAllSfx()
    {
        _sfxPlayer.StopAll();
        if (PlayerRef != null && GodotObject.IsInstanceValid(PlayerRef))
        {
            var audio = PlayerRef.GetNodeOrNull<AudioStreamPlayer2D>("AudioStreamPlayer2D");
            if (audio != null)
            {
                audio.Stop();
            }
        }
    }

    public void Shake(double strength) => EmitSignal(SignalName.ScreenShake, strength);

    public void ResetRun()
    {
        Score = 0;
        Kills = 0;
        BossKills = 0;
        Buffs.Clear();
        Health = MaxHealth();
        DifficultyMultiplier = 1.0;
        _difficultyTimeStep = 0;
        Rp = 0;
        RunTime = 0.0;
        InitMissions();
        RefreshPoints = 0;
        EmitSignal(SignalName.RefreshPointsChanged, RefreshPoints);
        ChosenRoutes.Clear();
        LockedRoutes.Clear();
        _milestoneCount = 0;
        _nextMilestone = MilestoneThreshold(0);
        _ddaTimer = 0.0; // A 审计：DDA 计时跨对局残留——旧局受击降档渗透新局
        EmitSignal(SignalName.BuffsChanged);
    }

    public void AddScore(int points)
    {
        // 难度分数倍率统一在此乘算（easy ×1 / medium ×2 / hard ×3，配置表里的分值不变）
        // P4（2026-08-05）：得分总量钳制——手改配置 score 倍率极大时 int64 溢出（1e308 级）
        Score = Mathf.Min(Score + points * ScoreMultiplier(), ScoreCapValue);
        EmitSignal(SignalName.ScoreChanged, Score);
        // 2026-08-06 审计：里程碑推进改 while——与 apply_run_save 的全补口径一致（原单次 +1
        // 在单次加分跨多档时漏档：如 hard 倍率下高分击杀/Boss 奖励一次跨两档阈值），
        // 两路径行为统一（milestone_reached 按触发的档位逐档发，消费方按里程碑数计档）。
        // 2026-08-07：阈值求值迁移 C#（milestone_threshold 转发）；此处保持基于
        // _next_milestone 的 while——set_milestone_override 测试钩子允许阈值脱离曲线，
        // 批量推进（CountThresholdsUpTo）仅用于 apply_run_save 的存档恢复路径（低频、
        // 病态档数场景，批量收益大）；加分逐档仅 1-2 档，单值调用开销可忽略。
        while (Score >= _nextMilestone)
        {
            _milestoneCount += 1;
            _nextMilestone = MilestoneThreshold(_milestoneCount);
            EmitSignal(SignalName.MilestoneReached, Score);
        }
    }

    // ---------------- 难度档位 ----------------

    /// <summary>切换难度档位（非法档位忽略），持久化到 profile 并广播</summary>
    public void SetDifficulty(StringName pDifficulty)
    {
        if (!DIFFICULTY_DEFS.ContainsKey(pDifficulty) || pDifficulty == Difficulty)
        {
            return;
        }

        Difficulty = pDifficulty;
        RefreshRegenCache();
        EmitSignal(SignalName.DifficultySelected, Difficulty);
        SaveProfile();
    }

    public string DifficultyLabel() => (string)Tr("DIFF_" + Difficulty.ToString().ToUpperInvariant());

    /// <summary>B 梯队：受击触发 DDA 降档（重入安全——幂等置位，重复受击刷新计时）</summary>
    private void OnPlayerDamagedDda(float amount, Vector2 fromPos)
    {
        _ddaTimer = DDA_DURATION;
    }

    public int ScoreMultiplier()
    {
        // 2026-08-03 审计回退：曾尝试缓存 _score_multiplier_cache，但 difficulty 是公开字段，
        // 测试/调用方直写不触发 _refresh_regen_cache（白盒契约），缓存会返回旧值；与同族
        // enemy_hp_multiplier/enemy_speed_multiplier/spawn_interval_multiplier 一致保持直接查表
        return (int)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["score"].AsInt64();
    }

    /// <summary>B 梯队（fair plan §8）：DDA 降档中（玩家受击后 DDA_DURATION 内）——消费方
    /// （enemy 开火计时 / spawner 波次间隔 / boss 攻击间隔）乘 dda_factor() 拉长间隔</summary>
    public bool DdaActive() => _ddaTimer > 0.0;

    /// <summary>DDA 降档乘区：active 时返回配置因子（>1 拉长间隔），否则 1.0（热路径零分支常态）</summary>
    public double DdaFactor() => _ddaTimer > 0.0 ? DDA_FACTOR : 1.0;

    /// <summary>测试/诊断：立即结束降档（对齐「测试经公开接口」白盒契约）</summary>
    public void ResetDda() => _ddaTimer = 0.0;

    public double EnemyHpMultiplier() => (double)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["hp"].AsDouble();

    public double EnemySpeedMultiplier() => (double)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["speed"].AsDouble();

    /// <summary>敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))，随 Boss 击杀线性成长。
    /// 纯查询委托 BalanceService（难度乘数作参数）。</summary>
    public float EnemyHpRamp() => (float)_balanceService.EnemyHpRamp(DifficultyMultiplier);

    /// <summary>敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))，
    /// 统一作用于全部敌方伤害源（敌弹/Boss 弹/撞体/编队炸弹；2026-07-29 无限段修订）。
    /// 纯查询委托 BalanceService（难度乘数作参数）。</summary>
    public float EnemyDamageRamp() => (float)_balanceService.EnemyDamageRamp(DifficultyMultiplier);

    public double SpawnIntervalMultiplier() => (double)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["spawn"].AsDouble();

    /// <summary>spread 弹种敌机同屏上限（easy 1 / medium 2 / hard 3）</summary>
    public int SpreadEnemyCap() => (int)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["spread_cap"].AsInt64();

    /// <summary>被动回血：距上次受伤 regen_delay 秒起每秒回 regen_rate HP（对齐原作 HEALTH_REGEN）
    /// P0-2：档位值在难度变更/重新加载时缓存，热路径免双层字典查找</summary>
    public double PassiveRegenDelay() => _regenDelay;

    public double PassiveRegenRate() => _regenRate;

    private void RefreshRegenCache()
    {
        var def = DIFFICULTY_DEFS.GetValueOrDefault(Difficulty, new Variant());
        if (def.VariantType == Variant.Type.Dictionary)
        {
            _regenDelay = (double)def.AsGodotDictionary().GetValueOrDefault("regen_delay", _regenDelay).AsDouble();
            _regenRate = (double)def.AsGodotDictionary().GetValueOrDefault("regen_rate", _regenRate).AsDouble();
        }
    }

    // ---------------- 里程碑阈值曲线 ----------------

    /// <summary>第 index 次（0 起）里程碑的分数阈值：8 档基础阈值循环，档差按 ×1.35^cycle 增长，
    /// 再乘难度阈值倍率（easy ×1 / medium ×1 / hard ×1.5）。
    /// 2026-08-07：算法核心迁移 InfiAir.Core.Progression.MilestoneCurve（C# 纯函数，xUnit 直测；
    /// 逐位等价：pow 钳制、roundf half-away-from-zero、累加顺序一致）。</summary>
    public int MilestoneThreshold(int index) => (int)_progression.MilestoneThreshold(
        index, Variant.From(MilestoneBase).AsGodotArray(), MilestoneCycleMult, MilestoneMult());

    /// <summary>难度档阈值倍率（DIFFICULTY_DEFS 经 _valid_difficulty_defs 校验，milestone 恒为正数）</summary>
    private double MilestoneMult() => (double)DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["milestone"].AsDouble();

    /// <summary>测试钩子（A7 遗留清理，公开化）：直接设定下一个里程碑阈值（不动曲线计数，保证测试确定性）</summary>
    public void SetMilestoneOverride(int threshold) => _nextMilestone = threshold;

    /// <summary>A7：测试/诊断白盒断言经公开接口
    /// 当前已触发的里程碑数（2026-08-04 母舰升级档位等消费点）</summary>
    public int MilestoneCount() => _milestoneCount;

    /// <summary>A7：测试/诊断白盒 setter（2026-08-06 审计：mothership_upgrade_test 曾直写
    /// _milestone_count ×5，补语义化公开接口；负值钳 0）</summary>
    public void SetMilestoneCount(int count) => _milestoneCount = Mathf.Max(count, 0);

    public int NextMilestone() => _nextMilestone;

    public void RecomputeDifficulty() => RecomputeDifficultyInternal();

    // ---------------- 设置项（Ctrl/Shift 模式） ----------------

    /// <summary>Ctrl 微调模式：false=按住生效，true=按一下切换；持久化到 profile</summary>
    public void SetCtrlToggleMode(bool enabled)
    {
        CtrlToggleMode = enabled;
        SaveProfile();
    }

    /// <summary>Shift 加速模式：false=按住生效，true=按一下切换；持久化到 profile</summary>
    public void SetShiftToggleMode(bool enabled)
    {
        ShiftToggleMode = enabled;
        SaveProfile();
    }

    /// <summary>触屏虚拟控件开关（mobile touch）：持久化 + 广播（Main 联动 VirtualControls.set_enabled）</summary>
    public void SetTouchControls(bool enabled)
    {
        TouchControls = enabled;
        SaveProfile();
        EmitSignal(SignalName.TouchControlsChanged, enabled);
    }

    // ---------------- 视角缩放 ----------------

    /// <summary>视角档位表（设置页三选，profile 持久化；值为相机 zoom 倍率）。
    /// zoom&gt;1 时可见世界区域 = 视口 ÷ zoom（以相机位置为中心收窄），
    /// 所有"屏幕边缘/出屏"逻辑统一走 view_world_rect() 适配。</summary>
    public Godot.Collections.Dictionary VIEW_ZOOM_LEVELS { get; } = new()
    {
        [new StringName("small")] = 1.0,
        [new StringName("medium")] = 1.35,
        [new StringName("large")] = 1.7,
    };

    public Godot.Collections.Array<StringName> VIEW_ZOOM_ORDER { get; } = new()
    {
        new StringName("small"),
        new StringName("medium"),
        new StringName("large"),
    };

    /// <summary>main 场景相机注册表（main.gd 在 _ready/_exit_tree 维护），供可见区域计算</summary>
    public Camera2D? CameraRef
    {
        get => _registry.CameraRef;
        set
        {
            _registry.CameraRef = value;
            InvalidateViewRectCache();
        }
    }

    /// <summary>生效 zoom 倍率缓存（set_view_zoom/load_profile 同步；热路径免查表，须与 small 档一致）</summary>
    private double _viewZoomFactor = 1.0;

    /// <summary>view_world_rect 物理帧缓存：同帧多次调用免重复视口查询（子弹/敌机/玩家每帧共用一帧结果）。
    /// zoom 因子或相机注册变更时置 -1 强制重算；相机位置固定 (960,540)，帧内语义不变。</summary>
    private long _viewRectFrame = -1;

    private Rect2 _viewRectCached = new();

    /// <summary>切换视角档位（非法/同档忽略），持久化到 profile 并广播</summary>
    public void SetViewZoom(StringName level)
    {
        if (!VIEW_ZOOM_LEVELS.ContainsKey(level) || level == ViewZoom)
        {
            return;
        }

        ViewZoom = level;
        _viewZoomFactor = (double)VIEW_ZOOM_LEVELS[level].AsDouble();
        InvalidateViewRectCache();
        SaveProfile();
        EmitSignal(SignalName.ViewZoomChanged, _viewZoomFactor);
    }

    public double ViewZoomFactor() => _viewZoomFactor;

    public void SetViewZoomFactor(double factor)
    {
        _viewZoomFactor = factor;
        InvalidateViewRectCache();
    }

    // ---------------- 窗口大小 ----------------

    /// <summary>窗口尺寸档位表（设置页三选，profile 持久化；stretch 等比缩放，仅改窗口物理尺寸）。
    /// 非 const：Vector2i 构造为非常量表达式（同 spawner.ENEMY_TYPES 先例）。</summary>
    public Godot.Collections.Dictionary WINDOW_SIZE_LEVELS { get; set; } = new()
    {
        [new StringName("small")] = new Vector2I(1280, 720),
        [new StringName("medium")] = new Vector2I(1600, 900),
        [new StringName("large")] = new Vector2I(1920, 1080),
    };

    public Godot.Collections.Array<StringName> WINDOW_SIZE_ORDER { get; } = new()
    {
        new StringName("small"),
        new StringName("medium"),
        new StringName("large"),
    };

    /// <summary>切换窗口尺寸档位（非法/同档忽略）：立即应用窗口，持久化到 profile 并广播</summary>
    public void SetWindowSize(StringName level)
    {
        if (!WINDOW_SIZE_LEVELS.ContainsKey(level) || level == WindowSize)
        {
            return;
        }

        WindowSize = level;
        ApplyWindowSize();
        SaveProfile();
        EmitSignal(SignalName.WindowSizeChanged, WindowSize);
    }

    /// <summary>应用当前档位到窗口：仅窗口模式生效；headless 为 dummy 渲染直接跳过。
    /// 档位尺寸按逻辑点定义：高分屏（Retina 等 content scale&gt;1）乘屏幕缩放换算物理像素，
    /// 否则 1920×1080 档位在 2x 屏上只显示为 960×540 点的小窗；超出当前屏可用区域时等比收缩并居中。</summary>
    private void ApplyWindowSize()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        var win = GetWindow();
        if (win == null || win.Mode != Window.ModeEnum.Windowed)
        {
            return;
        }

        var screen = win.CurrentScreen;
        var scale = DisplayServer.ScreenGetScale(screen);
        var phys = (Vector2I)((Vector2)WINDOW_SIZE_LEVELS[WindowSize].AsVector2I() * scale);
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        if (phys.X > usable.Size.X || phys.Y > usable.Size.Y)
        {
            var fit = Mathf.Min((float)usable.Size.X / phys.X, (float)usable.Size.Y / phys.Y);
            phys = (Vector2I)((Vector2)phys * fit);
        }

        win.Size = phys;
        win.Position = usable.Position + (usable.Size - phys) / 2;
    }

    // ---------------- 瞄准辅助强度 ----------------

    /// <summary>强度档位表（设置页三选，profile 持久化；辅助瞄准常驻、刻意不提供关闭档）。
    /// 各档数值（辅助框内边距 frame_pad/追踪转向速率 homing_turn_rate）在 balance.json player.aim_assist.levels。</summary>
    public Godot.Collections.Array<StringName> AIM_ASSIST_ORDER { get; } = new()
    {
        new StringName("low"),
        new StringName("medium"),
        new StringName("high"),
    };

    /// <summary>切换瞄准辅助强度档位（非法/同档忽略），持久化到 profile 并广播</summary>
    public void SetAimAssistLevel(StringName level)
    {
        if (!AIM_ASSIST_ORDER.Contains(level) || level == AimAssistLevel)
        {
            return;
        }

        AimAssistLevel = level;
        SaveProfile();
        EmitSignal(SignalName.AimAssistChanged, level);
    }

    /// <summary>无障碍·减少闪光：开关持久化到 profile 并广播（Meta HUD 据此折算色差/禁脉冲）</summary>
    public void SetReduceFlash(bool enabled)
    {
        if (enabled == ReduceFlash)
        {
            return;
        }

        ReduceFlash = enabled;
        SaveProfile();
        EmitSignal(SignalName.ReduceFlashChanged, enabled);
    }

    /// <summary>鼠标锁定窗口内：开关持久化到 profile 并广播（MouseTrap 据此决定是否拉回出框鼠标）</summary>
    public void SetMouseLock(bool enabled)
    {
        if (enabled == MouseLock)
        {
            return;
        }

        MouseLock = enabled;
        SaveProfile();
        EmitSignal(SignalName.MouseLockChanged, enabled);
    }

    /// <summary>P0-1 手柄设置 setter：右摇杆瞄准灵敏度（200..4000 px/s）。
    /// K06：只更新内存 + 广播（灵敏度不影响 InputMap 死区）；持久化由设置页 drag_ended 统一
    /// 提交——原实现每步全量原子写盘，滑杆拖动（数十次 value_changed）放大为磁盘写风暴</summary>
    public void SetJoyAimSpeed(double value)
    {
        JoyAimSpeed = Mathf.Clamp(value, 200.0, 4000.0);
        EmitSignal(SignalName.JoySettingsChanged, JoyAimSpeed, JoyDeadzone);
    }

    /// <summary>P0-1 手柄设置 setter：摇杆死区（0.05..0.90，应用至全部手柄动作的 InputMap deadzone）。
    /// K06：立即应用死区（InputMap 全局生效，base_system_test 契约）+ 广播；不自动写盘</summary>
    public void SetJoyDeadzone(double value)
    {
        JoyDeadzone = Mathf.Clamp(value, 0.05, 0.9);
        foreach (var a in JOYPAD_ACTIONS)
        {
            if (InputMap.HasAction(a))
            {
                InputMap.ActionSetDeadzone(a, (float)JoyDeadzone);
            }
        }

        EmitSignal(SignalName.JoySettingsChanged, JoyAimSpeed, JoyDeadzone);
    }

    /// <summary>手柄设置持久化：设置页滑杆 drag_ended 调用一次（setter 不再自动写盘，防拖动写风暴）</summary>
    public void PersistJoySettings() => SaveProfile();

    /// <summary>当前可见世界区域（相机未注册时以 (960,540) 为心），margin 向外扩张。
    /// 屏幕边缘钳制 / 出屏销毁 / 刷怪位置统一以此为准；zoom=1 时即全屏 1920×1080。
    /// 物理帧内缓存（P0-1）：同一物理帧内多次调用（每弹/每敌/玩家/Boss）共享一次视口查询。</summary>
    public Rect2 ViewWorldRect(double margin = 0.0)
    {
        if (margin == 0.0)
        {
            return CachedViewRect();
        }

        return CachedViewRect().Grow((float)margin);
    }

    private void InvalidateViewRectCache() => _viewRectFrame = -1;

    private Rect2 CachedViewRect()
    {
        var frame = (long)Engine.GetPhysicsFrames();
        if (frame != _viewRectFrame)
        {
            _viewRectFrame = frame;
            var center = new Vector2(960.0f, 540.0f);
            if (CameraRef != null && GodotObject.IsInstanceValid(CameraRef))
            {
                center = CameraRef.GlobalPosition;
            }

            var size = new Vector2(1920.0f, 1080.0f);
            var viewport = GetViewport();
            if (viewport != null)
            {
                size = viewport.GetVisibleRect().Size;
            }

            size /= (float)_viewZoomFactor;
            _viewRectCached = new Rect2(center - size * 0.5f, size);
        }

        return _viewRectCached;
    }

    public void AddKill()
    {
        Kills += 1;
        SetKindProgress("kill", Kills);
    }

    public void AddBossKill(double scoreScale = 1.0)
    {
        BossKills += 1;
        // G012：加分基准入 balance.json（milestones.boss_kill_base；击杀低频，非热路径可直查）
        AddScore((int)(Cfg("milestones.boss_kill_base", 500.0).AsDouble() * scoreScale));
        AddRp(RpBossKillValue);
        SetKindProgress("boss", BossKills);
        if (RecomputeDifficultyInternal())
        {
            EmitSignal(SignalName.DifficultyChanged, DifficultyMultiplier);
        }
    }

    /// <summary>难度乘数对局进程曲线（2026-07-29 无限段修订，D1=必死曲线，docs/ENDLESS_BALANCE_PLAN.md）：
    /// 1 + per_boss_kill×Boss击杀 + 时间轴累进（每 time_step_seconds 量化一档，每 10 分钟 +per_ten_minutes）。
    /// 线性无封顶：敌方 HP/伤害 ramp 随之无限增长，最终超过玩家固定成长上限。
    /// 返回乘数是否变化；变化时由调用方广播 difficulty_changed（apply_run_save 统一在末尾广播）。</summary>
    private bool RecomputeDifficultyInternal()
    {
        var step = (int)Mathf.Floor(RunTime / _progTimeStepSeconds);
        // 2026-08-07：曲线公式迁移 InfiAir.Core.Progression.DifficultyCurve（C#，运算顺序逐位等价）
        var newMult = _progression.DifficultyMultiplier(RunTime, _progTimeStepSeconds, _progPerTenMinutes, _progPerBossKill, BossKills);
        _difficultyTimeStep = step;
        if (Mathf.IsEqualApprox(newMult, DifficultyMultiplier))
        {
            return false;
        }

        DifficultyMultiplier = newMult;
        return true;
    }

    /// <summary>生命上限：基础 100 + extra_life 每层 +50（对齐原作 EXTRA_LIFE_BONUS_HP）
    /// P0-2：基础值 _apply_balance 缓存，热路径免 cfg 路径解析（extra_life 层数查询 O(1)）</summary>
    public double MaxHealth() => _maxHpBase + _maxHpBonus * BuffCount("extra_life");

    public void LoseHealth(double amount = 1.0)
    {
        Health = Mathf.Max(Health - amount, 0.0);
        EmitSignal(SignalName.HealthChanged, Health);
        if (Health <= 0.0)
        {
            EmitSignal(SignalName.PlayerDied);
        }
    }

    /// <summary>治疗（单点封顶 max_health，调用侧不再各自判断）</summary>
    public void Heal(double amount)
    {
        Health = Mathf.Min(Health + amount, MaxHealth());
        EmitSignal(SignalName.HealthChanged, Health);
    }

    /// <summary>吸血 buff：击杀回复 int(上限 × 10%)（对齐原作 LIFESTEAL_FRACTION），每帧至多结算一次</summary>
    private long _lifestealFrame = -1;

    /// <summary>吸血比例缓存（P0-2 同款：_apply_balance 刷新，击杀帧免 cfg 路径解析）</summary>
    private double _lifestealFraction = 0.1;

    public void TryLifesteal()
    {
        if (BuffCount("lifesteal") <= 0)
        {
            return;
        }

        var frame = (long)Engine.GetPhysicsFrames();
        if (frame == _lifestealFrame)
        {
            return;
        }

        _lifestealFrame = frame;
        Heal(Mathf.Max(1, (int)(MaxHealth() * _lifestealFraction)));
    }

    public int BuffCount(StringName id) => (int)Buffs.GetValueOrDefault(id, 0).AsInt64();

    public void AddBuff(StringName id)
    {
        Buffs[id] = BuffCount(id) + 1;
        EmitSignal(SignalName.BuffsChanged);
    }

    /// <summary>消耗一层 buff（护盾等一次性层；无剩余层返回 false；层数变动广播 buffs_changed）</summary>
    public bool ConsumeBuff(StringName id)
    {
        if (BuffCount(id) <= 0)
        {
            return false;
        }

        Buffs[id] = BuffCount(id) - 1;
        EmitSignal(SignalName.BuffsChanged);
        return true;
    }

    // ---------------- 可改键系统 ----------------

    /// <summary>可改键动作清单（restart/pause 固定不可改）。</summary>
    public Godot.Collections.Array<StringName> REBINDABLE_ACTIONS { get; } = new()
    {
        new StringName("move_up"),
        new StringName("move_down"),
        new StringName("move_left"),
        new StringName("move_right"),
        new StringName("boost"),
        new StringName("fine_move"),
        new StringName("dash"),
        new StringName("dock"),
        new StringName("homecoming"),
        new StringName("give_up"),
        new StringName("buff_panel"),
        new StringName("parry"),
    };

    /// <summary>action -> Array[int]（keycode，最多 2 个）；restart/pause 固定不可改</summary>
    public Godot.Collections.Dictionary KeyBindings { get; set; } = new();

    private readonly Godot.Collections.Dictionary _defaultBindings = new();

    private void CaptureDefaultBindings()
    {
        _defaultBindings.Clear();
        foreach (var a in REBINDABLE_ACTIONS)
        {
            _defaultBindings[a] = GetActionKeycodes(a);
        }
    }

    private Godot.Collections.Array<int> GetActionKeycodes(StringName action)
    {
        var outArr = new Godot.Collections.Array<int>();
        foreach (var ev in InputMap.ActionGetEvents(action))
        {
            if (ev is InputEventKey keyEvent)
            {
                var k = keyEvent.Keycode != Key.None ? (int)keyEvent.Keycode : (int)keyEvent.PhysicalKeycode;
                outArr.Add(k);
                if (outArr.Count >= 2)
                {
                    break;
                }
            }
        }

        return outArr;
    }

    /// <summary>用 key_bindings（含 profile 覆盖）刷新 InputMap</summary>
    public void ApplyKeyBindings()
    {
        // H02（健壮性审核）：只擦除键盘事件，保留手柄事件——action_erase_events 会连
        // _bind_joypad_defaults 装配的手柄绑定一起清掉（改键后本会话手柄失效）
        foreach (var a in REBINDABLE_ACTIONS)
        {
            foreach (var ev in InputMap.ActionGetEvents(a))
            {
                if (ev is InputEventKey)
                {
                    InputMap.ActionEraseEvent(a, ev);
                }
            }

            var bindings = KeyBindings.GetValueOrDefault(a, _defaultBindings.GetValueOrDefault(a, new Variant())).AsGodotArray();
            foreach (var k in bindings)
            {
                var ev = new InputEventKey { Keycode = (Key)(int)k.AsInt64() };
                InputMap.ActionAddEvent(a, ev);
            }
        }
    }

    /// <summary>P0-1（竞品调研）：手柄默认绑定运行时装配——project.godot 保持键盘单一事实源，
    /// 手柄左摇杆移动/动作键/右摇杆瞄准在此追加（InputMap.action_add_event），
    /// 与 keybind 改键系统（只改键盘事件）互不覆盖；一次装配幂等。</summary>
    private void BindJoypadDefaults()
    {
        if (_joypadBound)
        {
            return;
        }

        _joypadBound = true;
        // 左摇杆移动（轴 0=x、1=y；axis_value 负=上/左）
        AddJoyAxis("move_up", 1, -1.0);
        AddJoyAxis("move_down", 1, 1.0);
        AddJoyAxis("move_left", 0, -1.0);
        AddJoyAxis("move_right", 0, 1.0);
        // 动作键（B=ui_cancel 已被引擎默认占用，返航让位 Y）
        AddJoyButton("dash", 0); // A
        AddJoyButton("boost", 5); // RB
        AddJoyButton("fine_move", 4); // LB
        AddJoyButton("dock", 2); // X
        AddJoyButton("homecoming", 3); // Y（长按返航）
        AddJoyButton("give_up", 7); // R3（长按放弃）
        AddJoyButton("buff_panel", 6); // L3（展开/收起 buff 栏）
        AddJoyButton("restart", 0); // A（结算/暂停重开）
        AddJoyAxis("parry", 4, -1.0); // LT 左扳机（弧光弹反盾，轴 4 负向按下；阈值经 deadzone）
        // 右摇杆瞄准（player.aim_point 经 Input.get_vector 读取四向动作，虚拟准星）。
        // H01（健壮性审核）：必须装配正负两个独立动作——get_vector(pos, neg) 取 strength 差值，
        // 同一动作正负双向传会恒为零（右摇杆瞄准完全失效）
        AddJoyAxis("aim_left", 2, -1.0);
        AddJoyAxis("aim_right", 2, 1.0);
        AddJoyAxis("aim_up", 3, -1.0);
        AddJoyAxis("aim_down", 3, 1.0);
        // 应用已持久化的摇杆死区（不触发 save/广播，启动装配专用）
        foreach (var a in JOYPAD_ACTIONS)
        {
            if (InputMap.HasAction(a))
            {
                InputMap.ActionSetDeadzone(a, (float)JoyDeadzone);
            }
        }
    }

    private void AddJoyAxis(StringName action, int axis, double value)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }

        var ev = new InputEventJoypadMotion { Axis = (JoyAxis)axis, AxisValue = (float)value };
        InputMap.ActionAddEvent(action, ev);
    }

    private void AddJoyButton(StringName action, int button)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }

        var ev = new InputEventJoypadButton { ButtonIndex = (JoyButton)button };
        InputMap.ActionAddEvent(action, ev);
    }

    /// <summary>PS 布局适配：手柄插拔时重检布局</summary>
    private void OnJoyConnectionChanged(long device, bool connected) => DetectJoyLayout();

    /// <summary>检测已连接手柄的布局：SDL GUID vendor = 0x054c（LE "4c05"）为 Sony（DualShock/DualSense），
    /// 名称含 PlayStation 特征词兜底；其余保持 Xbox/SDL 标准布局（位置语义一致）。</summary>
    private void DetectJoyLayout()
    {
        var found = new StringName();
        foreach (var d in Input.GetConnectedJoypads())
        {
            if (IsPsGuid(Input.GetJoyGuid(d)))
            {
                found = new StringName("ps");
                break;
            }

            var name = Input.GetJoyName(d).ToLowerInvariant();
            if (name.Contains("dualshock") || name.Contains("dualsense") || name.Contains("playstation"))
            {
                found = new StringName("ps");
                break;
            }
        }

        if (found != new StringName() && found != JoyLayout)
        {
            JoyLayout = found;
            EmitSignal(SignalName.JoyLayoutChanged, JoyLayout);
        }
        else if (found == new StringName() && JoyLayout != new StringName("xbox"))
        {
            // 2026-08-03 审计：全部手柄拔出时回落 Xbox/SDL 布局，防 PS 标签残留误导设置页
            JoyLayout = new StringName("xbox");
            EmitSignal(SignalName.JoyLayoutChanged, JoyLayout);
        }
    }

    /// <summary>Sony 手柄 GUID 判定（SDL GUID：vendor 0x054c 小端序为 "4c05"；PS4/PS5/DualShock/DualSense）</summary>
    public bool IsPsGuid(string guid) => guid.StartsWith("030000004c05");

    /// <summary>手柄按钮的物理标签（按当前布局）：PS 用 ✕○□△/L1/R1…，Xbox/SDL 用 A/B/X/Y/LB/RB…</summary>
    public string JoyButtonLabel(int button)
    {
        if (JoyLayout == new StringName("ps"))
        {
            return PS_BUTTON_LABELS.GetValueOrDefault(button, XBOX_BUTTON_LABELS.GetValueOrDefault(button, button.ToString())).ToString();
        }

        return XBOX_BUTTON_LABELS.GetValueOrDefault(button, button.ToString()).ToString();
    }

    /// <summary>改键：清除该动作现有键设新键；冲突键从占用者移除（允许交换）
    /// G04：冲突清理同时扫默认绑定——未自定义动作的默认键被占用时置空绑定覆盖默认，
    /// 避免 apply_key_bindings 从默认表重灌同键造成两动作冲突</summary>
    public bool RebindAction(StringName action, int keycode)
    {
        if (!REBINDABLE_ACTIONS.Contains(action))
        {
            return false;
        }

        foreach (var a in REBINDABLE_ACTIONS)
        {
            if (a == action)
            {
                continue;
            }

            var effective = KeyBindings.GetValueOrDefault(a, _defaultBindings.GetValueOrDefault(a, new Variant())).AsGodotArray();
            if (effective.Count == 0)
            {
                continue; // 空绑定 = 该动作无键，不占用任何键
            }

            if (effective.Contains(keycode))
            {
                if (KeyBindings.ContainsKey(a))
                {
                    KeyBindings[a].AsGodotArray().Remove(keycode);
                }
                else
                {
                    KeyBindings[a] = new Godot.Collections.Array(); // 默认键被占用：空绑定覆盖默认，解除占用
                }
            }
        }

        KeyBindings[action] = new Godot.Collections.Array { keycode };
        ApplyKeyBindings();
        SaveProfile();
        EmitSignal(SignalName.KeyBindingsChanged);
        return true;
    }

    public void ResetKeyBindings()
    {
        KeyBindings = (Godot.Collections.Dictionary)_defaultBindings.Duplicate(true);
        ApplyKeyBindings();
        SaveProfile();
        EmitSignal(SignalName.KeyBindingsChanged);
    }

    public string ActionKeysText(StringName action)
    {
        var keys = KeyBindings.GetValueOrDefault(action, _defaultBindings.GetValueOrDefault(action, new Variant())).AsGodotArray();
        if (keys.Count == 0)
        {
            return (string)Tr("SET_UNBOUND");
        }

        var parts = new List<string>();
        foreach (var k in keys)
        {
            parts.Add(OS.GetKeycodeString((Key)(int)k.AsInt64()));
        }

        return string.Join(" / ", parts);
    }

    // ---------------- 语言（中英双语） ----------------

    /// <summary>当前语言（"zh"/"en"，profile 持久化）。
    /// 存储承载于 snake 字段 locale（GDScript 直读写；桥，M7 过渡，删除前）——属性转发字段。</summary>
    public string Locale { get; set; } = "zh";

    public void SetLocale(string pLocale)
    {
        if (pLocale != "zh" && pLocale != "en")
        {
            return;
        }

        Locale = pLocale;
        TranslationServer.SetLocale(pLocale);
        SaveProfile();
        EmitSignal(SignalName.LocaleChanged);
    }

    // ---------------- RP 经济 / 基地任务 / 天赋路线 ----------------

    public void AddRp(int amount)
    {
        Rp += amount;
        EmitSignal(SignalName.RpChanged, Rp);
    }

    /// <summary>余额不足返回 false 且不扣减</summary>
    public bool SpendRp(int amount)
    {
        if (Rp < amount)
        {
            return false;
        }

        Rp -= amount;
        EmitSignal(SignalName.RpChanged, Rp);
        return true;
    }

    private void InitMissions()
    {
        Missions.Clear();
        foreach (var def in MISSION_DEFS)
        {
            // P0-3：goal 一次性缓存进条目，_set_mission_progress 免每帧线性扫 MISSION_POOL
            Missions[def["id"]] = new Godot.Collections.Dictionary { ["progress"] = 0, ["claimed"] = false, ["goal"] = (int)def["goal"].AsInt64() };
        }

        // 任务轮换：每局从全新洗牌序列开始（初始手牌固定 MISSION_DEFS，刷新才随机）
        _taskPool = new TaskPool(MISSION_POOL); // M7：TaskPool 迁 C#，typed
        RebuildKindIndex();
    }

    /// <summary>kind -> 池内 id 索引重建（MISSION_POOL 为 const，仅 _init_missions 调用一次）</summary>
    private void RebuildKindIndex()
    {
        _missionsByKind.Clear();
        foreach (var def in MISSION_POOL)
        {
            var kind = def["kind"].AsStringName();
            if (!_missionsByKind.ContainsKey(kind))
            {
                _missionsByKind[kind] = new Godot.Collections.Array();
            }

            _missionsByKind[kind].AsGodotArray().Add(def["id"]);
        }
    }

    /// <summary>C32 修复：公开任务重置口（仅清任务进度，不清 rp/buffs——比 reset_run 副作用小，
    /// 供测试/调用方在保留状态的前提下重置 missions）</summary>
    public void ResetMissions() => InitMissions();

    private void SetMissionProgress(StringName id, int value)
    {
        if (!Missions.ContainsKey(id))
        {
            return;
        }

        var m = Missions[id].AsGodotDictionary();
        // P4（2026-08-05）：进度负值钳 0（防御；正常路径 value 恒 ≥0，手改存档/异常注入不产生负进度）
        var clamped = Mathf.Max(value, 0);
        // P0-3：survive 类每帧触发但整秒才变化一次，未变化跳过字典写与完成判定
        if ((int)m["progress"].AsInt64() == clamped)
        {
            return;
        }

        var goal = (int)m.GetValueOrDefault("goal", 0).AsInt64();
        var wasDone = (int)m["progress"].AsInt64() >= goal;
        m["progress"] = clamped;
        if (!wasDone && clamped >= goal)
        {
            EmitSignal(SignalName.MissionCompleted, id);
        }
    }

    /// <summary>按 kind 推进全部该类型在场任务的进度（任务轮换后 id 变化，进度源按 kind 分发；
    /// 已不在场的 id 由 _set_mission_progress 的 missions.has 守卫自动跳过）</summary>
    private void SetKindProgress(StringName kind, int value)
    {
        // U16：TryGetValue 免空容器默认值每次分配（原 GetValueOrDefault 实参先求值分配空 Array）
        if (_missionsByKind.TryGetValue(kind, out var list))
        {
            foreach (var idV in list.AsGodotArray())
            {
                SetMissionProgress(idV.AsStringName(), value);
            }
        }
    }

    /// <summary>在场任务 id 列表（base_console 任务面板据此渲染；任务轮换后不再等于 MISSION_DEFS）</summary>
    public Godot.Collections.Array<StringName> ActiveMissionIds()
    {
        var outArr = new Godot.Collections.Array<StringName>();
        foreach (var id in Missions.Keys)
        {
            outArr.Add(id.AsStringName());
        }

        return outArr;
    }

    /// <summary>任务定义查询（MISSION_POOL 无命中返回 {}，供 goal/存档恢复校验共用）</summary>
    private Godot.Collections.Dictionary MissionDef(StringName id)
    {
        foreach (var def in MISSION_POOL)
        {
            if (def["id"].AsStringName() == id)
            {
                return def;
            }
        }

        return new Godot.Collections.Dictionary();
    }

    public int MissionGoal(StringName id) => (int)MissionDef(id).GetValueOrDefault("goal", 0).AsInt64();

    // U16：TryGetValue 免空容器默认值每次分配（原 GetValueOrDefault 实参先求值分配空 Dictionary）
    public int MissionProgress(StringName id) =>
        Missions.TryGetValue(id, out var rec)
            ? (int)rec.AsGodotDictionary().GetValueOrDefault("progress", 0).AsInt64()
            : 0;

    public bool IsMissionDone(StringName id) => Missions.ContainsKey(id) && MissionProgress(id) >= MissionGoal(id);

    public bool IsMissionClaimed(StringName id) =>
        Missions.TryGetValue(id, out var rec)
            && (bool)rec.AsGodotDictionary().GetValueOrDefault("claimed", false).AsBool();

    /// <summary>领取已完成任务的 +3RP，每任务每局限领一次</summary>
    public bool ClaimMission(StringName id)
    {
        if (!IsMissionDone(id) || IsMissionClaimed(id))
        {
            return false;
        }

        Missions[id].AsGodotDictionary()["claimed"] = true;
        AddRp(RpMissionRewardValue);
        return true;
    }

    // ---------------- 基地任务轮换（RefreshPoints 经济 + TaskPool 重抽） ----------------

    /// <summary>进基地发放刷新点数（amount &lt; 0 用 GRANT_PER_VISIT 档位值；base_console.show_base 调用）</summary>
    public void GrantRefreshPoints(int amount = -1)
    {
        RefreshPoints += amount < 0 ? GRANT_PER_VISIT : amount;
        EmitSignal(SignalName.RefreshPointsChanged, RefreshPoints);
    }

    /// <summary>刷新资格校验（点数不足禁止刷新；UI 据此禁用按钮并提示）</summary>
    public bool CanRefreshMissions() => RefreshPoints >= REFRESH_COST;

    /// <summary>刷新任务：消耗 RefreshPoints 重抽任务（槽位数 MISSION_SLOTS）。
    /// 已完成未领取的任务保留（防止刷新吞掉待领奖励），其余槽位从任务池无放回重抽
    /// （排除在场 id，避免与保留槽位重号）。余额不足返回 false 且不扣减。</summary>
    public bool RefreshMissions()
    {
        if (!CanRefreshMissions())
        {
            return false;
        }

        if (_taskPool == null || !GodotObject.IsInstanceValid(_taskPool))
        {
            InitMissions(); // 防御：池未初始化（异常时序）时重建
        }

        RefreshPoints -= REFRESH_COST;
        EmitSignal(SignalName.RefreshPointsChanged, RefreshPoints);
        // 收集保留条目（已完成未领取）与在场 id（重抽排除全部在场 id：
        // 既防抽回刚换下的任务，也防与保留任务重号覆盖其进度）
        var kept = new Godot.Collections.Dictionary();
        var exclude = new Godot.Collections.Array<StringName>();
        foreach (var idV in Missions.Keys)
        {
            var id = idV.AsStringName();
            if (IsMissionDone(id) && !IsMissionClaimed(id))
            {
                kept[id] = Missions[id];
            }

            exclude.Add(id);
        }

        var drawn = _taskPool!.Draw(MissionSlotsValue - kept.Count, exclude);
        Missions.Clear();
        foreach (var idV in kept.Keys)
        {
            Missions[idV] = kept[idV];
        }

        foreach (var def in drawn) // U13：Draw 返回 typed Array<Dictionary>，元素直接是 Dictionary
        {
            Missions[def["id"]] = new Godot.Collections.Dictionary { ["progress"] = 0, ["claimed"] = false, ["goal"] = def["goal"] };
        }

        return true;
    }

    /// <summary>选择天赋路线：该线两个 buff 的层数合并到所选 buff，另一个锁定不进奖励池。
    /// line/buff 非法或该线没有任何层数时返回 false。</summary>
    public bool ChooseRoute(StringName line, StringName buffId)
    {
        if (!ROUTE_LINES.ContainsKey(line))
        {
            return false;
        }

        var options = ROUTE_LINES[line].AsGodotArray();
        if (!options.Contains(buffId))
        {
            return false;
        }

        var other = options[1].AsStringName() == buffId ? options[0].AsStringName() : options[1].AsStringName();
        var total = BuffCount(buffId) + BuffCount(other);
        if (total <= 0)
        {
            return false;
        }

        Buffs[buffId] = total;
        Buffs.Remove(other);
        ChosenRoutes[line] = buffId;
        LockedRoutes[line] = other;
        EmitSignal(SignalName.RouteChosen, line, buffId);
        EmitSignal(SignalName.BuffsChanged);
        return true;
    }

    /// <summary>奖励池抽取时排除锁定 buff</summary>
    public bool IsBuffLocked(StringName buffId)
    {
        foreach (var v in LockedRoutes.Values)
        {
            if (v.AsStringName() == buffId)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- 用户会话（2026-08-04 账户系统） ----------------

    /// <summary>登录已有用户：载入其设置/最高分并即时生效（locale 即时 set_locale——B7-11）</summary>
    public void LoginUser(string name)
    {
        if (!_userDb.UserExists(name))
        {
            return;
        }

        CurrentUser = name;
        _userDb.RecordLogin(name);
        LoadSessionSettings();
        TranslationServer.SetLocale(Locale);
        ApplyKeyBindings();
        ApplyWindowSize();
        InvalidateViewRectCache();
    }

    /// <summary>游客进入：设置仅内存、不存档、不写统计（B7-8）；保留当前内存值（启动 profile 值视作游客会话）</summary>
    public void LoginGuest() => CurrentUser = "Guest";

    /// <summary>退出：登录用户落盘设置；游客丢弃（内存）；复位未登录</summary>
    public void LogoutUser()
    {
        if (CurrentUser != "" && CurrentUser != "Guest")
        {
            SaveProfile();
        }

        CurrentUser = "";
    }

    public bool IsGuest() => CurrentUser == "Guest";

    /// <summary>当前会话存档路径：登录用户 = 每用户文件；未登录 = 旧单文件；游客无路径（不存档）</summary>
    private string SavePathForCurrent()
    {
        if (CurrentUser == "")
        {
            return SavePathValue;
        }

        if (IsGuest())
        {
            return "";
        }

        return _userDb.SavefileForUser(CurrentUser);
    }

    /// <summary>载入当前会话档案：登录用户 → user_db settings + 统计；游客/未登录 → 保留内存（游客不落盘）</summary>
    private void LoadSessionSettings()
    {
        if (CurrentUser == "" || IsGuest())
        {
            return;
        }

        ApplySettingsDict(_userDb.GetUserSettings(CurrentUser));
        HighScore = (int)_userDb.GetUserData(CurrentUser).GetValueOrDefault("high_score", 0).AsInt64();
    }

    /// <summary>profile.json 退役迁移（B5）：启动时存在旧 profile 且用户表为空 → 缓存待首个注册用户合并</summary>
    private void MaybeMigrateLegacyProfile()
    {
        if (_pendingLegacyProfile.Count > 0)
        {
            return;
        }

        if (_saveManager.Exists(ProfilePathValue) && _userDb.ListUsernames().Count == 0)
        {
            var parsed = _saveManager.Load(ProfilePathValue);
            if (parsed.Count > 0)
            {
                _pendingLegacyProfile = parsed;
            }
        }
    }

    /// <summary>Q25（2026-08-05）：旧 profile 迁移缓存查询/触发/清空公开化（A7 私有访问残留收敛，
    /// 测试经公开接口；生产路径不变——create_user 消费后自清）</summary>
    public bool LegacyMigrationPending() => _pendingLegacyProfile.Count > 0;

    public void ScanLegacyMigration() => MaybeMigrateLegacyProfile();

    public void ClearLegacyMigration() => _pendingLegacyProfile.Clear();

    /// <summary>注册用户（转发 user_db.create_user）；成功后合并旧 profile 迁移数据并删除 profile.json（B5）</summary>
    public bool CreateUser(string name, string password)
    {
        if (!_userDb.CreateUser(name, password))
        {
            return false;
        }

        if (_pendingLegacyProfile.Count > 0)
        {
            var legacy = (Godot.Collections.Dictionary)_pendingLegacyProfile.Duplicate();
            _pendingLegacyProfile.Clear();
            _userDb.UpdateHighScore(name, (int)SaveNum(legacy.GetValueOrDefault("high_score", 0), 0.0));
            legacy.Remove("high_score");
            legacy.Remove("version");
            legacy.Remove("highscores");
            _userDb.UpdateUserSettings(name, legacy);
            _saveManager.Delete(ProfilePathValue);
        }

        return true;
    }

    /// <summary>用户数据库转发（A2 组合服务；供 welcome 登录面板使用）</summary>
    public bool VerifyUser(string name, string password) => _userDb.VerifyUser(name, password);

    public bool UserExists(string name) => _userDb.UserExists(name);

    public Godot.Collections.Array<String> ListUsernames()
    {
        var outArr = new Godot.Collections.Array<String>();
        foreach (var n in _userDb.ListUsernames()) // U13：typed Array<string>，元素直接是 string
        {
            outArr.Add(n);
        }

        return outArr;
    }

    /// <summary>2026-08-06 审计：UserDB 显式重载（测试 wipe user:// 后刷新缓存起点——GameState
    /// _ready 的迁移探测会提前缓存真实用户表；Q23 快照范式配套）</summary>
    public void ReloadUserDb() => _userDb.Reload();

    public string GetLastLoginUser() => _userDb.GetLastLoginUser();

    public bool DeleteUser(string name, string password) => _userDb.DeleteUser(name, password);

    public Godot.Collections.Array GetLeaderboard() => _userDb.GetLeaderboard();

    public Godot.Collections.Dictionary GetUserSettings(string name) => _userDb.GetUserSettings(name);

    public void UpdateUserSettings(string name, Godot.Collections.Dictionary settings) => _userDb.UpdateUserSettings(name, settings);

    public Godot.Collections.Dictionary GetUserData(string name) => _userDb.GetUserData(name);

    public string UserDbSavefileFor(string name) => _userDb.SavefileForUser(name);

    // ---------------- 对局存档（登录用户 = user://savegame_<user>_<hash12>.json；游客不存档） ----------------

    public void SaveRun(double fuel, double elapsed)
    {
        if (IsGuest())
        {
            return; // 游客不存档（B7-8）
        }

        var path = SavePathForCurrent();
        if (path == "")
        {
            return;
        }

        var data = new Godot.Collections.Dictionary
        {
            ["version"] = PersistVersionValue,
            ["score"] = Score,
            ["kills"] = Kills,
            ["health"] = Health,
            ["fuel"] = fuel,
            ["boss_kills"] = BossKills,
            ["difficulty_multiplier"] = DifficultyMultiplier,
            ["buffs"] = Buffs.Duplicate(),
            ["elapsed"] = elapsed,
            ["rp"] = Rp,
            ["refresh_points"] = RefreshPoints,
            ["missions"] = Missions.Duplicate(true),
            ["chosen_routes"] = ChosenRoutes.Duplicate(),
            ["locked_routes"] = LockedRoutes.Duplicate(),
            ["ctrl_toggle_mode"] = CtrlToggleMode,
            ["shift_toggle_mode"] = ShiftToggleMode,
            ["touch_controls"] = TouchControls,
        };
        if (CurrentUser != "")
        {
            data["username"] = CurrentUser;
        }

        // A2 阶段 2：文件 IO 委托 SaveManager
        _saveManager.Save(path, data);
    }

    public bool HasSave()
    {
        if (IsGuest())
        {
            return false;
        }

        return _saveManager.Exists(SavePathForCurrent());
    }

    public Godot.Collections.Dictionary LoadRunData()
    {
        SaveCorrupt = false;
        var path = SavePathForCurrent();
        if (path == "" || !_saveManager.Exists(path))
        {
            return new Godot.Collections.Dictionary();
        }

        var data = _saveManager.Load(path);
        if (_saveManager.LastWasCorrupt)
        {
            // 损坏存档已由 SaveManager 隔离备份（<path>.corrupt），按无存档处理（不留死路径）。
            // 2026-08-06 审计 M2：必须直接返回——空字典继续做档主校验会走 quarantine 二次隔离，
            // 先删刚生成的 .corrupt 备份再 rename 已不存在的正本（失败刷伪警告），损坏档彻底消失
            SaveCorrupt = true;
            return new Godot.Collections.Dictionary();
        }

        if (CurrentUser != "" && data.GetValueOrDefault("username", "").AsString() != CurrentUser)
        {
            // B5 读档校验：档主不匹配（手改/旧匿名档）→ 隔离备份按无存档处理
            _saveManager.Quarantine(path);
            SaveCorrupt = true;
            return new Godot.Collections.Dictionary();
        }

        return data;
    }

    /// <summary>存档数值字段安全读取：手改存档的非法类型（字符串/数组/字典等）回默认值
    /// （委托 SaveManager 壳 sanitize_num——GDScript 浮点为 64 位，经 Variant 往返保持逐位等价）</summary>
    public double SaveNum(Variant v, double defaultValue) => _saveManager.SanitizeNum(v, defaultValue);

    /// <summary>C16 修复：布尔字段安全读取——仅接受真 bool（GDScript 的 bool("false") 为 true，
    /// 手改存档写字符串 "false"/"0" 会被误读为开；与 save_num 同款判型回退）</summary>
    public bool SaveBool(Variant v, bool defaultValue) => v.VariantType == Variant.Type.Bool ? v.AsBool() : defaultValue;

    public void ApplyRunSave(Godot.Collections.Dictionary data)
    {
        // 逐字段判型：语法合法但结构非法的存档（手改）不崩，异常字段回默认值
        // R07：负值钳 0（L 系列判型族登记遗留）——手改负 score/kills 破坏统计与排行榜
        Score = (int)Mathf.Max(SaveNum(data.GetValueOrDefault("score", 0), 0.0), 0.0);
        Kills = (int)Mathf.Max(SaveNum(data.GetValueOrDefault("kills", 0), 0.0), 0.0);
        BossKills = (int)Mathf.Max(SaveNum(data.GetValueOrDefault("boss_kills", 0), 0.0), 0.0);
        DifficultyMultiplier = SaveNum(data.GetValueOrDefault("difficulty_multiplier", 1.0), 1.0);
        Buffs.Clear();
        var savedBuffs = data.GetValueOrDefault("buffs", new Variant());
        if (savedBuffs.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in savedBuffs.AsGodotDictionary().Keys)
            {
                var v = savedBuffs.AsGodotDictionary()[key];
                if (v.VariantType is Variant.Type.Int or Variant.Type.Float)
                {
                    // G013：层数钳制 ≥0（手改存档负层数会破坏 buff_count 逻辑；超大值属手改作弊。
                    // 注：add_buff 本身无 max_stacks 钳制——上限约束在 buff_select 选取侧检查
                    // （buffs.<id>.max_stacks），此处仅保下限防负层数，不改存档恢复行为）
                    Buffs[key.AsStringName()] = Mathf.Max((int)v.AsInt64(), 0);
                }
            }
        }

        EmitSignal(SignalName.BuffsChanged);
        // 血量在 buffs 恢复之后再处理（max_health() 依赖 extra_life 层数）
        // v1（3 命制 lives）存档不回迁血量，按满血开；v2 起读 health
        if ((int)SaveNum(data.GetValueOrDefault("version", 1), 1.0) >= 2)
        {
            Health = Mathf.Clamp(SaveNum(data.GetValueOrDefault("health", MaxHealth()), MaxHealth()), 0.0, MaxHealth());
        }
        else
        {
            Health = MaxHealth();
        }

        RunTime = SaveNum(data.GetValueOrDefault("elapsed", 0.0), 0.0);
        // 难度乘数按曲线从 boss_kills + run_time 重算（旧档的 difficulty_multiplier 字段仅作读入兼容）
        RecomputeDifficultyInternal();
        Rp = (int)SaveNum(data.GetValueOrDefault("rp", 0), 0.0);
        // 任务轮换：刷新点数随存档往返（手改负值钳制 ≥0）
        RefreshPoints = Mathf.Max((int)SaveNum(data.GetValueOrDefault("refresh_points", 0), 0.0), 0);
        EmitSignal(SignalName.RefreshPointsChanged, RefreshPoints);
        InitMissions();
        // 任务轮换：先清空初始手牌再恢复存档任务——存档集合可能含池内非手牌 id
        // （如 kill_15），不清空会使初始手牌未在存档中的 id（survive_180/boss_1）残留
        Missions.Clear();
        var savedMissions = data.GetValueOrDefault("missions", new Variant());
        if (savedMissions.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in savedMissions.AsGodotDictionary().Keys)
            {
                var id = key.AsStringName();
                var m = savedMissions.AsGodotDictionary()[key];
                // 任务轮换：恢复条件从「初始手牌包含」放宽为「id 属于任务池」——
                // 轮换后的任务（如 kill_15）不在初始手牌，必须能随存档恢复
                if (MissionDef(id).Count == 0 || m.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                var md = m.AsGodotDictionary();
                var claimed = md.GetValueOrDefault("claimed", false);
                // H18（健壮性审核）：恢复保留 goal 键——整体替换会丢 goal 致
                // mission_completed 判定 progress >= 0 恒真而永久哑火（潜伏）
                Missions[id] = new Godot.Collections.Dictionary
                {
                    ["progress"] = (int)SaveNum(md.GetValueOrDefault("progress", 0), 0.0),
                    ["claimed"] = claimed.VariantType == Variant.Type.Bool ? claimed : false,
                    // 2026-08-06 审计：goal 走 save_num 判型（R06/R07 判型族同族遗漏——裸 int()
                    // 对手改字符串/数组值抛类型错误或静默转 0 使任务永久可领）
                    ["goal"] = (int)SaveNum(md.GetValueOrDefault("goal", MissionGoal(id)), MissionGoal(id)),
                };
            }
        }

        ChosenRoutes.Clear();
        var savedChosen = data.GetValueOrDefault("chosen_routes", new Variant());
        if (savedChosen.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in savedChosen.AsGodotDictionary().Keys)
            {
                var v = savedChosen.AsGodotDictionary()[key];
                if (v.VariantType is Variant.Type.String or Variant.Type.StringName)
                {
                    ChosenRoutes[key.AsStringName()] = v.AsStringName();
                }
            }
        }

        LockedRoutes.Clear();
        var savedLocked = data.GetValueOrDefault("locked_routes", new Variant());
        if (savedLocked.VariantType == Variant.Type.Dictionary)
        {
            foreach (var key in savedLocked.AsGodotDictionary().Keys)
            {
                var v = savedLocked.AsGodotDictionary()[key];
                if (v.VariantType is Variant.Type.String or Variant.Type.StringName)
                {
                    LockedRoutes[key.AsStringName()] = v.AsStringName();
                }
            }
        }

        // 设置项随存档往返（旧存档无字段时保留当前值）
        CtrlToggleMode = SaveBool(data.GetValueOrDefault("ctrl_toggle_mode", CtrlToggleMode), CtrlToggleMode);
        ShiftToggleMode = SaveBool(data.GetValueOrDefault("shift_toggle_mode", ShiftToggleMode), ShiftToggleMode);
        TouchControls = SaveBool(data.GetValueOrDefault("touch_controls", TouchControls), TouchControls);
        // 里程碑曲线：恢复到大于当前分数的第一档（2026-08-07 批量推进迁移 C# 侧——
        // CountThresholdsUpTo 单次调用 + O(1)/档 增量推进，含原 while 的 10000 档挂死守卫；
        // 原逐档跨语言往返的 while 循环删除，存档恢复路径不再每档一次 GDScript 求值）
        _milestoneCount = (int)_progression.CountThresholdsUpTo(Score, Variant.From(MilestoneBase).AsGodotArray(), MilestoneCycleMult, MilestoneMult());
        _nextMilestone = MilestoneThreshold(_milestoneCount);
        EmitSignal(SignalName.ScoreChanged, Score);
        EmitSignal(SignalName.HealthChanged, Health);
        EmitSignal(SignalName.DifficultyChanged, DifficultyMultiplier);
        EmitSignal(SignalName.RpChanged, Rp);
    }

    public void DeleteSave()
    {
        var path = SavePathForCurrent();
        if (path != "")
        {
            _saveManager.Delete(path);
        }
    }

    // ---------------- 局外档案（登录用户 = user_db settings；游客仅内存；未登录 = 旧 profile.json 兼容路径） ----------------

    /// <summary>局外档案：最高分 + 难度档位 + 设置项（旧版 talents/talent_points 字段读取时忽略；
    /// 旧档案缺少新字段时保留当前内存值，保证兼容；损坏文件隔离备份后按默认值继续）</summary>
    public void LoadProfile()
    {
        ProfileCorrupt = false;
        if (CurrentUser != "")
        {
            return; // 会话模式下档案由登录流程管理（_load_session_settings）
        }

        var parsed = _saveManager.Load(ProfilePathValue);
        if (_saveManager.LastWasCorrupt)
        {
            ProfileCorrupt = true;
            return;
        }

        if (parsed.Count == 0)
        {
            return;
        }

        HighScore = (int)SaveNum(parsed.GetValueOrDefault("high_score", 0), 0.0); // save_num 判型：手改档案字符串等非法类型回默认
        ApplySettingsDict(parsed);
        // P0-3：高分榜判型加载（手改档案的元素级守卫，对齐 E11）——非法条目跳过、排序截断
        Highscores.Clear();
        var savedHighscores = parsed.GetValueOrDefault("highscores", new Variant());
        if (savedHighscores.VariantType == Variant.Type.Array)
        {
            foreach (var entryV in savedHighscores.AsGodotArray())
            {
                if (entryV.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                var entry = entryV.AsGodotDictionary();
                var s = entry.GetValueOrDefault("score", 0);
                if (s.VariantType is not Variant.Type.Int and not Variant.Type.Float)
                {
                    continue;
                }

                Highscores.Add(new Godot.Collections.Dictionary { ["score"] = (int)s.AsInt64(), ["date"] = (int)SaveNum(entry.GetValueOrDefault("date", 0), 0.0) }); // E11 同款：date 走 save_num 判型
            }

            // 泛型 Array&lt;Dictionary&gt; 无 SortCustom（Godot C# 未绑定）——List.Sort 降序重建
            var sortList = new List<Godot.Collections.Dictionary>();
            foreach (var entry in Highscores)
            {
                sortList.Add(entry);
            }

            // U15：int64 直接比较（原 (int) 截断 + 减法——score>2^31 手改档案排序语义漂移）
            sortList.Sort((a, b) => b["score"].AsInt64().CompareTo(a["score"].AsInt64()));
            Highscores.Clear();
            foreach (var entry in sortList)
            {
                Highscores.Add(entry);
            }

            if (Highscores.Count > HighscoreLimitValue)
            {
                Highscores.Resize(HighscoreLimitValue);
            }
        }
    }

    /// <summary>设置字段应用（profile.json 与 user_db settings 共用；含键位/窗口/视图缓存副作用，对齐原 load_profile）</summary>
    private void ApplySettingsDict(Godot.Collections.Dictionary data)
    {
        TutorialDone = SaveBool(data.GetValueOrDefault("tutorial_done", TutorialDone), TutorialDone);
        // E10：locale 加载经 zh/en 白名单守卫（对齐 set_locale）——手改非法值保持当前语言，
        // 避免 locale 变量与 TranslationServer 状态不一致
        var savedLocale = data.GetValueOrDefault("locale", Locale).AsString();
        if (savedLocale == "zh" || savedLocale == "en")
        {
            Locale = savedLocale;
        }

        // C02 修复：key_bindings 手改档案的类型守卫——非 Dictionary / 子值非 Array 时跳过该字段，
        // 不崩溃、不提前返回（其余字段照常加载）；typed 赋值在运行期校验失败会抛错并丢后续字段。
        KeyBindings.Clear();
        var savedKeys = data.GetValueOrDefault("key_bindings", new Variant());
        if (savedKeys.VariantType == Variant.Type.Dictionary)
        {
            foreach (var a in savedKeys.AsGodotDictionary().Keys)
            {
                var raw = savedKeys.AsGodotDictionary()[a];
                if (raw.VariantType != Variant.Type.Array)
                {
                    continue;
                }

                var keys = new Godot.Collections.Array<int>();
                foreach (var k in raw.AsGodotArray())
                {
                    // E11：元素级判型（C02 外层守卫的补全）——手改字符串 keycode 直接跳过，
                    // 不再 int() 转换错误刷屏（不崩溃但不干净）
                    if (k.VariantType is not Variant.Type.Int and not Variant.Type.Float)
                    {
                        continue;
                    }

                    keys.Add((int)k.AsInt64());
                }

                KeyBindings[a.AsStringName()] = keys;
            }
        }

        var savedDifficulty = data.GetValueOrDefault("difficulty", "").AsStringName();
        if (DIFFICULTY_DEFS.ContainsKey(savedDifficulty))
        {
            Difficulty = savedDifficulty;
            // Q04（2026-08-05）：存档/账户设置恢复难度后刷新被动回血缓存——
            // 原实现仅 _apply_balance 与 set_difficulty 刷新，重启后 hard 玩家按 medium 回血
            RefreshRegenCache();
        }

        CtrlToggleMode = SaveBool(data.GetValueOrDefault("ctrl_toggle_mode", CtrlToggleMode), CtrlToggleMode);
        ShiftToggleMode = SaveBool(data.GetValueOrDefault("shift_toggle_mode", ShiftToggleMode), ShiftToggleMode);
        var savedZoom = data.GetValueOrDefault("view_zoom", "").AsStringName();
        if (VIEW_ZOOM_LEVELS.ContainsKey(savedZoom))
        {
            ViewZoom = savedZoom;
            _viewZoomFactor = (double)VIEW_ZOOM_LEVELS[savedZoom].AsDouble();
            InvalidateViewRectCache();
        }

        var savedWindow = data.GetValueOrDefault("window_size", "").AsStringName();
        if (WINDOW_SIZE_LEVELS.ContainsKey(savedWindow))
        {
            WindowSize = savedWindow;
            ApplyWindowSize();
        }

        var savedAim = data.GetValueOrDefault("aim_assist", "").AsStringName();
        if (AIM_ASSIST_ORDER.Contains(savedAim))
        {
            AimAssistLevel = savedAim;
        }

        ReduceFlash = SaveBool(data.GetValueOrDefault("reduce_flash", ReduceFlash), ReduceFlash);
        MouseLock = SaveBool(data.GetValueOrDefault("mouse_lock", MouseLock), MouseLock);
        // P0-1 手柄设置：灵敏度默认取 balance player.aim_assist.joy_speed，死区默认 0.5
        var joySpeed = data.GetValueOrDefault("joy_aim_speed", Cfg("player.aim_assist.joy_speed", JoyAimSpeed));
        if (joySpeed.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            JoyAimSpeed = Mathf.Clamp(joySpeed.AsDouble(), 200.0, 4000.0);
        }

        var joyDz = data.GetValueOrDefault("joy_deadzone", JoyDeadzone);
        if (joyDz.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            JoyDeadzone = Mathf.Clamp(joyDz.AsDouble(), 0.05, 0.9);
        }
    }

    /// <summary>当前设置字段收集（profile.json 与 user_db settings 共用；统计类字段不在此列）</summary>
    private Godot.Collections.Dictionary CollectSettingsDict() => new()
    {
        ["tutorial_done"] = TutorialDone,
        ["key_bindings"] = KeyBindings,
        ["locale"] = Locale,
        ["difficulty"] = Difficulty.ToString(),
        ["ctrl_toggle_mode"] = CtrlToggleMode,
        ["shift_toggle_mode"] = ShiftToggleMode,
        ["view_zoom"] = ViewZoom.ToString(),
        ["window_size"] = WindowSize.ToString(),
        ["aim_assist"] = AimAssistLevel.ToString(),
        ["reduce_flash"] = ReduceFlash,
        ["mouse_lock"] = MouseLock,
        ["joy_aim_speed"] = JoyAimSpeed,
        ["joy_deadzone"] = JoyDeadzone,
    };

    public void SaveProfile()
    {
        if (IsGuest())
        {
            return; // 游客设置仅内存（B7-8）
        }

        if (CurrentUser != "")
        {
            _userDb.UpdateUserSettings(CurrentUser, CollectSettingsDict());
            return;
        }

        var data = CollectSettingsDict();
        data["version"] = PersistVersionValue;
        data["high_score"] = HighScore;
        data["highscores"] = Highscores;
        _saveManager.Save(ProfilePathValue, data);
    }

    /// <summary>记录最高分，破纪录返回 true（登录用户写 user_db；游客仅内存；未登录写旧 profile.json）</summary>
    public bool RecordScore()
    {
        if (Score > HighScore)
        {
            HighScore = Score;
            if (IsGuest())
            {
                return true;
            }

            if (CurrentUser != "")
            {
                _userDb.UpdateHighScore(CurrentUser, Score);
            }
            else
            {
                SaveProfile();
            }

            return true;
        }

        return false;
    }

    /// <summary>Q06（2026-08-05）：一局对局统计落地（账户计划 Task 2 game_over_stats）——死亡结算调用。
    /// 登录用户累计 total_kills/games_played；游客/未登录跳过（游客不写统计，B7-8）</summary>
    public void RecordGameOver()
    {
        if (CurrentUser == "" || IsGuest() || !_userDb.UserExists(CurrentUser))
        {
            return;
        }

        var data = _userDb.GetUserData(CurrentUser);
        _userDb.UpdateUserData(CurrentUser, new Godot.Collections.Dictionary
        {
            ["total_kills"] = (int)data.GetValueOrDefault("total_kills", 0).AsInt64() + Kills,
            ["games_played"] = (int)data.GetValueOrDefault("games_played", 0).AsInt64() + 1,
        });
    }

    /// <summary>提交本局分数入本地榜，返回名次（1-based；未上榜返回 0）。
    /// 同分新条目排后（先到先得）；超出上限的分数不入榜。登录/游客走 user_db 排行榜（游客以 "Guest" 提交，B7-8）。</summary>
    public int SubmitHighscore(int runScore)
    {
        if (CurrentUser != "")
        {
            return (int)_userDb.SubmitScore(CurrentUser, runScore);
        }

        if (runScore <= 0)
        {
            return 0;
        }

        var rank = 1;
        foreach (var e in Highscores)
        {
            if ((int)e["score"].AsInt64() >= runScore)
            {
                rank += 1;
            }
            else
            {
                break;
            }
        }

        if (rank > HighscoreLimitValue)
        {
            return 0;
        }

        Highscores.Insert(rank - 1, new Godot.Collections.Dictionary
        {
            ["score"] = runScore,
            ["date"] = (int)Time.GetUnixTimeFromSystem(),
        });
        if (Highscores.Count > HighscoreLimitValue)
        {
            Highscores.Resize(HighscoreLimitValue);
        }

        SaveProfile();
        return rank;
    }

    /// <summary>榜单文本（供结算页/开始页展示）："1. 12345\n2. 9876..."；空榜返回空串</summary>
    public string HighscoresText(int limit = 5)
    {
        if (CurrentUser != "")
        {
            var board = _userDb.GetLeaderboard();
            if (board.Count == 0)
            {
                return "";
            }

            var lines = new List<string>();
            for (var i = 0; i < Mathf.Min(limit, board.Count); i++)
            {
                lines.Add(GdFormat("%d. %d", i + 1, (int)board[i].AsGodotDictionary()["score"].AsInt64()));
            }

            return string.Join("\n", lines);
        }

        if (Highscores.Count == 0)
        {
            return "";
        }

        var localLines = new List<string>();
        for (var i = 0; i < Mathf.Min(limit, Highscores.Count); i++)
        {
            localLines.Add(GdFormat("%d. %d", i + 1, (int)Highscores[i]["score"].AsInt64()));
        }

        return string.Join("\n", localLines);
    }

    /// <summary>GDScript 字符串 % 格式化语义（%s/%d/%f 占位 + %% 转义；tr() 文案补参用，
    /// C# 无 % 运算符；数组参数按序填占位）。</summary>
    private static string GdFormat(string format, params object[] args)
    {
        var sb = new System.Text.StringBuilder(format.Length + 16);
        var argIndex = 0;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '%' && i + 1 < format.Length)
            {
                var spec = format[i + 1];
                if (spec == '%')
                {
                    sb.Append('%');
                    i++;
                    continue;
                }

                if (spec is 's' or 'd' or 'f')
                {
                    sb.Append(argIndex < args.Length ? args[argIndex] : "?");
                    argIndex++;
                    i++;
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    // ---------------- 静态构造辅助（集合常量；局部构造，规则 19 不静态持有） ----------------

    private static Godot.Collections.Dictionary BuildDifficultyDefs() => new()
    {
        [new StringName("easy")] = new Godot.Collections.Dictionary
        {
            ["hp"] = 0.75,
            ["speed"] = 0.85,
            ["spawn"] = 1.25,
            ["score"] = 1,
            ["spread_cap"] = 1,
            ["milestone"] = 1.0,
            ["regen_delay"] = 3.0,
            ["regen_rate"] = 4.0,
        },
        [new StringName("medium")] = new Godot.Collections.Dictionary
        {
            ["hp"] = 1.0,
            ["speed"] = 1.0,
            ["spawn"] = 1.0,
            ["score"] = 2,
            ["spread_cap"] = 2,
            ["milestone"] = 1.0,
            ["regen_delay"] = 4.0,
            ["regen_rate"] = 2.0,
        },
        [new StringName("hard")] = new Godot.Collections.Dictionary
        {
            ["hp"] = 1.5,
            ["speed"] = 1.2,
            ["spawn"] = 0.8,
            ["score"] = 3,
            ["spread_cap"] = 3,
            ["milestone"] = 1.5,
            ["regen_delay"] = 5.0,
            ["regen_rate"] = 0.67,
        },
    };

    private static Godot.Collections.Array<Godot.Collections.Dictionary> BuildMissionDefs() => new()
    {
        new() { ["id"] = new StringName("kill_5"), ["goal"] = 5, ["kind"] = new StringName("kill") },
        new() { ["id"] = new StringName("survive_180"), ["goal"] = 180, ["kind"] = new StringName("survive") },
        new() { ["id"] = new StringName("boss_1"), ["goal"] = 1, ["kind"] = new StringName("boss") },
    };

    private static Godot.Collections.Array<Godot.Collections.Dictionary> BuildMissionPool() => new()
    {
        new() { ["id"] = new StringName("kill_5"), ["goal"] = 5, ["kind"] = new StringName("kill") },
        new() { ["id"] = new StringName("kill_15"), ["goal"] = 15, ["kind"] = new StringName("kill") },
        new() { ["id"] = new StringName("kill_30"), ["goal"] = 30, ["kind"] = new StringName("kill") },
        new() { ["id"] = new StringName("survive_60"), ["goal"] = 60, ["kind"] = new StringName("survive") },
        new() { ["id"] = new StringName("survive_180"), ["goal"] = 180, ["kind"] = new StringName("survive") },
        new() { ["id"] = new StringName("survive_300"), ["goal"] = 300, ["kind"] = new StringName("survive") },
        new() { ["id"] = new StringName("boss_1"), ["goal"] = 1, ["kind"] = new StringName("boss") },
        new() { ["id"] = new StringName("boss_2"), ["goal"] = 2, ["kind"] = new StringName("boss") },
        new() { ["id"] = new StringName("boss_3"), ["goal"] = 3, ["kind"] = new StringName("boss") },
    };

    private static Godot.Collections.Dictionary BuildRouteLines() => new()
    {
        [new StringName("offense")] = new Godot.Collections.Array { new StringName("spread_shot"), new StringName("laser_beam") },
        [new StringName("mobility")] = new Godot.Collections.Array { new StringName("phase_dash"), new StringName("mothership_recall") },
    };

    private static Godot.Collections.Array<int> BuildMilestoneBase() => new()
    {
        3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000,
    };

    // ---------------- 常量访问器（GDScript 不能读 C# 常量/静态字段；M7 过渡，删除前） ----------------
    // 集合常量访问器每次局部构造新集合（规则 19：静态字段禁持 Godot 对象）。
    // 注：同名 UPPER_SNAKE 实例属性已可直接供 GDScript 经实例零适配读取（GameState.MISSION_POOL 等），
    // 静态 GetXxx() 供主代理按需适配（load().GetXxx() / 测试直改）。

    public static int GetScoreCap() => ScoreCapValue;

    public static double GetMilestoneCycleMult() => MilestoneCycleMultValue;

    public static Godot.Collections.Array<StringName> GetDifficultyOrder() => new()
    {
        new StringName("easy"),
        new StringName("medium"),
        new StringName("hard"),
    };

    public static Godot.Collections.Array<int> GetMilestoneBase() => BuildMilestoneBase();

    public static int GetRpBossKill() => RpBossKillValue;

    public static int GetRpMissionReward() => RpMissionRewardValue;

    public static Godot.Collections.Array<Godot.Collections.Dictionary> GetMissionDefs() => BuildMissionDefs();

    public static Godot.Collections.Array<Godot.Collections.Dictionary> GetMissionPool() => BuildMissionPool();

    public static Godot.Collections.Dictionary GetRouteLines() => BuildRouteLines();

    public static int GetSfxPoolSize() => SfxPoolSizeValue;

    public static int GetPersistVersion() => PersistVersionValue;

    public static Godot.Collections.Dictionary GetXboxButtonLabels() => new()
    {
        [0] = "A",
        [1] = "B",
        [2] = "X",
        [3] = "Y",
        [4] = "LB",
        [5] = "RB",
        [6] = "LS",
        [7] = "RS",
    };

    public static Godot.Collections.Dictionary GetPsButtonLabels() => new()
    {
        [0] = "✕",
        [1] = "○",
        [2] = "□",
        [3] = "△",
        [4] = "L1",
        [5] = "R1",
        [6] = "L3",
        [7] = "R3",
    };

    public static Godot.Collections.Array<StringName> GetJoypadActions() => new()
    {
        new StringName("move_up"),
        new StringName("move_down"),
        new StringName("move_left"),
        new StringName("move_right"),
        new StringName("aim_left"),
        new StringName("aim_right"),
        new StringName("aim_up"),
        new StringName("aim_down"),
        new StringName("dash"),
        new StringName("boost"),
        new StringName("fine_move"),
        new StringName("dock"),
        new StringName("homecoming"),
        new StringName("give_up"),
        new StringName("buff_panel"),
        new StringName("restart"),
        new StringName("parry"),
    };

    public static Godot.Collections.Array<String> GetDifficultyDefKeys()
    {
        var keys = new Godot.Collections.Array<String>();
        foreach (var k in DifficultyDefKeys)
        {
            keys.Add(k);
        }

        return keys;
    }

    public static Godot.Collections.Dictionary GetViewZoomLevels() => new()
    {
        [new StringName("small")] = 1.0,
        [new StringName("medium")] = 1.35,
        [new StringName("large")] = 1.7,
    };

    public static Godot.Collections.Array<StringName> GetViewZoomOrder() => new()
    {
        new StringName("small"),
        new StringName("medium"),
        new StringName("large"),
    };

    public static Godot.Collections.Dictionary GetWindowSizeLevels() => new()
    {
        [new StringName("small")] = new Vector2I(1280, 720),
        [new StringName("medium")] = new Vector2I(1600, 900),
        [new StringName("large")] = new Vector2I(1920, 1080),
    };

    public static Godot.Collections.Array<StringName> GetWindowSizeOrder() => new()
    {
        new StringName("small"),
        new StringName("medium"),
        new StringName("large"),
    };

    public static Godot.Collections.Array<StringName> GetAimAssistOrder() => new()
    {
        new StringName("low"),
        new StringName("medium"),
        new StringName("high"),
    };

    public static Godot.Collections.Array<StringName> GetRebindableActions() => new()
    {
        new StringName("move_up"),
        new StringName("move_down"),
        new StringName("move_left"),
        new StringName("move_right"),
        new StringName("boost"),
        new StringName("fine_move"),
        new StringName("dash"),
        new StringName("dock"),
        new StringName("homecoming"),
        new StringName("give_up"),
        new StringName("buff_panel"),
        new StringName("parry"),
    };

    // ---------------- snake → PascalCase 桥属性（M7d 后保留：测试契约与历史调用名） ----------------


}
