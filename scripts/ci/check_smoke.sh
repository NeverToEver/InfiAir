#!/usr/bin/env bash
# 无头冒烟（固定步长、机器速度）+ 完成标记断言。
# Usage: check_smoke.sh [log_path]   (default /tmp/smoke.log)
# 覆盖「跑不到就发现不了」的面（趟数与调度单源在文件末尾 SMOKE_CASES；同一开关的不同参数视作同一面）：
#   1) 300 帧基线：开机全链路（生产 main.tscn 直达标题屏）；
#   2) --settings-probe：设置页各分组在「开页」时才构建，写错＝玩家点开即崩；
#   3) --event-probe=formation_strike / elite_turret：遭遇要过分数门槛 + 掷签，常规冒烟跑不到，
#      而编排/投弹/落点圈/反射弹/结算分支是高密度出错区；
#   4) --event-probe-death=elite_turret：死亡打断路径（管理器 EndActive → 事件 Abort →
#      归还波次/Boss 互斥），自然探针等不到（无头局玩家不操作、不会死），此前是覆盖缺口；
#   5) --fuel-probe：燃料量槽满扫——无头局玩家不操作不掉油，低液位填充绘制路径走不到，
#      探针把液位从满扫到空逼 _Draw 在每个液位各画一次（自交多边形整块不画且不崩）；
#   6) --feel-probe：命中顿帧与屏幕震动复位——顿帧写 Engine.TimeScale，写错（倍率 0 或按
#      缩放 delta 推进）的表现是画面永久定格，无头下不崩也不报错，只有完成标记能抓住；
#   7) --long-probe：难度曲线落在预期带——曲线是 D 的纯函数，直接取 t=5/10/20/30min 的值断言
#      单调/速度有顶/精英随难度增长/Boss 斜率独立/软上限，把「曲线形状」变成可失败的判定；
#   8) --fog-probe：迷雾事件全周期——迷雾走「首延迟 25s + 每 3s 掷 35%」的随机链，常规冒烟
#      跑不到，而注册/context 构建/生命周期/效果清理是高密度出错区。强制入口只替换掷签与
#      权重选取（仍过生产门控），并断言「start→end 跑满生产 duration」——截断不打标记；
#   9) --return-probe：返航过场输入宽限与跳过收尾——走生产蓄力链触发返航，三段确定性判据：
#      宽限内跳过被忽略、推进 >宽限 的模拟时长后仍被忽略（真实时间基准的判别式：若误改模拟
#      时间此刻会放行）、越过宽限后跳过生效且落基地并保持暂停。不用「等真实时间越宽限」
#      （--fixed-fps 下那个写法既慢又不可靠，见 71e6324）；
#  10) --hostile-save-probe：恶意存档（语法合法但字段类型不符）读入必须逐字段回默认——
#      Variant.As* 是宽松转换、不抛（AsBool("no") 得 true、AsInt64("lots") 得 0），裸取会把坏值
#      静默读成合法值（任务 claimed 错判为已领取即此类）；单测够不到（判型在引擎绑定层），
#      故探针写档 + 调生产读档入口实跑。该趟另断正常档逐项还原与读档补发信号
#      （只判恶意档会让「守卫一律回退」照样绿）；
#  11) --death-gate-probe：死亡删档的本局门控——删档钩子挂在全局 PlayerDied 上且无场景上下文，
#      教程死亡走同一信号并把它当预期终态，无门控就是「玩教程顺手抹掉玩家检查点」。本趟跑在
#      探针宿主里（非本局），发 PlayerDied 后检查点必须原封不动；再按生产语义置本局后发一次，
#      检查点必须被删——前半段判误伤、后半段判「该删的仍然删」，缺一半判不出；
#  12) --boss-probe：Boss 阶段机全周期——Boss 出场要过分数门 + 最小间隔（或时间兜底），
#      常规冒烟跑不到；而阶段机（P1→P2→狂暴→击杀）在引擎侧此前零覆盖，写坏的表现是
#      「不崩、不报错、只是没那一段」：转场未发生（少一次清弹与喘息）、锁血不解（Boss 永久无敌）、
#      击杀没接轮换（下一只又是同一型）。本趟走生产触发链请出 Boss，再经生产受击链逐段越线，
#      并断血量单调不增、转场清弹 + 玩家短暂无敌、狂暴锁血自行解除、击杀后 BossKills 推进；
#  13) --dock-probe：母舰坞态全周期——长按 dock 蓄满走生产蓄力链，断
#      DESCEND→DOCKING→RESUPPLY→STAY→RELEASE→DEPART 六态按序推进（写坏的表现是坞态卡死，
#      母舰悬停不动、不崩不报错）、驻留期弹匣确实在耗到警告档、长按提前离舰生效、离场给出坞冷却；
#  14) --event-probe-killall：遭遇的「击杀型」收尾（精英炮塔 → 轰炸编队）——常规
#      --event-probe 跑不到任何击杀（精英趟只走到超时 0 奖励、编队趟让编队自然离场只命中「清除」档），
#      于是全歼奖励、Tier.AllClear、结算台词的节点侧发奖与播报在 CI 里从未执行。
#      本趟把两个事件各走一遍全歼，并断「档位奖励确实入账」（分数增量 ≥ 生产配置下界）+ 结算台词已播；
#  15) 教程场景直开（--scene res://scenes/tutorial.tscn）：教程是独立场景、不走 Main 的标题屏
#      交接，属**另一条生产入口**——常规趟只跑 main.tscn，教程的入场链路（ResetRun、HUD 构建、
#      六阶段首屏）写坏时没有任何一趟会经过它。断场景就绪标记，判「加载/切场景失败」这类静默坏点。
# 判定三件事，缺一不可：
#   a) 退出码为 0；b) 日志无引擎错误；c) 每趟必须出现各自的完成标记
#   ——帧数只是上限，事件中途停摆同样是「零错误退出」，没有标记就是没跑到。
# --fixed-fps 60：固定步长让帧数＝模拟时长，且不等真实时间（帧数＝模拟秒数 × 60）。
#
# 首趟跑生产 main.tscn；中间各趟走 scenes/probe_host.tscn（探针宿主，以子节点嵌入
# main.tscn）——测试开关不进生产 main.tscn/Main（AGENTS §5）；最后一趟直开
# scenes/tutorial.tscn：教程自有场景与入口，不经 Main，无宿主、无开关，只断完成标记。
# **每趟都在各自临时用户目录里跑（使用前先清空重建）**——探针会读存档/设置在标题屏与设置页
# 分叉，且返航趟的收尾走生产存档出口（Main.OnReturnFinished → SaveRun）；不隔离就会读走开发者
# 本机配置、写坏开发者当前存档，不清空则同目录重跑会读到上一趟探针写下的档
# （AGENTS §5「不依赖外部残留状态」）。
#
# 并行度：各趟彼此独立（各自日志、各自用户目录、各自进程），串行时每趟的引擎启动与场景加载
# 是固定开销（实测约占总时长四成），故按 SMOKE_WORKERS（默认 4）分批并行——**只改墙钟，不改
# 判据**：帧数仍由 --fixed-fps 60 决定（模拟时长不受机器快慢影响），每趟的完成标记与错误正则
# 判定与串行完全一致。批内任一趟失败即整趟判红（失败趟的输出全部打出）。设 SMOKE_WORKERS=1
# 退回串行（同一份判定逻辑，只换调度）。
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG="${1:-/tmp/smoke.log}"
# 引擎错误正则。`Invalid polygon data, triangulation failed.` 是程序化绘制的静默坏点：
# headless 走 dummy 渲染仍会执行 _Draw（实测），自交/退化多边形在 canvas_item_add_polygon
# 处报该错并**整块不画**——不崩、不看日志就完全无感（燃料槽低油量整块消失即此类）。
# `ERROR:` 是通用引擎错误前缀，兜住上面未列举的错误类别（此前只看退出码，静默错误漏判）。
ERR="SCRIPT ERROR\|Parse Error\|Compile Error\|Nonexistent function\|Unhandled exception\|Invalid polygon data\|ERROR:"
# 白名单：退出期资源统计噪声（RefCounted 释放顺序告警，非功能坏点）。设置页趟实测出现
# `ERROR: 1 resources still in use at exit`；无头冒烟退出时对象释放顺序与探针无关，不判功能。
ERR_ALLOW="ERROR: [0-9][0-9]* resources still in use at exit"
PROBE_SCENE="res://scenes/probe_host.tscn"
PROBE_LOG_BASE="${LOG%.log}"
WORKERS="${SMOKE_WORKERS:-4}"

