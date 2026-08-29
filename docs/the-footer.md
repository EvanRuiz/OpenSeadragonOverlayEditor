<!-- SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# The footer

Every page ends with the same footer. Out of the box that is one line — the **Footer text** field in
Site Settings, which allows HTML so it can hold a link — but it can also carry columns of links,
edited with the **Footer…** button beside it and stored in `dir2site.yaml`:

```yaml
footerColor: "#101c32"
footerItems:
  - column: 1
    icon: bi-youtube
    iconColor: "#ff0000"
    iconBackground: "#ffffff"
    title: Watch on YouTube
    link: https://example.com/channel
    note: 12,000+ views
  - column: 2
    icon: bi-info-circle
    title: About
    link: -About/Our Story.md
  - column: 3
    icon: bi-lock
    title: Privacy
    link: --Footer/Privacy.md
```

Rows sharing a `column` are stacked together and columns run left to right, in the order the rows
are written. An empty column number closes up rather than leaving a gap, so numbering 1 and 3 gives
two columns.

---

## `link`

**`link`** takes one of three forms, told apart by how it starts:

| Written as | Means |
| --- | --- |
| `https://…`, `http://…`, `mailto:…` | an address off the site; opens in a new tab |
| `/privacy/` | a path within the site, for a page dir2site didn't generate |
| `-About/Our Story.md` | a file or folder in the project, resolved to wherever it publishes |

The third form is the one to reach for: it follows the artifact, so a page published at a folder's
own address because it is the only thing in that folder is still linked correctly. A link naming
something that isn't in the project is reported by Generate Site and left out of the footer.

---

## Icons

**`icon`** is a [Bootstrap Icons](https://icons.getbootstrap.com/) name, with or without its `bi-`
prefix, and `iconColor` tints it.

**Brand icons color themselves.** `icon: bi-youtube` alone renders the real mark — red, with a
white play triangle — and the same goes for Facebook, Instagram, LinkedIn, Mastodon, GitHub, Bluesky
and the rest of Bootstrap's brand set. You don't have to know a brand's hex code, and you can't
accidentally ship a logo that looks wrong.

Each mark is filled the way its own drawing needs. YouTube's triangle is cut out of the middle of a
solid badge; Facebook's "f" runs through the bottom of its circle, so the white behind it has to be
circular to cover the letter without spilling past the curve; and marks like X, TikTok and Bluesky
are silhouettes with nothing cut out of them at all, so they get their color and nothing behind.

Naming either color yourself turns that off, so a mark that should match the rest of the column
rather than shout is one line:

```yaml
  - icon: bi-youtube
    iconColor: "#999999"    # deliberately muted; no brand fill applied
```

**`iconBackground`** is what makes the above work, and is there if you need it directly. Bootstrap's
brand icons are a single shape with the inner symbol cut out — `bi-youtube` is a rounded rectangle
whose play triangle is a *hole* — so on a dark footer that triangle would show the band color.
`iconBackground` fills the cut-out, and the glyph itself masks everything around it. Ordinary
single-color icons don't need it.

---

## Text and color

**`note`** is a caption line under the link — a maintainer's name, a view count. It is plain text;
`footer:` remains the one field that takes HTML, which is where the copyright line with its `<br>`
belongs.

**`footerColor`** is the band's background, defaulting to the primary color so the footer matches
the navbar. Text and link colors follow from it: a dark color gets light text, a light one dark.

Pages that only belong in the footer want a
[`--`-folder](folder-markers.md#footer-only-sections----folders), which keeps them out of the menu as
well as the cards.
