#!/usr/bin/env bash
# 无头冒烟十一趟（固定步长、机器速度）+ 完成标记断言。
# Usage: check_smoke.sh [log_path]   (default /tmp/smoke.log)
# 覆盖「跑不到就发现不了」的面：
#   1) 300 帧基线：开机全链路（生产 main.tscn 直达标题屏）；
#   2) --settings-probe：设置页各分组在「开页」时才构建，写错＝玩家点开即崩；
#   3) --event-probe=formation_strike / elite_turret：遭遇要过分数门槛 + 掷签，常规冒烟跑不到，
#      而编排/投弹/落点圈/反射弹/结算分支是高密度出错区；
#   4) --event-probe-death=elite_turret：死亡打断路径（管理器 EndActive → 事件 Abort →
#      归还波次/Boss 互斥），自然探针等不到（无头局玩家不操作、不会死），此前是覆盖缺口；
#   5) --feel-probe：命中顿帧与屏幕震动复位——顿帧写 Engine.TimeScale，写错（倍率 0 或按
#      缩放 delta 推进）的表现是画面永久定格，无头下不崩也不报错，只有完成标记能抓住；
#   6) --long-probe：难度曲线落在预期带——曲线是 D 的纯函数，直接取 t=5/10/20/30min 的值断言
#      单调/速度有顶/精英随难度增长/Boss 斜率独立/软上限，把「曲线形状」变成可失败的判定；
#   7) --fog-probe：迷雾事件全周期——迷雾走「首延迟 25s + 每 3s 掷 35%」的随机链，常规冒烟
#      跑不到，而注册/context 构建/生命周期/效果清理是高密度出错区。强制入口只替换掷签与
#      权重选取（仍过生产门控），并断言「start→end 跑满生产 duration」——截断不打标记。
#   8) --return-probe：返航过场输入宽限与跳过收尾——走生产蓄力链触发返航，三段确定性判据：
#      宽限内跳过被忽略、推进 >宽限 的模拟时长后仍被忽略（真实时间基准的判别式：若误改模拟
#      时间此刻会放行）、越过宽限后跳过生效且落基地并保持暂停。不用「等真实时间越宽限」
#      （--fixed-fps 下那个写法既慢又不可靠，见 71e6324）。
#   9) 教程场景直开（--scene res://scenes/tutorial.tscn）：教程是独立场景、不走 Main 的标题屏
#      交接，属**另一条生产入口**——常规趟只跑 main.tscn，教程的入场链路（ResetRun、HUD 构建、
#      六阶段首屏）写坏时没有任何一趟会经过它。断场景就绪标记，判「加载/切场景失败」这类静默坏点。
# 判定三件事，缺一不可：
#   a) 退出码为 0；b) 日志无引擎错误；c) 每趟必须出现各自的完成标记
#   ——帧数只是上限，事件中途停摆同样是「零错误退出」，没有标记就是没跑到。
# --fixed-fps 60：固定步长让帧数＝模拟时长，且不等真实时间（帧数＝模拟秒数 × 60）。
#
# 2~10 趟走 scenes/probe_host.tscn（探针宿主，以子节点嵌入 main.tscn）：测试开关不进生产
# main.tscn/Main（AGENTS §5）。第 11 趟直开 scenes/tutorial.tscn——教程自有场景与入口，不经
# Main，故无宿主、无开关，只断完成标记。**每趟都在各自临时用户目录里跑**——探针会读存档/设置
# 在标题屏与设置页分叉，且返航趟的收尾走生产存档出口（Main.OnReturnFinished → SaveRun）；
# 不隔离就会读走开发者本机配置、写坏开发者当前存档（AGENTS §5「不依赖外部残留状态」）。
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

run_case() {
  local label="$1" frames="$2" log="$3" scene="$4" userdir="$5"
  shift 5
  local -a scene_args=()
  [ -n "$scene" ] && scene_args=(--scene "$scene")
  local -a env_args=()
  if [ -n "$userdir" ]; then
    mkdir -p "$userdir"
    # 隔离用户数据目录：Windows 读 APPDATA（须 Windows 路径），Linux 读 XDG_DATA_HOME
    local win_dir="$userdir"
    command -v cygpath >/dev/null 2>&1 && win_dir="$(cygpath -w "$userdir")"
    env_args=(env "APPDATA=$win_dir" "XDG_DATA_HOME=$userdir")
  fi
  if ! "${env_args[@]}" "$GODOT" --headless --path . --fixed-fps 60 --quit-after "$frames" \
      "${scene_args[@]}" -- "$@" > "$log" 2>&1; then
    echo "::error::$label failed"
    tail -30 "$log"
    exit 1
  fi
  if grep "$ERR" "$log" | grep -v "$ERR_ALLOW" | grep -q .; then
    echo "::error::$label engine errors in log"
    grep "$ERR" "$log" | grep -v "$ERR_ALLOW" | head -10
    exit 1
  fi
  echo "$label: ok"
}

expect_marker() {
  local label="$1" log="$2" marker="$3"
  if ! grep -qF "$marker" "$log"; then
    echo "::error::$label：日志无完成标记「$marker」——该路径没跑到终点"
    tail -30 "$log"
    exit 1
  fi
  echo "$label: ok"
}

