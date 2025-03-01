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

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UpdateNotifier.dll"]
ARG DATABASE_PATH="/data/app.db"
ARG LOGS_FOLDER="/data/logs"
ARG SELF_HOSTED=false
ARG RSS_UPDATE_INTERVAL=5