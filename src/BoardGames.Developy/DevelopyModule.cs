using BoardGames.Core;
using BoardGames.Developy.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BoardGames.Developy;

public static class DevelopyModule
{
    public static readonly GameDescriptor Descriptor = new(
        "developy", "Developy", "Developy",
        "Il gioco da tavolo della software house: compra progetti, incassa budget, sopravvivi ai deploy.",
        "/developy", "_content/BoardGames.Developy/assets/developy-board-900.webp", "_content/BoardGames.Developy/developy.css",
        "2–6 giocatori · bot", "30–60 min", "coral", typeof(DevelopyModule).Assembly, 1);

    public static IServiceCollection AddDevelopy(this IServiceCollection services)
    {
        services.AddSingleton<DevelopyRooms>();
        services.AddSingleton(Descriptor);
        return services;
    }
}
