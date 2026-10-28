# JoBot

Detailed docs: [https://docs.jobot.jeppdev.com/](https://docs.jobot.jeppdev.com/)

A Discord bot built on .NET 10 with Claude AI integration, music streaming, and SQLite persistence.

## Project Structure

| Project | Description |
|---|---|
| `JoBot.App` | Entry point. Wires up all services and hosts the application. |
| `JoBot.Core` | Shared interfaces and abstractions referenced by all other projects. |
| `JoBot.Ai` | Claude AI integration via the Anthropic SDK. |
| `JoBot.Discord` | Discord bot logic using DSharpPlus, with audio via Lavalink4NET or natively via DSharpPlus.Voice. |
| `JoBot.Data` | EF Core data access layer with SQLite. Contains migrations. |
| `JoBot.Services` | Business logic and configuration services. |
| `JoBot.Subsonic` | Subsonic music server client integration. |
| `JoBot.YouTube` | YouTube music integration, exposes music tools via Lavalink. |
| `JoBot.Transcription` | Client for a self-hosted speech-to-text server, used to listen in voice channels. |

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

## Voice Backends

Audio can be played through one of two backends, chosen with `Voice__Backend`:

| Backend | Description |
|---|---|
| `Lavalink` (default) | Audio is decoded and sent by a separate Lavalink server. Requires `Lavalink__Passphrase`. |
| `Native` (experimental) | Audio is decoded locally with ffmpeg and sent with DSharpPlus.Voice, which supports Discord's DAVE end-to-end encryption. No Lavalink server is needed. TTS is mixed over the music, which is quietly ducked underneath, instead of interrupting it. |

To use the native backend, set `Voice__Backend=Native` and drop the `lavalink` service and `Lavalink__*` variables from the compose file. The Docker image includes ffmpeg; when running outside Docker, `ffmpeg` and `ffprobe` must be on the `PATH`, or set `Voice__FfmpegPath` / `Voice__FfprobePath`.

With either backend, the bot leaves a voice channel after it has been there for 10 minutes without any other non-bot users. Change this with `Voice__AloneTimeout` (e.g. `00:30:00`), or set it to `00:00:00` to stay indefinitely.

## Voice Listening (experimental)

With the native backend, the bot can listen in voice channels and answer when spoken to. Everything said is sent to a self-hosted speech-to-text server. When a transcript contains the bot's name (its server nickname, its username, or anything in `VoiceListening__WakeWords`), the transcript goes to the AI. The reply is spoken with TTS and posted in the voice channel's text chat. For `VoiceListening__FollowUpWindow` (20 seconds by default) after the bot answers someone, that person can keep talking to it without saying its name. A transcript that is nothing but the bot's name is ignored: the speech-to-text server is primed with the name, so it tends to "hear" it in background noise.

Only enable this where everyone in the voice channel knows the bot is listening.

The transcription server is optional and can come and go. While it's unreachable, the bot ignores voice audio and carries on as normal. It checks the server's health endpoint every 30 seconds and picks it up again when it's back.

### Transcription server

Any server with an OpenAI-compatible `/v1/audio/transcriptions` endpoint works. [speaches](https://speaches.ai) with Whisper large-v3-turbo is a good default on an NVIDIA GPU. The host needs the [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html):

```yaml
services:
  speaches:
    image: ghcr.io/speaches-ai/speaches:latest-cuda
    restart: unless-stopped
    ports:
      - 8000:8000
    environment:
      # Download the model on startup, and keep it in VRAM instead of unloading it after 5 minutes idle
      - PRELOAD_MODELS=["deepdml/faster-whisper-large-v3-turbo-ct2"]
      - STT_MODEL_TTL=-1
    volumes:
      - hf-hub-cache:/home/ubuntu/.cache/huggingface/hub
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]

volumes:
  hf-hub-cache:
```

Older speaches images ignore `PRELOAD_MODELS`; if the bot logs `Model '…' is not installed locally`, download it once (it's kept in the volume):

```bash
curl -X POST http://localhost:8000/v1/models/deepdml/faster-whisper-large-v3-turbo-ct2
```

### Bot configuration

```yaml
      - Voice__Backend=Native
      - VoiceListening__Enabled=true
      - Transcription__BaseUrl=http://192.168.1.50:8000  # Address of the transcription server

      # Optional (defaults shown)
      - Transcription__Model=deepdml/faster-whisper-large-v3-turbo-ct2
      - Transcription__Language=            # e.g. da or en; empty = detect per utterance
      - VoiceListening__WakeWords__0=       # Extra names, e.g. common mis-transcriptions like "joe bot"
      - VoiceListening__FollowUpWindow=00:00:20
      - VoiceListening__SpeakReplies=true
      - VoiceListening__PostReplies=true
      - VoiceListening__PostTranscripts=true # Post what the bot heard before it answers
```

Setting `Transcription__Language` is worth it if everyone speaks the same language: detecting the language on short utterances is unreliable. The bot needs permission to send messages in the voice channel for posting replies and transcripts.

When the bot joins a channel with listening enabled, it logs `Listening in voice channel …`, and each time someone addresses it, `User … spoke to the bot: …` with the transcript.

If it doesn't react, set `Serilog__MinimumLevel__Override__JoBot.Discord.Voice=Debug`. It then also logs everything it hears, which DAVE (end-to-end encryption) keys it has, which users it has identified as speaking, and once a minute while people talk, a summary of how many audio frames arrived, how many failed decryption or decoding, and how many utterances were sent for transcription. That shows where things break down.

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
