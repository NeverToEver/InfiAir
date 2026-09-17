using InfiAir.Core.Input;
using Xunit;

namespace InfiAir.Core.Tests.Input;

/// <summary>最近输入设备追踪的判定测试：默认键鼠、最近者胜、切换信号（提示行据此重渲染）。</summary>
public sealed class LastInputDeviceTests
{
    [Fact]
    public void DefaultsToKeyboardMouse()
    {
        var tracker = new LastInputDevice();
        Assert.Equal(HintDevice.KeyboardMouse, tracker.Current);
        Assert.False(tracker.IsGamepad);
    }

    [Fact]
    public void LastObservedDeviceWins()
    {
        var tracker = new LastInputDevice();
        Assert.True(tracker.Note(HintDevice.Gamepad)); // 键鼠 → 手柄：切换
        Assert.True(tracker.IsGamepad);
        Assert.False(tracker.Note(HintDevice.Gamepad)); // 同档重复喂入：不切换（不重复通知）
        Assert.True(tracker.Note(HintDevice.KeyboardMouse)); // 手柄 → 键鼠：切回
        Assert.False(tracker.IsGamepad);
    }

    [Fact]
    public void SwitchSignalIsExactOnTransition()
    {
        // 只有档位真的变了才返回 true：重渲染的订阅方靠它避免同档事件触发的无谓刷新
        var tracker = new LastInputDevice();
        Assert.False(tracker.Note(HintDevice.KeyboardMouse));
        Assert.True(tracker.Note(HintDevice.Gamepad));
        Assert.False(tracker.Note(HintDevice.Gamepad));
        Assert.True(tracker.Note(HintDevice.KeyboardMouse));
        Assert.Equal(HintDevice.KeyboardMouse, tracker.Current);
    }
}
