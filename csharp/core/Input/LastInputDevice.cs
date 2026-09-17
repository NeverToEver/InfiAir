namespace InfiAir.Core.Input;

/// <summary>提示标签的设备档位：键鼠档取键名，手柄档取按钮/扳机/摇杆标签。</summary>
public enum HintDevice
{
    KeyboardMouse,
    Gamepad,
}

/// <summary>
/// 最近使用的输入设备追踪（纯逻辑，零 Godot 依赖）：喂入「观察到哪类设备的事件」，
/// 最近者胜。为什么默认键鼠——无手柄连接时（桌面常态、无头探针亦然）键名是唯一可取的标签，
/// 默认手柄档会到处印按钮名。
///
/// 档位判定在此收口（可单测），事件→设备类的映射与标签拼装留在 godot 层；
/// 「最近者胜」是默认策略（无客观对错项，登记待确认）：混合使用时跟随最后一次操作的手。
/// </summary>
public sealed class LastInputDevice
{
    public HintDevice Current { get; private set; } = HintDevice.KeyboardMouse;

    /// <summary>喂入一次设备观察；返回档位是否切换（切换即提示行需要重渲染）。</summary>
    public bool Note(HintDevice observed)
    {
        if (observed == Current)
        {
            return false;
        }

        Current = observed;
        return true;
    }

    /// <summary>当前是否手柄档（取标签的分档口，避免调用方自带一份枚举比较）。</summary>
    public bool IsGamepad => Current == HintDevice.Gamepad;
}
