using Godot;
using InfiAir.Core.Progression;

namespace InfiAir;

/// <summary>
/// GameState 部分定义（Y 系列拆分，2026-08-09）：对局状态字段与核心状态方法（ResetRun/AddScore 等）。
/// </summary>
public partial class GameState : Node
{

    /// <summary>生效的里程碑表（默认值见 BuildMilestoneBase()，可被 balance.json 覆盖）</summary>
    public Godot.Collections.Array<int> MilestoneBase { get; set; } = BuildMilestoneBase();

    public double MilestoneCycleMult { get; set; } = MilestoneCycleMultValue;

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
        // H03（健壮性审核）补全：milestones.cycle_mult 全局域校验——曲线语义要求阈值单调增长，
        // 下限须钳 ≥1.0：mult ∈ (0,1) 时阈值级数收敛（上确界 ≈ base_last/(1-mult)），Score 一旦
        // 越过收敛上界，AddScore/apply_run_save 的 while 里程碑推进永不退出（主线程挂死）——
        // 原 0.01 下限恰好放任收敛区间，防挂死目标未达成。difficulty 子表无 cycle_mult 键
        // （原 _valid_difficulty_defs 内检查恒真为死代码），此处对全局键钳制下限（同 world_scale 款）
        MilestoneCycleMult = Mathf.Max(Cfg("milestones.cycle_mult", MilestoneCycleMultValue).AsDouble(), 1.0);
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
        // 击杀连击参数缓存（热路径禁 cfg 约定；window ≤0 会每帧断连——钳制下限，
        // step ≤0 乘区不增、max_mult <1 会倒扣击杀分——钳制 ≥1）
        ComboWindow = Mathf.Max(Cfg("scoring.combo.window", ComboWindow).AsDouble(), 0.1);
        // AC1（2026-08-11 健壮性审查）：step/max_mult 上界钳 [0,1e3]/[1,1e3]——巨值乘区在
        // AddKillScore 的 (long) 乘算下溢出回绕为负（分数巨负进里程碑/榜单）；1e3 远超合理域
        // （设计封顶 ×2.0）但杜绝 long 溢出（AB15/AB16 上界钳先例）
        ComboStep = Mathf.Clamp(Cfg("scoring.combo.step", ComboStep).AsDouble(), 0.0, 1e3);
        ComboMaxMult = Mathf.Clamp(Cfg("scoring.combo.max_mult", ComboMaxMult).AsDouble(), 1.0, 1e3);
        _maxHpBase = Mathf.Max(Cfg("player.max_health", _maxHpBase).AsDouble(), 0.1); // H15 同款：≤0 使 max_health 归零/负值，玩家秒死
        // 2026-08-03 审计：与 _max_hp_base 钳制对称——负值使 extra_life 叠层反而降血上限（生存轴收紧意图相悖）
        _maxHpBonus = Mathf.Max(Cfg("buffs.extra_life.max_hp_bonus", _maxHpBonus).AsDouble(), 0.0);
        // 2026-08-03 审计：吸血比例缓存（击杀帧免 cfg 路径解析，P0-2 同款）
        _lifestealFraction = Mathf.Max(Cfg("buffs.lifesteal.max_hp_fraction", 0.1).AsDouble(), 0.0);
        // 基地任务轮换：刷新点数经济（≤0 钳制下限，防免费无限刷新）
        REFRESH_COST = Mathf.Max((int)Cfg("base_task.refresh_cost", REFRESH_COST).AsInt64(), 1);
        GRANT_PER_VISIT = Mathf.Max((int)Cfg("base_task.grant_per_visit", GRANT_PER_VISIT).AsInt64(), 0);
        // 局外成长：meta 节配置缓存（科技点结算 + 升级定义；键经 Cfg 静态调用被 BALANCE_MAP 收录）
        LoadMetaConfig();
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

    private int _nextMilestone = 3000; // = MilestoneBase[0]

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

    /// <summary>击杀连击（2026-08-11，docs/archive/2026-08-11-score-combo-buff-pity-plan.md）：
    /// 窗口内连杀放大击杀分——怒首领蜂/虫姬链式得分的温和版（贪分 vs 稳）。</summary>
    public double ComboWindow { get; private set; } = 3.0;

    public double ComboStep { get; private set; } = 0.1;

    public double ComboMaxMult { get; private set; } = 2.0;

    private double _comboTimer;

    public int Combo { get; private set; }

    /// <summary>回血链热路径缓存（P0-2）：max_health 基础值 _apply_balance 缓存；regen 档位难度变更时刷新。
    /// 默认值须与脚本默认 difficulty=medium 档一致（medium: regen_delay=4.0, regen_rate=2.0）。</summary>
    private double _maxHpBase = 100.0;

