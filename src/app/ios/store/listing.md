# App Store: Produktseite und TestFlight

Was in App Store Connect steht, an einem Ort. Die Beschreibung ist in beiden Stores gleich (mit
«iPhone» statt «Telefon»), die übrigen Texte sind so kurz wie im Play Store. Ändert sich dort etwas,
gehört es hier nachgeführt. Die Entwicklerwerkzeuge kommen in keinem Text vor, den Nutzer oder Prüfer
lesen.

## Einreichung

| Feld | Inhalt |
|---|---|
| Version | 1.0.0 |
| Build | 1107, Modell der Variante c aus dem Trainingslauf `<run-id>` (steht in `models.json` des Builds) |
| Eingereicht | zusammen mit dem In-App-Kauf in einer Übermittlung |
| Freigabe | manuell - veröffentlicht wird von Hand |
| Exportkonformität | wird nicht pro Build gefragt: `ITSAppUsesNonExemptEncryption` ist `NO` (`project.yml`) |

## Produktseite (Version 1.0.0)

| Feld | Inhalt |
|---|---|
| Name | JassCardEye |
| Untertitel (max. 30) | Jasspunkte zählen per Kamera |
| Werbetext (max. 170) | Nach dem Jassen den Stapel nicht mehr von Hand zählen: Karten vor die Kamera legen, JassCardEye rechnet die Punkte zusammen. |
| Schlüsselwörter (max. 100) | Jass,jassen,Jasskarten,Punkte,zählen,Schieber,Trumpf,Obenabe,Undenufe,Slalom,Guschti,Karten,Schweiz |
| Support-URL | https://jasscardeye.yarx.ch/support |
| Marketing-URL | https://jasscardeye.yarx.ch |
| Copyright | 2026 YARX GmbH |
| Screenshots iPhone | 6,5"-Feld: `store/screenshots/6.5/*.png` (1284 × 2778) - Start, Neue Zählung, Zählen, Einstellungen; siehe «Screenshots» |

Die Schlüsselwörter belegen 99 der 100 Zeichen.

**Beschreibung (max. 4000)** - der Text des Play Stores, mit «iPhone» statt «Telefon»:

> JassCardEye zählt nach dem Jass die Punkte. Karten der Reihe nach vor die Kamera legen, die App
> erkennt die oberste Karte und rechnet laufend zusammen.
>
> • Französisches und deutsches Blatt
> • Trumpf in jeder Farbe, Obenabe, Undenufe, Slalom und Guschti
> • Letzter Stich und Faktor ×1 bis ×8
> • Falsch erkannte Karte antippen und entfernen
> • Erkennung direkt auf dem iPhone, ohne Internet

## Weitere Sprachen

Die App spricht Deutsch, Französisch, Italienisch und Englisch (`l10n/`), und App Store Connect listet
die Sprachen aus dem Build. Die Produktseite ist heute nur auf Deutsch eingetragen; für jede weitere
Sprache kommt unter *App-Informationen → Lokalisierung* eine Lokalisierung dazu, mit den Texten unten.
Name, URLs und Copyright bleiben gleich. Die Jassbegriffe folgen `l10n/<Sprache>.json`; welche davon ein
jassender Mensch schon geprüft hat, steht in `l10n/README.md`.

### Französisch

| Feld | Inhalt |
|---|---|
| Lokalisierung | Französisch (fr-FR) |
| Untertitel (max. 30) | Compter les points du Jass |
| Werbetext (max. 170) | Après le Jass, plus besoin de compter le tas à la main : pose les cartes devant la caméra, JassCardEye fait le total des points. |
| Schlüsselwörter (max. 100) | Jass,chibre,cartes,points,compter,atout,Obenabe,Undenufe,Slalom,Guschti,Suisse,jeu de cartes |
| In-App-Kauf: Anzeigename / Beschreibung | Compter les points / Rend lisibles les points comptés. |
| TestFlight: Beta-App-Beschreibung | JassCardEye compte les points après le Jass. Pose les cartes l’une après l’autre devant la caméra, l’app reconnaît la carte du dessus et fait le total au fur et à mesure. |
| Screenshots | `store/screenshots/6.5/fr/*.png`, sobald aufgenommen (siehe «Screenshots»); bis dahin die deutschen |