run_case() {
  local label="$1" frames="$2" log="$3" scene="$4" userdir="$5"
  shift 5
  local -a scene_args=()
  [ -n "$scene" ] && scene_args=(--scene "$scene")
  local -a env_args=()
  local -a expect_args=()
  if [ -n "$userdir" ]; then
    # 先复位再建：探针会往自己的用户目录写存档/设置（恶意档趟写 run.json/settings.json，
    # 返航趟走生产存档出口），残留会让下一趟（或同目录重跑）读到上一趟的档而判红
    # ——绿不再是可信信号（AGENTS §5「不依赖外部残留状态」）。各趟用户目录互不相同、
    # 且每趟需要的预置状态都在趟内自备（恶意档/检查点均由探针自己写出），故清空安全。
    rm -rf "$userdir"
    mkdir -p "$userdir"
    # 隔离用户数据目录：Windows 读 APPDATA（须 Windows 路径），Linux 读 XDG_DATA_HOME，
    # macOS 读 HOME——三处都指到本趟临时目录，否则 macOS 上 user:// 落在真实
    # ~/Library/Application Support/Godot/app_userdata/InfiAir：恶意档趟覆写、死亡删档趟
    # 删掉开发者的真实检查点，而门禁照旧全绿。
    local win_dir="$userdir"
    command -v cygpath >/dev/null 2>&1 && win_dir="$(cygpath -w "$userdir")"
    env_args=(env "APPDATA=$win_dir" "XDG_DATA_HOME=$userdir" "HOME=$userdir")
    # 显式期望值交给探针（ProbeHost 在 _Ready 比对引擎实际 user:// 路径，不符即报错）；
    # 格式为绝对路径、正斜杠分隔。脚本侧另有与探针实现无关的日志判据（见下方隔离断言），
    # 覆盖 main/tutorial 这类不经探针宿主的趟。
    expect_args=(--expect-user-dir="${win_dir//\\//}")
  else
    echo "::error::$label 未给用户目录——不隔离的趟会读走开发者配置、写坏开发者当前存档，" \
         "且同目录重跑会读到上一趟的残留（AGENTS §5）"
    return 1
  fi
  if ! "${env_args[@]}" "$GODOT" --headless --path . --fixed-fps 60 --quit-after "$frames" \
      "${scene_args[@]}" -- "$@" "${expect_args[@]}" > "$log" 2>&1; then
    echo "::error::$label failed"
    tail -30 "$log"
    return 1
  fi
  # 隔离判据（与探针实现无关）：引擎的 user:// 若没落在本趟临时目录内，这里就看不到它写出的
  # user://logs/godot.log——隔离失效必须显式判红并指明是哪趟，否则开发者的真实检查点被删/
  # 被覆写而门禁全绿（AGENTS §6 铁律 2）。
  local user_log=""
  user_log="$(find "$userdir" -type f -name 'godot.log' -print -quit 2>/dev/null)"
  if [ -z "$user_log" ]; then
    echo "::error::$label 用户目录隔离未生效：$userdir 下没有引擎写出的 user:// 日志" \
         "（隔离环境变量没被引擎采纳？引擎版本换了日志落点？）——本趟可能已读写开发者本机数据"
    ls -la "$userdir" 2>/dev/null | head -5
    return 1
  fi
  # 错误行判定先落文件再取内容，不用 `grep | grep -v | grep -q` 管道：错误行极多时 `grep -q` 一命中
  # 就退出，上游 grep 可能收到 SIGPIPE，pipefail 下整条管道返回非 0（141）而把有错的日志判成无错。
  # 模式沿用 BRE（ERR 内是 `\|` 交替）——**不得改成 grep -E**：`\|` 在 ERE 里是字面竖线，
  # 全部错误类别会一起失配，门禁静默变成永不报错。
  local errs="$log.errs"
  grep "$ERR" "$log" > "$errs" 2>/dev/null
  if [ -s "$errs" ]; then
    grep -v "$ERR_ALLOW" "$errs" > "$errs.kept" 2>/dev/null
    if [ -s "$errs.kept" ]; then
      echo "::error::$label engine errors in log"
      head -10 "$errs.kept"
      rm -f "$errs" "$errs.kept"
      return 1
    fi
  fi
  rm -f "$errs" "$errs.kept"
  echo "$label: ok"
  return 0
}

