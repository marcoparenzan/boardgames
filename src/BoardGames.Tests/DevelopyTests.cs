using BoardGames.Developy.Engine;
using BoardGames.Developy.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace BoardGames.Tests;

/// <summary>Dice come from a queue; shuffles stay random but seeded.</summary>
internal sealed class LoadedDice(params int[] rolls) : Random(42)
{
    private readonly Queue<int> rolls = new(rolls);
    public override int Next(int minValue, int maxValue) =>
        minValue == 1 && maxValue == 7 && rolls.Count > 0 ? rolls.Dequeue() : base.Next(minValue, maxValue);
}

public class DevelopyTests
{
    private static DevelopyGame Two(params int[] rolls) =>
        new([new("Ada", "developer", false), new("Bob", "devops", false)], 25, new LoadedDice(rolls));

    private static void Do(DevelopyGame g, ActKind kind, int space = -1) => g.Apply(g.Current, new Act(kind, space));

    [Fact]
    public void BoardHas24SpacesWithFourGroupsOfThreeAndFourClouds()
    {
        Assert.Equal(Catalog.Spaces, Catalog.Board.Length);
        Assert.All(Catalog.Groups, gr => Assert.Equal(3, DevelopyGame.GroupSpaces(gr.Id).Count()));
        Assert.Equal(4, Catalog.Board.Count(s => s.Kind == SpaceKind.Cloud));
        Assert.All(Catalog.Board.Where(s => s.Kind == SpaceKind.Project), s => Assert.True(s.Rent!.Zip(s.Rent!.Skip(1)).All(p => p.First < p.Second)));
    }

    [Fact]
    public void BuyThenOpponentPaysFee()
    {
        var g = Two(1, 1);
        var bobStart = g.Players[1].Cash;
        Assert.Equal(Catalog.StartBudget + Catalog.SeatBonus, bobStart);
        Do(g, ActKind.Roll);                       // Ada → Northwind API (1)
        Assert.Equal(Phase.Buy, g.Phase);
        Do(g, ActKind.Buy, 1);
        Assert.Equal(Catalog.StartBudget - 90, g.Players[0].Cash);
        Do(g, ActKind.EndTurn);
        Do(g, ActKind.Roll);                       // Bob → 1, has a token so he may negotiate
        Assert.Equal(Phase.Fee, g.Phase);
        Do(g, ActKind.TokenFee);
        var fee = Catalog.Board[1].Rent![0];
        Assert.Equal(bobStart - fee / 2, g.Players[1].Cash);
        Assert.Equal(Catalog.StartBudget - 90 + fee / 2, g.Players[0].Cash);
        Assert.Equal(0, g.Players[1].Tokens);
    }

    [Fact]
    public void FullGroupDoublesFeeAndUpgradesMustBeEven()
    {
        var g = Two();
        foreach (var s in DevelopyGame.GroupSpaces(0)) g.Holdings[s.Index].Owner = 0;
        Assert.Equal(Catalog.Board[1].Rent![0] * 2, g.Fee(1));
        Assert.True(g.CanUpgrade(0, 1));
        Do(g, ActKind.Upgrade, 1);
        Assert.False(g.CanUpgrade(0, 1));          // must build on 3 and 5 first
        Assert.True(g.CanUpgrade(0, 3));
        Assert.Equal(Catalog.Board[1].Rent![1], g.Fee(1));
        Assert.False(g.CanFreeze(0, 3));           // no freezing while the group has teams
        Assert.True(g.CanDowngrade(0, 1));
    }

    [Fact]
    public void EscalationSendsToMeetingAndSixLetsYouOut()
    {
        var g = Two(6, 1, 6, 6, 6, 6);
        g.Players[0].Position = 12;                 // 12 + 6 = Escalation (18)
        Do(g, ActKind.Roll);
        Assert.True(g.Players[0].InMeeting);
        Assert.Equal(Catalog.MeetingIndex, g.Players[0].Position);
        Do(g, ActKind.EndTurn);
        Do(g, ActKind.Roll); Do(g, ActKind.Pass, 1); Do(g, ActKind.EndTurn); // Bob
        Do(g, ActKind.Roll);                       // Ada rolls 6: out and moves to 12
        Assert.False(g.Players[0].InMeeting);
        Assert.Equal(Catalog.HackathonIndex, g.Players[0].Position);
    }

    [Fact]
    public void ThreeFailedMeetingRollsForcePaymentAndMove()
    {
        var g = Two(1, 1, 1, 1, 2);
        g.Players[0].Position = Catalog.MeetingIndex;
        g.Players[0].InMeeting = true;
        g.Players[0].Tokens = 0;
        Do(g, ActKind.Roll); Assert.Equal(Phase.End, g.Phase); Do(g, ActKind.EndTurn);
        g.Players[1].Position = 3;
        Do(g, ActKind.Roll); Do(g, ActKind.Pass, 4); Do(g, ActKind.EndTurn);   // Bob skips cloud 4
        Do(g, ActKind.Roll); Assert.Equal(2, g.Players[0].MeetingAttempts); Do(g, ActKind.EndTurn);
        Do(g, ActKind.Roll); Do(g, ActKind.Pass, 5); Do(g, ActKind.EndTurn);   // Bob skips project 5
        Do(g, ActKind.Roll);                       // third attempt: rolls 2, pays 50, moves to 8 (AI Lab)
        Assert.False(g.Players[0].InMeeting);
        Assert.Equal(8, g.Players[0].Position);
        Assert.Equal(Catalog.StartBudget - Catalog.MeetingFee, g.Players[0].Cash);
        Assert.Equal(1, g.Players[0].Tokens);
        Assert.Equal(Catalog.MeetingFee, g.Pot);
    }

