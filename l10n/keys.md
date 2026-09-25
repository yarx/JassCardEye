# Keys

One line of context for every key in `de.json`: where the text appears and how much room it has.
`src/tools/l10n.py` refuses a key that is missing here, so this list and the file cannot drift apart.

- A key ending in `.ios` or `.android` exists on that platform only, because the two stores and
  systems are named differently; everything else appears in both apps.
- `%1$d` is a number, `%1$s` a text, both filled in by the app. The number says which value it is,
  so a translation may put them in another order but has to keep every one of them.
- An entry with `one` and `other` is a plural: the app picks the form that fits the number.
- *Narrow* means a cell a fifth or a third of a phone's width: a word or two, never a sentence.

## Jass vocabulary

These are not translations but the words the players at a table use, and the Romandie and Ticino
have their own. They need a person who plays Jass in that language, not a dictionary; the same
words appear on the website's *Anleitung* and have to match there.

| Key | Context |
|---|---|
| `deck.french` | The French deck (Kreuz, Ecken, Herz, Schaufel), in the deck picker of *Neue Zählung*. Narrow. |
| `deck.german` | The Swiss German deck (Eichel, Schellen, Rosen, Schilten), next to `deck.french`. Narrow. |
| `suit.clubs` | French deck: the suit a French pack calls clubs. Under its mark in the trump row, and read aloud for the mark. |
| `suit.diamonds` | French deck: diamonds. As `suit.clubs`. |
| `suit.hearts` | French deck: hearts. As `suit.clubs`. |
| `suit.spades` | French deck: spades. As `suit.clubs`. |
| `suit.acorns` | Swiss German deck: the suit that plays the role of Kreuz. As `suit.clubs`. |
| `suit.roses` | Swiss German deck: the suit that plays the role of Herz. As `suit.clubs`. |
| `suit.bells` | Swiss German deck: the suit that plays the role of Ecken. As `suit.clubs`. |
| `suit.shields` | Swiss German deck: the suit that plays the role of Schaufel. As `suit.clubs`. |
| `rank.jack` | The jack, the same word on both decks. Next to the suit mark on a card chip, so short. |
| `rank.queen` | The queen, the same word on both decks. As `rank.jack`. |
| `rank.king` | The king. As `rank.jack`. |
| `rank.ace` | The ace. As `rank.jack`. |
| `mode.trump.hint` | Under the discipline row when a trump suit is chosen: what the Under and the Nell (the nine of trump) are worth. |
| `mode.obenabe.name` | The discipline played top-down without trump, as the score and the screen reader name it. |
| `mode.obenabe.short` | The same, under its arrow in the discipline row. Narrow. |
| `mode.obenabe.hint` | Under the discipline row: which cards change value in Obenabe. One line. |
| `mode.undenufe.name` | The discipline played bottom-up without trump. As `mode.obenabe.name`. |
| `mode.undenufe.short` | Narrow, as `mode.obenabe.short`. |
| `mode.undenufe.hint` | One line, as `mode.obenabe.hint`. |
| `mode.slalom_obe.name` | Slalom, alternating from trick to trick, begun top-down; counted as Obenabe. |
| `mode.slalom_obe.short` | Narrow; the arrow says it began top-down. |
| `mode.slalom_obe.hint` | One line: how it began and how it is counted. |
| `mode.slalom_unde.name` | Slalom begun bottom-up; counted as Undenufe. |
| `mode.slalom_unde.short` | Narrow; the arrow says it began bottom-up. |
| `mode.slalom_unde.hint` | One line: how it began and how it is counted. |
| `mode.guschti.name` | Guschti: four tricks top-down, then five bottom-up; counted as Obenabe. |
| `mode.guschti.short` | Narrow. |
| `mode.guschti.hint` | One line: how it is played and how it is counted. |
| `last_trick.note_mine` | Under the score while counting: `%1$d` is the 5 points for the last trick, taken by us. |
| `last_trick.note_opponents` | Under the score while counting: the opponents took the last trick. |

## App

