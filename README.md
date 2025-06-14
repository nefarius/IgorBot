# <img src="assets/NSS-128x128.png" align="left" />IgorBot

[![Docker Image CI](https://github.com/nefarius/IgorBot/actions/workflows/docker-image.yml/badge.svg)](https://github.com/nefarius/IgorBot/actions/workflows/docker-image.yml)
[![Requirements](https://img.shields.io/badge/Requirements-.NET%208.0-blue.svg)](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md)
[![Docker Pulls](https://img.shields.io/docker/pulls/containinger/igor-bot)](https://hub.docker.com/r/containinger/igor-bot)
![Docker Image Version (latest by date)](https://img.shields.io/docker/v/containinger/igor-bot)
![Docker Image Size (latest by date)](https://img.shields.io/docker/image-size/containinger/igor-bot)

Advanced Discord bot to automate new Member on-boarding.

## Motivation

TBD

## Disclaimer

I am using it successfully for a couple of years on my community servers now, however especially configuration
simplification has missing features. You have been warned.

## Prerequisites

You **must** have at least one other bot (Carl, ME6, ...) in use that assigns the "Lurker" role to new members, since most servers already have some sort of moderation bot, I did not include this feature into Igor on purpose. Alternatively you can assign the "Lurker" role to new members manually to trigger onboarding, but where's the fun in that 😁

## Setup

It's recommended to use Docker and docker-compose to bring up the bot and its companion services.

- Copy `docker/docker-compose.example.yml` as `docker-compose.yml` to a directory of your choice and add your bot token
  in there
- Copy `docker/appsettings.Production.example.json` as `appsettings.Production.json` into the same directory as the
  compose-file
- Run `docker-compose pull` to fetch the container images
- Run `docker-compose up -d` to bring the bot and database services online

## Configuration

As of the time of writing, the bot doesn't have any server setup commands, so some elbow grease is required to get it up
and running 💪 No worries though, I've got you covered! Adjust your `appsettings.Production.json` with the values
outlined below.

### Discord server preparations

- Create a new Guild entry in the config file with your server's ID as the key name
- Set the `GuildId` config value to the same ID
- Add a "Lurker" role and copy its ID to the `StrangerRoleId` config value
    - This is the role new members should get assigned to get identified. More on that later on.
- Add a "Full Member" role and copy its ID to the `MemberRoleId` config value
    - This is the role that promoted/unlocked members will get assigned when approved by a moderator
- Create a new category "Newbies" and copy its ID to the `ApplicationCategoryId` config value
- (Optional) Add one or more moderator role IDs to `ApplicationModeratorRoleIds` to give them the power to kick, ban or
  approve new members
- Add a **private** channel for bot status messages and copy its ID to `StrangerStatusChannelId` config value
- Add a **public** channel for the welcome messages and copy its ID to `MemberWelcomeMessageChannelId` config value

To be done...

## How to build

```bash
docker build --platform linux/amd64 --push -t containinger/igor-bot .
```

## 3rd party credits

- [DSharpPlus](https://github.com/DSharpPlus/DSharpPlus)
- [Nefarius.DSharpPlus.Extensions.Hosting](https://github.com/nefarius/Nefarius.DSharpPlus.Extensions.Hosting)
- [MongoDB.Entities](https://mongodb-entities.com/)
- [Coravel](https://docs.coravel.net/)
- [MongoDB](https://www.mongodb.com/)
- [Rebus](https://github.com/rebus-org/Rebus)
