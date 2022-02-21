<img src="assets/NSS-128x128.png" align="right" />

# IgorBot

Simple Discord bot to automate new Member on-boarding.

## About

Work in progress, may explode at any moment, stay away 😆

## Setup

It's recommended to use Docker and docker-compose to bring up the bot and its companion services.

- Copy `docker/docker-compose.example.yml` as `docker-compose.yml` to a directory of your choice and add your bot token in there
- Copy `docker/appsettings.Production.example.json` as `appsettings.Production.json` into the same directory as the compose file
- Run `docker-compose pull` to fetch the container images
- Run `docker-compose up -d igor-bot-app igor-bot-db` to bring the bot and database services online
  - Optionally you can simply run `docker-compose up -d` which will also start the MongoDB web UI, which you might not need running all the time

## 3rd party credits

- [Nefarius.DSharpPlus.Extensions.Hosting](https://github.com/nefarius/Nefarius.DSharpPlus.Extensions.Hosting)
- [MongoDB.Entities](https://mongodb-entities.com/)
- [Coravel](https://docs.coravel.net/)
- [MongoDB](https://www.mongodb.com/)
- [mongo-express](https://github.com/mongo-express/mongo-express)
