# Glossary

Terms used in the code, the documents and the apps. German terms are the ones on screen and at the
table; the code uses the English tokens next to them.

## Jass

| Term | Meaning |
|---|---|
| **Jass** | The Swiss card game this project counts. 36 cards per deck, four players in two teams. |
| **Blatt** (deck) | The set of cards in play: *Französisch* (French suits) or *Deutsch* (Swiss German suits). A pile is always one deck. The scans in `data/cards/` are AGMüller's French standard deck and its Schaffhauser Spielkarten «Opti-Quatro» (German suits) - the two decks the model knows. |
| **Farbe** (suit) | French: *Kreuz* `clubs`, *Ecken* `diamonds`, *Herz* `hearts`, *Schaufel* `spades`. German: *Eichel* `acorns`, *Rosen* `roses`, *Schellen* `bells`, *Schilten* `shields`. Each German suit plays and scores in the role of a French one: acorns = clubs, roses = hearts, bells = diamonds, shields = spades. |
| **Ranks** | 6, 7, 8, 9, 10 (*Banner* on the German deck), *Under* `jack`, *Ober* `queen`, *König* `king`, *Ass* `ace`. The French deck prints *Bauer* and *Dame*; the apps show *Under* and *Ober* for both decks. |
| **Stich** (trick) | The four cards played in one round of the game. A team's pile is the tricks it won. |
| **Letzter Stich** | The last trick of a game, worth +5 points by convention (*Wir*, *Gegner* or *Keiner* in the start sheet). |
| **Trumpf** | The suit that beats all others; the *Under* of trump counts 20, the 9 (*Nell*) 14. |
| **Obenabe** | No trump, ranks count top-down: *Ass* 11, 8 counts 8. |
| **Undenufe** | No trump, ranks count bottom-up: 6 counts 11, *Ass* 0. |
| **Slalom** | Alternates Obenabe and Undenufe from trick to trick; the direction it began with decides how the pile is counted (*Slalom ↓* / *Slalom ↑*). |
| **Guschti** | Four tricks Obenabe, then five Undenufe; counted like Obenabe. |
| **Mary, Mezzo, Misère** | Disciplines the app counts through another one: Mary as Undenufe, Mezzo and Misère as Obenabe. |
| **Disziplin / Spielart** | What was played: the four trump suits, Obenabe, Undenufe, Slalom ↓, Slalom ↑, Guschti - nine entries in the app. |
| **Faktor** | What the table multiplies the written result by, ×1…×8 (e.g. in a *Coiffeur*, where each discipline has its own multiplier). |
| **152 / 157** | The card points of a full deck in every discipline; with the last trick, the 157 a team writes for a full pile. |
| **Match** | Winning all tricks. The app has no match bonus; it arrives at 157 by counting. |
| **Weis, Stöck** | Points announced and written during play (combinations; *König* and *Ober* of trump). Not part of the pile count. |
| **Tafel** | The written score across games and rounds. The app is a pile counter, not a Tafel. |

## The apps

| Term | Meaning |
|---|---|
| **Zählung** (count, session) | Counting one pile: from *Zählen starten* in the start sheet to *Fertig*. Camera and recognition run only during a session. |
| **Start sheet** (*Neue Zählung*) | The screen before a session: deck, discipline, last trick, factor. |
| **Pile** | The cards counted so far in a session, shown as chips. |
| **Commit** | A card landing on the pile - once the stability rule accepts it, or when added by hand with *Karte fehlt?*. Tap and tick fire on a commit. |
| **Stability rule** (*Zählweise*) | When a detection becomes a commit: *Serie* (the same top card in N frames in a row; the default) or *Mehrheit* (N of the last 2N−1 frames, with no other uncounted card appearing more than once). N is 3 by default, adjustable from 1 to 10. |
| **Suppressed card** | A card removed by tapping its chip; it is not counted again while it is still on top. |
| **Demo** / **Freigeschaltet** | The free app with blurred points, and the app after the purchase. |
| **Punkte zählen** | The one in-app purchase, product `ch.yarx.jasscardeye.counting`. |
| **Developer tools** | *Bild* and *Session aufzeichnen*, hidden until *Firma* on the *Über* page is tapped five times. |
| **Screenshot mode** | `JASSCARDEYE_SCREENSHOTS=1` (iOS) / `-Pjasscardeye.screenshots=true` (Android): hides the test-video note for store screenshots. |

