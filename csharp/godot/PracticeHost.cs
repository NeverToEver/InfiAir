using Godot;
using InfiAir.Core.Practice;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 练习场景宿主（<c>scenes/practice.tscn</c> = 本节点 + 内嵌 main.tscn，与探针宿主同形）：
/// 练习局的入口、直选内容的请求方与门槛分补足方。它是**生产节点**——不读任何测试开关
/// （AGENTS §5：测试设施不进生产路径），练习局的判定全在 core <see cref="PracticeSetup"/>。
///
/// 三件事，按生命周期分：
/// 1) _EnterTree（早于所有子节点 _Ready）：消费待开始的设置 → <c>GameState.BeginPractice</c>
///    （置练习态 + 应用起始难度档）→ 通知 Main 走练习分支；
///    起始难度必须在此应用——HUD 在自己 _Ready 里读一次档位标签，晚于它就只能等下一次难度信号。
/// 2) _Ready（晚于 Main._Ready：遭遇已注册、分数门槛可读）：按所选内容补门槛分。
/// 3) _Process：等玩家入场演出结束，走生产链请求所选 Boss / 遭遇 / 迷雾；被拒即逐帧重试，
///    受理后立刻停 _Process（练习局其余部分全由生产链驱动，本节点不再空转）。
///
/// 返回键不在这里处理：本场景复用 main.tscn，Esc/右键由既有 BackNavigator 收口
/// （练习态下判为「回标题屏」，见 BackNavigator.DecideBackAction）——再写一份自处理会双响应。
/// </summary>
public partial class PracticeHost : Node
{
    private Main? _main;
    private PracticeDriver? _driver;
    private PracticeSetup _setup = PracticeSetup.Default;

    public override void _EnterTree()
    {
        _main = GetNodeOrNull<Main>("Main");
        if (_main == null)
        {
            GD.PushError("[practice] 场景缺少子节点 Main——练习局无法启动（取不到判据，不静默继续）");
            return;
        }

        _setup = GameState.Instance.ConsumePendingPractice();
        GameState.Instance.BeginPractice(_setup);
        _main.MarkPracticeRun();
    }

    public override void _Ready()
    {
        if (_main == null)
        {
            return;
        }

        var player = _main.GetNode<Player>("Player");
        var spawner = _main.GetNode<Spawner>("Spawner");
        _driver = new PracticeDriver(_setup, player, spawner, GameState.Instance.Events);
        _driver.SeedScore();
    }

    public override void _Process(double delta)
    {
        if (_driver == null)
        {
            return;
        }

        if (_driver.Tick())
        {
            SetProcess(false); // 请求均已受理：本节点不再逐帧空转（练习局其余部分由生产链驱动）
        }
    }
}