| Key | Context |
|---|---|
| `app.name` | The app's name under its icon and as the heading of the home screen and *Über*. |
| `app.camera_usage.ios` | The system's question for camera access, asked once before the first count. One sentence. |

## Shared buttons

| Key | Context |
|---|---|
| `common.done` | Closes a sheet (settings, *Über*) and ends a count. Trailing button. |
| `common.cancel` | Leaves *Neue Zählung* or the card picker without doing anything. Leading button. |
| `common.close` | Closes the purchase page. Leading button. |

## Home screen

| Key | Context |
|---|---|
| `home.last_count` | Small heading over the result of the last count. |
| `home.nothing_counted` | Where the last result stands, before anything was counted. |
| `home.demo_note` | Under the start button in the demo: counting works, only the points are blurred. |
| `count.start` | The start button on the home screen and at the bottom of *Neue Zählung*. |

## Score

The score row has three columns of a third each: own points, what is counted, the opponents'.

| Key | Context |
|---|---|
| `score.mine` | Caption under the own points. Narrow. |
| `score.mine_factor` | The same with the factor, `%1$d` from 2 to 8. Narrow. |
| `score.card_points` | Caption under the own points when last trick or factor are in play: `%1$d` card points, followed by "+5" and "×2". Narrow. |
| `score.cards` | How many cards are on the pile, `%1$d` from 0 to 36. Narrow. |
| `score.opponents` | Caption under the opponents' points. Narrow. |
| `score.locked` | Read aloud for a blurred score in the demo instead of the number. |

## New count (*Neue Zählung*)

| Key | Context |
|---|---|
| `start.title` | Title of the sheet asked before every count. |
| `start.deck` | Small heading over the deck picker. |
| `start.deck_picker.ios` | Read aloud for the deck picker. |
| `start.trump` | Small heading over the row of the four trump suits. |
| `start.no_trump` | Small heading over the row of the disciplines without trump. |
| `start.choose` | Where the hint of the chosen discipline stands, before one is chosen. |
| `start.last_trick` | Small heading over the choice who took the last trick. |
| `start.factor` | Small heading over the row ×1 to ×8. |
| `start.factor_value` | Read aloud for one of the ×1 to ×8 buttons, `%1$d` the factor. |
| `last_trick.mine` | Segment: we took the last trick. A third of the width. |
| `last_trick.opponents` | Segment: the opponents took it. A third of the width. |
| `last_trick.none` | Segment: the last trick is not counted at this table. A third of the width. |

## Counting

| Key | Context |
|---|---|
| `scan.add_card` | Title of the picker that adds a card the camera did not recognise. |
| `scan.missing_card` | Button that opens that picker. Short, it shares a row with two more buttons. |
| `scan.torch_on` | Read aloud for the torch button while the light is off. |
| `scan.torch_off` | Read aloud for the torch button while the light is on. |
| `scan.reset` | Button that empties the pile. One short word. |
| `scan.empty` | Where the recognised cards line up, before the first one. |
| `scan.camera_denied` | Heading over the picture when camera access was refused. |
| `scan.camera_denied_text` | Under it: why the app needs the camera. |
| `scan.open_settings` | Button under it that opens the system settings. |
| `scan.model_failed` | Over the picture if the recognition model cannot be loaded: `%1$s` the model's name, `%2$s` the system's reason. |
| `camera.denied` | Message when camera access was refused. |
| `camera.none` | Message on a device without a camera. |
| `camera.attach_failed` | Message when the camera cannot be opened. |
| `camera.output_failed.ios` | Message when the camera's picture cannot be read. |

## Settings (*Einstellungen*)

