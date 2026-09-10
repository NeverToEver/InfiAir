# InfiAir

单人 2D 俯视弹幕射击游戏（shmup）。Godot 4.6 .NET + C#（全量 C#，零 GDScript），GL Compatibility 渲染，1920×1080。

纯街机流：无登录、无存档、无排行榜、无局外成长——开机即玩，无尽模式，必死曲线。

## 玩法

- 自动开火 + 波次推进；里程碑与 Boss 击杀产出天赋点入缓存池，蓄力开天赋面板自主加点（27 节点 / 4 类别）
- 4 种 Boss 轮换（阶段弹幕 + 狂暴），50 秒未击杀则逃逸
- 母舰召唤补给、返回基地休整（机库/补给/契约/任务），继续出击
- 随机事件：精英炮塔、编队突袭、四种干扰雾事件
- 连击计分、擦弹、弹反（F）、无尽难度曲线

## 操作

| 按键 | 功能 |
| --- | --- |
| WASD / 方向键 | 移动（Ctrl 微调，Shift 加速） |
| Space | 冲刺 |
| F | 弹反（360° 反弹敌弹） |
| G（按住蓄力） | 天赋面板 |
| H（按住蓄力） | 召唤母舰 |
| B（按住蓄力） | 返回基地 |
| R | 重开（暂停页） |
| Esc | 返回 / 暂停 |

支持手柄与触屏（虚拟摇杆）。

## 运行

需要 **Godot 4.6+ .NET 版**（含 C# 工程，标准版无法打开）；开发构建需 **.NET 8 SDK**。

- Windows：双击 `run.bat`
- Linux / macOS：`./run.sh`（`--editor` 打开编辑器）
- 预编译成品见 `builds/release/`（Windows zip / Linux tar.gz）

## 开发

```bash
dotnet build                      # C# 构建（零警告门禁）
bash scripts/ci/check_import.sh   # 资源导入无警告
bash scripts/ci/check_smoke.sh    # 主场景 300 帧无头冒烟
./release.sh                      # 打包发布（Windows/Linux）
```

## 结构

```
csharp/core/     纯逻辑层（天赋树、进度曲线等，不依赖 Godot）
csharp/godot/    Godot 绑定层（场景脚本、UI、服务）
scenes/          场景文件
data/            balance.json 数值配置、翻译表
assets/           sprites / 音频 / 字体 / shader
packaging/       发布打包资源
docs/            DESIGN_BASELINE.md（设计定稿）、ROADMAP.md（方向与债务登记）
```

## 许可证

MIT — 见 [LICENSE](LICENSE)。
