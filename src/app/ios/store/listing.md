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
| Lokalisierung | Deutsch |
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
