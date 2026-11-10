# Development setup

This is the first-run checklist for a developer machine. It covers the model files, the
stream-token secret, the development API keys, and the local Plainsentinel packages.

## 1. Prerequisites

- .NET 10 SDK (the version is pinned in `global.json`).
- Node.js 20+ and npm.
- A local llama.cpp build with a chat server binary (for example `llama-server` or
  `llama.exe`).

## 2. Restore and build

```bash
dotnet build SeldonLattice.slnx
cd frontend
npm install
```

## 3. Model files

The GGUF model files are not in the repository (they are multi-GB). Download them and place
them under a local models root:

```
<models-root>/
  Qwen/Qwen3-30B-A3B-Instruct-2507/Qwen3-30B-A3B-Instruct-2507-Q4_K_M.gguf
  nomic-ai/nomic-embed-text-v1.5/nomic-embed-text-v1.5.f16.gguf
```

Then set the following in `src/SeldonLattice.API/appsettings.json` (or through environment
variables):

| Key | Value |
|---|---|
| `Models:Root` | Absolute path to `<models-root>` |
| `Models:Chat` | `Qwen/Qwen3-30B-A3B-Instruct-2507/Qwen3-30B-A3B-Instruct-2507-Q4_K_M.gguf` |
| `Models:Embedding` | `nomic-ai/nomic-embed-text-v1.5/nomic-embed-text-v1.5.f16.gguf` |
| `LlamaCpp:Model` | `Qwen3-30B-A3B-Instruct-2507-Q4_K_M` (the alias, not a path) |
| `LlamaCpp:Executable` | Full path to the llama.cpp server binary, if it is not on `PATH` |

Environment variable equivalents: `Models__Root`, `Models__Chat`, `Models__Embedding`,
`LlamaCpp__Model`, `LlamaCpp__Executable`.

The API starts even when the models are missing (`DisableStartupLlmCheck` is true), but the
reasoning engine stays disabled until both llama.cpp servers are listening on ports 9931
and 8081.

## 4. Stream-token secret

`StreamTokens:Secret` signs the single-use SSE tokens. In Development the application falls
back to a built-in development secret and the startup check is skipped. Outside Development
the application fails fast if the secret is missing or still the default.

For a production or shared deployment, set a random value:

```bash
StreamTokens__Secret=<random value>
```

## 5. Development API keys

`appsettings.json` ships a set of development API keys (`dev-key-admin`, `dev-key-reader`,
`dev-key-ingest`, `dev-key-dialectician`, `dev-key-settings`, `dev-key-full`) with their
roles. They are for local development only. Change them for any shared deployment; the
frontend dev proxy uses `dev-key-full` by default (see `frontend/.env.example`).

## 6. Frontend

```bash
cd frontend
npm install
npm run dev
```

Copy `.env.example` to `.env` if you need to change the proxy target or the API key. The
client opens on `http://localhost:5174`.

## 7. Optional services

| Service | Config key | Default |
|---|---|---|
| Seq | `Seq:ServerUrl` | `http://localhost:5341` |
| OpenTelemetry | `OpenTelemetry:OtlpEndpoint` | `http://localhost:4317` |

Both are optional. Seq receives structured logs; OTLP receives traces and metrics. If the
endpoints are not reachable, the application still runs.

## 8. Local Plainsentinel packages

The API references `Plainsentinel.Sdk` and `Plainsentinel.Telemetry`, which are not yet
published to nuget.org. The `.nupkg` files are committed under `nuget/local/` and
`nuget.config` points at that folder, so `dotnet restore` works out of the box. When
Sentinel is published, remove the `local` source from `nuget.config` and the `nuget/local/`
folder.

## 9. Generated data

The application creates a `Data/` directory under the API content root on first run. It
contains:

- `sessions.db` and `settings.db` (SQLite).
- `merkle/store.dat` (the content-addressed node store).
- `pqc-keys/*.mldsa.json` (per-signer ML-DSA keys, generated if missing).

`Data/` is gitignored. Do not commit it.

## 10. Ports

| Service | Port |
|---|---|
| API (development) | 5050 |
| Frontend dev server | 5174 |
| llama.cpp chat | 9931 |
| llama.cpp embeddings | 8081 |
| Seq | 5341 |
| OTLP | 4317 |

## 11. Zero-config checklist

Out of the box, the following work with the committed configuration:

- The API builds and starts.
- The SQLite databases and ML-DSA keys are generated on first run.
- The stream-token check is skipped in Development.
- The development API keys are present.
- The frontend dev proxy falls back to the committed dev key.

The following require one-time setup:

- Model files and `Models:Root` (debates do not run without them).
- `LlamaCpp:Executable` if the binary is not on `PATH`.
- A random `StreamTokens:Secret` outside Development.
- The Plainsentinel packages are committed, so no extra step is needed now.
