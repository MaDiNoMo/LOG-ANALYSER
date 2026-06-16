public class LogEvent
{
    public string Timestamp { get; set; }
    public string MessageType { get; set; } = "S6F11"; // Make sure this line is here!
    public string DataID { get; set; } = "*";
    public string CEID { get; set; } = "*";
    public string ReportID { get; set; } = "*";
    public int LineNumber { get; set; }

    public int Lines { get; set; }

    public string GetSignature() => $"[Data:{DataID} | CEID:{CEID} | Rep:{ReportID}]";
}