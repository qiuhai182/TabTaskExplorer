namespace TabTaskExplorer.Kernel.Capture;

/// <summary>截图取消来源：鼠标右键或 Esc，二者语义完全一致，均为直接退出截图。</summary>
public enum CaptureCancelSource
{
    /// <summary>没有收到取消请求。</summary>
    None,

    /// <summary>鼠标右键按下。</summary>
    RightButton,

    /// <summary>Esc 键按下。</summary>
    Escape
}

/// <summary>
/// 截图取消输入：在遮罩出现前（延迟等待、遮罩显示中、截图完成前）轮询全局按键，
/// 让全屏 / 活动窗口 / 上次区域 / 贴图截图等没有框选窗口的入口也能用右键或 Esc 立即取消。
/// </summary>
public interface ICaptureCancelInput
{
    /// <summary>当前平台是否支持全局取消轮询。</summary>
    bool IsSupported { get; }

    /// <summary>清除已经按下的按键状态，避免用右键菜单触发截图时被旧按键误取消。</summary>
    void Reset();

    /// <summary>读取一次取消请求；没有按下右键或 Esc 时返回 None。</summary>
    CaptureCancelSource PollCancelRequest();
}

/// <summary>不支持全局按键轮询的平台使用：永远返回没有取消请求。</summary>
public class NullCaptureCancelInput : ICaptureCancelInput
{
    /// <inheritdoc />
    public bool IsSupported
    {
        get { return false; }
    }

    /// <inheritdoc />
    public void Reset()
    {
    }

    /// <inheritdoc />
    public CaptureCancelSource PollCancelRequest()
    {
        return CaptureCancelSource.None;
    }
}
