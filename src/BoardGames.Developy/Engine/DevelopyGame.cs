namespace BoardGames.Developy.Engine;

public enum Phase { Roll, Buy, Card, Fee, Debt, End, Over }

public enum ActKind
{
    Roll, Buy, Pass, Accept, PayFee, TokenFee,
    PayMeeting, TokenMeeting, PassMeeting,
    Upgrade, Downgrade, Freeze, Unfreeze,
    PayDebt, Bankrupt, EndTurn, Takeover,
}

public sealed record Act(ActKind Kind, int Space = -1);

public sealed record PlayerSetup(string Name, string Role, bool Bot);

public sealed class PlayerState(int seat, string name, string role, bool bot)
{
    public int Seat { get; } = seat;
    public string Name { get; } = name;
    public string Role { get; } = role;
    public bool Bot { get; } = bot;
    public string Color => Catalog.PlayerColors[Seat % Catalog.PlayerColors.Length];
    public int Cash { get; internal set; } = Catalog.StartBudget + seat * Catalog.SeatBonus;
    public int Position { get; internal set; }
    public int Tokens { get; internal set; } = Catalog.StartTokens;
    public bool InMeeting { get; internal set; }
    public int MeetingAttempts { get; internal set; }
    public int MeetingPasses { get; internal set; }
    public bool Bankrupt { get; internal set; }
    public int LastIncome { get; internal set; }
}

public sealed class Holding
{
    public int Owner { get; internal set; } = -1;
    public int Level { get; internal set; }
    public bool Frozen { get; internal set; }
}

/// <summary>Money owed. No creditors means the bank (it feeds the Hackathon pot); otherwise each creditor gets <see cref="Each"/>.</summary>
public sealed record Debt(int Amount, int[] Creditors, int Each, string Reason, int ThenMove = 0);

public sealed record LogEntry(int Round, int Seat, string Text);

/// <summary>
/// Authoritative rules of Developy. Every state change goes through <see cref="Apply"/>, which validates
/// seat, phase and target. Dice and the event deck use the injected RNG, so games are replayable in tests.
/// </summary>
public sealed class DevelopyGame
{
    private readonly Random rng;
    private readonly Queue<int> deck = new();
    private readonly List<LogEntry> log = [];
    private bool takeoverDone;

    public DevelopyGame(IReadOnlyList<PlayerSetup> players, int maxRounds = 25, Random? rng = null)
    {
        if (players.Count is < 2 or > 6) throw new ArgumentException("Da 2 a 6 giocatori.", nameof(players));
        this.rng = rng ?? new Random();
        Players = players.Select((p, i) => new PlayerState(i, p.Name, p.Role, p.Bot)).ToArray();
        Holdings = Catalog.Board.Select(_ => new Holding()).ToArray();
        MaxRounds = Math.Clamp(maxRounds, 5, 99);
        Shuffle();
        Log(-1, $"Partita avviata: {Players.Length} consulenti, {MaxRounds} round. Budget iniziale €{Catalog.StartBudget} (+€{Catalog.SeatBonus} per ogni posto dopo il primo).");
    }

    public PlayerState[] Players { get; }
    public Holding[] Holdings { get; }
    public int MaxRounds { get; }
    public int Round { get; private set; } = 1;
    public int Current { get; private set; }
    public Phase Phase { get; private set; } = Phase.Roll;
    public int Version { get; private set; }
    public int LastRoll { get; private set; }
    public int Pot { get; private set; }
    public Card? PendingCard { get; private set; }
    public Debt? PendingDebt { get; private set; }
    public int PendingFee { get; private set; }
    public int[] Winners { get; private set; } = [];
    public IReadOnlyList<LogEntry> History => log;
    public PlayerState Turn => Players[Current];

    // ---------- queries ----------

    public int Fee(int space)
    {
        var s = Catalog.Board[space];
        var h = Holdings[space];
        if (!s.Buyable || h.Owner < 0 || h.Frozen) return 0;
        var owner = Players[h.Owner];
        if (s.Kind == SpaceKind.Cloud)
        {
            var count = Catalog.Board.Count(x => x.Kind == SpaceKind.Cloud && Holdings[x.Index].Owner == h.Owner);
            var fee = Catalog.CloudRent[Math.Clamp(count, 1, 4) - 1];
            return owner.Role == "devops" ? fee * 3 / 2 : fee;
        }
        if (h.Level > 0) return s.Rent![h.Level];
        return OwnsFullGroup(h.Owner, s.Group, requireUnfrozen: true) ? s.Rent![0] * 2 : s.Rent![0];
    }

