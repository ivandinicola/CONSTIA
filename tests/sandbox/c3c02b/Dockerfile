# Official MCR metadata checked 2026-10-24; no image was downloaded in B2-A.
FROM mcr.microsoft.com/dotnet/sdk@sha256:8dd1a57fe6b7b8b1b357635db67e272c2f9e9d76d1dd0a32711c4c33c57d7b43 AS build
ARG CODE_SHA
WORKDIR /source
COPY . .
RUN test -n "$CODE_SHA" && dotnet publish tools/Constia.C3C02B.Harness/Constia.C3C02B.Harness.csproj -c Release -o /app/harness -p:SourceRevisionId="$CODE_SHA" && dotnet publish tests/Constia.C3C02B.Tests/Constia.C3C02B.Tests.csproj -c Release -o /app/tests -p:SourceRevisionId="$CODE_SHA"
FROM mcr.microsoft.com/dotnet/sdk@sha256:8dd1a57fe6b7b8b1b357635db67e272c2f9e9d76d1dd0a32711c4c33c57d7b43
WORKDIR /app
ENV HOME=/run/c3/home DOTNET_CLI_HOME=/run/c3/home DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
COPY --from=build /app /app
# No Docker socket, host mounts, network tools, provider secrets or API server.
CMD ["sleep", "infinity"]
