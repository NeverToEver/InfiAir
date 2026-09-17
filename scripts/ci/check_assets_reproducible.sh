#!/usr/bin/env bash
# 素材生成可复现性门禁：重跑离线生成器，判定「生成器确定」与「产物与生成器同步」（AGENTS §1 单源政策）。
# 用法：bash scripts/ci/check_assets_reproducible.sh（输出走 stdout，由 gates.py / CI 捕获）
#
# 抓的静默错误：`scripts/tools/regenerate_all.sh` 声称「重跑后 git diff 应为空」，但这条不变式此前
# 没有任何自动判定——只写在脚本尾部提示里，靠人工执行核对。生成器引入时间戳 / 未播种随机 / 顺序不稳定，
# 或改了生成器却忘了提交产物时，产物会静默漂移：游戏照跑、单测照绿、冒烟照过，直到某次真要重跑素材才
# 暴露（那时已分不清是本次漂移还是历史累积漂移）。
#
# 判定分三段（段一/段二判生成器与产物的同步，段三判产物来源），都不依赖「本机恰好是产出这些产物的
# 那台机器」——跨机逐字节一致对这条管线**原理上做不到**，且实测的跨机差异有两类，量级完全不同：
#   (1) **PNG 编码字节**：像素逐位一致，只有压缩流不同（zlib / 编码器实现的版本差）。实测 CI 的 ubuntu
#       （Pillow 12.2）上全部漂移产物都属此类——产物内容其实是可复现的，逐字节判据在此纯属过严。
#   (2) **栅格化像素差**：AA 级、落在图元边缘（4× 超采样 + LANCZOS 降采样 + 高斯滤波的卷积实现与
#       FreeType 构建随平台变化）。实测 macOS（Pillow 12.2）最坏 3.6%：logo 字形边缘的亚像素落点，
#       最大通道差可达 255 但面积占比低。
# 同机两次重跑则逐字节一致（生成器本身确定）。故：
#
#   段一 同步（重跑一次）：逐文件与入库资产比对——尺寸/模式必须相同；PNG **非像素块**（文本、时间、
#        物理分辨率等元数据）必须相同（时间戳类漂移在此判红）；像素差异占比必须 ≤ 抗锯齿包络。
#        全字节一致 → 通过（本机即产出环境，确定性与同步一次性成立）。
#   段二 本机确定（仅当段一有残差时执行）：再重跑一次，比对两次重跑的输出是否逐字节一致——
#        不一致即「生成器非确定」，任何机器上都能判红；一致则确认残差来自跨机栅格化，放行并打印
#        残差摘要与「产物同步未在本机判定」的显式声明（不静默放过）。
#
# 抗锯齿包络（ENVELOPE_PCT）：单文件差异像素占比上限，实测口径为「跨机栅格化噪声远低于真实改动」——
# 栅格化差异只落在图元边缘且面积很小（实测 macOS 最坏 3.6%：logo 字形边缘的亚像素落点，最大通道差
# 可达 255 但面积占比低），而真实改动（新增/移动/改色元素）会形成连续区域，占比高出一个量级。
# 取值 10 是实测最坏值的约 3 倍余量：超出即判「生成器与产物不同步」，而不是继续当成噪声放行。
#
# 失败即**工作区已被本门禁改写**：脚本刻意不自动还原（自动还原会掩盖漂移现场，也可能覆盖跑者自己
# 的未提交素材改动），故必须打印醒目提示与 diff 摘要，让读者知道要去看 git status。
# 通过时则相反：判为跨机噪声的那几张图由本门禁自己还原回入库状态——基线已证明跑前干净，留着不还原
# 会让工作区带上「像未提交改动」的图，污染下一次跑的基线检查。
#
# 时间预算：段一实测约 33 秒（产出环境到此为止）；段二只在跨机残差时执行，本步最多约 66 秒，是全量
# 门禁里唯一超 30 秒的单条——AGENTS §6 时间预算允许「单条超 30 秒在上表注明理由」，理由是：它是唯一
# 能判「生成器确定 + 产物与生成器同步」的手段，无法用静态扫描替代（需要真跑 Pillow 绘制管线）；
# 它不依赖引擎、不改写 csharp/，失败面与其它门禁完全独立。段三是静态级判定（一次 git ls-files + 逐条
# stat，75 个文件），不另计时间。
#
# 依赖口径：Pillow 属**构建期素材生成器**依赖（AGENTS §1：构建期生成器允许 Python 第三方库，
# 产物入库、不进发布包），不是运行时依赖；缺失即显式判红（不得静默跳过——跳过等于假绿）。
#
# 段三 产物来源（登记表，与上两段互补）：AGENTS §1 的「素材一律程序化生成、产物入库」**不只**等于
# 「重跑无漂移」——上两段判的是「生成器确定 + 产物同步」，而一个**手工放进来、已提交、生成器根本不产出**
# 的素材在它们下面不留任何痕迹：git status 干净、生成器不碰它、逐文件比对里没有它，于是全部门禁绿灯，
# 纪律只剩口头承诺（谁都能塞一张下载来的图进 assets/）。故本段把 assets/ 与 data/ 下每个被跟踪的
# 非 *.import 文件按来源登记在下面的 ASSET_ORIGIN_MANIFEST（本脚本内单源），逐条给出「它是怎么来的」的
# 可验证证据，并判三类红：
#   - 未登记的新文件（手工塞素材的形态）：分类未知即拒判——默认怀疑，而不是默认放行；
#   - 登记项已失效（登记了但仓库里没有这个文件）：登记表腐烂成「什么都放行」的形态；
#   - 登记为生成物却没被本次重跑刷新：管线不再产出它（生成器被摘掉调用、或换了产出路径）的形态。
# 分类：generated（regenerate_all.sh 管线产出，第三列写产出它的生成器）/ handwritten（人写的源，
# 第三列写理由）/ third_party（外部素材，第三列写许可与随包授权文本的出处）/ engine_sidecar
# （引擎导入期写出的伴随文件）。第三列一律必填——「不需要理由」本身就是一种分类，得写出来。
#
# 生成物的证据＝「本次重跑确实写过它」：跑前落一个运行起点标记，跑后逐条比对该文件 mtime 是否落在标记
# 之后、且与跑前快照不同。为何这不是 AGENTS §5 说的真实时间判定：那里禁的是拿墙钟推「游戏世界推进了
# 多少」；这里读的是文件系统记下的**谁在何时写过它**这一事实，判据本身在任何机器上给出同一结论（同一份
# 登记表 + 同一套条件），墙钟只是「写没写过」的凭据，模拟与判定都不依赖它。边界两头写清：
#   - 假阴（生成物被判成非生成物）：文件系统若只记秒级 mtime，而某个生成器恰好在起点标记的同一秒内落盘，
#     该条会读成「未刷新」而误红。实测平台（APFS / ext4 均纳秒级，python3 os.stat 直读）不触发；即便
#     触发也只是误红、在 CI 里立刻暴露，不会静默放过。
#   - 假阳（非生成物被判成生成物）：需要在运行窗口内由管线之外的进程改写该文件。跑前基线已断工作区干净、
#     CI 里没有第二写者；本地有人在门禁跑动时手工改该文件理论上能骗过这一条，代价是他同时还得把登记表
#     填对——属人为绕过，不是静默错误。
#   - 「跑前刚 git checkout 过、mtime 很新」不构成假阳：git 落盘用的是检出时刻，而起点标记是本脚本在
#     基线检查（工作区必须干净）之后才创建的，故跑前检出的文件 mtime 必然早于标记，仍读作「未被刷新」。
#   - 本段只判「谁产出的」，不重复判「regenerate_all.sh 是否覆盖了全部生成器」：静态装配归
#     check_gate_wiring.sh，运行期刷新证据归本段，两者互补而非双份。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

