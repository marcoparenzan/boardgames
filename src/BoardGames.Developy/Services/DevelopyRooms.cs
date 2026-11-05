using BoardGames.Core;
using BoardGames.Developy.Engine;
using Microsoft.Extensions.Logging;

namespace BoardGames.Developy.Services;

public enum SeatKind { Human, Bot, Open }

public sealed class Seat(string name, string role, SeatKind kind, string? token)
{
    public string Name { get; internal set; } = name;
    public string Role { get; internal set; } = role;
    public SeatKind Kind { get; internal set; } = kind;
    public string? Token { get; internal set; } = token;
}

/// <summary>What a creator asks for each extra seat.</summary>
public enum SeatRequest { Bot, Remote, Local }

public sealed record SeatView(int Index, string Name, string Role, SeatKind Kind, bool Mine);

public sealed record PlayerView(
    int Seat, string Name, string Role, bool Bot, string Color, int Cash, int Position, int Tokens,
    bool InMeeting, int MeetingAttempts, int MeetingPasses, bool Bankrupt, int NetWorth, int LastIncome, bool Mine);

public sealed record HoldingView(int Owner, int Level, bool Frozen, int Fee);

public sealed record TableView(
    string Code, bool Started, bool IsHost, int MaxRounds, SeatView[] Seats, ChatLine[] Chat,
    int Version, Phase Phase, int Round, int Current, int LastRoll, int Pot,
    Card? PendingCard, Debt? PendingDebt, int PendingFee, int[] Winners,
    PlayerView[] Players, HoldingView[] Holdings, LogEntry[] Log, Act[] Options)
{
    public bool MyTurn => Options.Length > 0;
    public PlayerView? CurrentPlayer => Started ? Players[Current] : null;
    public bool Watching => Seats.All(s => !s.Mine);
}

public sealed class DevelopyRoom(string code, string hostToken, int maxRounds) : RoomBase(code)
{
    private readonly CancellationTokenSource closing = new();
    public string HostToken { get; } = hostToken;
    public int MaxRounds { get; } = maxRounds;
    public List<Seat> Seats { get; } = [];
    public DevelopyGame? Game { get; internal set; }
    public List<ChatLine> Chat { get; } = [];
    internal bool BotRunning { get; set; }
    internal CancellationToken Closing => closing.Token;
    public override void Close() => closing.Cancel();

    public TableView View(string? token)
    {
        lock (Sync)
        {
            var seats = Seats.Select((s, i) => new SeatView(i, s.Name, s.Role, s.Kind, token is not null && s.Token == token)).ToArray();
            var g = Game;
            if (g is null)
                return new(Code, false, token == HostToken, MaxRounds, seats, Chat.ToArray(), 0, Phase.Roll, 0, 0, 0, 0, null, null, 0, [], [], [], [], []);
            var players = g.Players.Select(p => new PlayerView(p.Seat, p.Name, p.Role, p.Bot, p.Color, p.Cash, p.Position, p.Tokens,
                p.InMeeting, p.MeetingAttempts, p.MeetingPasses, p.Bankrupt, g.NetWorth(p.Seat), p.LastIncome, seats[p.Seat].Mine)).ToArray();
            var holdings = g.Holdings.Select((h, i) => new HoldingView(h.Owner, h.Level, h.Frozen, g.Fee(i))).ToArray();
            var options = seats[g.Current].Mine ? g.Options(g.Current).ToArray() : [];
            return new(Code, true, token == HostToken, MaxRounds, seats, Chat.ToArray(), g.Version, g.Phase, g.Round, g.Current, g.LastRoll, g.Pot,
                g.PendingCard, g.PendingDebt, g.PendingFee, g.Winners, players, holdings, g.History.TakeLast(80).ToArray(), options);
        }
    }
}

public sealed class DevelopyRooms : IDisposable
{
    private readonly RoomStore<DevelopyRoom> store;
    private readonly ILogger<DevelopyRooms> logger;

    /// <summary>Pause before a bot acts; tests set it to zero.</summary>
    public TimeSpan BotDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    public DevelopyRooms(ILogger<DevelopyRooms> logger)
    {
        this.logger = logger;
        store = new RoomStore<DevelopyRoom>(logger);
    }

    public DevelopyRoom? Find(string? code) => store.Find(code);

    public DevelopyRoom Create(string token, string name, string role, IReadOnlyList<SeatRequest> extra, int maxRounds = 25)
    {
        if (extra.Count is < 1 or > 5) throw new InvalidOperationException("Servono da 2 a 6 posti.");
        var room = store.Create(code => new DevelopyRoom(code, token, Math.Clamp(maxRounds, 5, 60)));
        lock (room.Sync)
        {
            room.Seats.Add(new(CleanName(name, "Consulente"), Catalog.RoleOf(role).Id, SeatKind.Human, token));
            var local = 1;
            foreach (var request in extra)
            {
                var free = FreeRole(room);
                room.Seats.Add(request switch
                {
                    SeatRequest.Bot => new($"Bot {Catalog.RoleOf(free).Short}", free, SeatKind.Bot, null),
                    SeatRequest.Local => new($"Giocatore {++local}", free, SeatKind.Human, token),
                    _ => new("In attesa…", free, SeatKind.Open, null),
                });
            }
            room.Chat.Add(new(DateTimeOffset.UtcNow, "Sistema", $"Stanza {room.Code} aperta. Condividi il codice con i colleghi."));
            if (room.Seats.All(s => s.Kind != SeatKind.Open)) StartLocked(room);
        }
        Kick(room);
        return room;
    }

