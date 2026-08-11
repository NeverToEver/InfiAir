# InfiAir AB 系列修复指引（2026-08-11）

> 来源：第八轮全量对抗性复审（2026-08-11，登记 `docs/AUDIT_VAULT.md` AB 系列，23 项发现：P1×1 / P2×15 / P3×7）。
> 本文件为修复执行指引；修复按 SOP Phase 3-4 分批提交（docs 与代码分 commit），每项修复后立即定向验证，修复完成后回填 AUDIT_VAULT「修复起效记录」。
> 基线：`dotnet build` 0w/0e、xUnit 111/111、全量断言场景 56/56（权威计数 `docs/TESTING.md`）。

## 修复总原则

1. **钳制族/判型族逐键清单化**——本轮约 10/23 项为既有修复的孪生遗漏（每轮只封一侧入口）。修复时把同一文件的同族键一次性扫齐，避免再产生「修复的修复」。
2. 数值语义从函数定义推断，不按注释猜测；配置损坏/手改存档一律回退脚本默认（`.agents/balance-config.md` 纪律），不抛不崩。
3. 每项修复后跑最小验证集；C# 改动 + `dotnet build`（零警告）+ `dotnet test tests-csharp/`；配置改动 + `balance_test`；批次收尾跑全量断言场景 + `--quit-after 300` smoke。
4. 修复顺序：批次 1（P1，正常游玩可达）→ 批次 2（数据安全）→ 批次 3（钳制/判型族）→ 批次 4（P3/文档）。

---

## 批次 1 — P1（正常游玩路径可达，优先）

### AB1 `LaserWeapon.cs` 激光 active 期间返航 → 自动开火永久禁用

- **位置**：`LaserWeapon.cs:228-238`（EndBeam）/ `Player.cs:932-954`（PlayEntryAnimation）/ `Player.cs:1021-1027`（FinishEntry）/ `Main.cs:1019-1023`（继续出击恢复顺序）
- **问题**：光束 active 中按 B 返航 → 树暂停冻结激光 active 态 → 恢复时 `PlayEntryAnimation` 捕获 `_entryPrevAutoFire = false`（激光禁用中）→ 物理帧恢复后激光 `_activeTime` 耗尽，`EndBeam()` 恢复 `SetAutoFire(true)` → 入场结束 `FinishEntry()` 又无条件覆盖回 `false` → 此后 StartBeam 捕获 false、EndBeam 恢复 false，自维持哑火至本局结束（激光 buff 无移除机制，无恢复路径）。
- **修复方案（最小侵入，Player 侧）**：在 `Player` 增加公开方法，供 LaserWeapon 在入场序列期间同步捕获值：
  1. `Player.cs` 新增：
     ```csharp
     /// <summary>AB1：入场序列期间外部系统（LaserWeapon.EndBeam）恢复 autofire 时同步覆盖捕获值，
     /// 防 FinishEntry 把激光恢复的 true 踩回 false（返航暂停冻结激光 active 的孪生路径）。</summary>
     public void OverrideEntryAutoFire(bool value)
     {
         if (_entryPhase != 0)
         {
             _entryPrevAutoFire = value;
         }
     }
     ```
  2. `LaserWeapon.EndBeam()`（`:228-238`）恢复前补：
     ```csharp
     _player.SetAutoFire(_savedAutofire);
     _player.OverrideEntryAutoFire(_savedAutofire);
     ```
- **验证**：`dotnet build` 0 警告；定向：新断言场景或手测（光束 active 中按 B 返航 → 继续出击 → 入场结束后自动开火恢复）；回归 `smoke_test` + 全量断言场景。若加断言场景，登记 `docs/TESTING.md` 计数。

---

## 批次 2 — 数据安全（P2）

### AB11 `UserDb.cs` DeleteUser 回滚不覆盖存档文件删除副作用

