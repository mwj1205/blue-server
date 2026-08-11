using Pulumi;

return await Deployment.RunAsync(() =>
{
    // Stack별 Application 설정 로드
    var configuration = new Config();

    var projectName = configuration.Require("projectName");
    var environment = configuration.Require("environment");
    var location = configuration.Require("location");
    var namePrefix = $"{projectName}-{environment}";

    // Resource 추가 전 Stack Configuration과 Naming 규칙 검증용 Output
    return new Dictionary<string, object?>
    {
        ["projectName"] = projectName,
        ["environment"] = environment,
        ["location"] = location,
        ["namePrefix"] = namePrefix,
    };
});