ARTIFACT_PATHS=(assets data)
ENVELOPE_PCT=10

# 素材来源登记表（单源）：<分类>|<路径>|<产出者 / 理由>，路径须与 git ls-files 逐字一致。
# 改动须知：新增素材只有两条路——由 regenerate_all.sh 的管线产出（登记 generated），或登记 handwritten /
# third_party / engine_sidecar 并把来源、许可与随包授权文本写进第三列。未登记的文件一律判红。
ASSET_ORIGIN_MANIFEST=(
    # 玩家战机（generate_player_sprite.py）
    "generated|assets/sprites/player_ship.png|generate_player_sprite.py"
    "generated|assets/sprites/player_ship_glow.png|generate_player_sprite.py"
    "generated|assets/sprites/player_ship_hit_1.png|generate_player_sprite.py"
    "generated|assets/sprites/player_ship_hit_2.png|generate_player_sprite.py"

    # 敌机 / 精英 / Boss / 航母 / 炮塔（generate_enemy_sprites.py）
    "generated|assets/sprites/boss_ship_1.png|generate_enemy_sprites.py"
    "generated|assets/sprites/boss_ship_1_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/boss_ship_2.png|generate_enemy_sprites.py"
    "generated|assets/sprites/boss_ship_2_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/boss_ship_3.png|generate_enemy_sprites.py"
    "generated|assets/sprites/boss_ship_3_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/boss_ship_4.png|generate_enemy_sprites.py"
    "generated|assets/sprites/boss_ship_4_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/elite_ship_1.png|generate_enemy_sprites.py"
    "generated|assets/sprites/elite_ship_1_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/elite_ship_2.png|generate_enemy_sprites.py"
    "generated|assets/sprites/elite_ship_2_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/elite_ship_3.png|generate_enemy_sprites.py"
    "generated|assets/sprites/elite_ship_3_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/elite_turret.png|generate_enemy_sprites.py"
    "generated|assets/sprites/enemy_ship_1.png|generate_enemy_sprites.py"
    "generated|assets/sprites/enemy_ship_1_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/enemy_ship_2.png|generate_enemy_sprites.py"
    "generated|assets/sprites/enemy_ship_2_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/enemy_ship_3.png|generate_enemy_sprites.py"
    "generated|assets/sprites/enemy_ship_3_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/enemy_ship_4.png|generate_enemy_sprites.py"
    "generated|assets/sprites/enemy_ship_4_glow.png|generate_enemy_sprites.py"
    "generated|assets/sprites/strike_carrier.png|generate_enemy_sprites.py"
    "generated|assets/sprites/strike_carrier_glow.png|generate_enemy_sprites.py"

    # Boss P2 损伤帧（generate_boss_p2_frames.py）
    "generated|assets/sprites/boss_ship_1_p2.png|generate_boss_p2_frames.py"
    "generated|assets/sprites/boss_ship_1_p2_glow.png|generate_boss_p2_frames.py"
    "generated|assets/sprites/boss_ship_2_p2.png|generate_boss_p2_frames.py"
    "generated|assets/sprites/boss_ship_2_p2_glow.png|generate_boss_p2_frames.py"
    "generated|assets/sprites/boss_ship_3_p2.png|generate_boss_p2_frames.py"
    "generated|assets/sprites/boss_ship_3_p2_glow.png|generate_boss_p2_frames.py"
    "generated|assets/sprites/boss_ship_4_p2.png|generate_boss_p2_frames.py"
    "generated|assets/sprites/boss_ship_4_p2_glow.png|generate_boss_p2_frames.py"

    # 母舰（generate_mothership_sprite.py）
    "generated|assets/sprites/mothership.png|generate_mothership_sprite.py"
    "generated|assets/sprites/mothership_glow.png|generate_mothership_sprite.py"

    # 深空背景（generate_backdrop_sprites.py）
    "generated|assets/sprites/backdrop/debris_1.png|generate_backdrop_sprites.py"
    "generated|assets/sprites/backdrop/debris_2.png|generate_backdrop_sprites.py"
    "generated|assets/sprites/backdrop/debris_3.png|generate_backdrop_sprites.py"
    "generated|assets/sprites/backdrop/planet_1.png|generate_backdrop_sprites.py"
    "generated|assets/sprites/backdrop/planet_2.png|generate_backdrop_sprites.py"

    # 标题 logo（generate_logo.py）
    "generated|assets/sprites/ui/logo.png|generate_logo.py"

    # UI 金属贴图（gen_metal_textures.py）
    "generated|assets/sprites/ui/button_plate.png|gen_metal_textures.py"
    "generated|assets/sprites/ui/button_plate_pressed.png|gen_metal_textures.py"
    "generated|assets/sprites/ui/metal_streak.png|gen_metal_textures.py"

    # 音效 / BGM（generate_audio.py + tune_sfx.py --apply）
    "generated|assets/audio/bgm_base.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/bgm_boss.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/bgm_loop.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/buff_pick.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/bullet_fire.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/bullet_fire_b.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/bullet_fire_c.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/dash.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/explosion.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/explosion_big.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/heartbeat.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/player_hit.wav|generate_audio.py + tune_sfx.py --apply"
    "generated|assets/audio/resupply.wav|generate_audio.py + tune_sfx.py --apply"

    # 外部字体（第三方素材）：Noto Sans SC，SIL OFL 1.1。OFL 要求每份副本附版权声明与许可全文，
    # 故许可全文（NotoSansSC-OFL.txt）与 NOTICE 一并在库，发布包的拷贝清单见 release.sh 的授权文本段
    # （LICENSE / NOTICE / assets/fonts/NotoSansSC-OFL.txt 三个文件都缺失即中止打包）。
    "third_party|assets/fonts/NotoSansSC.ttf|Noto Sans SC 字体文件；SIL OFL 1.1；授权全文见 assets/fonts/NotoSansSC-OFL.txt，出处与许可摘要见 NOTICE"
    "third_party|assets/fonts/NotoSansSC-OFL.txt|Noto Sans SC 的 SIL OFL 1.1 许可全文（随副本分发用）；release.sh 把它拷进发布包"

    # 手写源：着色器与 data/ 下的两张人维护表（都不是素材管线的产物，重跑不会写它们）
    "handwritten|assets/shaders/crack_field_bake.gdshader|手写的 Godot 着色器（视觉源文件，着色器由引擎在运行时编译，不经素材管线）"
    "handwritten|assets/shaders/meta_health.gdshader|手写的 Godot 着色器（同上）"
    "handwritten|assets/shaders/ship_energy.gdshader|手写的 Godot 着色器（同上）"
    "handwritten|assets/shaders/starfield_nebula.gdshader|手写的 Godot 着色器（同上）"
    "handwritten|assets/shaders/world_grade.gdshader|手写的 Godot 着色器（同上）"
    "handwritten|data/balance.json|人维护的数值表（单源见 AGENTS §1；scripts/tools/balance_editor.py 是它的可视化编辑器而非生成器，键的完整性归 check_balance_keys.sh / check_balance_dead_keys.sh）"
    "handwritten|data/translations.csv|人维护的玩家可见文案表（单源见 AGENTS §1；键的完整性归 check_ui_copy.sh）"

    # 引擎伴随文件：Godot 导入期为着色器写出的 UID（资源引用口径要求它与 .gdshader 一起入库），
    # 既不是人写的、也不是素材管线产出的，故单列一类
    "engine_sidecar|assets/shaders/crack_field_bake.gdshader.uid|Godot 为 .gdshader 生成的 UID 伴随文件（随对应着色器一起入库）"
    "engine_sidecar|assets/shaders/meta_health.gdshader.uid|Godot 为 .gdshader 生成的 UID 伴随文件（同上）"
    "engine_sidecar|assets/shaders/ship_energy.gdshader.uid|Godot 为 .gdshader 生成的 UID 伴随文件（同上）"
    "engine_sidecar|assets/shaders/starfield_nebula.gdshader.uid|Godot 为 .gdshader 生成的 UID 伴随文件（同上）"
    "engine_sidecar|assets/shaders/world_grade.gdshader.uid|Godot 为 .gdshader 生成的 UID 伴随文件（同上）"
)

