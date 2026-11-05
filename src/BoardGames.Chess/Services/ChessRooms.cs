using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using BoardGames.Core;
using BoardGames.Chess.Engine;

namespace BoardGames.Chess.Services;
public record ChessView(string Code, bool Remote, bool Ready, int Version, string Fen, string Cells, string Status, bool Finished, bool WhiteTurn, string[] LegalMoves, Transition[] Log, ChatLine[] Chat, string Pgn);
public sealed class ChessRoom(string code, bool remote, string host) : RoomBase(code)
{
    public bool Remote { get; } = remote;
    public string Host { get; } = host;
    public string? Guest { get; set; }
    public ChessGame Game { get; } = new();
    public List<Transition> Log { get; } = [];
    public List<ChatLine> Chat { get; } = [new(DateTimeOffset.UtcNow, "Ralf", "Benvenuto al club! Qui il re non può delegare le proprie responsabilità.")];
    public ChessView View() { lock (Sync) return new(Code, Remote, !Remote || Guest is not null, Log.Count, Game.State, Game.Cells, Game.Status, Game.Finished, Game.WhiteTurn, Game.LegalMoves, Log.ToArray(), Chat.ToArray(), Game.Pgn); }
    public bool IsWhite(string player) => player == Host;
    public bool Member(string player) => player == Host || player == Guest;
}
public sealed class ChessRooms(IConfiguration config, ILogger<ChessRooms> logger) : IDisposable
{
    private readonly RoomStore<ChessRoom> rooms = new(logger);
    public ChessRoom Create(bool remote, string player) => rooms.Create(code => new ChessRoom(code, remote, player));
    public void Dispose() => rooms.Dispose();
    public ChessRoom Join(string code, string player)
    {
        var room = rooms.Find(code);
        if (room is null || !room.Remote) throw new InvalidOperationException("Invito inesistente o scaduto.");
        lock (room.Sync) { if (room.Member(player)) return room; if (room.Guest is not null) throw new InvalidOperationException("Partita già occupata."); room.Guest = player; room.Chat.Add(new(DateTimeOffset.UtcNow, "Sistema", "Il Nero è arrivato. Buona partita!")); }
        room.Notify(); return room;
    }
    public void Move(ChessRoom room, string player, string move, int version, bool agent = false)
    {
        lock (room.Sync)
        {
            if (version != room.Log.Count) throw new InvalidOperationException("Stato cambiato: riprova.");
            if (agent ? room.Remote || room.Game.WhiteTurn : !room.Member(player) || room.IsWhite(player) != room.Game.WhiteTurn || (room.Remote && room.Guest is null)) throw new InvalidOperationException("Non è il tuo turno.");
            var transition = room.Game.Apply(move, version); room.Log.Add(transition);
            room.Chat.Add(new(DateTimeOffset.UtcNow, agent ? "Ralf" : room.IsWhite(player) ? "Bianco" : "Nero", $"{transition.San} ({move})"));
            Save(room);
        }
        room.Notify();
    }
    public void Chat(ChessRoom room, string player, string text, bool agent = false)
    {
        lock (room.Sync) { if (!room.Member(player)) throw new InvalidOperationException("Non autorizzato."); text = text.Trim(); if (text.Length is 0 or > 1000) throw new InvalidOperationException("Scrivi da 1 a 1000 caratteri."); room.Chat.Add(new(DateTimeOffset.UtcNow, agent ? "Ralf" : room.IsWhite(player) ? "Bianco" : "Nero", text)); if (room.Chat.Count > 300) room.Chat.RemoveAt(0); }
        room.Notify();
    }
    private void Save(ChessRoom room)
    {
        try { var dir = config["Games:LogDirectory"] ?? "../../../../ops/data/games"; Directory.CreateDirectory(dir); var path = Path.Combine(dir, room.Code + ".json"); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(room.View(), new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogError(ex, "Impossibile salvare log partita {Code}", room.Code); }
    }
}
