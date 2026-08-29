<div align="center">
  <img src="Assets/app/dir2site-icon.svg" alt="dir2site" width="250"/>
</div>

# Dir2Site

Turn any folder into a polished static website — instantly.

Dir2Site is a open-source cross-platform desktop application that walks your local directory structure and generates a ready-to-serve static site. Point it at a folder full of photos, PDFs, or documents, configure a few settings, and click **Generate**. A built-in preview server lets you review the results immediately in your browser.

**Your filesystem is your CMS.** Metadata is stored as YAML files alongside your content — one file per artifact, human-readable and diff-friendly. Check your whole site into a git or other source-control repository, track every change, and collaborate with standard source-control tools. No database, no lock-in.

> **Alpha Stage:** Dir2Site is under active development. Expect rough edges, missing features, and breaking changes. Contributions and feedback welcome.

## Features

- **Photo galleries** — full-screen browsing with deep-zoom viewer (OpenSeadragon), optional overlay annotations
- **PDF viewer** — embedded document reader (BookReader)
- **Markdown articles** — render `.md` files as clean web pages
- **Videos** — drop in a YouTube `.url` shortcut; plays inline on the collection page
- **Collection pages** — browsable index pages for every subdirectory
- **Folder introductions** — drop an `index.md` into a folder to open its page with your own prose
- **Customizable branding** — site title, primary/secondary colors, custom logo, dark or light navbar
- **Multi-column footer** — columns of icon-and-label links, with brand icons that color themselves
- **YAML configuration** — site settings edited in the app; per-artifact metadata in a plain-text file beside the artifact
- **Built-in preview server** — one click to serve and open in your browser, no external tools needed
- **One-click generation** — static HTML output written directly alongside your files

## How it works

1. Open dir2site and click **Choose…** to select your site project folder
2. Fill in site settings (title, colors, logo)
3. Click **Generate Site** — the static site is written to a `_site/` subfolder inside your project folder
4. Click **▶ Start** to launch the preview server, then open it in your browser

If you have deleted or renamed something since the last run, generating finds the pages and images
it left behind in `_site` and asks whether to remove them. Files starting with a dot — a hand-placed
`.htaccess`, a `.well-known/` folder — are never touched, since dir2site didn't put them there.

> **Tip:** when checking your project into git, ignore the generated output with `/_site/` — do
> **not** use a blanket `_*` rule, or you'll exclude `_media` and other static-asset folders.

## Documentation

Your folder tree is the site's structure, and a YAML file beside each artifact holds its settings.
The full reference lives in `docs/`:

- **[Artifact settings](docs/artifact-settings.md)** — what goes in an artifact's YAML: captions,
  credits, source links, home-page features, folder cover images, publishing a PDF's original file
- **[Folder markers](docs/folder-markers.md)** — `-About` for menu-only sections, `--Footer` for
  footer-only pages, `Newspapers+` to feature a folder on the home page, `_media` for static assets,
  and `index.md` to introduce a folder in your own words
- **[Writing Markdown articles](docs/writing-articles.md)** — Markdown support, images, and figures
- **[Adding videos](docs/adding-videos.md)** — `.url` shortcuts, start offsets, thumbnails, playback
- **[The footer](docs/the-footer.md)** — footer columns, link forms, and icons

## Platform support

| Platform | Architecture |
|---|---|
| Windows | x64 |
| macOS | x64, ARM64 (Apple Silicon) |
| Linux | x64 |

## License

Dir2Site is licensed under the [GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0).

Third-party open-source components are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Build from source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/EvanRuiz/dir2site
cd dir2site
dotnet run
```

To publish a self-contained release build:

```bash
# macOS (Apple Silicon)
dotnet publish -r osx-arm64 -c Release

# Windows
dotnet publish -r win-x64 -c Release

# Linux
dotnet publish -r linux-x64 -c Release
```

## Demos / Test Data

Demo and test sites (with source projects) are in the [dir2site-demos](https://github.com/EvanRuiz/dir2site-demos) repository.

### Demo: Famous Physicists

- Preview Generated Static Site: [Famous Physicists Demo](https://evanruiz.github.io/dir2site-demos/physicists/_site/)
- Source Project Directory: [Project Directory](https://github.com/EvanRuiz/dir2site-demos/tree/main/docs/physicists)

> Note: Demo content (images, biography, papers) was generated or collected by AI for testing purposes. Any inaccuracies are unintentional — please open an issue to report them.
