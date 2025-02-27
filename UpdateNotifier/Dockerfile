FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["UpdateNotifier/UpdateNotifier.csproj", "UpdateNotifier/"]
RUN dotnet restore "UpdateNotifier/UpdateNotifier.csproj"
COPY . .
WORKDIR "/src/UpdateNotifier"
RUN dotnet build "UpdateNotifier.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "UpdateNotifier.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
ENV DISCORD_BOT_TOKEN=""
ENV DISCORD_GUILD_ID=""
ENV DATABASE_PATH="/data/app.db"
ENV SELF_HOSTED=false
ENV RSS_UPDATE_INTERVAL=5
ENV XF_USER=""
ENV XF_SESSION=""

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UpdateNotifier.dll"]
