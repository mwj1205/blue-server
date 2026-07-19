FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/blueServer.Domain/blueServer.Domain.csproj src/blueServer.Domain/
COPY src/blueServer.Infrastructure/blueServer.Infrastructure.csproj src/blueServer.Infrastructure/
COPY src/Orleans/blueServer.GrainContracts/blueServer.GrainContracts.csproj src/Orleans/blueServer.GrainContracts/
COPY src/Orleans/blueServer.Grains/blueServer.Grains.csproj src/Orleans/blueServer.Grains/
COPY src/Orleans/blueServer.Silo/blueServer.Silo.csproj src/Orleans/blueServer.Silo/
RUN dotnet restore src/Orleans/blueServer.Silo/blueServer.Silo.csproj

COPY src/ src/
RUN dotnet publish src/Orleans/blueServer.Silo/blueServer.Silo.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
EXPOSE 11111 30000

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "blueServer.Silo.dll"]
