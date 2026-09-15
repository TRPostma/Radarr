using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Download.Fallback
{
    // Fork feature: when a grab succeeds, ProcessDownloadDecisions stores the approved runner-up releases as
    // pending releases with reason FailedDownloadFallback. When that download later fails this service
    // re-evaluates those cached releases locally and grabs the best one, so no indexer is searched again.
    // Only if nothing usable is cached does it fall back to upstream's regular search.
    public interface IFailedDownloadFallbackService
    {
        bool HasCandidates(int movieId);
    }

    public class FailedDownloadFallbackService : IFailedDownloadFallbackService, IExecute<FailedDownloadFallbackCommand>
    {
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);

        private readonly IPendingReleaseRepository _repository;
        private readonly IMakeDownloadDecision _downloadDecisionMaker;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public FailedDownloadFallbackService(IPendingReleaseRepository repository,
                                             IMakeDownloadDecision downloadDecisionMaker,
                                             IProcessDownloadDecisions processDownloadDecisions,
                                             IManageCommandQueue commandQueueManager,
                                             Logger logger)
        {
            _repository = repository;
            _downloadDecisionMaker = downloadDecisionMaker;
            _processDownloadDecisions = processDownloadDecisions;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public bool HasCandidates(int movieId)
        {
            return GetCandidates(movieId).Any();
        }

        public void Execute(FailedDownloadFallbackCommand message)
        {
            var candidates = GetCandidates(message.MovieId);

            if (candidates.Any())
            {
                _logger.Debug("Evaluating {0} cached fallback release(s) for failed download", candidates.Count);

                var releases = candidates.Select(c => c.Release).ToList();
                var decisions = _downloadDecisionMaker.GetRssDecision(releases)
                                                      .Where(d => d.RemoteMovie.Movie != null && d.RemoteMovie.Movie.Id == message.MovieId)
                                                      .ToList();

                var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

                RemoveRejected(candidates, processed.Rejected);

                if (processed.Grabbed.Any())
                {
                    _logger.Info("Grabbed cached fallback release '{0}' for failed download, skipping indexer search", processed.Grabbed.First().RemoteMovie.Release.Title);
                    return;
                }

                _logger.Debug("No cached fallback release could be grabbed ({0} rejected), searching indexers instead", processed.Rejected.Count);
            }
            else
            {
                _logger.Debug("No cached fallback releases found, searching indexers instead");
            }

            _commandQueueManager.Push(new MoviesSearchCommand { MovieIds = new List<int> { message.MovieId } });
        }

        private List<PendingRelease> GetCandidates(int movieId)
        {
            var cutoff = DateTime.UtcNow.Subtract(MaxAge);

            return _repository.AllByMovieId(movieId)
                              .Where(p => p.Reason == PendingReleaseReason.FailedDownloadFallback && p.Added > cutoff)
                              .ToList();
        }

        private void RemoveRejected(List<PendingRelease> candidates, List<DownloadDecision> rejected)
        {
            foreach (var decision in rejected)
            {
                var release = decision.RemoteMovie.Release;
                var matching = candidates.Where(c => c.Title == release.Title &&
                                                     c.Release.PublishDate == release.PublishDate &&
                                                     c.Release.Indexer == release.Indexer)
                                         .ToList();

                foreach (var candidate in matching)
                {
                    _logger.Debug("Removing cached fallback release '{0}', it was rejected: {1}", candidate.Title, decision.Rejections.FirstOrDefault()?.Message);
                    _repository.Delete(candidate.Id);
                }
            }
        }
    }
}
