FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/blueServer.Domain/blueServer.Domain.csproj src/blueServer.Domain/
COPY src/blueServer.Infrastructure/blueServer.Infrastructure.csproj src/blueServer.Infrastructure/
COPY src/blueServer.Game/blueServer.Game.csproj src/blueServer.Game/
RUN dotnet restore src/blueServer.Game/blueServer.Game.csproj

COPY src/ src/
RUN dotnet publish src/blueServer.Game/blueServer.Game.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
EXPOSE 7777

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "blueServer.Game.dll"]
