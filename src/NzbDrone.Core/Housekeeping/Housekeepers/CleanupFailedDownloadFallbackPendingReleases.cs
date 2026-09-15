using System;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download.Fallback;
using NzbDrone.Core.Download.Pending;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // Fork: cached fallback releases are only useful shortly after the grab they belong to
    public class CleanupFailedDownloadFallbackPendingReleases : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupFailedDownloadFallbackPendingReleases(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();

            mapper.Execute(@"DELETE FROM ""PendingReleases""
                             WHERE ""Added"" < @Cutoff
                             AND ""Reason"" = @Reason",
                           new
                           {
                               Cutoff = DateTime.UtcNow.Subtract(FailedDownloadFallbackService.MaxAge),
                               Reason = (int)PendingReleaseReason.FailedDownloadFallback
                           });
        }
    }
}