run_case "main scene smoke(300)" 300 "$LOG" "" "${PROBE_LOG_BASE}.main.userdata"
# 开机交接：main 开机必须落到 title.tscn（无开场过场，直达标题屏）。标记由 TitleScreen._Ready
# 打印——切场景静默失败（路径错/资源缺失）时它不会出现，只判「不崩」则停在 main 空战场看不出。
expect_marker "开机直达标题屏" "$LOG" "[boot] 标题屏就绪"
run_case "settings page smoke" 60 "${PROBE_LOG_BASE}.settings.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.settings.userdata" --settings-probe
expect_marker "settings page 五页" "${PROBE_LOG_BASE}.settings.log" "[settings-probe] 五页切换完成"
# 全周期帧数含两段：① 探针等入场动画（0.55 + 1.1 = 1.65s）后才在生产触发链上放行
# （入场窗口内 spawner 停驱动、生产不可能触发，探针不得绕过）；② 事件自身全周期。
# 编队全周期 ≈ 入场 1.46s + 转弯 1.2s + 投弹最长 4.95s + 离场 1.5s ≈ 9.1s
run_case "formation strike smoke" 800 "${PROBE_LOG_BASE}.formation.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.formation.userdata" --event-probe=formation_strike
expect_marker "formation strike 全周期" "${PROBE_LOG_BASE}.formation.log" "[event-probe] formation_strike 全周期完成"
# 精英炮塔全周期 ≈ 入场 2s + 升起 1.5s + 30s 倒计时 + 撤离 ≈1.7s + Boss 恢复 4s ≈ 39.2s
run_case "elite turret smoke" 2700 "${PROBE_LOG_BASE}.elite.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.elite.userdata" --event-probe=elite_turret
expect_marker "elite turret 全周期" "${PROBE_LOG_BASE}.elite.log" "[event-probe] elite_turret 全周期完成"
# 死亡打断：激活后延迟 4s 击杀（覆盖「炮塔已升起」的清理分支）→ 打断 → 撤离 + Boss 恢复 ≈ 14s
run_case "elite turret death-path smoke" 1500 "${PROBE_LOG_BASE}.elite_death.log" "$PROBE_SCENE" \
  "${PROBE_LOG_BASE}.elite_death.userdata" --event-probe-death=elite_turret
expect_marker "elite turret 死亡打断" "${PROBE_LOG_BASE}.elite_death.log" "[event-probe] elite_turret 死亡打断完成"
# 燃料量槽满扫：无头局玩家不操作、不掉油，低油量填充绘制路径平时走不到；探针把液位从满扫到空，
# 逼 _Draw 在每个液位各画一次（含掉液触发的最大波幅晃动）。判定靠错误正则抓「Invalid polygon data」
# ——自交/退化多边形整块不画且不崩，只判「不崩」抓不到（低油量燃料槽整块消失即此类）。
run_case "fuel tank sweep smoke" 400 "${PROBE_LOG_BASE}.fuel.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.fuel.userdata" --fuel-probe
expect_marker "燃料量槽满扫" "${PROBE_LOG_BASE}.fuel.log" "[fuel-probe] 液位满扫完成"
# 手感探针：请求顿帧与震动后断言时间缩放压低/复位与 trauma 归零（无头下不崩即坏点，见文件头）。
# 隔离用户目录：探针前提是「顿帧/震动未被玩家关掉」，而 GameFeelService 在强度为 0 时直接忽略请求
# ——读开发者本机 settings.json 会让无障碍配置（把震动/顿帧拉 0）变成门禁假红（AGENTS §5 外部残留状态）。
run_case "game feel probe smoke" 400 "${PROBE_LOG_BASE}.feel.log" "$PROBE_SCENE"   "${PROBE_LOG_BASE}.feel.userdata" --feel-probe
expect_marker "顿帧与震动复位" "${PROBE_LOG_BASE}.feel.log" "[feel-probe] 顿帧与震动复位完成"
# 长局难度曲线：直接取生产曲线在 t=5/10/20/30min 的值，断言单调/速度有顶/精英增长/Boss 斜率独立/软上限。
run_case "long-run difficulty curve smoke" 200 "${PROBE_LOG_BASE}.long.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.long.userdata" --long-probe
expect_marker "难度曲线落在预期带" "${PROBE_LOG_BASE}.long.log" "[long-probe] 难度曲线落在预期带"
# 迷雾全周期（fake_enemies）：强制入口只替换掷签与权重选取，仍过生产门控（首延迟/冷却/接线/
# 本局活跃/组内无进行中），并断言 start→end 跑满生产 duration。帧数单源：25s 首延迟 + 8s
# fake_enemies duration（data/balance.json fog_events.durations）+ 3s 余量 = 36s × 60 = 2160。
run_case "fog event full-cycle smoke" 2160 "${PROBE_LOG_BASE}.fog.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.fog.userdata" --fog-probe
expect_marker "迷雾全周期" "${PROBE_LOG_BASE}.fog.log" "[fog-probe] 迷雾全周期完成"
# 返航宽限与跳过收尾：入场约 1.65s + 蓄力 1.5s + 判别窗口 1.5s（90 帧）+ 收尾余量 ≈ 6s，取 10s
# 余量 600 帧；探针不等真实时间，判据全部由帧数与墙钟前置守卫决定（见 ProbeHost.TickReturnProbe）。
run_case "return grace smoke" 600 "${PROBE_LOG_BASE}.return.log" "$PROBE_SCENE" "${PROBE_LOG_BASE}.return.userdata" --return-probe
expect_marker "返航宽限与跳过收尾" "${PROBE_LOG_BASE}.return.log" "[return-probe] 返航宽限与跳过收尾完成"
# 教程场景直开：教程是**另一条生产入口**（独立场景，不经 Main 的标题屏交接），上面各趟都不经过
# 它。标记在 Tutorial._Ready 末尾打，切场景/资源加载失败时不出现——只判「不崩」抓不到。
run_case "tutorial scene smoke" 120 "${PROBE_LOG_BASE}.tutorial.log" "res://scenes/tutorial.tscn" "${PROBE_LOG_BASE}.tutorial.userdata"
expect_marker "教程场景就绪" "${PROBE_LOG_BASE}.tutorial.log" "[tutorial] 场景就绪"