expect_marker() {
  local label="$1" log="$2" marker="$3"
  if ! grep -qF "$marker" "$log"; then
    echo "::error::$label：日志无完成标记「$marker」——该路径没跑到终点"
    tail -30 "$log"
    return 1
  fi
  echo "$label: ok"
  return 0
}

# ---- 各趟定义：每趟是「run_case 紧随 expect_marker」的成对结构（成对性由 check_gate_wiring.sh
# 静态判定：漏一条断言就把该趟降级为「不崩即过」）。函数体只做定义，调度在文件末尾。

smoke_main() {
  run_case "main scene smoke(300)" 300 "$LOG" "" "${PROBE_LOG_BASE}.main.userdata"
  # 开机交接：main 开机必须落到 title.tscn（无开场过场，直达标题屏）。标记由 TitleScreen._Ready
  # 打印——切场景静默失败（路径错/资源缺失）时它不会出现，只判「不崩」则停在 main 空战场看不出。
  expect_marker "开机直达标题屏" "$LOG" "[boot] 标题屏就绪"
}

smoke_settings() {
  run_case "settings page smoke" 60 "${PROBE_LOG_BASE}.settings.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.settings.userdata" --settings-probe
  expect_marker "settings page 五页" "${PROBE_LOG_BASE}.settings.log" "[settings-probe] 五页切换完成"
}