    [Fact]
    public void DebtCanBeCoveredByFreezingOrEndsInBankruptcyToCreditor()
    {
        var g = Two(1);
        g.Holdings[1].Owner = 1; g.Holdings[3].Owner = 1; g.Holdings[5].Owner = 1;
        g.Holdings[1].Level = 4; g.Holdings[3].Level = 4; g.Holdings[5].Level = 4;
        g.Holdings[22].Owner = 0;
        g.Players[0].Cash = 20;
        g.Players[0].Tokens = 0;
        Do(g, ActKind.Roll);                       // fee far above cash
        Assert.Equal(Phase.Debt, g.Phase);
        Assert.Contains(new Act(ActKind.Freeze, 22), g.Options(0));
        Do(g, ActKind.Freeze, 22);
        Assert.DoesNotContain(g.Options(0), a => a.Kind == ActKind.PayDebt);
        Do(g, ActKind.Bankrupt);
        Assert.True(g.Players[0].Bankrupt);
        Assert.Equal(Phase.Over, g.Phase);
        Assert.Equal([1], g.Winners);
        Assert.Equal(1, g.Holdings[22].Owner);     // creditor inherits the frozen cloud
        Assert.True(g.Holdings[22].Frozen);
    }

    [Fact]
    public void HostileTakeoverPaysDoubleToOwnerButNeverBreaksACompleteGroup()
    {
        var g = Two(1, 2);
        g.Holdings[1].Owner = 1;
        Do(g, ActKind.Roll);                       // Ada lands on Bob's Northwind API
        if (g.Phase == Phase.Fee) Do(g, ActKind.PayFee);
        var takeover = new Act(ActKind.Takeover, 1);
        Assert.Contains(takeover, g.Options(0));
        var ada = g.Players[0].Cash; var bob = g.Players[1].Cash;
        g.Apply(0, takeover);
        Assert.Equal(0, g.Holdings[1].Owner);
        Assert.Equal(ada - 180, g.Players[0].Cash);
        Assert.Equal(bob + 180, g.Players[1].Cash);
        Do(g, ActKind.EndTurn);
        foreach (var s in DevelopyGame.GroupSpaces(0)) g.Holdings[s.Index].Owner = 0; // Ada now owns the whole group
        Do(g, ActKind.Roll);                       // Bob lands on 2 (event) → not a project
        Assert.False(g.CanTakeover(1, 3));         // complete groups are protected
    }

    [Fact]
    public void IllegalOrStaleActionsAreRejectedWithoutChangingState()
    {
        var g = Two(3);
        Assert.Throws<InvalidOperationException>(() => g.Apply(1, new Act(ActKind.Roll)));
        Assert.Throws<InvalidOperationException>(() => g.Apply(0, new Act(ActKind.Buy, 1)));
        Assert.Throws<InvalidOperationException>(() => g.Apply(0, new Act(ActKind.Roll), expectedVersion: 7));
        Assert.Equal(0, g.Version);
        Assert.Equal(0, g.Players[0].Position);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 3)]
    public void BotOnlyGamesAlwaysFinishWithConsistentMoney(int players, int seed)
    {
        for (var run = 0; run < 25; run++)
        {
            var setup = Catalog.Roles.Take(players).Select(r => new PlayerSetup(r.Name, r.Id, true)).ToArray();
            var g = new DevelopyGame(setup, 25, new Random(seed * 1000 + run));
            var steps = 0;
            while (g.Phase != Phase.Over)
            {
                g.Apply(g.Current, BotBrain.Choose(g, g.Current));
                Assert.All(g.Players, p => Assert.True(p.Cash >= 0, $"{p.Name} has {p.Cash}"));
                Assert.True(++steps < 20000, "game did not terminate");
            }
            Assert.NotEmpty(g.Winners);
            Assert.True(g.Round <= g.MaxRounds);
        }
    }

    [Fact]
    public async Task RoomFillsOpenSeatsWithBotsAndBotsPlayOnServer()
    {
        using var rooms = new DevelopyRooms(NullLogger<DevelopyRooms>.Instance) { BotDelay = TimeSpan.Zero };
        var room = rooms.Create("host", "Marco", "cto", [SeatRequest.Remote, SeatRequest.Bot]);
        Assert.False(room.View("host").Started);
        rooms.Join(room, "guest", "Gio", "cto");    // role taken → keeps the pre-assigned one
        var view = room.View("guest");
        Assert.True(view.Started);
        Assert.NotEqual("cto", view.Seats[1].Role);
        Assert.Throws<InvalidOperationException>(() => rooms.Act(room, "guest", new Act(ActKind.Roll), view.Version));
        rooms.Act(room, "host", new Act(ActKind.Roll), view.Version);
        var host = room.View("host");
        while (host.Phase != Phase.End) { rooms.Act(room, "host", host.Options.First(o => o.Kind is ActKind.Pass or ActKind.Accept or ActKind.PayFee or ActKind.PayDebt or ActKind.Bankrupt), host.Version); host = room.View("host"); }
        rooms.Act(room, "host", new Act(ActKind.EndTurn), host.Version);
        Assert.Equal(1, room.View("guest").Current);
        var g = room.Game!;
        for (var gv = room.View("guest"); gv.Current == 1 && gv.Phase != Phase.Over; gv = room.View("guest"))
            rooms.Act(room, "guest", BotBrain.Choose(g, 1), gv.Version); // guest plays like a bot; EndTurn wakes the real bot
        rooms.Say(room, "guest", "ciao");
        Assert.Contains(room.View(null).Chat, c => c.Author == "Gio" && c.Text == "ciao");
        for (var i = 0; i < 100 && room.View(null).Current == 2; i++) await Task.Delay(20);
        Assert.NotEqual(2, room.View(null).Current); // the bot moved on by itself
    }
}
