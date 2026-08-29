<!-- SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Folder markers

Your folder tree is the site's structure: each folder becomes a collection page, listing a card for
everything inside it. A few characters in a folder's name change how that folder is presented, and
they are stripped everywhere a visitor can see — the marker is an instruction to the generator, not
part of the name.

| Named | Does |
|---|---|
| `-About` | in the menu, but no card on its parent page |
| `--Footer` | neither menu nor card; just its pages |
| `Newspapers+` | its ordinary card, plus one on the home page |
| `_media` | copied verbatim; never scanned for artifacts |
| `index.md` | opens its folder's page with your own prose, above the cards |

Because the markers are stripped, two folders that would publish to the same address — `Newspapers+`
and a plain `Newspapers` beside it — are reported by Generate Site rather than one quietly
overwriting the other.

---

## Introducing a folder (`index.md`)

Put an `index.md` in a folder and its collection page opens with whatever you write there, above the
cards. It works in any folder, including the project root — which is the one page with no other way
to say anything at all, since the home page is a collection like any other.

**The introduction owns the top of the page.** Where there is one, the heading dir2site would have
written — the folder's name — is left out, and whatever your `index.md` opens with stands in its
place. The file being there is the whole decision, whatever is in it. Write a heading of your own to
give the folder a title it wouldn't otherwise have, or open with a paragraph and have no heading at
all, which is usually what a home page wants: the site's name is already in the navbar above it. The
folder is still named in the browser's title bar and in the breadcrumbs either way.

`index.md` is special all the way down. It is not an artifact: no card, no page of its own, no
thumbnail, and — alone among the files dir2site reads — **no yaml**. There is nothing to caption,
credit or date, so nothing is written beside it. Ordinary Markdown, figures and `_media` references
all work as they do in an article, and relative paths are written from the folder the file sits in.

Two consequences worth knowing:

- A folder holding one artifact normally publishes as that artifact — see below. One that also has
  an introduction keeps its own page, otherwise the prose would vanish when the folder collapsed.
- Renaming an article to `index.md` turns it into an introduction, and its old yaml is left behind
  rather than carried over. Generate offers to tidy it up, as it does for any leftover.

---

## Folders holding a single item

A folder with exactly one artifact in it publishes that artifact as the folder's own page, rather
than a collection page whose only content is one card. So `-About/Our Story.md` is served at
`/About/` — clicking **About** in the menu shows the article itself.

Two things are deliberately left alone: a folder holding only a video (videos play inline and have
no page of their own, so there is nothing to promote), and a folder holding only another folder
(collapsing chains of folders gets surprising quickly).

---

## Menu-only sections (`-`-folders)

A folder whose name starts with a hyphen (e.g. `-About`) appears in the menu but not as a card on
its parent page, and it sits after the ordinary folders in the menu. Use it for the sections a site
needs but isn't presenting — About, Contact, Colophon.

```
Photographs/          shown in the menu and as a card
Documents/            shown in the menu and as a card
-About/               shown in the menu only, last
```

`-About` is published at `/About/` and shows as "About" everywhere a visitor can see.

---

## Footer-only sections (`--`-folders)

Double the hyphen and the menu entry goes too: a `--`-folder gets its page and nothing else — no
card, no nav. Use it for the pages nobody browses to and everybody expects to find at the bottom of
the page: privacy, terms, credits.

The recommended arrangement is one `--Footer/` folder holding all of them, rather than a marked
folder each:

```
--Footer/Privacy.md      published at /Footer/Privacy/, linked from the footer
--Footer/Use.md          published at /Footer/Use/
--Footer/Credits.md      published at /Footer/Credits/
```

Nothing enforces that shape — the marker works on any folder — but it keeps the project root
readable and puts the footer's pages where someone looking for them would look. `--Footer/` itself
still publishes a collection page at `/Footer/` listing them, which nothing links to and which is
out of both the menu and the cards.

Both hyphens are stripped, so `--Footer` is published at `/Footer/`.

---

## Folders featured on the home page (`+`-folders)

A folder whose name ends in a plus (e.g. `Newspapers+`) also gets a card on the home page, however
deep it sits. It is the folder-shaped counterpart of
[`home: true`](artifact-settings.md#featuring-an-item-on-the-home-page-home-true), and the way to
say which folders in the tree are worth a direct link from the front door.

```
Archive/Newspapers+/    a card in Archive, and a card on the home page
```

The plus is stripped: the folder is published at `/Archive/Newspapers/`, and its breadcrumbs still
show the full path it really lives at. The two markers are independent, so `-Newspapers+` is a
folder reachable from the menu and the home page but not from its parent's listing.

---

## Static media (`_media` and other `_`-folders)

To include images or other assets that should **not** become artifacts of their own, put them in a
folder whose name starts with an underscore (e.g. `_media`). Any `_`-prefixed folder is copied
verbatim into the generated site and is never scanned for artifacts. Reference it from your Markdown
with a path relative to the `.md` file:

```
MyArticle.md
_media/myfigure.webp
```

```markdown
![My figure](_media/myfigure.webp)
```

> **Tip:** when checking your project into git, ignore the generated output with `/_site/` — do
> **not** use a blanket `_*` rule, or you'll exclude `_media` and other static-asset folders.
