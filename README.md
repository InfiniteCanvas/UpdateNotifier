# Update Notifier Bot

A Discord bot that watches the f95zone RSS feeds for game updates and DMs you when something on your watchlist gets
one. That's it. That's the bot.

## What it does

**Update watching**  
Every few minutes the bot pulls the configured RSS feeds, compares them against what it has seen
before, and pings everyone tracking a game that just got an update.

**Watchlist management**  
Your watchlist is just Discord commands:

- add games
- remove games
- see what you're tracking

**Companion Extension**  
A Chromium extension that puts an add/remove button right on the thread page.
Needs your User Hash (grab it with `/get_hash`) to work, and can optionally DM you when it adds or removes something.
Get it [here!](https://github.com/InfiniteCanvas/Update-Notifier-Chromium-Extension/releases) -
unzip the release and load it unpacked.

**Companion Website**  
The `web` container serves a small site on the same hostname as the API (Traefik sends it
everything that isn't `/api`, `/openapi` or `/scalar`). Make an account with just a username and
password (no email, no recovery - forget the password and it's gone), see your tracked games
sorted by last update, add/remove games with your hash (same as the extension), link your Discord
account, or delete the account. Discord and web logins are two identities on one shared account:
one watchlist, one limit (6969 games - an anti-spam cap, not a paywall), usable from either side.
Deleting the account (website or `/disable`) wipes everything.

> **Extension users:** hashes are now random and were rotated once during the upgrade - if the
> extension stopped recognizing you, run `/get_hash` again and paste the new hash.

## Planned Features

- get updates on custom RSS feeds
- ~~add to watchlist directly from the thread using a plugin (more likely to be a tamper monkey script for now)~~
    - done for Chromium browsers

## Command Reference

| Command                    | Description                                                                                                         | Example                                                            |
|----------------------------|---------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------|
| `/disable`                 | Disables the bot for you (deletes your data **and your linked website account** - not reversible)                   | `/disable`                                                         |
| `/watch [URL1 URL2 ...]`   | Add games to watchlist                                                                                              | `/watch https://f95zone.to/threads/1 https://f95zone.to/threads/2` |
| `/unwatch [URL1 URL2 ...]` | Remove games from watchlist                                                                                         | `/unwatch https://f95zone.to/threads/1`                            |
| `/import_watchlist`        | Bulk-add games from an attachment (text file of URLs)                                                               | `/import_watchlist`                                                |
| `/list`                    | Show your watched games (sorted by last update)                                                                     | `/list`                                                            |
| `/get_hash`                | Gets the hash associated with your account (needed for the extension)                                               | `/get_hash`                                                        |
| `/link [code]`             | Link your Discord to your website account (code comes from the website's "Link Discord" section; merges watchlists) | `/link ABC123...`                                                  |

Tracking a game works out of the box - your Discord user ID gets stored so the bot knows whose watchlist it is (and
where to send the DMs). `/disable` deletes all of your data at any time.

## Architecture

Three containers behind your own Traefik (Docker label provider):

- **api** - the web API, the RSS monitor and the SQLite database (single owner), plus the
  durable pending-notification queue.
- **bot** - the Discord gateway and slash commands. It polls the api for pending
  notifications every `NOTIFICATION_POLL_INTERVAL` seconds and acks them after delivery,
  and pushes the privileged-user cache every `PRIVILEGE_SYNC_INTERVAL` minutes.
- **web** - an nginx container serving the static SPA build (no proxying; `/api` goes
  straight to the api via Traefik).

Bot goes down mid-notify? Nothing is lost - notifications pile up in the api's database and get
delivered (at-least-once) once a bot polls again. After 10 failed attempts one gets dead-lettered
and left alone.

The internal endpoints (`/api/internal/*`) aren't for you, and they're locked down like it:
Traefik never routes them publicly (the api router rule excludes the prefix), callers need the
shared `X-Internal-Api-Key` header, and their IP has to be inside a CIDR allowlist (default:
loopback + RFC1918 + Tailscale CGNAT + IPv6 ULA; override with `INTERNAL_API_ALLOWED_CIDRS`).

## Docker Deployment

You need a Traefik already running with the Docker label provider, plus an external `traefik`
network (`docker network create traefik` once, if you don't have it).

Set these in `.env` next to `compose.yaml`:

| Key                  | Required | Notes                                                       |
|----------------------|----------|-------------------------------------------------------------|
| TOKEN                | yes      | Discord bot token                                           |
| GUILD_ID             | yes      | Your Discord server                                         |
| XF_USER / XF_SESSION | no       | f95zone cookies                                             |
| INTERNAL_API_KEY     | yes      | Shared api/bot key: generate with `openssl rand -hex 32`    |
| DOMAIN               | yes      | Public hostname Traefik routes to the web+api services      |
| SELF_HOSTED          | no       | `true` removes the watchlist limit                          |
| FREE_USER_LIMIT      | no       | Watchlist cap (default 6969); SELF_HOSTED=true removes it   |
| RSS_UPDATE_INTERVAL  | no       | RSS check interval in minutes (default 5)                   |
| TRAEFIK_NETWORK      | no       | Traefik's docker network name (default `traefik`)           |
| TRAEFIK_ENTRYPOINT   | no       | Traefik entrypoint (default `websecure`)                    |
| TRAEFIK_CERTRESOLVER | no       | Uncomment the tls label(s) in `compose.yaml` if you use one |

Then:

```bash
docker compose up -d --build
```

Routing: Traefik sends `/api`, `/openapi` and `/scalar` (everything *except* `/api/internal`)
to the **api** container and everything else on the host to the static **web** container. The
**bot** never goes near Traefik - it talks to the api directly over the compose network
(`API_BASE_URL=http://api:8080`) with the shared internal key.

One gotcha: chown the `./data` mount before first boot - `sudo chown -R 1654:1654 /path/to/folder`.

## Configuration

Environment variables:

| Variable                   | Default                          | Container | Description                                                                               |
|----------------------------|----------------------------------|-----------|-------------------------------------------------------------------------------------------|
| DISCORD_BOT_TOKEN          | -                                | bot       | Your discord bot token                                                                    |
| DISCORD_GUILD_ID           | -                                | bot       | Your discord server                                                                       |
| DATABASE_PATH              | /data/app.db                     | api       | SQLite db location                                                                        |
| LOGS_FOLDER                | /data/logs                       | api       | Folder where logs are put                                                                 |
| RSS_UPDATE_INTERVAL        | 5                                | api       | How often it checks the RSS feeds (in minutes)                                            |
| SELF_HOSTED                | false                            | api, bot  | Removes the watchlist limit for everyone on the instance                                  |
| FREE_USER_LIMIT            | 6969                             | api       | Watchlist cap per account (anti-spam). Raise it, or set SELF_HOSTED=true to lift it       |
| XF_USER                    | -                                | api       | cookies                                                                                   |
| XF_SESSION                 | -                                | api       | cookies (I had these because I got cucked by ratelimits as anon user for tests)           |
| COOKIE_SECURE              | true                             | api       | Mark the website session cookie `Secure`. Only set `false` for plain-HTTP LAN self-hosts. |
| INTERNAL_API_KEY           | - (required)                     | api, bot  | Shared key for the `/api/internal` endpoints (`openssl rand -hex 32`)                     |
| INTERNAL_API_ALLOWED_CIDRS | loopback + RFC1918 + CGNAT + ULA | api       | Optional comma-separated CIDR allowlist for `/api/internal`                               |
| API_BASE_URL               | http://api:8080                  | bot       | Where the bot reaches the api (compose network)                                           |
| NOTIFICATION_POLL_INTERVAL | 5                                | bot       | Seconds between pending-notification polls                                                |
| PRIVILEGE_SYNC_INTERVAL    | 15                               | bot       | Minutes between privileged-user cache syncs                                               |
| DOMAIN                     | - (required)                     | compose   | Public hostname Traefik routes to the web+api services                                    |

## Running without the bot

Don't want the bot? Don't start it (`docker compose up -d api web`, or comment the `bot` service
out). The API, RSS monitor and website keep working, and DM notifications pile up in the api's
pending queue until a bot shows up to deliver them. There's no `DISABLE_DISCORD` anymore - the
bot is its own container now.

## Deployment Notes

- **Traefik is the only way in**: no ports are published. The api and web containers join your
  external `traefik` network and get routed by label rules. Hostname goes in `DOMAIN`
  (plus `TRAEFIK_NETWORK` / `TRAEFIK_ENTRYPOINT` / `TRAEFIK_CERTRESOLVER` if your setup needs them).
- **Website login wants HTTPS**: browsers refuse `Secure` cookies over plain `http://`
  (`localhost` excepted), so put the stack behind a TLS-terminating Traefik entrypoint if you
  actually want to log in. Running plain-HTTP on your LAN? Set `COOKIE_SECURE=false`.
- **One api replica, ever**: the api is the single owner/writer of the SQLite database (watchlist
  mutation locking and rate limiting live in-process). Two replicas on the same database file
  will fight over it. Don't.
- **Migrations** run automatically at api boot, and each one gets a backup first: the app copies
  the database to `<DATABASE_PATH>.pre-migration-<timestamp>`. The hash rotation in this upgrade
  is irreversible, so hang onto that backup until you've checked everything works.
- **Website build**: the SPA is built inside the web image (node stage into nginx). The old manual
  `npm run build` + copy-into-`wwwroot/` dance is gone - the api serves no static files anymore.
- **Breaking change** vs the old single-container compose: the stack is api/bot/web behind Traefik
  now, `DISABLE_DISCORD` no longer exists (stop the bot container instead) and port 8080 isn't
  published anymore.

