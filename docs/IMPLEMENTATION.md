# SignalR Implementation Journal — Step by Step

This is a living document. Each time you learn a new SignalR concept from [THEORY.md](THEORY.md), implement it here and record what you did. Steps marked ✅ are done in this repo; ⬜ steps are the roadmap from basic to expert.

**Project:** `src/SignalRLab.Api` — ASP.NET Core (.NET 10) Web API.

---

## Progress Overview

| # | Step | Status |
|---|------|--------|
| 1 | Register SignalR service + map a hub | ✅ Done |
| 2 | First hub method: broadcast a chat message | ⬜ |
| 3 | A client to talk to the hub (JS or .NET console) | ⬜ |
| 4 | Connection lifecycle: OnConnected / OnDisconnected | ⬜ |
| 5 | Targeting: Caller, Others, specific connections | ⬜ |
| 6 | Groups: chat rooms | ⬜ |
| 7 | Strongly typed hub (`Hub<IChatClient>`) | ⬜ |
| 8 | Push from outside the hub (`IHubContext` + BackgroundService) | ⬜ |
| 9 | Authentication: JWT + `[Authorize]` + `Clients.User` | ⬜ |
| 10 | Error handling: `HubException` + hub filters | ⬜ |
| 11 | Streaming: server→client and client→server | ⬜ |
| 12 | Client results: server awaits a client's answer | ⬜ |
| 13 | Reconnection: automatic + stateful reconnect | ⬜ |
| 14 | MessagePack protocol | ⬜ |
| 15 | Scale-out: Redis backplane | ⬜ |
| 16 | Testing: unit + integration tests for hubs | ⬜ |

---

## ✅ Step 1 — Register SignalR service + map a hub

