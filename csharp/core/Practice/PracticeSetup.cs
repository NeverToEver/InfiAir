namespace InfiAir.Core.Practice;

/// <summary>
/// 练习模式的直选设置（口径见 DESIGN_BASELINE §1.16）：Boss 型别 / 起始难度档 / 遭遇事件三项。
///
/// 「选项索引 → 显示哪条文案键、启动哪个事件 id」的映射只写在这里，面板、练习宿主与探针共用一份——
/// 三处各写一份是最容易静默分叉的形态：改一项忘一处，表现是按钮上写着 A、启动的却是 B，
/// 而编译、单测、冒烟都不会有任何信号。
///
/// 构造即归一：索引来自 UI 环形循环与命令行两路，任一路越界都不该让「显示」与「启动」脱钩，
/// 故一律按选项数取模回正（不设非法态，也就不存在「越界后走哪条分支」的第二套语义）。
/// </summary>
public sealed record PracticeSetup
{
    /// <summary>Boss 选项数：无 + 4 型（显示复用 BOSS_TYPE_1..4）。</summary>
    public const int BossOptionCount = 5;

    /// <summary>难度选项数：易 / 中 / 难（显示复用 DIFF_EASY/MEDIUM/HARD）。</summary>
    public const int DifficultyOptionCount = 3;

    /// <summary>遭遇选项数：无 / 精英炮塔 / 轰炸编队 / 迷雾。</summary>
    public const int EncounterOptionCount = 4;

    /// <summary>迷雾选项固定取幽灵机群（fake_enemies，与 --fog-probe 同一条 id）：
    /// 它是无伤害无碰撞的纯视觉干扰，练习要练的是「迷雾来了怎么读屏」，不是「被随机事件打死」。</summary>
    public const string FogEncounterId = "fake_enemies";

    /// <summary>精英炮塔事件 id。</summary>
    public const string EliteTurretId = "elite_turret";

    /// <summary>轰炸编队事件 id。</summary>
    public const string FormationStrikeId = "formation_strike";

    /// <summary>难度档名（settings.json 的 difficulty 取值），索引 0/1/2 → easy/medium/hard。</summary>
    private static readonly string[] DifficultyNames = { "easy", "medium", "hard" };

    private static readonly string[] DifficultyLabelKeys = { "DIFF_EASY", "DIFF_MEDIUM", "DIFF_HARD" };

    /// <summary>遭遇选项的事件 id（空串 = 不指定遭遇）。</summary>
    private static readonly string[] EncounterIds = { "", EliteTurretId, FormationStrikeId, FogEncounterId };

    /// <summary>遭遇选项的文案键：前两项复用事件自己的标题键，迷雾复用其事件名。</summary>
    private static readonly string[] EncounterLabelKeys =
    {
        "PRACTICE_NONE",
        "ETV_TITLE",
        "FBQ_HUD_TITLE",
        "FOG_EVENT_FAKE_ENEMIES_NAME",
    };

    /// <summary>RingWrap 单步：索引加步长后按选项数回正（负数取模在 C# 得负值，故先加一轮）。</summary>
    private static int RingWrap(int index, int step, int count)
    {
        var wrapped = (index + step) % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    /// <summary>面板首次打开与无待定设置时的基线：中档难度、不指定 Boss、不指定遭遇。</summary>
    public static readonly PracticeSetup Default = new(0, 1, 0);

    public PracticeSetup(int bossType, int difficultyIndex, int encounterIndex)
    {
        BossType = RingWrap(bossType, 0, BossOptionCount);
        DifficultyIndex = RingWrap(difficultyIndex, 0, DifficultyOptionCount);
        EncounterIndex = RingWrap(encounterIndex, 0, EncounterOptionCount);
    }

    /// <summary>Boss 型别索引：0 = 无，1..4 = 对应 BOSS_TYPE_1..4。</summary>
    public int BossType { get; }

    /// <summary>难度索引：0 = 易、1 = 中、2 = 难。</summary>
    public int DifficultyIndex { get; }

    /// <summary>遭遇索引：0 = 无、1 = 精英炮塔、2 = 轰炸编队、3 = 迷雾。</summary>
    public int EncounterIndex { get; }

    /// <summary>是否指定了 Boss 型别。</summary>
    public bool HasBoss => BossType > 0;

    /// <summary>是否指定了遭遇事件。</summary>
    public bool HasEncounter => EncounterIndex > 0;

    /// <summary>所选遭遇是否属迷雾组（迷雾组的启动链与遭遇组不同：无分数门槛、走迷雾门控）。</summary>
    public bool EncounterIsFog => EncounterIndex == 3;

    /// <summary>Boss 行显示键。</summary>
    public string BossLabelKey => BossType == 0 ? "PRACTICE_NONE" : "BOSS_TYPE_" + BossType;

    /// <summary>难度档名（FrameCache 之外也要用的字符串形态）。</summary>
    public string DifficultyName => DifficultyNames[DifficultyIndex];

    /// <summary>难度行显示键。</summary>
    public string DifficultyLabelKey => DifficultyLabelKeys[DifficultyIndex];

    /// <summary>遭遇行显示键。</summary>
    public string EncounterLabelKey => EncounterLabelKeys[EncounterIndex];

    /// <summary>所选遭遇的事件 id（空串 = 不请求遭遇）。</summary>
    public string EncounterId => EncounterIds[EncounterIndex];

    /// <summary>环形切换三行之一（面板按键与手柄方向键共用；步长可负）。</summary>
    public PracticeSetup CycleBoss(int step) => new(RingWrap(BossType, step, BossOptionCount), DifficultyIndex, EncounterIndex);

    public PracticeSetup CycleDifficulty(int step) => new(BossType, RingWrap(DifficultyIndex, step, DifficultyOptionCount), EncounterIndex);

    public PracticeSetup CycleEncounter(int step) => new(BossType, DifficultyIndex, RingWrap(EncounterIndex, step, EncounterOptionCount));

    /// <summary>
    /// 练习局起始分数：把所选内容的生产分数门槛补足。
    ///
    /// 门槛本身在 core <see cref="InfiAir.Core.Events.EncounterTrigger"/> / Spawner 里真判，
    /// 补分是**让门槛被满足**而不是绕过它——练习局从 0 分开局、时长按分钟计，
    /// 不清门槛就等于「选了精英炮塔也永远等不到」（min_score 800）与「选了 Boss 也永远等不到」
    /// （第一只的分数门 boss_score_step 1500，时间门 80s 更是练不了）。
    /// 只补到门槛、不多给：补分仍低于第一档里程碑阈值（balance milestones.base[0] = 3000），
    /// 故不会顺带发出天赋点。
    /// </summary>
    public int ScoreSeed(int encounterMinScore, int bossScoreStep)
    {
        var need = 0;
        if (HasBoss)
        {
            need = System.Math.Max(need, bossScoreStep);
        }

        if (HasEncounter && !EncounterIsFog)
        {
            need = System.Math.Max(need, encounterMinScore);
        }

        return System.Math.Max(need, 0);
    }
}