    public void Join(DevelopyRoom room, string token, string name, string role)
    {
        lock (room.Sync)
        {
            if (room.Seats.Any(s => s.Token == token)) return;
            if (room.Game is not null) throw new InvalidOperationException("La partita è già iniziata: puoi solo guardare.");
            var seat = room.Seats.FirstOrDefault(s => s.Kind == SeatKind.Open) ?? throw new InvalidOperationException("Nessun posto libero in questa stanza.");
            var wanted = Catalog.RoleOf(role).Id;
            if (room.Seats.Any(s => s != seat && s.Role == wanted)) wanted = seat.Role; // pre-assigned roles are unique
            seat.Name = CleanName(name, "Ospite");
            seat.Role = wanted;
            seat.Kind = SeatKind.Human;
            seat.Token = token;
            room.Chat.Add(new(DateTimeOffset.UtcNow, "Sistema", $"{seat.Name} ({Catalog.RoleOf(seat.Role).Name}) è entrato nella war room."));
            if (room.Seats.All(s => s.Kind != SeatKind.Open)) StartLocked(room);
        }
        room.Notify();
        Kick(room);
    }

    /// <summary>Host only: fills open seats with bots and starts.</summary>
    public void Start(DevelopyRoom room, string token)
    {
        lock (room.Sync)
        {
            if (token != room.HostToken) throw new InvalidOperationException("Solo chi ha creato la stanza può avviarla.");
            if (room.Game is not null) return;
            foreach (var s in room.Seats.Where(s => s.Kind == SeatKind.Open)) { s.Kind = SeatKind.Bot; s.Name = $"Bot {Catalog.RoleOf(s.Role).Short}"; }
            StartLocked(room);
        }
        room.Notify();
        Kick(room);
    }

    public void Act(DevelopyRoom room, string token, Act act, int version)
    {
        lock (room.Sync)
        {
            var g = room.Game ?? throw new InvalidOperationException("La partita non è ancora iniziata.");
            if (room.Seats[g.Current].Token != token || room.Seats[g.Current].Kind != SeatKind.Human)
                throw new InvalidOperationException("Non è il tuo turno.");
            g.Apply(g.Current, act, version);
        }
        room.Notify();
        Kick(room);
    }

    public void Say(DevelopyRoom room, string token, string text)
    {
        text = text.Trim();
        if (text.Length is 0 or > 500) throw new InvalidOperationException("Scrivi da 1 a 500 caratteri.");
        lock (room.Sync)
        {
            var seat = room.Seats.FirstOrDefault(s => s.Token == token) ?? throw new InvalidOperationException("Solo i giocatori possono scrivere.");
            room.Chat.Add(new(DateTimeOffset.UtcNow, seat.Name, text));
            if (room.Chat.Count > 200) room.Chat.RemoveAt(0);
        }
        room.Notify();
    }

    private static void StartLocked(DevelopyRoom room)
    {
        room.Game = new DevelopyGame(room.Seats.Select(s => new PlayerSetup(s.Name, s.Role, s.Kind == SeatKind.Bot)).ToArray(), room.MaxRounds);
        room.Chat.Add(new(DateTimeOffset.UtcNow, "Sistema", "Si parte! Il primo sprint è iniziato."));
    }

    /// <summary>Runs bot turns on the server, one action at a time, so every client sees them happen.</summary>
    private void Kick(DevelopyRoom room)
    {
        lock (room.Sync)
        {
            if (room.BotRunning || room.Game is not { Phase: not Phase.Over } g || !g.Turn.Bot) return;
            room.BotRunning = true;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var pause = BotDelay;
                while (true)
                {
                    if (pause > TimeSpan.Zero) await Task.Delay(pause, room.Closing);
                    lock (room.Sync)
                    {
                        var g = room.Game!;
                        if (g.Phase == Phase.Over || !g.Turn.Bot) { room.BotRunning = false; break; }
                        var act = BotBrain.Choose(g, g.Current);
                        g.Apply(g.Current, act);
                        // Leave time for clients to animate the move before the next bot action.
                        pause = act.Kind == ActKind.Roll ? BotDelay + TimeSpan.FromMilliseconds(170 * Math.Max(1, g.LastRoll)) : BotDelay;
                    }
                    room.Notify();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Bot failure in room {Code}", room.Code);
                lock (room.Sync) room.BotRunning = false;
            }
        });
    }

    private static string FreeRole(DevelopyRoom room) =>
        Catalog.Roles.Select(r => r.Id).FirstOrDefault(id => room.Seats.All(s => s.Role != id)) ?? Catalog.Roles[0].Id;

    private static string CleanName(string? name, string fallback)
    {
        var clean = new string((name ?? "").Trim().Where(c => !char.IsControl(c)).Take(18).ToArray());
        return clean.Length == 0 ? fallback : clean;
    }

    public void Dispose() => store.Dispose();
}