if ! command -v git >/dev/null 2>&1 || ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    echo "::error::git 不可用或当前目录不是 git 工作区——复现性判据（产物 diff）取不到，拒绝判 clean"
    exit 1
fi

if ! python3 -c "import PIL" >/dev/null 2>&1; then
    echo "::error::python3 无法 import PIL（Pillow 缺失）——素材生成可复现性判据取不到，拒绝判 clean。"
    echo "::error::装法：pip install pillow==12.2（版本与本机一致，避免上游改渲染导致误报）；" \
         "CI 见 .github/workflows/ci.yml 的 Pillow 安装步骤。"
    exit 1
fi

baseline="$(git status --porcelain -- "${ARTIFACT_PATHS[@]}")"
if [ -n "$baseline" ]; then
    echo "::error::产物路径（${ARTIFACT_PATHS[*]}）已有未提交改动——重跑后的差异分不清是本次漂移还是跑前就有，"
    echo "::error::判据取不到，拒绝判 clean。先提交或还原下列改动再跑："
    printf '%s\n' "$baseline"
    echo "::error::若是新素材：先提交，再按 ASSET_ORIGIN_MANIFEST 登记它的来源——未登记的素材本门禁一律判红，"
    echo "::error::且只有「由 regenerate_all.sh 的管线真的产出」才配登记为 generated（详见脚本头注「段三」）。"
    exit 1
