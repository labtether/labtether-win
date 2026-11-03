using System.Xml.Linq;
using LabTetherAgent.Presentation;
using LabTetherAgent.Services;

namespace LabTetherAgent.Tests.Services;

// Alert names and messages come from the Hub as free text. Unescaped, a name
// such as "Disk < 10% & rising" breaks the toast XML and no notification shows.
public class ToastNotificationServiceTests
{
    [Fact]
    public void BuildXmlEscapesHubTextAndKeepsTheUpdateButton()
    {
        var xml = XDocument.Parse(ToastNotificationService.BuildXml(
            new ToastMessage("Critical alert: Disk < 10% & rising", "\"C:\" is <full>"), ("View release", "action=view-update")));

        var texts = xml.Descendants("text").Select(t => t.Value).ToArray();
        Assert.Equal(new[] { "Critical alert: Disk < 10% & rising", "\"C:\" is <full>" }, texts);
        Assert.Equal("action=view-update", xml.Root!.Attribute("launch")!.Value);
        Assert.Equal("View release", xml.Descendants("action").Single().Attribute("content")!.Value);
    }

    [Fact]
    public void BuildXmlLeavesOutAnEmptySecondLine()
    {
        var xml = XDocument.Parse(ToastNotificationService.BuildXml(new ToastMessage("Agent stopped at 21:12", ""), null));

        Assert.Single(xml.Descendants("text"));
        Assert.Empty(xml.Descendants("actions"));
    }
}
