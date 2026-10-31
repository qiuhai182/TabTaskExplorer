using System.Collections.Generic;
using TabTaskExplorer.Kernel.Media;
using Xunit;

namespace TabTaskExplorer.Tests;

/// <summary>媒体格式分类测试：扩展名判定、可播放判定与界面文本。</summary>
public class MediaFormatCatalogTests
{
    /// <summary>常见图片扩展名分类为图片。</summary>
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    [InlineData("shot.png")]
    [InlineData("anim.gif")]
    [InlineData("pic.webp")]
    [InlineData("icon.ico")]
    public void Classify_ImageExtensions_ReturnsImage(string fileName)
    {
        Assert.Equal(MediaKind.Image, MediaFormatCatalog.Classify(fileName));
    }

    /// <summary>常见视频扩展名分类为视频。</summary>
    [Theory]
    [InlineData("movie.mp4")]
    [InlineData("movie.MKV")]
    [InlineData("clip.avi")]
    [InlineData("show.ts")]
    [InlineData("clip.webm")]
    public void Classify_VideoExtensions_ReturnsVideo(string fileName)
    {
        Assert.Equal(MediaKind.Video, MediaFormatCatalog.Classify(fileName));
    }

    /// <summary>常见音频扩展名分类为音频。</summary>
    [Theory]
    [InlineData("song.mp3")]
    [InlineData("song.FLAC")]
    [InlineData("voice.wav")]
    [InlineData("track.m4a")]
    [InlineData("track.ape")]
    public void Classify_AudioExtensions_ReturnsAudio(string fileName)
    {
        Assert.Equal(MediaKind.Audio, MediaFormatCatalog.Classify(fileName));
    }

    /// <summary>非媒体文件与无扩展名文件分类为未知。</summary>
    [Theory]
    [InlineData("readme.txt")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    [InlineData("")]
    public void Classify_NonMedia_ReturnsUnknown(string fileName)
    {
        Assert.Equal(MediaKind.Unknown, MediaFormatCatalog.Classify(fileName));
    }

    /// <summary>已登记但后端暂不支持的图片格式：分类仍是图片，但不交给内置查看器。</summary>
    [Theory]
    [InlineData("vector.svg")]
    [InlineData("photo.heic")]
    [InlineData("raw.cr2")]
    public void UnsupportedImage_ClassifiedAsImageButNotPlayable(string fileName)
    {
        Assert.Equal(MediaKind.Image, MediaFormatCatalog.Classify(fileName));
        Assert.False(MediaFormatCatalog.IsPlayable(fileName));
    }

    /// <summary>可播放格式判定：三类媒体为真，其他为假。</summary>
    [Fact]
    public void IsPlayable_ReturnsTrueOnlyForSupportedMedia()
    {
        Assert.True(MediaFormatCatalog.IsPlayable("a.jpg"));
        Assert.True(MediaFormatCatalog.IsPlayable("a.mp4"));
        Assert.True(MediaFormatCatalog.IsPlayable("a.mp3"));
        Assert.False(MediaFormatCatalog.IsPlayable("a.txt"));
        Assert.False(MediaFormatCatalog.IsPlayable("a.svg"));
    }

    /// <summary>动图格式识别：gif / webp / apng 为真。</summary>
    [Theory]
    [InlineData("a.gif", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.apng", true)]
    [InlineData("a.jpg", false)]
    public void IsAnimatedImage_DetectsAnimatedFormats(string fileName, bool expected)
    {
        Assert.Equal(expected, MediaFormatCatalog.IsAnimatedImage(fileName));
    }

    /// <summary>类型文本：扩展名大写 + 中文大类。</summary>
    [Fact]
    public void DescribeType_BuildsReadableText()
    {
        Assert.Equal("JPEG 图片", MediaFormatCatalog.DescribeType("photo.jpeg"));
        Assert.Equal("MP4 视频", MediaFormatCatalog.DescribeType("movie.mp4"));
        Assert.Equal("MP3 音频", MediaFormatCatalog.DescribeType("song.mp3"));
        Assert.Equal("文件", MediaFormatCatalog.DescribeType("readme.txt"));
    }

    /// <summary>自然排序：数字段按数值比较，img2 排在 img10 之前。</summary>
    [Fact]
    public void NaturalComparer_OrdersNumericSegmentsByValue()
    {
        List<string> names = new List<string> { "img10.png", "img2.png", "img1.png" };
        names.Sort(MediaFormatCatalog.NaturalComparer);

        Assert.Equal(new string[] { "img1.png", "img2.png", "img10.png" }, names);
    }

    /// <summary>自然排序：大小写不敏感，且数字段优先于字母段。</summary>
    [Fact]
    public void NaturalComparer_IsCaseInsensitiveAndPrefersDigits()
    {
        List<string> names = new List<string> { "B.mp4", "a.mp4", "1.mp4" };
        names.Sort(MediaFormatCatalog.NaturalComparer);

        Assert.Equal(new string[] { "1.mp4", "a.mp4", "B.mp4" }, names);
    }
}
