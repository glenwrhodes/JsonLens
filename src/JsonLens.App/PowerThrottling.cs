using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JsonLens.App;

/// <summary>
/// Windows 11 "EcoQoS" power throttling moves processes it considers background work to slower,
/// power-saving cores. Measured with --bench on a hybrid (P-core/E-core) CPU: after a couple of
/// seconds of work, parsing and search ran 3-4x slower. JSON Lens only uses CPU while the user is
/// waiting for it (opening, searching, querying) and none when idle, so it opts out.
/// </summary>
internal static class PowerThrottling
{
    private const int ProcessPowerThrottling = 4;
    private const uint ExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

    /// <summary>Asks Windows never to throttle this process's execution speed. Returns false where unsupported.</summary>
    public static bool OptOut()
    {
        try
        {
            var state = new PowerThrottlingState { Version = 1, ControlMask = ExecutionSpeed, StateMask = 0 };
            using var self = Process.GetCurrentProcess();
            return SetProcessInformation(self.Handle, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false; // Windows older than 8
        }
    }
}
