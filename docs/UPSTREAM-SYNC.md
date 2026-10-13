# Upstream Sync Guide (v2rayMini)

How to pull changes from `2dust/v2rayN` (remote `upstream`) into this fork.

## Remotes

- `upstream` — `git@github.com:2dust/v2rayN.git` (the original project; read-only for us)
- `origin` — `https://github.com/zamotkin/v2rayMini.git` (this fork)

## Sync flow

```bash
git checkout master && git fetch upstream && git merge --ff-only upstream/master
git checkout feature/v2raymini && git merge master
```

1. Update `master` from upstream. `master` is a pure mirror of `2dust/v2rayN` — it is
   **fast-forward-only**; never commit to it directly.
2. Merge the refreshed `master` into the feature branch. All our work lives on
   `feature/v2raymini` — that is the only branch we commit to and push to `origin`.

## Conflict policy

On conflicts, **prefer upstream** — upstream owns the codebase, and taking their side keeps
future merges trivial. The ONLY exceptions are our additive files, where we keep OUR side:

- `Common/MenuBarMode.cs`
- `Assets/menubar_*.png`
- `docs/UPSTREAM-SYNC.md`
- `scripts/deploy-macos.sh`
- the "Epics & tasks: use br (beads)" section in `CLAUDE.md`

These files do not exist upstream (or, for the CLAUDE.md section, upstream has no such
section), so conflicts there mean an accidental deletion — always restore our version.
