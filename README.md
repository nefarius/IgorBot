# <img src="assets/NSS-128x128.png" align="left" />IgorBot

[![Docker Image CI](https://github.com/nefarius/IgorBot/actions/workflows/docker-image.yml/badge.svg)](https://github.com/nefarius/IgorBot/actions/workflows/docker-image.yml)
[![.NET Tests](https://github.com/nefarius/IgorBot/actions/workflows/dotnet-tests.yml/badge.svg)](https://github.com/nefarius/IgorBot/actions/workflows/dotnet-tests.yml)
[![Requirements](https://img.shields.io/badge/Requirements-.NET%2010.0-blue.svg)](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
[![Docker Pulls](https://img.shields.io/docker/pulls/containinger/igor-bot)](https://hub.docker.com/r/containinger/igor-bot)

Discord bot for member onboarding, moderator review, optional System One screening, and honeypot enforcement.

- [System One setup and screening recovery](docs/system-one.md)
- [Commands, maintenance jobs, and troubleshooting](docs/operations.md)

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

1. Copy [docker/docker-compose.example.yml](docker/docker-compose.example.yml) as `docker-compose.yml` to a directory of your choice and replace the bot-token placeholder.
2. Copy [docker/appsettings.Production.example.json](docker/appsettings.Production.example.json) as `appsettings.Production.json` into the same directory. The production template leaves guild configuration empty so you can use `/config setup`.
3. Run `docker compose pull`, then `docker compose up -d`.
4. Check startup and command registration with `docker compose logs --tail=100 -f igor-bot-app`.
5. Configure each Discord server using `/config setup` or `/config setup-honeypot` below.

The Compose example persists MongoDB under `./data/mongo/` and mounts the production settings read-only. The bundled base settings use `mongodb://igor-bot-db:27017/`, matching the Compose service name. For an external database, set `ConnectionStrings__MongoDB` in the app service environment. Keep the database data when updating the bot.

To update an existing deployment, run `docker compose pull` followed by `docker compose up -d`. [Operations](docs/operations.md#deployment-and-configuration-changes) explains restarts, environment changes, and configuration storage.

## Configuration

Guild settings are **runtime-configurable** through Discord slash commands and stored in MongoDB. Bot-wide settings, including the token, database connection, and System One endpoint, are loaded at startup and require an app restart or container recreation after changes.

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

`/config setup` **replaces the existing guild configuration**, including its questionnaire and screening settings. Use it for initial setup or an intentional reset; use `/config set` for individual changes.

Setup creates a Member questionnaire with one rules question, a three-minute timeout per answer, screening disabled, and submissions directed to the stranger status channel. It does not create the five-question questionnaire shown in the full appsettings example. For custom questions, a different submission channel, or DM delivery, see [questionnaire configuration](docs/operations.md#custom-questionnaires).

Use `/config view` to inspect the current configuration and `/config set` to update individual options. Available
options include: stranger role, member role, application category, stranger status channel, member welcome channel,
application channel format, templates, auto-assign stranger role, idle kick minutes, honeypot channel, moderator role,
**enable onboarding workflow**, **honeypot exclusion role (add)**, **honeypot exclusion role (remove)**,
**questionnaire screening**, **questionnaire screening context**, and **questionnaire auto-promote threshold**.

### Honeypot-only setup with `/config setup-honeypot`

For servers that only need the honeypot feature (no onboarding workflow), run `/config setup-honeypot`:

| Option                    | Description                            |
|---------------------------|----------------------------------------|
| `honeypot_channel`         | Channel that bans users who post in it    |
| `honeypot_exclusion_role`  | (Optional) Role exempt from honeypot ban |

For a new guild this creates a minimal config. For an existing guild it preserves its other settings, sets the honeypot, and disables the onboarding workflow; an existing auto-assign setting is preserved. Add more exclusion roles later via `/config set` → **Honeypot exclusion role (add)** or remove them with **Honeypot exclusion role (remove)**.

### Per-guild feature toggles

Individual features can be enabled or disabled per guild:

| Feature              | Toggle / Config                       | Notes                                     |
|----------------------|---------------------------------------|-------------------------------------------|
| Join role assignment | `auto_assign_stranger_role`           | Assigns stranger role on join             |
| Onboarding workflow  | `enable_onboarding_workflow`          | When disabled: no channel/widget creation |
| Honeypot             | `honeypot_channel` / `setup-honeypot` | Works standalone                           |
| Questionnaire screening | Member questionnaire automation    | Off until enabled; see below              |

### Optional System One screening

System One can approve high-confidence `normal` applications automatically. Screening is opt-in for each Member questionnaire; configuring an endpoint alone does not enable it. `bot`, `reject`, uncertain results, and failures remain available for moderator review. Screening itself never kicks or bans.

1. Set `Bot__SystemOne__BaseUrl` to the System One server root and restart/recreate the app. For PS5LM, use a URL such as `http://console-host:8081`, not the `/v1/systemone` path. Hosted services may also need a model name and `Bot__SystemOne__ApiToken`.
2. Set **Questionnaire screening context** and **Questionnaire auto-promote threshold** through `/config set` as needed. The default threshold is `0.80` (80%); enter a decimal probability, not a percentage string.
3. Run `/systemone test` to test inference and inspect screening settings, role permissions/hierarchy, and submission-channel access. The test does not change anyone's membership.
4. Enable **Questionnaire screening** through `/config set` with value `true`. Enabling it queues a waiting-applicant scan automatically.
5. To trigger a scan yourself, run `/jobs run` and choose **Pending applicant screening**. Job progress is logged by the bot.

The same screening workflow handles new submissions and recoverable waiting applications. Recovery also runs at guild startup/reconnect and every five minutes. Completed manual-review decisions and attempted promotions are not repeatedly reclassified or promoted. See the [System One guide](docs/system-one.md) for connection settings, privacy, retry behavior, and troubleshooting.

### Optional: Initial config via appsettings

[src/appsettings.Example.json](src/appsettings.Example.json) shows a complete bootstrap configuration, including a custom questionnaire. Replace every example guild, role, and channel ID before using it.

At startup, entries under `Bot:Guilds` are imported only for guilds not already stored in MongoDB. Existing guild configurations are left unchanged: editing the bootstrap file later will not update them. Guild settings then live in MongoDB; bot-wide settings remain in appsettings/environment/user secrets. See [configuration storage and migrations](docs/operations.md#configuration-storage-and-migrations).

### Discord server preparations

All items below correspond to `/config setup` parameters (appsettings equivalents in parentheses).

- Add a stranger role (e.g. "Lurker") → `stranger_role` (`StrangerRoleId`)
- Add a member role (e.g. "Full Member") → `member_role` (`MemberRoleId`)
- Create a category (e.g. "Newbies") → `application_category` (`ApplicationCategoryId`)
- Add a **private** status channel → `stranger_status_channel` (`StrangerStatusChannelId`)
- Add a **public** welcome channel → `member_welcome_channel` (`MemberWelcomeMessageChannelId`)
- (Optional) Add a moderator role → `moderator_role` (`ApplicationModeratorRoleIds`)
- (Optional) Add a **honeypot** channel → `honeypot_channel`. Eligible posts trigger bans; bots, the guild owner, and configured exclusion roles are exempt. For honeypot-only servers, use `/config setup-honeypot` instead of full setup.
- If using Option A (auto-assign): the bot needs **Manage Roles** and its role must be above the stranger role. Enable the **Guild Members** privileged intent in the [Discord Developer Portal](https://discord.com/developers/applications).

## Using the bot

Applicants run `/apply member` from their onboarding channel while holding the stranger role. Depending on the questionnaire configuration, Igor collects replies there or in DMs, posts the answers to the submission channel, and screens them if enabled.

Moderators use the status panel to **Promote**, **Kick**, **Ban**, or **Disable auto-kick**. Promote appears after a questionnaire is submitted. Successful promotion grants the member role, removes the stranger role, verifies the resulting roles, and sends the configured welcome message. Stranger-role removal triggers cleanup of the newbie channel and application widget.

When enabled, the idle timer applies to applicants who have not submitted a questionnaire. Answering temporarily disables the timer; timeout/cancellation can re-enable it. Submitted applicants and members holding the member role are protected from idle kicks, including the intermediate state where both roles are present.

Administrators can inspect settings with `/config view`, test screening with `/systemone test`, and queue maintenance through `/jobs run`. The [operations guide](docs/operations.md) lists every job, its schedule, scope, and troubleshooting steps.

## Local development

Install the .NET 10 SDK and make a MongoDB server available. The default connection string targets the Docker service name; override it when running the app directly:

```bash
dotnet user-secrets set --project src/IgorBot.csproj "Bot:Discord:Token" "YOUR_BOT_TOKEN"
dotnet user-secrets set --project src/IgorBot.csproj "ConnectionStrings:MongoDB" "mongodb://localhost:27017/"
dotnet run --project src/IgorBot.csproj
```

The checked-in launch profile selects `Development`, allowing the host to load user secrets. Never commit bot tokens or System One bearer keys. Configure guilds through Discord, or use a local `src/appsettings.Development.json` for first-time bootstrap data.

## Running tests

```bash
dotnet test tests/IgorBot.Tests/IgorBot.Tests.csproj
```

Unit tests cover lifecycle rules, command permissions, independent readiness checks, screening decisions, submission matching, and honeypot eligibility. Integration tests exercise configuration persistence/concurrency, lifecycle transitions, reactivation, widget behavior, honeypot enforcement, and the live/backlog screening pipeline against real temporary MongoDB databases.

The screening pipeline tests use the real System One HTTP client with controlled inference responses and a Discord boundary double. They cover request serialization, malformed responses, timeouts, configuration/role changes, repeated submissions, concurrent runs, partial-role compensation, and result retention after cleanup. They do not connect to a production model or change real Discord users.

[EphemeralMongo](https://github.com/asimmon/ephemeral-mongo) downloads `mongod` into its local cache on first use and starts the temporary test server; no separate MongoDB installation or Docker is required for tests. Allow network/cache access for that initial download.

## How to build

```bash
docker build -t igor-bot:local .
```

Successful Docker Image CI runs for pushes to `master` publish `containinger/igor-bot:latest`. Pull requests targeting `master` build the same `linux/amd64` image without pushing it. The separate .NET Tests workflow builds and tests the solution in Release mode and uploads test/coverage artifacts.

Maintainers must add a repository secret named `DOCKERHUB_TOKEN` containing a Docker Hub access token for the `containinger` account. The workflow uses that token to authenticate before publishing.

## 3rd party credits

- [DSharpPlus](https://github.com/DSharpPlus/DSharpPlus)
- [MongoDB.Entities](https://mongodb-entities.com/)
- [Coravel](https://docs.coravel.net/)
- [OllamaSharp](https://github.com/awaescher/OllamaSharp)
- [MongoDB](https://www.mongodb.com/)
