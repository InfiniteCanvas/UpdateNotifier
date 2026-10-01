FROM node:24-alpine AS webbuild
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["UpdateNotifier/UpdateNotifier.csproj", "UpdateNotifier/"]
COPY ["UpdateNotifier.Core/UpdateNotifier.Core.csproj", "UpdateNotifier.Core/"]
COPY ["UpdateNotifier.Discord/UpdateNotifier.Discord.csproj", "UpdateNotifier.Discord/"]
RUN dotnet restore "UpdateNotifier/UpdateNotifier.csproj"
COPY . .
WORKDIR "/src/UpdateNotifier"
RUN dotnet build "UpdateNotifier.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "UpdateNotifier.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
COPY --from=webbuild /web/build /app/publish/wwwroot

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UpdateNotifier.dll"]
