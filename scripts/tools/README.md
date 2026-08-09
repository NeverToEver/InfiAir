# scripts/tools 素材生成器

离线生成游戏素材(非游戏运行时依赖),全部为确定性输出(固定 seed / 纯绘制),
重跑应逐字节一致。

## 生成器

| 脚本 | 职责 | 输出(相对仓库根) |
| --- | --- | --- |
| `generate_player_sprite.py` | 玩家战机贴图(钛灰钢甲 + 青色能量,画布 254×254) | `assets/sprites/player_ship.png` |
| `generate_enemy_sprites.py` | 敌机/精英/Boss/航母/炮塔贴图(12 个文件) | `assets/sprites/enemy_ship_1..4.png`、`elite_ship_1..3.png`、`boss_ship_1..3.png`、`strike_carrier.png`、`elite_turret.png` |
| `generate_mothership_sprite.py` | 母舰贴图 | `assets/sprites/mothership.png` |
| `generate_audio.py` | 音效 + BGM(11 个 WAV) | `assets/audio/explosion.wav`、`explosion_big.wav`、`player_hit.wav`、`buff_pick.wav`、`dash.wav`、`resupply.wav`、`heartbeat.wav`、`bgm_loop.wav`、`bullet_fire.wav`、`bullet_fire_b.wav`、`bullet_fire_c.wav` |

各生成器无命令行参数;输出路径锚定脚本位置,可在任意 cwd 下运行。

## regenerate_all.sh

统一入口,依序重跑上述 4 个生成器:

```bash
scripts/tools/regenerate_all.sh
```

- 自动探测解释器:优先 `python3`(须可 `import PIL`),否则回退仓库 `.venv/bin/python3`;
  均不可用时报错退出。
- `set -e` 任一生成器失败即终止;脚本幂等,可重复执行。

## 可复现性

- 3 个贴图生成器为纯确定性绘制(无随机源);
- `generate_audio.py` 固定 `random.seed(20260720)`(bullet_fire 三变体生成前再次重置对齐资产);
- 全量重跑后 `git diff --stat` 应为空(与提交资产零 diff)。
