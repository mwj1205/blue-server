FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/blueServer.Domain/blueServer.Domain.csproj src/blueServer.Domain/
COPY src/blueServer.Infrastructure/blueServer.Infrastructure.csproj src/blueServer.Infrastructure/
COPY src/blueServer.Migrations/blueServer.Migrations.csproj src/blueServer.Migrations/
RUN dotnet restore src/blueServer.Migrations/blueServer.Migrations.csproj

COPY src/ src/
RUN dotnet publish src/blueServer.Migrations/blueServer.Migrations.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "blueServer.Migrations.dll"]
