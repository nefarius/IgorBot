# Operations and maintenance

[Back to the README](../README.md) · [System One screening](system-one.md)

## Commands and moderation

Applicants use `/apply member` in a server while holding its configured stranger role. Replies are collected in the invoking channel or in DMs according to `ConductInPrivate`. Each question defaults to a three-minute timeout. `BlockedChannelIds` can disallow invocation in selected channels.

Administrators have these command groups:

- `/config setup`: create or intentionally replace the guild's complete configuration. Replacement resets the questionnaire to one rules question, uses the status channel for submissions, and disables screening.
- `/config setup-honeypot`: configure a honeypot and disable onboarding. For an existing configuration, other settings and existing exclusion roles are preserved; auto-assign is not automatically disabled.
- `/config view`: inspect the current guild settings, including screening status, threshold, and context.
- `/config set`: edit a supported option. Role/channel values accept IDs or mentions; booleans use `true`/`false`. Idle kick uses minutes, with `0` disabling it. **Moderator role (add)** adds a role; honeypot exclusion roles have separate add/remove options. A honeypot-channel value with no parseable ID clears the honeypot setting.
- `/systemone test`: test decision inference and report guild readiness without changing membership.
- `/jobs run`: queue a maintenance job across all configured servers. The response confirms queuing, not completion. Read the bot log for results.

These administrative slash commands require the invoking user to have Administrator. The bot needs the relevant role/channel/moderation permissions; it does not require Administrator.

Moderator status panels expose Kick and Ban, plus Promote after questionnaire submission and Disable auto-kick while the idle timer is enabled. Application buttons remain associated with their specific panel; stale interactions must not recreate a detached widget. Submitted applicants are protected from idle kicks. The intermediate state with both stranger and member roles also disables idle kicks without marking the member fully promoted until the stranger role is removed.

## Maintenance jobs

Select a job by its displayed name in `/jobs run`:

- **Pending applicant screening**: recovers unscreened/unavailable submitted applications and runs System One when enabled. Automatic triggers: enabling screening, guild availability at startup/reconnect, and every five minutes. Completed decisions and attempted promotions stay with moderators. See the [screening guide](system-one.md).
- **Lurker reconciliation**: repairs missing onboarding channel/widget resources for non-bot members holding only the stranger role when onboarding is enabled. Automatic triggers: guild availability and daily. It queues at most 50 members per guild per scan; repeat the job for a larger backlog. It does not screen completed questionnaires.
- **Member database sync**: discovers current non-bot guild members, refreshes display names, and clears obsolete newbie-channel references after confirming Discord deletion. Queued at host startup; guilds not yet available can be skipped. Run it manually once connected if needed. It is not a scheduled daily job and does not itself create missing onboarding resources.
- **Orphan embed reconciliation**: reconciles orphan application-widget records and missing Discord widget messages. Automatic schedule: daily. Newly created records have a two-minute grace period.
- **Honeypot reconciliation**: scans honeypot history and resumes persisted progress to handle posts missed by live events. Automatic schedule: every minute. Scans are bounded and continue across runs rather than fetching all history at once.
- **Kick stale strangers**: applies each guild's configured idle timeout to eligible applicants who have not submitted a questionnaire and still have auto-kick enabled. Automatic schedule: every minute. Current member roles and persisted eligibility are checked before kicking.

Individual jobs also apply their own guild availability and feature checks. Seeing a disabled-feature skip for one server does not imply the job failed for another. Overlap guards can skip a duplicate invocation while the original work continues.

## Honeypot behavior

An eligible user posting in the configured honeypot is banned independently of questionnaire screening. Bot accounts, the guild owner, and members holding an exclusion role are exempt. Other human members, including moderators without an exclusion role, are evaluated by the same rules; Discord's permissions and hierarchy still determine whether a ban can succeed.

The history job stores its cursor/continuation state in MongoDB. With no checkpoint, its forward scan starts at the later of the last 24 hours or the recorded channel activation time. Existing checkpoints resume across restarts, subject to activation bounds. The job also performs bounded backlog cleanup. Changing the honeypot through the configuration service stamps its activation time; preserve that state when backing up or restoring the database.

## Deployment and configuration changes

For the supplied Compose deployment:

```bash
docker compose pull
docker compose up -d
docker compose logs --tail=100 -f igor-bot-app
```

Guild slash-command edits apply without restarting. Bot-wide connection settings are read at startup:

- Recreate the app with `docker compose up -d igor-bot-app` after editing Compose environment values.
- Restart the app after changing a mounted appsettings file's bot-wide values. Changes to bootstrap guild entries still do not overwrite existing database configuration.
- A Compose update retains the database bind mounts under `./data/mongo/`. Back up MongoDB and keep its data when replacing the app container.

