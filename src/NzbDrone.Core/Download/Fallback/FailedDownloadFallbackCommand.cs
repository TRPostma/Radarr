using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Download.Fallback
{
    public class FailedDownloadFallbackCommand : Command
    {
        public int MovieId { get; set; }

        public override bool SendUpdatesToClient => true;

        public FailedDownloadFallbackCommand()
        {
        }

        public FailedDownloadFallbackCommand(int movieId)
        {
            MovieId = movieId;
        }
    }
}
