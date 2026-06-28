using System.Text;
using System.Text.Json;
using LocalChromeStore.Services.Cdp;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class CdpProtocolTests
{
    [Fact]
    public void BuildLoadUnpacked_ProducesCorrectMethodAndParams()
    {
        var cmd = CdpProtocol.BuildLoadUnpacked(7, @"C:\ext\one");
        Assert.Equal(7, cmd.Id);
        Assert.Equal("Extensions.loadUnpacked", cmd.Method);
        Assert.Equal(@"C:\ext\one", cmd.Params!["path"]);
    }

    [Fact]
    public void EncodeFrame_IsNulTerminatedLowercaseJson()
    {
        var frame = CdpProtocol.EncodeFrame(CdpProtocol.BuildLoadUnpacked(1, "/tmp/e"));
        Assert.Equal(CdpProtocol.MessageTerminator, frame[^1]);

        var json = Encoding.UTF8.GetString(frame, 0, frame.Length - 1);
        using var doc = JsonDocument.Parse(json);
        // CDP requires lowercase keys.
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Extensions.loadUnpacked", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal("/tmp/e", doc.RootElement.GetProperty("params").GetProperty("path").GetString());
    }

    [Fact]
    public void DecodeFrames_SplitsMultipleMessages_AndKeepsRemainder()
    {
        var buffer = Encoding.UTF8.GetBytes("{\"a\":1}\0{\"b\":2}\0{\"partial\"");
        var (messages, remainder) = CdpProtocol.DecodeFrames(buffer);

        Assert.Equal(2, messages.Count);
        Assert.Equal("{\"a\":1}", messages[0]);
        Assert.Equal("{\"b\":2}", messages[1]);
        Assert.Equal("{\"partial\"", Encoding.UTF8.GetString(remainder));
    }

    [Fact]
    public void DecodeFrames_NoTerminator_ReturnsAllAsRemainder()
    {
        var buffer = Encoding.UTF8.GetBytes("{\"x\":1}");
        var (messages, remainder) = CdpProtocol.DecodeFrames(buffer);
        Assert.Empty(messages);
        Assert.Equal("{\"x\":1}", Encoding.UTF8.GetString(remainder));
    }

    [Theory]
    [InlineData("{\"id\":5,\"result\":{\"id\":\"abcdefghijklmnopabcdefghijklmnop\"}}", 5, "abcdefghijklmnopabcdefghijklmnop", false, false)]
    [InlineData("{\"id\":5,\"result\":{}}", 5, null, false, false)]
    [InlineData("{\"id\":5,\"error\":{\"message\":\"boom\"}}", 5, null, true, false)]
    [InlineData("{\"method\":\"Target.attached\",\"params\":{}}", null, null, false, true)]
    public void ParseResponse_IdentifiesRepliesEventsExtensionIdsAndErrors(string json, int? id, string? extensionId, bool isError, bool isEvent)
    {
        var resp = CdpProtocol.ParseResponse(json);
        Assert.Equal(id, resp.Id);
        Assert.Equal(extensionId, resp.ExtensionId);
        Assert.Equal(isError, resp.IsError);
        Assert.Equal(isEvent, resp.IsEvent);
    }

    [Fact]
    public void RequiredLaunchFlags_IncludePipeAndUnsafeDebugging()
    {
        Assert.Contains(CdpProtocol.RemoteDebuggingPipeFlag, CdpProtocol.RequiredLaunchFlags);
        Assert.Contains(CdpProtocol.EnableUnsafeExtensionDebuggingFlag, CdpProtocol.RequiredLaunchFlags);
    }

    [Fact]
    public void EncodeFrame_RoundTripsThroughDecode()
    {
        var f1 = CdpProtocol.EncodeFrame(CdpProtocol.BuildLoadUnpacked(1, "/a"));
        var f2 = CdpProtocol.EncodeFrame(CdpProtocol.BuildLoadUnpacked(2, "/b"));
        var stream = new byte[f1.Length + f2.Length];
        Array.Copy(f1, stream, f1.Length);
        Array.Copy(f2, 0, stream, f1.Length, f2.Length);

        var (messages, remainder) = CdpProtocol.DecodeFrames(stream);
        Assert.Equal(2, messages.Count);
        Assert.Empty(remainder);
        Assert.Equal(1, CdpProtocol.ParseResponse(messages[0]).Id);
        Assert.Equal(2, CdpProtocol.ParseResponse(messages[1]).Id);
    }
}