The checked-in production example is deliberately an empty guild configuration. The base appsettings supplies `ConnectionStrings:MongoDB = mongodb://igor-bot-db:27017/`; override this for a different database host. Bot token: `Bot__Discord__Token`. Screening endpoint: `Bot__SystemOne__BaseUrl`. See [all screening connection settings](system-one.md#connect-the-endpoint).

## Configuration storage and migrations

Igor uses the MongoDB database named `IgorBot`. Guild configuration is represented by `GuildConfigEntity` and identified by the guild ID string. Runtime configuration edits use a version token (`ConfigVersion`) to detect competing writes. A conflict response means nothing was saved; retry the command rather than replacing the whole configuration with `/config setup`.

The startup bootstrap migration reads `Bot:Guilds` and imports guild IDs not already stored. It runs on subsequent startups too, but skips existing guild configuration. Thus appsettings is an initial seed, not the ongoing source of truth for guild settings. The full [appsettings example](../src/appsettings.Example.json) is useful before the first import; replace all its illustrative IDs.

A separate `GuildMemberStatusMigration` derives canonical lifecycle states for legacy member records. `Bot:Migration:DryRun` defaults to `false`; set it to `true` to inspect that migration without writing its status changes. The histogram is logged at Information, but individual proposed status changes are logged at Debug. To inspect those proposals, enable Debug logging before startup, for example with `Serilog__MinimumLevel__Default=Debug` in the environment or `Serilog:MinimumLevel:Default` set to `Debug` in appsettings.

This is **not** a dry run of the bot: Discord connection, other startup work, and guild bootstrap migration still run. Set `Bot:Migration:DryRun` back to `false` and restart to apply the status migration. Records already carrying a non-Unknown status are skipped.

Rejoins use an atomic lifecycle reset, detach old channel/application references, and clear the previous screening result. Completed screening decisions survive normal widget cleanup; recoverable retries with no new decision retain the previous result until replacement.

## Custom questionnaires

The slash commands currently configure screening policy, not question text, timeout, DM delivery, or the questionnaire's submission channel. A new `/config setup` creates only the default rules question.

For first-time bootstrap, customize `Bot:Guilds:<guild-id>:Questionnaires:Member` in your local appsettings using [the example](../src/appsettings.Example.json). Relevant fields are `Id`, `Name`, `Description`, `Questions` (`Content` per question), `SubmissionChannelId`, `TimeoutMinutes`, `ConductInPrivate`, `BlockedChannelIds`, and `Automation`.

For an already imported guild, those settings must be changed in its persisted MongoDB configuration. Stop the bot and back up the database before direct maintenance; update `ConfigVersion` when editing the configuration, then restart. Editing an appsettings seed or rerunning `/config setup` is not a way to preserve and update a custom questionnaire.

Use relevant questions and context when enabling screening: the classifier only receives what the questionnaire asks. Questionnaire fields and answers must also fit Discord embed limits. Keep original submission messages available for screening recovery. Changing question fields can make older answers unrecoverable automatically; those applicants need moderator review.

## Dependency maintenance

[Dependabot](../.github/dependabot.yml) checks NuGet packages, GitHub Actions, and the root Dockerfile's base images weekly on Mondays at 09:00 Europe/Vienna, with up to five open version-update PRs per ecosystem. NuGet uses the root central package configuration and solution projects/lock files.

DSharpPlus packages are grouped together because their nightly versions should stay aligned. Other NuGet and Actions minor/patch updates are grouped; their major updates remain individual PRs. The Dockerfile's .NET SDK and runtime images are grouped so they can be reviewed together. A .NET image major-version PR still needs review against the project's target framework; grouping does not update that framework automatically.

The configuration does not enable auto-merge or suppress major-version updates. The deployment Compose file is an example, not an active production manifest managed by this Dockerfile entry. Review and apply any required Compose image changes to your deployment separately.

## Troubleshooting

- **No new onboarding channel**: check workflow/auto-assign flags, stranger-role assignment, category and status-channel IDs, bot permissions, and configured moderator roles. If moderator role IDs are configured but none resolve, channel creation is skipped. Use **Lurker reconciliation** for eligible members missed while the bot was offline.
- **No Promote button**: Promote requires a recorded questionnaire submission. Buttons belong to the status panel, not the questionnaire submission message. Check the member's state and application association before retrying a stale interaction.
- **An applicant is waiting despite screening being enabled**: run `/systemone test` and inspect the Screening field and logs. A completed manual-review decision or failed promotion attempt is intentionally not automatically retried. [Screening troubleshooting](system-one.md#troubleshooting) distinguishes retryable failures from moderator actions.
- **Configuration edits seem ignored**: guild settings are in MongoDB after import. Use `/config view` and `/config set`. Bot-wide environment changes need container recreation.
- **Honeypot posts were missed during downtime**: run **Honeypot reconciliation**, verify Read Message History and Ban Members, and allow successive scans to advance their persisted progress.
