# Security policy

## Scope

Seldon Lattice is a local-first research tool. It is designed to run on a single machine
with local models. It is not hardened for multi-tenant or internet-facing production use.
Run it on a trusted network and keep the API keys and stream-token secret private.

## Reporting a vulnerability

Please do not open a public issue for security reports. Email `contact@seldonlattice.com`
with a description, the affected version or commit, and steps to reproduce. We will
acknowledge the report and work on a fix before any public disclosure.

## Authentication and authorization

The API uses API keys with roles:

| Role | Purpose |
|---|---|
| `admin` | Administrative endpoints |
| `reader` | Read sessions and nodes |
| `writer` | Create and modify content |
| `dialectician` | Run debates |
| `stream` | Consume SSE streams and mint stream tokens |
| `ingest` | Ingest external documents |
| `settings` | Read and write framework settings |

The committed keys (`dev-key-*`) are development defaults. Change them for any shared
deployment.

## Stream tokens

SSE streams that require a token use single-use, HMAC-SHA256-signed tokens with a 60-second
lifetime. The signing secret is `StreamTokens:Secret`. In Development the application falls
back to a built-in development secret; outside Development it fails fast if the secret is
missing or still the default. Set a random secret for any non-development run.

## Cryptographic storage

Every stored node is content-addressed (SHA3-256) and signed with ML-DSA-65. The signing keys
are generated on first run under `Data/pqc-keys/` and are gitignored. Keep the `Data/`
directory private.

## Rate limiting and CORS

The API enforces a per-key rate limit (30 requests per minute, burst 10) and restricts CORS
to localhost development origins. Adjust both for any deployment that is not local.

## Telemetry

Seq and OpenTelemetry export are opt-in. When enabled, structured logs and traces are sent
to the configured endpoint. Both default to localhost and are inactive unless that service
is running.

## Dependency security

Dependabot is enabled for NuGet and npm. Review dependency updates before merging.
