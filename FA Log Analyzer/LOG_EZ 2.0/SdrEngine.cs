using System;
using System.Collections.Generic;
using System.Text;

using System.Runtime.InteropServices;

namespace LOG_EZ
{
    public static class SdrEngine
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ResultCallback(bool isExpectedTree, string timestamp, string dataID, string ceid, string reportID, string colorHex);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SummaryCallback(int perfectMatches, int totalExpected, int extraMessages);

        [DllImport(@"C:\Users\ArJuN\source\repos\SdrParserEngine\x64\Release\SdrParserEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void CompareLogSequence(string filePath, string startTime, string endTime, string expectedListStr, ResultCallback itemCallback, SummaryCallback summaryCallback);
    }
}