fi

# 段三的跑前准备：登记表投影 + 被跟踪素材清单 + 运行起点标记 + 生成物的跑前 mtime 快照。
# 顺序要紧：基线检查已在上面断过工作区干净，故这三样是「本次重跑之前」的状态。
ORIGIN_TMP="$(mktemp -d "${TMPDIR:-/tmp}/infiair-asset-origin.XXXXXX")" || {
    echo "::error::临时目录创建失败——产物来源判据取不到，拒绝判 clean"
    exit 1
}
trap 'rm -rf "$ORIGIN_TMP"' EXIT
ORIGIN_MANIFEST="$ORIGIN_TMP/manifest.txt"
ORIGIN_TRACKED="$ORIGIN_TMP/tracked.txt"
ORIGIN_PRE="$ORIGIN_TMP/mtime-pre.txt"
ORIGIN_MARKER="$ORIGIN_TMP/run-start.marker"

printf '%s\n' "${ASSET_ORIGIN_MANIFEST[@]}" > "$ORIGIN_MANIFEST"
git ls-files -- "${ARTIFACT_PATHS[@]}" | grep -v '\.import$' | LC_ALL=C sort > "$ORIGIN_TRACKED"
: > "$ORIGIN_MARKER"

python3 - "$ORIGIN_MANIFEST" "$ORIGIN_PRE" <<'PY'
import os
import sys