smoke_formation() {
  # 全周期帧数含两段：① 探针等入场动画（0.55 + 1.1 = 1.65s）后才在生产触发链上放行
  # （入场窗口内 spawner 停驱动、生产不可能触发，探针不得绕过）；② 事件自身全周期。
  # 编队全周期 ≈ 入场 1.46s + 转弯 1.2s + 投弹最长 4.95s + 离场 1.5s ≈ 9.1s（余量 300 帧）。
  run_case "formation strike smoke" 750 "${PROBE_LOG_BASE}.formation.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.formation.userdata" --event-probe=formation_strike
  expect_marker "formation strike 全周期" "${PROBE_LOG_BASE}.formation.log" "[event-probe] formation_strike 全周期完成"
}

smoke_elite() {
  # 精英炮塔全周期 ≈ 入场 2s + 升起 1.5s + 30s 倒计时 + 撤离 ≈1.7s + Boss 恢复 4s ≈ 39.2s
  # （余量 250 帧）。
  run_case "elite turret smoke" 2600 "${PROBE_LOG_BASE}.elite.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.elite.userdata" --event-probe=elite_turret
  expect_marker "elite turret 全周期" "${PROBE_LOG_BASE}.elite.log" "[event-probe] elite_turret 全周期完成"
}

smoke_elite_death() {
  # 死亡打断：激活后延迟 4s 击杀（覆盖「炮塔已升起」的清理分支）→ 打断 → 撤离 + Boss 恢复 ≈ 14s。
  run_case "elite turret death-path smoke" 1400 "${PROBE_LOG_BASE}.elite_death.log" "$PROBE_SCENE" \
    "${PROBE_LOG_BASE}.elite_death.userdata" --event-probe-death=elite_turret
  expect_marker "elite turret 死亡打断" "${PROBE_LOG_BASE}.elite_death.log" "[event-probe] elite_turret 死亡打断完成"
}

