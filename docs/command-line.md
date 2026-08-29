<!-- SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Generating from the command line

Dir2Site is a desktop app, but a site you have already set up can be regenerated without opening a
window — from a script, a `make` target, or a CI job that rebuilds a demo and commits the output:

```bash
dir2site --generate path/to/project
```

No display is needed: the command starts Avalonia headlessly, so it runs on a build agent.

## What it does

The same three stages the **Generate Site** button runs. It reads the folder's `dir2site.yaml` for
the title, colours and PDF settings, brings previews up to date, writes `_site/`, and prints the
counters the app shows on its status line:

```
Generating Riverbend Press from /path/to/project
  Scanning for changes...
  Generating previews...
  Generating site...
Site generated → _site/
Artifacts 28/28 · Previews 28/28 · Pages 34/34 · Files 91/91
```

A folder with no `dir2site.yaml` gets one, holding the same defaults the app would have given it,
and the run says so. Editing that file — in the app or by hand — is how the site stops looking
like every other new project.

## Options

| Option | What it does |
|---|---|
| `--generate <folder>` | Generate that project and exit. `-g` and `--generate=<folder>` also work |
| `--quiet` | Print the summary alone, not each stage. `-q` also works |
| `--help` | Print usage |
| `--version` | Print the version |

With no arguments at all, the app opens as usual.

## What it reports

- **Warnings** — a misspelled setting in `dir2site.yaml` or in an artifact's yaml, an
  unpublishable `url` — are printed and do not fail the run.
- **Settings added to your yaml.** A scan completes any artifact's yaml that predates a setting, and
  the command says how many files it touched, so the diff that appears afterwards is explained.
- **Files in `_site` with no source left** are listed and **left alone**. In the app this is a
  question with a Delete button; a run with nobody to ask does not delete published files on its own
  say-so. Open the project in the app to clear them.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | The site was generated |
| `1` | Generation reported errors — each is printed to stderr |
| `2` | Bad arguments, a folder that isn't there or can't be written, or a `dir2site.yaml` that doesn't parse |
