using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TabTaskExplorer.App.Services;

/// <summary>一次增量读取的结果：本次读到的完整行、是否已到文件末尾、文件是否被截断或替换。</summary>
public sealed class LogChunk
{
    /// <summary>本次读到的完整日志行（不含换行符）。</summary>
    public List<string> Lines { get; } = new List<string>();

    /// <summary>是否已经读到当前文件末尾。</summary>
    public bool ReachedEnd { get; set; }

    /// <summary>文件长度小于上次读取位置，说明日志被裁剪或替换，调用方应清空旧行重新加载。</summary>
    public bool ResetDueToTruncation { get; set; }
}

/// <summary>
/// 日志文件增量读取器：记录上次读取位置，按块读取完整行，支持查看器不阻塞界面地加载大文件。
/// 只读打开文件句柄，不阻止日志写入与裁剪。
/// </summary>
public sealed class LogFileTailReader
{
    private readonly string filePath;
    private long offset;
    private bool skipPartialLine;

    public LogFileTailReader(string filePath)
    {
        this.filePath = filePath;
    }

    /// <summary>当前读取位置（字节）。</summary>
    public long Offset
    {
        get { return this.offset; }
    }

    /// <summary>日志文件路径。</summary>
    public string FilePath
    {
        get { return this.filePath; }
    }

    /// <summary>
    /// 定位到文件尾部附近：只加载最近的一段，避免打开上百兆文件时一次性读入内存。
    /// 起点落在半行中间时，后续读取会先跳过残行。
    /// </summary>
    public void SeekToTail(long maxBytes)
    {
        this.offset = 0;
        this.skipPartialLine = false;
        try
        {
            if (!File.Exists(this.filePath))
            {
                return;
            }
            long length = new FileInfo(this.filePath).Length;
            long start = length > maxBytes ? length - maxBytes : 0;
            this.offset = start;
            this.skipPartialLine = start > 0;
        }
        catch
        {
            // 文件信息读取失败时从文件开头读取，后续读取内部仍会吞掉异常
        }
    }

    /// <summary>
    /// 读取下一块完整行：最多读取指定字节数，最多返回指定行数；末尾不完整的半行留到下次读取。
    /// </summary>
    public LogChunk ReadChunk(int maxBytes, int maxLines)
    {
        LogChunk chunk = new LogChunk();
        try
        {
            if (!File.Exists(this.filePath))
            {
                chunk.ReachedEnd = true;
                return chunk;
            }
            long length = new FileInfo(this.filePath).Length;
            if (length < this.offset)
            {
                // 文件被裁剪或替换：从头开始，并通知调用方清空旧行
                this.offset = 0;
                this.skipPartialLine = false;
                chunk.ResetDueToTruncation = true;
            }
            if (length <= this.offset)
            {
                chunk.ReachedEnd = true;
                return chunk;
            }

            int readSize = (int)Math.Min(maxBytes, length - this.offset);
            byte[] buffer = new byte[readSize];
            int totalRead = 0;
            using (FileStream stream = new FileStream(this.filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Seek(this.offset, SeekOrigin.Begin);
                while (totalRead < readSize)
                {
                    int read = stream.Read(buffer, totalRead, readSize - totalRead);
                    if (read <= 0)
                    {
                        break;
                    }
                    totalRead = totalRead + read;
                }
            }
            if (totalRead <= 0)
            {
                chunk.ReachedEnd = true;
                return chunk;
            }

            int start = 0;
            if (this.skipPartialLine)
            {
                // 起点是半行中间：找第一个换行符，从下一行开始解析
                int newlineIndex = Array.IndexOf(buffer, (byte)'\n', 0, totalRead);
                if (newlineIndex < 0)
                {
                    // 整块都是残行，整块跳过，下次继续找换行符
                    this.offset = this.offset + totalRead;
                    chunk.ReachedEnd = this.offset >= length;
                    return chunk;
                }
                start = newlineIndex + 1;
                this.skipPartialLine = false;
            }

            if (start < totalRead && buffer[start] == (byte)'\r')
            {
                start = start + 1;
            }

            int end = -1;
            int lineCount = 0;
            for (int index = start; index < totalRead; index++)
            {
                if (buffer[index] != (byte)'\n')
                {
                    continue;
                }
                lineCount = lineCount + 1;
                end = index + 1;
                if (lineCount >= maxLines)
                {
                    break;
                }
            }
            if (end < 0)
            {
                // 还没读到完整的一行：不推进位置，等下一轮把剩余字节一起读入
                chunk.ReachedEnd = true;
                return chunk;
            }

            string text = Encoding.UTF8.GetString(buffer, start, end - start);
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string line in lines)
            {
                if (line.Length > 0)
                {
                    chunk.Lines.Add(line);
                }
            }
            this.offset = this.offset + end;
            chunk.ReachedEnd = this.offset >= length;
        }
        catch
        {
            // 文件被删除、被占用或编码损坏时按读到末尾处理，查看器继续轮询
            chunk.ReachedEnd = true;
        }
        return chunk;
    }
}
