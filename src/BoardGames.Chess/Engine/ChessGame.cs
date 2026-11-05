using Chess;

namespace BoardGames.Chess.Engine;

public record Transition(string Game, int Version, string StatoCorrente, string Mossa, string NuovoStato, string San);
public interface IBoardGame
{
    string Kind { get; }
    string State { get; }
    string[] LegalMoves { get; }
    bool Finished { get; }
    Transition Apply(string move, int version);
}

public sealed class ChessGame : IBoardGame
{
    private readonly ChessBoard board;
    public ChessGame(string? fen = null)
    {
        board = Detached(() => fen is null ? new ChessBoard() { AutoEndgameRules = AutoEndgameRules.All } : ChessBoard.LoadFromFen(fen));
    }

    /// <summary>
    /// Gera.Chess captures SynchronizationContext.Current and raises its events (e.g. OnCaptured) with a blocking Send.
    /// Under Blazor the current context is the circuit renderer, and the move runs on that same context, so the first
    /// capture deadlocks the circuit. Every board call that may raise events runs with no context.
    /// </summary>
    private static T Detached<T>(Func<T> action)
    {
        var previous = SynchronizationContext.Current;
        if (previous is null) return action();
        SynchronizationContext.SetSynchronizationContext(null);
        try { return action(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    public string Kind => "chess";
    public string State => board.ToFen();
    public bool Finished => board.IsEndGame;
    public bool IsCheckmate => board.EndGame?.EndgameType == EndgameType.Checkmate;
    public string Status => Finished ? EndText : (board.WhiteKingChecked || board.BlackKingChecked ? "Scacco! " : "") + (WhiteTurn ? "Tocca al Bianco" : "Tocca al Nero");
    private string EndText => board.EndGame?.EndgameType switch
    {
        EndgameType.Checkmate => $"Scacco matto: vince il {(board.EndGame.WonSide == PieceColor.White ? "Bianco" : "Nero")}",
        EndgameType.Stalemate => "Patta per stallo",
        EndgameType.InsufficientMaterial => "Patta: materiale insufficiente",
        EndgameType.FiftyMoveRule => "Patta: regola delle 50 mosse",
        EndgameType.Repetition => "Patta per ripetizione",
        { } other => $"Fine partita: {other}",
        null => "Fine partita",
    };
    public bool WhiteTurn => State.Split(' ')[1] == "w";
    public string Pgn => board.ToPgn();
    public static string Uci(Move m) => m.OriginalPosition.ToString() + m.NewPosition.ToString() + (m.IsPromotion ? m.Parameter!.ShortStr[1..].ToLowerInvariant() : "");
    public string[] LegalMoves => Finished ? [] : board.Moves().Select(Uci).Distinct().ToArray();
    public Transition Apply(string move, int version)
    {
        if (Finished) throw new InvalidOperationException("Partita terminata.");
        var legal = board.Moves().FirstOrDefault(m => Uci(m) == move);
        if (legal is null) throw new InvalidOperationException("Mossa non legale.");
        var before = State;
        if (!Detached(() => board.Move(legal))) throw new InvalidOperationException("Mossa rifiutata dal motore.");
        var san = legal.San ?? move;
        return new(Kind, version + 1, before, move, State, san);
    }
    public string Cells => string.Concat(State.Split(' ')[0].Where(c => c != '/').Select(c => char.IsDigit(c) ? new string('.', c - '0') : c.ToString()));
}
