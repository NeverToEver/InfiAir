# InfiAir

[![CI](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml/badge.svg)](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml)

**简体中文** | [English](README_EN.md)

单人 2D 俯视角弹幕射击游戏（shmup）。无尽街机流：无登录、无排行榜、无局外成长，开机即玩——成长有界、压力无界，必死曲线上看你能撑多久。

| | |
| --- | --- |
| 引擎 | Godot 4.6 .NET，全量 C#（零 GDScript），GL Compatibility，1920×1080 |
| 平台 | Windows / Linux / macOS，支持手柄与触屏（虚拟摇杆） |
| 界面语言 | 简体中文 / English（游戏内切换） |
| 许可证 | MIT |

## 特性

- **本局检查点存档** — 回基地或「保存并退出」自动落盘；死亡或放弃重开即删档——检查点可以续，死了不能读。标题屏「继续上次出击」还原整局进度（分数、难度、天赋、增幅、任务），战场从新一波开始。
- **天赋缓存树** — 里程碑与 Boss 击杀产出天赋点进入缓存池（后进先出、超额衰减），蓄力打开面板自主加点：35 节点 / 4 类别，派系互斥、专注惩罚、路线契约、风险加点四套机制互相制衡，构筑没有免费午餐。
- **精确的弹幕手感** — 自动开火 + 连击计分（3 秒窗口、最高 ×2）、擦弹得分、360° 弹反把敌弹原样奉还；受击判定只有 2.8px 的核心，宽限期与离场结算保证直击必中、擦边才免。
- **Boss 轮换与遭遇事件** — 4 种 Boss 阶段弹幕 + 狂暴，50 秒打不死就逃逸；精英炮塔、编队突袭、四种干扰雾事件按优先级互斥穿插，同屏不乱套。
- **母舰与黎明站** — 蓄力召唤母舰随行支援（火力平台 + 机库小窗），返回黎明站休整：机库、补给、契约、任务轮换，然后继续出击。
- **战术琥珀视觉** — 深炭蓝黑底、琥珀主交互、数据青辅助；世界层手写屏幕后处理（辉光 / 调色 / 晕影 / 颗粒）补足 GL Compatibility；重伤时屏幕碎裂、呼吸与心跳随血量下沉，可一键减弱闪烁。
- **窗口与性能** — 窗口化 / 无边框全屏；渲染分辨率 720p–4K 按显示器列档，窗口自由拖拽并记忆任意尺寸；视角三档缩放；帧率上限六档（60–240）+ 垂直同步开关。
- **无尽必死曲线** — 难度随 Boss 击杀与时长无上限爬升，三档难度决定得分倍率与节奏；全部数值单源 `data/balance.json`。

## 操作

| 输入 | 功能 |
| --- | --- |
| WASD / 方向键 | 移动（Ctrl 微调，Shift 加速） |
| 鼠标 / 右摇杆 | 瞄准（准星与光标逐像素绑定，永不脱钩） |
| Space | 冲刺 |
| F / LT | 弹反（360° 反弹敌弹） |
| G（按住蓄力） | 天赋面板 |
| H（按住蓄力） | 召唤母舰 |
| B（按住蓄力） | 返回基地 |
| R | 重新出击（暂停页） |
| Esc | 返回 / 暂停 |

标题屏：**任意键**新的一局 · **C** 继续上次出击 · **T** 教程。菜单为左缘圆盘 UI，方向键 / 摇杆旋转、确认键按下。

## 运行

**预编译包**：从 [Releases](https://github.com/NeverToEver/InfiAir/releases) 下载（Windows zip / Linux tar.gz），需安装 [.NET 8 运行时](https://dotnet.microsoft.com/download/dotnet/8.0)。

**从源码运行**：需要 [Godot 4.6+ .NET 版](https://godotengine.org/download)（含 C# 支持，标准版无法打开本工程）；构建 C# 需 .NET 8 SDK。

- Windows：双击 `run.bat`
- Linux / macOS：`./run.sh`（`--editor` 打开编辑器）

脚本会自动探测引擎（`godot-mono` → `godot` → `godot4` → 常见安装位置）。

## 开发

```
csharp/core/     纯逻辑层：天赋树、进度曲线、数值模型（不依赖 Godot）
csharp/godot/    Godot 绑定层：场景脚本、UI、服务
scenes/          场景：main / title / tutorial
data/            balance.json 数值单源 · translations.csv 双语翻译表
assets/          sprites / 音频 / 字体 / shader
packaging/       发布打包资源
scripts/         CI 门禁与工具脚本
docs/            设计定稿（DESIGN_BASELINE.md）· 方向与债务（ROADMAP.md）
```

- 数值调参只改 `data/balance.json`，全站颜色只改 `csharp/godot/UITheme.cs`。
- 设计意图见 [DESIGN_BASELINE](docs/DESIGN_BASELINE.md)；方向 / 债务 / 决策索引见 [ROADMAP](docs/ROADMAP.md)；**提交前必过的验证门禁见 [AGENTS.md](AGENTS.md)**。

## 许可证

[MIT](LICENSE)。代码、贴图、音频与着色器均为原创（程序化生成）；唯一第三方素材为 [Noto Sans SC](assets/fonts/NotoSansSC.ttf) 字体（SIL OFL 1.1），详见 [NOTICE](NOTICE)。