    public bool OwnsFullGroup(int seat, int group, bool requireUnfrozen = false) =>
        GroupSpaces(group).All(s => Holdings[s.Index].Owner == seat && (!requireUnfrozen || !Holdings[s.Index].Frozen));

    public static IEnumerable<Space> GroupSpaces(int group) => Catalog.Board.Where(s => s.Kind == SpaceKind.Project && s.Group == group);

    public int PriceFor(int seat, int space) => PriceFor(Players[seat].Role, space);
    public int UpgradeCost(int seat, int space) => UpgradeCost(Players[seat].Role, space);

    public static int PriceFor(string role, int space)
    {
        var price = Catalog.Board[space].Price;
        return role == "analyst" && Catalog.Board[space].Kind == SpaceKind.Project ? price * 9 / 10 : price;
    }

    public static int UpgradeCost(string role, int space)
    {
        var cost = Catalog.Groups[Catalog.Board[space].Group].UpgradeCost;
        return role == "cto" ? cost * 7 / 10 : cost;
    }

    /// <summary>Hostile takeover: allowed on an opponent's project with no teams, unless it is part of the owner's complete group.</summary>
    public bool CanTakeover(int seat, int space)
    {
        var s = Catalog.Board[space];
        var h = Holdings[space];
        return s.Kind == SpaceKind.Project && h.Owner >= 0 && h.Owner != seat && h.Level == 0
               && !OwnsFullGroup(h.Owner, s.Group) && !takeoverDone;
    }

    public static int TakeoverPrice(int space) => Catalog.Board[space].Price * 2;

    public static int UpgradeRefund(int space) => Catalog.Groups[Catalog.Board[space].Group].UpgradeCost / 2;
    public static int FreezeValue(int space) => Catalog.Board[space].Price / 2;
    public static int UnfreezeCost(int space) => Catalog.Board[space].Price * 55 / 100;

    public int NetWorth(int seat)
    {
        var p = Players[seat];
        if (p.Bankrupt) return 0;
        var worth = p.Cash;
        foreach (var s in Catalog.Board.Where(s => s.Buyable && Holdings[s.Index].Owner == seat))
        {
            var h = Holdings[s.Index];
            worth += h.Frozen ? s.Price / 2 : s.Price;
            if (s.Kind == SpaceKind.Project) worth += h.Level * Catalog.Groups[s.Group].UpgradeCost;
        }
        return worth;
    }

    public bool CanUpgrade(int seat, int space)
    {
        var s = Catalog.Board[space];
        var h = Holdings[space];
        if (s.Kind != SpaceKind.Project || h.Owner != seat || h.Level >= Catalog.MaxLevel) return false;
        if (!OwnsFullGroup(seat, s.Group, requireUnfrozen: true)) return false;
        return h.Level <= GroupSpaces(s.Group).Min(x => Holdings[x.Index].Level); // build evenly
    }

    public bool CanDowngrade(int seat, int space)
    {
        var s = Catalog.Board[space];
        var h = Holdings[space];
        if (s.Kind != SpaceKind.Project || h.Owner != seat || h.Level == 0) return false;
        return h.Level >= GroupSpaces(s.Group).Max(x => Holdings[x.Index].Level); // sell evenly
    }

    public bool CanFreeze(int seat, int space)
    {
        var s = Catalog.Board[space];
        var h = Holdings[space];
        if (!s.Buyable || h.Owner != seat || h.Frozen) return false;
        return s.Kind == SpaceKind.Cloud || GroupSpaces(s.Group).All(x => Holdings[x.Index].Level == 0);
    }

    public bool CanUnfreeze(int seat, int space) =>
        Catalog.Board[space].Buyable && Holdings[space].Owner == seat && Holdings[space].Frozen;

