using Godot;
using InfiAir.Core.Storage;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// GameState 部分定义：本局存档（user://run.json 单一档案）。
///
/// **模型：存档 = 可继续的检查点**，生命周期如下（三条规则覆盖全部边界）：
/// - **写入/覆盖**：退出确认「保存并退出」+ 回基地（母舰坞修/返航）自动存（新档覆盖旧档）。
/// - **终结删档**：玩家死亡（PlayerDied）与「放弃重开」（RestartRun）——本局终结，检查点一并作废。
/// - **非破坏性**：标题屏选「新的一局」**不删旧档**（防一次误触抹掉进度）；旧档保留到被新档覆盖
///   或被本局终结清除。`ExitToTitle` 同样不删（Tutorial 也走此口，删档会误伤玩家存档；
///   且「回标题保留检查点」语义正确）。
///
/// 读档粒度：还原本局进度，战场从新一波开始（敌机/弹幕/波次计时/连击窗口/DDA 剩余不持久化）。
/// 复用 SaveManager/SaveStore（原子写 / 损坏隔离 / JSON）；与 settings.json 分区互不干扰。
/// 档案自带 version，版本不符按无存档处理（不隔离、仅忽略），保证开机不被旧档卡住。
/// 还原顺序固定：talent（先恢复 extra_life 层级）→ combat（重算 MaxHealth 才有正确上限）
/// → score → progress → missions；每个服务末尾自行补发信号驱动 HUD/Player 刷新。
/// </summary>
public partial class GameState : Node
{
    private const string RunPathValue = "user://run.json";
    private const int RunSaveVersion = 1;

    /// <summary>本次开局是否读取存档（标题屏「继续上次出击」置位；Main 开局分支消费后复位）。</summary>
    public bool PendingLoadRun { get; set; } = false;

    /// <summary>最近一次 LoadRun/HasRunSave 的核心层结果（Missing/Ok/Corrupt/Unreadable）。
    /// Main 据此区分「无档」与「暂时读不出」：后者不得当成无档覆盖旧进度。</summary>
    public SaveLoadStatus LastRunLoadStatus { get; private set; } = SaveLoadStatus.Missing;

    /// <summary>是否存在**可解析且含本局数据**的存档（可读 + version 匹配）。
    /// 标题屏据此显示「继续」提示——不能只判文件存在：损坏/版本不符/暂时读不出的档
    /// 会显示一个「按了却读不出来」的假承诺。损坏档在此被既有隔离逻辑移为 &lt;path&gt;.corrupt
    /// 并返回 false（自愈，无需人工清理）；墓碑档（空对象）解析成功但无 version，同样判否。</summary>
    public bool HasRunSave()
    {
        var result = LoadJsonCore(RunPathValue);
        LastRunLoadStatus = result.Status;
        if (result.Status != SaveLoadStatus.Ok || result.Tree is null || result.Tree.Count == 0)
        {
            return false;
        }

        return RunFieldNormalize.ReadInt(result.Tree, "version", 0) == RunSaveVersion;
    }

    /// <summary>删除本局存档（死亡即删档 / 开局选「新的一局」清旧档）。返回 true = 已确保读不回来。
    /// 删除失败（IO/权限）时不再静默吞掉：发可观测警告并兜底把正本覆写为墓碑内容（空 JSON 对象
    /// `{}`），使 HasRunSave 判否——保住「不可读档回滚」承诺；覆写也失败再警告一次。</summary>
    public bool DeleteRunSave()
    {
        if (DeleteJsonCore(RunPathValue, out var error))
        {
            return true;
        }

        GD.PushWarning($"InfiAir: 删除本局存档失败（{error}）——改写为墓碑内容以防读回旧进度");
        if (!_saveManager.Save(RunPathValue, new Godot.Collections.Dictionary()))
        {
            GD.PushWarning($"InfiAir: 墓碑写入也失败——{RunPathValue} 仍可能被读回（不可读档回滚承诺被破坏）");
            return false;
        }

        return true;
    }

    /// <summary>落盘本局进度（退出保存 / 回基地自动存）。IO 失败仅告警不抛（不影响退出流程）。</summary>
    public bool SaveRun()
    {
        var data = CollectRunDict();
        data["version"] = RunSaveVersion;
        return _saveManager.Save(RunPathValue, data);
    }