- **位置**：`csharp/core/Storage/UserDb.cs:324-334`
- **问题**：存档文件与 `.corrupt` 备份在 users.json 提交**之前**删除；`Save()` 失败时仅内存条目回滚（返回 false），但该用户对局存档已永久销毁。回归测试 `UserDbTests.cs:176-197` 只断言内存回滚，未断言 saveFile 幸存（盲区）。
- **修复方案**：调整顺序——先 `Save()` 成功，再删文件；失败则文件不删（仅残留孤儿存档文件，安全）：
  ```csharp
  var removed = Users[name];
  Users.Remove(name);
  if (!Save())
  {
      Users[name] = removed;
      return false;
  }
  _store.Delete(saveFilePath);
  _store.Delete(saveFilePath + ".corrupt");
  return true;
  ```
  同步更新 `:330-331` 注释口径（「对称 CreateUser 回滚」→「事务轴：先提交 users.json 再删文件，落盘失败不产生文件副作用」）。
- **验证**：`UserDbTests.cs` 补断言——`DeleteUser_SaveFails_RollsBackInMemoryEntry` 用例增加 `File.Exists(saveFile)` 幸存断言；`dotnet test` 全绿；`dotnet format` 零 diff。

### AB12 `ProgressionCurves.cs` × `GameState.Save.cs` 存档 elapsed 无上界 → 难度乘数巨负

- **位置**：`GameState.Save.cs:146`（入口）/ `ProgressionCurves.cs:122-123`（计算）
- **问题**：`SaveNum` 仅判型无上界钳；手改 `elapsed: 1e300` → `(long)Math.Floor(1e300/600)` 未定义转换（实践得 `long.MinValue`）→ 难度乘数 ≈ -1.4e19 → 敌 HP/伤害/速度 ramp 全反向、刷怪风暴；同时击穿「损坏回退默认」与「难度单调不减」双防线（ScoreMultiplier/里程碑等整数字段均有钳，float 字段是遗漏入口）。
- **修复方案**：入口钳制 + 曲线层防御（双保险）：
  1. `GameState.Save.cs:146`：
     ```csharp
     // AB12：elapsed 钳 [0, 1e6]（≈11.6 天，远超合理对局时长）——SaveNum 仅判型无上界，
     // 手改超大值经 (long) 未定义转换得 long.MinValue 使难度乘数巨负，击穿单调不减防线
     RunTime = Math.Clamp(SaveNum(data.GetValueOrDefault("elapsed", 0.0), 0.0), 0.0, 1e6);
     ```
  2. `ProgressionCurves.cs:122` 防御（防未来其他入口绕过）：
     ```csharp
     if (runTime <= 0.0) return 1.0 + perBossKill * bossKills; // 0/负值钳制（既有口径）
     if (runTime > 1e6) runTime = 1e6; // AB12：巨值防御——(long) 转换溢出为 long.MinValue
     long step = (long)Math.Floor(runTime / timeStepSeconds);
     ```
- **验证**：xUnit 补用例——`DifficultyCurve.Compute(1e300, …)` 与 `Compute(-1, …)` 不溢出、返回值有界（`tests-csharp/ProgressionCurvesTests.cs`）；存档手改 1e300 经 ApplyRunSave 后 `RunTime ≤ 1e6`。

### AB13 `ExitConfirm.cs` × `PauseUi.cs` 退出确认淡出窗口未屏蔽 R 键

- **位置**：`ExitConfirm.cs:115-124`（OnOkPressed）/ `PauseUi.cs:200-205`（restart 分支）
- **问题**：确认退出后 `_exiting=true` 仅挡 Ok/Cancel；`PauseUi._UnhandledInput` 在 ExitConfirm 可见时仍响应 R → `ReloadCurrentScene` 杀淡出 tween → `Quit()` 永不执行，但 battle 模式 `DeleteSave()` 已执行（档删、未退出、静默重开新局）。
- **修复方案**：ExitConfirm 暴露状态，PauseUi restart 分支加模态守卫：
  1. `ExitConfirm.cs` 新增：
     ```csharp
     /// <summary>AB13：退出确认已受理（_exiting），调用方（PauseUi）须屏蔽冲突快捷键。</summary>
     public bool Exiting() => _exiting;
     ```
  2. `PauseUi.cs:200` 处：
     ```csharp
     var exitConfirm = GetParent().GetNodeOrNull("ExitConfirm") as ExitConfirm;
     if (exitConfirm != null && exitConfirm.Exiting())
     {
         return; // AB13：确认退出淡出窗口内忽略 R（防删档后 ReloadCurrentScene 杀 tween 使 Quit 不执行）
     }
     ```
