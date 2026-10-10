# PhonePilot – Implementierung und Prüfung

Stand: 10.10.2026. Die vollständige Implementierung wurde vom Nutzer einschließlich der technischen Detailvorschläge freigegeben. Grundlage bleibt die [Detail-Spezifikation](PhonePilot-Spezifikation.md). Aktuelle Ansichten stehen unter [Screenshots](screenshots/2026-10-10/README.md).

## Umgesetzte Anwendung

Die bisherige Launcher-Logik ist in einen unabhängig prüfbaren Anwendungskern und eine dauerhaft geöffnete WPF-Oberfläche aufgeteilt. Die vorhandenen Regeln für lokale Werkzeugpfade, IPv4-mDNS, Hardware-Seriennummern und gezielte scrcpy-Transporte bleiben die Grundlage.

- Dynamische Geräteliste, Auswahl und getrennte Zustände für mDNS, ADB und scrcpy.
- Höchstens vier eigene Sitzungen einschließlich Startreservierungen; höchstens eine Sitzung pro Gerätekennung.
- Einzel- und Sammelaktionen mit unabhängigen Fehlerergebnissen; ausschließlich eigene Prozess-Handles werden verwaltet.
- Steuerung, Geräteeinstellungen, Erweitert und Logs entsprechend der freigegebenen Aufteilung.
- Stream-Profile und validierte individuelle Einstellungen, inklusive Audio, Display, Fenstertitel, Vollbild, Rahmen, Vordergrund, Encoder und Darstellungsausrichtung.
- Code-Kopplung über geschützte Eingabe und Standardinput, manuelle Verbindung ohne gespeicherte Adresse.
- Automatische Aktualisierung standardmäßig alle fünf Sekunden, ausschließlich in den allgemeinen Einstellungen konfigurierbar.
- Zusammengefasste Refresh-Anfragen; alte Erkennungsergebnisse überschreiben keine neuere Geräteaktion.
- ADB-Wiederverbindung mit begrenztem Rücksetzintervall; bewusstes Trennen unterdrückt eigene Wiederverbindung, beendetes scrcpy wird nicht automatisch gestartet.
- Globale Darstellung System/Hell/Dunkel, konfigurierbares Schließen und Infobereich mit Öffnen, Alle stoppen und Beenden.
- Einzelinstanzschutz mit Aktivierung der vorhandenen Anwendung.
- Automatische, koordinierte Konfigurationsspeicherung, Sicherung vor Altdatenmigration und bewusste Wiederherstellung geschützter Formate.
- Live-Logs mit Bereich, Quelle, Level und Suche; Kopieren, UTF-8-Export, Ansicht leeren, 5000 Anzeigeeinträge und drei Logdateien mit begrenzter Größe.
- Explizit bestätigter ADB-Server-Neustart mit Hinweis auf die gemeinsame Wirkung; kein reguläres Beenden des ADB-Servers.
- Portable Veröffentlichung samt lokalen Werkzeugpaketen; `build.ps1 -NoLaunch` ermöglicht einen Neubau ohne anschließenden Programmstart.

## Teststrategie

Für den Kern wurde mit zuerst formulierten Verhaltensprüfungen und anschließender Implementierung gearbeitet. Ergänzend prüfen Integrations- und Regressionstests Dateisystem, echte lokale Unterprozesse und konkurrierende Aktionen. Die Oberfläche wird zusätzlich als tatsächlicher WPF-Inhalt mit Beispieldaten gerendert und visuell geprüft. Hardwareabhängige Eigenschaften werden getrennt ausgewiesen.

### Automatisiert bestanden: 43 Tests

```powershell
dotnet test .\tests\PhonePilot.Tests\PhonePilot.Tests.csproj
```

Geprüfte Verhaltensgruppen:

