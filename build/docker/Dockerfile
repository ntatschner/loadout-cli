# Verifies the Linux half of the build on a machine that is not Linux.
#
# Every platform implementation below the seam is untestable from the host it
# was not written for, and "it compiles" is not the same claim as "it works".
# The Unix pseudo-terminal in particular forks, execs and drives a real tty:
# nothing about that is exercised by building it.
#
# Deliberately a plain image with no project-specific base. Spec section 1
# forbids a container from being part of how the launcher runs; this one exists
# only so a developer on Windows or macOS can run the Linux tests before pushing,
# and CI still runs them natively on its Ubuntu leg.

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
FROM ${SDK_IMAGE}

# dpkg-deb ships with the base image; rpm does not. Both are needed to build the
# Linux packages, and file/binutils are what verify the results are what they
# claim to be.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        rpm \
        file \
        binutils \
        git \
    && rm -rf /var/lib/apt/lists/*

# The packaging scripts are PowerShell so that one command works on all three
# Tier-1 platforms. That only holds if pwsh is actually present here too.
RUN dotnet tool install --global powershell --version 7.*
ENV PATH="${PATH}:/root/.dotnet/tools"

# Source is copied rather than mounted. A bind mount would drop the host's
# obj/ and bin/ into the container, where a Windows-built intermediate confuses
# the Linux build in ways that look like real failures.
WORKDIR /src
COPY . .

# Any host build output that slipped past .dockerignore is cleared, so what is
# built here is built here.
RUN find . -type d \( -name obj -o -name bin \) -prune -exec rm -rf {} + \
    && rm -rf artifacts

RUN dotnet restore --nologo

ENV DOTNET_NOLOGO=true
ENV DOTNET_CLI_TELEMETRY_OPTOUT=true

ENTRYPOINT ["/bin/bash", "/src/build/docker/verify.sh"]
