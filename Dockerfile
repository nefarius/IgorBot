#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/IgorBot.csproj src/packages.lock.json ./
RUN dotnet restore "./IgorBot.csproj" --locked-mode
COPY src/ .
RUN dotnet build "IgorBot.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "IgorBot.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "IgorBot.dll"]