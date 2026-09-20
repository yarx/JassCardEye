# Thesis (doc/)

The CAS Transferarbeit and the work journal, in LaTeX. Two documents, built separately.

| Path | What it is |
|---|---|
| `transferarbeit.tex` | The thesis. Preamble, macros and the order of the parts; the parts themselves are in `transferarbeit/`. |
| `transferarbeit/` | Title page, declaration, summary, the eight chapters, glossary and the two appendices, one file each, pulled in with `\input`. |
| `arbeitsjournal.tex` | The work journal, a document of its own. |
| `literatur.bib` | The bibliography of the thesis. |
| `figures/` | Everything the two documents include. |
| `latexmkrc` | Tells latexmk to build both documents into `build/`. |

Both PDFs are git-ignored; they are built from the sources.

## Building

With a full TeX distribution (MacTeX / TeX Live), from this folder:

```bash
latexmk            # transferarbeit.pdf and arbeitsjournal.pdf, into build/
latexmk -pvc       # rebuild on every save
```

Or with [tectonic](https://tectonic-typesetting.github.io), which needs no installed distribution and
downloads what a document asks for:

```bash
tectonic transferarbeit.tex
tectonic arbeitsjournal.tex
```

The two differ in where the result lands: latexmk writes into `build/`, tectonic next to the source.

The engine is pdflatex, the class `scrartcl` (11 pt, A4, `parskip=half`).

## Bibliography

Classic BibTeX, not biber: `\bibliographystyle{alphadin}` (DIN 1505-2, alphanumeric keys such as
`[EVGW+10]`). Tectonic runs BibTeX itself, so citations resolve in a plain `tectonic` call. A new
entry needs nothing but a key in `literatur.bib` and a `\cite` that uses it - an entry nobody cites
never appears in the bibliography.

## What bites in this document

| | |
|---|---|
| `\textdegree{}` | The degree sign. A literal `°` comes out as `ř` under T1 encoding. |
| `\enquote{…}` | Quotation marks, from `csquotes`; with `nswissgerman` they come out as « ». |
| `\map{50}` | Writes `mAP@50`. |
| `\code{…}` | Monospace, for paths, commands and identifiers. |
| `[H]` | From `float`: places a figure or table exactly here instead of letting it drift. |

Figures are addressed by file name alone (`\graphicspath{{figures/}}`). Where a figure exists as PDF
and as PNG, the PDF is the one to include: it stays sharp at any size.