    /// <summary>读取并还原本局存档；无存档 / 版本不符 / 字段缺失一律返回 false（调用方回退全新一局）。
    /// 还原前先 ResetRun，保证未持久化的战场态（连击窗口/DDA 剩余/任务池游标等）从干净基线起。
    /// 结果状态留痕在 <see cref="LastRunLoadStatus"/>：Corrupt 已被隔离，Unreadable 不隔离也不丢档。</summary>
    public bool LoadRun()
    {
        var result = LoadJsonCore(RunPathValue);
        LastRunLoadStatus = result.Status;
        if (result.Status != SaveLoadStatus.Ok || result.Tree is null || result.Tree.Count == 0)
        {
            return false;
        }

        if (RunFieldNormalize.ReadInt(result.Tree, "version", 0) != RunSaveVersion)
        {
            return false;
        }

        ResetRun();
        ApplyRunDict(VariantBridge.ToVariant(result.Tree).AsGodotDictionary());
        return true;
    }

    /// <summary>收集本局可持久化状态（对齐 DESIGN_BASELINE §2.5 字段表）。</summary>
    private Godot.Collections.Dictionary CollectRunDict()
    {
        var talent = new Godot.Collections.Dictionary();
        foreach (var kv in _talent.LevelsSnapshot())
        {
            talent[kv.Key] = kv.Value;
        }

        var overcharged = new Godot.Collections.Array();
        foreach (var id in _talent.OverchargedSnapshot())
        {
            overcharged.Add(id);
        }

        var cacheValues = new Godot.Collections.Array();
        foreach (var v in _talent.CacheSnapshot())
        {
            cacheValues.Add(v);
        }

        var lastKind = new Godot.Collections.Dictionary();
        foreach (var kv in _missions.LastKindValueSnapshot())
        {
            lastKind[kv.Key] = kv.Value;
        }

        return new Godot.Collections.Dictionary
        {
            // score
            ["score"] = Score,
            ["kills"] = Kills,
            ["boss_kills"] = BossKills,
            ["combo"] = _score.Combo,
            ["milestone_count"] = _score.MilestoneCount(),
            // progress
            ["run_time"] = RunTime,
            ["difficulty_multiplier"] = DifficultyMultiplier,
            ["dda_timer"] = _runProg.DdaRemaining(),
            ["difficulty_time_step"] = _runProg.DifficultyTimeStep(),
            // combat
            ["health"] = Health,
            ["augments"] = Augments.Duplicate(),
            // talent
            ["talent_levels"] = talent,
            ["talent_overcharged"] = overcharged,
            ["talent_route"] = _talent.Route,
            ["talent_reset_tokens"] = _talent.ResetTokens,
            ["talent_bonus_overcharge_slots"] = _talent.BonusOverchargeSlots,
            ["talent_cache_values"] = cacheValues,
            // missions
            ["rp"] = Rp,
            ["refresh_points"] = RefreshPoints,
            ["missions"] = Missions.Duplicate(),
            ["last_kind_value"] = lastKind,
        };
    }

