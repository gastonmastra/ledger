# Build context: repository root.
#   docker build -f src/Ledger.Api/Dockerfile .                      -> local image (Debug + vsdbg)
#   docker build -f src/Ledger.Api/Dockerfile --target production .  -> deployed image (Release)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Ledger.Api/Ledger.Api.csproj -c Debug -o /app

# Compiles on the build machine's architecture and targets the image's (fast arm64 images from x64 CI).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build-release
ARG TARGETARCH
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Ledger.Api/Ledger.Api.csproj -c Release -a $TARGETARCH -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS production
WORKDIR /app
COPY --from=build-release /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Ledger.Api.dll"]

# Last stage = default target (docker compose, "Docker: attach to API").
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# vsdbg lets VS Code attach to the running container ("Docker: attach to API").
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl unzip procps \
    && curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l /vsdbg \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Ledger.Api.dll"]
