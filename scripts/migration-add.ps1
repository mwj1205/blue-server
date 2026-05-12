param(
    [string]$Name
)

dotnet ef migrations add $Name `
--project .\src\blueServer.Infrastructure\ `
--startup-project .\src\blueServer.Api\