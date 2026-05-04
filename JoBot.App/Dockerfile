FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
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
RUN dotnet restore "JoBot.App/JoBot.App.csproj"
COPY . .
WORKDIR "/src/JoBot.App"
RUN dotnet build "./JoBot.App.csproj" -c $BUILD_CONFIGURATION --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./JoBot.App.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-build --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "JoBot.App.dll"]