# 只做一件事：把登记为生成物的文件在「重跑之前」的 mtime 落盘。格式有问题的行跳过不报——
# 逐行格式判定（含行号）归段三的判定，这里报一遍会让同一个问题出现两套措辞。
manifest, out = sys.argv[1], sys.argv[2]
with open(manifest, encoding="utf-8") as src, open(out, "w", encoding="utf-8") as dst:
    for line in src:
        parts = line.strip().split("|")
        if len(parts) != 3 or parts[0].strip() != "generated":
            continue
        path = parts[1].strip()
        try:
            stamp = str(os.stat(path).st_mtime_ns)
        except OSError:
            stamp = "MISSING"
        dst.write(f"{path}\t{stamp}\n")
PY
if [ "$?" -ne 0 ]; then
    echo "::error::跑前 mtime 快照失败（登记表格式？）——产物来源判据取不到，拒绝判 clean"
    exit 1
fi

snapshot() {
    # 产物树的内容指纹（排序后逐文件 sha256）：段二用它判「两次重跑是否一致」
    find "${ARTIFACT_PATHS[@]}" -type f -print0 2>/dev/null \
        | sort -z \
        | xargs -0 shasum -a 256 2>/dev/null \
        | shasum -a 256
}

echo "==> 重跑素材生成器（scripts/tools/regenerate_all.sh，实测约 33s）"
if ! bash scripts/tools/regenerate_all.sh; then
    echo "::error::regenerate_all.sh 自身失败（非漂移判定）——生成器写坏或环境缺依赖，门禁不代它下结论，先修生成器"
    exit 1
fi

