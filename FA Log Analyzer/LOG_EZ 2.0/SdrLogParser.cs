using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LOG_EZ
{
    public static class SdrLogParser
    {
        public static List<LogEvent> ParseLog(string path, bool useFilter, DateTime start, DateTime end)
        {
            var list = new List<LogEvent>();
            var regexSF = new Regex(@"(S\d+F\d+)\s*W=([01])");
            var regexApp = new Regex(@"\)\s*(Sdr[a-zA-Z]+)");

            string lastTs = "UNKNOWN", lastApp = "Unknown";
            bool capS6 = false, capS3 = false;
            int intCount = 0;
            string curD = "-", curC = "-", curR = "-";
            string curCarrier = "-", curPort = "-", curAttr = "-", curAction = "-";
            int currentLine = 0;

            foreach (string line in File.ReadLines(path))
            {
                currentLine++;
                if (line.Length >= 19 && DateTime.TryParse(line.Substring(0, 19), out _))
                {
                    lastTs = line.Substring(0, 19);
                    var appMatch = regexApp.Match(line);
                    if (appMatch.Success) lastApp = appMatch.Groups[1].Value;
                }

                var sfMatch = regexSF.Match(line);
                if (sfMatch.Success)
                {
                    string sf = sfMatch.Groups[1].Value;
                    string w = sfMatch.Groups[2].Value;

                    if (sf == "S6F11")
                    {
                        capS6 = true;
                        capS3 = false;
                        intCount = 0;
                        curD = "-";
                        curC = "-";
                        curR = "-";
                    }
                    else if (sf == "S3F17")
                    {
                        capS3 = true;
                        capS6 = false;
                        intCount = 0;
                        curCarrier = "-";
                        curPort = "-";
                        curAttr = "-";
                        curAction = "-";
                    }
                    else
                    {
                        if (!useFilter || (DateTime.TryParse(lastTs, out DateTime t) && t >= start && t <= end))
                            list.Add(new LogEvent { LogDate = lastTs.Substring(0, 10), LogTime = lastTs.Substring(11), Timestamp = lastTs, SdrMessage = lastApp, Protocol = sf, WaitBit = w, LineNumber = currentLine });
                        capS6 = false;
                        capS3 = false;
                    }
                }
                else if (capS6 && (line.Contains("<U") || line.Contains("<I")))
                {
                    intCount++;
                    var parts = line.Split(new char[] { '<', ' ', '>' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        if (intCount == 1) curD = parts[1].Trim();
                        else if (intCount == 2) curC = parts[1].Trim();
                        else if (intCount == 3)
                        {
                            curR = parts[1].Trim();
                            if (!useFilter || (DateTime.TryParse(lastTs, out DateTime t) && t >= start && t <= end))
                                list.Add(new LogEvent { LogDate = lastTs.Substring(0, 10), LogTime = lastTs.Substring(11), Timestamp = lastTs, SdrMessage = lastApp, Protocol = "S6F11", WaitBit = "1", DataID = curD, CEID = curC, ReportID = curR, LineNumber = currentLine });
                            capS6 = false;
                        }
                    }
                }
                else if (capS3 && (line.Contains("<U") || line.Contains("<I")))
                {
                    intCount++;
                    var parts = line.Split(new char[] { '<', ' ', '>' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        if (intCount == 1) curAction = parts[1].Trim();
                        else if (intCount == 2) curCarrier = parts[1].Trim();
                        else if (intCount == 3) curPort = parts[1].Trim();
                        else if (intCount == 4)
                        {
                            curAttr = parts[1].Trim();
                            if (!useFilter || (DateTime.TryParse(lastTs, out DateTime t) && t >= start && t <= end))
                                list.Add(new LogEvent { LogDate = lastTs.Substring(0, 10), LogTime = lastTs.Substring(11), Timestamp = lastTs, SdrMessage = lastApp, Protocol = "S3F17", WaitBit = "1", CarrierAction = curAction, CarrierID = curCarrier, PortID = curPort, AttrID = curAttr, LineNumber = currentLine });
                            capS3 = false;
                        }
                    }
                }
            }
            return list;
        }
    }
}