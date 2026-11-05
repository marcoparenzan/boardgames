namespace BoardGames.Developy.Engine;

public enum SpaceKind { Start, Project, Cloud, Event, AiLab, Tax, Meeting, Hackathon, Escalation }

public sealed record Group(int Id, string Name, string Color, int UpgradeCost);

public sealed record Space(int Index, SpaceKind Kind, string Name, string Icon, int Group = -1, int Price = 0, int[]? Rent = null)
{
    public bool Buyable => Kind is SpaceKind.Project or SpaceKind.Cloud;
}

public sealed record Role(string Id, string Name, string Short, string Perk);

public enum CardEffect { Money, MoveTo, MoveBy, GoToMeeting, Token, CollectFromEach, PayEach, Repairs, MeetingPass, NearestCloud }

public sealed record Card(int Id, string Icon, string Title, string Text, CardEffect Effect, int Value = 0, int Value2 = 0);

/// <summary>Static content of the game: board, groups, roles, event deck, economy constants.</summary>
public static class Catalog
{
    public const int Spaces = 24;
    public const int StartBudget = 1500;
    /// <summary>Later seats start richer to offset the first-mover advantage on purchases.</summary>
    public const int SeatBonus = 75;
    public const int KickoffBonus = 200;
    public const int MeetingIndex = 6;
    public const int HackathonIndex = 12;
    public const int MeetingFee = 50;
    public const int TechDebt = 100;
    public const int MaxTokens = 5;
    public const int StartTokens = 1;
    public const int MaxLevel = 4; // 1-3 developers, 4 = architect
    public static readonly int[] CloudRent = [25, 50, 100, 200];

    public static readonly Group[] Groups =
    [
        new(0, "API MIGRATION", "coral", 50),
        new(1, "CLOUD PLATFORM", "teal", 75),
        new(2, "DATA & AI", "mustard", 100),
        new(3, "ENTERPRISE", "blue", 125),
    ];

    public static readonly Space[] Board = Build();

    private static Space[] Build()
    {
        Space P(int i, string name, int group, int price) => new(i, SpaceKind.Project, name, "", group, price, RentTable(price));
        Space C(int i, string name) => new(i, SpaceKind.Cloud, name, "☁", -1, 200);
        return
        [
            new(0, SpaceKind.Start, "KICK-OFF", "▶"),
            P(1, "Northwind API", 0, 90),
            new(2, SpaceKind.Event, "IMPREVISTO", "?"),
            P(3, "SOAP Bridge", 0, 90),
            C(4, "Region EU-West"),
            P(5, "Contoso Gateway", 0, 120),
            new(6, SpaceKind.Meeting, "ENDLESS MEETING", "☕"),
            P(7, "Contoso Cloud", 1, 150),
            new(8, SpaceKind.AiLab, "AI LAB", "✦"),
            P(9, "K8s Lift & Shift", 1, 150),
            C(10, "Region US-East"),
            P(11, "Serverless Rewrite", 1, 180),
            new(12, SpaceKind.Hackathon, "HACKATHON", "🏆"),
            P(13, "Fabrikam AI", 2, 210),
            new(14, SpaceKind.Event, "IMPREVISTO", "?"),
            P(15, "Data Lakehouse", 2, 210),
            C(16, "Edge CDN"),
            P(17, "Chatbot Interno", 2, 240),
            new(18, SpaceKind.Escalation, "ESCALATION", "⚠"),
            P(19, "Core Banking", 3, 270),
            new(20, SpaceKind.Tax, "TECH DEBT", "⛓"),
            P(21, "Trading Platform", 3, 270),
            C(22, "Disaster Recovery"),
            P(23, "Mainframe Exit", 3, 320),
        ];
    }

    /// <summary>Fee by level: base, 1-3 developers, architect.</summary>
    public static int[] RentTable(int price)
    {
        var b = Math.Max(5, (int)Math.Round(price / 10.0));
        static int R5(double v) => (int)(Math.Round(v / 5) * 5);
        return [b, R5(b * 5), R5(b * 15), R5(b * 38), R5(b * 52)];
    }

    public static readonly Role[] Roles =
    [
        new("developer", "Developer", "DEV", "+€25 extra a ogni Kick-off"),
        new("devops", "DevOps", "OPS", "+50% sulle fee dei Cloud"),
        new("analyst", "Business Analyst", "BA", "Progetti scontati del 10%"),
        new("designer", "UX Designer", "UX", "+50% sugli imprevisti positivi"),
        new("security", "Security Engineer", "SEC", "Tech Debt e patch dimezzati"),
        new("cto", "CTO", "CTO", "Team e architetti -30%"),
    ];

    public static Role RoleOf(string id) => Roles.FirstOrDefault(r => r.Id == id) ?? Roles[0];

    public static readonly Card[] Deck =
    [
        new(0, "☕", "SCOPE CREEP", "Il cliente ha aggiunto “una piccola modifica”. Paga €80.", CardEffect.Money, -80),
        new(1, "✓", "DEPLOY RIUSCITO", "Contro ogni previsione, funziona. Incassa €120.", CardEffect.Money, 120),
        new(2, "▣", "ENDLESS TEAMS MEETING", "Hai detto “facciamo un rapido allineamento”. Vai all'Endless Meeting.", CardEffect.GoToMeeting),
        new(3, "✦", "AI TOKEN", "Hai automatizzato i test di regressione. Ottieni 1 AI Token.", CardEffect.Token, 1),
        new(4, "⚠", "BUG IN PRODUZIONE", "Era un problema di configurazione. Paga €60.", CardEffect.Money, -60),
        new(5, "★", "CLIENTE ENTUSIASTA", "La demo è piaciuta perfino al legale. Bonus €150.", CardEffect.Money, 150),
        new(6, "↩", "HOTFIX DEL VENERDÌ", "Rollback! Torna indietro di 3 caselle.", CardEffect.MoveBy, -3),
        new(7, "▶", "NUOVO KICK-OFF", "Riorganizzazione: vai al Kick-off e incassa il bonus.", CardEffect.MoveTo, 0),
        new(8, "⌘", "CODE REVIEW COLLETTIVA", "Tutti imparano qualcosa: ogni consulente ti paga €20.", CardEffect.CollectFromEach, 20),
        new(9, "✈", "OFFSITE AZIENDALE", "Offri tu il team building: paga €25 a ogni consulente.", CardEffect.PayEach, 25),
        new(10, "🛡", "PATCH DI SICUREZZA", "CVE critica: paga €25 per developer e €100 per architetto.", CardEffect.Repairs, 25, 100),
        new(11, "📅", "CALENDARIO BLOCCATO", "Tieni questa carta: ti fa uscire dall'Endless Meeting.", CardEffect.MeetingPass),
        new(12, "☁", "MIGRAZIONE CLOUD", "Avanza fino al Cloud più vicino.", CardEffect.NearestCloud),
        new(13, "🏆", "CALL FOR HACKERS", "Vai all'Hackathon e prova a vincere il montepremi.", CardEffect.MoveTo, HackathonIndex),
        new(14, "€", "RIMBORSO SPESE", "La nota spese è stata approvata al primo colpo. Incassa €50.", CardEffect.Money, 50),
        new(15, "⌛", "LICENZE SCADUTE", "Nessuno aveva rinnovato le licenze. Paga €100.", CardEffect.Money, -100),
    ];

    public static readonly string[] PlayerColors = ["#f35e4c", "#22aaa1", "#f3b929", "#55a8cf", "#9b6cd1", "#46b878"];
}
