# Fork image: the stock linuxserver.io Radarr image with its binaries swapped for this fork's build.
# Everything else (s6 init, PUID/PGID handling, /config layout, ports) is unchanged, so this image is a
# drop-in replacement for lscr.io/linuxserver/radarr.
ARG BASE_IMAGE=lscr.io/linuxserver/radarr:latest
FROM ${BASE_IMAGE}

ARG TARGETARCH
ARG RADARR_VERSION=unknown
ARG PATCH_SHA=unknown

COPY ${TARGETARCH}/Radarr /app/radarr/bin.fork

RUN rm -rf /app/radarr/bin && \
    mv /app/radarr/bin.fork /app/radarr/bin && \
    rm -rf /app/radarr/bin/Radarr.Update && \
    chmod +x /app/radarr/bin/Radarr && \
    printf 'UpdateMethod=docker\nBranch=master\nPackageVersion=%s\nPackageAuthor=fallback-grab fork (%s)\n' "${RADARR_VERSION}" "${PATCH_SHA}" > /app/radarr/package_info
