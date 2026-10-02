# Update Notifier Bot

This Discord bot monitors the RSS feed for game updates and notifies users when games on their watchlist receive new
updates.

## Key Features

**Game Update Monitoring**  
The bot periodically checks configured RSS feeds for new game updates, comparing
publication dates against stored records. When new updates are detected, it triggers notifications for subscribed
users.

**Watchlist Management**  
Users can manage personalized game watchlists through Discord commands:

- Add games to watchlist
- Remove games from watchlist
- View current watchlist

**Companion Extension**  
A Chromium extension that adds a button to add/remove games from the watchlist.
Needs the User Hash (get it with `/get_hash`) to work. Optionally sends you a discord notification that a game has been added or removed.
Get it [here!](https://github.com/InfiniteCanvas/Update-Notifier-Chromium-Extension/releases)
Download the release, unzip and load unpacked.

**Companion Website**  
A small website is served by the `web` container, on the same hostname as the API (Traefik
routes everything that is not `/api`, `/openapi` or `/scalar` to it). There you can create an
account with just a username and password (no email, no recovery - don't lose it), see your
tracked games sorted by last update, add/remove games with your hash (same as the extension),
link your Discord account, and manage/delete your account. Discord and web logins are two
identities attached to one shared account: one watchlist, one free-tier limit (69 games),
managed from either surface. Deleting the account (website or `/disable`) removes everything.

> **Extension users:** hashes are now random and were rotated once during the upgrade - if the
> extension stopped recognizing you, run `/get_hash` again and paste the new hash.

## Planned Supporter Features

- get updates on custom RSS feeds
- ~~add to watchlist directly from the thread using a plugin (more likely to be a tamper monkey script for now)~~
    - done for Chromium browsers

## Command Reference

| Command                            | Description                                                                                                    | Example                                                             |
|------------------------------------|----------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------|
| `/disable`                         | Disables the bot for you (deletes your data **and your linked website account** - not reversible)              | `/disable`                                                          |
| `/watch [URL1 URL2 ...]`           | Add games to watchlist                                                                                         | `/watch https://f95zone.to/threads/1 https://f95zone.to/threads/2`  |
| `/unwatch [URL1 URL2 ...]`         | Remove games from watchlist                                                                                    | `/unwatch https://f95zone.to/threads/1`                             |
| `/import_watchlist`                | Bulk-add games from an attachment (text file of URLs)                                                          | `/import_watchlist`                                                 |
| `/list`                            | Show your watched games (sorted by last update)                                                                | `/list`                                                             |
| `/get_hash`                        | Gets the hash associated with your account (needed for the extension)                                          | `/get_hash`                                                         |
| `/link [code]`                     | Link your Discord to your website account (code comes from the website's "Link Discord" section; merges watchlists) | `/link ABC123...`                                        |

Tracking a game works out of the box - your Discord user ID is stored to manage the watchlist
(and DM you updates). `/disable` deletes all of your data at any time.

## Architecture

The app is split into three containers behind your own Traefik (Docker label provider):

- **api** - the web API, the RSS monitor and the SQLite database (single owner), plus the
  durable pending-notification queue.
- **bot** - the Discord gateway and slash commands. It polls the api for pending
  notifications every `NOTIFICATION_POLL_INTERVAL` seconds and acks them after delivery,
  and pushes the privileged-user cache every `PRIVILEGE_SYNC_INTERVAL` minutes.
- **web** - an nginx container serving the static SPA build (no proxying; `/api` goes
  straight to the api via Traefik).

Notifications survive bot restarts: they are queued in the api's database and delivered
at-least-once when a bot polls; after 10 failed attempts a notification is dead-lettered.

Security model for the internal endpoints (`/api/internal/*`): Traefik never routes them
publicly (the api router rule excludes the prefix), callers must present the shared
`X-Internal-Api-Key` header, and their IP must be inside a CIDR allowlist (default:
loopback + RFC1918 + Tailscale CGNAT + IPv6 ULA; override with `INTERNAL_API_ALLOWED_CIDRS`).

## Docker Deployment

Prerequisites: an existing Traefik instance with the Docker label provider and an external
`traefik` network (create it once with `docker network create traefik` if you don't have one).

Set these in `.env` next to `compose.yaml`:

| Key                    | Required | Notes                                                          |
|------------------------|----------|----------------------------------------------------------------|
| TOKEN                  | yes      | Discord bot token                                              |
| GUILD_ID               | yes      | Your Discord server                                            |
| XF_USER / XF_SESSION   | no       | f95zone cookies                                                |
| INTERNAL_API_KEY       | yes      | Shared api/bot key: generate with `openssl rand -hex 32`       |
| DOMAIN                 | yes      | Public hostname Traefik routes to the web+api services         |
| SELF_HOSTED            | no       | `true` enables supporter features                              |
| RSS_UPDATE_INTERVAL    | no       | RSS check interval in minutes (default 5)                      |
| TRAEFIK_NETWORK        | no       | Traefik's docker network name (default `traefik`)              |
| TRAEFIK_ENTRYPOINT     | no       | Traefik entrypoint (default `websecure`)                       |
| TRAEFIK_CERTRESOLVER   | no       | Uncomment the tls label(s) in `compose.yaml` if you use one    |

Then:

```bash
docker compose up -d --build
```

Routing: Traefik sends `/api`, `/openapi` and `/scalar` (everything *except* `/api/internal`)
to the **api** container, and everything else on the host to the static **web** container.
The **bot** never goes through Traefik - it talks to the api directly over the compose
network (`API_BASE_URL=http://api:8080`) with the shared internal key.

When binding the `./data` mount, make sure to set permissions with
`sudo chown -R 1654:1654 /path/to/folder`.

## Configuration

Environment variables:

| Variable                   | Default          | Container | Description                                                                     |
|----------------------------|------------------|-----------|---------------------------------------------------------------------------------|
| DISCORD_BOT_TOKEN          | -                | bot       | Your discord bot token                                                          |
| DISCORD_GUILD_ID           | -                | bot       | Your discord server                                                             |
| DATABASE_PATH              | /data/app.db     | api       | SQLite db location                                                              |
| LOGS_FOLDER                | /data/logs       | api       | Folder where logs are put                                                       |
| RSS_UPDATE_INTERVAL        | 5                | api       | How often it checks the RSS feeds (in minutes)                                  |
| SELF_HOSTED                | false            | api, bot  | Basically makes you a supporter on your instance                                |
| XF_USER                    | -                | api       | cookies                                                                         |
| XF_SESSION                 | -                | api       | cookies (I had these because I got cucked by ratelimits as anon user for tests) |
| COOKIE_SECURE              | true             | api       | Mark the website session cookie `Secure`. Only set `false` for plain-HTTP LAN self-hosts. |
| INTERNAL_API_KEY           | - (required)     | api, bot  | Shared key for the `/api/internal` endpoints (`openssl rand -hex 32`)           |
| INTERNAL_API_ALLOWED_CIDRS | loopback + RFC1918 + CGNAT + ULA | api | Optional comma-separated CIDR allowlist for `/api/internal`          |
| API_BASE_URL               | http://api:8080  | bot       | Where the bot reaches the api (compose network)                                 |
| NOTIFICATION_POLL_INTERVAL | 5                | bot       | Seconds between pending-notification polls                                      |
| PRIVILEGE_SYNC_INTERVAL    | 15               | bot       | Minutes between privileged-user cache syncs                                     |
| DOMAIN                     | - (required)     | compose   | Public hostname Traefik routes to the web+api services                          |

## Running without the bot

Simply don't start the bot container (`docker compose up -d api web`, or comment the `bot`
service out): the API, RSS monitor and website keep working, and DM notifications
accumulate in the api's pending queue - they are delivered when a bot comes back online.
There is no `DISABLE_DISCORD` anymore; the bot is a separate container now.

## Deployment Notes

- **Public entry is Traefik only**: no ports are published; the api and web containers join
  your external `traefik` network and are routed by label rules. Configure the hostname via
  `DOMAIN` (and `TRAEFIK_NETWORK` / `TRAEFIK_ENTRYPOINT` / `TRAEFIK_CERTRESOLVER` if your
  setup needs them).
- **HTTPS for website login**: browsers refuse `Secure` cookies over plain `http://` (except
  `localhost`), so serve the stack through a TLS-terminating Traefik entrypoint if you want
  the website's login to work. For plain-HTTP LAN self-hosts set `COOKIE_SECURE=false`.
- **Single api replica only**: the api is the single owner/writer of the SQLite database
  (watchlist mutation locking and rate limiting are in-process). Do not run two replicas
  against the same database file.
- **Migrations**: run automatically at api boot. Before applying a pending migration the app
  copies the database to `<DATABASE_PATH>.pre-migration-<timestamp>` - the hash rotation that
  ships with this upgrade is irreversible, so keep that backup until you have verified
  everything works.
- **Website build**: the SPA is built inside the web image (node stage into nginx); the old
  manual `npm run build` + copy-into-`wwwroot/` flow is gone - the api serves no static
  files anymore.
- **Breaking change**: this replaces the previous single-container compose file. The stack
  is now api/bot/web behind Traefik, `DISABLE_DISCORD` no longer exists (stop the bot
  container instead), and port 8080 is no longer published.