**Beschreibung:**

> JassCardEye compte les points après le Jass. Pose les cartes l’une après l’autre devant la caméra, l’app reconnaît la carte du dessus et fait le total au fur et à mesure.
>
> • Jeu français et jeu suisse allemand
> • Atout dans chaque couleur, Obenabe, Undenufe, Slalom et Guschti
> • Dernier pli et facteur ×1 à ×8
> • Toucher une carte mal reconnue pour la retirer
> • Reconnaissance directement sur l’iPhone, sans Internet

### Italienisch

| Feld | Inhalt |
|---|---|
| Lokalisierung | Italienisch (it) |
| Untertitel (max. 30) | Contare i punti del Jass |
| Werbetext (max. 170) | Dopo il Jass, basta contare il mazzetto a mano: posa le carte davanti alla fotocamera, JassCardEye fa il totale dei punti. |
| Schlüsselwörter (max. 100) | Jass,carte,punti,contare,briscola,Obenabe,Undenufe,Slalom,Guschti,Svizzera,Ticino,gioco di carte |
| In-App-Kauf: Anzeigename / Beschreibung | Contare i punti / Rende leggibili i punti contati. |
| TestFlight: Beta-App-Beschreibung | JassCardEye conta i punti dopo il Jass. Posa le carte una dopo l’altra davanti alla fotocamera, l’app riconosce la carta in cima e fa il totale man mano. |
| Screenshots | `store/screenshots/6.5/it/*.png`, sobald aufgenommen (siehe «Screenshots»); bis dahin die deutschen |

**Beschreibung:**

> JassCardEye conta i punti dopo il Jass. Posa le carte una dopo l’altra davanti alla fotocamera, l’app riconosce la carta in cima e fa il totale man mano.
>
> • Mazzo francese e mazzo svizzero tedesco
> • Briscola in ogni seme, Obenabe, Undenufe, Slalom e Guschti
> • Ultima presa e fattore da ×1 a ×8
> • Toccare una carta riconosciuta male per toglierla
> • Riconoscimento direttamente sull’iPhone, senza Internet

### Englisch

| Feld | Inhalt |
|---|---|
| Lokalisierung | Englisch (Vereinigtes Königreich) (en-GB) |
| Untertitel (max. 30) | Count Jass points by camera |
| Werbetext (max. 170) | No more counting the pile by hand after a game of Jass: lay the cards in front of the camera, JassCardEye adds up the points. |
| Schlüsselwörter (max. 100) | Jass,Schieber,cards,points,count,score,trump,Obenabe,Undenufe,Slalom,Guschti,Swiss,card game |
| In-App-Kauf: Anzeigename / Beschreibung | Count points / Makes the counted points readable. |
| TestFlight: Beta-App-Beschreibung | JassCardEye counts the points after a game of Jass. Lay the cards down one by one in front of the camera, the app recognises the top card and keeps adding up. |
| Screenshots | `store/screenshots/6.5/en/*.png`, sobald aufgenommen (siehe «Screenshots»); bis dahin die deutschen |

**Beschreibung:**

> JassCardEye counts the points after a game of Jass. Lay the cards down one by one in front of the camera, the app recognises the top card and keeps adding up.
>
> • French and Swiss German deck
> • Trump in every suit, Obenabe, Undenufe, Slalom and Guschti
> • Last trick and factor ×1 to ×8
> • Tap a wrongly recognised card to remove it
> • Recognition right on the iPhone, without internet

## App-Informationen

| Feld | Inhalt |
|---|---|
| Kategorie | Dienstprogramme (Google Play: Unterhaltung) |
| Altersfreigabe | 4+ |
| Inhaltsrechte | Ja, mit Rechten |

## Preise und Verfügbarkeit

