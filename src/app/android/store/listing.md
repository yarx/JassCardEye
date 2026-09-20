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
