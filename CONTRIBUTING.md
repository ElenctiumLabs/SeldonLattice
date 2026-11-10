# Contributing

Thanks for your interest in Seldon Lattice. This document describes how to work on the
code and how to propose changes.

## Getting started

1. Read `README.md` and `docs/setup.md`.
2. Install the .NET 10 SDK and Node.js 20+.
3. Run the backend and frontend once to confirm your environment.

## Branching

- Create a feature branch from the default branch (`main`).
- Use a short, descriptive branch name (`feature/ladder-memory`, `fix/theme-cap`).

## Commits

The repository uses conventional commit prefixes:

| Prefix | Use for |
|---|---|
| `feat` | New features |
| `fix` | Bug fixes |
| `docs` | Documentation |
| `chore` | Maintenance and tooling |
| `refactor` | Code changes with no behavior change |
| `test` | Tests |

Write a subject line that completes the sentence "This commit ...". Keep the subject under
72 characters and add a body when the change is not self-explanatory.

## Running the tests

```bash
dotnet build SeldonLattice.slnx
dotnet test SeldonLattice.slnx
cd frontend
npm run lint
npm run build
npm test
```

The backend treats warnings as errors in Debug and Release. The frontend uses oxlint and
`tsc -b`.

## Pull requests

1. Open a draft PR early if you want feedback.
2. Keep the PR focused: one feature or fix per PR.
3. Add or update tests for behavior changes.
4. Update the README or `docs/` when the change affects setup or usage.
5. Do not commit secrets, model files, or generated data.
6. Answer review comments and keep the branch up to date with `main`.

## Reporting bugs and requesting features

Use the issue templates. For bugs, include the environment (OS, .NET SDK, Node), the steps
to reproduce, and the relevant log output. For security reports, follow `SECURITY.md`.

## Design discussions

Large design changes are discussed in issues before implementation. A written proposal with
the trade-offs helps the review.