| Bereich | Nachweis |
| --- | --- |
| Identität und Parser | IPv4-/Portvalidierung, ausschließlich WLAN-Verbindungsdienste, Proto-/Textparser, falsche Seriennummer verhindert Start |
| Konfiguration | H.265/40-Mbit/s-Altdatenmigration, exakte Originalsicherung, unbekannte Version geschützt, Wiederherstellung mit Sicherung, vollständiges JSON bei konkurrierenden Schreibvorgängen |
| Schreibfehler | Gesperrte Zieldatei bleibt erhalten, temporäre Datei wird aufgeräumt |
| Profile und Argumente | Kerneinstellungen, Erhalt weiterer Optionen, eindeutiger Transport, Fenstertitel als einzelnes Argument |
| Sitzungen | Vier Plätze, fünfte Sitzung abgewiesen, parallele Doppelstarts erzeugen einen Prozess, Startreservierungen und individuelles Abbrechen |
| Gerätekoordination | Kein automatisches Starten/Verbinden beim ersten Fund gespeicherter Geräte, manuelle Trennung unterdrückt Retry, Geräte-Rückkehr startet kein scrcpy |
| Aktualisierung | Fehlgeschlagene Abfrage erfindet keine Trennung, mDNS-Verlust beendet bestätigtes ADB nicht, Refresh-Anfragen werden zusammengefasst |
| Konkurrierende Ergebnisse | Alter Refresh und verspätete Diagnose überschreiben keine neuere Benutzeraktion |
| Fehler und Beenden | Sammelstart setzt nach Einzelfehler fort, unerwartetes Sitzungsende gibt Platz frei, Fehlererklärung bleibt trotz verbundenem ADB erhalten, Schließen bricht laufenden Start ab |
| Echte Unterprozesse | Gleichzeitiges Lesen von stdout/stderr, Zeitlimit, Benutzerabbruch, unabhängiges Stoppen zweier Testprozesse, konkurrierendes Stoppen/Freigeben |
| Geheimnisse und Logs | Kopplungscode fehlt in Ergebnis und Logs, beschränkter Speicher auch bei Dateifehler, Rotation und Begrenzung sehr langer Zeilen |

Die Sitzungsintegration verwendet ein eigenes lokales Testprogramm, das ein Bereitschaftssignal und laufende Ausgabe liefert. Diese Prüfung belegt die Prozesssteuerung; sie belegt keine Android-Videoübertragung.

### Windows-Oberfläche und Veröffentlichung

- Vollständiger WPF-Build erfolgreich, zuletzt ohne Compilerwarnungen oder Compilerfehler.
- Selbstständige Windows-x64-Veröffentlichung erstellt: `publish/PhonePilot.exe` mit `adb/` und `scrcpy/`.
- Bestehende persönliche `publish/config.json` blieb beim Publish unverändert.
- Offline-Renderer prüft die vier Tabs, allgemeine Einstellungen, Hell/Dunkel, die Größe 960 × 680, zustandsabhängige Start-/Stoppaktionen und ungültige Eingaben.
- Die veröffentlichte EXE wurde für diese Offline-Prüfung aus einem anderen Arbeitsverzeichnis gestartet.
- Vollständiger Neubau über `build.ps1 -NoLaunch` bestanden; die persönliche Konfiguration und eine eigens angelegte Testsicherung blieben erhalten.
- Die zunächst sandboxbedingt nicht erreichbare NuGet-Sicherheitsabfrage wurde anschließend mit Netzwerkzugriff erfolgreich und ohne Warnung wiederholt.

Die aktuellen gerenderten Ansichten stehen unter [Screenshots](screenshots/2026-10-10/README.md). Sie zeigen die implementierte Oberfläche mit erfundenen Demo-Kennungen und simulierten Sitzungen.

### Prüfung mit den vorhandenen Werkzeugen und Geräten

Die lokalen Pakete wurden geprüft: ADB 37.0.1, scrcpy 4.1. Die verwendeten scrcpy-Argumente wurden mit der mitgelieferten Hilfe abgeglichen.

Die reale mDNS-Abfrage hat zwei WLAN-Geräte gefunden. Beide ließen sich über den lokalen ADB verbinden; `ro.serialno` entsprach jeweils der gefundenen Gerätekennung. Für diese Prüfung wurden keine scrcpy-Sitzungen gestartet und keine Display-/Audioeinstellungen am Smartphone geändert. Die bestätigten ADB-Verbindungen bleiben verfügbar.

## Noch am Desktop und mit Smartphones abzunehmen

Diese Punkte sind implementiert, aber durch die bisherigen automatisierten Prüfungen beziehungsweise die reine ADB-Identitätsprüfung noch nicht vollständig nachgewiesen:

