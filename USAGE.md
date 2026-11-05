# Usage

## Installation
Requirements: .NET 11 SDK (tested with 11.0.100-rc.3). Pages load Google Fonts from the internet; without a connection they fall back to system fonts.

Credentials live in a JSON file outside the repository; no path is hard-coded. Point to it with the `BOARDGAMES_SETTINGS` environment variable or `--settings <path>`. When a path is given, the file must exist (startup fails otherwise); without one, the app starts with no external settings. Ralf uses its `AzureAIFoundry` section (`Endpoint`, `ApiKey`, `ModelId`). Without it you can only play chess in remote battle. An optional `Ralf` section (`Endpoint` as a full chat-completions URL, `ApiKey`, `Model`) overrides it. Environment variables and CLI arguments override both, for example `$env:AzureAIFoundry__ModelId = '...'`. Credentials are never copied into the repository.

```powershell
[Environment]::SetEnvironmentVariable('BOARDGAMES_SETTINGS', '<path to your appsettings.json>', 'User')  # once; restart VS/terminal
dotnet build BoardGames.slnx
dotnet run --project src/BoardGames --launch-profile http   # http://localhost:5006
dotnet test BoardGames.slnx
```
Build output goes to `D:\Dev\BuildArtifacts\BoardGames` (set in `Directory.Build.props`), not inside the repo.

## Developy: how to play
1. Go to `/developy` and choose **Crea una partita**. Enter your name and pick a role. Each extra seat is one of:
   - **Bot**: played by the server.
   - **Remoto**: a colleague joins with the code.
   - **Locale**: another person at the same screen.

   Then pick the length: 12, 25 or 40 rounds.
2. For remote seats, share the 8-character code or use **Copia invito**. The game starts when every seat is taken. The host can press **Avvia ora** to turn empty seats into bots.
3. On your turn, roll the die (1–6), move around the 24-space ring, then act on the space you land on.

Rules:
- **Projects** (4 coloured groups of 3) and **Cloud** spaces can be bought. Other players pay you a consulting fee when they land on them.
- Owning a **complete group** doubles the base fee and lets you add **teams**: 1–3 developers, then an architect ◆. Teams must be built evenly across the group. Clouds pay €25/50/100/200 depending on how many you own.
- **Hostile takeover** 🦈: if you are standing on an opponent's project that has no teams and is not part of their complete group, you can buy it for twice its price, paid to the owner. Once per turn.
- **AI Tokens** ✦: you start with 1 (max 5) and gain 1 at the AI Lab. Spend one to pay half a fee or to leave the meeting.
- **Endless Meeting** (the jail): leave by rolling a 6, paying €50, spending a token or playing the *Calendario bloccato* card. After the third failed roll you pay and move.
- **Escalation** sends you to the meeting. **Tech Debt** costs €100. Fees, fines and meeting exits go into the **Hackathon** pot, won by whoever lands there.
- Passing **Kick-off** pays €200. Each later seat starts with €75 more than the one before, to offset the advantage of buying first.
- Short of cash? **Freeze** projects (you get half the price back but collect no fees) or sell teams (half their cost back). If that is not enough you go bankrupt and your assets go to the creditor.
- The game ends when only one player is left or the rounds run out. The highest valuation (cash + projects + teams) wins.

Role perks:

| Role | Perk |
|------|------|
| Developer | +€25 at every Kick-off |
| DevOps | +50% on cloud fees |
| Analyst | −10% on projects |
| UX | +50% on positive events |
| Security | Tech Debt and patches cost half |
| CTO | −30% on teams |

Click any space to see its fee table and management actions. **Vista dall'alto** switches the 3D board to a flat view. Refreshing the page keeps your seat (the identity is stored in the tab's session storage). A new tab or another browser counts as a new player.

## Chess
Select a piece, then a highlighted square. Against Ralf you play White; each of his moves is a call to the model (2–14 s in tests). While he thinks, a panel shows the stage (waiting for the model, illegal move and new attempt), the attempt number and the seconds elapsed, so you can tell he is working. Press **Interrompi** to stop him; after a stop or a failure, **Riprova Ralf** asks again. Each call times out after 60 s. In a remote battle, press **Remote battle** and share the code; the other player enters it and presses **Partecipa**. The game keeps a move log in FEN/UCI/SAN with PGN and JSON download. Snapshots are saved in `ops/data/games`; set `Games__LogDirectory` to change the folder.