echo "==> 段三：产物来源判定（登记表 ↔ 被跟踪素材清单 ↔ 本次重跑刷新证据）"
ORIGIN_MANIFEST="$ORIGIN_MANIFEST" ORIGIN_TRACKED="$ORIGIN_TRACKED" \
ORIGIN_PRE="$ORIGIN_PRE" ORIGIN_MARKER="$ORIGIN_MARKER" python3 - <<'PY'
import os
import sys
import time

CATEGORIES = ("generated", "handwritten", "third_party", "engine_sidecar")
# 下限守卫（AGENTS §6 铁律 2：取不到判据必须显式失败）：登记表或 git ls-files 被读空 / 截断时，
# 下面逐条判定会全部静默判 clean。下限取当前值（75 个被跟踪素材文件、61 个生成物）的余量以内——
# 真要下线这些素材时，改这里与登记表是同一件事，改不动就说明登记表已经不可信。
TRACKED_FLOOR = 60
GENERATED_FLOOR = 50


def stamp(ns: int) -> str:
    return time.strftime("%H:%M:%S", time.localtime(ns / 1e9)) + f".{ns % 1_000_000_000 // 1_000_000:03d}"


errors = []

tracked = [ln.strip() for ln in open(os.environ["ORIGIN_TRACKED"], encoding="utf-8") if ln.strip()]
if len(tracked) < TRACKED_FLOOR:
    errors.append(
        f"被跟踪素材文件只读到 {len(tracked)} 个（下限 {TRACKED_FLOOR}）——git ls-files 取空或范围漂移，判据取不到"
    )

entries = []
for lineno, line in enumerate(open(os.environ["ORIGIN_MANIFEST"], encoding="utf-8"), 1):
    line = line.strip()
    if not line:
        continue
    parts = line.split("|")
    if len(parts) != 3 or not all(p.strip() for p in parts):
        errors.append(f"登记表第 {lineno} 行不是 <分类>|<路径>|<理由>：{line[:80]}")
        continue
    kind, path, reason = (p.strip() for p in parts)
    if kind not in CATEGORIES:
        errors.append(f"登记表第 {lineno} 行的分类 {kind!r} 不在 {CATEGORIES} 里：{path}")
        continue
    if not path.startswith(("assets/", "data/")):
        errors.append(f"登记表第 {lineno} 行的路径不在 assets/ 或 data/ 下：{path}")
        continue
    if path.endswith(".import"):
        errors.append(f"登记表第 {lineno} 行登记了 *.import（导入产物不参与来源登记）：{path}")
        continue
    entries.append((kind, path, reason, lineno))

linenos_of = {}
for _kind, path, _reason, lineno in entries:
    linenos_of.setdefault(path, []).append(lineno)
for path, linenos in sorted(linenos_of.items()):
    if len(linenos) > 1:
        errors.append(f"登记表里 {path} 出现 {len(linenos)} 次（第 {linenos} 行）——分类有歧义，删到只剩一条")

by_kind = {k: [p for kk, p, _r, _l in entries if kk == k] for k in CATEGORIES}
if len(by_kind["generated"]) < GENERATED_FLOOR:
    errors.append(
        f"登记为生成物的只有 {len(by_kind['generated'])} 条（下限 {GENERATED_FLOOR}）——登记表被截断或类别写错，判据取不到"
    )

owned = set(linenos_of)
tracked_set = set(tracked)
for path in sorted(tracked_set - owned):
    errors.append(f"素材来源未登记：{path}——不在 ASSET_ORIGIN_MANIFEST 里，本门禁无法断定它是程序化生成的")
for path in sorted(owned - tracked_set):
    errors.append(f"登记项已失效：{path} 登记在册，但仓库里没有这个被跟踪文件——登记表腐烂（条目指向不存在的文件），删条目或改回真实路径")

marker = os.stat(os.environ["ORIGIN_MARKER"]).st_mtime_ns
pre = {}
for line in open(os.environ["ORIGIN_PRE"], encoding="utf-8"):
    path, value = line.rstrip("\n").split("\t")
    pre[path] = value

