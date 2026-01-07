# ===== STAGE 1: build/publish =====
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

ARG BUILD_CONFIGURATION=Release

COPY src/Worker/GamesWorker.csproj src/Worker/
RUN dotnet restore src/Worker/GamesWorker.csproj

COPY src/ src/
RUN dotnet publish src/Worker/GamesWorker.csproj -c $BUILD_CONFIGURATION -o /app /p:UseAppHost=false

# ===== STAGE 2: runtime =====
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final
WORKDIR /app

# Instalar certificados e timezone
RUN apk add --no-cache icu-libs tzdata

COPY --from=build --chown=app:app /app .

ENV DOTNET_EnableDiagnostics=false
ENV DOTNET_TieredPGO=1
ENV DOTNET_TC_QuickJitForLoops=1
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

USER app

ENTRYPOINT ["dotnet", "GamesWorker.dll"]

