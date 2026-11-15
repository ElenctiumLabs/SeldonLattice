# Models

Local GGUF model files, laid out `<publisher>/<repo>/<file>.gguf`.

These are **not** committed — see `.gitignore`. Each is multi-GB; the tree exists so
`ServerLaunchService` and `appsettings.json` can name a model once, by relative path,
instead of hardcoding absolute locations in two places.

## Layout

```
models/
  Qwen/Qwen3-30B-A3B-Instruct-2507/Qwen3-30B-A3B-Instruct-2507-Q4_K_M.gguf   chat (default)
  nomic-ai/nomic-embed-text-v1.5/nomic-embed-text-v1.5.f16.gguf     embeddings
  ggml-org/gemma-4-E2B-it-GGUF/gemma-4-E2B-it-Q8_0.gguf             chat (alternative)
  ggml-org/gemma-4-E2B-it-GGUF/mmproj-gemma-4-E2B-it-Q8_0.gguf      gemma multimodal projector
```

## Configuration

`appsettings.json` carries two distinct things, and they are not interchangeable:

| Key | Meaning | Example |
|---|---|---|
| `Models:Root` + `Models:Chat` / `Models:Embedding` | **file paths**, used by `ServerLaunchService` to start llama.cpp | `A:\SeldonLattice\models` + `Qwen\...\Qwen3-30B-A3B-Instruct-2507-Q4_K_M.gguf` |
| `LlamaCpp:Model` | the **alias** the server advertises and the client requests in the HTTP body | `Qwen3-30B-A3B-Instruct-2507-Q4_K_M` |

`ServerLaunchService` derives the alias from the chat model's file name
(`Path.GetFileNameWithoutExtension`), so the launched server and the client request cannot
disagree. Setting `LlamaCpp:Model` to a file path instead of an alias is a bug: the path is
sent as the model name in the request body.

## Switching chat model

1. Drop the new `.gguf` under `models/<publisher>/<repo>/`.
2. Set `Models:Chat` to its path relative to `Models:Root`.
3. Set `LlamaCpp:Model` to the alias — by convention the file name without extension.
4. Restart the chat server from the frontend (Settings → Servers).

## Why not `--hf-repo`

The chat server was previously launched with `--hf-repo <repo>`, which resolves against the
HuggingFace cache. That cache did not hold these files in a form the resolver accepts: the
gemma entry had real files under `snapshots/` with an empty `blobs/`, which is not a valid
HF cache layout, so `--hf-repo` would re-download rather than reuse it. Several models were
also loose files at the cache root rather than inside a repo directory. `-m <path>` against
this tree is therefore the reliable form.

## VRAM (GTX 1080, 8 GB)

| Model | Quant | File size | Fits |
|---|---|---|---|
| Qwen3-30B-A3B-Instruct-2507 | Q4_K_M | 17.2 GB | yes, with KV cache |
| gemma-4-E2B-it | Q8_0 | 4.6 GB | yes |
| Qwen2.5-14B-Instruct-1M | Q4_K_M | 8.4 GB | no — not copied into this tree |
| nomic-embed-text-v1.5 | F16 | 262 MB | separate process, port 8081 |
