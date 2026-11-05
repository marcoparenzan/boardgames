using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using BoardGames.Chess.Components.Pages;
using BoardGames.Chess.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BoardGames.Tests;

/// <summary>Fake OpenAI-compatible endpoint: answers each request with the next queued "content" string.</summary>
internal sealed class FakeLlm(params string[] contents) : HttpMessageHandler
{
    private readonly Queue<string> contents = new(contents);
    public List<string> Requests { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri + " " + string.Join(",", request.Headers.Select(h => h.Key)) + " " + await request.Content!.ReadAsStringAsync(cancellationToken));
        if (contents.Count == 0) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = contents.Dequeue() } } } });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

public class ComponentTests
{
    private static IConfiguration Foundry() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AzureAIFoundry:Endpoint"] = "https://example.services.ai.azure.com/api/projects/demo",
        ["AzureAIFoundry:ApiKey"] = "test-key",
        ["AzureAIFoundry:ModelId"] = "test-model",
        ["Games:LogDirectory"] = Path.Combine(Path.GetTempPath(), "boardgames-tests", Guid.NewGuid().ToString()),
    }).Build();

    private static RalfAgent Agent(IConfiguration config, FakeLlm llm) => new(new HttpClient(llm), config, NullLogger<RalfAgent>.Instance);

    [Fact]
    public void BoardSelectsLegalDestinationsAndRemoteWaitsForGuest()
    {
        using var ctx = new BunitContext();
        var config = Foundry();
        ctx.Services.AddSingleton(config);
        ctx.Services.AddSingleton(new ChessRooms(config, NullLogger<ChessRooms>.Instance));
        ctx.Services.AddSingleton(Agent(config, new FakeLlm()));
        var cut = ctx.Render<ChessTable>();
        Assert.Equal(64, cut.FindAll("button[title]").Count);
        cut.Find("button[title='e2']").Click();
        Assert.Equal("true", cut.Find("button[title='e2']").GetAttribute("aria-pressed"));
        Assert.Contains("target", cut.Find("button[title='e4']").ClassName);
        Assert.DoesNotContain("target", cut.Find("button[title='e5']").ClassName);
        cut.FindAll("button").First(b => b.TextContent.Contains("Remote battle")).Click();
        Assert.Contains("In attesa del secondo giocatore", cut.Markup);
        Assert.True(cut.Find("button[title='e2']").HasAttribute("disabled"));
    }

    [Fact]
    public void WithoutConfigurationRalfIsUnavailableButRemoteStillWorks()
    {
        using var ctx = new BunitContext();
        var config = new ConfigurationBuilder().Build();
        ctx.Services.AddSingleton<IConfiguration>(config);
        ctx.Services.AddSingleton(new ChessRooms(config, NullLogger<ChessRooms>.Instance));
        ctx.Services.AddSingleton(new RalfAgent(new HttpClient(), config, NullLogger<RalfAgent>.Instance));
        var cut = ctx.Render<ChessTable>();
        Assert.Contains("Ralf non è configurato", cut.Markup);
        Assert.True(cut.FindAll("button").First(b => b.TextContent.Contains("Contro Ralf")).HasAttribute("disabled"));
        cut.FindAll("button").First(b => b.TextContent.Contains("Remote battle")).Click();
        Assert.Equal(64, cut.FindAll("button[title]").Count);
    }

    [Fact]
    public async Task RalfRetriesIllegalMovesThenPlaysALegalOne()
    {
        var llm = new FakeLlm("""{"text":"Vado di regina","move":"d8h4"}""", "non json", """{"text":"Ok, e5","move":"E7E5"}""");
        var agent = Agent(Foundry(), llm);
        var game = new BoardGames.Chess.Engine.ChessGame(); game.Apply("e2e4", 0);
        var reply = await agent.AskAsync(game.State, game.LegalMoves, "Gioca", true, ["e4"]);
        Assert.Equal("e7e5", reply.Move);
        Assert.Equal(3, llm.Requests.Count);
        Assert.StartsWith("https://example.services.ai.azure.com/openai/v1/chat/completions api-key", llm.Requests[0]);
        Assert.Contains("test-model", llm.Requests[0]);
        Assert.Contains("Scegli esattamente una di queste", llm.Requests[1]); // the model is told why its move was refused
    }

    [Fact]
    public async Task RalfGivesUpWithAClearErrorInsteadOfInventingAMove()
    {
        var agent = Agent(Foundry(), new FakeLlm("""{"text":"a","move":"a1a8"}""", """{"text":"b","move":"a1a8"}""", """{"text":"c","move":"a1a8"}"""));
        var game = new BoardGames.Chess.Engine.ChessGame();
        var ex = await Assert.ThrowsAsync<RalfUnavailableException>(() => agent.AskAsync(game.State, game.LegalMoves, "Gioca", true));
        Assert.Contains("mossa legale", ex.Message);
        var down = Agent(Foundry(), new FakeLlm()); // service answers 503
        await Assert.ThrowsAsync<RalfUnavailableException>(() => down.AskAsync(game.State, game.LegalMoves, "Gioca", true));
    }
}
