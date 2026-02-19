namespace SynkrFox.Core
{
    public enum ConflictResolutionMode
    {
        AutoNewest,      // Automatically use file with newest timestamp
        KeepBoth         // Create backup with conflict suffix
    }

    public enum SyncDirection
    {
        SourceToBackup,
        BackupToSource
    }

    public class ConflictInfo
    {
        public string FilePath { get; set; } = "";
        public DateTime SourceModified { get; set; }
        public DateTime BackupModified { get; set; }
        public long SourceSize { get; set; }
        public long BackupSize { get; set; }
        public bool IsSimultaneous { get; set; } // Both modified within conflict window
    }
}