    /// <summary>All actions the seat may take now. Empty when it is not their turn.</summary>
    public IReadOnlyList<Act> Options(int seat)
    {
        var list = new List<Act>();
        if (Phase == Phase.Over || seat != Current) return list;
        var p = Players[seat];
        switch (Phase)
        {
            case Phase.Roll:
                list.Add(new(ActKind.Roll));
                if (p.InMeeting)
                {
                    if (p.Cash >= Catalog.MeetingFee) list.Add(new(ActKind.PayMeeting));
                    if (p.Tokens > 0) list.Add(new(ActKind.TokenMeeting));
                    if (p.MeetingPasses > 0) list.Add(new(ActKind.PassMeeting));
                }
                AddManagement(seat, list, building: true);
                break;
            case Phase.Buy:
                if (p.Cash >= PriceFor(seat, p.Position)) list.Add(new(ActKind.Buy, p.Position));
                list.Add(new(ActKind.Pass, p.Position));
                break;
            case Phase.Card:
                list.Add(new(ActKind.Accept));
                break;
            case Phase.Fee:
                list.Add(new(ActKind.PayFee));
                if (p.Tokens > 0) list.Add(new(ActKind.TokenFee));
                break;
            case Phase.Debt:
                if (p.Cash >= PendingDebt!.Amount) list.Add(new(ActKind.PayDebt));
                AddManagement(seat, list, building: false);
                list.Add(new(ActKind.Bankrupt));
                break;
            case Phase.End:
                list.Add(new(ActKind.EndTurn));
                if (!p.InMeeting && CanTakeover(seat, p.Position) && p.Cash >= TakeoverPrice(p.Position)) list.Add(new(ActKind.Takeover, p.Position));
                AddManagement(seat, list, building: true);
                break;
        }
        return list;
    }

    private void AddManagement(int seat, List<Act> list, bool building)
    {
        var p = Players[seat];
        foreach (var s in Catalog.Board.Where(s => s.Buyable && Holdings[s.Index].Owner == seat))
        {
            if (building && CanUpgrade(seat, s.Index) && p.Cash >= UpgradeCost(seat, s.Index)) list.Add(new(ActKind.Upgrade, s.Index));
            if (CanDowngrade(seat, s.Index)) list.Add(new(ActKind.Downgrade, s.Index));
            if (CanFreeze(seat, s.Index)) list.Add(new(ActKind.Freeze, s.Index));
            if (building && CanUnfreeze(seat, s.Index) && p.Cash >= UnfreezeCost(s.Index)) list.Add(new(ActKind.Unfreeze, s.Index));
        }
    }

    // ---------- commands ----------

    public void Apply(int seat, Act act, int? expectedVersion = null)
    {
        if (expectedVersion is { } v && v != Version) throw new InvalidOperationException("Lo stato è cambiato: riprova.");
        if (!Options(seat).Contains(act)) throw new InvalidOperationException("Azione non disponibile in questo momento.");
        var p = Players[seat];
        switch (act.Kind)
        {
            case ActKind.Roll: Roll(p); break;
            case ActKind.Buy: Buy(p); break;
            case ActKind.Pass:
                Log(seat, $"rinuncia a {Catalog.Board[p.Position].Name}.");
                Phase = Phase.End;
                break;
            case ActKind.Accept: ResolveCard(p); break;
            case ActKind.PayFee: PayFee(p, half: false); break;
            case ActKind.TokenFee: p.Tokens--; PayFee(p, half: true); break;
            case ActKind.PayMeeting:
                Charge(p, Catalog.MeetingFee, [], "uscita dal meeting", next: Phase.Roll);
                LeaveMeeting(p, "paga e lascia il meeting.");
                break;
            case ActKind.TokenMeeting: p.Tokens--; LeaveMeeting(p, "usa un AI Token per farsi riassumere il meeting ed esce."); break;
            case ActKind.PassMeeting: p.MeetingPasses--; LeaveMeeting(p, "mostra il calendario bloccato ed esce dal meeting."); break;
            case ActKind.Upgrade: Upgrade(p, act.Space); break;
            case ActKind.Downgrade: Downgrade(p, act.Space); break;
            case ActKind.Freeze:
                Holdings[act.Space].Frozen = true;
                Earn(p, FreezeValue(act.Space));
                Log(seat, $"congela {Catalog.Board[act.Space].Name} (+€{FreezeValue(act.Space)}).");
                break;
            case ActKind.Unfreeze:
                Holdings[act.Space].Frozen = false;
                p.Cash -= UnfreezeCost(act.Space);
                Log(seat, $"riattiva {Catalog.Board[act.Space].Name} (−€{UnfreezeCost(act.Space)}).");
                break;
            case ActKind.PayDebt: PayDebt(p); break;
            case ActKind.Bankrupt: GoBankrupt(p); break;
            case ActKind.EndTurn: NextTurn(); break;
            case ActKind.Takeover:
                var seller = Players[Holdings[act.Space].Owner];
                var price = TakeoverPrice(act.Space);
                p.Cash -= price;
                Earn(seller, price);
                Holdings[act.Space].Owner = seat;
                takeoverDone = true;
                Log(seat, $"lancia un'acquisizione ostile: rileva {Catalog.Board[act.Space].Name} da {seller.Name} per €{price}.");
                break;
        }
        Version++;
    }

