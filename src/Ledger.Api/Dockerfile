# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Ledger.Api/Ledger.Api.csproj -c Debug -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# vsdbg lets VS Code attach to the running container ("Docker: attach to API").
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl unzip procps \
    && curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l /vsdbg \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Ledger.Api.dll"]