smoke_fuel() {
  # 燃料量槽满扫：无头局玩家不操作、不掉油，低油量填充绘制路径平时走不到；探针把液位从满扫到空，
  # 逼 _Draw 在每个液位各画一次（含掉液触发的最大波幅晃动）。判定靠错误正则抓「Invalid polygon data」
  # ——自交/退化多边形整块不画且不崩，只判「不崩」抓不到（低油量燃料槽整块消失即此类）。
  run_case "fuel tank sweep smoke" 300 "${PROBE_LOG_BASE}.fuel.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.fuel.userdata" --fuel-probe
  expect_marker "燃料量槽满扫" "${PROBE_LOG_BASE}.fuel.log" "[fuel-probe] 液位满扫完成"
}

smoke_feel() {
  # 手感探针：请求顿帧与震动后断言时间缩放压低/复位与 trauma 归零（无头下不崩即坏点，见文件头）。
  # 隔离用户目录：探针前提是「顿帧/震动未被玩家关掉」，而 GameFeelService 在强度为 0 时直接忽略请求
  # ——读开发者本机 settings.json 会让无障碍配置（把震动/顿帧拉 0）变成门禁假红（AGENTS §5 外部残留状态）。
  run_case "game feel probe smoke" 300 "${PROBE_LOG_BASE}.feel.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.feel.userdata" --feel-probe
  expect_marker "顿帧与震动复位" "${PROBE_LOG_BASE}.feel.log" "[feel-probe] 顿帧与震动复位完成"
}

smoke_long() {
  # 长局难度曲线：直接取生产曲线在 t=5/10/20/30min 的值，断言单调/速度有顶/精英增长/Boss 斜率独立/软上限。
  run_case "long-run difficulty curve smoke" 180 "${PROBE_LOG_BASE}.long.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.long.userdata" --long-probe
  expect_marker "难度曲线落在预期带" "${PROBE_LOG_BASE}.long.log" "[long-probe] 难度曲线落在预期带"
}

smoke_fog() {
  # 迷雾全周期（fake_enemies）：强制入口只替换掷签与权重选取，仍过生产门控（首延迟/冷却/接线/
  # 本局活跃/组内无进行中），并断言 start→end 跑满生产 duration。帧数单源：25s 首延迟 + 8s
  # fake_enemies duration（data/balance.json fog_events.durations）+ 余量 = 2100 帧。
  run_case "fog event full-cycle smoke" 2100 "${PROBE_LOG_BASE}.fog.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.fog.userdata" --fog-probe
  expect_marker "迷雾全周期" "${PROBE_LOG_BASE}.fog.log" "[fog-probe] 迷雾全周期完成"
}

smoke_fog_interrupt() {
  # 迷雾打断：首延迟 25s 后起事件、跑满 1s 走生产返航/死亡同一条 API（FogEvents.EndActive）打断，
  # 断「存活补偿不发放」。与 --fog-probe 的自然到期发奖互补——只判一侧会让「一律发/一律不发」混过。
  run_case "fog interrupt smoke" 1700 "${PROBE_LOG_BASE}.fog_cut.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.fog_cut.userdata" --fog-interrupt-probe
  expect_marker "迷雾打断不发存活补偿" "${PROBE_LOG_BASE}.fog_cut.log" "[fog-interrupt-probe] 打断不发存活补偿"
}

smoke_return() {
  # 返航宽限与跳过收尾：入场约 1.65s + 蓄力 1.5s + 判别窗口 1.5s（90 帧）+ 收尾余量 ≈ 6s
  # （余量 550 帧）；探针不等真实时间，判据全部由帧数与墙钟前置守卫决定（见 ProbeHost.TickReturnProbe）。
  run_case "return grace smoke" 550 "${PROBE_LOG_BASE}.return.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.return.userdata" --return-probe
  expect_marker "返航宽限与跳过收尾" "${PROBE_LOG_BASE}.return.log" "[return-probe] 返航宽限与跳过收尾完成"
}

