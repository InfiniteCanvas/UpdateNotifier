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

## Command Reference

| Command                   | Description                                                                                                    | Example                                                             |
|---------------------------|----------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------|
| `/enable`                 | Enables the bot for you (agree to data privacy things). You need to run this before you can use anything else. | `/enable`                                                           |
| `/disable`                | Disables the bot for you (deletes your data - not reversible)                                                  | `/disable`                                                          |                                                          
| `/watch [URL1 URL2 ...]`  | Add games to watchlist                                                                                         | `/watch https://f95zone.to/threads/1 https://f95zone.to/threads/2`  |
| `/remove [URL1 URL2 ...]` | Remove games from watchlist                                                                                    | `/remove https://f95zone.to/threads/1 https://f95zone.to/threads/2` |
| `/list`                   | Show your watched games                                                                                        | `/list`                                                             |

## Docker Deployment

Set these in your Docker deployment if you want to host your own bot. Adjust to your own system.
When binding a mount, make sure to set permissions with `sudo chown -R 1654:1654 /path/to/folder`

If you build from source:
```yaml
services:
  update-notifier:
    image: update-notifier
    build:
      context: .
      dockerfile: UpdateNotifier/Dockerfile
    environment:
      - DISCORD_BOT_TOKEN=${TOKEN}
      - DISCORD_GUILD_ID=${GUILD_ID}
      - DATABASE_PATH=/data/app.db
      - LOGS_FOLDER=/data/logs
      - SELF_HOSTED=true # enables supporter features
      - RSS_UPDATE_INTERVAL=5 # in minutes
    volumes:
      - ./data:/data
```
If you pull the image:
```yaml
services:
  update-notifier:
    container_name: update-notifier
    image: infinitecanvas/update-notifier:latest
    environment:
      - DISCORD_BOT_TOKEN=${TOKEN}
      - DISCORD_GUILD_ID=${GUILD_ID}
      - DATABASE_PATH=/data/app.db
      - LOGS_FOLDER=/data/logs
      - SELF_HOSTED=true
      - RSS_UPDATE_INTERVAL=5 # in minutes
    volumes:
      - ./data:/data
```

## Configuration

Environment variables:

| Variable            | Default      | Description                                                                     |
|---------------------|--------------|---------------------------------------------------------------------------------|
| DISCORD_BOT_TOKEN   | -            | Your discord bot token                                                          |
| DISCORD_GUILD_ID    | -            | Your discord server                                                             |
| DATABASE_PATH       | /data/app.db | SQLite db location                                                              |
| LOGS_FOLDER         | /data/logs   | Folder where logs are put                                                       |
| RSS_UPDATE_INTERVAL | 5            | How often it checks the RSS feeds (in minutes)                                  |
| SELF_HOSTED         | false        | Basically makes you a supporter on your instance                                |
| XF_USER             | -            | cookies                                                                         |
| XF_SESSION          | -            | cookies (I had these because I got cucked by ratelimits as anon user for tests) |

