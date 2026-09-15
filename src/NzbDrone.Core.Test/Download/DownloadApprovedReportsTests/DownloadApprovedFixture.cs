using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.DownloadApprovedReportsTests
{
    [TestFixture]
    public class DownloadApprovedFixture : CoreTest<ProcessDownloadDecisions>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IPrioritizeDownloadDecision>()
                .Setup(v => v.PrioritizeDecisionsForMovies(It.IsAny<List<DownloadDecision>>()))
                .Returns<List<DownloadDecision>>(v => v);
        }

        private Movie GetMovie(int id)
        {
            return Builder<Movie>.CreateNew()
                            .With(e => e.Id = id)
                                 .With(m => m.Tags = new HashSet<int>())

                            .Build();
        }

        private RemoteMovie GetRemoteMovie(QualityModel quality, Movie movie = null, DownloadProtocol downloadProtocol = DownloadProtocol.Usenet)
        {
            if (movie == null)
            {
                movie = GetMovie(1);
            }

            movie.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() };

            var remoteMovie = new RemoteMovie()
            {
                ParsedMovieInfo = new ParsedMovieInfo()
                {
                    Quality = quality,
                    Year = 1998,
                    MovieTitles = new List<string> { "A Movie" },
                },
                Movie = movie,

                Release = new ReleaseInfo()
                {
                    PublishDate = DateTime.UtcNow,
                    Title = "A.Movie.1998",
                    Size = 200,
                    DownloadProtocol = downloadProtocol
                }
            };

            return remoteMovie;
        }

        [Test]
        public async Task should_store_approved_runner_ups_as_failed_download_fallback()
        {
            var movie = GetMovie(1);
            var best = GetRemoteMovie(new QualityModel(Quality.HDTV1080p), movie);
            var runnerUp = GetRemoteMovie(new QualityModel(Quality.HDTV720p), movie);

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(best));
            decisions.Add(new DownloadDecision(runnerUp));

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().HaveCount(1);
            result.Pending.Should().BeEmpty();
            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(best, null), Times.Once());
            Mocker.GetMock<IPendingReleaseService>()
                  .Verify(v => v.AddMany(It.Is<List<Tuple<DownloadDecision, PendingReleaseReason>>>(l => l.Count == 1 && l[0].Item1.RemoteMovie == runnerUp && l[0].Item2 == PendingReleaseReason.FailedDownloadFallback)),
                          Times.Once());
        }

        [Test]
        public async Task should_cap_failed_download_fallbacks_per_movie()
        {
            var movie = GetMovie(1);
            var decisions = new List<DownloadDecision>();

            for (var i = 0; i < 15; i++)
            {
                decisions.Add(new DownloadDecision(GetRemoteMovie(new QualityModel(Quality.HDTV720p), movie)));
            }

            await Subject.ProcessDecisions(decisions);

            Mocker.GetMock<IPendingReleaseService>().Verify(v => v.AddMany(It.Is<List<Tuple<DownloadDecision, PendingReleaseReason>>>(l => l.Count == 10)), Times.Once());
        }

        [Test]
        public async Task should_download_report_if_movie_was_not_already_downloaded()
        {
            var remoteMovie = GetRemoteMovie(new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie));

            await Subject.ProcessDecisions(decisions);
            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteMovie>(), null), Times.Once());
        }

        [Test]
        public async Task should_only_download_movie_once()
        {
            var remoteMovie = GetRemoteMovie(new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie));
            decisions.Add(new DownloadDecision(remoteMovie));

            await Subject.ProcessDecisions(decisions);
            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteMovie>(), null), Times.Once());
        }

        [Test]
        public async Task should_not_download_if_any_movie_was_already_downloaded()
        {
            var remoteMovie1 = GetRemoteMovie(
                                                    new QualityModel(Quality.HDTV720p));

            var remoteMovie2 = GetRemoteMovie(
                                                    new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie1));
            decisions.Add(new DownloadDecision(remoteMovie2));

            await Subject.ProcessDecisions(decisions);
            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteMovie>(), null), Times.Once());
        }

        [Test]
        public async Task should_return_downloaded_reports()
        {
            var remoteMovie = GetRemoteMovie(new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie));

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().HaveCount(1);
        }

        [Test]
        public async Task should_return_all_downloaded_reports()
        {
            var remoteMovie1 = GetRemoteMovie(new QualityModel(Quality.HDTV720p), GetMovie(1));

            var remoteMovie2 = GetRemoteMovie(new QualityModel(Quality.HDTV720p), GetMovie(2));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie1));
            decisions.Add(new DownloadDecision(remoteMovie2));

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().HaveCount(2);
        }

        [Test]
        public async Task should_only_return_downloaded_reports()
        {
            var remoteMovie1 = GetRemoteMovie(
                                                    new QualityModel(Quality.HDTV720p),
                                                    GetMovie(1));

            var remoteMovie2 = GetRemoteMovie(
                                                    new QualityModel(Quality.HDTV720p),
                                                    GetMovie(2));

            var remoteMovie3 = GetRemoteMovie(
                                                    new QualityModel(Quality.HDTV720p),
                                                    GetMovie(2));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie1));
            decisions.Add(new DownloadDecision(remoteMovie2));
            decisions.Add(new DownloadDecision(remoteMovie3));

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().HaveCount(2);
        }

        [Test]
        public async Task should_not_add_to_downloaded_list_when_download_fails()
        {
            var remoteMovie = GetRemoteMovie(new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie));

            Mocker.GetMock<IDownloadService>().Setup(s => s.DownloadReport(It.IsAny<RemoteMovie>(), null)).Throws(new Exception());

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_an_empty_list_when_none_are_appproved()
        {
            var decisions = new List<DownloadDecision>();
            RemoteMovie remoteMovie = null;
            decisions.Add(new DownloadDecision(remoteMovie, new DownloadRejection(DownloadRejectionReason.Unknown, "Failure!")));
            decisions.Add(new DownloadDecision(remoteMovie, new DownloadRejection(DownloadRejectionReason.Unknown, "Failure!")));

            Subject.GetQualifiedReports(decisions).Should().BeEmpty();
        }

        [Test]
        public async Task should_not_grab_if_pending()
        {
            var remoteMovie = GetRemoteMovie(new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie, new DownloadRejection(DownloadRejectionReason.Unknown, "Failure!", RejectionType.Temporary)));

            await Subject.ProcessDecisions(decisions);
            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteMovie>(), null), Times.Never());
        }

        [Test]
        public async Task should_not_add_to_pending_if_movie_was_grabbed()
        {
            var removeMovie = GetRemoteMovie(new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(removeMovie));
            decisions.Add(new DownloadDecision(removeMovie, new DownloadRejection(DownloadRejectionReason.Unknown, "Failure!", RejectionType.Temporary)));

            await Subject.ProcessDecisions(decisions);
            Mocker.GetMock<IPendingReleaseService>().Verify(v => v.AddMany(It.IsAny<List<Tuple<DownloadDecision, PendingReleaseReason>>>()), Times.Never());
        }

        [Test]
        public async Task should_add_to_pending_even_if_already_added_to_pending()
        {
            var remoteEpisode = GetRemoteMovie(new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteEpisode, new DownloadRejection(DownloadRejectionReason.Unknown, "Failure!", RejectionType.Temporary)));
            decisions.Add(new DownloadDecision(remoteEpisode, new DownloadRejection(DownloadRejectionReason.Unknown, "Failure!", RejectionType.Temporary)));

            await Subject.ProcessDecisions(decisions);
            Mocker.GetMock<IPendingReleaseService>().Verify(v => v.AddMany(It.IsAny<List<Tuple<DownloadDecision, PendingReleaseReason>>>()), Times.Once());
        }

        [Test]
        public async Task should_add_to_failed_if_already_failed_for_that_protocol()
        {
            var remoteMovie = GetRemoteMovie(new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie));
            decisions.Add(new DownloadDecision(remoteMovie));

            Mocker.GetMock<IDownloadService>().Setup(s => s.DownloadReport(It.IsAny<RemoteMovie>(), null))
                  .Throws(new DownloadClientUnavailableException("Download client failed"));

            await Subject.ProcessDecisions(decisions);
            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteMovie>(), null), Times.Once());
        }

        [Test]
        public async Task should_not_add_to_failed_if_failed_for_a_different_protocol()
        {
            var remoteMovie = GetRemoteMovie(new QualityModel(Quality.HDTV720p), null, DownloadProtocol.Usenet);
            var remoteMovie2 = GetRemoteMovie(new QualityModel(Quality.HDTV720p), null, DownloadProtocol.Torrent);

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie));
            decisions.Add(new DownloadDecision(remoteMovie2));

            Mocker.GetMock<IDownloadService>().Setup(s => s.DownloadReport(It.Is<RemoteMovie>(r => r.Release.DownloadProtocol == DownloadProtocol.Usenet), null))
                  .Throws(new DownloadClientUnavailableException("Download client failed"));

            await Subject.ProcessDecisions(decisions);
            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.Is<RemoteMovie>(r => r.Release.DownloadProtocol == DownloadProtocol.Usenet), null), Times.Once());
            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.Is<RemoteMovie>(r => r.Release.DownloadProtocol == DownloadProtocol.Torrent), null), Times.Once());
        }

        [Test]
        public async Task should_add_to_rejected_if_release_unavailable_on_indexer()
        {
            var remoteMovie = GetRemoteMovie(new QualityModel(Quality.HDTV720p));

            var decisions = new List<DownloadDecision>();
            decisions.Add(new DownloadDecision(remoteMovie));

            Mocker.GetMock<IDownloadService>()
                  .Setup(s => s.DownloadReport(It.IsAny<RemoteMovie>(), null))
                  .Throws(new ReleaseUnavailableException(remoteMovie.Release, "That 404 Error is not just a Quirk"));

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().BeEmpty();
            result.Rejected.Should().NotBeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
