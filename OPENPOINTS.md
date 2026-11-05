# Open points

- **State lives in memory.** Restarting the server loses every room. Developy games are not saved to disk; chess saves snapshots in `ops/data/games`.
- **Identity is per browser tab.** Closing the tab loses the seat. Opening the invite in another tab of the same browser creates a new identity, which is also what makes two-tab testing on one machine possible.
- **Codes are not authentication.** Anyone with a code can watch the game, and can take a free seat before it starts.
- **No trades or auctions in Developy.** Groups can only be completed by landing on the spaces or by hostile takeovers.
- **Balance is tested only between bots.** Human players may find stronger strategies. In 6-player games the first seats still win somewhat more often.
- **Labels on the far side of the 3D board appear upside down**, as on a real board seen from one side. The flat view and the space card are the readable alternatives.
- **The settings file location** comes from `BOARDGAMES_SETTINGS` or `--settings`. Without it, Ralf is not configured and chess works only in remote battle.
- **`Directory.Build.props` keeps machine build paths** (`D:\Dev\BuildArtifacts\BoardGames`, `D:\Dev\NuGetLocalFeed`). They are build conventions, not app code.
- **UI checks were manual.** I took headless Edge screenshots at 1440 px (desktop) and 390 px (mobile), using two separate browsers for the remote flow. The repo has no automated browser tests. Screen readers and keyboard-only play were not checked.
- **Ralf depends on the model service.** There is no offline opponent. Each move costs a request (30 RPM limit in the config) and takes several seconds. Play strength depends on the model, and the chat has no memory beyond the position and move list sent each time.
- **Fonts come from Google Fonts**, which is an external request.
