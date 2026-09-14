using Godot;

namespace InfiAir;

/// <summary>
/// 手感域驱动节点：以 `ProcessMode = Always` 每帧推进顿帧与屏幕震动 trauma。
///
/// **存在理由**：手感推进原先挂在 `GameState._Process`（Pausable）上。顿帧期间按 Esc 会让
/// 树暂停 → 推进停摆 → `Engine.TimeScale` 停在冻结值，而暂停菜单/结算页都是 Always 节点，
/// 于是整份 UI 以 6% 速度播放（轮盘滑入 0.5s 变约 8s）；trauma 同样不再衰减，镜头在暂停菜单
/// 背后持续抖动。手感是「按真实时间流逝」的现象，不该受本局暂停影响。
///
/// 本节点作为 GameState 的子节点创建（autoload 场景无关），故教程等没有相机/主场景的场景
/// 也照常推进；帧长用 <see cref="GameFeelService.RealDelta"/> 从缩放后 delta 反解，保证
/// 顿帧自身压低时间缩放时剩余时长照常走完。
/// </summary>
public partial class GameFeelDriver : Node
{
    private GameFeelService _service = null!;

    /// <summary>绑定手感域（GameState._Ready 创建子节点后调用）。</summary>
    public void Bind(GameFeelService service) => _service = service;

    public override void _Process(double delta)
    {
        _service.Tick(_service.RealDelta(delta));
    }
}
