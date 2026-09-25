# Google Play: Store-Eintrag und App-Inhalte

Was in der Play Console steht, an einem Ort - wie `src/app/ios/store/listing.md` für App Store
Connect. Ändert sich dort etwas, gehört es hier nachgeführt. Die Angaben zu
Berechtigungen und Daten gelten für das zusammengeführte Manifest mit allen Bibliotheken; nach jeder
Änderung am Manifest oder an den Abhängigkeiten sind sie am gebauten Build nachzuprüfen.

## Veröffentlichung

| Was | Stand |
|---|---|
| App | JassCardEye, `ch.yarx.jasscardeye` |
| Play App-Signatur | aktiv; der Fingerabdruck des Upload-Schlüssels steht in `src/scripts/lib/release.sh` |
| Interner Test | Testerliste «Intern»; ein Lauf des `Release`-Workflows mit den Standardwerten lädt dorthin |
| Lizenztester | Einstellung des Entwicklerkontos, gilt für alle Apps des Kontos |
| Produktion | 1.0.0 (1107) aus dem internen Test hochgestuft, 177 Länder, Versionshinweis de-DE; zur Prüfung eingereicht |
| Verwaltete Veröffentlichung | an: eine freigegebene Version geht erst mit «Veröffentlichen» live |

## Store-Eintrag

| Feld | Inhalt |
|---|---|
| App-Name (max. 30) | JassCardEye |
| Standardsprache | Deutsch – de-DE (in der Console «Standardmäßiger Store-Eintrag») |
| Kurzbeschreibung (max. 80) | Zählt die Karten nach dem Jass mit der Kamera, Karte für Karte. |
| App-Symbol | `store/icon-512.png` (512 × 512, aus `src/tools/make_android_icon.py`) |
| Vorstellungsgrafik | `store/feature-graphic.png` (1024 × 500, aus demselben Skript) |
| Screenshots Telefon | `store/screenshots/*.png` (1080 × 2160, 1:2) aus dem Emulator: Start, Neue Zählung, Zählen, Einstellungen. Ablauf: «Store screenshots» in `src/app/android/README.md` |
| Kategorie | App, *Unterhaltung* |
| Kontakt E-Mail | support@yarx.ch |
| Website | https://jasscardeye.yarx.ch |
| Datenschutzerklärung | https://jasscardeye.yarx.ch/datenschutz |

**Zu den Screenshots.** Die eingecheckten Bilder zeigen die App ohne Demo: Die Punkte sind lesbar,
ohne Unschärfe und ohne «Punkte freischalten». Ausserdem sind alle vier Modelle in der App, darum
zeigen die Einstellungen den Abschnitt *Erkennungsmodell*, den die veröffentlichte App mit nur einem
Modell nicht hat. Der Zählbildschirm zeigt die Bildrate des Emulators (2 FPS). Sie neu aufzunehmen ist
offen.

**Beschreibung (max. 4000):**

> JassCardEye zählt nach dem Jass die Punkte. Karten der Reihe nach vor die Kamera legen, die App
> erkennt die oberste Karte und rechnet laufend zusammen.
>
> • Französisches und deutsches Blatt
> • Trumpf in jeder Farbe, Obenabe, Undenufe, Slalom und Guschti
> • Letzter Stich und Faktor ×1 bis ×8
> • Falsch erkannte Karte antippen und entfernen
> • Erkennung direkt auf dem Telefon, ohne Internet

## Weitere Sprachen

Die App spricht Deutsch, Französisch, Italienisch und Englisch (`l10n/`). Der Store-Eintrag ist heute nur
auf Deutsch eingetragen; jede weitere Sprache kommt unter *Store-Präsenz → Store-Einträge → Übersetzungen
verwalten* dazu, mit den Texten unten. App-Name, Grafiken, Kontakt und URLs bleiben gleich. Die
Jassbegriffe folgen `l10n/<Sprache>.json`; welche davon ein jassender Mensch schon geprüft hat, steht in
`l10n/README.md`.

### Französisch