- **验证**：退出流程相关断言场景（如有 `exit_confirm` 类测试补「确认后按 R 不重开」用例）；`smoke_test` 回归。

---

## 批次 3 — 钳制/判型族收口（P2）

### AB2 `BuffSelect.cs` dynamic_weight 子键判型缺口

- **位置**：`BuffSelect.cs:161-168`（`SelectCandidates`）
- **问题**：`buffs.dynamic_weight` 四子键裸读无判型：① `"enabled": "false"` 字符串按 GDScript 语义误读为**开**（C16 存档侧同款陷阱）；② 坏 `hp_ratio` 解析得 0.0 → 保底+加权静默失效；③ `ids` 缺键回退空数组而非设计 5 项（extra_life/regen/armor/shield/evasion），保底静默失效——与同函数硬编码默认值口径自相矛盾。
- **修复方案**（C16/SaveBool 判型口径）：
  ```csharp
  var dw = GameState.Instance.Cfg("buffs.dynamic_weight", new Godot.Collections.Dictionary()).AsGodotDictionary();
  // AB2：条目级判型（C16 存档口径移植）——字符串 "false" 按 GDScript bool() 语义为 true，坏值一律回退设计默认
  var enabledV = dw.GetValueOrDefault("enabled", true);
  var enabled = enabledV.VariantType == Variant.Type.Bool ? enabledV.AsBool() : true;
  var hpRatioV = dw.GetValueOrDefault("hp_ratio", 0.5);
  var hpRatio = hpRatioV.VariantType is Variant.Type.Int or Variant.Type.Float
      ? Mathf.Clamp(hpRatioV.AsDouble(), 0.0, 1.0) : 0.5;
  var weightV = dw.GetValueOrDefault("weight", 2.0);
  var weight = weightV.VariantType is Variant.Type.Int or Variant.Type.Float
      ? Mathf.Max(weightV.AsDouble(), 1.0) : 2.0;
  var defIds = new Godot.Collections.Array<StringName>();
  var idsV = dw.GetValueOrDefault("ids", new Godot.Collections.Array());
  if (idsV.VariantType == Variant.Type.Array)
  {
      foreach (var v in idsV.AsGodotArray())
      {
          if (v.VariantType == Variant.Type.String)
          {
              defIds.Add(v.AsStringName());
          }
      }
  }
  if (defIds.Count == 0)
  {
      // 缺键/坏值回退设计默认（与 data/balance.json buffs.dynamic_weight.ids 一致）
      defIds.Add("extra_life"); defIds.Add("regen"); defIds.Add("armor"); defIds.Add("shield"); defIds.Add("evasion");
  }
  ```
- **验证**：`buff_select_test` 补用例（dynamic_weight 子键手改坏值/缺 ids → 行为回退默认）；`dotnet build` 0 警告。

### AB3 `Boss.cs` 模式表 interval 无下限钳制

- **位置**：`Boss.cs:965`（`_fireTimer` 重装）、`:1024`（`StartPatternInternal` 首装）
- **问题**：`pattern.interval` ≤0 → `_fireTimer` 重装 ≤0 → 每物理帧攻击一次（波次模式 1 帧烧 1 波、时长模式连射至弹上限）；R06 只封了 enrage interval 族。
- **修复方案**：两处统一钳（R06 口径 0.05）：
  ```csharp
  _fireTimer = Mathf.Max((float)pattern.GetValueOrDefault("interval", BaseFireInterval()).AsDouble(), 0.05f);
  ```
  （`Boss.cs:1024` 同款。）如 `LoadPatterns`（:1109-1121）处有统一入口，优先在装入时清洗一遍，运行期两处保留兜底。
- **验证**：Boss 族断言场景（boss_pattern/boss_phase/boss_enrage）定向；当前数据（interval 均 >0）行为零变化。

### AB4 `Boss.cs` E2PointCount 无下限

- **位置**：`Boss.cs:534`（enrage type_2 point_count 读值）
- **问题**：配 0 → `_attackIndex < E2PointCount` 恒假 → 二型狂暴 ACTIVE 冻结 6s（同族 count 键 E1RingCount/E3RingCount 等均有 floor，独漏此项）。
- **修复方案**（同族口径）：
  ```csharp
  E2PointCount = Mathf.Max((int)GameState.Instance.Cfg("boss.enrage.type_2.point_count", E2PointCount).AsInt64(), 4);
  ```