    private void Roll(PlayerState p)
    {
        LastRoll = rng.Next(1, 7);
        if (p.InMeeting)
        {
            if (LastRoll == 6) { LeaveMeeting(p, "tira 6 e scappa dal meeting!"); Move(p, LastRoll); return; }
            p.MeetingAttempts++;
            if (p.MeetingAttempts < 3)
            {
                Log(p.Seat, $"tira {LastRoll}: il meeting continua ({p.MeetingAttempts}/3).");
                Phase = Phase.End;
                return;
            }
            Log(p.Seat, $"tira {LastRoll} e dopo tre tentativi paga €{Catalog.MeetingFee} per uscire.");
            p.InMeeting = false;
            p.MeetingAttempts = 0;
            if (!Charge(p, Catalog.MeetingFee, [], "uscita forzata dal meeting", next: Phase.End, thenMove: LastRoll)) return;
        }
        else Log(p.Seat, $"tira {LastRoll}.");
        Move(p, LastRoll);
    }

    private void LeaveMeeting(PlayerState p, string text)
    {
        p.InMeeting = false;
        p.MeetingAttempts = 0;
        Log(p.Seat, text);
    }

    private void Move(PlayerState p, int steps)
    {
        var target = ((p.Position + steps) % Catalog.Spaces + Catalog.Spaces) % Catalog.Spaces;
        if (steps > 0 && p.Position + steps >= Catalog.Spaces) PassKickoff(p);
        p.Position = target;
        Land(p);
    }

    private void MoveTo(PlayerState p, int target)
    {
        if (target <= p.Position) PassKickoff(p);
        p.Position = target;
        Land(p);
    }

    private void PassKickoff(PlayerState p)
    {
        var bonus = Catalog.KickoffBonus + (p.Role == "developer" ? 25 : 0);
        Earn(p, bonus);
        Log(p.Seat, $"passa dal Kick-off: nuovo sprint, +€{bonus}.");
    }

    private void Land(PlayerState p)
    {
        var s = Catalog.Board[p.Position];
        var h = Holdings[p.Position];
        Phase = Phase.End;
        switch (s.Kind)
        {
            case SpaceKind.Project or SpaceKind.Cloud:
                if (h.Owner < 0) { Phase = Phase.Buy; Log(p.Seat, $"arriva su {s.Name}: progetto libero a €{PriceFor(p.Seat, s.Index)}."); }
                else if (h.Owner == p.Seat) Log(p.Seat, $"arriva sul suo {s.Name}.");
                else if (h.Frozen) Log(p.Seat, $"arriva su {s.Name}, congelato: nessuna fee.");
                else
                {
                    PendingFee = Fee(s.Index);
                    Log(p.Seat, $"arriva su {s.Name} di {Players[h.Owner].Name}: consulting fee €{PendingFee}.");
                    if (p.Tokens > 0) Phase = Phase.Fee;
                    else PayFee(p, half: false);
                }
                break;
            case SpaceKind.Event:
                PendingCard = Catalog.Deck[Draw()];
                Phase = Phase.Card;
                Log(p.Seat, $"pesca un imprevisto: {PendingCard.Title}.");
                break;
            case SpaceKind.AiLab:
                if (p.Tokens < Catalog.MaxTokens) { p.Tokens++; Log(p.Seat, "visita l'AI Lab: +1 AI Token."); }
                else Log(p.Seat, "visita l'AI Lab, ma ha già il massimo di AI Token.");
                break;
            case SpaceKind.Tax:
                var debt = p.Role == "security" ? Catalog.TechDebt / 2 : Catalog.TechDebt;
                Log(p.Seat, $"ripaga il Tech Debt: €{debt} al montepremi dell'Hackathon.");
                Charge(p, debt, [], "Tech Debt");
                break;
            case SpaceKind.Hackathon:
                if (Pot > 0) { Log(p.Seat, $"vince l'Hackathon: montepremi €{Pot}!"); Earn(p, Pot); Pot = 0; }
                else Log(p.Seat, "partecipa all'Hackathon: niente montepremi, solo pizza fredda.");
                break;
            case SpaceKind.Escalation:
                SendToMeeting(p, "riceve un'escalation e viene convocato all'Endless Meeting.");
                break;
            case SpaceKind.Meeting:
                Log(p.Seat, "passa davanti all'Endless Meeting. Solo di passaggio.");
                break;
            case SpaceKind.Start:
                break;
        }
    }