| Feld | Inhalt |
|---|---|
| Sprache | Französisch – fr-FR |
| Kurzbeschreibung (max. 80) | Compte les cartes après le Jass avec la caméra, carte après carte. |
| In-App-Produkt: Name / Beschreibung | Compter les points / Rend lisibles les points comptés. |
| Screenshots Telefon | `store/screenshots/fr/*.png`, sobald aufgenommen (Ablauf: «Store screenshots» in `src/app/android/README.md`, die Sprache mit `adb shell cmd locale set-app-locales ch.yarx.jasscardeye --locales fr-CH`); bis dahin die deutschen |

**Beschreibung:**

> JassCardEye compte les points après le Jass. Pose les cartes l’une après l’autre devant la caméra, l’app reconnaît la carte du dessus et fait le total au fur et à mesure.
>
> • Jeu français et jeu suisse allemand
> • Atout dans chaque couleur, Obenabe, Undenufe, Slalom et Guschti
> • Dernier pli et facteur ×1 à ×8
> • Toucher une carte mal reconnue pour la retirer
> • Reconnaissance directement sur le téléphone, sans Internet

### Italienisch

| Feld | Inhalt |
|---|---|
| Sprache | Italienisch – it-IT |
| Kurzbeschreibung (max. 80) | Conta le carte dopo il Jass con la fotocamera, carta dopo carta. |
| In-App-Produkt: Name / Beschreibung | Contare i punti / Rende leggibili i punti contati. |
| Screenshots Telefon | `store/screenshots/it/*.png`, sobald aufgenommen (Ablauf: «Store screenshots» in `src/app/android/README.md`, die Sprache mit `adb shell cmd locale set-app-locales ch.yarx.jasscardeye --locales it-CH`); bis dahin die deutschen |

**Beschreibung:**

> JassCardEye conta i punti dopo il Jass. Posa le carte una dopo l’altra davanti alla fotocamera, l’app riconosce la carta in cima e fa il totale man mano.
>
> • Mazzo francese e mazzo svizzero tedesco
> • Briscola in ogni seme, Obenabe, Undenufe, Slalom e Guschti
> • Ultima presa e fattore da ×1 a ×8
> • Toccare una carta riconosciuta male per toglierla
> • Riconoscimento direttamente sul telefono, senza Internet

### Englisch

| Feld | Inhalt |
|---|---|
| Sprache | Englisch (Vereinigtes Königreich) – en-GB |
| Kurzbeschreibung (max. 80) | Counts the cards after a game of Jass with the camera, card by card. |
| In-App-Produkt: Name / Beschreibung | Count points / Makes the counted points readable. |
| Screenshots Telefon | `store/screenshots/en/*.png`, sobald aufgenommen (Ablauf: «Store screenshots» in `src/app/android/README.md`, die Sprache mit `adb shell cmd locale set-app-locales ch.yarx.jasscardeye --locales en-GB`); bis dahin die deutschen |

**Beschreibung:**

> JassCardEye counts the points after a game of Jass. Lay the cards down one by one in front of the camera, the app recognises the top card and keeps adding up.
>
> • French and Swiss German deck
> • Trump in every suit, Obenabe, Undenufe, Slalom and Guschti
> • Last trick and factor ×1 to ×8
> • Tap a wrongly recognised card to remove it
> • Recognition right on the phone, without internet

## App-Inhalte

**Datensicherheit.** Eingetragen ist: Die App erhebt und teilt keine Daten.

| Frage | Eingetragen | Grundlage |
|---|---|---|
| Werden Nutzerdaten erhoben oder geteilt? | Nein | Der eigene Code der App öffnet keine Netzwerkverbindung und hat keinen Server. `INTERNET` kommt mit der Play Billing Library (siehe «Berechtigungen»); die Antwort bleibt so. |
| Verschlüsselung bei der Übertragung | entfällt | die App selbst überträgt nichts |
| Löschung von Daten beantragen | entfällt | keine Daten bei uns |
| Kamera | Verarbeitung nur auf dem Gerät, nichts gespeichert | `src/web/privacy.md` |

