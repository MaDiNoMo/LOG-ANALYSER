using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace LOG_EZ
{
    public static class EventMapper
    {
        private static Dictionary<string, string> ceidEventMap = new Dictionary<string, string>();

        public static void LoadEventMapping(string eventFilePath)
        {
            ceidEventMap.Clear();
            try
            {
                if (File.Exists(eventFilePath))
                {
                    foreach (string line in File.ReadLines(eventFilePath))
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            ceidEventMap[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }
            }
            catch { MessageBox.Show("Failed to parse EVENTS.txt file."); }
        }

        public static string GetEventName(string ceid)
        {
            if (ceidEventMap.TryGetValue(ceid, out string name)) return name;
            return ""; // Returns clean empty string if no match is found
        }
    }
}