    private void SendToMeeting(PlayerState p, string text)
    {
        p.Position = Catalog.MeetingIndex;
        p.InMeeting = true;
        p.MeetingAttempts = 0;
        Phase = Phase.End;
        Log(p.Seat, text);
    }

    private void Buy(PlayerState p)
    {
        var price = PriceFor(p.Seat, p.Position);
        p.Cash -= price;
        Holdings[p.Position].Owner = p.Seat;
        Log(p.Seat, $"acquisisce {Catalog.Board[p.Position].Name} per €{price}.");
        Phase = Phase.End;
    }

    private void PayFee(PlayerState p, bool half)
    {
        var owner = Holdings[p.Position].Owner;
        var amount = half ? PendingFee / 2 : PendingFee;
        if (half) Log(p.Seat, $"usa un AI Token per negoziare: paga solo €{amount}.");
        PendingFee = 0;
        Charge(p, amount, [owner], $"fee a {Players[owner].Name}");
    }

    private void Upgrade(PlayerState p, int space)
    {
        var cost = UpgradeCost(p.Seat, space);
        p.Cash -= cost;
        var h = Holdings[space];
        h.Level++;
        Log(p.Seat, h.Level == Catalog.MaxLevel
            ? $"nomina un architetto su {Catalog.Board[space].Name} (−€{cost})."
            : $"aggiunge un developer a {Catalog.Board[space].Name} (−€{cost}).");
    }

    private void Downgrade(PlayerState p, int space)
    {
        Holdings[space].Level--;
        Earn(p, UpgradeRefund(space));
        Log(p.Seat, $"riduce il team di {Catalog.Board[space].Name} (+€{UpgradeRefund(space)}).");
    }

    private void ResolveCard(PlayerState p)
    {
        var card = PendingCard!;
        PendingCard = null;
        Phase = Phase.End;
        var others = Players.Where(x => !x.Bankrupt && x.Seat != p.Seat).ToArray();
        switch (card.Effect)
        {
            case CardEffect.Money when card.Value > 0:
                var gain = p.Role == "designer" ? card.Value * 3 / 2 : card.Value;
                Earn(p, gain);
                Log(p.Seat, $"incassa €{gain}.");
                break;
            case CardEffect.Money:
                Charge(p, -card.Value, [], card.Title);
                break;
            case CardEffect.MoveTo: MoveTo(p, card.Value); break;
            case CardEffect.MoveBy: Move(p, card.Value); break;
            case CardEffect.GoToMeeting: SendToMeeting(p, "va dritto all'Endless Meeting."); break;
            case CardEffect.Token:
                p.Tokens = Math.Min(Catalog.MaxTokens, p.Tokens + card.Value);
                break;
            case CardEffect.MeetingPass: p.MeetingPasses++; break;
            case CardEffect.NearestCloud:
                var cloud = Enumerable.Range(1, Catalog.Spaces).Select(i => (p.Position + i) % Catalog.Spaces)
                    .First(i => Catalog.Board[i].Kind == SpaceKind.Cloud);
                MoveTo(p, cloud);
                break;
            case CardEffect.CollectFromEach:
                var each = p.Role == "designer" ? card.Value * 3 / 2 : card.Value;
                foreach (var o in others)
                {
                    var paid = Math.Min(o.Cash, each); // the payer never goes into debt for a gift
                    o.Cash -= paid;
                    Earn(p, paid);
                }
                Log(p.Seat, $"riceve €{each} da ogni consulente.");
                break;
            case CardEffect.PayEach:
                Charge(p, card.Value * others.Length, others.Select(o => o.Seat).ToArray(), card.Title, each: card.Value);
                break;
            case CardEffect.Repairs:
                var owned = Catalog.Board.Where(s => s.Kind == SpaceKind.Project && Holdings[s.Index].Owner == p.Seat).Select(s => Holdings[s.Index].Level).ToArray();
                var cost = owned.Sum(l => l == Catalog.MaxLevel ? card.Value2 : l * card.Value);
                if (p.Role == "security") cost /= 2;
                if (cost == 0) Log(p.Seat, "non ha team da patchare: nessun costo.");
                else Charge(p, cost, [], card.Title);
                break;
        }
    }

