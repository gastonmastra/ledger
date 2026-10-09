# Build context: repository root.
# Compiles on the build machine's architecture and targets the image's (fast arm64 images from x64 CI).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Ledger.Migrator/Ledger.Migrator.csproj -c Release -a $TARGETARCH -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Ledger.Migrator.dll"]
