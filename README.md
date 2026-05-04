# JoBot

A Discord bot built on .NET 10 with Claude AI integration, music streaming, and SQLite persistence.

## Project Structure

| Project | Description |
|---|---|
| `JoBot.App` | Entry point. Wires up all services and hosts the application. |
| `JoBot.Core` | Shared interfaces and abstractions referenced by all other projects. |
| `JoBot.Ai` | Claude AI integration via the Anthropic SDK. |
| `JoBot.Discord` | Discord bot logic using DSharpPlus with audio support via Lavalink4NET. |
| `JoBot.Data` | EF Core data access layer with SQLite. Contains migrations. |
| `JoBot.Services` | Business logic and configuration services. |
| `JoBot.Subsonic` | Subsonic music server client integration. |
| `JoBot.YouTube` | YouTube music integration, exposes music tools via Lavalink. |

## Docker Compose

A minimal setup with Lavalink as a sidecar:

```yaml
services:
  JoBot:
    restart: unless-stopped
    image: ghcr.io/jeppevinkel/jobot:latest
    depends_on:
      - lavalink
    environment:
      # Required
      - Anthropic__Token=${ANTHROPIC_API_KEY}
      - Discord__Token=${DISCORD_TOKEN}
      - Lavalink__Passphrase=${LAVALINK_PASSPHRASE}
      - Subsonic__BaseUrl=${SUBSONIC_BASE_URL}
      - Subsonic__Username=${SUBSONIC_USERNAME}
      - Subsonic__Password=${SUBSONIC_PASSWORD}
      - ConnectionStrings__JoBot=Data Source=/app/config/jobot.db

      # Optional — Lavalink (defaults shown)
      - Lavalink__BaseAddress=http://lavalink:2333

      # Optional — AI tuning (defaults shown)
      - Ai__Model=claude-sonnet-4-6
      - Ai__MaxTokens=4096
      - Ai__Temperature=0.7
      - Ai__MaxToolIterations=50
      - Ai__MaxHistoryMessages=40

      # Optional — per-guild defaults (defaults shown)
      - GuildDefaults__MaxHistoryMessages=20
      - GuildDefaults__AiTemperature=0.7
      - GuildDefaults__MusicVolume=0.5

  lavalink:
    image: ghcr.io/lavalink-devs/lavalink:4
    environment:
      - LAVALINK_SERVER_PASSWORD=${LAVALINK_PASSPHRASE}
```

Copy the snippet above to a `docker-compose.yml`, create a `.env` file with the required variables, and run:

```bash
docker compose up -d
```

## Database Migrations

Migrations are managed with EF Core. Run commands from the solution root:

```bash
# Add a new migration
dotnet ef migrations add <migration-name> --project JoBot.Data

# Apply pending migrations
dotnet ef database update --project JoBot.Data

# Remove the last migration (if not yet applied)
dotnet ef migrations remove --project JoBot.Data

# List all migrations
dotnet ef migrations list --project JoBot.Data
```
