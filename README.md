# MarcoSoft Board Games

A multi-game board game site on Blazor Server (.NET 11). One host, with game modules plugged into it.

| Game | Route | Mode |
|------|-------|------|
| **Chess with Ralf** | `/chess` | against Ralf (an LLM on Azure AI Foundry), or a remote battle with an invite code |
| **Developy** | `/developy` | circular "software house" property game for 2–6 players: bots, hot-seat or remote |

The lobby is at `/`. There are no accounts: rooms live in server memory and expire after 6 idle hours.

- [USAGE.md](USAGE.md): installation and how to play
- [ARCHITECTURE.md](ARCHITECTURE.md): design
- [ROADMAP.md](ROADMAP.md): what comes next
- [OPENPOINTS.md](OPENPOINTS.md): known gaps

Licence: MIT, Copyright (c) 2026 [Marco Parenzan](https://www.marcoparenzan.it).

- Website: <https://www.marcoparenzan.it>
- LinkedIn: <https://www.linkedin.com/in/marcoparenzan/>
- Source: <https://github.com/marcoparenzan/boardgames>
