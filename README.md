# signalr-lab

A hands-on lab for learning ASP.NET Core SignalR: real-time messaging, hubs, and client-server communication — from basics to expert, one step at a time.

## How this repo works

Every time you come back here: read a bit of theory, then implement the matching step.

| Document | Purpose |
|---|---|
| 📖 [docs/THEORY.md](docs/THEORY.md) | SignalR theory A to Z — transports, hubs, groups, users, auth, streaming, reconnection, scale-out, and more. Read once fully, then use as a reference. |
| 🛠️ [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md) | Step-by-step implementation journal — 16 steps from `AddSignalR()` to Redis backplane and testing. Updated gradually as each step is completed. |

**Current progress:** Step 1 of 16 — SignalR service registered, `ChatHub` mapped at `/hubs/chat`, CORS configured. Next up: [Step 2 — first hub method](docs/IMPLEMENTATION.md#-step-2--first-hub-method-broadcast-a-chat-message).

## Project structure

```
src/
└── SignalRLab.Api/        ASP.NET Core (.NET 10) Web API hosting the SignalR hub
    ├── Hubs/ChatHub.cs    The hub clients connect to
    └── Program.cs         Service registration, CORS, hub endpoint mapping
```

## Running

```powershell
dotnet run --project src/SignalRLab.Api
```

The hub listens at `/hubs/chat`. Quick smoke test that the endpoint is alive:

```powershell
curl.exe -X POST https://localhost:7064/hubs/chat/negotiate?negotiateVersion=1 -k
```