| Feld | Inhalt |
|---|---|
| Preis | kostenlos, Basis Schweiz |
| Verfügbarkeit | alle 175 Länder und Regionen |

## App-Datenschutz

| Feld | Inhalt |
|---|---|
| Datenschutz-URL | https://jasscardeye.yarx.ch/datenschutz |
| Datenerfassung | «Keine Daten erfasst», veröffentlicht |

Belegt durch `PrivacyInfo.xcprivacy` (keine erhobenen Daten) und `src/web/privacy.md`: Die App hat keinen
eigenen Netzwerkcode, nur StoreKit fragt beim App Store nach, und die Zahlung wickelt Apple ab.

## In-App-Kauf

| Feld | Inhalt |
|---|---|
| Name | «Punkte zählen» |
| Produkt-ID | `ch.yarx.jasscardeye.counting` |
| Apple-ID | 6811688048 |
| Typ | Nicht-Verbrauchsartikel |
| Preis | CHF 5, Basis Schweiz |
| Verfügbarkeit | 175 Länder und Regionen |
| Familienfreigabe | an |
| Lokalisierung | Deutsch; Französisch, Italienisch und Englisch siehe «Weitere Sprachen» |
| Prüfung | Screenshot für die Prüfung hochgeladen; mit Version 1.0.0 eingereicht |

## App-Prüfung

**Kontakt:** Raphael Bolliger, support@yarx.ch

**Anmerkungen für die App-Prüfung:**

> Keine Anmeldung nötig. Ohne Jasskarten lässt sich die App trotzdem ausprobieren: «Zählen starten»,
> eine Spielart wählen und im Zählbildschirm über «Karte fehlt?» Karten von Hand hinzufügen.
> Kartenliste und Punkte füllen sich wie bei der Erkennung mit der Kamera.
>
> Die App ist gratis und zählt vollständig, nur die berechneten Punkte sind verwischt. Der einmalige
> In-App-Kauf «Punkte zählen» macht sie lesbar. «Kauf wiederherstellen» steht in den Einstellungen.
>
> Die Erkennung läuft ganz auf dem Gerät. Die App hat keinen eigenen Server; nur Kauf und
> Wiederherstellen fragen beim App Store nach.

## TestFlight

| Feld | Inhalt |
|---|---|
| E-Mail für Feedback | support@yarx.ch |
| Marketing-URL | https://jasscardeye.yarx.ch |
| Datenschutz-URL | https://jasscardeye.yarx.ch/datenschutz |
| Gruppen | «Tester» (intern) und «Externe Tester» (extern, mit Beta-App-Prüfung) |

**Beta-App-Beschreibung:**

> JassCardEye zählt nach dem Jass die Punkte. Karten der Reihe nach vor die Kamera legen, die App
> erkennt die oberste Karte und rechnet laufend zusammen.

## Screenshots

`store/screenshots/6.9/*.png` (1320 × 2868) kommen aus dem Simulator «iPhone 17 Pro Max»;
`store/screenshots/6.5/*.png` (1284 × 2778, ohne Alphakanal) sind daraus skaliert und zugeschnitten,
für das 6,5"-Feld. Vier Bilder: Start mit einer letzten Zählung, Neue Zählung, Zählen mit erkannter
Karte, Einstellungen.

**Stand der eingecheckten Bilder.** Sie zeigen einen Build ohne Demo und Kauf und mit allen vier
Modellen im Bundle. Darum zeigen Start und Zählen lesbare Punkte ohne «Punkte freischalten», und die
Einstellungen zeigen den Abschnitt «Erkennungsmodell», den die ausgelieferte App mit nur einem Modell
nicht hat, aber keinen Abschnitt «Kauf». Eine neue Aufnahme nach der folgenden Anleitung gleicht sie
der ausgelieferten App an.

### Neu aufnehmen

1. In `src/app/ios/Models` liegt nur `JassCardEye-c.mlpackage` (mit `models.json`). Mit mehreren
   Varianten zeigen die Einstellungen «Erkennungsmodell».
