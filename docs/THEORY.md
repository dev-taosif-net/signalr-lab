# SignalR Theory — A to Z

A complete reference for understanding ASP.NET Core SignalR, from the absolute basics to advanced production topics. Read this top to bottom once, then come back to specific sections as you implement each step in [IMPLEMENTATION.md](IMPLEMENTATION.md).

---

## Table of Contents

1. [What Problem Does SignalR Solve?](#1-what-problem-does-signalr-solve)
2. [What Is SignalR?](#2-what-is-signalr)
3. [Transports](#3-transports)
4. [Hubs — The Core Concept](#4-hubs--the-core-concept)
5. [The Hub Protocol](#5-the-hub-protocol)
6. [Connections, ConnectionId, and Lifetime](#6-connections-connectionid-and-lifetime)
7. [Clients — Targeting Who Receives a Message](#7-clients--targeting-who-receives-a-message)
8. [Groups](#8-groups)
9. [Users vs Connections](#9-users-vs-connections)
10. [Strongly Typed Hubs](#10-strongly-typed-hubs)
11. [Sending From Outside a Hub (IHubContext)](#11-sending-from-outside-a-hub-ihubcontext)
12. [Clients (JavaScript, .NET, and others)](#12-clients-javascript-net-and-others)
13. [Authentication & Authorization](#13-authentication--authorization)
14. [CORS and SignalR](#14-cors-and-signalr)
15. [Streaming](#15-streaming)
16. [Client Results (Server Calls Client and Waits)](#16-client-results-server-calls-client-and-waits)
17. [Error Handling](#17-error-handling)
18. [Reconnection Strategies](#18-reconnection-strategies)
19. [Scaling Out (Redis Backplane / Azure SignalR)](#19-scaling-out-redis-backplane--azure-signalr)
20. [MessagePack Protocol](#20-messagepack-protocol)
21. [Configuration & Tuning](#21-configuration--tuning)
22. [Performance & Limits](#22-performance--limits)
23. [Testing SignalR](#23-testing-signalr)
24. [Security Checklist](#24-security-checklist)
25. [Common Pitfalls](#25-common-pitfalls)
26. [Glossary](#26-glossary)

---

## 1. What Problem Does SignalR Solve?

HTTP is **request/response**: the client asks, the server answers. The server cannot start a conversation. That's a problem for:

- Chat applications
- Live dashboards (stock tickers, IoT telemetry, monitoring)
- Notifications ("someone liked your post")
- Collaborative editing (Google Docs style)
- Live games, auctions, location tracking

Before push technology, apps **polled**: the client asked "anything new?" every few seconds. Polling wastes bandwidth, adds latency, and hammers the server.

SignalR gives you **real-time, bidirectional communication**: the server can push to clients at any moment, and clients can call the server — all over a single persistent connection.

## 2. What Is SignalR?

ASP.NET Core SignalR is a library that abstracts real-time communication. Key ideas:

- You write **Hubs** (server-side classes) and **clients** call methods on them.
- The server calls **methods on clients** by name, with arguments.
- SignalR handles the hard parts: connection management, transport negotiation, serialization, reconnection, scale-out.

It is an **RPC (Remote Procedure Call)** framework on top of a persistent connection. You think in method calls, not raw messages.

## 3. Transports

SignalR picks the best available transport automatically, in this order:

| Transport | How it works | When it's used |
|---|---|---|
| **WebSockets** | True full-duplex TCP-based protocol (`ws://`/`wss://`). One connection, both directions. | Default — virtually always available on modern servers/browsers. |
| **Server-Sent Events (SSE)** | Server pushes over a long-lived HTTP response; client-to-server goes via separate HTTP requests. | Fallback when WebSockets are blocked. Not supported by old IE. |
| **Long Polling** | Client sends a request that the server holds open until it has data (or times out), then the client immediately re-requests. | Last resort. Works everywhere, highest overhead. |

**Negotiation:** before connecting, the client POSTs to `/yourhub/negotiate`. The server replies with supported transports and a connection token. You can skip negotiation (`skipNegotiation: true`) only when forcing WebSockets.

**Takeaway:** you almost never think about transports. Write code against the Hub API; SignalR deals with the wire.

## 4. Hubs — The Core Concept

A **Hub** is a server-side class deriving from `Microsoft.AspNetCore.SignalR.Hub`. It is the API surface your clients talk to.

```csharp
public class ChatHub : Hub
{
    // Clients call this: connection.invoke("SendMessage", user, message)
    public async Task SendMessage(string user, string message)
    {
        // Server calls "ReceiveMessage" on every connected client
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}
```

Crucial facts about hubs:

- **Hubs are transient.** A new hub instance is created for *every* hub method invocation, then disposed. Never store state in hub instance fields — use static/singleton services, groups, or a database.
- **Don't block.** Hub methods should be `async`; blocking calls tie up the connection.
- Inside a hub you have access to:
  - `Clients` — send messages to clients (see §7)
  - `Groups` — add/remove connections from groups (see §8)
  - `Context` — info about the *current* connection: `ConnectionId`, `User`, `UserIdentifier`, `Items` (per-connection key/value bag), `ConnectionAborted` token
- Lifecycle hooks you can override:
  - `OnConnectedAsync()` — runs when a client connects
  - `OnDisconnectedAsync(Exception?)` — runs when a client disconnects (exception is non-null for abnormal disconnects)
- Hubs support **dependency injection** via constructor parameters, like controllers.

You register and expose a hub in `Program.cs`:

```csharp
builder.Services.AddSignalR();   // register services
app.MapHub<ChatHub>("/hubs/chat"); // expose endpoint
```

## 5. The Hub Protocol

On the wire, SignalR speaks a **hub protocol** — a framing format for invocations, results, and streams. Two built-in protocols:

- **JSON** (default) — human-readable, uses `System.Text.Json` (camelCases property names by default).
- **MessagePack** — compact binary, faster, smaller payloads (see §20).

Message types you'll see if you sniff traffic: `Invocation`, `StreamItem`, `Completion`, `Ping` (keepalive), `Close`. You never construct these by hand, but knowing they exist helps when debugging in browser dev tools (Network tab → WS → Messages).

## 6. Connections, ConnectionId, and Lifetime

- Every client connection gets a unique **`ConnectionId`** (a string). It identifies *that connection*, not the user.
- One user with three browser tabs = **three connections**, three ConnectionIds.
- A ConnectionId changes on every reconnect. **Never persist it as a user identity.**
- Lifetime timeline:
  1. Client negotiates → connects → `OnConnectedAsync` fires.
  2. Server and client exchange keepalive pings (`KeepAliveInterval`, default 15s).
  3. If the server hears nothing for `ClientTimeoutInterval` (default 30s), it considers the client gone → `OnDisconnectedAsync` fires.
  4. Graceful disconnect (client calls `stop()`) also fires `OnDisconnectedAsync` with `null` exception.

`Context.Items` is a dictionary scoped to the connection — handy for caching per-connection data between hub method calls (it survives across invocations, unlike hub fields).

## 7. Clients — Targeting Who Receives a Message

Inside a hub (or via `IHubContext`), `Clients` lets you choose recipients:

| Target | Sends to |
|---|---|
| `Clients.All` | Every connected client |
| `Clients.Caller` | Only the client that invoked the method |
| `Clients.Others` | Everyone except the caller |
| `Clients.Client(connectionId)` | One specific connection |
| `Clients.Clients(ids)` | Several specific connections |
| `Clients.Group("name")` | All connections in a group |
| `Clients.Groups(names)` | Multiple groups |
| `Clients.OthersInGroup("name")` | Group members except the caller |
| `Clients.GroupExcept("name", ids)` | Group minus specific connections |
| `Clients.User(userId)` | All connections of a *user* (see §9) |
| `Clients.Users(userIds)` | Multiple users |

Then call `.SendAsync("MethodName", arg1, arg2, ...)` — the string is the client-side handler name.

**Important:** `SendAsync` is fire-and-forget; it does *not* wait for the client to handle the message, and it does not fail if no one is listening.

## 8. Groups

A **group** is a named collection of connections, managed by the server:

```csharp
await Groups.AddToGroupAsync(Context.ConnectionId, "room-42");
await Groups.RemoveFromGroupAsync(Context.ConnectionId, "room-42");
await Clients.Group("room-42").SendAsync("ReceiveMessage", user, msg);
```

Facts:

- Groups are created on first add and vanish when empty — there is no "create group" API and **no API to list group members**. If you need membership lists, track them yourself (dictionary, Redis, DB).
- A connection can be in many groups.
- Group membership is **not preserved across reconnects** — a reconnected client has a new ConnectionId and must rejoin (do it in `OnConnectedAsync` or via a client call).
- Perfect for: chat rooms, tenant isolation, topic subscriptions, per-document collaboration channels.

## 9. Users vs Connections

SignalR has a first-class notion of a **user**, distinct from a connection:

- The **user identifier** defaults to `ClaimTypes.NameIdentifier` from the authenticated `ClaimsPrincipal`.
- `Clients.User("alice-id").SendAsync(...)` reaches **all** of Alice's connections (every tab, every device) without you tracking ConnectionIds.
- Customize the mapping by implementing `IUserIdProvider` and registering it as a singleton.

Rule of thumb: use **users** for "notify this person", **groups** for "notify this room/topic", **connections** only for connection-specific plumbing.

## 10. Strongly Typed Hubs

Magic strings in `SendAsync("ReceiveMessage", ...)` break silently when renamed. Strongly typed hubs fix the server side:

```csharp
public interface IChatClient
{
    Task ReceiveMessage(string user, string message);
}

public class ChatHub : Hub<IChatClient>
{
    public async Task SendMessage(string user, string message)
        => await Clients.All.ReceiveMessage(user, message); // compile-time checked
}
```

With `Hub<T>` you lose `SendAsync` entirely — every client call goes through the interface. The client-side handler name is the interface method name (`ReceiveMessage`). This is the recommended style for anything beyond a demo.

## 11. Sending From Outside a Hub (IHubContext)

Most real-time pushes don't originate from a client call — they come from a controller, background service, or message handler. Inject `IHubContext<THub>`:

```csharp
public class OrdersController(IHubContext<ChatHub, IChatClient> hub) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(Order order)
    {
        // ... save order ...
        await hub.Clients.Group("admins").ReceiveMessage("system", $"New order {order.Id}");
        return Ok();
    }
}
```

Notes:

- `IHubContext` has `Clients` and `Groups` but **no** `Context` or `Caller` — there's no "current caller" outside a hub.
- Works from hosted services (`BackgroundService`), making it the backbone of dashboards and notification systems.

## 12. Clients (JavaScript, .NET, and others)

Official client libraries: **JavaScript/TypeScript** (`@microsoft/signalr`), **.NET** (`Microsoft.AspNetCore.SignalR.Client`), **Java**. Community: Python, Swift, and more.

### JavaScript client essentials

```js
const connection = new signalR.HubConnectionBuilder()
    .withUrl("https://localhost:7064/hubs/chat")
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Information)
    .build();

// Register handlers BEFORE starting (messages arriving before .on() are dropped)
connection.on("ReceiveMessage", (user, message) => { /* update UI */ });

await connection.start();
await connection.invoke("SendMessage", "alice", "hello"); // waits for completion
// connection.send(...) — fire-and-forget variant
```

### .NET client essentials

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("https://localhost:7064/hubs/chat")
    .WithAutomaticReconnect()
    .Build();

connection.On<string, string>("ReceiveMessage", (user, msg) => Console.WriteLine($"{user}: {msg}"));
await connection.StartAsync();
await connection.InvokeAsync("SendMessage", "bot", "hi");
```

`invoke` vs `send`: `invoke` returns a promise/task that completes when the server method finishes (and carries its return value or exception); `send` returns as soon as the message is sent.

## 13. Authentication & Authorization

SignalR rides on ASP.NET Core auth — same middleware, same `[Authorize]`:

```csharp
[Authorize]
public class ChatHub : Hub { }

// or per-method
[Authorize(Roles = "admin")]
public Task DeleteRoom(string room) => ...;
```

The **JWT gotcha**: browsers can't set headers on WebSocket requests, so the JS client sends the bearer token as a query string (`?access_token=...`). The server must be taught to read it:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = ctx =>
    {
        var token = ctx.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            ctx.Token = token;
        return Task.CompletedTask;
    }
};
```

Client side: `.withUrl(url, { accessTokenFactory: () => getToken() })`.

Cookie auth works transparently (cookies flow on the WebSocket handshake) but requires `AllowCredentials()` + explicit origins in CORS.

Inside the hub: `Context.User` is the `ClaimsPrincipal`, `Context.UserIdentifier` is the mapped user id (§9).

## 14. CORS and SignalR

Needed only when the client page is served from a **different origin** than the hub. SignalR with credentials/cookies requires:

```csharp
policy.WithOrigins("http://localhost:5221")  // explicit — AllowAnyOrigin() is incompatible
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials();                    // required for SignalR's negotiate w/ cookies
```

Order matters: `app.UseCors(...)` must run **before** `app.MapHub<...>()` takes effect (in minimal hosting, place `UseCors` before the `Map*` calls, after `UseRouting` if you call it explicitly). The negotiate request is a plain HTTP POST — if CORS is wrong you'll see negotiate fail in the browser console before any WebSocket attempt.

## 15. Streaming

For data that arrives over time (progress updates, large result sets, live feeds):

**Server → client** — return `IAsyncEnumerable<T>` (or `ChannelReader<T>`):

```csharp
public async IAsyncEnumerable<int> Counter(int count, int delayMs,
    [EnumeratorCancellation] CancellationToken ct)
{
    for (var i = 0; i < count; i++)
    {
        ct.ThrowIfCancellationRequested();
        yield return i;
        await Task.Delay(delayMs, ct);
    }
}
```

JS client: `connection.stream("Counter", 10, 500).subscribe({ next, complete, error })`.

**Client → server** — hub method accepts `IAsyncEnumerable<T>`/`ChannelReader<T>`; the JS client passes a `signalR.Subject`.

Streaming respects backpressure and is cancellable — far better than firing hundreds of `SendAsync` calls in a loop.

## 16. Client Results (Server Calls Client and Waits)

Since .NET 7, the server can invoke a method on **one specific client** and await its return value:

```csharp
// Server
var answer = await Clients.Client(connectionId)
    .InvokeAsync<string>("GetUserConfirmation", "Proceed?", cancellationToken);
```

```js
// JS client must return a value from the handler
connection.on("GetUserConfirmation", (prompt) => window.confirm(prompt));
```

Constraints: only works on a *single* client target (not `All`/`Group`), and requires the client to actually respond — always pass a cancellation token or timeout.

## 17. Error Handling

- An unhandled exception in a hub method sends a generic *"An unexpected error occurred"* to the caller — details are hidden by design (they may leak internals).
- To send real error messages, throw **`HubException`** — its message *is* delivered to the client:

```csharp
throw new HubException("Room name already taken.");
```

- During development, reveal all exception details with:

```csharp
builder.Services.AddSignalR(o => o.EnableDetailedErrors = true); // dev only!
```

- On the client, `invoke` rejects/throws when the server method throws — wrap in try/catch.
- For cross-cutting concerns (logging, validation, exception mapping), use **Hub filters** (`IHubFilter`) — middleware for hub method invocations.

## 18. Reconnection Strategies

Connections drop — networks blip, servers recycle. The client must handle it:

- `.withAutomaticReconnect()` — retries after 0s, 2s, 10s, 30s, then **gives up** (fires `onclose`).
- `.withAutomaticReconnect([0, 2000, 5000, 10000, null])` — custom schedule.
- Events: `onreconnecting` (show "reconnecting…" UI, queue outgoing messages), `onreconnected` (rejoin groups! new ConnectionId!), `onclose` (offer manual retry / reload).
- **Stateful reconnect** (.NET 8+): `.MapHub<ChatHub>(url, o => o.AllowStatefulReconnects = true)` + `.withStatefulReconnect()` on the client buffers messages during brief drops so nothing is lost and the ConnectionId is preserved. Great for flaky mobile networks.
- Remember §8: without stateful reconnect, group membership is lost on reconnect — rejoin in `onreconnected` or `OnConnectedAsync`.

## 19. Scaling Out (Redis Backplane / Azure SignalR)

One server is fine until you run two. Then: client A is connected to server 1, client B to server 2 — `Clients.All` on server 1 never reaches B. Each server only knows its own connections.

Solutions:

1. **Redis backplane** — every server publishes messages to Redis pub/sub; all servers forward to their local connections.

   ```csharp
   builder.Services.AddSignalR().AddStackExchangeRedis("redis-connection-string");
   ```

   You still need **sticky sessions** at the load balancer (unless every client uses pure WebSockets), because non-WebSocket transports make multiple HTTP requests that must hit the same server.

2. **Azure SignalR Service** — a managed proxy that owns all client connections; your servers only talk to the service. No sticky sessions needed, massive scale, but it's an Azure dependency.

   ```csharp
   builder.Services.AddSignalR().AddAzureSignalR("connection-string");
   ```

3. **SQL Server backplane** — exists via community packages; Redis is the standard choice.

## 20. MessagePack Protocol

Binary serialization protocol — smaller messages, faster encode/decode. Enable on both sides:

```csharp
// Server: dotnet add package Microsoft.AspNetCore.SignalR.Protocols.MessagePack
builder.Services.AddSignalR().AddMessagePackProtocol();
```

```js
// JS: npm i @microsoft/signalr-protocol-msgpack
.withHubProtocol(new signalR.protocols.msgpack.MessagePackHubProtocol())
```

Caveats: case-sensitive property names, `DateTime` handling differs (always send UTC), payloads aren't human-readable in dev tools. Use when message volume/size matters (telemetry, games); JSON is fine for chats.

## 21. Configuration & Tuning

Key knobs (server, in `AddSignalR(options => ...)`):

| Option | Default | Meaning |
|---|---|---|
| `KeepAliveInterval` | 15 s | Ping frequency server→client |
| `ClientTimeoutInterval` | 30 s | No message from client for this long → disconnect. Keep ≥ 2× client's keepalive. |
| `HandshakeTimeout` | 15 s | Max time for the initial handshake |
| `MaximumReceiveMessageSize` | 32 KB | Max single incoming message. Raise deliberately, not to `null` (= unlimited = DoS risk). |
| `EnableDetailedErrors` | false | Exception details to clients — **dev only** |
| `MaximumParallelInvocationsPerClient` | 1 | Hub methods per client running concurrently |
| `StreamBufferCapacity` | 10 | Buffered items for client→server streams |

Client mirrors: `serverTimeoutInMilliseconds` (default 30 s — keep ≥ 2× server `KeepAliveInterval`), `keepAliveIntervalInMilliseconds` (15 s).

## 22. Performance & Limits

- **TCP connections**: each SignalR client holds one open connection. Windows/Linux servers handle tens of thousands, but watch file descriptor / ephemeral port limits and memory (~a few KB per idle connection).
- **Send loops**: avoid `foreach (id in ids) Clients.Client(id).Send(...)` — use `Clients.Clients(ids)` or groups.
- **Big payloads**: SignalR is a messaging system, not a file transfer protocol. Send a URL/ID and let the client fetch large blobs over HTTP.
- **Hot path allocation**: strongly typed hubs + MessagePack reduce overhead.
- **Don't await client work**: `SendAsync` completes when the message is *buffered*, not handled — design accordingly.

## 23. Testing SignalR

- **Unit-test hubs** by instantiating them with mocked `IHubCallerClients`, `HubCallerContext`, `IGroupManager` (set via the public `Clients`/`Context`/`Groups` properties). Strongly typed hubs make mocking easy: mock `IChatClient`.
- **Integration-test** with `WebApplicationFactory<Program>` + a real `HubConnection` pointed at `factory.Server.CreateHandler()`:

  ```csharp
  var connection = new HubConnectionBuilder()
      .WithUrl("http://localhost/hubs/chat",
          o => o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
      .Build();
  ```

- Test reconnection/disconnection logic by killing connections deliberately.

## 24. Security Checklist

- [ ] `[Authorize]` on hubs that need it — an unprotected hub is a public API.
- [ ] Validate **all** hub method parameters — they're client input, exactly like controller actions.
- [ ] Never trust client-supplied identity (user names, ids in args) — use `Context.UserIdentifier`.
- [ ] Authorize group joins — clients ask to join groups; the server must check they're allowed (group names are not secrets).
- [ ] Explicit CORS origins; never `AllowAnyOrigin` with credentials.
- [ ] `EnableDetailedErrors = false` in production.
- [ ] Cap `MaximumReceiveMessageSize`.
- [ ] Treat `access_token` in query strings carefully: it can land in server logs — keep tokens short-lived.

## 25. Common Pitfalls

1. **Storing state in hub fields** — hubs are per-invocation; state evaporates. Use `Context.Items`, singletons, or a store.
2. **Persisting ConnectionId as identity** — it changes on every reconnect.
3. **Registering `connection.on(...)` after `start()`** — early messages are silently dropped.
4. **Forgetting to rejoin groups after reconnect.**
5. **CORS missing `AllowCredentials()`** — negotiate fails with a cryptic browser error.
6. **JWT not read from query string** — authenticated REST works, hub returns 401.
7. **Expecting `Clients.All` to work across servers without a backplane.**
8. **Method name mismatch** — `SendAsync("receiveMessage")` vs `on("ReceiveMessage")`: client handler names are case-insensitive in the JS client, but server *hub method* names invoked by the client must match. Strongly typed hubs prevent half of these.
9. **Blocking calls (`.Result`, `.Wait()`) in hub methods** — thread starvation under load.
10. **Mixing up `invoke` and `send`** then wondering why errors are swallowed (`send` never reports server exceptions).

## 26. Glossary

| Term | Meaning |
|---|---|
| **Hub** | Server class exposing RPC methods to clients |
| **Transport** | Underlying connection mechanism (WebSockets/SSE/Long Polling) |
| **Negotiate** | Pre-connection HTTP handshake choosing transport |
| **ConnectionId** | Unique id of one live connection |
| **UserIdentifier** | Stable id of an authenticated user across connections |
| **Group** | Server-managed named set of connections |
| **Backplane** | Shared message bus (e.g., Redis) syncing multiple servers |
| **Hub protocol** | Wire format (JSON / MessagePack) |
| **Hub filter** | Middleware around hub method invocations |
| **Stateful reconnect** | .NET 8+ feature buffering messages across brief disconnects |
| **Client result** | Server→client invocation that awaits a return value |
| **Sticky sessions** | LB affinity keeping a client on the same server |

---

*Next: put it into practice, step by step, in [IMPLEMENTATION.md](IMPLEMENTATION.md).*