- **验证**：`boss_enrage_test` 定向；当前默认 6 行为零变化。

### AB5 `Spawner.cs` elite_wave_size 无下限

- **位置**：`Spawner.cs:168`
- **问题**：0/负 → 精英波循环不执行、特殊槽静默吞掉（:257 `WaveSizeInternal` 有 `Mathf.Max(1, …)`，同族遗漏）。
- **修复方案**：
  ```csharp
  ELITE_WAVE_SIZE = Mathf.Max((int)GameState.Instance.Cfg("spawner.elite_wave_size", ELITE_WAVE_SIZE).AsDouble(), 1);
  ```
- **验证**：当前默认 1 行为零变化；全量回归。

### AB6 `Spawner.cs` boss_time_limit / boss_min_interval 无下限

- **位置**：`Spawner.cs:143-144`（:142 BOSS_SCORE_STEP 已钳，同批遗漏）
- **问题**：`boss_time_limit ≤ 0` → `_bossTimer ≥ 0 ≥ limit` 恒真 → Boss 无限连出；`boss_min_interval < 0` 使分数门恒真。
- **修复方案**：
  ```csharp
  // AB6：时间兜底 ≤0 时 Boss 无限连出（_bossTimer≥0 恒满足）；分数门负值恒真——钳下限（L06 口径）
  BOSS_MIN_INTERVAL = Mathf.Max((float)GameState.Instance.Cfg("spawner.boss_min_interval", BOSS_MIN_INTERVAL).AsDouble(), 0.0f);
  BOSS_TIME_LIMIT = Mathf.Max((float)GameState.Instance.Cfg("spawner.boss_time_limit", BOSS_TIME_LIMIT).AsDouble(), 5.0f);
  ```
  （`BOSS_MIN_INTERVAL` 钳 0 即可防负值；`BOSS_TIME_LIMIT` 钳 5s 保时间兜底至少一轮可控间隔。）
- **验证**：当前默认 120/80 行为零变化；全量回归。

### AB7 `Mothership.cs` mag_cell_time 无下限

- **位置**：`Mothership.cs:238`（:237 `MagCells` 同批已钳 ≥1，孪生遗漏）
- **问题**：≤0 → `_magCellTimer ≥ MagCellTime` 恒真 → 每帧耗 1 格、STAY 驻留（设计 20s）瞬结、警告/提前离舰路径失效。
- **修复方案**：
  ```csharp
  MagCellTime = Mathf.Max((float)GameState.Instance.Cfg("mothership.mag_cell_time", MagCellTime).AsDouble(), 0.05f);
  ```
- **验证**：当前默认 2 行为零变化；`mothership_summon` 等母舰场景定向。

### AB8 `Spawner.cs` MergeType 数值域未校验

- **位置**：`Spawner.cs:241-250`
- **问题**：`fire_interval/score/scale/radius` 仅 `IsNumber` 判型、无数值域校验：`fire_interval:0` → 机枪化、`score:-100` → 击杀倒扣分、`radius:0` → 碰撞半径 0 子弹直穿（JSON 是正式配置入口，非手改存档）。
- **修复方案**：判型后按键钳值域（对齐 L05/L06 域校验族；坏值回退脚本默认而非覆写）：
  ```csharp
  foreach (var k in new[] { "score", "fire", "fire_interval", "scale", "radius" })
  {
      var v = src.GetValueOrDefault(k, new Variant());
      if (!IsNumber(v)) continue;
      switch (k)
      {
          case "score": dst[k] = Math.Max(v.AsInt64(), 0L); break;              // AB8：负分倒扣
          case "fire_interval": dst[k] = Math.Max(v.AsDouble(), 0.05); break;  // AB8：≤0 机枪化
          case "scale": dst[k] = Math.Max(v.AsDouble(), 0.1); break;           // AB8：0 缩放不可见
          case "radius": dst[k] = Math.Max(v.AsDouble(), 0.5); break;          // AB8：0 半径子弹直穿
          default: dst[k] = v; break;                                           // "fire" 布尔原样
      }
  }
  ```
- **验证**：当前数据行为零变化；敌机相关断言场景定向 + 全量回归。

### AB9 `GameEventManager.cs` fog weights/durations 条目值判型缺口

