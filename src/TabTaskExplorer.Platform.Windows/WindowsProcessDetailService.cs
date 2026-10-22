using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 平台的进程详情与调节服务：
/// 详情按需查询（可执行路径、命令行、启动时间、用户、优先级、CPU 亲和性），
/// 优先级与亲和性按需设置。参考 Process Explorer 的实现方式（NtQueryInformationProcess 读命令行）。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsProcessDetailService : IProcessDetailService
{
    /// <summary>Windows 平台支持此能力。</summary>
    public bool IsSupported
    {
        get { return true; }
    }

    /// <summary>查询指定进程的详情；进程不存在或读取失败时返回 null。</summary>
    public ProcessDetail? GetDetail(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            ProcessDetail detail = new ProcessDetail();
            detail.ProcessId = processId;

            try
            {
                detail.ExecutablePath = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                // 无权读取模块信息的系统进程：路径留空
                detail.ExecutablePath = string.Empty;
            }

            detail.CommandLine = ReadCommandLine(process);

            try
            {
                detail.StartTimeText = process.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                // 进程已退出或无权读取：启动时间留空
                detail.StartTimeText = string.Empty;
            }

            try
            {
                detail.UserName = ReadUserName(process);
            }
            catch
            {
                // 无权读取进程所有者：用户名留空
                detail.UserName = string.Empty;
            }

            try
            {
                detail.PriorityLevel = ToPriorityLevel(process.PriorityClass);
            }
            catch
            {
                // 无权读取优先级：保持 null
                detail.PriorityLevel = null;
            }

            try
            {
                detail.AffinityMask = process.ProcessorAffinity.ToInt64();
            }
            catch
            {
                // 无权读取亲和性：标记为不支持
                detail.AffinityMask = -1;
            }

            return detail;
        }
        catch
        {
            // 进程不存在或已被拒绝访问
            return null;
        }
    }

    /// <summary>设置指定进程的优先级级别；失败返回 false。</summary>
    public bool SetPriorityLevel(int processId, ProcessPriorityLevel level)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            ProcessPriorityClass? priorityClass = ToPriorityClass(level);
            if (priorityClass == null)
            {
                return false;
            }
            process.PriorityClass = priorityClass.Value;
            return true;
        }
        catch
        {
            // 进程已退出、权限不足或优先级非法（如实时优先级需要特殊权限）
            return false;
        }
    }

    /// <summary>设置指定进程的 CPU 亲和性位掩码；失败返回 false。</summary>
    public bool SetProcessorAffinity(int processId, long affinityMask)
    {
        if (affinityMask == -1 || affinityMask == 0)
        {
            // 掩码无效（-1 表示不支持，0 表示没有任何核心）
            return false;
        }
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.ProcessorAffinity = new IntPtr(affinityMask);
            return true;
        }
        catch
        {
            // 进程已退出、权限不足或掩码包含不允许使用的处理器
            return false;
        }
    }

    /// <summary>把系统优先级类别映射为内核优先级级别；未知类别返回 null。</summary>
    private static ProcessPriorityLevel? ToPriorityLevel(ProcessPriorityClass priorityClass)
    {
        if (priorityClass == ProcessPriorityClass.Idle)
        {
            return ProcessPriorityLevel.Idle;
        }
        if (priorityClass == ProcessPriorityClass.BelowNormal)
        {
            return ProcessPriorityLevel.BelowNormal;
        }
        if (priorityClass == ProcessPriorityClass.Normal)
        {
            return ProcessPriorityLevel.Normal;
        }
        if (priorityClass == ProcessPriorityClass.AboveNormal)
        {
            return ProcessPriorityLevel.AboveNormal;
        }
        if (priorityClass == ProcessPriorityClass.High)
        {
            return ProcessPriorityLevel.High;
        }
        if (priorityClass == ProcessPriorityClass.RealTime)
        {
            return ProcessPriorityLevel.Realtime;
        }
        return null;
    }

    /// <summary>把内核优先级级别映射回系统优先级类别；未知级别返回 null。</summary>
    private static ProcessPriorityClass? ToPriorityClass(ProcessPriorityLevel level)
    {
        if (level == ProcessPriorityLevel.Idle)
        {
            return ProcessPriorityClass.Idle;
        }
        if (level == ProcessPriorityLevel.BelowNormal)
        {
            return ProcessPriorityClass.BelowNormal;
        }
        if (level == ProcessPriorityLevel.Normal)
        {
            return ProcessPriorityClass.Normal;
        }
        if (level == ProcessPriorityLevel.AboveNormal)
        {
            return ProcessPriorityClass.AboveNormal;
        }
        if (level == ProcessPriorityLevel.High)
        {
            return ProcessPriorityClass.High;
        }
        if (level == ProcessPriorityLevel.Realtime)
        {
            return ProcessPriorityClass.RealTime;
        }
        return null;
    }

    /// <summary>
    /// 读取进程所有者（域名\用户名）：打开进程令牌后从 WindowsIdentity 读取。
    /// 读取失败返回空字符串。
    /// </summary>
    private static string ReadUserName(Process process)
    {
        IntPtr tokenHandle = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process.Handle, TokenQuery, out tokenHandle))
            {
                return string.Empty;
            }
            using (System.Security.Principal.WindowsIdentity identity = new System.Security.Principal.WindowsIdentity(tokenHandle))
            {
                return identity.Name ?? string.Empty;
            }
        }
        catch
        {
            // 进程已退出或无权读取令牌：用户名留空
            return string.Empty;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
            {
                CloseHandle(tokenHandle);
            }
        }
    }

    /// <summary>读取进程启动命令行：使用 NtQueryInformationProcess 的 ProcessCommandLineInformation（信息类 60）。
    /// 读取失败返回空字符串。
    /// </summary>
    private static string ReadCommandLine(Process process)
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            int bufferLength = 1024;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                buffer = Marshal.AllocHGlobal(bufferLength);
                int returnLength;
                int status = NtQueryInformationProcess(
                    process.Handle,
                    ProcessCommandLineInformation,
                    buffer,
                    bufferLength,
                    out returnLength);
                if (status == 0)
                {
                    // 缓冲区开头是 UNICODE_STRING：Length（字节）+ MaximumLength + Buffer 指针
                    int length = Marshal.ReadInt16(buffer, 0);
                    int bufferPointerOffset = IntPtr.Size == 8 ? 8 : 4;
                    IntPtr stringBuffer = Marshal.ReadIntPtr(buffer, bufferPointerOffset);
                    if (length <= 0 || stringBuffer == IntPtr.Zero)
                    {
                        return string.Empty;
                    }
                    return Marshal.PtrToStringUni(stringBuffer, length / 2) ?? string.Empty;
                }
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                if (status != StatusInfoLengthMismatch || returnLength <= bufferLength)
                {
                    // 数据不足或权限不足：不再重试
                    return string.Empty;
                }
                bufferLength = returnLength + 256;
            }
            return string.Empty;
        }
        catch
        {
            // 进程已退出或无权查询：命令行留空
            return string.Empty;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>NtQueryInformationProcess 的信息类编号：ProcessCommandLineInformation（60）。</summary>
    private const int ProcessCommandLineInformation = 60;

    /// <summary>打开令牌的访问权限：查询权限（读取用户需要）。</summary>
    private const uint TokenQuery = 0x0008;

    /// <summary>查询进程令牌（读取进程所有者使用）。</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    /// <summary>关闭内核对象句柄。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>NtQueryInformationProcess 返回值：信息长度不匹配，需要更大的缓冲区。</summary>
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>查询进程信息（读取命令行使用）。</summary>
    [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        out int returnLength);
}
