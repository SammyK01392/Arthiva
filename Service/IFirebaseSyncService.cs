using System;
using System.Collections.Generic;
using System.Text;

namespace Arthiva.Services;

public interface IFirebaseSyncService
{
    Task<DateTime?> GetLastBackupTimeAsync();
    Task BackupAsync(IProgress<string>? progress = null);
    Task RestoreAsync(IProgress<string>? progress = null);
}
