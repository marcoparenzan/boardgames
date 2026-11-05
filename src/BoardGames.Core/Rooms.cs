using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace BoardGames.Core;

/// <summary>
/// Catalog entry for a game module. Each module registers one; the host reads them to build the lobby,
/// the navigation, the router (<see cref="Assembly"/>) and the stylesheet links (<see cref="Stylesheet"/>).
/// </summary>
public sealed record GameDescriptor(
    string Id, string Title, string NavTitle, string Tagline, string Route, string Image, string Stylesheet,
    string Players, string Duration, string Accent, Assembly Assembly, int Order = 0);

public sealed record ChatLine(DateTimeOffset At, string Author, string Text);

/// <summary>Invitation codes: unambiguous alphabet (no 0/O, 1/I/L), 8 chars ≈ 40 bits.</summary>
public static class RoomCodes
{
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    public static string New(int length = 8) => string.Create(length, 0, static (span, _) =>
    {
        for (var i = 0; i < span.Length; i++) span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
    });
    public static string Normalize(string? code) =>
        new((code ?? "").Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>Base for a server-side room: one lock serializes every action, one event fans out updates to circuits.</summary>
public abstract class RoomBase(string code)
{
    public string Code { get; } = code;
    public object Sync { get; } = new();
    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;
    public event Action? Changed;
    public void Touch() => LastActivity = DateTimeOffset.UtcNow;
    /// <summary>Call outside the lock. A failing subscriber never breaks the others.</summary>
    public void Notify()
    {
        Touch();
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try { ((Action)handler)(); } catch { /* a disconnected circuit must not stop the room */ }
        }
    }
    /// <summary>Hook for rooms that own timers or background work.</summary>
    public virtual void Close() { }
}

/// <summary>In-memory room store with a capacity limit and idle expiry.</summary>
public sealed class RoomStore<TRoom> : IDisposable where TRoom : RoomBase
{
    private readonly ConcurrentDictionary<string, TRoom> rooms = new();
    private readonly Timer sweeper;
    private readonly ILogger logger;
    public int Capacity { get; }
    public TimeSpan IdleTimeout { get; }

    public RoomStore(ILogger logger, int capacity = 500, TimeSpan? idleTimeout = null)
    {
        this.logger = logger;
        Capacity = capacity;
        IdleTimeout = idleTimeout ?? TimeSpan.FromHours(6);
        sweeper = new Timer(_ => Sweep(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public int Count => rooms.Count;

    public TRoom Create(Func<string, TRoom> factory)
    {
        if (rooms.Count >= Capacity) Sweep();
        if (rooms.Count >= Capacity) throw new InvalidOperationException("Troppe stanze aperte: riprova più tardi.");
        while (true)
        {
            var room = factory(RoomCodes.New());
            if (rooms.TryAdd(room.Code, room)) return room;
        }
    }

    public TRoom? Find(string? code) => rooms.TryGetValue(RoomCodes.Normalize(code), out var room) ? room : null;

    public int Sweep()
    {
        var limit = DateTimeOffset.UtcNow - IdleTimeout;
        var removed = 0;
        foreach (var (code, room) in rooms)
        {
            if (room.LastActivity >= limit || !rooms.TryRemove(code, out _)) continue;
            room.Close();
            removed++;
        }
        if (removed > 0) logger.LogInformation("Removed {Count} idle rooms", removed);
        return removed;
    }

    public void Dispose()
    {
        sweeper.Dispose();
        foreach (var room in rooms.Values) room.Close();
    }
}