## Dataset and recognition

| Term | Meaning |
|---|---|
| **Top card** | The topmost card of a pile - the only card that is labelled and recognised. |
| **Class-ID contract** | Class ID = suit × 9 + rank: French deck 0-35, German deck 36-71. Labels are `suit_rank`, e.g. `hearts_ace`. |
| **Variant A, B, C** | The three approaches compared in the thesis. **C** `detect`: one detector with 72 classes (shipped). **A** `classify_full`: classify the whole image (72 classes + `none`). **B**: **B₁** `locate` finds the card as an oriented box, **B₂** `classify_crop` classifies the rectified crop. After the thesis only C is pursued. |
| **OBB** | Oriented bounding box: four corners in the order top-left, top-right, bottom-right, bottom-left. |
| **Rectified crop** | The top card cut out along its OBB and straightened to 246×384 - what B₂ sees. |
| **Negative** | An image in which no card should be recognised: empty table, a flying or covering card, a card clearly cut off by the border, or a real photo. Negatives have no box label and sit under `none` for A. |
| **Real negative** | A photograph without cards (`data/negatives/`) used as a negative. |
| **RunPlan** | What the image index decides in a run: the camera tilt (swept evenly 0-60°) and the deck (half the run each). |
| **Seed, count** | Together with the commit they identify a generated dataset; it is regenerated for every run, never stored. |
| **Source of truth / derived parts** | The committed part of a dataset (`images/`, `detect/labels/`, `locate/labels/`, `classes.txt`) and what `rebuild` derives from it. |
| **Validation set** | `data/real/val`: 813 hand-labelled real photos and video frames - 305 with a French card, 208 with a German card, 300 negatives - never trained on. There is no separate test set. |
| **JassCardEye Dataset Tool** | The Avalonia app in `src/tools/dataset/Viewer`: browse a dataset, label photos, extract and review video frames. |

## Training and release

| Term | Meaning |
|---|---|
| **Run** | One training run: generate the dataset once, train the requested variants in sequence, upload the results. |
| **Run id** | `<UTC yyyymmdd-HHMM>-<7-character commit>`, e.g. `20260101-1200-abc1234`; also the pod's name and the folder in Azure. |
| **Pod** | The rented RunPod GPU container a cloud run trains on; it deletes itself at the end. |
| **manifest.json** | The one state file of a run (state, progress, heartbeat, per-variant results). |
| **run.json** | The result of one variant: metrics of the best epoch, training time, exit code. |
| **models.json** | Provenance of the models bundled into an app: run id, commit, dataset, metrics per variant. |
| **Sweep / watchdog** | `src/scripts/sweep_pods.py`, run every half hour by `cleanup.yml`, removes pods whose run finished, went silent or passed its ceiling. |
| **mAP50, mAP50-95, top-1** | Detection accuracy at IoU 0.5 and averaged over 0.5-0.95; classification accuracy of the first guess. |
| **best.pt** | The weights of the epoch Ultralytics ranks best; the model that is exported. |
| **Marketing version / build number** | `MARKETING_VERSION` (e.g. 1.0.0) and the Release workflow's run number + 1200 - the same for both apps. |
| **Track** | Where a Play build goes: `internal`, `alpha`, `beta`, `production`, or `none` (a rehearsal that uploads nothing). |
| **Upload key / app signing key** | The key the Android bundle is signed with, and the key Google Play re-signs it with. |