for path in by_kind["generated"]:
    if path not in tracked_set:      # 已由上面「登记项已失效」判过，这里不再重复报一遍
        continue
    try:
        cur = os.stat(path).st_mtime_ns
    except OSError:
        errors.append(f"生成物未被本次重跑刷新：{path}——文件不存在（登记为生成物，重跑后仍没有它）")
        continue
    before = pre.get(path, "MISSING")
    # 两条证据都要看：mtime 与跑前快照不同（有东西写过它）、且落在运行起点之后（时间窗是本次运行）
    if before != "MISSING" and cur == int(before):
        errors.append(f"生成物未被本次重跑刷新：{path}——mtime 未变（{stamp(cur)}），本次重跑没写过它")
    elif cur <= marker:
        errors.append(f"生成物未被本次重跑刷新：{path}——mtime {stamp(cur)} 早于运行起点 {stamp(marker)}，本次重跑没写过它")

if errors:
    for item in errors:
        print("::error::" + item)
    print("::error::怎么算过：① 程序化生成 → 让 scripts/tools/regenerate_all.sh 的管线真的写出来它，"
          "并在 ASSET_ORIGIN_MANIFEST 登记 generated；")
    print("::error::② 人写的源文件 → 登记 handwritten 并写明理由；③ 外部素材 → 登记 third_party 并写明"
          "许可与随包授权文本的出处（见 NOTICE 与 release.sh 的拷贝清单）。")
    print("::error::登记表就在本脚本顶部，全库只有这一份——加进去的是来源说明，不是放行条。")
    sys.exit(1)

counts = " / ".join(f"{kind} {len(by_kind[kind])}" for kind in CATEGORIES)
print(f"assets-origin gate: clean（登记表 {len(entries)} 条 ↔ 仓库 {len(tracked)} 个被跟踪素材文件逐条对齐："
      f"{counts}；生成物 {len(by_kind['generated'])} 个本次重跑全部刷新，无未分类文件、无失效登记项）")
PY
if [ "$?" -ne 0 ]; then
    exit 1
fi

drift="$(git status --porcelain -- "${ARTIFACT_PATHS[@]}")"
if [ -z "$drift" ]; then
    echo "assets-reproducible gate: clean（重跑生成器后 ${ARTIFACT_PATHS[*]} 与入库资产逐字节一致，" \
         "无改动也无新增文件——本机即产出环境；产物来源见段三判定）"
    exit 0
fi

echo "==> 产物与入库资产不完全一致，按「结构 + 抗锯齿包络」逐文件判定（漂移清单见下）"
printf '%s\n' "$drift"

first_snapshot="$(snapshot)"
drift_files="$(printf '%s\n' "$drift" | awk '{print $NF}')"
verdict="$(
    DRIFT_FILES="$drift_files" ENVELOPE_PCT="$ENVELOPE_PCT" python3 - <<'PY'
import io
import os
import struct
import subprocess
import sys

from PIL import Image, ImageChops

envelope = float(os.environ["ENVELOPE_PCT"])
paths = [p for p in os.environ["DRIFT_FILES"].splitlines() if p]

# PNG 非像素块：文本/时间/色彩/物理分辨率等元数据块的内容要逐字节相同（时间戳类漂移在此判红）；
# IDAT（像素数据）不进签名——它的字节差异正是本判据要度量的东西。
_META_CHUNKS = (b"tEXt", b"zTXt", b"iTXt", b"tIME", b"pHYs", b"sRGB", b"gAMA", b"iCCP", b"sBIT")


def chunk_signature(data: bytes) -> list:
    out = []
    pos = 8
    while pos + 8 <= len(data):
        length, ctype = struct.unpack(">I4s", data[pos : pos + 8])
        body = data[pos + 8 : pos + 8 + length]
        if ctype in _META_CHUNKS:
            out.append((ctype, body))
        elif ctype != b"IDAT":
            out.append((ctype, b""))
        pos += 12 + length
    return out


structural = []
over = []
within = []
encoding = []
identical = 0

