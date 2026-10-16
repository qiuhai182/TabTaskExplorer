using System;
using System.Collections.Generic;
using System.IO;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Storage;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>JsonTaskStore 的读写回环测试。</summary>
public class JsonTaskStoreTests
{
    [Fact]
    public void SaveThenLoadKeepsTaskFields()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-tasks.json");
        try
        {
            JsonTaskStore store = new JsonTaskStore(tempFile);

            TaskItem first = new TaskItem();
            first.Title = "写第一版内核接口";
            first.Description = "覆盖文件、进程、指标、任务四类接口";
            first.Priority = TaskPriority.High;
            first.State = TaskState.InProgress;
            first.Tags.Add("内核");

            TaskItem second = new TaskItem();
            second.Title = "设计任务看板界面";
            second.State = TaskState.Todo;

            List<TaskItem> tasks = new List<TaskItem>();
            tasks.Add(first);
            tasks.Add(second);
            store.SaveAll(tasks);

            IReadOnlyList<TaskItem> loaded = store.LoadAll();

            Assert.Equal(2, loaded.Count);
            Assert.Equal("写第一版内核接口", loaded[0].Title);
            Assert.Equal(TaskPriority.High, loaded[0].Priority);
            Assert.Equal(TaskState.InProgress, loaded[0].State);
            Assert.Equal("内核", loaded[0].Tags[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadReturnsEmptyWhenFileMissing()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-missing.json");
        JsonTaskStore store = new JsonTaskStore(tempFile);

        IReadOnlyList<TaskItem> loaded = store.LoadAll();

        Assert.Empty(loaded);
    }
}