**«Nichts gespeichert» ist Absicht.** *Bild* und *Session aufzeichnen* legen zwar Dateien auf dem Gerät
ab, sind aber Entwicklerwerkzeuge, versteckt hinter fünf Tippern auf *Firma*, und erscheinen in keinem
Text für Nutzer oder Prüfer. Diese Zeile darum nicht um
«ausser selbst ausgelösten Aufnahmen» ergänzen.

**Berechtigungen** (zusammengeführtes Manifest):

| Berechtigung | Herkunft und Zweck |
|---|---|
| `CAMERA` | die App: die Erkennung; die einzige, die sie zur Laufzeit erfragt |
| `VIBRATE` | die App: der Tipp bei jeder gezählten Karte |
| `com.android.vending.BILLING` | Play Billing Library 9.1.0: der Kauf |
| `INTERNET` | Play Billing Library 9.1.0, über Googles datatransport-Bibliothek (`com.google.android.datatransport:transport-backend-cct`); der eigene Code der App nutzt sie nicht |
| `ch.yarx.jasscardeye.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` | androidx.core: eine Signaturberechtigung der App für nicht exportierte Empfänger |

`ACCESS_NETWORK_STATE`, das androidx.media3 (über CameraX) und die datatransport-Bibliotheken
mitbringen, ist im Manifest entfernt. Keine Speicherberechtigung. `INTERNET` kommt mit Play Billing
und bleibt.

**Kauf und Datensicherheit.** Ob der Kauf besteht, fragt die App über die Play Billing Library bei
Google Play ab; sie sendet nichts an eigene Server, und Zahlungsdaten verarbeitet Google Play selbst.
Deshalb ist «keine Daten» eingetragen, und das bleibt trotz der `INTERNET`-Berechtigung der
Bibliothek so, ebenso der Datenschutztext auf der *Über*-Seite beider Apps.

**Inhaltsbewertung (IARC).** Kategorie *Dienstprogramm/Produktivität*; keine Gewalt, keine sexuellen
Inhalte, keine Interaktion zwischen Nutzern, kein Standort. **Käufe digitaler Güter: ja** - der
einmalige Kauf «Punkte zählen». Jass wird gezählt, nicht um Geld gespielt: kein Glücksspiel.

**Zielgruppe.** Erwachsene (18+); die App richtet sich nicht an Kinder.

**Werbung.** Keine. **Behörden-, Finanz-, Gesundheits-App.** Nein.

**Anmeldedaten (früher App-Zugriff).** Ja - kein Konto, aber die Punkte hängen am Einmalkauf, und die
Prüfer von Google kaufen nichts. Die Anleitung ist englisch (max. 500 Zeichen) und enthält zwei
Gutscheincodes aus der Promotion «App-Prüfung Google Play» (ID 129514676, 5 Einmal-Codes für «Punkte
zählen», gültig 15.09.-31.12.2026, Weitergabe an Testpartner ausgeschaltet). Die Codes stehen nur in
der Play Console, nie im Repository.

## Preis

Kostenlos, mit einem einmaligen In-App-Kauf «Punkte zählen» (`ch.yarx.jasscardeye.counting`, CHF 5,
173 Regionen). Die Schweiz ist einzeln auf 4,64 gesetzt: Die Console nimmt einen
eingegebenen CHF-Preis für die Schweiz als Nettopreis und schlägt die Mehrwertsteuer auf (aus 5 würden
5,40), und die deutsche Oberfläche liest «5.00» als 500. Eine Familienfreigabe gibt es bei Google Play
für Einmalkäufe nicht; der Kauf gehört zum Google-Konto. Eine kostenlose App kann bei Google Play nie
mehr kostenpflichtig werden - der Kauf läuft deshalb in der App.

## Offen

- **Gutscheincodes.** Sie gelten bis 31.12.2026; eine Prüfung danach braucht eine neue Promotion und
  neue Codes in der Anleitung.
