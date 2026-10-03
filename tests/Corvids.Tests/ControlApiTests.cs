using System.Text.Json;
using Corvids.Services;

namespace Corvids.Tests;

public class ControlApiTests
{
    [Fact]
    public void AppDto_serializes_with_camelCase_keys()
    {
        var dto = new AppDto("id1", "api", "Running", true, 1234, "up 5s", @"E:\work", "npm run dev",
            "port 3000", new[] { 3000 });

        var json = JsonSerializer.Serialize(dto, ApiJson.Default.AppDto);

        Assert.Contains("\"name\":\"api\"", json);
        Assert.Contains("\"running\":true", json);
        Assert.Contains("\"ports\":[3000]", json);
        Assert.DoesNotContain("\"Name\"", json); // not PascalCase
    }

    [Fact]
    public void App_list_round_trips_through_the_source_generated_context()
    {
        IReadOnlyList<AppDto> apps = new[]
        {
            new AppDto("a", "one", "Stopped", false, null, "", "/x", "node .", "stopped", Array.Empty<int>()),
            new AppDto("b", "two", "Running", true, 42, "up 1m", "/y", "npm start", "port 8080", new[] { 8080 }),
        };

        var json = JsonSerializer.Serialize(apps, ApiJson.Default.IReadOnlyListAppDto);
        var back = JsonSerializer.Deserialize(json, ApiJson.Default.IReadOnlyListAppDto);

        Assert.NotNull(back);
        Assert.Equal(2, back!.Count);
        Assert.Equal("two", back[1].Name);
        Assert.Equal(8080, back[1].Ports[0]);
    }

    [Fact]
    public void ActionResult_and_error_serialize()
    {
        Assert.Contains("\"ok\":true", JsonSerializer.Serialize(new ActionResult(true, "done"), ApiJson.Default.ActionResult));
        Assert.Contains("\"error\":\"nope\"", JsonSerializer.Serialize(new ApiError("nope"), ApiJson.Default.ApiError));
    }
}
