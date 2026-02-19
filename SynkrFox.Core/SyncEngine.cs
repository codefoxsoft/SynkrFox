using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

namespace SynkrFox.Core
{
    public class SyncEngine
    {
        // Watchers for source folders
        private ConcurrentDictionary<string, FileSystemWatcher> _sourceWatchers = new();
        // Watchers for corresponding backup folders
        private ConcurrentDictionary<string, FileSystemWatcher> _backupWatchers = new();
        // Track ongoing sync operations to prevent ping-pong
        private ConcurrentDictionary<string, DateTime> _syncInProgress = new();
        
        private string _backupPath = "";
        public SyncStatistics Statistics { get; private set; } = new();
        
        // Conflict window: if both sides modified within this many seconds, keep both
        private const int CONFLICT_WINDOW_SECONDS = 5;
        
        public event Action<LogEntry>? LogMessage;

        public SyncEngine()
        {
        }

        public void SetBackupPath(string path)
        {
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    Log($"Error creating backup directory: {ex.Message}", LogLevel.Error);
                    return;
                }
            }
            _backupPath = path;
            Log($"Backup path set to: {path}", LogLevel.Info);
        }

        public void AddSourceFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                Log($"Invalid source path: {path}", LogLevel.Error);
                return;
            }

            if (_sourceWatchers.ContainsKey(path))
            {
                Log($"Already watching: {path}", LogLevel.Warning);
                return;
            }

            // Create source watcher
            var sourceWatcher = CreateWatcher(path, SyncDirection.SourceToBackup);
            _sourceWatchers.TryAdd(path, sourceWatcher);
            
            // Create corresponding backup watcher
            string backupFolder = Path.Combine(_backupPath, new DirectoryInfo(path).Name);
            if (!Directory.Exists(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
            }
            
            var backupWatcher = CreateWatcher(backupFolder, SyncDirection.BackupToSource);
            _backupWatchers.TryAdd(path, backupWatcher); // Key by source path for easy lookup
            
            Log($"Started bi-directional watch: {path} ↔ {backupFolder}", LogLevel.Success);

            // Initial Sync
            Task.Run(() => PerformInitialSync(path));
        }

        public void RemoveSourceFolder(string path)
        {
            if (_sourceWatchers.TryRemove(path, out var sourceWatcher))
            {
                sourceWatcher.EnableRaisingEvents = false;
                sourceWatcher.Dispose();
            }
            
            if (_backupWatchers.TryRemove(path, out var backupWatcher))
            {
                backupWatcher.EnableRaisingEvents = false;
                backupWatcher.Dispose();
            }
            
            Log($"Stopped watching: {path}", LogLevel.Info);
        }

        public void ForceFullRescan()
        {
            Log("Starting full rescan of all folders...", LogLevel.Info);
            
            foreach (var kvp in _sourceWatchers)
            {
                string sourcePath = kvp.Key;
                Task.Run(() => PerformInitialSync(sourcePath));
            }
        }

        private FileSystemWatcher CreateWatcher(string path, SyncDirection direction)
        {
            var watcher = new FileSystemWatcher(path);
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | 
                                   NotifyFilters.DirectoryName | NotifyFilters.CreationTime;

            // Capture direction in closure
            watcher.Created += (s, e) => OnChanged(s, e, direction);
            watcher.Changed += (s, e) => OnChanged(s, e, direction);
            watcher.Renamed += (s, e) => OnRenamed(s, e, direction);
            watcher.Deleted += (s, e) => OnDeleted(s, e, direction);
            watcher.Error += OnError;

            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void PerformInitialSync(string sourcePath)
        {
            if (string.IsNullOrEmpty(_backupPath)) return;

            Log($"Starting initial sync for {sourcePath}...", LogLevel.Info);
            try
            {
                // 1. Sync all files from source to backup
                foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    SyncFile(file, sourcePath, SyncDirection.SourceToBackup, isInitialSync: true);
                }
                
                // 2. Sync all files from backup to source (Bi-Directional)
                string backupFolder = Path.Combine(_backupPath, new DirectoryInfo(sourcePath).Name);
                if (Directory.Exists(backupFolder))
                {
                    foreach (var file in Directory.GetFiles(backupFolder, "*", SearchOption.AllDirectories))
                    {
                        SyncFile(file, backupFolder, SyncDirection.BackupToSource, isInitialSync: true);
                    }
                }

                // 3. Create empty directories (source to backup)
                foreach (var dir in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(sourcePath, dir);
                    string destPath = Path.Combine(backupFolder, relativePath);
                    if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);
                }

                Log($"Initial sync complete: {sourcePath}", LogLevel.Success);
            }
            catch (Exception ex)
            {
                Log($"Error during initial sync: {ex.Message}", LogLevel.Error);
                Statistics.RecordError();
            }
        }



        private bool IsExcluded(string path)
        {
            // Exclude running executable to prevent lock errors
            string currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!string.IsNullOrEmpty(currentExe) && path.Equals(currentExe, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Exclude common build artifacts and temporary files
            string[] excludePatterns = new[]
            {
                "\\obj\\", "\\bin\\", 
                "\\node_modules\\", "\\.git\\",
                "\\.vs\\", "\\packages\\",
                ".tmp", ".temp", "~$", ".lock"
            };

            foreach (var pattern in excludePatterns)
            {
                if (path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private async Task<bool> RetryFileCopy(string source, string dest, int maxRetries = 10)
        {
            int retryDelay = 200; // Start with 200ms

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    File.Copy(source, dest, true);
                    return true; // Success
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    // File is locked, wait and retry with exponential backoff
                    await Task.Delay(retryDelay);
                    retryDelay = Math.Min(retryDelay * 2, 3000); // Cap at 3 seconds per retry
                }
                catch (IOException)
                {
                    // Final attempt failed
                    return false;
                }
            }

            return false;
        }

        private ConflictInfo DetectConflict(string sourcePath, string destPath)
        {
            var sourceModified = File.GetLastWriteTimeUtc(sourcePath);
            var destModified = File.GetLastWriteTimeUtc(destPath);
            var sourceSize = new FileInfo(sourcePath).Length;
            var destSize = new FileInfo(destPath).Length;

            // Check if both modified within conflict window
            var timeDiff = Math.Abs((sourceModified - destModified).TotalSeconds);
            bool isSimultaneous = timeDiff < CONFLICT_WINDOW_SECONDS && sourceSize != destSize;

            return new ConflictInfo
            {
                FilePath = sourcePath,
                SourceModified = sourceModified,
                BackupModified = destModified,
                SourceSize = sourceSize,
                BackupSize = destSize,
                IsSimultaneous = isSimultaneous
            };
        }

        private string GetConflictPath(string originalPath)
        {
            string dir = Path.GetDirectoryName(originalPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(originalPath);
            string ext = Path.GetExtension(originalPath);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            return Path.Combine(dir, $"{name}_conflict_{timestamp}{ext}");
        }

        private bool IsSyncInProgress(string path)
        {
            if (_syncInProgress.TryGetValue(path, out var lastSync))
            {
                // Clear stale entries (older than 10 seconds)
                if ((DateTime.Now - lastSync).TotalSeconds < 10)
                {
                    return true;
                }
                _syncInProgress.TryRemove(path, out _);
            }
            return false;
        }

        private void MarkSyncInProgress(string path)
        {
            _syncInProgress[path] = DateTime.Now;
            // Auto-cleanup after a delay
            Task.Run(async () =>
            {
                await Task.Delay(10000);
                _syncInProgress.TryRemove(path, out _);
            });
        }

        private void OnChanged(object? sender, FileSystemEventArgs e, SyncDirection direction)
        {
            var watcher = sender as FileSystemWatcher;
            if (watcher == null) return;

            string originRoot = watcher.Path;
            if (string.IsNullOrEmpty(originRoot)) return;

            Statistics.ReportActivity(); // HDD-style flash on event detection

            // Delay to allow file locks to release
            Task.Run(async () =>
            {
                await Task.Delay(150);
                SyncFile(e.FullPath, originRoot, direction);
            });
        }

        private void OnRenamed(object? sender, RenamedEventArgs e, SyncDirection direction)
        {
            var watcher = sender as FileSystemWatcher;
            if (watcher == null) return;
            string originRoot = watcher.Path;
            
            Statistics.ReportActivity();
            Log($"Renamed: {e.OldName} → {e.Name}", LogLevel.Info);
            
            Task.Run(async () =>
            {
                await Task.Delay(100);
                SyncFile(e.FullPath, originRoot, direction);
            });
        }

        private void OnDeleted(object? sender, FileSystemEventArgs e, SyncDirection direction)
        {
            Statistics.ReportActivity();
            Log($"Deleted: {e.Name}", LogLevel.Info);
            // TODO: Implement delete sync if desired
        }

        private void OnError(object? sender, ErrorEventArgs e)
        {
            Log($"Watcher Error: {e.GetException().Message}", LogLevel.Error);
            Statistics.RecordError();
        }

        private async void SyncFile(string fullPath, string originRoot, SyncDirection direction, bool isInitialSync = false)
        {
            if (string.IsNullOrEmpty(_backupPath)) return;

            // Robustness: Check if path exists
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) return;

            // Robustness: Handle directory vs file
            bool isDirectory = (File.GetAttributes(fullPath) & FileAttributes.Directory) == FileAttributes.Directory;

            if (isDirectory)
            {
                // Just create the directory structure
                // Need to calculate Dest Root first
                string? targetRoot = GetTargetRoot(originRoot, direction);
                if (targetRoot != null) SyncDirectory(fullPath, originRoot, targetRoot);
                return;
            }

            // Skip excluded paths (build artifacts, temp files, etc.)
            if (IsExcluded(fullPath))
            {
                return;
            }

            // Prevent ping-pong syncs
            if (!isInitialSync && IsSyncInProgress(fullPath))
            {
                return;
            }

            Statistics.IncrementActive();
            try
            {
                string? targetRoot = GetTargetRoot(originRoot, direction);
                if (targetRoot == null) return;

                string relativePath = Path.GetRelativePath(originRoot, fullPath);
                string destPath = Path.Combine(targetRoot, relativePath);
                
                // Ensure destination directory exists
                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                // Conflict detection
                if (File.Exists(destPath))
                {
                    var conflict = DetectConflict(fullPath, destPath);
                    
                    if (conflict.IsSimultaneous)
                    {
                        // Keep both versions
                        string conflictPath = GetConflictPath(destPath);
                        MarkSyncInProgress(destPath);
                        await RetryFileCopy(destPath, conflictPath);
                        Statistics.RecordConflict();
                        Log($"⚠ Conflict: Kept both versions → {Path.GetFileName(conflictPath)}", LogLevel.Warning);
                    }
                    else if (conflict.SourceModified <= conflict.BackupModified)
                    {
                        // Destination is newer, skip
                        return;
                    }
                }

                // Perform sync with retry
                MarkSyncInProgress(fullPath);
                MarkSyncInProgress(destPath);
                bool copySuccess = await RetryFileCopy(fullPath, destPath);
                
                if (copySuccess)
                {
                    FileInfo fi = new FileInfo(fullPath);
                    Statistics.RecordSync(fi.Length);
                    
                    string directionArrow = direction == SyncDirection.SourceToBackup ? "→" : "←";
                    Log($"{directionArrow} Synced: {relativePath}", LogLevel.Success);
                }
                else
                {
                    // File is locked, skip gracefully (user-friendly)
                    Log($"⏸ Skipped (file in use): {Path.GetFileName(fullPath)}", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Log($"Error syncing {fullPath}: {ex.Message}", LogLevel.Error);
                Statistics.RecordError();
            }
            finally
            {
                Statistics.DecrementActive();
            }
        }

        private string? GetTargetRoot(string originRoot, SyncDirection direction)
        {
            if (direction == SyncDirection.SourceToBackup)
            {
                // originRoot is Source. Target is Backup.
                // We can construct backup path directly or look it up
                return Path.Combine(_backupPath, new DirectoryInfo(originRoot).Name);
            }
            else
            {
                // originRoot is Backup. Target is Source.
                // find key where value.Path == originRoot
                foreach (var kvp in _backupWatchers)
                {
                    if (kvp.Value.Path.Equals(originRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Key;
                    }
                }
                return null;
            }
        }

        private void SyncDirectory(string fullPath, string originRoot, string targetRoot)
        {
            Statistics.ReportActivity();
            try
            {
                string relativePath = Path.GetRelativePath(originRoot, fullPath);
                string destPath = Path.Combine(targetRoot, relativePath);

                if (!Directory.Exists(destPath))
                {
                    Directory.CreateDirectory(destPath);
                }
            }
            catch (Exception ex)
            {
                Log($"Error creating directory {fullPath}: {ex.Message}", LogLevel.Error);
                Statistics.RecordError();
            }
        }

        private void Log(string message, LogLevel level)
        {
            var entry = new LogEntry(message, level);
            LogMessage?.Invoke(entry);
        }
    }
}