- **位置**：`GameEventManager.cs:670,681,712`（`FOG_WEIGHTS`/`FOG_EVENT_DURATIONS` 读值）
- **问题**：容器已判 Dictionary，条目值未判型——坏值（字符串/数组）`AsDouble()` 抛 InvalidCastException，迷雾触发路径每 `check_interval` 崩溃（2026-08-10 批次只修了 FakeEnemiesEvent/遭遇事件难度键，孪生遗漏）。
- **修复方案**（`FakeEnemiesEvent.cs:31-32` 同款模式）：抽一个条目读值助手：
  ```csharp
  // AB9：条目级判型（FakeEnemiesEvent 同族口径）——坏值回退默认，不抛
  private static double FogNum(Godot.Collections.Dictionary dict, StringName key, double def)
  {
      var v = dict.GetValueOrDefault(key, def);
      return v.VariantType is Variant.Type.Int or Variant.Type.Float ? v.AsDouble() : def;
  }
  ```
  三处调用改为 `FogNum(FOG_WEIGHTS, id, 1.0)` / `FogNum(FOG_EVENT_DURATIONS, pId, 6.0)`。
- **验证**：事件族断言场景定向（含 fog 触发路径）；`autoplay` 短程探针。

### AB10 `GameEventManager.cs` 场景重入残留遭遇活跃态

- **位置**：`GameEventManager.cs:193-197`（`SetRunActive(false)` 只 `EndFog()`）
- **问题**：死亡/切场景不中断进行中的遭遇 → `_encounterActiveId`/`_encounterEndPending` 残留 → 重进 main 后对从未 start 的新实例广播幽灵 `EventEnded`（打破 Q13「恒只发一次」），且首帧 `ForceTrigger` 被 `:373` 拒绝（消费方仅测试，危害低）。
- **修复方案**（Q10/Q12 同族：`set_run_active` 重置）：
  ```csharp
  public void SetRunActive(bool active)
  {
      _runActive = active;
      if (!active)
      {
          EndFog();
          // AB10：遭遇活跃态一并复位（防场景重入残留 → 幽灵 EventEnded + 首帧 ForceTrigger 拒触发）
          _encounterActiveId = EmptyId;
          _encounterEndPending = false;
          return;
      }
      ...
  }
  ```
  确认 `_encounterEndPending` 字段名与消费方语义后落地。
- **验证**：遭遇族断言场景 + autoplay（死亡→重开路径）；`encounter` 信号计数用例（如存在）。

### AB14 `GameState.Save.cs` 继续对局不广播 TouchControlsChanged

- **位置**：`GameState.Save.cs:218`（ApplyRunSave 恢复 touch_controls 处）/ `Main.cs:163`（_Ready 先 SetEnabled 后 ApplyRunSave）
- **问题**：存档恢复触屏开关只写内存字段，不广播 → VirtualControls 启用态与设置脱钩（设置页显示与实机相反且无感知途径；Ctrl/Shift 直读字段不受影响，唯独触屏有状态缓存消费方）。
- **修复方案**：ApplyRunSave 恢复处补发射（与 `:134` `BuffsChanged` 同款）：
  ```csharp
  TouchControls = SaveBool(data.GetValueOrDefault("touch_controls", TouchControls), TouchControls);
  EmitSignal(SignalName.TouchControlsChanged, TouchControls); // AB14：恢复值回流 VirtualControls
  ```
  （确认订阅方 VirtualControls 的 `OnTouchControlsChanged` 连接存在且幂等——`Main.cs` 已连；另确认 SaveBool 已存在于当前行。）
- **验证**：`virtual_controls_test` 补「存档恢复触屏开关」用例；`dotnet build` 0 警告。

### AB15 `GameState.Difficulty.cs` SpreadEnemyCap 无上界

- **位置**：`GameState.Difficulty.cs:120`
- **问题**：裸 `(int)…AsInt64()` 无上界——手改 >2^31 回绕负 → spread 弹幕敌机同屏上限恒负 → 整类玩法消失（同文件 :44-45 ScoreMultiplier 已钳，孪生遗漏）。
- **修复方案**：
  ```csharp
  public int SpreadEnemyCap() => (int)Math.Clamp(
      DIFFICULTY_DEFS[Difficulty].AsGodotDictionary()["spread_cap"].AsInt64(), 0L, (long)int.MaxValue);
  ```
