#!/usr/bin/env bash
# 统一素材生成入口:依固定顺序重跑 7 个离线生成器,输出与仓库提交资产一致。
# 用法:scripts/tools/regenerate_all.sh
# - 生成器输出路径均锚定脚本位置,可在任意 cwd 下运行;
# - 脚本幂等、可重复执行:贴图生成器为纯确定性绘制(无随机源),
#   generate_audio.py 固定 random.seed(20260720),全量重跑输出应逐字节一致
#   (验证: 运行后 git diff --stat 应为空)。
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# 解释器探测:优先系统 python3(须可 import PIL),否则用仓库 .venv/bin/python3
PY=""
if command -v python3 >/dev/null 2>&1 && python3 -c "import PIL" >/dev/null 2>&1; then
    PY="python3"
elif [ -x "$REPO_ROOT/.venv/bin/python3" ] && "$REPO_ROOT/.venv/bin/python3" -c "import PIL" >/dev/null 2>&1; then
    PY="$REPO_ROOT/.venv/bin/python3"
else
    echo "错误:未找到可用的 Python(Pillow/PIL 缺失)。" >&2
    echo "  请安装 Pillow,或使用包含 Pillow 的仓库 .venv。" >&2
    exit 1
fi
echo "==> 使用解释器: $PY"

echo "==> [1/7] 玩家战机 + 受击帧贴图 (generate_player_sprite.py)"
"$PY" "$SCRIPT_DIR/generate_player_sprite.py"
echo "    产物: assets/sprites/player_ship.png, player_ship_hit_1.png, player_ship_hit_2.png,"
echo "          player_ship_glow.png(能量发光遮罩)"

echo "==> [2/7] 敌机/精英/Boss/航母/炮塔贴图 (generate_enemy_sprites.py)"
"$PY" "$SCRIPT_DIR/generate_enemy_sprites.py"
echo "    产物: assets/sprites/enemy_ship_1..4.png, elite_ship_1..3.png,"
echo "          boss_ship_1..4.png, strike_carrier.png(800x460), elite_turret.png"
echo "          + 各机 *_glow.png(能量发光遮罩,elite_turret 除外)"

echo "==> [3/7] Boss P2 损伤帧 (generate_boss_p2_frames.py)"
"$PY" "$SCRIPT_DIR/generate_boss_p2_frames.py"
echo "    产物: assets/sprites/boss_ship_1_p2.png .. boss_ship_4_p2.png"
echo "          + boss_ship_1_p2_glow.png .. boss_ship_4_p2_glow.png(损伤能量遮罩)"

echo "==> [4/7] 母舰贴图 (generate_mothership_sprite.py)"
"$PY" "$SCRIPT_DIR/generate_mothership_sprite.py"
echo "    产物: assets/sprites/mothership.png, mothership_glow.png(能量发光遮罩)"

echo "==> [5/7] 深空背景贴图 (generate_backdrop_sprites.py)"
"$PY" "$SCRIPT_DIR/generate_backdrop_sprites.py"
echo "    产物: assets/sprites/backdrop/planet_1.png(900x900), planet_2.png(640x640),"
echo "          debris_1..3.png"

echo "==> [6/7] 标题 logo (generate_logo.py)"
"$PY" "$SCRIPT_DIR/generate_logo.py"
echo "    产物: assets/sprites/ui/logo.png(900x260)"

echo "==> [7/7] 音效/BGM (generate_audio.py)"
"$PY" "$SCRIPT_DIR/generate_audio.py"
echo "    产物: assets/audio/explosion.wav, explosion_big.wav, player_hit.wav,"
echo "          buff_pick.wav, dash.wav, resupply.wav, heartbeat.wav,"
echo "          bgm_loop.wav, bullet_fire.wav, bullet_fire_b.wav, bullet_fire_c.wav"

echo "==> 全部生成完成。可复现性验证: git diff --stat(重跑应无 diff)。"
