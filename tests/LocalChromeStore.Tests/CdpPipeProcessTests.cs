using System.Buffers.Binary;
using LocalChromeStore.Services.Cdp;
using Xunit;

namespace LocalChromeStore.Tests;

/// <summary>
/// Covers the deterministic, host-independent pieces of the Windows <c>--remote-debugging-pipe</c>
/// handshake: the MSVCRT fd-inheritance block layout and argv quoting. The actual
/// <see cref="CdpPipeProcess.Launch"/> spawns a process, so it needs a live browser host and is
/// exercised separately (end-to-end verification is blocked on branded Chrome 142+).
/// </summary>
public sealed class CdpPipeProcessTests
{
    [Fact]
    public void StdioBlock_HasCountThenFlagsThenHandles()
    {
        var fds = new (byte, IntPtr)[]
        {
            (0, MsvcrtStdioBlock.InvalidHandle),                 // fd 0
            (MsvcrtStdioBlock.PipeFlags, new IntPtr(0x1234)),    // fd 3-style pipe
            (MsvcrtStdioBlock.PipeFlags, new IntPtr(0x5678)),    // fd 4-style pipe
        };

        var block = MsvcrtStdioBlock.Build(fds);
        var hs = IntPtr.Size;

        Assert.Equal(sizeof(int) + 3 + 3 * hs, block.Length);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(block));

        // Flags region directly follows the count.
        Assert.Equal(0x00, block[4]);
        Assert.Equal(MsvcrtStdioBlock.PipeFlags, block[5]);
        Assert.Equal(MsvcrtStdioBlock.PipeFlags, block[6]);

        // Handle region follows the flags, one HANDLE-sized slot per fd.
        var hoff = sizeof(int) + 3;
        Assert.Equal(-1L, ReadHandle(block, hoff, hs));            // INVALID_HANDLE_VALUE
        Assert.Equal(0x1234L, ReadHandle(block, hoff + hs, hs));
        Assert.Equal(0x5678L, ReadHandle(block, hoff + 2 * hs, hs));
    }

    [Fact]
    public void StdioBlock_PipeFlags_AreFopenAndFpipe()
    {
        Assert.Equal(MsvcrtStdioBlock.FOPEN | MsvcrtStdioBlock.FPIPE, MsvcrtStdioBlock.PipeFlags);
        Assert.Equal(0x09, MsvcrtStdioBlock.PipeFlags);
    }

    private static long ReadHandle(byte[] b, int off, int size)
    {
        long v = 0;
        for (var i = 0; i < size; i++) v |= (long)b[off + i] << (8 * i);
        return size == 4 ? (int)v : v; // sign-extend the 32-bit case so INVALID stays -1
    }

    [Theory]
    // Plain flags need no quoting.
    [InlineData("chrome.exe", new[] { "--remote-debugging-pipe", "--enable-unsafe-extension-debugging" },
        "chrome.exe --remote-debugging-pipe --enable-unsafe-extension-debugging")]
    // An exe path with spaces is quoted.
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", new string[0],
        "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"")]
    // An arg value with spaces is quoted as a whole.
    [InlineData("x", new[] { @"--user-data-dir=C:\Users\a b\p" },
        "x \"--user-data-dir=C:\\Users\\a b\\p\"")]
    // Empty argument becomes "".
    [InlineData("x", new[] { "" }, "x \"\"")]
    // Embedded double-quote is backslash-escaped.
    [InlineData("x", new[] { "a\"b" }, "x \"a\\\"b\"")]
    // Trailing backslashes are doubled before the closing quote.
    [InlineData("x", new[] { @"c:\a b\" }, "x \"c:\\a b\\\\\"")]
    public void WindowsCommandLine_QuotesPerArgvRules(string exe, string[] args, string expected)
    {
        Assert.Equal(expected, WindowsCommandLine.Build(exe, args));
    }
}
