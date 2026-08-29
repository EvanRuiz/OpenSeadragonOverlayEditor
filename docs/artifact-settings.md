<!-- SPDX-FileCopyrightText: 2026 Evan Ruiz and Dir2Site Contributors -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Artifact settings

Every artifact — photo, PDF, article, video — has a YAML file beside it holding its settings.
Dir2Site writes one the first time it sees the file, listing every setting that artifact type
accepts, blank or at its default. A yaml written before a setting existed gains it on the next scan,
so what's in the file is always the whole menu.

That last part means the app adds lines to files you already had — the first scan after an upgrade
will show up as a diff across your project. It only ever *adds* settings, blank, at the end of the
file; values you wrote, your comments and your key order are left exactly as they were, and the app
says how many files it touched when the scan finishes.

---

## The settings every artifact has

| Setting | What it does | Default |
|---|---|---|
| `caption` | The title on the card and the page | the filename, tidied up |
| `credit` | Attribution line under the caption | blank |
| `date` | Shown under the credit; free text, so `1890` and `March 1890` both work | blank |
| `url` | An external source or reference to link out to | blank |
| `url-text` | The words of that link; blank uses the address itself | blank |
| `home` | Also show this artifact on the home page — see [below](#featuring-an-item-on-the-home-page-home-true) | `false` |
| `parent-cover` | Make this the picture for its folder's card — see [below](#choosing-a-folders-picture-parent-cover-grandparent-cover) | blank |
| `grandparent-cover` | The same, one level further up | `false` |

Some types add their own: `photographer` on a photo, `author` and `publishOriginal` on a PDF,
`provider`, `videoId` and `start` on a video.

Anything else you find in a yaml — `id`, `preview`, `previewLarge`, `image`, `overlays` — belongs to
Dir2Site, which fills it in and overwrites it as the site is generated. Leave those alone.

A misspelled setting is reported as a warning when you generate, because YAML has no way of knowing
that `parentcover` was meant to be `parent-cover` and would otherwise just sit there doing nothing.

The app shows an artifact's metadata but doesn't edit it — the yaml is where per-artifact settings
live. Site-wide settings are the ones you edit in the app, and they live in `dir2site.yaml`.

---

## Linking to a source (`url`, `url-text`)

An artifact often came from somewhere — a catalogue entry, an archive record, the page you found it
on. Put the address in `url` and the words in `url-text`:

```yaml
type: photo
caption: Portrait of a Stranger
credit: Unknown photographer
url: https://example.org/archive/1890/portrait
url-text: See the archive record
```

The link appears on the artifact's own page, under the credit line, with an "opens in a new window"
icon and in your site's secondary color. Leave `url-text` blank and the address itself is the link
text — filling in only the address never means no link at all.

Cards don't carry the link; the card's one job is to take you to the artifact.

Videos differ in where the link goes, not in how it works: a video has no page of its own, so its
link sits on the card. A blank `url` there means the `.url` shortcut's own address, which stays
opt-in — the player already offers YouTube's — so a video with neither `url` nor `url-text` carries
no link. See [Adding videos](adding-videos.md).

An address is published only if it's one a browser would follow to another page — `http`, `https`,
`mailto` or somewhere within your own site. Anything else is left off, rather than turned into a
link that runs when clicked, and generating says which file it was so a perfectly innocent `ftp://`
doesn't just quietly vanish.

---

## Featuring an item on the home page (`home: true`)

Add `home: true` to an artifact's YAML and it also gets a card on the home page, wherever in the
tree it actually lives. The card links to the artifact's real page — a video plays in place, as it
does anywhere else — and the artifact keeps its ordinary card in its own folder, so nothing moves.
Featured cards come after the home page's own contents.

The folder-shaped counterpart is a [`+`-folder](folder-markers.md#folders-featured-on-the-home-page--folders).

### Breadcrumbs on cards

A card featured on the home page carries the folders its item sits in — "Trips › Japan › Kyoto" — on
a small quiet line above its name, using the same labels as that item's page shows in its breadcrumb
bar. It is what makes such a card legible: something pulled onto the home page from three levels
down otherwise arrives with nothing but its own name. This is not optional and needs no setting.

Ordinary cards don't show a trail, because on a folder page it would be the breadcrumb bar directly
above them, repeated once per card. If you want it anyway — every card self-describing wherever it
appears — tick **Card Titles → Include Breadcrumbs** in Site Settings. The setting is stored in
`dir2site.yaml` as `cardBreadcrumbs` and is off unless it says otherwise. Top-level cards never show
a trail: the home page is their only ancestor.

---

## Choosing a folder's picture (`parent-cover`, `grandparent-cover`)

A folder's card is illustrated by whichever artifact inside it sorts first, which is rarely the one
that says what the collection is. Add `parent-cover: true` to an artifact's YAML to choose it
instead:

```yaml
type: photo
caption: The one that says what this is
parent-cover: true
```

It also becomes the folder page's `og:image`, so a shared link shows the same picture. Only the
folder the artifact sits in is affected — that's what "parent" names.

A folder that holds nothing but sub-folders has no artifacts of its own to choose from. Mark one a
level deeper with `grandparent-cover: true` and it illustrates that folder too:

```
Trips/                            card shows Cherry Blossom.jpg
Trips/Japan/Cherry Blossom.jpg    grandparent-cover: true
```

A `grandparent-cover` never outranks a real direct child, so a folder with its own photos still
shows one of those.

(`cover: true` is the older spelling of `parent-cover` and still works. Where a project carries
both, `parent-cover` decides — including when it says `false`.)

---

## Offering a PDF's file (`publishOriginal: true`)

A `.pdf` gets a page of its own with an embedded reader (BookReader): each page is rendered to an
image at generation time, and the reader pages through those. The source PDF itself is not
published, so visitors read the document but don't get the file.

To publish the PDF itself as well, set `publishOriginal` in its yaml:

```yaml
type: pdf
caption: The Riverbend Type Specimen
publishOriginal: true
```

The source file is copied into the site next to the artifact's page, and the page gains a
**Download PDF** link. With `publishOriginal: false` — the default — no copy is made and no link
appears; if the file had already been published, the next generate offers to take it back down.
