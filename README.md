# <img src="assets/NSS-128x128.png" align="left" />IgorBot

[![Docker Image CI](https://github.com/nefarius/IgorBot/actions/workflows/docker-image.yml/badge.svg)](https://github.com/nefarius/IgorBot/actions/workflows/docker-image.yml)
[![.NET Tests](https://github.com/nefarius/IgorBot/actions/workflows/dotnet-tests.yml/badge.svg)](https://github.com/nefarius/IgorBot/actions/workflows/dotnet-tests.yml)
[![Requirements](https://img.shields.io/badge/Requirements-.NET%2010.0-blue.svg)](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
[![Docker Pulls](https://img.shields.io/docker/pulls/containinger/igor-bot)](https://hub.docker.com/r/containinger/igor-bot)
![Docker Image Version (latest by date)](https://img.shields.io/docker/v/containinger/igor-bot)
![Docker Image Size (latest by date)](https://img.shields.io/docker/image-size/containinger/igor-bot)

Advanced Discord bot to automate new member onboarding.

## Motivation

Discord's built-in anti-spam tools were insufficient to protect against mass joins of idle accounts that later post spam and scam links. An onboarding questionnaire separates humans from bots and filters out members who don't genuinely care about the community.

## Prerequisites

- **Option A**: Enable `auto_assign_stranger_role` in your guild config so Igor assigns the stranger role automatically. The bot needs **Manage Roles** and its role must be above the stranger role in the hierarchy.
- **Option B**: Use another bot (Carl, MEE6, …) to assign the stranger role on join.
- **Option C**: Assign the stranger role to new members manually.

## Setup

### Inviting the bot

Use this invite URL with fine-grained permissions (no Administrator). Replace `YOUR_CLIENT_ID` with your bot's
Application ID from the [Discord Developer Portal](https://discord.com/developers/applications):

```
https://discord.com/oauth2/authorize?client_id=YOUR_CLIENT_ID&permissions=268528662&scope=bot%20applications.commands
```

**Required permissions:** View Channel, Manage Channels, Manage Roles, Kick Members, Ban Members, Send Messages,
Embed Links, Read Message History, Manage Messages.

**Developer Portal intents** (Bot → Privileged Gateway Intents): enable **Server Members Intent** and
**Message Content Intent**.

**Role hierarchy:** Place the bot's role above both the stranger (Lurker) and member roles.

### Running the bot

It's recommended to use Docker and Docker Compose v2 to bring up the bot and its companion services.

- Copy `docker/docker-compose.example.yml` as `docker-compose.yml` to a directory of your choice and add your bot token
- Copy `docker/appsettings.Production.example.json` as `appsettings.Production.json` into the same directory
- Run `docker compose pull` to fetch the container images
- Run `docker compose up -d` to bring the bot and database services online

## Configuration

Configuration is **runtime-configurable** via Discord slash commands—no restart required. Guild config lives in MongoDB and can be migrated from `appsettings` on first run (see below).

### Quick start with `/config setup`

After inviting the bot to your server, run `/config setup` in Discord (Administrator permission required). This prompts you for:

| Option                       | Description                                                    | Default                         |
|------------------------------|----------------------------------------------------------------|---------------------------------|
| `stranger_role`              | Role assigned to new members before they complete onboarding   | —                               |
| `member_role`                | Role assigned when a member is promoted                        | —                               |
| `application_category`       | Category where newbie channels are created                     | —                               |
| `stranger_status_channel`    | Channel where application status embeds appear                 | —                               |
| `member_welcome_channel`     | Channel where welcome messages for promoted members appear     | —                               |
| `application_channel_format` | Format for newbie channel names (`{0}` = number)               | `newbie-{0:D4}`                 |
| `newbie_welcome_template`    | Welcome message template (`{0}` = member mention)              | *(standard onboarding prompt)*  |
| `member_welcome_template`    | Welcome message for promoted members (`{0}` = member mention)  | `Welcome {0}, enjoy your stay!` |
| `auto_assign_stranger_role`  | Automatically assign stranger role when member joins           | `false`                         |
| `idle_kick_minutes`          | Minutes before kicking inactive strangers (`0` = disabled)     | `0`                             |
| `honeypot_channel`           | (Optional) Channel that bans users who post in it              | —                               |
| `moderator_role`             | (Optional) Role that can see and interact with newbie channels | —                               |
| `enable_onboarding_workflow` | Run onboarding workflow when member gets stranger role         | `true`                          |

Use `/config view` to inspect the current configuration and `/config set` to update individual options. Available
options include: stranger role, member role, application category, stranger status channel, member welcome channel,
application channel format, templates, auto-assign stranger role, idle kick minutes, honeypot channel, moderator role,
**enable onboarding workflow**, **honeypot exclusion role (add)**, and **honeypot exclusion role (remove)**.

### Honeypot-only setup with `/config setup-honeypot`

For servers that only need the honeypot feature (no onboarding workflow), run `/config setup-honeypot`:

| Option                    | Description                            |
|---------------------------|----------------------------------------|
| `honeypot_channel`         | Channel that bans users who post in it    |
| `honeypot_exclusion_role`  | (Optional) Role exempt from honeypot ban |

This creates a minimal config. Add more exclusion roles later via `/config set` → **Honeypot exclusion role (add)** or remove them with **Honeypot exclusion role (remove)**.

### Per-guild feature toggles

Individual features can be enabled or disabled per guild:

| Feature              | Toggle / Config                       | Notes                                     |
|----------------------|---------------------------------------|-------------------------------------------|
| Join role assignment | `auto_assign_stranger_role`           | Assigns stranger role on join             |
| Onboarding workflow  | `enable_onboarding_workflow`          | When disabled: no channel/widget creation |
| Honeypot             | `honeypot_channel` / `setup-honeypot` | Works standalone                           |

### Optional: Initial config via appsettings

If `Bot:Guilds` is present in `appsettings.json` or `appsettings.Production.json`, guild configs are migrated once to MongoDB on startup; afterwards all configuration lives in MongoDB. (.NET uses colon-separated key paths for nested config, e.g. `Bot:Guilds`.) You can still add new guilds later via `/config setup` in Discord.

### Discord server preparations

All items below correspond to `/config setup` parameters (appsettings equivalents in parentheses).

- Add a stranger role (e.g. "Lurker") → `stranger_role` (`StrangerRoleId`)
- Add a member role (e.g. "Full Member") → `member_role` (`MemberRoleId`)
- Create a category (e.g. "Newbies") → `application_category` (`ApplicationCategoryId`)
- Add a **private** status channel → `stranger_status_channel` (`StrangerStatusChannelId`)
- Add a **public** welcome channel → `member_welcome_channel` (`MemberWelcomeMessageChannelId`)
- (Optional) Add a moderator role → `moderator_role` (`ApplicationModeratorRoleIds`)
- (Optional) Add a **honeypot** channel → `honeypot_channel`—users who post in it are automatically banned. For honeypot-only servers, use `/config setup-honeypot` instead of full setup.
- If using Option A (auto-assign): the bot needs **Manage Roles** and its role must be above the stranger role. Enable the **Guild Members** privileged intent in the [Discord Developer Portal](https://discord.com/developers/applications).

## Running tests

```bash
dotnet test tests/IgorBot.Tests/IgorBot.Tests.csproj
```

The test suite has two layers:

- **Pure unit tests** (`tests/IgorBot.Tests/Schema/`) — exercise `MemberLifecycleClassifier`, `GuildMemberStatusMigration.DeriveStatus`, the derived flags on `GuildMember`, `Reset()`, and `MemberStatus` ordinal pins. No external dependencies; runs instantly.
- **Integration tests** (`tests/IgorBot.Tests/Integration/`) — exercise `TransitionToAsync` and `GuildMemberStatusMigration.RunAsync` against a real embedded MongoDB instance. [EphemeralMongo](https://github.com/asimmon/ephemeral-mongo) downloads the mongod binary automatically on first run (no Docker or separate MongoDB installation required).

## How to build

```bash
docker build -t igor-bot:local .
```

Maintainers publishing the official image use:

```bash
docker buildx build --platform linux/amd64 --push -t containinger/igor-bot:latest .
```

## 3rd party credits

- [DSharpPlus](https://github.com/DSharpPlus/DSharpPlus)
- [Nefarius.DSharpPlus.Extensions.Hosting](https://github.com/nefarius/Nefarius.DSharpPlus.Extensions.Hosting)
- [MongoDB.Entities](https://mongodb-entities.com/)
- [Coravel](https://docs.coravel.net/)
- [MongoDB](https://www.mongodb.com/)
