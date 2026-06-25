using System;
using System.Collections.Generic;
using System.Text;

namespace LOG_EZ
{
    public class LogEvent
    {
        public string LogDate { get; set; } = "";
        public string LogTime { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string SdrMessage { get; set; } = "";
        public string Protocol { get; set; } = "S6F11"; // S6F11, S3F17, S14F9, etc.
        public string WaitBit { get; set; } = "";

        // S6F11 Fields
        public string DataID { get; set; } = "-";
        public string CEID { get; set; } = "-";
        public string ReportID { get; set; } = "-";

        // S3F17 Fields
        public string CarrierAction { get; set; } = "-";
        public string CarrierID { get; set; } = "-";
        public string PortID { get; set; } = "-";
        public string AttrID { get; set; } = "-";

        // S14F9 Fields
        public string ObjSpec { get; set; } = "-";
        public string AttrID_S14F9 { get; set; } = "-";
        public string AttrData { get; set; } = "-";

        public int LineNumber { get; set; }
        public string S16DataID { get; internal set; }

        public string GetSignature() => Protocol switch
        {
            "S3F17" => $"[Carrier:{CarrierID} | Port:{PortID} | Attr:{AttrID}]",
            "S14F9" => $"[ObjSpec:{ObjSpec} | AttrID:{AttrID_S14F9}]",
            _ => $"[Data:{DataID} | CEID:{CEID} | Rep:{ReportID}]"
        };
    }
}