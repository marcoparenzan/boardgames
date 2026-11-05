namespace BoardGames.Developy.Engine;

/// <summary>Heuristic opponent: keeps a cash reserve, completes groups, builds evenly, liquidates before going bankrupt.</summary>
public static class BotBrain
{
    public static Act Choose(DevelopyGame game, int seat)
    {
        var options = game.Options(seat);
        if (options.Count == 0) throw new InvalidOperationException("Il bot non ha azioni disponibili.");
        var p = game.Players[seat];
        var reserve = 150 + game.Round * 10;
        Act? Find(ActKind kind) => options.FirstOrDefault(o => o.Kind == kind);

        switch (game.Phase)
        {
            case Phase.Card: return Find(ActKind.Accept)!;
            case Phase.Fee:
                return game.PendingFee >= 40 && Find(ActKind.TokenFee) is { } token ? token : Find(ActKind.PayFee)!;
            case Phase.Buy:
                if (Find(ActKind.Buy) is { } buy)
                {
                    var s = Catalog.Board[p.Position];
                    var price = game.PriceFor(seat, s.Index);
                    var completes = s.Kind == SpaceKind.Project && DevelopyGame.GroupSpaces(s.Group).All(x => x.Index == s.Index || game.Holdings[x.Index].Owner == seat);
                    var blocks = s.Kind == SpaceKind.Project && DevelopyGame.GroupSpaces(s.Group).Any(x => game.Holdings[x.Index].Owner is var o && o >= 0 && o != seat)
                                 && DevelopyGame.GroupSpaces(s.Group).Count(x => game.Holdings[x.Index].Owner == -1) == 1;
                    if (p.Cash - price >= reserve || (completes || blocks) && p.Cash - price >= 40) return buy;
                }
                return Find(ActKind.Pass)!;
            case Phase.Debt:
                if (Find(ActKind.PayDebt) is { } pay) return pay;
                // Sell the most expensive team first, then freeze the cheapest project.
                var down = options.Where(o => o.Kind == ActKind.Downgrade).OrderByDescending(o => DevelopyGame.UpgradeRefund(o.Space)).FirstOrDefault();
                if (down is not null) return down;
                var freeze = options.Where(o => o.Kind == ActKind.Freeze).OrderBy(o => Catalog.Board[o.Space].Price).FirstOrDefault();
                return freeze ?? Find(ActKind.Bankrupt)!;
        }

        // Hostile takeover when it completes a group, or gets close to it with the last piece still free.
        if (Find(ActKind.Takeover) is { } takeover)
        {
            var g = Catalog.Board[takeover.Space].Group;
            var mine = DevelopyGame.GroupSpaces(g).Count(x => x.Index == takeover.Space || game.Holdings[x.Index].Owner == seat);
            var free = DevelopyGame.GroupSpaces(g).Count(x => game.Holdings[x.Index].Owner == -1);
            if ((mine == 3 || mine == 2 && free == 1) && p.Cash - DevelopyGame.TakeoverPrice(takeover.Space) >= reserve) return takeover;
        }

        // Roll or End: manage first, then move on.
        var unfreeze = options.Where(o => o.Kind == ActKind.Unfreeze && p.Cash - DevelopyGame.UnfreezeCost(o.Space) >= reserve * 2).FirstOrDefault();
        if (unfreeze is not null) return unfreeze;
        var upgrade = options.Where(o => o.Kind == ActKind.Upgrade && p.Cash - game.UpgradeCost(seat, o.Space) >= reserve)
            .OrderByDescending(o => Catalog.Board[o.Space].Price).FirstOrDefault();
        if (upgrade is not null) return upgrade;

        if (game.Phase == Phase.Roll && p.InMeeting)
        {
            if (Find(ActKind.PassMeeting) is { } pass) return pass;
            if (p.Tokens >= 3 && Find(ActKind.TokenMeeting) is { } tok) return tok;
            if (game.Round <= 8 && p.Cash >= 400 && Find(ActKind.PayMeeting) is { } payMeeting) return payMeeting;
        }
        return Find(ActKind.Roll) ?? Find(ActKind.EndTurn)!;
    }
}