**Theory:** [§2 What Is SignalR](THEORY.md#2-what-is-signalr), [§4 Hubs](THEORY.md#4-hubs--the-core-concept), [§14 CORS](THEORY.md#14-cors-and-signalr)

**What was done:**

1. Registered SignalR in DI (`Program.cs`):

   ```csharp
   builder.Services.AddSignalR();
   ```

2. Added a CORS policy named `dev` with explicit origins, `AllowAnyHeader`, `AllowAnyMethod`, and `AllowCredentials()` — required because SignalR's negotiate request sends credentials, which is incompatible with `AllowAnyOrigin()`.

3. Created an empty hub `Hubs/ChatHub.cs`:

   ```csharp
   public class ChatHub : Hub { }
   ```

4. Mapped it to an endpoint:

   ```csharp
   app.MapHub<ChatHub>("/hubs/chat");
   ```

**Verify it works:** run the API and POST to the negotiate endpoint:

```powershell
curl.exe -X POST https://localhost:7064/hubs/chat/negotiate?negotiateVersion=1 -k
```

You should get JSON containing a `connectionId` and `availableTransports` — proof the hub endpoint is alive.

**Key takeaways:**
- `AddSignalR()` = services, `MapHub<T>()` = endpoint. You need both.
- The hub is reachable even with zero methods — clients can connect, there's just nothing to call yet.

---

## ⬜ Step 2 — First hub method: broadcast a chat message

**Theory:** [§4 Hubs](THEORY.md#4-hubs--the-core-concept), [§7 Clients](THEORY.md#7-clients--targeting-who-receives-a-message)

**Goal:** add `SendMessage(string user, string message)` to `ChatHub` that broadcasts to all clients with `Clients.All.SendAsync("ReceiveMessage", user, message)`.

**Done when:** two connected clients both receive a message sent by either one.

<!-- Record what you did here when complete -->

---

## ⬜ Step 3 — A client to talk to the hub

**Theory:** [§12 Clients](THEORY.md#12-clients-javascript-net-and-others)

**Goal:** create a simple client — either a static HTML page using `@microsoft/signalr` from a CDN, or a .NET console app using `Microsoft.AspNetCore.SignalR.Client`. Connect, register `on("ReceiveMessage")` *before* `start()`, send messages.

**Done when:** you can chat between two browser tabs / console windows.

---

## ⬜ Step 4 — Connection lifecycle

**Theory:** [§6 Connections & Lifetime](THEORY.md#6-connections-connectionid-and-lifetime)

**Goal:** override `OnConnectedAsync` / `OnDisconnectedAsync` to announce joins/leaves to other clients. Log `Context.ConnectionId`. Open multiple tabs and watch each get its own ConnectionId.

**Done when:** clients see "a user joined/left" messages, and you've observed that refreshing a tab produces a *new* ConnectionId.

---

## ⬜ Step 5 — Targeting: Caller, Others, specific connections

**Theory:** [§7 Clients](THEORY.md#7-clients--targeting-who-receives-a-message)

**Goal:** add methods demonstrating `Clients.Caller` (echo/private ack), `Clients.Others` (broadcast minus me), and `Clients.Client(connectionId)` (direct message by connection).

**Done when:** you can DM one specific tab from another.

---

## ⬜ Step 6 — Groups: chat rooms

**Theory:** [§8 Groups](THEORY.md#8-groups)

**Goal:** `JoinRoom(string room)` / `LeaveRoom(string room)` using `Groups.AddToGroupAsync` / `RemoveFromGroupAsync`; messages go to `Clients.Group(room)`. Notice there's no built-in member list — keep a simple in-memory dictionary if you want one.

**Done when:** messages in room A are invisible to clients in room B.

---

## ⬜ Step 7 — Strongly typed hub

**Theory:** [§10 Strongly Typed Hubs](THEORY.md#10-strongly-typed-hubs)

**Goal:** define `IChatClient` with `ReceiveMessage`, `UserJoined`, etc., and convert `ChatHub : Hub` → `ChatHub : Hub<IChatClient>`. All `SendAsync` magic strings disappear.

**Done when:** the project compiles with zero `SendAsync` calls in the hub and everything still works.

---

## ⬜ Step 8 — Push from outside the hub

**Theory:** [§11 IHubContext](THEORY.md#11-sending-from-outside-a-hub-ihubcontext)

**Goal:** inject `IHubContext<ChatHub, IChatClient>` into (a) a minimal API endpoint that broadcasts a "server announcement", and (b) a `BackgroundService` that pushes the server time every 10 seconds.

**Done when:** connected clients receive messages no client ever sent.

---

## ⬜ Step 9 — Authentication

**Theory:** [§13 Auth](THEORY.md#13-authentication--authorization), [§9 Users vs Connections](THEORY.md#9-users-vs-connections)

**Goal:** add JWT bearer auth, put `[Authorize]` on the hub, read the token from the `access_token` query string in `OnMessageReceived`, send the token from the client via `accessTokenFactory`. Then use `Clients.User(userId)` to message a *person* across all their tabs.

**Done when:** unauthenticated connections are rejected (401 at negotiate), and a message to one user lands in all of that user's tabs but nobody else's.

---

## ⬜ Step 10 — Error handling

**Theory:** [§17 Error Handling](THEORY.md#17-error-handling)

**Goal:** throw a plain exception in a hub method and observe the client gets a generic error; switch to `HubException` and see the real message arrive. Add an `IHubFilter` that logs every invocation.

**Done when:** you can explain why `EnableDetailedErrors` must stay off in production.

---

## ⬜ Step 11 — Streaming

**Theory:** [§15 Streaming](THEORY.md#15-streaming)

**Goal:** server→client stream returning `IAsyncEnumerable<T>` (e.g., a counter or fake stock ticks), consumed with `connection.stream(...)`. Then a client→server stream (upload values via `signalR.Subject`). Test cancellation mid-stream.

**Done when:** the client renders items as they arrive and can cancel cleanly.

---

## ⬜ Step 12 — Client results

**Theory:** [§16 Client Results](THEORY.md#16-client-results-server-calls-client-and-waits)

**Goal:** server asks one client a question with `Clients.Client(id).InvokeAsync<string>(...)` and awaits the answer. Add a timeout via `CancellationToken`.

**Done when:** the server-side code receives the value the client returned.

---

## ⬜ Step 13 — Reconnection

**Theory:** [§18 Reconnection](THEORY.md#18-reconnection-strategies)

**Goal:** add `.withAutomaticReconnect()` and handlers for `onreconnecting`/`onreconnected`/`onclose`; rejoin rooms in `onreconnected`. Then try .NET 8+ stateful reconnect (`AllowStatefulReconnects` + `.withStatefulReconnect()`). Test by stopping/starting the server while clients are connected.

**Done when:** a client survives a server restart and ends up back in its room.

---

## ⬜ Step 14 — MessagePack

**Theory:** [§20 MessagePack](THEORY.md#20-messagepack-protocol)

**Goal:** add `Microsoft.AspNetCore.SignalR.Protocols.MessagePack` server-side and the msgpack protocol client-side; inspect frames in browser dev tools (now binary).

**Done when:** chat still works and you've compared payload sizes vs JSON.

---

## ⬜ Step 15 — Scale-out: Redis backplane

**Theory:** [§19 Scaling Out](THEORY.md#19-scaling-out-redis-backplane--azure-signalr)

**Goal:** run Redis (Docker), add `.AddStackExchangeRedis(...)`, launch the API on two different ports, connect one client to each instance, and confirm `Clients.All` reaches both. Then remove the backplane and watch it break.

**Done when:** cross-instance broadcast works, and you've seen first-hand why a single server can't do it alone.

---

## ⬜ Step 16 — Testing

**Theory:** [§23 Testing](THEORY.md#23-testing-signalr)

**Goal:** unit-test `ChatHub` with a mocked `IChatClient`/`HubCallerContext`; integration-test with `WebApplicationFactory<Program>` and a real `HubConnection`.

**Done when:** `dotnet test` proves SendMessage broadcasts and JoinRoom isolates rooms.

---

## How to update this document

When you finish a step:

1. Change ⬜ to ✅ in the progress table and the step heading.
2. Under the step, replace the goal text with a **"What was done"** section: the actual code you added, commands you ran, and anything that surprised you.
3. Commit with a message like `Step 6: groups — chat rooms`.
