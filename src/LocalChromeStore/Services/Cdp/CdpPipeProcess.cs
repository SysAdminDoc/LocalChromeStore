using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LocalChromeStore.Services.Cdp;

/// <summary>
/// Builds the undocumented MSVCRT <c>lpReserved2</c> inherited-file-descriptor block that Windows
/// <c>CreateProcess</c> hands to a child's C runtime. Chrome launched with
/// <c>--remote-debugging-pipe</c> reads CDP from fd 3 and writes to fd 4 (via
/// <c>_get_osfhandle(3/4)</c>), and the ONLY way to populate a child's CRT fd table with specific OS
/// handles is this block — <see cref="System.Diagnostics.ProcessStartInfo"/> cannot express it. This
/// is the exact mechanism Node/libuv use for <c>stdio: ['ignore','ignore','ignore','pipe','pipe']</c>.
///
/// Block layout (little-endian):
/// <code>
///   int32   count
///   byte    flags[count]      // per-fd CRT flags: FOPEN|FPIPE for a pipe, 0 for an unused fd
///   HANDLE  handles[count]    // per-fd OS handle, sizeof(HANDLE) each (8 on x64, 4 on x86)
/// </code>
/// </summary>
internal static class MsvcrtStdioBlock
{
    public const byte FOPEN = 0x01;
    public const byte FPIPE = 0x08;
    public const byte FDEV = 0x40;

    /// <summary>CRT flags marking an fd as an open anonymous pipe.</summary>
    public const byte PipeFlags = FOPEN | FPIPE;

    /// <summary>The sentinel handle the CRT treats as "this fd is not present".</summary>
    public static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>Serializes the inheritance block for the given ordered fds (index 0 == fd 0).</summary>
    public static byte[] Build(IReadOnlyList<(byte flag, IntPtr handle)> fds)
    {
        var count = fds.Count;
        var handleSize = IntPtr.Size;
        var block = new byte[sizeof(int) + count + count * handleSize];

        BinaryPrimitives.WriteInt32LittleEndian(block, count);
        var flagsOffset = sizeof(int);
        var handlesOffset = flagsOffset + count;
        for (var i = 0; i < count; i++)
        {
            block[flagsOffset + i] = fds[i].flag;
            var value = fds[i].handle.ToInt64();
            for (var b = 0; b < handleSize; b++)
                block[handlesOffset + i * handleSize + b] = (byte)(value >> (8 * b));
        }
        return block;
    }
}

/// <summary>
/// Quotes an argv array into a single Windows command-line string per the
/// <c>CommandLineToArgvW</c> / MSVCRT parsing rules. Mirrors the .NET runtime's internal
/// <c>PasteArguments</c> so the hand-rolled <c>CreateProcess</c> launcher quotes paths with spaces,
/// quotes, and trailing backslashes identically to <c>ProcessStartInfo.ArgumentList</c>.
/// </summary>
internal static class WindowsCommandLine
{
    public static string Build(string exePath, IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        AppendArgument(sb, exePath);
        foreach (var a in args)
        {
            sb.Append(' ');
            AppendArgument(sb, a);
        }
        return sb.ToString();
    }

    private static void AppendArgument(StringBuilder sb, string argument)
    {
        if (argument.Length != 0 && !ContainsSpecial(argument))
        {
            sb.Append(argument);
            return;
        }

        sb.Append('"');
        var idx = 0;
        while (idx < argument.Length)
        {
            var c = argument[idx++];
            if (c == '\\')
            {
                var backslashes = 1;
                while (idx < argument.Length && argument[idx] == '\\') { backslashes++; idx++; }
                if (idx == argument.Length) sb.Append('\\', backslashes * 2);
                else if (argument[idx] == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); idx++; }
                else sb.Append('\\', backslashes);
            }
            else if (c == '"') { sb.Append('\\'); sb.Append('"'); }
            else sb.Append(c);
        }
        sb.Append('"');
    }

    private static bool ContainsSpecial(string s)
    {
        foreach (var c in s)
            if (c is ' ' or '\t' or '\n' or '\v' or '"') return true;
        return false;
    }
}

