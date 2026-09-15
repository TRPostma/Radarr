using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Fallback;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Fallback
{
    [TestFixture]
    public class FailedDownloadFallbackServiceFixture : CoreTest<FailedDownloadFallbackService>
    {
        private Movie _movie;
        private List<PendingRelease> _pendingReleases;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew().With(m => m.Id = 10).Build();
            _pendingReleases = new List<PendingRelease>();

            Mocker.GetMock<IPendingReleaseRepository>()
                  .Setup(s => s.AllByMovieId(_movie.Id))
                  .Returns(() => _pendingReleases);
        }

        private PendingRelease GivenPendingRelease(PendingReleaseReason reason, DateTime? added = null)
        {
            var release = Builder<ReleaseInfo>.CreateNew()
                                              .With(r => r.Title = "A.Movie.1998.720p-" + reason)
                                              .With(r => r.PublishDate = DateTime.UtcNow.AddDays(-1))
                                              .Build();

            var pending = new PendingRelease
            {
                Id = _pendingReleases.Count + 1,
                MovieId = _movie.Id,
                Title = release.Title,
                Added = added ?? DateTime.UtcNow.AddMinutes(-30),
                ParsedMovieInfo = new ParsedMovieInfo { Year = 1998, MovieTitles = new List<string> { "A Movie" } },
                Release = release,
                Reason = reason
            };

            _pendingReleases.Add(pending);

            return pending;
        }

        private void GivenDecisions(Func<RemoteMovie, DownloadDecision> factory)
        {
            Mocker.GetMock<IMakeDownloadDecision>()
                  .Setup(s => s.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), false))
                  .Returns<List<ReleaseInfo>, bool>((releases, _) => releases.Select(r => factory(new RemoteMovie
                  {
                      Movie = _movie,
                      Release = r,
                      ParsedMovieInfo = new ParsedMovieInfo { Year = 1998, MovieTitles = new List<string> { "A Movie" } }
                  })).ToList());
        }

        private void GivenProcessed(Func<List<DownloadDecision>, ProcessedDecisions> result)
        {
            Mocker.GetMock<IProcessDownloadDecisions>()
                  .Setup(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                  .Returns<List<DownloadDecision>>(d => System.Threading.Tasks.Task.FromResult(result(d)));
        }

        [Test]
        public void should_not_have_candidates_without_fallback_rows()
        {
            GivenPendingRelease(PendingReleaseReason.Delay);
            GivenPendingRelease(PendingReleaseReason.Fallback);

            Subject.HasCandidates(_movie.Id).Should().BeFalse();
        }

        [Test]
        public void should_ignore_expired_fallback_rows()
        {
            GivenPendingRelease(PendingReleaseReason.FailedDownloadFallback, DateTime.UtcNow.Subtract(FailedDownloadFallbackService.MaxAge).AddMinutes(-5));

            Subject.HasCandidates(_movie.Id).Should().BeFalse();
        }

        [Test]
        public void should_have_candidates_for_movie()
        {
            GivenPendingRelease(PendingReleaseReason.FailedDownloadFallback);

            Subject.HasCandidates(_movie.Id).Should().BeTrue();
        }

        [Test]
        public void should_grab_cached_release_and_not_search()
        {
            GivenPendingRelease(PendingReleaseReason.FailedDownloadFallback);
            GivenDecisions(r => new DownloadDecision(r));
            GivenProcessed(d => new ProcessedDecisions(d, new List<DownloadDecision>(), new List<DownloadDecision>()));

            Subject.Execute(new FailedDownloadFallbackCommand(_movie.Id));

            Mocker.GetMock<IProcessDownloadDecisions>().Verify(v => v.ProcessDecisions(It.Is<List<DownloadDecision>>(d => d.Count == 1)), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(v => v.Push(It.IsAny<MoviesSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_remove_rejected_rows_and_search_when_nothing_grabbed()
        {
            var pending = GivenPendingRelease(PendingReleaseReason.FailedDownloadFallback);
            GivenDecisions(r => new DownloadDecision(r, new DownloadRejection(DownloadRejectionReason.Unknown, "Blocklisted")));
            GivenProcessed(d => new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), d));

            Subject.Execute(new FailedDownloadFallbackCommand(_movie.Id));

            Mocker.GetMock<IPendingReleaseRepository>().Verify(v => v.Delete(pending.Id), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(v => v.Push(It.Is<MoviesSearchCommand>(c => c.MovieIds.Single() == _movie.Id), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void should_search_when_no_candidates()
        {
            Subject.Execute(new FailedDownloadFallbackCommand(_movie.Id));

            Mocker.GetMock<IProcessDownloadDecisions>().Verify(v => v.ProcessDecisions(It.IsAny<List<DownloadDecision>>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(v => v.Push(It.IsAny<MoviesSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }
    }
}