1. Ein bis vier reale scrcpy-Fenster: Bild, Bedienung, Audio, ausgeschaltetes Handy-Display und Codec-/Encoderunterstützung.
2. WLAN aus/ein und wechselnder Verbindungsport: ADB kehrt zurück, beendete Sitzung bleibt gestoppt.
3. Code-Kopplung mit echter neuer beziehungsweise aufgehobener Kopplung, falschem Code und ausbleibendem mDNS-Fund.
4. Fensterfokus, echtes manuelles Schließen einzelner scrcpy-Fenster und Schutz eines zusätzlich fremd gestarteten scrcpy-Prozesses.
5. Schließdialog, Entscheidung merken, Infobereich, Öffnen über zweiten Start sowie Beenden über das Infobereich-Menü.
6. Eingabe per Tastatur, Dropdowns, Copy/Export, mehrere Monitore und Windows-Skalierung 125/150/200 Prozent.
7. Ausdrücklicher ADB-Server-Neustart mit anderen ADB-Anwendungen; kein unbeabsichtigter Sitzungsneustart.
8. Längere Laufzeit und Leistungsprofile mit ein bis vier Geräten unter realen WLAN-Bedingungen.

Die Profile sind weiterhin abgestimmte Ausgangswerte, keine zugesicherten Leistungswerte. Der Offline-Renderer ersetzt diese Abnahme nicht.

## Nachbesserung: stabile Abmessungen beim Fokuswechsel (10.10.2026)

Der Button-Fokus veränderte zuvor die Rahmenbreite von einem auf zwei Pixel. Auch die fokussierte Gerätezeile erhielt einen zusätzlichen Rahmen. Dadurch änderten sich die gewünschten Abmessungen und gegebenenfalls der Umbruch der oberen Aktionsleiste. Beide Fokusrahmen werden jetzt innerhalb der bestehenden Abmessungen darüber gezeichnet; sie bleiben für die Tastaturbedienung sichtbar.

Ein Regressionstest setzt den echten WPF-Tastaturfokus auf die sichtbaren, aktiven Buttons und die ausgewählte Gerätezeile im Steuerungs-Tab. Bei 1440 und 960 Pixel Fensterbreite bestanden alle 26 Fokuswechsel ohne Positions- oder Größenänderung. Gewolltes Scrollen zum jeweiligen Ziel wird vor dem Vergleich ausgeführt. Der Test arbeitet mit Beispieldaten und verbindet keine Geräte.

```powershell
dotnet .\bin\Debug\net10.0-windows\win-x64\PhonePilot.dll --check-layout .\artifacts\layout-check
```

Das Ergebnis steht bei Erfolg in `layout-check.txt`, bei Fehlern in `preview-error.txt`; der Prozess liefert einen entsprechenden Exitcode. Die Prüfung ergänzt die Verhaltensprüfungen des Anwendungskerns. Sie ersetzt keine Prüfung aller Fensterzustände, Tabs und Windows-Skalierungen.

Die aktualisierte selbstständige EXE wurde unter `publish/PhonePilot.exe` veröffentlicht. Auch diese EXE bestand die 26 Fokuswechsel, aus einem anderen Arbeitsverzeichnis gestartet. Die vorhandene persönliche Konfiguration blieb beim Ersetzen unverändert (SHA-256-Vergleich).

Das aktuelle Anwendungssymbol und seine Einbindung beschreibt [Assets/README.md](../Assets/README.md).

## Umbenennung zu PhonePilot und Anwendungssymbol (10.10.2026)

Auf Nutzerwunsch heißen das Hauptprojekt, die Assemblies, Namensräume und Testprojekte nun PhonePilot. Die neue Veröffentlichung ist `publish/PhonePilot.exe`. Oberfläche, Fenstertexte, Infobereich, Lognamen, Einzelinstanz-Signale, Manifest, EXE-Metadaten, Build-Skript, Editoraufgabe und aktuelle Dokumentation verwenden den neuen Namen. Der vorhandene Workspace-Ordner darf seinen bisherigen Namen behalten; er ist kein fest verdrahteter Laufzeitpfad.