/// <summary>
/// Launches a Chromium browser for a <c>--remote-debugging-pipe</c> CDP session on Windows, mapping
/// two anonymous pipes onto the child's fd 3 (it reads our commands) and fd 4 (it writes responses)
/// via the MSVCRT inheritance block (<see cref="MsvcrtStdioBlock"/>). Only the two pipe handles are
/// inherited — a <c>STARTUPINFOEX</c> handle list restricts inheritance so no unrelated handle leaks
/// into the browser process.
///
/// <see cref="System.Diagnostics.Process"/>/<c>ProcessStartInfo</c> cannot do this (no fd mapping, no
/// handle-list scoping), which is why this is hand-rolled P/Invoke. <see cref="Writer"/> carries
/// commands to the browser; <see cref="Reader"/> carries responses back.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class CdpPipeProcess : IDisposable
{
    /// <summary>Stream we write CDP commands to; the child reads them on fd 3.</summary>
    public Stream Writer { get; }

    /// <summary>Stream we read CDP responses from; the child writes them on fd 4.</summary>
    public Stream Reader { get; }

    public int ProcessId { get; }

    private IntPtr _processHandle;
    private bool _disposed;

    private CdpPipeProcess(Stream writer, Stream reader, IntPtr processHandle, int processId)
    {
        Writer = writer;
        Reader = reader;
        _processHandle = processHandle;
        ProcessId = processId;
    }

    /// <summary>
    /// Starts <paramref name="exePath"/> with <paramref name="args"/> and the two CDP pipes wired to
    /// fd 3/4. Throws <see cref="Win32Exception"/> on any Win32 failure; the caller treats a failed
    /// launch as a non-fatal fall-back signal.
    /// </summary>
    public static CdpPipeProcess Launch(string exePath, IReadOnlyList<string> args)
    {
        // commands pipe: child reads (fd 3) <- we write the write end.
        // responses pipe: child writes (fd 4) -> we read the read end.
        var sa = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = 1
        };

        IntPtr cmdRead = default, cmdWrite = default, respRead = default, respWrite = default;
        var attrList = IntPtr.Zero;
        GCHandle pinnedBlock = default, pinnedHandles = default;
        var pinnedBlockSet = false;
        var pinnedHandlesSet = false;
        try
        {
            if (!CreatePipe(out cmdRead, out cmdWrite, ref sa, 0)) ThrowLastError("CreatePipe (commands)");
            if (!CreatePipe(out respRead, out respWrite, ref sa, 0)) ThrowLastError("CreatePipe (responses)");

            // Our own ends must NOT be inheritable, or the child would hold a duplicate that keeps
            // the pipe open after we close ours.
            if (!SetHandleInformation(cmdWrite, HANDLE_FLAG_INHERIT, 0)) ThrowLastError("SetHandleInformation (cmdWrite)");
            if (!SetHandleInformation(respRead, HANDLE_FLAG_INHERIT, 0)) ThrowLastError("SetHandleInformation (respRead)");

            var block = MsvcrtStdioBlock.Build(new (byte, IntPtr)[]
            {
                (0, MsvcrtStdioBlock.InvalidHandle),          // fd 0 (stdin)  — ignored
                (0, MsvcrtStdioBlock.InvalidHandle),          // fd 1 (stdout) — ignored
                (0, MsvcrtStdioBlock.InvalidHandle),          // fd 2 (stderr) — ignored
                (MsvcrtStdioBlock.PipeFlags, cmdRead),        // fd 3 — child reads our commands
                (MsvcrtStdioBlock.PipeFlags, respWrite),      // fd 4 — child writes its responses
            });
            pinnedBlock = GCHandle.Alloc(block, GCHandleType.Pinned);
            pinnedBlockSet = true;

            // Scope handle inheritance to exactly the two pipe ends the child needs.
            attrList = BuildHandleListAttribute(new[] { cmdRead, respWrite }, ref pinnedHandles);
            pinnedHandlesSet = true;

            var siEx = new STARTUPINFOEX();
            siEx.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            siEx.StartupInfo.cbReserved2 = (short)block.Length;
            siEx.StartupInfo.lpReserved2 = pinnedBlock.AddrOfPinnedObject();
            siEx.lpAttributeList = attrList;

            var cmdLine = new StringBuilder(WindowsCommandLine.Build(exePath, args));
            var ok = CreateProcess(
                exePath, cmdLine, IntPtr.Zero, IntPtr.Zero,
                bInheritHandles: true,
                dwCreationFlags: EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero, null, ref siEx, out var pi);
            if (!ok) ThrowLastError("CreateProcess");

            CloseHandle(pi.hThread);

            // Transfer ownership of our ends to FileStreams; the child owns copies of the other two,
            // so close ours.
            var writeSafe = new SafeFileHandle(cmdWrite, ownsHandle: true); cmdWrite = default;
            var readSafe = new SafeFileHandle(respRead, ownsHandle: true); respRead = default;
            var writer = new FileStream(writeSafe, FileAccess.Write);
            var reader = new FileStream(readSafe, FileAccess.Read);

            CloseHandle(cmdRead); cmdRead = default;
            CloseHandle(respWrite); respWrite = default;

            return new CdpPipeProcess(writer, reader, pi.hProcess, pi.dwProcessId);
        }
        catch
        {
            foreach (var h in new[] { cmdRead, cmdWrite, respRead, respWrite })
                if (h != default && h != MsvcrtStdioBlock.InvalidHandle) CloseHandle(h);
            throw;
        }
        finally
        {
            if (attrList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attrList);
                Marshal.FreeHGlobal(attrList);
            }
            if (pinnedHandlesSet) pinnedHandles.Free();
            if (pinnedBlockSet) pinnedBlock.Free();
        }
    }

    private static IntPtr BuildHandleListAttribute(IntPtr[] handles, ref GCHandle pinnedHandles)
    {
        var size = IntPtr.Zero;
        // First call sizes the buffer; it intentionally returns false with ERROR_INSUFFICIENT_BUFFER.
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var list = Marshal.AllocHGlobal(size);
        try
        {
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size))
                ThrowLastError("InitializeProcThreadAttributeList");

            pinnedHandles = GCHandle.Alloc(handles, GCHandleType.Pinned);
            if (!UpdateProcThreadAttribute(
                    list, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                    pinnedHandles.AddrOfPinnedObject(), (IntPtr)(IntPtr.Size * handles.Length),
                    IntPtr.Zero, IntPtr.Zero))
                ThrowLastError("UpdateProcThreadAttribute");
            return list;
        }
        catch
        {
            Marshal.FreeHGlobal(list);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Writer.Dispose(); } catch { /* best-effort */ }
        try { Reader.Dispose(); } catch { /* best-effort */ }
        if (_processHandle != IntPtr.Zero)
        {
            CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
    }

    private static void ThrowLastError(string what) =>
        throw new Win32Exception(Marshal.GetLastWin32Error(), $"{what} failed");

    private const uint HANDLE_FLAG_INHERIT = 0x1;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private static readonly IntPtr PROC_THREAD_ATTRIBUTE_HANDLE_LIST = new(0x00020002);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
}
