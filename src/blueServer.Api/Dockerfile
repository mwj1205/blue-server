FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/blueServer.Domain/blueServer.Domain.csproj src/blueServer.Domain/
COPY src/blueServer.Infrastructure/blueServer.Infrastructure.csproj src/blueServer.Infrastructure/
COPY src/blueServer.Api/blueServer.Api.csproj src/blueServer.Api/
RUN dotnet restore src/blueServer.Api/blueServer.Api.csproj

COPY src/ src/
RUN dotnet publish src/blueServer.Api/blueServer.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "blueServer.Api.dll"]