- **验证**：当前数据行为零变化；全量回归。

### AB16 `Starfield.cs` far_count/near_count 无上界 → OOM

- **位置**：`Starfield.cs:50-53,79-93`
- **问题**：只判型+非负，无上界——手改 1e9 → `new Vector2[1e9]` + `[2e9]` ≈ 24GB 数组，启动即 OOM；>2^31 还会 `(int)` 回绕负。
- **修复方案**：
  ```csharp
  // AB16：count 钳 [0, 4096]（默认 140/90；上界防手改巨值 OOM 与 (int) 回绕）
  const long MaxCount = 4096;
  if (fc.VariantType == Variant.Type.Int && fc.AsInt64() >= 0)
  {
      _farCount = (int)Math.Min(fc.AsInt64(), MaxCount);
  }
  ```
  `near_count` 同款。
- **验证**：启动 smoke（starfield 每场景运行）；当前默认行为零变化。

### AB17 `GameState.Save.cs` 登录用户榜单显示裸 (int) 截断

- **位置**：`GameState.Save.cs:570`（user_db 分支；本地榜 :584 经加载期 SaveInt 钳制，此分支是孪生遗漏）
- **问题**：core `UserDb.GetLeaderboard` 只判型不钳上界——手改 users.json 榜单 score >2^31 → 结算页显示负数名次分。
- **修复方案**（显示处钳制，最小改动；或 core 层归一化）：
  ```csharp
  // 显示处：
  lines.Add(GdFormat.Format("%d. %d", i + 1,
      (int)Math.Clamp(board[i].AsGodotDictionary()["score"].AsInt64(), 0L, (long)int.MaxValue)));
  ```
  建议同时 core 层 `UserDb.GetLeaderboard`（:404 判型处）补 `Math.Clamp(…, 0, long.MaxValue)` 归一化读取值。
- **验证**：xUnit 补用例（手改超大 score 经 GetLeaderboard/显示路径不出现负数）；`user_db` interop 场景回归。

---

## 批次 4 — P3（文档矛盾/质量）

### AB18 `Boss.cs` P2/ENRAGE 血量比例不校验顺序

- **位置**：`Boss.cs:397-399`（各自 `Mathf.Clamp(…, 0.01f, 0.99f)`，无关系断言）
- **问题**：倒挂配置（phase2=0.2, enrage=0.3）→ P2 段被整体跳过、Boss 以 P1 强度直接狂暴，违 BOSS_REDESIGN §4.1（70%→30% 顺序）且无告警。
- **修复方案**：读入后修正关系（保序）：
  ```csharp
  // AB18：保序修正——P2 段必须高于 ENRAGE 线，倒挂时抬 P2 至 enrage+0.01（防 P2 段整体跳过）
  Phase2HpRatio = Mathf.Clamp(Phase2HpRatio, 0.01f, 0.99f);
  EnrageHpRatio = Mathf.Clamp(EnrageHpRatio, 0.01f, 0.99f);
  if (Phase2HpRatio <= EnrageHpRatio)
  {
      Phase2HpRatio = Mathf.Min(EnrageHpRatio + 0.01f, 0.98f);
  }
  ```
- **验证**：`boss_phase_test` 补「倒挂配置走默认顺序」用例；当前默认（0.7/0.3）行为零变化。

### AB19 `Enemy.cs` 离场方向注释失实

- **位置**：`Enemy.cs:822-825`
- **问题**：注释「就近侧方…从较近的一侧离场」，代码 `Position.X < center ? 1.0f : -1.0f` 实际向**较远**一侧离场（行为与旧版 GDScript 逐位一致，属迁移保留的失实注释）。
- **修复方案**：只改注释，不动行为：
  ```csharp
  // 侧向离场（AB19：注释订正——向镜像侧离场：左半区向右、右半区向左，与旧版一致）
  ```
- **验证**：无（注释）。

### AB20 同帧 Boss/遭遇竞态优先级反转（文档-代码矛盾）

