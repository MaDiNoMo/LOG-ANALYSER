using System;
using System.Runtime.InteropServices;

namespace LOG_EZ
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct EventData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Timestamp;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string Protocol;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string P1;
        [MarshalAs(UnmanagedType.LPStr)] public string P2; // For longer strings
        [MarshalAs(UnmanagedType.LPStr)] public string P3;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string ColorHex;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ResultCallback(int isExpectedTree, EventData data);
}
