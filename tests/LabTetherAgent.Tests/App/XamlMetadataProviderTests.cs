using Microsoft.UI.Xaml.Markup;

namespace LabTetherAgent.Tests.App;

public class XamlMetadataProviderTests
{
    [Fact]
    public void NestedApplication_ImplementsAndResolvesGeneratedMetadataProvider()
    {
        Assert.True(typeof(IXamlMetadataProvider).IsAssignableFrom(typeof(global::LabTetherAgent.App.App)));

        var provider = global::LabTetherAgent.App.App.CreateGeneratedXamlMetadataProvider();

        Assert.Equal(
            global::LabTetherAgent.App.App.GeneratedXamlMetadataProviderTypeName,
            provider.GetType().FullName);
    }
}
