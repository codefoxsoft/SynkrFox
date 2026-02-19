namespace SynkrFox.Core
{
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
    
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = "";
        public LogLevel Level { get; set; }
        
        public LogEntry(string message, LogLevel level = LogLevel.Info)
        {
            Timestamp = DateTime.Now;
            Message = message;
            Level = level;
        }
    }
}