Das gewählte Icon B2 zeigt Smartphone, WLAN und Mauszeiger. Die unveränderte PNG-Ausgabe liegt unter `Assets/PhonePilot.png`. `tools/Build-Icon.ps1` verpackt sie reproduzierbar in ein transparentes ICO mit neun Auflösungen (16, 20, 24, 32, 40, 48, 64, 128 und 256 Pixel). Das ICO ist als native EXE-Ressource und als WPF-Ressource eingebettet; das PNG erscheint in der App-Kopfzeile. Fenster, Dialoge und Infobereich nutzen das gewählte Symbol. Keine externe Icon-Datei muss neben der EXE verteilt werden.

Nachweise nach der Umbenennung:

- Build ohne Warnungen oder Fehler; alle 43 Tests der umbenannten Testprojekte bestanden, einschließlich echter lokaler Test-Unterprozesse.
- Veröffentlichte EXE aus einem anderen Arbeitsverzeichnis geprüft: Fenstername, Fenster-/Dialog-/Infobereich-Symbole, vier Tabs, allgemeine Einstellungen, Hell/Dunkel, kompaktes Fenster, Zustandsaktionen und Eingabevalidierung bestanden.
- Die 26 echten WPF-Fokuswechsel bei zwei Fensterbreiten bleiben ohne Layoutverschiebung.
- Windows konnte das neue 32×32-Symbol direkt aus `PhonePilot.exe` extrahieren; visuell geprüft. Produktname und Dateibeschreibung lauten PhonePilot.
- Persönliche `config.json` und vorhandene Sicherung sind unverändert (SHA-256-Vergleich vor/nach der Veröffentlichung).
- Die bisherige `PixelRemote.exe` und ihre Kern-PDB wurden nach erfolgreicher Prüfung unter `artifacts/previous-pixelremote` archiviert; im Veröffentlichungsordner liegt die neue EXE.

Ursprüngliche Mockup-Bilder, Bildgenerierungs-Prompts und Konzeptarchive wurden beim Aufräumen aus dem Projekt entfernt. Für die genannten Offline-Nachweise wurden keine tatsächlichen Geräteverbindungen hergestellt und keine Git-Commits erstellt.

### Symbolgröße in der Windows-Taskleiste

Nach dem Größenvergleich des Nutzers wurde der transparente Außenrand beim Verpacken ins ICO reduziert. Das gewählte PNG und das Motiv bleiben unverändert. Der quadratische Ausschnitt richtet sich nach der sichtbaren Kachel (Alpha ab 128), erhält ihre Proportionen und lässt einen kleinen Sicherheitsrand für die Kantenglättung stehen. Das ist Bestandteil von `tools/Build-Icon.ps1` und gilt für alle neun ICO-Größen.

Die sichtbare Fläche der 24-Pixel-Version wächst von 22×20 auf 24×22 Pixel, die der 32-Pixel-Version von 28×28 auf 32×30 Pixel (jeweils Alpha ab 128). Alle neun Größen wurden auf ausreichende Flächennutzung geprüft. Das größere ICO wird weiterhin in Fenster, Infobereich und EXE eingebettet.

Die neu veröffentlichte EXE bestand erneut die Prüfung der eingebetteten Fenster-/Infobereich-Symbole und die 26 Fokuswechsel. Das von Windows direkt aus dieser EXE ausgelesene 32-Pixel-Symbol besitzt bestätigt die neue sichtbare Größe 32×30. Die vorhandene Konfiguration und ihre Sicherung blieben beim Publish unverändert.

**Erneute Korrektur nach Nutzervergleich:** Die reine Randreduktion war optisch nicht ausreichend; die sichtbare Kachel blieb breiter als hoch. Die Kachel wurde mit Imagegen überarbeitet. Die ICO-Verpackung entfernt die Außenränder und passt die Grafik auf eine exakt quadratische, deckende blaue Grundfläche mit einheitlichen Rundungen ein. Die frühere 32×30-Einpassung ist damit ersetzt.

Alle neun ICO-Größen nutzen jetzt ihre volle quadratische Fläche, einschließlich 24×24 und 32×32 sichtbarer Pixel (Alpha ab 128); die gerundeten Ecken bleiben transparent beziehungsweise kantengeglättet. Zusätzlich prüft der Offline-Fenstertest jetzt das native HICON, das das tatsächlich erstellte WPF-Fenster per `WM_GETICON` an Windows liefert. Dieses Fenstersymbol und das direkt aus der veröffentlichten EXE extrahierte Symbol wurden jeweils mit 32×32 sichtbaren Pixeln bestätigt. Die 26 Fokuswechsel bestanden weiterhin. Konfiguration und Sicherung blieben unverändert.

