# Radarr fork: grab cached fallback releases when a download fails

This fork carries one feature on top of upstream Radarr (`master` branch):

**When a grab succeeds, the other approved releases from the same search or RSS pass are cached.
If that download later fails, Radarr grabs the next best cached release instead of searching every
indexer again and re-running the whole decision engine.** Only when nothing usable is cached does it
fall back to upstream's normal re-search.

## How it works

- `ProcessDownloadDecisions` already sees the full prioritized list of releases. After it grabs the
  best one, the approved runner-ups (max 10 per movie) are stored in the existing `PendingReleases`
  table with a new reason, `FailedDownloadFallback`. They are hidden from the queue and ignored by
  RSS sync.
- On a failed download, `RedownloadFailedDownloadService` first asks `FailedDownloadFallbackService`
  whether cached releases exist for the movie. If so it queues a `FailedDownloadFallback` command
  instead of a `MoviesSearch` command.
- The command re-runs the normal decision specs on the cached releases locally (blocklist, quality,
  queue, history, ...), grabs the best survivor through the normal grab path, drops rejected rows,
  and only pushes the regular search if nothing could be grabbed. Grabbing still fetches the NZB or
  torrent from that one indexer, but there is no search.
- Cached rows expire after 2 hours (housekeeping task), are removed when a matching release is
  grabbed (upstream behaviour) and when they get rejected during a fallback attempt.

Files touched in upstream (kept as small as possible so rebases rarely conflict):

- `src/NzbDrone.Core/Download/Pending/PendingReleaseReason.cs` (new enum value)
- `src/NzbDrone.Core/Download/Pending/PendingReleaseRepository.cs` (hide from queue)
- `src/NzbDrone.Core/Download/Pending/PendingReleaseService.cs` (hide from RSS sync)
- `src/NzbDrone.Core/Download/ProcessDownloadDecisions.cs` (store runner-ups)
- `src/NzbDrone.Core/Download/RedownloadFailedDownloadService.cs` (try cache first)

New files: `src/NzbDrone.Core/Download/Fallback/*`,
`src/NzbDrone.Core/Housekeeping/Housekeepers/CleanupFailedDownloadFallbackPendingReleases.cs`,
tests under `src/NzbDrone.Core.Test/Download/Fallback/`.

## Using the image

The image is the stock `lscr.io/linuxserver/radarr` image with the binaries replaced, so swap the
image name and keep everything else:

```yaml
services:
  radarr:
    image: ghcr.io/trpostma/radarr:latest
    # ...same environment, volumes and ports as before
```

Tags: `latest`, `<upstream version>` (e.g. `6.4.4.10684`) and `<upstream version>-<patch sha>`.

If the GHCR package is private, run `docker login ghcr.io` on the host with a GitHub token that has
`read:packages`, or make the package public under
https://github.com/users/TRPostma/packages/container/radarr/settings.

## Staying current with upstream

`.github/workflows/fork-build.yml` runs daily (and on every push to `fallback-grab`):

1. Fetches upstream `master`, rebases `fallback-grab` onto it and force-pushes the result.
2. Builds the backend (linux-musl x64 + arm64) and the frontend at the upstream release version.
3. Publishes a multi-arch image to GHCR, skipping the build if that upstream version + patch
   combination was already published.

If a rebase conflicts, the run fails and GitHub emails you. Resolve it locally:

```bash
git fetch upstream master
git rebase upstream/master
# fix conflicts, then
git push --force-with-lease origin fallback-grab
```

To follow upstream `develop` instead of stable `master`, change `UPSTREAM_BRANCH` in the workflow and
rebase the branch onto `upstream/develop` once.

GitHub disables scheduled workflows after 60 days without repository activity; the daily rebase
push normally keeps it alive, otherwise re-enable it from the Actions tab.
