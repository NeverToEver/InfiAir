# InfiAir

2D 俯视空战射击游戏（Godot 4.6 重制版），重制自 Python/Pygame 游戏 [airwar-game](../airwar-game)。竖向卷动星空、鼠标瞄准射击、波次敌机、里程碑 Buff 三选一、周期 Boss 战。

## 玩法与操作

| 操作 | 按键 |
| --- | --- |
| 移动 | WASD / 方向键 |
| 瞄准 | 鼠标（机头始终朝向鼠标） |
| 开火 | 按住鼠标左键（自动连发） |
| 加速 | Shift（约 1.8x，消耗燃料；耗尽后需回到 30% 才能再加速） |
| 相位冲刺 | 空格（需选中「相位冲刺」buff 解锁，冲刺期间无敌） |
| 暂停 | Esc |
| 重开（结算/暂停时） | R |

规则：

- 3 条命开局，受击后有 1.5 秒无敌帧（闪烁）。
- 敌机 4 种移动模式（直下 / 正弦 / 折线 / 俯冲），约 1/3 会朝玩家开火；精英敌机 HP 更高、分数更高，随分数混入，击毁后掉落拾取物（回命 / 满燃料 / 加分三选一随机，150px 内磁铁吸附）。
- 每 500 分触发一次 Buff 三选一（暂停游戏），池共 13 种：强力射击、急速射击、散射弹道（最多 3 层）、额外生命、自我修复、穿透弹（最多 2 层）、爆炸弹、吸血（最多 2 层）、护甲（最多 2 层）、闪避（最多 2 层）、相位冲刺（解锁 + 减冷却最多 2 次）、慢速力场、高效推进（最多 2 层）。
- 每 1500 分或每 90 秒（取先到者）刷出 Boss：屏幕上部巡航，扇形 5 发 + 追踪单发交替；击毁 +500 分，难度乘数按 `1 + (2^min(Boss击杀,10) − 1) × 0.25`（封顶 8x）提升。
- 命尽进入结算面板，按 R 重开。

## 运行方式

需要 Godot 4.6（gl_compatibility 渲染器）。

- 编辑器打开项目后按 F5；
- 或命令行：`godot --path .`

无头验证：

```bash
godot --headless --import --path .
godot --headless --path . --quit-after 300
```

## 目录结构

```
├── project.godot        # 项目配置（窗口/输入映射/autoload）
├── autoload/
│   └── game_state.gd    # 全局状态与信号总线（GameState）
├── scenes/              # 场景（main / player / enemy / boss / bullet）
├── scripts/             # 与场景同名的脚本及系统脚本
├── test/                # 无头冒烟测试场景
└── assets/
    ├── sprites/         # 战机贴图（机头朝上）
    ├── audio/           # 开火音效（3 个轮换）
    └── fonts/           # msyh.ttc 中文 UI 字体
```

## 借鉴的开源项目

- [nezvers/Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate) — 项目结构与场景组织参考
- [quiver-dev/top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core) — 俯视射击手感参考
- [Unchained112/SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D) — 2D 俯视射击模板参考

平衡数值与行为设计参考 Python 原作 `airwar-game/airwar/` 下的 `config/`、`entities/`、`systems/difficulty_manager.py`、`game/buffs/buffs.py`。

## MVP 范围与路线图

当前为 MVP：单局循环（刷怪 → 里程碑 Buff → Boss → 结算）已完整。

后续阶段（未实现）：

- 母舰对接与存档
- 返航 / 天赋系统
- 排行榜
- 新手教程
