using System;

namespace SynkrFox.Core
{
    public class SyncStatistics
    {
        public long FilesSynced { get; set; }
        public long BytesSynced { get; set; }
        public int ConflictsResolved { get; set; }
        public int ErrorsEncountered { get; set; }
        public DateTime? LastSyncTime { get; set; }
        public DateTime? LastActivityTime { get; set; }
        private int _activeOperations = 0;
        
        // Event for real-time UI updates
        public event Action? ActivityDetected;

        public bool IsMovingData => _activeOperations > 0;
        public bool IsSyncing => _activeOperations > 0 || (LastActivityTime.HasValue && (DateTime.Now - LastActivityTime.Value).TotalMilliseconds < 1500);

        public void ReportActivity()
        {
            LastActivityTime = DateTime.Now;
            ActivityDetected?.Invoke();
        }

        public void IncrementActive()
        {
            System.Threading.Interlocked.Increment(ref _activeOperations);
            ReportActivity();
        }

        public void DecrementActive()
        {
            System.Threading.Interlocked.Decrement(ref _activeOperations);
            ReportActivity();
        }

        public void RecordSync(long fileSize)
        {
            FilesSynced++;
            BytesSynced += fileSize;
            LastSyncTime = DateTime.Now;
            ReportActivity();
        }
        
        public void RecordConflict()
        {
            ConflictsResolved++;
        }
        
        public void RecordError()
        {
            ErrorsEncountered++;
        }
        
        public string GetFormattedStats()
        {
            double sizeMB = BytesSynced / (1024.0 * 1024.0);
            return $"{FilesSynced} files ({sizeMB:F2} MB) | Conflicts: {ConflictsResolved} | Errors: {ErrorsEncountered}";
        }
        
        public string GetLastSyncFormatted()
        {
            if (LastSyncTime == null) return "Never";
            
            var elapsed = DateTime.Now - LastSyncTime.Value;
            if (elapsed.TotalSeconds < 60)
                return $"{(int)elapsed.TotalSeconds}s ago";
            if (elapsed.TotalMinutes < 60)
                return $"{(int)elapsed.TotalMinutes}m ago";
            return $"{(int)elapsed.TotalHours}h ago";
        }
    }
}