## Technische Orientierung

### Zusätzliche QR-Kopplung (10.10.2026)

„Gerät hinzufügen“ bietet jetzt zuerst „Mit QR-Code koppeln“ an; Code-Kopplung und manuelle Verbindung bleiben verfügbar. Der QR-Code verwendet das Android-ADB-Format mit zufälliger Dienstkennung und temporärem Geheimnis. PhonePilot sucht ausschließlich den passenden `_adb-tls-pairing._tcp`-Dienst und übergibt das Geheimnis über die Standardeingabe. Es wird weder gespeichert noch protokolliert; auch vom Unterprozess ausgegebene Geheimnisse werden ausgeblendet. Die verwendete QR-Bibliothek QRCoder 1.8.0 wird mit ihrem MIT-Lizenzhinweis ausgeliefert.

Ein Versuch läuft höchstens drei Minuten. Neuer Code, Tabwechsel oder Schließen bricht den laufenden Versuch ab. Nach erfolgreicher Kopplung aktualisiert PhonePilot die Geräteerkennung. scrcpy startet weiterhin nur auf ausdrücklichen Wunsch. QR-Kopplung benötigt mDNS und ein gegenseitig erreichbares WLAN; als Alternative steht die bisherige Code-Kopplung bereit.

Alle 49 Tests bestanden, einschließlich eindeutiger temporärer Daten, exakter Dienstzuordnung, einmaliger Kopplung, Ablauf, Abbruch und Ausblendung sowohl sechsstelliger Codes als auch längerer QR-Geheimnisse aus echten Test-Unterprozessausgaben. Die veröffentlichte EXE bestand außerdem das Offline-Rendering der Oberfläche und des QR-Panels. Persönliche Konfiguration und Sicherung blieben unverändert (SHA-256-Vergleich). Ein Scan und die vollständige Kopplung mit einem echten Smartphone sind noch nicht geprüft.

Die [Offline-Ansicht des QR-Panels](screenshots/2026-10-10/08-qr-kopplung.png) stammt ausschließlich aus einer nicht aktiven Beispielsitzung.

**Nachtrag vom 10.10.2026:** Der Nutzer hat die funktionierende QR-Kopplung mit seinem Smartphone bestätigt. Damit liegt für diesen Ablauf zusätzlich eine praktische Rückmeldung vor; andere Geräte- und Netzwerkkombinationen bleiben separat zu prüfen. Aktuelle, erneut aus der Veröffentlichung gerenderte README-Ansichten stehen unter [Screenshots](screenshots/2026-10-10/README.md).

| Datei/Bereich | Verantwortung |
| --- | --- |
| `src/PhonePilot.Core/DeviceManager.cs` | Gerätebestand, Aktionen, Reservierungen, Wiederverbindung, Wartung |
| `src/PhonePilot.Core/AdbClient.cs` und `AdbParser.cs` | Lokale ADB-Kommandos, Erkennung, Kopplung, Identitätsnachweis |
| `src/PhonePilot.Core/ScrcpySessions.cs` und `ProcessRunner.cs` | Eigene Prozesse, Bereitschaft, Ausgabe, Zeitlimits und kontrolliertes Stoppen |
| `src/PhonePilot.Core/ConfigurationStore.cs` | Schema, Migration, Schutz, Sicherung und atomare Speicherung |
| `Desktop/MainWindow.xaml`, ViewModels und Dialoge | Bedienung, Datenbindung, globale Einstellungen und Infobereich |
| `tests/PhonePilot.Tests` | Wiederholbare Verhaltens- und Integrationsprüfungen |

Es wurden keine fremden Prozesse beendet und kein Git-Commit oder Pull Request erstellt. Persönliche Konfiguration, Build-Ausgaben und diagnostische Dateien bleiben außerhalb der Versionsverwaltung. Die mitgelieferten Werkzeugordner `adb/` und `scrcpy/` werden für den ersten PhonePilot-Stand zusammen mit dem Quellcode versioniert.