- **位置**：`GameEventManager.cs:514`（autoload `_Process` 帧序在前）× `Spawner.cs:558-569` × `docs/ELITE_TURRET_EVENT.md §6.3`
- **问题**：文档承诺「same-frame race → boss wins」，实现中事件先启动（`SetBossFrozen(true)`）→ Boss 推迟到事件结束后 4s；Boss 仍保证触发、不累积，影响仅单帧优先次序。
- **修复方案**：推荐**文档同步为实际行为**（行为改动需跨 autoload 顺序依赖，风险高收益微）：
  - `docs/ELITE_TURRET_EVENT.md §6.3` 改为：「same-frame race → encounter may win (autoload _Process runs before main scene Spawner)；boss trigger is deferred until event end + boss_resume_delay, never lost」
  - 代码注释同步（`GameEventManager.cs:514` 附近补 AB20 注记）。
- **验证**：文档；无行为变更。

### AB21 `BaseConsole.cs` / `SettingsUi.cs` 孤儿文档注释

- **位置**：`BaseConsole.cs:695`、`SettingsUi.cs:776`（类尾悬挂 `/// <summary>`，M5 迁移 GdFormat 后残留）
- **修复方案**：删除两处悬挂注释（格式化实现已迁 `InfiAir.Core.Text.GdFormat`）。
- **验证**：无（注释）；`dotnet build` 0 警告。

### AB22 `Hud.cs` × `SegmentedBar.cs` boss_bar_segments 配置恒无效果

- **位置**：`Hud.cs:151,834`（写 `Segments`）× `SegmentedBar.cs:161-226`（加权分支只迭代 `SegWeights.Count` 固定 3 段）
- **问题**：`hud.boss_bar_segments`（默认 3）改 5/7 无任何视觉变化，配置名实不符。
- **修复方案**（二选一，推荐 1）：
  1. **删除配置键**：`Hud.cs:151` 移除读值（段数恒由权重数组决定），`data/balance.json` 删 `hud.boss_bar_segments`，重跑 `gen_balance_map.py` 同步 BALANCE_MAP（零 diff 闸）。
  2. 保留键并实现：`DrawWeighted` 段数不足时按 `Segments` 重采样/均分权重（改动面大，收益低，不推荐）。
- **验证**：推荐方案 1——BALANCE_MAP 重跑幂等 + `hud` 相关场景；若加测试，`SegmentedBar` 用例断言段数=权重数。

### AB23 `SettingsUi.cs` 键盘调整摇杆滑杆不持久化

- **位置**：`SettingsUi.cs:536`（`PersistJoySettings` 唯一触发点 = `DragEnded`）
- **问题**：键盘焦点链方向键调整只走 `ValueChanged` → 仅写内存（`JoyAimSpeed/JoyDeadzone` 注释明示不自动写盘）；正常退出路径兜底（SaveProfile），仅进程异常终止丢失调整值。
- **修复方案**：ValueChanged 补持久化（滑杆调整频率低，写盘成本可接受）：
  ```csharp
  slider.ValueChanged += _ => GameState.Instance.PersistJoySettings(); // AB23：键盘调整同样落盘（原仅 DragEnded）
  ```
  （与既有 `DragEnded` 并存；如需防频繁写盘可加 0.5s 防抖 Timer——设置写盘为小 JSON，直接写即可。）
- **验证**：`settings` 相关场景（如有）或手测：键盘调整 → 重进设置页值保持。

---

## 最终验证清单（批次全部完成后）

1. `dotnet build`（三工程）0 警告 0 错误；`dotnet format` 三工程零 diff。
2. `dotnet test tests-csharp/` 全绿（含本指引新增用例：AB11 文件幸存、AB12 巨值钳制、AB17 榜单钳制）。
3. `godot --headless --import` 0 引擎错误；main smoke 300 帧 0 错误。
4. 全量断言场景 0 FAIL（权威计数 `docs/TESTING.md`；若新增场景同步计数）+ 日志引擎错误扫描零命中（含 `Unhandled exception`）。
5. BALANCE_MAP 重跑零 diff（AB22 若删键：先删键再重跑提交）。
6. 变更配置/存档的批次确认「当前数据行为零变化」（默认值均正常，见各 AB 验证注）。
7. 回填 `docs/AUDIT_VAULT.md` AB 系列各条目「修复起效记录」（改了什么 / 为什么起效 / 如何验证）；按 SOP Phase 3 分批 commit（docs 与代码分 commit，commit message 标注 AB 编号）。
