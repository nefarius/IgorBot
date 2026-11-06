# System One screening

[Back to the README](../README.md)

System One classifies completed Member questionnaires. Igor automatically promotes an applicant only when the selected label is `normal` and its probability meets the configured threshold. `bot` and `reject` are recommendations for moderator review, not automatic kicks or bans. The honeypot and idle-kick features operate separately.

## Connect the endpoint

Connection settings apply to the whole bot and are read at startup:

- `Bot:SystemOne:BaseUrl`: System One server root; empty disables the client. Use `http://console-host:8081` for a reachable PS5LM server, not a URL ending in `/v1/systemone`.
- `Bot:SystemOne:Model`: defaults to `loaded`. PS5LM uses its loaded model; configure the model required by other servers.
- `Bot:SystemOne:ApiToken`: optional bearer token. Igor sends `Authorization: Bearer <token>` when configured.
- `Bot:SystemOne:TimeoutSeconds`: positive request timeout, default `10`.

In Docker Compose, the corresponding environment names use double underscores:

HTTP is for protected local connections only. Across untrusted networks, use HTTPS or an encrypted tunnel; otherwise bearer tokens and questionnaire answers may be exposed.

```yaml
environment:
  - Bot__SystemOne__BaseUrl=http://console-host:8081
  - Bot__SystemOne__Model=loaded
  # Add only when required by your endpoint:
  # - Bot__SystemOne__ApiToken=<your-bearer-token>
```

The address must be reachable from the bot container. `localhost` inside that container refers to the container itself. Keep tokens in environment variables or development user secrets rather than committed settings files. Apply Compose environment changes with `docker compose up -d igor-bot-app`; restarting the existing container alone does not replace its environment.

The background startup probe checks `/health` and retries transient connection errors, timeouts, and HTTP 408/429/5xx responses with exponential backoff. With the default timeout, retries can take roughly two to three minutes; timing varies with backoff jitter and request duration. A successful health probe confirms health-endpoint access, not inference, guild configuration, or role permissions.

## Configure the questionnaire

The server needs an enabled onboarding workflow and a Member questionnaire. Use `/config setup` only for initial setup or an intentional reset: it replaces the guild configuration, resets the questionnaire to a minimal rules-only question, and disables screening. Use `/config set` for supported changes to an existing configuration; a custom multi-question application requires [bootstrap or persisted questionnaire configuration](operations.md#custom-questionnaires).

Screening expects a good-faith reason for joining as well as acknowledgement of the rules. A rules-only answer without that information may be sent for manual review. Ask why the applicant wants to join when configuring a questionnaire for automatic screening.

Use `/config set` and select these options:

1. **Questionnaire screening context**: explain the server and the reasons people may legitimately join. Limit: 1,000 characters. If unset, the request uses the questionnaire description as context.
2. **Questionnaire auto-promote threshold**: a decimal probability from `0` to `1`, such as `0.80`. The default is 80%. This checks the `normal` probability, not the separate model confidence value.
3. **Questionnaire screening**: `true` enables screening, `false` leaves applications with moderators. Existing questionnaires default to disabled.

Before enabling, run `/systemone test`. It sends a canned application and reports:

- Whether the decision endpoint returned a valid verdict, plus model, elapsed time, probabilities, and confidence.
- The current guild's screening flag and threshold.
- Whether the configured roles exist, are distinct and assignable, and are below the bot's highest role; whether the bot has Manage Roles.
- Whether the submission channel is unconfigured, missing, outside the guild, or accessible with View Channel, Read Message History, Send Messages, and Embed Links.

Role and channel checks report independently. The test never approves, kicks, or bans a member. A successful canned inference does not guarantee any particular verdict for a real application. `/config view` shows the saved screening settings and context.

## New submissions and waiting applicants

After a submission is delivered to Discord, Igor atomically records its message reference and the submitted application state. New submissions and backlog scans use the same workflow: read current configuration and roles, recover the posted answers, classify, reserve the result/promotion atomically, and run the promotion service. Invalid or incomplete classifier responses remain available for manual review.

Waiting-applicant scans run:

- When **Questionnaire screening** is set to `true` through `/config set`.
- When a guild becomes available at startup or reconnect.
- Every five minutes.
- On demand: `/jobs run`, job **Pending applicant screening**.

The manual command scans all configured guilds, applying each guild's own feature settings. It replies when the job is queued; progress and completion counts are in the bot logs. A scan reporting `0 promoted` can mean earlier scans already handled the waiting applications.

Recovery considers `QuestionnaireSubmitted` applicants with no decision or a `Disabled`/`Unavailable` decision, provided automatic promotion has not already been attempted. Completed manual-review decisions, including `bot`, `reject`, and below-threshold `normal`, remain with moderators. Repeating `/apply member` does not erase those decisions. A retry with no new decision clears the active retry's decision while retaining the previous screening result in `LastOnboardingDecision` until a new result is available.

Classifier outages, unavailable guilds, configuration changes during inference, and promotion-preflight failures can be picked up by a later scan. An attempted promotion that fails needs moderator action; the scan does not repeatedly apply role mutations. If stranger-role removal fails, Igor checks whether it actually succeeded and otherwise attempts to undo a newly granted member role only when the original application and lifecycle are still current. Pre-existing member roles and concurrent lifecycle changes are preserved. A compensation failure is logged for moderator repair.

Successful promotion verifies the member role is present and the stranger role is absent. Gateway events clean up the newbie channel and application widget. The submission's Screening field is updated, and the latest screening result remains on the member after widget detachment. Reactivation on a new join clears that previous result.

## Answer recovery and data

Answers remain in Discord submission embeds rather than being copied into MongoDB. MongoDB stores submission message/channel references and decision metadata: outcome, label, model, probabilities, confidence, threshold, timestamps, failure details, and promotion flags.

For older applications without message references, Igor searches the configured submission channel from five minutes before to one minute after the saved submission timestamp. A candidate must be bot-authored, identify the applicant, link to the exact application panel, and contain the current question fields in order. Multiple matching submissions, incomplete answers, changed question fields, deleted messages, or inaccessible history require moderator review. The search is bounded; it does not download unlimited history.

The inference request includes the questionnaire name, description, screening context, and submitted question/answer text. Igor does not add the applicant's Discord ID or username to that request. Applicant-written answers may themselves contain identifying information.

## Troubleshooting

- **`screening disabled`**: inspect `/config view` in the affected server. The endpoint and the per-questionnaire flag are separate settings. Also check that the onboarding workflow is enabled. Startup messages can predate a later config change.
- **Health succeeds but inference fails**: run `/systemone test`. Check the model, authentication, timeout, and decision API independently of `/health`.
- **`Screening unavailable`**: inspect the displayed failure and bot log. Endpoint and role-preflight failures remain retryable. Correct the cause and wait for the next scan or queue the job manually.
- **No complete, unambiguous submission recovered**: verify the saved submission still exists and is readable, and that the questionnaire's question fields have not changed. Approve manually when the original answers cannot be recovered safely.
- **Automatic promotion failed**: fix permissions/hierarchy or reconcile partial roles, then use the moderator panel. The attempted-promotion guard deliberately prevents repeated automatic role changes.
- **`already running, skipping`**: another invocation owns the same scan/member operation. Startup availability/download events and manual/scheduled runs may overlap; the guard avoids duplicate processing.

See [operations](operations.md) for the remaining jobs and configuration storage rules.