| Key | Context |
|---|---|
| `settings.title` | Title of the settings sheet, and read aloud for the gear button that opens it. |
| `settings.camera` | Section heading, shown on phones with an ultra-wide lens. |
| `settings.lens` | Row label: which lens films the pile. |
| `lens.wide` | The normal lens, as the row shows it. |
| `lens.wide.note` | Under the row while the normal lens is chosen. |
| `lens.ultra_wide` | The 0.5× lens. |
| `lens.ultra_wide.note` | Under the row while the ultra-wide lens is chosen. |
| `settings.stability` | Section heading: how sure the app has to be before it counts a card. |
| `settings.frames` | Row with a − and + next to it: `%1$d` camera frames from 1 to 10 have to show the same card. |
| `settings.fewer.android` | Read aloud for the − button. |
| `settings.more.android` | Read aloud for the + button. |
| `settings.rule` | Row label of the rule the frames are judged by. |
| `rule.run` | The rule "that many frames in a row", `%1$d` frames. |
| `rule.run.note` | Under the section while that rule is chosen. |
| `rule.majority` | The rule "a majority of the last frames", `%1$d` of `%2$d` frames. |
| `rule.majority.note` | Under the section while that rule is chosen. |
| `settings.feedback` | Section heading: what the phone does for every counted card. |
| `settings.vibration` | Slider label. |
| `settings.sound` | Slider label. |
| `settings.feedback_off` | Read aloud for a slider at its lowest position. |
| `settings.feedback_percent` | Read aloud for a slider: `%1$d` from 1 to 100. |
| `settings.feedback_note.ios` | Under the two sliders. |
| `settings.feedback_note.android` | Under the two sliders. |
| `settings.purchase` | Section heading. |
| `settings.points` | Row label: whether the points are unlocked. |

## Purchase

| Key | Context |
|---|---|
| `purchase.unlocked` | The state after the purchase, in settings and on *Über*. |
| `purchase.demo` | The state before it. |
| `purchase.restore` | Button that restores a purchase on a new phone. |
| `purchase.title` | Title of the purchase page. |
| `purchase.unlock` | Heading of the purchase page, and the button to it wherever the points are blurred. |
| `purchase.unlock_price` | That button with the price, `%1$s` as the store formats it ("CHF 5.00"). |
| `purchase.benefit_readable` | First line of what the purchase unlocks. |
| `purchase.benefit_last_count` | Second line; the quoted label is `home.last_count`. |
| `purchase.benefit_once` | Third line. |
| `purchase.buy` | The buy button, `%1$s` the price. |
| `purchase.unavailable` | The buy button when the store has no product. |
| `purchase.loading` | The buy button until the price has arrived. |
| `purchase.pending.ios` | Under the button while the purchase waits for approval. |
| `purchase.pending.android` | Under the button while the purchase waits for the payment. |
| `purchase.store_unreachable.ios` | Under the button when the App Store cannot be reached. |
| `purchase.store_unreachable.android` | Under the button when Google Play cannot be reached. |
| `purchase.failed` | Under the button when a purchase did not go through. |
| `purchase.query_failed` | When the store could not be asked for the purchases. |
| `purchase.nothing_to_restore.ios` | After *Kauf wiederherstellen* found nothing. |
| `purchase.nothing_to_restore.android` | After *Kauf wiederherstellen* found nothing. |

## About (*Über*)

| Key | Context |
|---|---|
| `about.title` | Title of the page, and read aloud for the info button that opens it. |
| `about.version` | Under the app's name, `%1$s` as "1.0.1 (1201)". |
| `about.publisher` | Section heading. |
| `about.company` | Row label next to "YARX GmbH". |
| `about.email` | Row label next to the address. |
| `about.website` | Row label next to "yarx.ch". |
| `about.licence` | Section heading. |
| `about.licence_text` | The paragraph about the licence. |
| `about.licence_link` | Link to the licence text. |
| `about.source` | Link to the source code. |
| `about.recognition` | Section heading. |
| `about.model_text` | The paragraph about the recognition model. |
| `about.credits` | Section heading over the eight suit marks and their authors. |
| `about.credits_licence` | Link to the licence of seven of the marks. |
| `about.credits_note` | Under the marks: where they come from. |
| `about.public_domain` | Next to the author of the one mark without a licence. Narrow. |
| `about.privacy` | Section heading. |
| `about.privacy_text` | The privacy statement. It has to say what the website's privacy policy says. |
| `about.libraries` | Section heading. |
| `about.libraries_text.ios` | Which third-party libraries the app uses. |
| `about.libraries_text.android` | Which third-party libraries the app uses. |
