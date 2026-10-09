using Chitra.Application.Abstractions;
using Chitra.Infrastructure.Media;
using Chitra.Infrastructure.SystemInfo;

namespace Chitra.Tests.Unit;

public class FfmpegArgsTests
{
    private static readonly CompositionClip[] Clips =
    [
        new("a.mp4", false, 5, null, "First line"),
        new("b.png", true, 4, "n.wav", null),
        new("c.mp4", false, 6, null, "Third line"),
    ];

    [Fact]
    public void TransitionArgs_ComputeCumulativeOffsets()
    {
        var args = FfmpegService.BuildTransitionArgs(["seg_0.mp4", "seg_1.mp4", "seg_2.mp4"], Clips, "fade", 0.5, "out.mp4");
        var filter = args[args.IndexOf("-filter_complex") + 1];

        Assert.Contains("[0:v][1:v]xfade=transition=fade:duration=0.5:offset=4.5[v1]", filter);
        Assert.Contains("[v1][2:v]xfade=transition=fade:duration=0.5:offset=8[v2]", filter);
        Assert.Contains("[a1][2:a]acrossfade=d=0.5[a2]", filter);
        Assert.Equal("[v2]", args[args.IndexOf("-map") + 1]);
    }

    [Fact]
    public void Srt_AccountsForTransitions_AndSkipsEmptyText()
    {
        var srt = FfmpegService.BuildSrt(Clips, 0.5);

        Assert.Contains("1\r\n00:00:00,000 --> 00:00:04,500\r\nFirst line".Replace("\r\n", Environment.NewLine), srt);
        Assert.Contains("00:00:08,000 --> 00:00:14,000", srt);
        Assert.DoesNotContain("00:00:04,500 -->", srt);
    }

    [Fact]
    public void SegmentArgs_LoopStillImages_AndUseNarration()
    {
        var request = new VideoCompositionRequest { Clips = Clips, OutputPath = "out.mp4", Width = 1280, Height = 720, Fps = 24 };
        var image = FfmpegService.BuildSegmentArgs(Clips[1], "seg.mp4", request);
        Assert.Equal(["-loop", "1", "-framerate", "24", "-t", "4", "-i", "b.png", "-i", "n.wav"], image.Take(10));

        var video = FfmpegService.BuildSegmentArgs(Clips[0], "seg.mp4", request);
        Assert.Contains("anullsrc=channel_layout=stereo:sample_rate=44100", video);
        Assert.Contains(video, a => a.Contains("tpad=stop_mode=clone:stop_duration=5"));
    }

    [Fact]
    public void NvidiaSmiOutput_IsParsed()
    {
        var gpus = SystemInfoService.ParseNvidiaSmi("NVIDIA GeForce RTX 3060, 12288, 11020, 560.94\n");
        var gpu = Assert.Single(gpus);
        Assert.Equal("NVIDIA GeForce RTX 3060", gpu.Name);
        Assert.Equal(12288, gpu.VramTotalMb);
        Assert.Equal(11020, gpu.VramFreeMb);
    }
}