    /// <summary>Takes money or opens a debt. Returns true when paid immediately.</summary>
    private bool Charge(PlayerState p, int amount, int[] creditors, string reason, Phase next = Phase.End, int each = 0, int thenMove = 0)
    {
        if (amount <= 0) { Phase = next; return true; }
        if (p.Cash >= amount)
        {
            p.Cash -= amount;
            Distribute(amount, creditors, each);
            Log(p.Seat, $"paga €{amount} ({reason}).");
            Phase = next;
            return true;
        }
        PendingDebt = new Debt(amount, creditors, creditors.Length > 0 && each == 0 ? amount : each, reason, thenMove);
        Phase = Phase.Debt;
        Log(p.Seat, $"deve €{amount} ({reason}) ma ha solo €{p.Cash}: serve liquidità.");
        return false;
    }

    private void Distribute(int amount, int[] creditors, int each)
    {
        if (creditors.Length == 0) { Pot += amount; return; }
        if (creditors.Length == 1) { Earn(Players[creditors[0]], amount); return; }
        foreach (var c in creditors) Earn(Players[c], each);
    }

    private void PayDebt(PlayerState p)
    {
        var d = PendingDebt!;
        PendingDebt = null;
        p.Cash -= d.Amount;
        Distribute(d.Amount, d.Creditors, d.Each);
        Log(p.Seat, $"salda il debito di €{d.Amount} ({d.Reason}).");
        Phase = Phase.End;
        if (d.ThenMove > 0) Move(p, d.ThenMove);
    }

    private void GoBankrupt(PlayerState p)
    {
        var d = PendingDebt!;
        PendingDebt = null;
        var heir = d.Creditors.Length == 1 ? d.Creditors[0] : -1;
        foreach (var s in Catalog.Board.Where(s => s.Buyable && Holdings[s.Index].Owner == p.Seat))
        {
            var h = Holdings[s.Index];
            if (h.Level > 0) { p.Cash += h.Level * UpgradeRefund(s.Index); h.Level = 0; }
            h.Owner = heir;
            if (heir < 0) h.Frozen = false;
        }
        if (heir >= 0) Earn(Players[heir], p.Cash);
        else if (d.Creditors.Length > 1) foreach (var c in d.Creditors) Earn(Players[c], p.Cash / d.Creditors.Length);
        else Pot += p.Cash;
        p.Cash = 0;
        p.Bankrupt = true;
        p.InMeeting = false;
        Log(p.Seat, heir >= 0
            ? $"dichiara bancarotta: {Players[heir].Name} rileva progetti e cassa."
            : "dichiara bancarotta: i progetti tornano sul mercato.");
        if (Players.Count(x => !x.Bankrupt) <= 1) { Finish("ultimo consulente in piedi"); return; }
        NextTurn();
    }

    private void NextTurn()
    {
        PendingCard = null;
        PendingDebt = null;
        PendingFee = 0;
        takeoverDone = false;
        var next = Current;
        do
        {
            next = (next + 1) % Players.Length;
            if (next == 0) Round++;
        } while (Players[next].Bankrupt);
        if (Round > MaxRounds) { Round = MaxRounds; Finish("fine dei round: vince la valutazione più alta"); return; }
        Current = next;
        Phase = Phase.Roll;
    }

    private void Finish(string reason)
    {
        Phase = Phase.Over;
        var alive = Players.Where(p => !p.Bankrupt).ToArray();
        var best = alive.Max(p => NetWorth(p.Seat));
        Winners = alive.Where(p => NetWorth(p.Seat) == best).Select(p => p.Seat).ToArray();
        Log(-1, $"Fine partita ({reason}). Vince {string.Join(" e ", Winners.Select(w => Players[w].Name))} con €{best:N0}.");
    }

    private void Earn(PlayerState p, int amount)
    {
        p.Cash += amount;
        p.LastIncome = amount;
    }

    private int Draw()
    {
        if (deck.Count == 0) Shuffle();
        return deck.Dequeue();
    }

    private void Shuffle()
    {
        var order = Enumerable.Range(0, Catalog.Deck.Length).ToArray();
        rng.Shuffle(order);
        foreach (var i in order) deck.Enqueue(i);
    }

    private void Log(int seat, string text)
    {
        log.Add(new(Round, seat, seat >= 0 ? $"{Players[seat].Name} {text}" : text));
        if (log.Count > 400) log.RemoveRange(0, 100);
    }
}
