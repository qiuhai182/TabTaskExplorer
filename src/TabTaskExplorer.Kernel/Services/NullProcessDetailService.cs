using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 非 Windows 平台的留空实现：进程详情查询、优先级与亲和性调节全部不可用。
/// 这些是 Windows 特有能力（进程令牌、优先级类别、亲和性掩码），其他平台此能力留空。
/// </summary>
public class NullProcessDetailService : IProcessDetailService
{
    public bool IsSupported
    {
        get { return false; }
    }

    public ProcessDetail? GetDetail(int processId)
    {
        // 留空：非 Windows 平台不提供进程详情
        return null;
    }

    public bool SetPriorityLevel(int processId, ProcessPriorityLevel level)
    {
        // 留空：非 Windows 平台不提供优先级调节
        return false;
    }

    public bool SetProcessorAffinity(int processId, long affinityMask)
    {
        // 留空：非 Windows 平台不提供亲和性调节
        return false;
    }
}
