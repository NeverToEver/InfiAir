using Godot;
using InfiAir.Core.Text;

// 精英机贴图路径探针（`--elite-ship-probe=型别`，ProbeHost 的 partial 分文件）：把某一型精英的
// 「主贴图 + 辉光遮罩」映射放进 CI 的稳定覆盖。
//
// 为什么需要它（AGENTS §5 选层判据；ROADMAP 债务区「精英第 4 型在 CI 短局不保证出场」）：
//   - 型别按 `GD.Randi() % 4` 均匀抽取，而 180 模拟秒的自动游玩短局常只出 1–2 个精英波：
//     末位型写错时短局照绿。两种坏法**都不报错**——映射指到另一张存在的贴图（GD.Load 成功），
//     `_glow.png` 遮罩缺失（ShipEnergyFx.GlowTextureFor 返回 null，调用方静默保留原遮罩）。
//   - 静态对账面也覆盖不到：机型表的 texture 键不在 balance.json，check_code_defaults.sh 的
//     ELITE_SKIP_KEYS 明文跳过它——只能靠运行时真出现一只、再读它节点上的实际贴图。
//
// 口径：
//   - 型别由开关给出（一次性直选，Spawner.RequestEliteType）。**只替下随机抽取这一步**：
//     出场时机仍由生产波次表与 special gap 决定，入场仍走预告线 → 池化 → 生产数值与开火。
//   - 断言读的是**节点上的实际贴图**（Sprite2D.Texture.ResourcePath 与 GlowLayer 的同一字段），
//     不是代码里的字面量——映射写错时字面量照样自洽，只有实际贴图会暴露。
//   - 等到帧上限仍没有精英出场、或任一路径不符，都只 PushError、不打完成标记（门禁按缺标记判红）。
#if DEBUG || TOOLS
namespace InfiAir;

public partial class ProbeHost
{
    /// <summary>缺省型别：末位（短局最不容易覆盖到的那一型）。</summary>
    private const int EliteShipDefaultType = 4;

    /// <summary>等到第几帧仍没有精英出场就判红。生产波次表下第一波精英是第 4 波
    /// （special gap 初值 3；波次间隔 7s 起、随 elapsed 向 4s 收敛），约 27 模拟秒 ≈ 1620 帧，
    /// 余量按 2400 帧给。</summary>
    private const int EliteShipDeadlineFrame = 2400;

    private bool _eliteShipProbe;
    private int _eliteShipType = EliteShipDefaultType;
    private bool _eliteShipRequested;
    private int _eliteShipSawFrame = -1;

    /// <summary>精英机贴图路径探针：直选型别 → 等生产波次放出精英 → 读该机的实际贴图与辉光遮罩路径。
    /// 详细的为什么与口径见本文件头。</summary>
    private void TickEliteShipProbe()
    {
        if (_eliteShipSawFrame >= 0)
        {
            return;
        }

        // 与其余长跑趟同口径注入无敌：无头局玩家不操作，中途阵亡会让刷怪整个停摆、
        // 精英波永远等不到。探针判的是贴图映射，与玩家状态无关。
        _player.SetInvincible(ProbeInvincibleSeconds);

        if (!_eliteShipRequested)
        {
            if (!_spawner.RequestEliteType(_eliteShipType))
            {
                _eliteShipProbe = false;
                GD.PushError(GdFormat.Format(
                    "[elite-ship-probe] 型别 %d 越界，直选请求被拒——开关取值应为 1..%d",
                    _eliteShipType, _spawner.ELITE_TYPES.Count));
                return;
            }

            _eliteShipRequested = true;
            return;
        }

        var expected = $"res://assets/sprites/elite_ship_{_eliteShipType}.png";
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is not Enemy enemy || !GodotObject.IsInstanceValid(enemy) || !enemy.IsActive() || !enemy.IsElite)
            {
                continue;
            }

            var sprite = enemy.GetNodeOrNull<Sprite2D>("Sprite2D");
            var path = sprite?.Texture?.ResourcePath ?? "";
            if (path != expected)
            {
                _eliteShipProbe = false;
                GD.PushError(GdFormat.Format(
                    "[elite-ship-probe] 第 %d 型精英的主贴图是 %s，期望 %s——映射写错时短局照绿",
                    _eliteShipType, path, expected));
                return;
            }

            // 辉光遮罩（GlowLayer）走同一份直接读：缺 `_glow.png` 时该层保留的是上一只的遮罩
            // （或场景默认），不报错、也不影响玩法——只有读实际贴图才判得到。
            var glow = sprite!.GetNodeOrNull<Sprite2D>(ShipEnergyFx.NodeName);
            var glowPath = glow?.Texture?.ResourcePath ?? "";
            var expectedGlow = $"res://assets/sprites/elite_ship_{_eliteShipType}_glow.png";
            if (glowPath != expectedGlow)
            {
                _eliteShipProbe = false;
                GD.PushError(GdFormat.Format(
                    "[elite-ship-probe] 第 %d 型精英的辉光遮罩是 %s，期望 %s——遮罩缺失时静默保留原图的遮罩",
                    _eliteShipType, glowPath, expectedGlow));
                return;
            }

            _eliteShipSawFrame = _frame;
            GD.Print(GdFormat.Format("[elite-ship-probe] 精英第 %d 型贴图与辉光路径成立", _eliteShipType));
            return;
        }

        if (_frame >= EliteShipDeadlineFrame)
        {
            _eliteShipProbe = false;
            GD.PushError(GdFormat.Format(
                "[elite-ship-probe] 到帧 %d 仍没有精英出场——生产波次表的间隔或 special gap 变了？"
                + "（本探针不缩短任何生产时长，也不改型别抽取之外的任何东西）",
                _frame));
        }
    }
}
#endif
