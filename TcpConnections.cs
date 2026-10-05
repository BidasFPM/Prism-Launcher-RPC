using System.Runtime.InteropServices;

namespace PrismRpc;

public static class TcpConnections
{
    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const uint MIB_TCP_STATE_ESTAB = 5;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize,
        bool bOrder, int ulAf, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    public static List<(uint Pid, string RemoteIp, ushort RemotePort)> GetEstablished()
    {
        var result = new List<(uint, string, ushort)>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return result;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return result;

            int count = Marshal.ReadInt32(buffer);
            var ptr   = IntPtr.Add(buffer, 4);
            int rowSz = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(ptr);
                if (row.state == MIB_TCP_STATE_ESTAB)
                {
                    ushort rport = Swap16((ushort)(row.remotePort & 0xFFFF));
                    result.Add((row.owningPid, UintToIp(row.remoteAddr), rport));
                }
                ptr = IntPtr.Add(ptr, rowSz);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }

        return result;
    }

    private static ushort Swap16(ushort v) =>
        (ushort)(((v & 0xFF) << 8) | ((v >> 8) & 0xFF));

    private static string UintToIp(uint a) =>
        $"{(a) & 0xFF}.{(a >> 8) & 0xFF}.{(a >> 16) & 0xFF}.{(a >> 24) & 0xFF}";
}