smoke_hostile() {
  # 恶意存档读入：语法合法但字段类型不符的 run.json/settings.json 必须逐字段回默认。
  # Variant.As* 是宽松转换、不抛（AsBool("no") 得 true、AsInt64("lots") 得 0），裸取会把坏值静默读成
  # 合法值——任务 claimed 错判为已领取、血量读成 0，界面无任何信号。判定在引擎绑定层，
  # xUnit 只引用 core 够不到，故用探针实跑生产读档入口（写档 + LoadRun/LoadSettings）。
  # 该趟另断正常档逐项还原与读档补发信号（只判恶意档会让「守卫一律回退」的实现照样绿）。
  run_case "hostile save probe smoke" 120 "${PROBE_LOG_BASE}.hostile.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.hostile.userdata" --hostile-save-probe
  expect_marker "恶意档读入未崩溃" "${PROBE_LOG_BASE}.hostile.log" "[hostile-save-probe] 恶意档读入未崩溃"
}

smoke_death_gate() {
  # 死亡删档门控：删档钩子挂在全局 PlayerDied 上，教程死亡走同一信号且被教程当预期终态——
  # 无门控即「玩教程顺手抹掉玩家检查点」。本趟跑在宿主里（非本局）：发 PlayerDied 后检查点必须
  # 原封不动；再置本局发一次必须删掉——前半段判误伤、后半段判「该删的仍然删」，缺一半判不出。
  run_case "death delete gate smoke" 120 "${PROBE_LOG_BASE}.death_gate.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.death_gate.userdata" --death-gate-probe
  expect_marker "死亡删档本局门控" "${PROBE_LOG_BASE}.death_gate.log" "[death-gate-probe] 非本局不删档/本局删档均生效"
}

smoke_boss() {
  # Boss 阶段机全周期：生产触发链请出 Boss（分数门补到 boss_score_step 之上；时间门由生产链自己
  # 走满 boss_min_interval）→ 生产受击链逐段越线 → P1→P2（转场清弹 + 玩家短暂无敌）→ ENRAGE
  # （锁血自行解除）→ 击杀（BossKills 推进、生成器解槽）。帧数单源：入场 1.65s + boss_min_interval
  # 80s + 越线/狂暴/击杀推进 ≈ 20s + 余量 = 107s × 60 = 6420。
  run_case "boss phase machine smoke" 6420 "${PROBE_LOG_BASE}.boss.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.boss.userdata" --boss-probe
  expect_marker "boss 阶段机全周期" "${PROBE_LOG_BASE}.boss.log" "[boss-probe] 阶段机全周期完成"
}

smoke_dock() {
  # 母舰坞态全周期：长按 dock 蓄力 3s 召唤 → 入场 0.8s + 对接 1.5s + 补给 0.5s + 驻留（耗到
  # mag_warn_cells 警告档 12s）+ 提前离舰 2s + 释放 0.5s + 离场出界 ≈ 25s（余量 200 帧）。
  run_case "mothership dock cycle smoke" 1700 "${PROBE_LOG_BASE}.dock.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.dock.userdata" --dock-probe
  expect_marker "母舰坞态全周期" "${PROBE_LOG_BASE}.dock.log" "[dock-probe] 坞态全周期完成"
}

smoke_killall() {
  # 遭遇「击杀型」收尾（精英炮塔全歼 → 轰炸编队全歼，一趟串两个事件）：等升起到位/投弹后
  # 逐单位击杀，断每事件的完成标记 + 档位奖励确实入账 + 结算台词已播。两事件的固定开销
  # （引擎启动 + 场景加载）比帧数更贵，合趟是时间预算（AGENTS §6 铁律 4）下的取舍。
  run_case "encounter killall smoke" 1350 "${PROBE_LOG_BASE}.killall.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.killall.userdata" --event-probe-killall
  expect_marker "遭遇击杀型全周期" "${PROBE_LOG_BASE}.killall.log" "[event-probe] 击杀型全周期完成（精英炮塔与轰炸编队）"
}

smoke_augment_cache() {
  # 增幅缓存连接态：跑到敌机池发生复用（同一实例回收后被下一波取出），断言其 slow_field 缓存
  # 仍接在 AugmentsChanged 上。坏法静默——reparent 触发 _ExitTree，连/断错序后该敌机整个
  # 活跃期不再随加点刷新（「买了力场没感觉」），不崩不报错。未观察到复用不打标记。
  run_case "augment cache reuse smoke" 2000 "${PROBE_LOG_BASE}.augment.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.augment.userdata" --augment-cache-probe
  expect_marker "池化复用后缓存连接态成立" "${PROBE_LOG_BASE}.augment.log" "[augment-cache-probe] 池化复用后缓存连接态成立"
}