for path in paths:
    shown = subprocess.run(["git", "show", f"HEAD:{path}"], capture_output=True)
    if shown.returncode != 0:
        structural.append((path, "本次重跑新增的产物（入库资产里没有这个文件）"))
        continue

    committed = shown.stdout
    with open(path, "rb") as fh:
        current = fh.read()
    if committed == current:
        identical += 1
        continue

    left = Image.open(io.BytesIO(committed))
    right = Image.open(io.BytesIO(current))
    if left.size != right.size or left.mode != right.mode:
        structural.append((path, f"尺寸/模式变了：{left.size}/{left.mode} → {right.size}/{right.mode}"))
        continue

    if chunk_signature(committed) != chunk_signature(current):
        structural.append((path, "PNG 非像素块不同（元数据/编码参数漂移，如时间戳）"))
        continue

    diff = ImageChops.difference(left.convert("RGBA"), right.convert("RGBA"))
    bands = diff.split()
    mask = None
    for band in bands:
        one = band.point(lambda v: 255 if v else 0)
        mask = one if mask is None else ImageChops.lighter(mask, one)

    total = left.size[0] * left.size[1]
    differing = total - mask.histogram()[0]
    ratio = 100.0 * differing / total
    max_delta = max(band.getextrema()[1] for band in bands)
    if differing == 0:
        # 像素逐位一致、只有 PNG 编码字节不同（zlib / 编码器实现的版本差）：产物内容可复现
        encoding.append((path, f"编码字节不同（{len(committed)} → {len(current)} B），像素逐位一致"))
    elif ratio > envelope:
        over.append((path, f"差异像素 {ratio:.2f}%（上限 {envelope:g}%）、最大通道差 {max_delta}"))
    else:
        within.append((path, f"差异像素 {ratio:.2f}%、最大通道差 {max_delta}"))

print(f"identical={identical}")
for label, rows in (("STRUCTURAL", structural), ("OVER", over), ("ENCODING", encoding), ("WITHIN", within)):
    for path, detail in rows:
        print(f"{label}\t{path}\t{detail}")
sys.exit(1 if (structural or over) else 0)
PY
)"
rc=$?
printf '%s\n' "$verdict"

if [ "$rc" -ne 0 ]; then
    echo "::error::素材产物与生成器不同步：上面的结构与超包络条目是「生成器改了但产物没提交」或「新增/删除产物没同步」的证据。"
    echo "::error::diff 摘要（git diff --stat）："
    git diff --stat -- "${ARTIFACT_PATHS[@]}"
    echo "::error::处置：核对是否为有意改动——有意改素材就把新产物一起提交（并在提交正文说明生成器改了什么）；"
    echo "::error::非有意则生成器引入了非确定性（时间戳 / 未播种随机 / 遍历顺序），修成确定性后重跑。"
    exit 1
fi

echo "==> 段二：残差是否为跨机栅格化——再重跑一次，比对两次重跑是否逐字节一致"
if ! bash scripts/tools/regenerate_all.sh; then
    echo "::error::regenerate_all.sh 第二次运行失败（非漂移判定）——生成器不稳定，先修生成器"
    exit 1
fi

second_snapshot="$(snapshot)"
if [ "$first_snapshot" != "$second_snapshot" ]; then
    echo "::error::生成器**非确定**：同机连续两次重跑的输出不一致——产物会随运行时刻/随机序漂移。"
    echo "::error::常见来源：写进 PNG 的时间戳或版本串、未播种的随机、遍历顺序依赖、并发写同一文件。"
    echo "::error::本判据与平台无关（同机两次重跑），修成确定性后重跑本门禁。"
    exit 1
fi

# 判定为跨机噪声：把本门禁自己改写过的产物还原回入库状态——基线已证明跑前是干净的，故只可能
# 是本次重跑的产物；不还原会留下 6 张「看起来像未提交改动」的图，污染下一次跑的基线检查。
printf '%s\n' "$drift_files" | while IFS= read -r path; do
    [ -n "$path" ] && git checkout -- "$path"
done

echo "assets-reproducible gate: clean（生成器本机确定：两次重跑逐字节一致；与入库资产的差异见上方逐文件归类："
echo "ENCODING = 像素逐位一致、仅 PNG 编码字节不同（产物内容可复现）；WITHIN = 抗锯齿级像素差"
echo "（本机非产出环境，逐字节同步未在本机判定）。已把本门禁重跑出的产物还原回入库状态；"
echo "产物来源见段三判定；跨机差异口径见 AGENTS §6 与本步脚本头注）"
