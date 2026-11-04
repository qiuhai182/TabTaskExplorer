using System.Runtime.CompilerServices;

// 测试工程需要访问日志写入器的测试注入点（时间源、目录覆盖与大小阈值覆盖），
// 这些成员只在进程内可见，不改变生产行为。
[assembly: InternalsVisibleTo("TabTaskExplorer.Tests")]
