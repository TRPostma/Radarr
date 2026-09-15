using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Fallback;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public class RedownloadFailedDownloadService : IHandle<DownloadFailedEvent>
    {
        private readonly IConfigService _configService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IFailedDownloadFallbackService _fallbackService;
        private readonly Logger _logger;

        public RedownloadFailedDownloadService(IConfigService configService,
                                               IManageCommandQueue commandQueueManager,
                                               IFailedDownloadFallbackService fallbackService,
                                               Logger logger)
        {
            _configService = configService;
            _commandQueueManager = commandQueueManager;
            _fallbackService = fallbackService;
            _logger = logger;
        }

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(DownloadFailedEvent message)
        {
            if (message.SkipRedownload)
            {
                _logger.Debug("Skip redownloading requested by user");
                return;
            }

            if (!_configService.AutoRedownloadFailed)
            {
                _logger.Debug("Auto redownloading failed movies is disabled");
                return;
            }

            if (message.ReleaseSource == ReleaseSourceType.InteractiveSearch && !_configService.AutoRedownloadFailedFromInteractiveSearch)
            {
                _logger.Debug("Auto redownloading failed movies from interactive search is disabled");
                return;
            }

            // Fork: grab a cached runner-up release instead of searching all indexers again
            if (message.MovieId != 0 && _fallbackService.HasCandidates(message.MovieId))
            {
                _logger.Debug("Cached fallback releases available, grabbing next best instead of searching");
                _commandQueueManager.Push(new FailedDownloadFallbackCommand(message.MovieId));

                return;
            }

            if (message.MovieId != 0)
            {
                _logger.Debug("Failed download contains a movie, searching again.");
                _commandQueueManager.Push(new MoviesSearchCommand { MovieIds = new List<int> { message.MovieId } });
            }
        }
    }
}
