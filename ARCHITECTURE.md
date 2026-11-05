# Architecture

```text
BoardGames.slnx
src/BoardGames.Core      Razor class library, shared kit: GameDescriptor, RoomBase, RoomStore<T>, RoomCodes, ChatLine,
                         SiteHeader component, boardgames.css (tokens, buttons, panels)
src/BoardGames.Chess     game module (Razor class library)
src/BoardGames.Developy  game module (Razor class library)
src/BoardGames           Blazor Server host: lobby, layout, error pages; knows modules only through their descriptors
src/BoardGames.Tests     xUnit + bUnit
art/developy             original PNG artwork (not served); the module's wwwroot holds the optimized WebP copies
```

## Module structure
Both game modules follow the same layout:

```text
src/BoardGames.<Game>/
  <Game>Module.cs   Add<Game>() registers the services and the module's GameDescriptor
  Engine/           rules, no UI (Chess: ChessGame over Gera.Chess; Developy: Catalog, DevelopyGame, BotBrain)
  Services/         rooms and players (Chess: ChessRooms, RalfAgent; Developy: DevelopyRooms)
  Components/       Razor UI; Pages/ holds the routed pages (/chess; /developy, /developy/{Code})
  wwwroot/          <game>.css scoped under a root class (.chess, .dv) and assets/
  _Imports.razor
```

Namespaces follow the folders (`BoardGames.Chess.Engine`, `BoardGames.Developy.Services`, …). Static files are served under `_content/BoardGames.<Game>/`.

## Module contract
A module depends only on `BoardGames.Core`. It registers its services and one `GameDescriptor` with its title, route, cover image, stylesheet and assembly. The host calls `AddChess()` and `AddDevelopy()` and reads every other detail from the descriptors:

- the lobby and `SiteHeader` list them;
- `App.razor` links each stylesheet after `boardgames.css`;
- `Routes.razor` and `MapRazorComponents().AddAdditionalAssemblies(...)` take their assemblies.

Adding a game means adding a module and one `Add<Game>()` line in `Program.cs`.

## Rooms
`RoomBase` gives each room a lock (`Sync`), a `Changed` event and a last-activity time. `Notify()` runs outside the lock and catches errors from each subscriber, so a dead circuit cannot block the others.

`RoomStore<T>` creates rooms with 8-character codes from an unambiguous alphabet (about 40 bits) and retries on collision. It caps the store at 500 rooms and, every 5 minutes, removes rooms idle for 6 hours.

## Developy
- `Engine/Catalog.cs`: static content: 24 spaces, groups, roles, 16 event cards and economy constants. Fee tables are derived from each price.
- `Engine/DevelopyGame.cs`: the authoritative rules, written as a state machine over `Phase` (Roll, Buy, Card, Fee, Debt, End, Over). `Options(seat)` lists the legal `Act`s. `Apply(seat, act, expectedVersion)` validates and applies one; an illegal or stale action throws and leaves the state unchanged. The random generator is injected, so tests can replay games.
- `Engine/BotBrain.cs`: heuristic bot. It keeps a cash reserve that grows each round, completes or blocks groups, builds evenly and sells assets before going bankrupt.
- `Services/DevelopyRooms.cs`: lobby seats (human, bot or open; hot-seat seats share the host token), automatic start and chat. The server plays bot turns one action at a time with a pause, so every client sees them. The UI only reads immutable `TableView` snapshots taken under the lock.
- UI: `Wheel.razor` draws one sector at the bottom of the SVG and rotates copies into place. CSS 3D tilts the plate and spins it so the active player is in front. Tokens are upright HTML elements that counter-rotate with the same transition, so they always face the viewer. Each client animates token moves from the server positions, at 170 ms per space.
- Identity: a random token per tab, kept in `ProtectedSessionStorage` under `developy:{code}`. It survives page refreshes and circuit reconnects.

Balance was tuned with seeded bot-only simulations (300 games per player count). Each role wins within a few points of its fair share, and in 4-player games each seat wins about 25%.

## Chess
`ChessGame` adapts Gera.Chess: FEN for state, UCI for actions, SAN/PGN for display. `GameRooms` uses the shared `RoomStore`.

**Gera.Chess and Blazor:** the library captures `SynchronizationContext.Current` when a board is created and raises its events (such as the capture event) with a blocking `Send`. Under Blazor that context is the circuit renderer. Moves are applied on that same context, so the first capture of a game used to deadlock the circuit, leaving the page stuck on "Ralf sta pensando…". `ChessGame` therefore creates the board and calls `Move` with no synchronization context (`Detached`). The test `CapturesNeverCallBackIntoTheCallersSynchronizationContext` guards this.

`RalfAgent` plays **only through an LLM**; there is no local engine. It calls the OpenAI-compatible Chat Completions API of the Azure AI Foundry resource: the project endpoint `https://<resource>.services.ai.azure.com/api/projects/<p>` maps to `https://<resource>.services.ai.azure.com/openai/v1/chat/completions` with an `api-key` header. The prompt carries the FEN, the SAN history and the legal UCI moves, and asks for JSON `{text, move}`. A reply that is not JSON or not a legal move is sent back to the model with the reason, up to 3 attempts. After that, or if the service fails, `RalfUnavailableException` is raised: the page shows the error and a **Riprova Ralf** button, and the game state is untouched. The HTTP call runs off the circuit context, with a 60 s timeout.

## Performance choices
- No CSS framework or runtime compiler (the Tailwind CDN is gone). Two plain stylesheets of about 9 KB and 22 KB, fingerprinted and precompressed by `MapStaticAssets`.
- Artwork converted to WebP and resized: 8.2 MB of PNG became about 0.4 MB. The board image comes in 900 and 1600 px versions via `<picture>`; the characters are cropped to 420 px.
- Unused Bootstrap (about 6 MB) removed from the module.
- Board rotation and token movement are CSS transitions run by the GPU; the server sends only small diffs.
