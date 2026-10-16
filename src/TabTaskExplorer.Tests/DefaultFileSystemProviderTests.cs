using System;
using System.Collections.Generic;
using System.IO;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>DefaultFileSystemProvider 的目录浏览测试。</summary>
public class DefaultFileSystemProviderTests
{
    [Fact]
    public void ListDirectoryFindsCreatedFile()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            string filePath = Path.Combine(tempDirectory, "hello.txt");
            File.WriteAllText(filePath, "内容");

            DefaultFileSystemProvider provider = new DefaultFileSystemProvider();
            IReadOnlyList<FileEntry> entries = provider.ListDirectory(tempDirectory);

            Assert.Single(entries);
            Assert.Equal("hello.txt", entries[0].Name);
            Assert.False(entries[0].IsDirectory);
            Assert.Equal(6, entries[0].LengthBytes);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void ListDirectoryMarksSubDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string subDirectory = Path.Combine(tempDirectory, "子目录");
        Directory.CreateDirectory(subDirectory);
        try
        {
            DefaultFileSystemProvider provider = new DefaultFileSystemProvider();
            IReadOnlyList<FileEntry> entries = provider.ListDirectory(tempDirectory);

            Assert.Single(entries);
            Assert.Equal("子目录", entries[0].Name);
            Assert.True(entries[0].IsDirectory);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void ListDirectoryCanHideHiddenFiles()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string filePath = Path.Combine(tempDirectory, "隐藏文件.txt");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            File.WriteAllText(filePath, "隐藏内容");
            File.SetAttributes(filePath, FileAttributes.Hidden);

            DefaultFileSystemProvider provider = new DefaultFileSystemProvider();

            DirectoryListingOptions hideOptions = new DirectoryListingOptions();
            hideOptions.ShowHiddenFiles = false;
            Assert.Empty(provider.ListDirectory(tempDirectory, hideOptions));

            DirectoryListingOptions showOptions = new DirectoryListingOptions();
            showOptions.ShowHiddenFiles = true;
            Assert.Single(provider.ListDirectory(tempDirectory, showOptions));
        }
        finally
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
            Directory.Delete(tempDirectory, true);
        }
    }
}