    private double _maxHpBonus = 50.0;

    private double _regenDelay = 4.0;

    private double _regenRate = 2.0;

    /// <summary>已计入难度乘数的时间档位（按 time_step_seconds 量化步进，避免连续漂移）</summary>
    private int _difficultyTimeStep;


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
        ResetCombo(); // 连击跨对局清零（幂等 + 广播 HUD）
        EmitSignal(SignalName.BuffsChanged);
    }

    public void AddScore(int points)
    {
        // 难度分数倍率统一在此乘算（easy ×1 / medium ×2 / hard ×3，配置表里的分值不变）
        // P4（2026-08-05）：得分总量钳制——手改配置 score 倍率极大时 int64 溢出（1e308 级）
        // 2026-08-10 健壮性审查：乘算提升 long 域——int × int 在 points×倍率超 2^31 时
        // 先回绕为负再进 Min（负分进入里程碑/榜单），long 域乘算后与上限钳制才生效
        Score = (int)Math.Min((long)Score + (long)points * ScoreMultiplier(), (long)ScoreCapValue);
        EmitSignal(SignalName.ScoreChanged, Score);
        // 2026-08-06 审计：里程碑推进改 while——与 apply_run_save 的全补口径一致（原单次 +1
        // 在单次加分跨多档时漏档：如 hard 倍率下高分击杀/Boss 奖励一次跨两档阈值），
        // 两路径行为统一（milestone_reached 按触发的档位逐档发，消费方按里程碑数计档）。
        // 2026-08-07：阈值求值迁移 C#（milestone_threshold 转发）；此处保持基于
        // _next_milestone 的 while——set_milestone_override 测试钩子允许阈值脱离曲线，
        // 批量推进（CountThresholdsUpTo）仅用于 apply_run_save 的存档恢复路径（低频、
        // 病态档数场景，批量收益大）；加分逐档仅 1-2 档，单值调用开销可忽略。
        // H03 兜底挂死守卫：与 MilestoneCurve.CountThresholdsUpTo 同款迭代上限——
        // cycle_mult 已钳 ≥1.0 后曲线单调，但 set_milestone_override 测试钩子允许阈值脱离
        // 曲线恒 ≤ Score（或阈值求值 int 溢出回绕为负），此时 while 永不退出，超限直接 break
        int iterations = 0;
        while (Score >= _nextMilestone)
        {
            _milestoneCount += 1;
            _nextMilestone = MilestoneThreshold(_milestoneCount);
            EmitSignal(SignalName.MilestoneReached, Score);
            iterations += 1;
            if (iterations >= MilestoneCurve.MaxIterations)
            {
                break;
            }
        }
    }

    // ---------------- 击杀连击（2026-08-11；scoring.combo 段） ----------------

    /// <summary>击杀计分唯一入口（敌机击杀路径统一走此）：连击推进 + 乘区放大，
    /// 随后经 AddScore 乘难度倍率。Boss 击杀（AddBossKill）/事件奖励/擦弹不计连击。</summary>
    public void AddKillScore(int basePoints)
    {
        Combo += 1;
        _comboTimer = ComboWindow;
        // long 域乘算防回绕（乘区 double，截断前钳制 int 域；AddScore 内另有总分钳制）
        // AC1 双保险（2026-08-11 健壮性审查）：乘积钳 [0, long.MaxValue]——组合极端路径
        // （basePoints×乘区越界 → double→long 转换未定义/回绕巨负）下兜底防负分入账（AB12 双保险先例）
        var scaled = (long)Math.Round(Math.Clamp(basePoints * ComboMultiplier(), 0.0, (double)long.MaxValue));
        AddScore((int)Math.Min(scaled, (long)int.MaxValue));
        EmitSignal(SignalName.ComboChanged, Combo);
    }

    /// <summary>连击乘区：min(1 + (combo−1)×step, max_mult)；combo 0/1 → 1.0（第 1 杀不放大）。</summary>
    public double ComboMultiplier()
    {
        if (Combo <= 1)
        {
            return 1.0;
        }

        return Math.Min(1.0 + (Combo - 1) * ComboStep, ComboMaxMult);
    }

    /// <summary>断连（受击/测试/重开）：连击归零 + 计时清空 + 广播 HUD。幂等。</summary>
    public void ResetCombo()
    {
        if (Combo == 0 && _comboTimer <= 0.0)
        {
            return;
        }

        Combo = 0;
        _comboTimer = 0.0;
        EmitSignal(SignalName.ComboChanged, 0);
    }

    /// <summary>连击窗口剩余时长（测试/诊断白盒读取；0 = 已断连）。</summary>
    public double ComboTimeLeft() => _comboTimer;
}
