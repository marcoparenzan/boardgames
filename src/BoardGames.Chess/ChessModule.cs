using BoardGames.Chess.Services;
using BoardGames.Core;
using Microsoft.Extensions.DependencyInjection;

namespace BoardGames.Chess;

public static class ChessModule
{
    public static readonly GameDescriptor Descriptor = new(
        "chess", "Scacchi con Ralf", "Scacchi",
        "Il club dei pezzi fuori posto: sfida Ralf, l'AI di ufficio, o un collega con un codice invito.",
        "/chess", "_content/BoardGames.Chess/assets/chess.svg", "_content/BoardGames.Chess/chess.css",
        "1–2 giocatori · AI", "10–40 min", "teal", typeof(ChessModule).Assembly, 0);

    /// <summary>Snapshots of chess games go to "Games:LogDirectory" (set by the host).</summary>
    public static IServiceCollection AddChess(this IServiceCollection services)
    {
        services.AddSingleton<ChessRooms>();
        services.AddHttpClient<RalfAgent>(client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton(Descriptor);
        return services;
    }
}
