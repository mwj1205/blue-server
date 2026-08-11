using System.Security.Cryptography;
using System.Text;
using Pulumi;
using AzureNative = Pulumi.AzureNative;

return await Deployment.RunAsync(() =>
{
    // Stack별 Application 설정 로드
    var configuration = new Config();

    var projectName = configuration.Require("projectName");
    var environment = configuration.Require("environment");
    var location = configuration.Require("location");
    var containerRegistrySku = configuration.Require("containerRegistrySku");
    var namePrefix = $"{projectName}-{environment}";

    var supportedContainerRegistrySkus = new[] { "Basic", "Standard", "Premium" };
    if (!supportedContainerRegistrySkus.Contains(
            containerRegistrySku,
            StringComparer.Ordinal))
    {
        throw new RunException(
            $"containerRegistrySku는 {string.Join(", ", supportedContainerRegistrySkus)} 중 하나여야 합니다.");
    }

    // 현재 Azure Provider의 Subscription ID를 이용한 전역 고유 ACR 이름 구성
    var clientConfiguration = Output.Create(
        AzureNative.Authorization.GetClientConfig.InvokeAsync());

    var containerRegistryName = clientConfiguration.Apply(client =>
    {
        var compactProjectName = projectName.Replace("-", string.Empty);
        var projectNameSegment = compactProjectName[..Math.Min(compactProjectName.Length, 20)];
        var normalizedSubscriptionId = client.SubscriptionId.ToLowerInvariant();
        var subscriptionHashBytes = SHA1.HashData(
            Encoding.UTF8.GetBytes(normalizedSubscriptionId));
        var subscriptionHash = Convert
            .ToHexString(subscriptionHashBytes)
            .ToLowerInvariant()[..8];

        return $"acr{projectNameSegment}{environment}{subscriptionHash}";
    });

    // Terraform 구성과 관리 주체를 구분하는 공통 Tag
    var commonTags = new InputMap<string>
    {
        ["project"] = projectName,
        ["environment"] = environment,
        ["managed_by"] = "pulumi",
    };

    // Azure Resource의 논리적 관리 경계
    var resourceGroup = new AzureNative.Resources.ResourceGroup("main", new()
    {
        ResourceGroupName = $"rg-{namePrefix}",
        Location = location,
        Tags = commonTags,
    });

    // Application Container Image를 저장할 Private Registry
    var containerRegistry = new AzureNative.ContainerRegistry.Registry("main", new()
    {
        RegistryName = containerRegistryName,
        ResourceGroupName = resourceGroup.Name,
        Location = resourceGroup.Location,
        Sku = new AzureNative.ContainerRegistry.Inputs.SkuArgs
        {
            Name = containerRegistrySku,
        },
        AdminUserEnabled = false,
        AnonymousPullEnabled = false,
        PublicNetworkAccess = AzureNative.ContainerRegistry.PublicNetworkAccess.Enabled,
        Tags = commonTags,
    });

    // Terraform Output과 비교 가능한 Pulumi Stack Output
    return new Dictionary<string, object?>
    {
        ["projectName"] = projectName,
        ["environment"] = environment,
        ["location"] = location,
        ["namePrefix"] = namePrefix,
        ["resourceGroupName"] = resourceGroup.Name,
        ["containerRegistryName"] = containerRegistry.Name,
        ["containerRegistryLoginServer"] = containerRegistry.LoginServer,
    };
});