smoke_tutorial() {
  # 教程场景直开：教程是**另一条生产入口**（独立场景，不经 Main 的标题屏交接），上面各趟都不经过
  # 它。标记在 Tutorial._Ready 末尾打，切场景/资源加载失败时不出现——只判「不崩」抓不到。
  run_case "tutorial scene smoke" 120 "${PROBE_LOG_BASE}.tutorial.log" "res://scenes/tutorial.tscn" "${PROBE_LOG_BASE}.tutorial.userdata"
  expect_marker "教程场景就绪" "${PROBE_LOG_BASE}.tutorial.log" "[tutorial] 场景就绪"
}

SMOKE_CASES=(
  smoke_main
  smoke_settings
  smoke_formation
  smoke_elite
  smoke_elite_death
  smoke_fuel
  smoke_feel
  smoke_long
  smoke_fog
  smoke_fog_interrupt
  smoke_return
  smoke_hostile
  smoke_death_gate
  smoke_boss
  smoke_dock
  smoke_killall
  smoke_augment_cache
  smoke_tutorial
)

# 调度：串行（WORKERS ≤ 1）时逐趟直跑，输出实时可见、首个失败即退出（与既有行为一致）；
# 并行时按 WORKERS 分批，批内各趟各自重定向到临时文件，批结束后按定义顺序回放输出——
# 失败趟的输出与串行时同形（run_case/expect_marker 里的 ::error:: 与日志尾部照打）。
total="${#SMOKE_CASES[@]}"
failures=0
out_dir="$(mktemp -d)"
trap 'rm -rf "$out_dir"' EXIT

# 单趟失败判定：退出码非零，或该趟输出里出现 `::error::`。
# 为什么不能只看退出码：每趟函数体是「run_case 紧随 expect_marker」两条**并列**语句，脚本未开
# `set -e`（只用 set -uo pipefail），函数退出码只等于最后一条命令——run_case 判出的引擎错误
# 会被紧随其后的 expect_marker 成功覆盖，于是「日志有 ERROR 但路径跑完」的趟判绿，
# 而这一整类正是错误正则（含 `Invalid polygon data`、通用 `ERROR:`）存在的理由。
case_failed() {
  local out="$1" rc="$2"
  if [ "$rc" -ne 0 ]; then
    return 0
  fi
  if [ -f "$out" ] && grep -qF "::error::" "$out"; then
    return 0
  fi
  return 1
}

if [ "$WORKERS" -le 1 ]; then
  # 串行：输出实时可见，首个失败即退出（与既有行为一致）
  for fn in "${SMOKE_CASES[@]}"; do
    out="$out_dir/$fn.out"
    "$fn" 2>&1 | tee "$out"
    rc=${PIPESTATUS[0]}
    if case_failed "$out" "$rc"; then
      echo "::error::无头冒烟在趟次 $fn 处失败（串行模式，首个失败即停）"
      exit 1
    fi
  done
else
  i=0
  while [ "$i" -lt "$total" ]; do
    end=$((i + WORKERS))
    [ "$end" -gt "$total" ] && end="$total"
    pids=()
    for ((j = i; j < end; j++)); do
      "${SMOKE_CASES[j]}" > "$out_dir/$j.out" 2>&1 &
      pids+=($!)
    done
    for ((j = i; j < end; j++)); do
      rc=0
      wait "${pids[j - i]}" || rc=$?
      if case_failed "$out_dir/$j.out" "$rc"; then
        failures=$((failures + 1))
      fi
    done
    i="$end"
  done
  for ((j = 0; j < total; j++)); do
    cat "$out_dir/$j.out"
  done
fi

if [ "$failures" -gt 0 ]; then
  echo "::error::无头冒烟 ${failures}/${total} 趟失败（并行度 ${WORKERS}；SMOKE_WORKERS=1 可退回串行复现）"
  exit 1
fi

echo "无头冒烟 ${total} 趟全部通过（并行度 ${WORKERS}）"
