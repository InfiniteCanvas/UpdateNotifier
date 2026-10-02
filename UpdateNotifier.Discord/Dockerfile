FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["UpdateNotifier.Discord/UpdateNotifier.Discord.csproj", "UpdateNotifier.Discord/"]
COPY ["UpdateNotifier.Core/UpdateNotifier.Core.csproj", "UpdateNotifier.Core/"]
RUN dotnet restore "UpdateNotifier.Discord/UpdateNotifier.Discord.csproj"
COPY . .
WORKDIR "/src/UpdateNotifier.Discord"
RUN dotnet build "UpdateNotifier.Discord.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "UpdateNotifier.Discord.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UpdateNotifier.Discord.dll"]