2. Die App einmal aus Xcode auf dem Simulator «iPhone 17 Pro Max» starten, damit sie installiert ist,
   und in Xcode wieder stoppen. Die Kennung des Simulators nennt `xcrun simctl list devices booted`.
3. Statusleiste setzen und die App mit Screenshot-Modus und Testvideo starten. Das Testvideo wird
   über `JASSCARDEYE_VIDEO` angegeben.

   ```bash
   xcrun simctl status_bar <udid> override --time 9:41 --dataNetwork wifi --wifiMode active --wifiBars 3 \
       --cellularMode active --cellularBars 4 --batteryState discharging --batteryLevel 100
   SIMCTL_CHILD_JASSCARDEYE_SCREENSHOTS=1 SIMCTL_CHILD_JASSCARDEYE_VIDEO=/Pfad/zum/Testvideo.mov \
       xcrun simctl launch --terminate-running-process <udid> ch.yarx.JassCardEye
   ```

   `JASSCARDEYE_SCREENSHOTS=1` blendet nur den Hinweis «Simulator: Testvideo statt Kamera
   (Endlosschleife).» aus. Über `simctl` gestartet hat die App keine StoreKit-Konfiguration und zeigt
   die Demo: verwischte Punkte und «Punkte freischalten».
4. Die Entwicklerwerkzeuge sind ausgeblendet: Auf dem Startbildschirm steht kein «Session
   aufzeichnen». Sonst im Über-Bildschirm fünfmal auf «Firma» tippen.
5. Die Bilder in dieser Reihenfolge, jedes mit
   `xcrun simctl io <udid> screenshot src/app/ios/store/screenshots/6.9/<Name>.png`:
   - «Zählen starten», Blatt Französisch, Trumpf «Herz» wählen: `02-neue-zaehlung.png`
   - «Zählen starten», das Testvideo zählen lassen, aufnehmen, während eine Karte erkannt ist:
     `03-zaehlen.png`
   - «Fertig», der Startbildschirm zeigt die letzte Zählung: `01-start.png`. Sie lebt nur, solange die
     App läuft - zwischen Zählen und diesem Bild die App nicht neu starten.
   - Zahnrad: `04-einstellungen.png`
6. Statusleiste zurücksetzen und den 6,5"-Satz ableiten (braucht Pillow): auf 1284 Pixel Breite
   skalieren, oben und unten je 6 Pixel wegschneiden, ohne Alphakanal speichern.

   ```bash
   xcrun simctl status_bar <udid> clear
   python3 - <<'EOF'
   from pathlib import Path
   from PIL import Image
   source = Path("src/app/ios/store/screenshots/6.9")
   target = Path("src/app/ios/store/screenshots/6.5")
   for png in sorted(source.glob("*.png")):
       image = Image.open(png).convert("RGB").resize((1284, 2790), Image.LANCZOS)
       image.crop((0, 6, 1284, 2784)).save(target / png.name)
   EOF
   ```

In App Store Connect werden die Dateien von Hand ins 6,5"-Feld gezogen; ein Upload per Skript geht
dort nicht.

### Je Sprache

Derselbe Ablauf für jede weitere Sprache, mit der Sprache beim Start und einem Unterordner je Sprache
(`6.9/fr/`, `6.5/fr/` …); die deutschen Bilder bleiben, wo sie sind. Im Schritt 3 kommt die Sprache dazu:

```bash
SIMCTL_CHILD_JASSCARDEYE_SCREENSHOTS=1 SIMCTL_CHILD_JASSCARDEYE_VIDEO=/Pfad/zum/Testvideo.mov \
    xcrun simctl launch --terminate-running-process <udid> ch.yarx.JassCardEye -AppleLanguages "(fr)" -AppleLocale fr_CH
```

Die Knöpfe heissen dann so, wie `l10n/<Sprache>.json` sie nennt («Commencer à compter», «Inizia a
contare», «Start counting»). Aufgenommen ist noch keine weitere Sprache.
