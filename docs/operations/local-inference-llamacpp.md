# Local inference with llama.cpp

Seldon Lattice runs its local chat and embedding models through llama.cpp. The application no
longer launches or monitors Bionic / LM Studio.

## Services

| Service | Port | Model setting |
| --- | ---: | --- |
| Chat | 9931 | `Models:Chat` |
| Embeddings | 8081 | `Models:Embedding` |

Both servers are launched with the llama.cpp server binary configured by `LlamaCpp:Executable` (default `llama-server`; set it to the full path when the binary is not on PATH). The chat process lets llama.cpp fit
the GPU layer count to free VRAM; model weights for layers that do not fit remain in system RAM.
Its context is 16K with one slot. Embeddings run on CPU with a 2048 token context, batch, and
micro-batch.
The API's OpenAI-compatible HTTP clients are provider-neutral; they do not require a Bionic or
LM Studio installation.

## Startup and controls

When the API runs in Development with `LlmProvider` set to `LlamaCpp`, startup launches the
chat server and then the embedding server before the API starts serving requests. Existing
listeners are reused. Chat launch waits briefly for the port; because the 30B model can take
longer to load, the launcher reports `starting` until port 9931 is listening. The server-status
endpoint exposes this state, and the Settings page shows it while keeping engine features
disabled until both servers are ready. The Settings page's server-status provider automatically
starts both llama.cpp servers if either is down. Launch errors include the process output so a
missing model or invalid llama.cpp option can be diagnosed.

The launcher streams child-process stdout and stderr into the application logger. Raw lines are
logged at Debug level, while lines marked as warnings or errors use their corresponding levels.
When a process exits, the logger records its exit code and the last 40 combined output lines;
unexpected nonzero exits are logged as errors. Development enables Debug logging for the
launcher category, so the full live llama.cpp output is available in the configured Serilog
sinks without enabling Debug output for the rest of the application.

The Settings page's **Servers** section displays and controls only **llama.cpp Chat** and
**llama.cpp Embeddings**. Its server status endpoint reports both ports; `allRunning` means both
are listening. The bulk start and stop controls and individual service controls use the same
API launcher.

The configured LlamaCpp endpoints are `http://localhost:9931/v1` for chat and
`http://localhost:8081/v1` for embeddings. Keep the model aliases and configured model paths in
sync with the arguments built by `ServerLaunchService`.

On startup, saved framework settings that still point at the retired local port 1235 are
migrated to the current deployment defaults and returned to the settings confirmation gate.
The migration does not contact the old endpoint.

## Retired local provider

Bionic / LM Studio and its `lms.exe` command are not part of the runtime. Port 1235 is no longer
managed by the server launcher, and its state does not affect whether the reasoning engine is
enabled. Remote OpenAI-compatible providers remain configurable separately through the provider
settings.
