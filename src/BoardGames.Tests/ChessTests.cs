using BoardGames.Chess.Engine;
using BoardGames.Chess.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BoardGames.Tests;
/// <summary>Like Blazor's renderer context: a blocking Send from inside the context never returns.</summary>
internal sealed class NoSendContext : SynchronizationContext
{
    public override void Send(SendOrPostCallback d, object? state) => throw new InvalidOperationException("Send on the renderer context would deadlock.");
}

public class RulesTests
{
    [Fact]
    public void CapturesNeverCallBackIntoTheCallersSynchronizationContext()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new NoSendContext());
        try
        {
            var g = new ChessGame(); // created on the "circuit" context, as ChessRooms does
            foreach (var m in new[] { "e2e4", "d7d5", "e4d5" }) g.Apply(m, 0); // e4xd5 is a capture
            Assert.StartsWith("rnbqkbnr/ppp1pppp/8/3P4/8", g.State);
            var fromFen = new ChessGame("r1bq1bnr/pppkpp2/n2p2pp/8/PPP1P1P1/3B4/3P1P1P/RNBQK1NR b KQ b3 0 6");
            fromFen.Apply("a6b4", 0); // the capture that froze Ralf
            Assert.StartsWith("r1bq1bnr/pppkpp2/3p2pp/8/PnP1P1P1", fromFen.State);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact] public void InitialPositionHasTwentyMovesAnd64Cells() { var g = new ChessGame(); Assert.Equal(20, g.LegalMoves.Length); Assert.Equal(64, g.Cells.Length); }
    [Fact] public void ProtocolPreservesBeforeAndAfter() { var g = new ChessGame(); var fen = g.State; var t = g.Apply("e2e4", 0); Assert.Equal(fen, t.StatoCorrente); Assert.Equal(g.State, t.NuovoStato); Assert.Equal("e4", t.San); Assert.Equal(1, t.Version); Assert.False(g.WhiteTurn); }
    [Fact] public void IllegalMoveDoesNotMutateState() { var g = new ChessGame(); var before = g.State; Assert.Throws<InvalidOperationException>(() => g.Apply("e2e5", 0)); Assert.Equal(before, g.State); }
    [Fact] public void FoolsMateEndsGame() { var g = new ChessGame(); foreach (var m in new[] { "f2f3", "e7e5", "g2g4", "d8h4" }) g.Apply(m, 0); Assert.True(g.Finished); Assert.Empty(g.LegalMoves); }
    [Fact] public void CastlingEnPassantAndUnderpromotion() {
        var castle = new ChessGame("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1"); castle.Apply("e1g1", 0); Assert.StartsWith("r3k2r/8/8/8/8/8/8/R4RK1", castle.State);
        var ep = new ChessGame(); foreach (var m in new[] { "e2e4", "a7a6", "e4e5", "d7d5" }) ep.Apply(m, 0); ep.Apply("e5d6", 0); Assert.Contains("p2P4/8", ep.State);
        var promotion = new ChessGame("7k/P7/8/8/8/8/8/7K w - - 0 1"); Assert.Contains("a7a8n", promotion.LegalMoves); promotion.Apply("a7a8n", 0); Assert.StartsWith("N6k", promotion.State);
    }
    [Fact] public void RemoteEnforcesMembershipTurnsVersionsAndCapacity() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Games:LogDirectory"] = Path.Combine(Path.GetTempPath(), "boardgames-tests", Guid.NewGuid().ToString()) }).Build();
        var rooms = new ChessRooms(config, NullLogger<ChessRooms>.Instance); var r = rooms.Create(true, "host");
        Assert.Throws<InvalidOperationException>(() => rooms.Move(r, "host", "e2e4", 0));
        rooms.Join(r.Code, "guest"); Assert.Throws<InvalidOperationException>(() => rooms.Join(r.Code, "third"));
        Assert.Throws<InvalidOperationException>(() => rooms.Move(r, "guest", "e2e4", 0)); rooms.Move(r, "host", "e2e4", 0);
        Assert.Throws<InvalidOperationException>(() => rooms.Move(r, "guest", "e7e5", 0)); rooms.Move(r, "guest", "e7e5", 1); Assert.Equal(2, r.View().Version);
        Directory.Delete(config["Games:LogDirectory"]!, true);
    }
}
