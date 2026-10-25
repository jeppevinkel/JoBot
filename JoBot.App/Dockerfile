# Ubuntu 26.04: the Opus library bundled with DSharpPlus.Voice needs glibc 2.43, newer than 24.04 provides
FROM mcr.microsoft.com/dotnet/runtime:10.0-resolute AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["JoBot.App/JoBot.App.csproj", "JoBot.App/"]
COPY ["JoBot.Discord/JoBot.Discord.csproj", "JoBot.Discord/"]
COPY ["JoBot.Core/JoBot.Core.csproj", "JoBot.Core/"]
COPY ["JoBot.Ai/JoBot.Ai.csproj", "JoBot.Ai/"]
COPY ["JoBot.Subsonic/JoBot.Subsonic.csproj", "JoBot.Subsonic/"]
COPY ["JoBot.Data/JoBot.Data.csproj", "JoBot.Data/"]
COPY ["JoBot.Services/JoBot.Services.csproj", "JoBot.Services/"]
COPY ["JoBot.YouTube/JoBot.YouTube.csproj", "JoBot.YouTube/"]
COPY ["JoBot.TextToSpeech/JoBot.TextToSpeech.csproj", "JoBot.TextToSpeech/"]
RUN dotnet restore "JoBot.App/JoBot.App.csproj"
COPY . .
WORKDIR "/src/JoBot.App"
RUN dotnet build "./JoBot.App.csproj" -c $BUILD_CONFIGURATION --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./JoBot.App.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-build --no-restore /p:UseAppHost=false

FROM base AS final
USER root
# Needed by the native voice backend (Voice__Backend=Native):
# - ffmpeg decodes audio
# - libssl-dev provides the unversioned libcrypto.so that DSharpPlus.Voice loads by name; the runtime
#   image only ships libcrypto.so.3
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg libssl-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir -p /app/data && chown $APP_UID:$APP_UID /app/data
USER $APP_UID
ENTRYPOINT ["dotnet", "JoBot.App.dll"]