    /// <summary>还原本局状态。顺序固定：talent → combat → score → progress → missions。</summary>
    private void ApplyRunDict(Godot.Collections.Dictionary d)
    {
        // 1) talent 先还原（extra_life 层级决定 MaxHealth，必须先于 health 写入）
        _talent.RestoreRunState(
            ReadIntMap(d.GetValueOrDefault("talent_levels", new Godot.Collections.Dictionary())),
            ReadStringList(d.GetValueOrDefault("talent_overcharged", new Godot.Collections.Array())),
            d.GetValueOrDefault("talent_route", "").AsString(),
            SaveInt(d.GetValueOrDefault("talent_reset_tokens", 0), 0),
            SaveInt(d.GetValueOrDefault("talent_bonus_overcharge_slots", 0), 0),
            ReadDoubleList(d.GetValueOrDefault("talent_cache_values", new Godot.Collections.Array())));

        // 2) combat（talent 已就位，MaxHealth 正确；写入被消耗的盾层等 augments 运行态）
        // JSON 往返把 StringName 键退化为 String——重建成 StringName 键，否则 AugmentLevel(id)
        // 以 StringName 查字典会全部落空（增幅效果静默失效）。
        Augments = NormalizeAugments(d.GetValueOrDefault("augments", new Godot.Collections.Dictionary()));
        Health = Mathf.Clamp(SaveNum(d.GetValueOrDefault("health", MaxHealth()), MaxHealth()), 1.0, MaxHealth());

        // 3) score
        _score.RestoreRunState(
            SaveInt(d.GetValueOrDefault("score", 0), 0),
            SaveInt(d.GetValueOrDefault("kills", 0), 0),
            SaveInt(d.GetValueOrDefault("boss_kills", 0), 0),
            SaveInt(d.GetValueOrDefault("combo", 0), 0),
            SaveInt(d.GetValueOrDefault("milestone_count", 0), 0));

        // 4) progress
        RunTime = Math.Max(SaveNum(d.GetValueOrDefault("run_time", 0.0), 0.0), 0.0);
        _runProg.RestoreRunState(
            SaveNum(d.GetValueOrDefault("difficulty_multiplier", 1.0), 1.0),
            SaveInt(d.GetValueOrDefault("difficulty_time_step", 0), 0),
            SaveNum(d.GetValueOrDefault("dda_timer", 0.0), 0.0));

        // 5) missions
        _missions.RestoreRunState(
            SaveInt(d.GetValueOrDefault("rp", 0), 0),
            SaveInt(d.GetValueOrDefault("refresh_points", 0), 0),
            d.GetValueOrDefault("missions", new Godot.Collections.Dictionary()).AsGodotDictionary().Duplicate(),
            ReadIntMap(d.GetValueOrDefault("last_kind_value", new Godot.Collections.Dictionary())));
    }

    // ---------------- 存档字段判型读取（手改/损坏字段一律回退，不抛） ----------------
    // 判型与钳制口径单源在 RunFieldNormalize（core）：手改超大值（裸 (int) 转换会回绕成负数）
    // 与旧档缺键的回退语义由 core 单测钉住，本层只做 Variant → CLR 载入与 StringName 键重建。

    /// <summary>增幅表键归一化（StringName 键）：JSON 往返把键退化为 String，逐项重建为 StringName；
    /// 层级判定（只保留正层级 + 钳 [0, int.MaxValue]）委托 <see cref="RunFieldNormalize.NormalizeAugments"/>。
    /// 不重建 StringName 的话 AugmentLevel 以 StringName 查字典会全部落空（增幅效果静默失效）。</summary>
    private static Godot.Collections.Dictionary NormalizeAugments(Variant v)
    {
        var result = new Godot.Collections.Dictionary();
        if (!VariantBridge.TryToClr(v, out var clr, out _))
        {
            return result;
        }

        foreach (var kv in RunFieldNormalize.NormalizeAugments(clr))
        {
            result[new StringName(kv.Key)] = kv.Value;
        }

        return result;
    }

    /// <summary>int 子表读取（talent_levels / last_kind_value）：逐项判型 + 钳 [0, int.MaxValue]，
    /// 非数值整条跳过；判型口径单源在 <see cref="RunFieldNormalize.ReadIntMap"/>。</summary>
    private static Dictionary<string, int> ReadIntMap(Variant v)
        => VariantBridge.TryToClr(v, out var clr, out _)
            ? RunFieldNormalize.ReadIntMap(clr)
            : new Dictionary<string, int>();

    private static List<double> ReadDoubleList(Variant v)
    {
        var result = new List<double>();
        if (v.VariantType != Variant.Type.Array)
        {
            return result;
        }

        foreach (var item in v.AsGodotArray())
        {
            if (item.VariantType is Variant.Type.Int or Variant.Type.Float)
            {
                result.Add(item.AsDouble());
            }
        }

        return result;
    }

    private static List<string> ReadStringList(Variant v)
    {
        var result = new List<string>();
        if (v.VariantType != Variant.Type.Array)
        {
            return result;
        }

        foreach (var item in v.AsGodotArray())
        {
            if (item.VariantType == Variant.Type.String)
            {
                result.Add(item.AsString());
            }
        }

        return result;
    }
}
