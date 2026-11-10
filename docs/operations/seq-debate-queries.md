# Querying debate logs in Seq

SeldonLattice sends structured application events to Seq when `Seq:ServerUrl` is configured. The default development URL is `http://localhost:5341` in `src/SeldonLattice.API/appsettings.json`.

Each ascent level adds these structured properties to logs emitted during that debate, including nested agent and moderator calls:

- `TelemetryKind = 'Debate'`
- `SessionId`
- `AscentLevel`
- `DebateTheme`

Search in Seq using, for example:

```text
TelemetryKind = 'Debate' and SessionId = 'ac5c5876-f3cc-4067-8ba9-70f935518ea8'
```

To inspect a particular level, add `and AscentLevel = 2`.

## Query through Seq's HTTP API

Use a Seq API key with **Read** permission. Keep the key in the caller's secret store and send it in the `X-Seq-ApiKey` header. These examples target a local Seq instance; replace the host and session ID as needed.

Fetch stored events after or during a run:

```http
GET http://localhost:5341/api/events?filter=TelemetryKind%20%3D%20%27Debate%27%20and%20SessionId%20%3D%20%27ac5c5876-f3cc-4067-8ba9-70f935518ea8%27&count=100&render=true
X-Seq-ApiKey: <read-api-key>
```

Follow matching events while the debate is running:

```http
GET http://localhost:5341/api/events/stream?filter=TelemetryKind%20%3D%20%27Debate%27%20and%20SessionId%20%3D%20%27ac5c5876-f3cc-4067-8ba9-70f935518ea8%27
X-Seq-ApiKey: <read-api-key>
```

The live stream is a Seq event stream; callers should keep the HTTP response open and process events incrementally. Seq's server API and API-key header are documented at [Server HTTP API](https://datalust.co/docs/server-http-api) and [Using the HTTP API](https://datalust.co/docs/using-the-http-api).

The event stream includes the debate's structured logs, not the generated dialogue transcript as a separate endpoint. Use the application session/history APIs for transcript content.
