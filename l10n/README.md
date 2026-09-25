# Texts of the apps

Every text a person reads in the iPhone app or the Android app lives here, one file per language.
Both apps are built from these files, so a language added here appears in both after their next build,
and nobody needs Xcode or Android Studio for it.

| File | What it is |
|---|---|
| `de.json` | German, the source: every other language translates this file. Swiss spelling, *ss* and never *ß*. |
| `fr.json`, `it.json`, `en.json` | French, Italian, English. |
| `keys.md` | One line of context per key: where the text appears, how much room it has, what a placeholder is. |

`src/tools/l10n.py` turns the files into the resources of both apps and refuses what would go wrong on a
phone: a key missing in one language, a placeholder lost in a translation, a key without context. CI runs
it on every pull request (`python3 src/tools/l10n.py` from the repository root does the same).

## Adding a language

1. Copy `de.json` to `<code>.json`, with the two-letter code of the language (`rm.json` for Romansh).
2. Translate the values, never the keys. Read the line of each key in `keys.md` first - it says where the
   text stands and whether it has to be short.
3. Open a pull request. The check tells you which key is missing or which placeholder does not match.

The app then offers the language on every phone set to it; any language the app does not have shows German.
The website is translated separately, page by page - see "Languages" in `src/web/README.md`.

## Rules

- **Placeholders stay.** `%1$d` is a number and `%1$s` a text, filled in by the app. A translation may
  move them and change their order, but has to keep every one of them.
- **Plurals** are an object with a form per plural category of the language: `one` and `other` for German and
  English, plus `many` for French and Italian (for numbers such as a million). A form may leave the number
  out, `other` always carries it.
- **Platform keys.** A key ending in `.ios` or `.android` exists in that app only, because the two stores and
  systems are named differently (*Apple-ID* and *Google-Konto*). Translate both.
- **Address the player informally**: *du* in German, *tu* in French and Italian, *you* in English.
- **Labels quoted in a sentence** are written exactly as their key says, in « » - in English in “ ”.
- **Jass words are vocabulary, not translation.** The suits, the decks, the ranks, the disciplines and their
  hints are listed together at the top of `keys.md`. They need a person who plays Jass in that language: the
  words the table uses, not the dictionary's. The same words appear on the website's guide and must match.

## Status

| Language | Vocabulary held against the published rules | Checked by a player |
|---|---|---|
| German | the source | yes |
| French | [Swisslos, «Les règles du Jass»](https://www.swisslos.ch/fr/jass/informations/les-regles-du-jass/bases-du-jass.html) (*jeu français*, *jeu allemand*, *atout*, *dernier pli*, *Obenabe*, *Undenufe*, *grelot*, *écusson*); *Bour* and *Nell* as the Romandie says them | not yet |
| Italian | [Swisslos, «Regole dello jass»](https://www.swisslos.ch/it/jass/informazioni/regole-dello-jass/nozioni-di-base-dello-jass.html) (*mazzo francese*, *mazzo tedesco*, *briscola*, *presa*, *ultima presa*, *dall'alto*, *dal basso*); the Swiss suits after the [Circolo Svizzero](https://www.svizzeri.ch/2021/01/31/le-36-carte-per-giocare-a-jass/) (*Ghiande*, *Rose*, *Campanelle*, *Scudi*) | not yet |
| English | [pagat.com, Schieber](https://www.pagat.com/jass/schieber.html) (*Acorns*, *Bells*, *Shields*, *Obenabe*, *Undenufe*, *last trick*) | not yet |

A language counts as checked once a person who plays Jass in it has gone through the vocabulary in
`keys.md` and the website's guide; this table then says so. Until then the words are the published
ones where a source names them, and a careful choice where none does: the Italian hint names the trump
nine *nove* because no Italian source calls it *Nell*, and the French one keeps *Obenabe* and
*Undenufe* as Swisslos does, although older Romandie players also say *de haut en bas* and *de bas en
haut*.
