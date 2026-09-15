namespace NzbDrone.Core.Download.Pending
{
    public enum PendingReleaseReason
    {
        Delay = 0,
        DownloadClientUnavailable = 1,
        Fallback = 2,

        // Fork: approved runner-up release kept so a failed download can be replaced without a new indexer search
        FailedDownloadFallback = 3
    }
}
