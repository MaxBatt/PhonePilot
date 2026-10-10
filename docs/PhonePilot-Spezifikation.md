# PhonePilot – Detail-Spezifikation der Windows-GUI

**Version:** 1.0  
**Stand:** 10.10.2026  
**Status:** Vom Nutzer einschließlich aller technischen Detailvorschläge zur Implementierung freigegeben (10.10.2026)  
**Zielplattform:** Windows 11, Windows x64, C# / .NET 10  
**Technische Ausgangsbasis:** vorhandenes PhonePilot-Projekt einschließlich der lokalen Änderung für H.265 und 40 Mbit/s

## 1. Zweck, Verbindlichkeit und Freigabe

Dieses Dokument bündelt die im Gespräch vereinbarten Funktionen, das GUI-Konzept, die zugehörigen Mockups und die geplante technische Architektur. Es bildet die Grundlage für die spätere Implementierung im bestehenden Projekt.

Der Nutzer hat nach der Dokumentprüfung ausdrücklich alle Vorschläge bestätigt und die vollständige Implementierung beauftragt. Diese Freigabe umfasst Anwendungscode, Projektkonfiguration und Build-Verhalten. Dieses Dokument beschreibt den ursprünglichen Konzeptionsstand; den aktuellen Umsetzungs- und Prüfstand einschließlich der später ergänzten QR-Kopplung dokumentiert [Implementierung und Prüfung](Implementierung-und-Pruefung.md).

Kennzeichnung in diesem Dokument:

- **Festgelegt:** im Gespräch vereinbart oder Bestandteil der zuletzt bestätigten Mockups.
- **Vorläufige Testwerte:** abgestimmte Ausgangswerte, die mit echten Geräten überprüft werden müssen.
- **Technischer Detailvorschlag:** konkrete Ergänzung für eine vollständige Spezifikation; im Gespräch noch nicht einzeln festgelegt. Solche Vorschläge sind gesammelt in Abschnitt 20 aufgeführt und Bestandteil der bevorstehenden Dokumentprüfung.

Bei Widersprüchen zwischen einer Bildbeschriftung und einem funktionalen Ablauf ist der Text dieses Dokuments maßgeblich. Die Mockups bestimmen Gestaltung, Anordnung und visuelle Hierarchie. Beispieldaten in den Bildern sind keine Angaben über tatsächlich angeschlossene Geräte. Bildgenerierungs-Prompts dokumentieren die Entstehung der Bilder; sie sind keine zusätzlichen Produktanforderungen.

## 2. Ziel und Umfang der ersten GUI-Version

### 2.1 Festgelegtes Ziel

PhonePilot wird vom Launcher zu einer dauerhaft geöffneten, übersichtlichen Windows-Anwendung zur Verwaltung und Fernsteuerung mehrerer Android-Smartphones über WLAN weiterentwickelt.

- Typischer Einsatz: **ein bis zwei Geräte**.
- Auslegung: **maximal vier eigene scrcpy-Sitzungen gleichzeitig**.
- Bekannte Geräte dürfen die Anzahl vier überschreiten; die Grenze betrifft Sitzungen und keine festen Listenplätze.
- Jedes Gerät wird unabhängig ausgewählt, verbunden, gestartet, gestoppt und konfiguriert.
- Fernsteuerung erfolgt in **separaten scrcpy-Fenstern**.
- PhonePilot verwaltet ausschließlich selbst gestartete scrcpy-Prozesse.
- Der vorhandene Code wird schrittweise weiterverwendet und strukturiert; keine vollständige Neuentwicklung.

### 2.2 Enthaltene Funktionen

Geräteerkennung über mDNS und ADB, portable Werkzeugpakete, Kopplung per Code in der GUI, Einzel- und Sammelaktionen, individuelle Stream-Einstellungen, Leistungsprofile, Hintergrundaktualisierung, ADB-Wiederverbindung, Infobereich-Verhalten, automatisch gespeicherte Konfiguration, Diagnose und zugängliche Logs.

### 2.3 Nicht im Umfang dieser Version

- Eingebettete Liveansicht oder direkte Fernsteuerung innerhalb des PhonePilot-Hauptfensters.
- Verwaltung, Übernahme oder Beendigung fremd gestarteter scrcpy-Prozesse.
- Automatischer Start einer scrcpy-Sitzung beim Öffnen von PhonePilot.
- Automatischer Neustart einer beendeten scrcpy-Sitzung nach WLAN-Abbruch.
- Videoaufnahme, Screenshot-Schaltflächen oder eine Medienbibliothek.
- QR-Code-Kopplung, USB-Verwaltung, Emulator-Verwaltung und IPv6 als neue unterstützte Bedienabläufe.
- Cloud-Synchronisation, automatische Werkzeug-Updates und Autostart bei Windows-Anmeldung.
- Beliebige Shell-Befehle oder frei editierbare, ungeprüfte Zusatzargumente in der GUI.

Die vorhandene IPv4-basierte WLAN-Funktionalität bleibt die Ausgangsbasis. Nicht enthaltene Funktionen können später gesondert spezifiziert werden.

## 3. Visuelle Referenzen

Die ursprünglichen Mockups wurden beim Aufräumen aus dem Projekt entfernt. Als aktuelle Anschauung dienen die folgenden Screenshots der implementierten Oberfläche mit erfundenen Demo-Kennungen.

| Ansicht | Referenz |
| --- | --- |
| Hauptfenster / Steuerung | [Steuerung](screenshots/2026-10-10/01-steuerung.png) |
| Allgemeine Einstellungen | [Allgemeine Einstellungen](screenshots/2026-10-10/02-allgemein.png) |
| Geräteeinstellungen | [Einstellungen](screenshots/2026-10-10/03-einstellungen.png) |
| Erweitert | [Erweitert](screenshots/2026-10-10/04-erweitert.png) |
| Logs | [Logs](screenshots/2026-10-10/05-logs.png) |

Die fünf Ansichten gehören zu derselben Oberfläche: helle Windows-11-Gestaltung, ruhige Karten, zurückhaltende Rundungen, blaue Akzente, klare Typografie und nachvollziehbare Zustände. Es gibt keine reservierten leeren Gerätekacheln.

Die Bilder zeigen die helle Darstellung. Die Anwendung übernimmt standardmäßig das Windows-Farbschema; auch die [dunkle Darstellung](screenshots/2026-10-10/06-dunkel.png) ist umgesetzt.

## 4. Bestandsaufnahme der vorhandenen Anwendung

### 4.1 Architektur und Dateien

| Datei | Tatsächlicher Stand |
| --- | --- |
| [Program.cs](../Program.cs) | Statischer Launcher; Windows-Meldungsdialoge; Erkennung, Verbindung, Konfiguration, Prozessaufrufe und Logs in einer Datei |
| [PhonePilot.csproj](../PhonePilot.csproj) | `net10.0-windows`, `WinExe`, self-contained, Single-File-Publish, Copy-Target für lokale Werkzeuge |
| [build.ps1](../build.ps1) | Bereinigung von Ausgaben, Erhalt von `publish/config.json`, Publish und anschließender Programmstart |
| [README.md](../README.md) | Beschreibung des bisherigen Launchers und der manuellen ADB-Kopplung |

Die geprüften Quell- und Projektdateien des ZIPs stimmen mit dem Arbeitsordner überein. Gegenüber Git besteht bereits eine lokale Änderung am scrcpy-Aufruf: H.265 und 40 Mbit/s. Diese Änderung gehört zur technischen Ausgangsbasis und stammt nicht aus der Konzeptionsarbeit.

### 4.2 Bereits implementierte Funktionen

- Einzelinstanz-Sperre über `Local\PhonePilot.Launcher`.
- Lokale Pfade für ADB, scrcpy und `scrcpy-server`; Prüfung auf fehlende Programme.
- Start des ADB-Servers und kontrollierte Prozessaufrufe ohne Konsolenfenster.
- mDNS-Erkennung für `_adb-tls-connect._tcp` mit Proto-Text-Auswertung und Text-Fallback.
- Dynamische IPv4-Adressen und Ports; stabile Geräteauswahl über Seriennummer.
- Schnellpfad für das gespeicherte Gerät über bestehende ADB-Verbindungen oder mDNS-Snapshot.
- Zusätzliche Identitätsprüfung über `getprop ro.serialno`.
- Explizite Gerätezuordnung beim scrcpy-Start mit `--serial`.
- Start mit ausgeschaltetem Smartphone-Display, H.265 und 40 Mbit/s.
- Speicherung einer Seriennummer in `config.json` über temporäre Datei und anschließendes Ersetzen.
- Zeitlimits, Fehlerdialoge und Protokollierung nach `%TEMP%\PhonePilot.log`.
- Veröffentlichung mit eigenen `adb`- und `scrcpy`-Unterordnern.

### 4.3 Gezielte Weiterentwicklung

Die bisherige Auswahl eines einzelnen Geräts wird durch eine dauerhafte Geräteliste ersetzt. Die Prozessausführung muss laufende Ausgaben liefern und mehrere eigene Sitzungen unabhängig verwalten können. Die einmalige Suche wird zu koordinierter Hintergrundaktualisierung. Die vorhandenen Parser und Identitätsprüfungen werden übernommen, geprüft und bei Bedarf angepasst.

Ein automatisches `adb kill-server` nach erfolgloser Erkennung wird aus dem normalen Bedienablauf entfernt. Ein ADB-Server-Neustart bleibt eine ausdrücklich ausgelöste Diagnoseaktion.

## 5. Hauptfenster und Navigation

### 5.1 Globale Werkzeugleiste

Die obere Leiste enthält:

1. PhonePilot-Identität und kurze Beschreibung.
2. **Aktualisieren**.
3. **Alle verbinden**.
4. **Alle starten**.
5. **Alle stoppen**.
6. Zahnrad für **Allgemeine Einstellungen**.

Es gibt in dieser Leiste weder einen Schalter für automatische Aktualisierung noch eine sichtbare Intervallauswahl. Diese Optionen stehen ausschließlich in den allgemeinen Einstellungen.

### 5.2 Linker Bereich: Geräte

- Überschrift mit Anzahl der angezeigten Geräte und Schaltfläche **Hinzufügen**.
- Pro Gerät: Anzeigename, Seriennummer und kompakte ADB-/scrcpy-Anzeigen mit Symbol/Farbe und Text.
- Kleine Geräteabbildungen sind dekorativ; ein neutrales Symbol dient als Fallback.
- Ausgewähltes Gerät wird mit blauem Hintergrund beziehungsweise Akzent markiert.
- Bekannte, derzeit nicht erreichbare Geräte bleiben sichtbar.
- Eine Aktualisierung erhält Auswahl und Listenposition, solange das ausgewählte Gerät existiert.
- Keine doppelten Einträge für dieselbe Seriennummer, auch bei mehreren gefundenen Endpunkten.
- Sitzungszähler, beispielsweise **1 von 4 Sitzungen aktiv**.
- Bei langer Geräteliste wird gescrollt; keine starren vier Plätze.

**Technischer Detailvorschlag:** bekannte Geräte behalten eine stabile Reihenfolge; neu gefundene Kandidaten werden danach angeordnet. Temporäre, nie gespeicherte Funde dürfen nach eindeutig bestätigtem Verschwinden entfallen. Ein gerade ausgewählter Kandidat bleibt bis zum Auswahlwechsel als nicht erreichbar sichtbar.

### 5.3 Rechter Bereich: ausgewähltes Gerät

Kopfbereich mit Anzeigename, Bearbeiten-Symbol, verfügbaren Android-Informationen, Seriennummer und Kopieraktion sowie kompaktem Gesamtstatus. Nicht verfügbare Metadaten werden als unbekannt dargestellt und verhindern keine Sitzung.

Tabs in fester Reihenfolge:

1. **Steuerung**.
2. **Einstellungen**.
3. **Erweitert**.
4. **Logs**.

Die Geräteeinstellungen gelten immer für das sichtbar ausgewählte Gerät. Ein Wechsel der Auswahl stoppt oder startet keine Sitzung.

### 5.4 Leere und eingeschränkte Zustände

- Ohne Geräte: Hinweis auf WLAN-Debugging, **Gerät hinzufügen** und **Aktualisieren**; keine funktionslosen Detailkarten.
- Ohne Auswahl: Aufforderung zur Geräteauswahl.
- Bei fehlenden Werkzeugen: sichtbarer Hinweis mit erwartetem Ordner; betroffene Aktionen sind deaktiviert. Bekannte Einträge und Einstellungen bleiben zugänglich.
- Während einer Aktion: Fortschrittszustand direkt beim betroffenen Gerät; die restliche Oberfläche bleibt bedienbar.

## 6. Statusmodell und visuelle Bedeutung

### 6.1 Unabhängige Statusbereiche

| Bereich | Zustände und Bedeutung |
| --- | --- |
| mDNS | Gefunden; nicht gefunden; Suche läuft; Abfrage fehlgeschlagen; noch nicht geprüft |
| ADB | Getrennt; wird verbunden; verbunden; wird getrennt; offline; nicht autorisiert; Fehler; noch nicht geprüft |
| scrcpy | Gestoppt; startet; läuft; wird beendet; fehlgeschlagen |

Nicht jeder technische Unterzustand benötigt einen eigenen Eintrag in der Geräteliste. Die Detailkarte muss die Ursache trotzdem verständlich benennen können.

- **Grün:** Verbindung steht beziehungsweise Sitzung läuft.
- **Gelb:** Vorgang läuft, Wiederverbindung wartet oder Handlungsbedarf besteht.
- **Grau:** Inaktiv, unbekannt oder regulär nicht erreichbar.
- **Rot:** Fehlgeschlagene Aktion beziehungsweise bestätigter Fehler.

Farbe ist nie die einzige Information. Text und geeignete Symbole bleiben vorhanden.

### 6.2 Regeln zur Auswertung

- Kein mDNS-Treffer bedeutet nicht automatisch, dass eine bestehende ADB-Verbindung ausgefallen ist.
- Eine fehlgeschlagene Statusabfrage wird als Abfragefehler angezeigt, nicht als bewiesene Trennung aller Geräte.
- Ein mDNS-Dienst beweist weder eine vorhandene Kopplung noch die erfolgreiche ADB-Verbindung.
- Ein gestarteter Prozess wird zunächst als **Startet** geführt. **Läuft** benötigt ein zur mitgelieferten scrcpy-Version passendes Bereitschaftssignal; die genaue Erkennung wird bei der Implementierung geprüft.
- Ein beendeter Prozess darf nicht als laufend angezeigt werden. Reguläres Fenster-Schließen ergibt **Gestoppt**, ein unerwartetes Fehlerende einen verständlichen Fehlerstatus.
- Die Kopfmarkierung fasst den Zustand zusammen; die drei Einzelbereiche bleiben die maßgebliche Diagnose.
- Konfigurierte Bitrate und FPS-Limit werden nicht als gemessene Übertragungsrate oder tatsächlich erreichte FPS ausgegeben.

## 7. Erkennung und automatische Aktualisierung

### 7.1 Festgelegtes Verhalten

- Beim Öffnen erscheint die Übersicht; eine erste Gerätesuche startet im Hintergrund.
- Automatische Aktualisierung ist standardmäßig **aktiv**, Standardintervall **5 Sekunden**.
- Aktivierung und Intervall sind global konfigurierbar und werden gespeichert.
- Es läuft höchstens eine koordinierte Erkennungsrunde gleichzeitig.
- Ein globaler manueller Refresh prüft alle Geräte; die Refresh-Aktion in der Verbindungsstatuskarte prüft das ausgewählte Gerät.
- Während einer laufenden Runde werden weitere Refresh-Anforderungen zusammengefasst statt gestapelt. Ein manueller Wunsch wird zeitnah berücksichtigt.
- ADB-Geräteliste und mDNS werden zusammengeführt; Endpunkte werden nicht dauerhaft als Geräteidentität verwendet.
- Aktionen wie Verbinden oder Starten dürfen unabhängig vom periodischen Intervall aktuelle Informationen anfordern.

### 7.2 Intervall und deaktivierte Aktualisierung

**Technischer Detailvorschlag:** Dropdown mit 2, 5, 10, 15, 30 und 60 Sekunden. 5 Sekunden bleibt der Standard. Bei Intervalländerung wird der Zeitplan neu gesetzt. Langsame Abfragen erzeugen keine überlappenden Runden; das Intervall ist kein garantiertes Zeitmaß bis zum Abschluss.

**Technischer Detailvorschlag:** Deaktivieren stoppt periodische Geräteabfragen und davon abhängige automatische Wiederverbindungsversuche. Manuelle Aktionen und die Überwachung eigener Prozesse laufen weiter. Erneutes Aktivieren löst eine zeitnahe Runde aus. Der letzte Prüfzeitpunkt bleibt sichtbar, damit alte Informationen erkennbar sind.

Der bestehende sechsekündige Tracking-Aufruf wird nicht einfach alle fünf Sekunden neu gestartet. Die Implementierung prüft geeignete Snapshots beziehungsweise einen sauber verwalteten Tracking-Stream mit korrekter Behandlung von Aktualisierungen und verschwundenen Diensten. Der Text-Fallback bleibt erhalten, soweit die mitgelieferte ADB-Version ihn benötigt.

## 8. Geräte hinzufügen, koppeln und vergessen

### 8.1 Kopplung per Code

**Hinzufügen** öffnet einen kleinen Dialog mit kurzer Anleitung zum Smartphone-Menü **WLAN-Debugging → Gerät mit Kopplungscode koppeln**.

Eingaben:

- IPv4-Adresse.
- Kopplungsport.
- Kopplungscode.

Aktionen: **Koppeln** und **Abbrechen**. Die Kopplung läuft im Hintergrund mit sichtbarem Fortschritt. Ungültige Eingaben werden vor dem Aufruf erklärt. Der Kopplungscode erscheint weder in Logs noch in dauerhaft gespeicherten Einstellungen; er wird nicht als sichtbares Prozessargument weitergereicht.

Nach erfolgreicher Kopplung werden aktuelle Verbindungsdienste neu gesucht. Kopplungsport und Verbindungsport dürfen nicht verwechselt werden. Die GUI meldet getrennt, ob die Kopplung erfolgreich war und ob anschließend eine Verbindung hergestellt werden konnte. Kopplung allein startet keine scrcpy-Sitzung.

Falls mDNS nach der Kopplung keinen Verbindungsdienst liefert, ist im Hinzufügen-/Verbindungsablauf eine manuelle Eingabe der aktuell am Smartphone angezeigten Verbindungsadresse möglich. Auch diese Adresse bleibt flüchtig. Die Zuordnung zu einem gespeicherten Gerät erfolgt erst nach bestätigter Seriennummer.

Die Kopplung liegt weiterhin im Verantwortungsbereich von ADB und des Windows-Benutzers. PhonePilot verschiebt oder kopiert keine ADB-Schlüssel. Auf einem anderen PC beziehungsweise unter einem anderen Windows-Benutzer kann eine erneute Kopplung erforderlich sein.

### 8.2 Bekannte und gefundene Geräte

Unbekannte Funde dürfen in der Liste erscheinen, werden aber nicht allein wegen einer Netzwerkankündigung dauerhaft als verifiziertes Gerät übernommen. Nach erfolgreicher, identitätsgeprüfter Verbindung oder einem entsprechenden erfolgreichen Hinzufügen wird die stabile Gerätekennung gespeichert.

Die Hardware-Seriennummer ist maßgeblich. Eine IP-Adresse oder ein gleichlautender Gerätename genügt nicht. Nicht autorisierte Geräte können als Kandidaten mit erklärtem Status erscheinen; ohne erfolgreichen Identitätsnachweis erhalten sie keine bestehende Gerätekonfiguration.

### 8.3 Gerät vergessen

**Gerät vergessen…** entfernt den gespeicherten Eintrag und seine individuellen Einstellungen. Die ADB-Kopplung bleibt bestehen.

**Technischer Detailvorschlag:** Bestätigungsdialog benennt das Gerät und die zu entfernenden Einstellungen. Während einer eigenen aktiven Sitzung muss diese zuerst beendet werden; der Dialog bietet die entsprechende Aktion an. Danach wird der Eintrag entfernt. Ein weiterhin auffindbares Gerät darf anschließend wieder als neuer Fund erscheinen. Das Vergessen ist keine Netzwerksperre und kein Entkoppeln.

## 9. Einzelaktionen und globale Aktionen

### 9.1 Einzelaktionen

| Aktion | Verhalten |
| --- | --- |
| Verbinden | Aktuellen Endpunkt ermitteln, gezielt verbinden, Seriennummer bestätigen; keine scrcpy-Sitzung starten |
| Starten | Bei Bedarf verbinden, Sitzungskapazität reservieren, gespeicherte Optionen verwenden und eigenes scrcpy-Fenster öffnen |
| Stoppen | Eigene scrcpy-Sitzung dieses Geräts beenden; bestehende ADB-Verbindung erhalten |
| Verbindung trennen | Eigene Sitzung gegebenenfalls nach verständlicher Bestätigung beenden, den zugehörigen WLAN-Transport gezielt trennen, eigene automatische Wiederverbindung unterdrücken |
| scrcpy-Fenster anzeigen | Vorhandenes eigenes Fenster wiederherstellen und aktivieren; keinen zweiten Prozess starten |
| Aktualisieren | Status und aktuelle Endpunkte dieses Geräts gezielt neu prüfen |

**Starten** ist bei laufender oder startender Sitzung deaktiviert. **Stoppen** ist nur bei eigener vorhandener Sitzung verfügbar. Bei einem verbundenen Gerät ist keine redundante Aktion **Gerät neu verbinden** nötig. Beim getrennten Gerät steht **Verbinden** an der passenden Stelle der Aktionsleiste zur Verfügung.

Pro Gerät wird höchstens eine zustandsverändernde Aktion gleichzeitig ausgeführt. Mehrfachklicks starten keine Duplikate. Stoppen, Abbrechen und bewusstes Trennen können einen noch laufenden eigenen Start-/Verbindungsversuch kontrolliert beenden.

### 9.2 Globale Aktionen

| Aktion | Zielmenge und Verhalten |
| --- | --- |
| Alle verbinden | Bekannte, aktuell erreichbare Geräte; bereits verbundene Geräte überspringen; keine automatische Übernahme fremder Funde und kein scrcpy-Start |
| Alle starten | Bekannte, erreichbare Geräte ohne eigene laufende Sitzung; bei Bedarf zuerst verbinden; verfügbare Sitzungsplätze berücksichtigen |
| Alle stoppen | Sämtliche eigenen laufenden oder startenden Sitzungen; ADB-Verbindungen erhalten |

Fehler werden pro Gerät erfasst. Ein Fehler blockiert nicht die restlichen Geräte. Nach Sammelaktionen erscheint eine kurze Ergebnisübersicht mit Erfolgen, übersprungenen Geräten und Fehlern; Details stehen in Logs. Nicht erreichbare bekannte Geräte bleiben sichtbar und werden nachvollziehbar übersprungen.

**Technischer Detailvorschlag:** Zielmenge beim Klick als Snapshot festlegen. Wenn mehr Startkandidaten als freie Plätze existieren, Geräte in stabiler Listenreihenfolge starten und übrige als **Sitzungslimit erreicht** melden. Ein ausdrücklicher Sammelbefehl hebt eine vorherige manuelle Trennung für die von ihm betroffenen Geräte auf.

### 9.3 Sitzungsgrenze

- Höchstens eine eigene Sitzung pro Seriennummer.
- Höchstens vier eigene Sitzungen insgesamt, einschließlich bereits reservierter Startplätze.
- Reservierung und Freigabe müssen auch bei parallelen Klicks und Sammelaktionen zuverlässig sein.
- Fehlgeschlagener Start, Abbruch und Prozessende geben den Platz wieder frei.
- Ein fünfter Start erzeugt keinen Prozess, sondern einen verständlichen Hinweis.
- Ein verbundenes Gerät ohne scrcpy zählt nicht als aktive Sitzung.

## 10. WLAN-Abbrüche und Wiederverbindung

1. Ein zuvor verbundenes Gerät fällt aus; die Liste bleibt erhalten und der Zustand wird aktualisiert.
2. Bei aktivierter Wiederverbindung wird nach seiner Rückkehr der aktuelle Endpunkt neu bestimmt.
3. Die Identität wird erneut geprüft und die ADB-Verbindung wiederhergestellt.
4. Eine beendete scrcpy-Sitzung bleibt beendet, bis **Starten** oder **Alle starten** ausgelöst wird.

Das Verhalten ist pro Gerät über **ADB nach WLAN-Abbruch erneut verbinden** konfigurierbar, standardmäßig aktiv. Nach bewusstem **Verbindung trennen** erfolgen für dieses Gerät keine eigenen automatischen Verbindungsversuche bis zu einem erneuten ausdrücklichen Verbinden/Starten. Dieser Wiederverbindungswunsch ist ein Laufzeitzustand; ein Programmneustart setzt keine frühere Sitzung automatisch fort.

Beim Start der Anwendung können bereits bestehende ADB-Verbindungen erkannt werden. Die gespeicherte Geräteauswahl allein ist kein Auftrag, getrennte Geräte zu verbinden oder Sitzungen zu starten.

**Technischer Detailvorschlag:** Wiederholungsversuche mit wachsendem Abstand, beispielsweise 5, 10, 20 und höchstens 30 Sekunden; keine überlappenden Versuche und keine Dialogserie. Nach einer bestätigten Rückkehr ist eine zeitnahe Prüfung möglich. Fehlende Kopplung oder Autorisierung erzeugt Handlungsbedarf statt endloser schneller Wiederholung.

PhonePilot steuert eigene Verbindungsversuche. Wegen des gemeinsam genutzten ADB-Servers kann es nicht verhindern, dass ein anderes Programm oder ADB selbst unabhängig eine Verbindung aufbaut. Fremde scrcpy-Prozesse werden dabei weiterhin nicht verwaltet.

## 11. Tab Steuerung: Stream und Schnelloptionen

### 11.1 Verbindungsstatus

Karte mit WLAN/mDNS, ADB und scrcpy, gerätespezifischer Aktualisierung sowie **scrcpy-Fenster anzeigen** bei laufender eigener Sitzung. Der scrcpy-Status lautet beispielsweise **Läuft · eigenes Fenster** und enthält keine fingierten Messwerte.

### 11.2 Leistungsprofile

Die Profile setzen die vier Kerneinstellungen Bildkante, Videobitrate, FPS-Limit und Videocodec. Audio-, Display- und Fensteroptionen werden dadurch nicht überraschend verändert. Eine manuelle Änderung einer Kerneinstellung wechselt die Auswahl zu **Benutzerdefiniert**.

| Profil | Maximale Bildkante | Video-Bitrate | FPS-Limit | Codec | Status |
| --- | --- | --- | --- | --- | --- |
| Qualität | 2560 px | 20 Mbit/s | 60 | H.265 | Vorläufiger Testwert |
| Ausgewogen | 1920 px | 8 Mbit/s | 60 | H.264 | Vorläufiger Testwert; Standard für neue Geräte |
| Schnell | 1280 px | 4 Mbit/s | 30 | H.264 | Vorläufiger Testwert |
| Benutzerdefiniert | Individuell | Individuell | Individuell | Individuell | Gespeicherte Gerätewerte |

Die Profile sind keine Garantie für tatsächlich erreichte Bildrate oder Verzögerung. Sie werden mit ein bis vier Pixel-Geräten und unterschiedlichen WLAN-Bedingungen geprüft. Nicht unterstützte Codecs/Encoder müssen nachvollziehbar gemeldet werden; es gibt keinen stillen Wechsel der gespeicherten Auswahl.

### 11.3 Eingaben und Bedeutung

| Feld | Bedeutung und spätere Argumentzuordnung |
| --- | --- |
| Maximale Bildkante | Längere Bildkante begrenzen, Seitenverhältnis erhalten; `--max-size`; Original/unbegrenzt bedeutet Option weglassen |
| Video-Bitrate | Konfigurierter Zielwert in Mbit/s; `--video-bit-rate`; keine Live-Messung |
| FPS-Limit | Obergrenze; `--max-fps`; unbegrenzt bedeutet Option weglassen |
| Video-Codec | Zunächst H.264 und H.265; `--video-codec`; Unterstützung wird gerätebezogen geprüft |

Die Oberfläche verwendet nicht **1920 × 1080 (FHD)** als Synonym für `--max-size=1920`. Die tatsächliche Bildgröße hängt unter anderem vom Seitenverhältnis ab.

**Technischer Detailvorschlag:** Auswahlwerte für Bildkante Original, 1280, 1920, 2560 und 3840 px; für FPS unbegrenzt, 30, 60, 90 und 120. Individuelle positive Werte sind über eine validierte benutzerdefinierte Eingabe möglich. Bitrate ist positiv und wird ohne localeabhängige Argumentfehler in den Werkzeugwert umgerechnet. Endgültige Grenzen werden an der mitgelieferten Version geprüft.

### 11.4 Schnelloptionen

| Option | Standard bei neuen Geräten | Bedeutung |
| --- | --- | --- |
| Display am Handy ausschalten | Aktiv | Start mit `-S` |
| Audio übertragen | Aktiv | Audio nicht deaktivieren; bei Aus explizit `--no-audio` |
| Immer im Vordergrund | Inaktiv | `--always-on-top` beim Sitzungsstart |

Audio hängt von Gerät und Android-Version ab. Die GUI verspricht keine erfolgreiche Audioübertragung allein aufgrund des gesetzten Häkchens. Ein Audiofehler soll nach Möglichkeit Video und Steuerung nicht unnötig verhindern.

Es gibt einen Link **Weitere Optionen unter Einstellungen**. Seltene Optionen werden nicht zusätzlich als Schnelloptionen angeboten.

### 11.5 Speichern und Wirksamkeit

Gültige Änderungen werden automatisch für das Gerät gespeichert. Es gibt keinen Speichern-/Übernehmen-Button. Ungültige Zwischenwerte überschreiben nicht die letzte gültige Konfiguration. Ein noch bearbeitetes Textfeld wird bei Fokuswechsel beziehungsweise abgeschlossener Eingabe übernommen.

Laufende Sitzungen verwenden einen unveränderlichen Snapshot ihrer Startoptionen. Änderungen werden für den nächsten Start gespeichert und unterbrechen keine aktive Sitzung. Sichtbarer Hinweis: **Stream-Änderungen gelten beim nächsten Start.**

Die Anzeige unterscheidet bei Bedarf zwischen aktiver Sitzungskonfiguration und bereits gespeicherten nächsten Einstellungen. Die Statusleiste darf bei fehlgeschlagenem Schreiben nicht behaupten, Änderungen seien gespeichert.

## 12. Tab Einstellungen: Gerät, Fenster und Verhalten

### 12.1 Gerät

- **Anzeigename:** frei bearbeitbarer Name; initial verfügbarer Gerätename/Modell oder neutraler Fallback.
- **Seriennummer:** schreibgeschützt mit Kopieraktion.
- Anzeigenamenänderung ändert die Identität nicht und wird nicht bei jeder Erkennung durch den Android-Namen überschrieben.

### 12.2 scrcpy-Fenster

- **Im Vollbild starten**, Standard inaktiv, Zuordnung zu `--fullscreen`.
- **Fenster ohne Rahmen**, Standard inaktiv, Zuordnung zu `--window-borderless`.
- **Fenstertitel**, standardmäßig aus dem Anzeigenamen abgeleitet; Zuordnung zu `--window-title`.
- **Immer im Vordergrund** bleibt als Schnelloption auf Steuerung und wird hier nicht doppelt angeboten.

Vorschlag für das Datenmodell: Ein leerer eigener Fenstertitel bedeutet **Anzeigename verwenden**. Die GUI zeigt diesen Standard verständlich an, ohne den abgeleiteten Text als unabhängig gespeicherten eigenen Titel auszugeben. Änderungen an Fensteroptionen gelten beim nächsten Sitzungsstart.

### 12.3 Wiederverbindung

Checkbox **ADB nach WLAN-Abbruch erneut verbinden**, Standard aktiv. Erläuterungen nennen die Wirkung des bewussten Trennens und den weiterhin manuellen scrcpy-Neustart. Diese Option betrifft ausschließlich die ADB-Wiederverbindung und ist kein Sitzungs-Autostart.

### 12.4 Gerät verwalten

**Gerät vergessen…** mit Erklärung, dass gespeicherter Eintrag und Einstellungen entfernt werden, die ADB-Kopplung aber bestehen bleibt. Bestätigungs- und Sitzungsverhalten siehe Abschnitt 8.3.

## 13. Tab Erweitert

### 13.1 Erweiterte Stream-Optionen

- **Video-Encoder:** Standard automatisch; konkrete Encoder nur aus den Fähigkeiten des ausgewählten Geräts anbieten.
- **Bildausrichtung:** Standard automatisch; konkrete Orientierungswerte und die Zuordnung zu scrcpy werden mit der mitgelieferten Version geprüft.
- Änderungen gelten beim nächsten Start; normale Stream-Regler werden hier nicht dupliziert.

**Technischer Detailvorschlag:** Bildausrichtung bezieht sich auf die Darstellung im scrcpy-Fenster, nicht auf eine erzwungene Rotation der Android-Anwendung. Für Version 1 genügen automatisch und die vier Hauptorientierungen. Encoderlisten werden gezielt und mit Cache abgefragt, nicht jede Aktualisierungsrunde.

### 13.2 Gerätediagnose

- **Verbindung prüfen**: aktuelle Erreichbarkeit, ADB-Zustand und Identität gezielt prüfen.
- **Geräteinformationen lesen**: verfügbare Modell-/Android-/Build-Informationen erneut abfragen.
- **Ergebnisse in Logs anzeigen**: zum entsprechenden Logbereich wechseln.

### 13.3 Mitgelieferte Werkzeuge

Verfügbarkeit von ADB und scrcpy sichtbar darstellen; **Werkzeugordner öffnen** führt zum lokalen Werkzeugbereich. Detailprüfung berücksichtigt neben der EXE benötigte Paketdateien. Versionen dürfen nur angezeigt werden, wenn sie tatsächlich ermittelt wurden; Versionsnummern in Mockups werden nicht erfunden.

### 13.4 ADB-Server neu starten

Eine ausdrücklich ausgelöste Diagnoseaktion mit verständlicher Bestätigung. Sie betrifft den gemeinsam genutzten ADB-Server und kann alle ADB-Verbindungen auf dem PC sowie laufende Sitzungen unterbrechen. Der im Mockup gelb hervorgehobene Hinweis bleibt sichtbar.

Vor Ausführung werden eigene laufende Start-/Verbindungsversuche koordiniert abgebrochen und eigene aktive Sitzungen kontrolliert behandelt. Nach dem Neustart wird neu gesucht. Beendete scrcpy-Sitzungen starten auch dann nicht automatisch. Fremde scrcpy-Prozesse werden nicht direkt beendet; ihre Verbindung kann durch den Server-Neustart trotzdem betroffen sein.

## 14. Tab Logs

- Filter **Bereich**: dieses Gerät oder alle Meldungen, einschließlich allgemeiner ADB-/Anwendungsmeldungen.
- Filter **Quelle**: alle Quellen sowie ADB, mDNS, scrcpy und Anwendung.
- Filter **Level**: mindestens Info, Warnung und Fehler; technische Details können bei Bedarf ergänzt werden.
- Suche über die angezeigten Meldungen.
- Spalten **Zeit**, **Level**, **Quelle**, **Meldung**.
- **Automatisch scrollen**, **Kopieren**, **Exportieren…**, **Ansicht leeren**.
- Live-Ausgaben erscheinen während laufender Prozesse, nicht erst bei deren Ende.
- Jeder relevante Eintrag erhält eine stabile Gerätezuordnung beziehungsweise die Kennzeichnung als allgemeine Meldung.
- **Ansicht leeren** leert die Anzeige, ohne stillschweigend die gespeicherten Protokolldateien zu löschen.
- Kein wiederholter Dialog für normale WLAN-Abbrüche; Meldungen direkt am Gerät und in Logs.
- Kopplungscodes und andere Geheimnisse werden nicht protokolliert.

**Technischer Detailvorschlag:** automatisch scrollen standardmäßig aktiv; Export umfasst die aktuell gefilterte Ansicht als UTF-8-Text mit Zeit, Quelle, Level und Gerätekennung. Die Anzeige puffert höchstens 5000 Einträge, damit lange Laufzeiten die Oberfläche nicht unbegrenzt wachsen lassen. Aufbewahrungsvorschlag: `%TEMP%\PhonePilot\Logs`, begrenzte Rotation mit drei Dateien zu je 5 MiB. Bestehende Diagnoseinhalte werden nicht beim Öffnen der GUI blind überschrieben.

Logs können Seriennummern und flüchtige Netzwerkadressen zur Diagnose enthalten. Das Verbot dauerhaft gespeicherter Endpunkte betrifft die Gerätekonfiguration und die Verbindungsstrategie; historische Logadressen werden nie als aktuelle Verbindungsvorgabe verwendet.

## 15. Allgemeine Einstellungen und Infobereich

### 15.1 Globaler Dialog

Über das Zahnrad erreichbar, unabhängig vom ausgewählten Gerät. Änderungen werden automatisch gespeichert. Der Dialog besitzt **Schließen**, keine Speichern-/Übernehmen-Schaltfläche.

| Gruppe | Einstellung | Standard |
| --- | --- | --- |
| Geräteerkennung | Automatisch aktualisieren | Aktiv |
| Geräteerkennung | Aktualisierungsintervall | 5 Sekunden |
| Darstellung | Farbschema | Windows-Einstellung übernehmen |
| Fenster schließen | Verhalten bei aktiven Sitzungen | Nachfragen |

**Technischer Detailvorschlag:** Farbschema-Auswahl Windows-Einstellung übernehmen, Hell, Dunkel. Schließverhalten-Auswahl Nachfragen, Im Infobereich weiterlaufen, Sitzungen beenden und schließen.

### 15.2 Schließen mit aktiven Sitzungen

Beim ersten Schließen mit aktiven eigenen Sitzungen erscheinen:

- **Im Infobereich weiterlaufen**.
- **Sitzungen beenden und schließen**.
- **Abbrechen**.
- Möglichkeit **Entscheidung merken**.

Abbrechen schließt das Hauptfenster nicht und ändert keine gespeicherte Schließentscheidung. Eine gemerkte Entscheidung lässt sich in den allgemeinen Einstellungen ändern. Schließen ohne aktive Sitzung beendet die Anwendung regulär; ein laufender eigener Startversuch wird dabei ebenfalls berücksichtigt und nicht unbeaufsichtigt zurückgelassen.

### 15.3 Betrieb im Infobereich

Hauptfenster wird ausgeblendet, eigene Sitzungen und die konfigurierte Hintergrundarbeit laufen weiter. Das Symbol bietet mindestens **PhonePilot öffnen**, **Alle Sitzungen stoppen** und **Beenden**. Öffnen stellt dasselbe Hauptfenster wieder her; kein weiterer Launcher wird gestartet.

**Technischer Detailvorschlag:** Ein ausdrückliches **Beenden** bei aktiven Sitzungen verlangt eine klare Bestätigung und beendet anschließend diese Sitzungen. Die gemerkte Entscheidung zum bloßen Fensterschließen darf einen ausdrücklichen Beenden-Befehl nicht wieder in ein unsichtbares Weiterlaufen umwandeln.

Beim vollständigen Beenden werden eigene Hintergrundaufgaben und Prozesse kontrolliert abgeschlossen beziehungsweise beendet. Der gemeinsame ADB-Server wird nicht regulär beendet. Das Schließen eines einzelnen scrcpy-Fensters lässt PhonePilot und andere Sitzungen weiterlaufen.

## 16. Persistente Konfiguration und Migration

### 16.1 Festgelegte Inhalte

Eine lesbare `config.json` bleibt neben `PhonePilot.exe`. Gespeichert werden Formatversion, globale Einstellungen, stabile Gerätekennungen und individuelle Einstellungen. IP-Adressen, ADB-Verbindungsports, Kopplungsports, Kopplungscodes, Prozess-IDs und laufende Sitzungszustände gehören nicht hinein.

Speichern erfolgt atomar über eine temporäre Datei und anschließendes Ersetzen. Schreibvorgänge werden zentral koordiniert, damit parallele Geräteänderungen keine Einstellungen überschreiben. Ein Schreibfehler zeigt **Nicht gespeichert** mit verständlicher Ursache; es gibt keine falsche Erfolgsbestätigung.

### 16.2 Vorgeschlagenes Format

Das folgende JSON ist ein **technischer Detailvorschlag**, keine bereits implementierte Datei. Die Beispielkennung stammt aus dem Mockup.

```json
{
  "schemaVersion": 1,
  "application": {
    "autoRefreshEnabled": true,
    "refreshIntervalSeconds": 5,
    "theme": "system",
    "closeBehavior": "ask"
  },
  "selectedDeviceSerial": "DEMO-PHONE-01",
  "devices": [
    {
      "serial": "DEMO-PHONE-01",
      "displayName": "Pixel 10 Pro",
      "reconnectAdb": true,
      "stream": {
        "preset": "balanced",
        "maxSize": 1920,
        "videoBitRateMbps": 8,
        "maxFps": 60,
        "videoCodec": "h264",
        "turnScreenOff": true,
        "audioEnabled": true,
        "alwaysOnTop": false,
        "fullscreen": false,
        "borderless": false,
        "windowTitleOverride": null,
        "videoEncoder": null,
        "displayOrientation": "auto"
      }
    }
  ]
}
```

`null` bei Bildkante oder FPS-Limit bedeutet keine ausdrücklich gesetzte Begrenzung. `null` beim Encoder bedeutet automatische Auswahl. Ein leerer eigener Fenstertitel wird wie `null` behandelt. Es werden tatsächliche Werte zusammen mit dem Profilnamen gespeichert, damit spätere Profiländerungen nicht bestehende Geräte unbemerkt umkonfigurieren.

### 16.3 Übernahme der bisherigen Konfiguration

Das alte Format mit `DeviceSerial` wird erkannt und in einen Geräteeintrag übernommen. Der Anzeigename wird bei Verfügbarkeit ergänzt. Für dieses migrierte Gerät gelten die bisherigen Startoptionen:

- Profil Benutzerdefiniert.
- H.265 und 40 Mbit/s.
- Smartphone-Display ausschalten aktiv.
- Keine zusätzliche max-size- oder FPS-Begrenzung.
- Audio entsprechend bisheriger scrcpy-Standardnutzung aktiviert.
- Kein Vollbild, kein rahmenloses Fenster und kein Always-on-top durch neu eingeführte Defaults.

Die bisherige Geräteidentität geht nicht verloren. Die bewusste Änderung zum GUI-Start ohne automatisch gestartete Sitzung bleibt davon unberührt. Neue Geräte erhalten das Profil Ausgewogen.

**Technischer Detailvorschlag:** Vor dem ersten erfolgreichen Schreiben im neuen Format eine Sicherung der alten Konfiguration anlegen, ohne eine bestehende Sicherung zu überschreiben. Defekte oder künftig unbekannte Formate nicht still überschreiben; Original erhalten, verständlich melden und eine bewusste Wiederherstellung ermöglichen. Fehlende optionale Felder mit dokumentierten Standards ergänzen, ungültige Werte sichtbar behandeln.

## 17. Technische Architektur

### 17.1 Technologie und Trennung

Geplante GUI-Basis: **WPF auf .NET 10**, Entwicklung weiterhin mit VS Code und .NET CLI. Ein schlankes MVVM-Modell trennt Anzeige und Benutzeraktionen von Geräte-, Verbindungs- und Prozesslogik. Es werden nur benötigte Abhängigkeiten eingeführt; die konkrete Hilfsbibliothekswahl ist noch keine Produktentscheidung.

| Baustein | Verantwortlichkeit | Wiederverwendung |
| --- | --- | --- |
| WPF-Views / ViewModels | Split-View, Dialoge, Auswahl, Bindings, validierte Aktionen | Ersetzen die bisherigen Meldungsdialoge als Hauptbedienung |
| Geräteverwaltung | Gerätebestand, Identität, Auswahl, Status, manuelle Trennung und Aktionskoordination | `Service`, Teile von `FindDevice` und `TryKnownDevice` |
| Discovery-/ADB-Dienst | mDNS, ADB-Geräteliste, Pairing, Verbinden, Trennen, Metadaten, Serial-Verifikation | `ParseProto`, `ParsePlain`, `SerialFromInstance`, `ValidEndpoint`, `FindReady` |
| Sitzungsverwaltung | Eigene Prozessregistrierung, Fensteraktivierung, Start/Stop, Sitzungsgrenze und Ende-Ereignisse | scrcpy-Start aus `Launch` |
| Konfigurationsdienst | Validierung, Migration, zentral koordiniertes atomisches Speichern | `ReadSerial`, `SaveSerial` |
| Prozessausführung / Logging | Sichere Argumente, Standard-Ein-/Ausgaben, Zeitlimits, Abbruch, laufende Meldungen | `Run`, `Log` |
| App-Lebenszyklus | Einzelinstanz, globaler Dialog, Infobereich, kontrolliertes Beenden | Bestehende Einzelinstanz-Idee |

### 17.2 Nebenläufigkeit

- Blockierende Prozess-/Dateivorgänge liegen nicht auf dem UI-Thread.
- UI-Zustände werden geordnet auf den UI-Thread übertragen.
- Eine Sperre je Geräteidentität koordiniert verändernde Aktionen; Starten darf notwendige interne Verbindungsschritte ausführen, ohne sich selbst zu blockieren.
- Sitzungsplätze werden zentral und atomar reserviert.
- Erkennungsrunden überlappen nicht; ältere Ergebnisse überschreiben keine neuere Benutzeraktion.
- Globale ADB-Server-Aktionen werden gegen normale Verbindungsaktionen koordiniert.
- Abbruch wird vom App-Lebenszyklus bis zu eigenen gestarteten Client-Prozessen weitergereicht.
- Prozessausgaben werden laufend und ohne UI-Überlastung zusammengeführt; stdout und stderr dürfen sich nicht gegenseitig blockieren.

**Technischer Detailvorschlag:** höchstens zwei gleichzeitige eigene ADB-Verbindungsversuche, unabhängig von den vier möglichen scrcpy-Sitzungen. Der tatsächliche Scheduler und die Cache-Strategie werden anhand der vorhandenen Werkzeuge geprüft.

### 17.3 Identität und Transporte

Die gespeicherte Seriennummer bleibt die primäre Identität. Ein temporärer ADB-Selektor kann eine Netzwerkadresse oder eine mDNS-basierte Kennung sein. Vor Sitzungsstart muss er dem ausgewählten Gerät eindeutig und bestätigt zugeordnet sein. Ein zufällig verfügbares USB-Gerät oder ein anderes WLAN-Gerät darf nicht als Ersatz verwendet werden.

Bei Portwechsel wird neu ermittelt, nicht ein dauerhaft gespeicherter alter Endpunkt wiederverwendet. Mehrere Beobachtungen werden nach Seriennummer zusammengeführt. Unklare oder widersprüchliche Identität führt zu einer erklärten Unterbrechung des Starts statt zum Fernsteuern des falschen Geräts.

### 17.4 Prozessausführung

Lokale Programme werden über explizite Pfade und einzeln übergebene Argumente gestartet. Kein Zusammenbauen einer Shell-Kommandozeile aus UI-Eingaben. scrcpy erhält die gebündelte ADB-EXE und den lokalen Serverpfad. Die bestehenden Schutzmaßnahmen gegen geerbte fremde ADB-Server-/Geräteauswahlvariablen bleiben erhalten und werden für die GUI überprüft.

Das Abbrechen eines ADB-Client-Aufrufs beendet nicht pauschal den gemeinsamen ADB-Server. Prozesse werden über eigene Handles verwaltet; PhonePilot beendet keine Prozesse nur aufgrund eines gleichen Namens. Fensteraktivierung verwendet die Zuordnung zur eigenen Sitzung und funktioniert auch nach Minimieren des scrcpy-Fensters.

Die konkrete Erkennung eines betriebsbereiten scrcpy-Fensters sowie normale und fehlerhafte Exit-Codes müssen mit der tatsächlich mitgelieferten Version geprüft werden. Diagnosemeldungen ersetzen diese Prüfung nicht.

### 17.5 Zeitlimits

**Technischer Detailvorschlag:** vorhandene Grenzen als Ausgangspunkt verwenden: kurze ADB-Abfragen ungefähr 2–5 Sekunden, Verbindungsaktion insgesamt ungefähr 30 Sekunden, initiale erweiterte Suche höchstens ungefähr 60 Sekunden und Code-Kopplung ungefähr 30 Sekunden. Periodische Arbeit darf dadurch keine Warteschlange erzeugen. Laufende scrcpy-Sitzungen besitzen kein pauschales Laufzeitlimit. Beenden wartet nur begrenzt und kann anschließend ausschließlich den eigenen betroffenen Prozess beenden.

## 18. Portable Veröffentlichung und Bedienqualität

### 18.1 Veröffentlichung

Windows-x64-Veröffentlichung bleibt eigenständig, mit gebündelter .NET-Laufzeit und lokalen Werkzeugordnern. Die spätere WPF-Anpassung ergänzt die nötigen Projekteigenschaften, ersetzt aber nicht den bisherigen Publish-Ablauf.

```powershell
dotnet publish .\PhonePilot.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

Der komplette Ausgabeordner wird verteilt: EXE plus ADB-/scrcpy-Paketdateien. Single-File-Publish bedeutet nicht, dass diese Werkzeugordner entfallen. Kein Bedarf an systemweit installiertem ADB, scrcpy, Visual Studio oder einer separaten .NET-Laufzeit auf dem Ziel-PC.

Das bestehende Copy-Target bleibt erhalten beziehungsweise wird nur nötigenfalls angepasst. Wiederholte Veröffentlichung bewahrt persönliche `config.json`. Die spätere Bereinigung darf nicht unbeabsichtigt persönliche Einstellungen oder notwendige Migrationssicherungen entfernen. Eine gesperrte eigene EXE wird verständlich gemeldet statt zwangsweise beendet.

### 18.2 Darstellung und Zugänglichkeit

- Größenveränderbares Fenster und skalierbarer Split-View; ein bis zwei Geräte wirken nicht wie eine unvollständig gefüllte Viereransicht.
- Bei wenig Breite werden Karten/Bedienelemente sinnvoll umgebrochen oder Inhalte gescrollt; wesentliche Aktionen bleiben erreichbar.
- **Technischer Detailvorschlag:** Mindestgröße ungefähr 960 × 640 geräteunabhängige Einheiten; Erprobung bei 100 %, 125 %, 150 % und 200 % Windows-Skalierung.
- Tastaturbedienung, sichtbare Fokusmarkierungen, beschriftete Schalter und verständliche Hilfetexte.
- Keine exklusiv durch Farbe kommunizierten Zustände.
- Routinefehler werden am Gerät angezeigt; Bestätigungsdialoge bleiben auf bewusst einschneidende Aktionen begrenzt.
- Bei globalen Aktionen bleiben andere Geräte und Tabs bedienbar.

## 19. Umsetzungsetappen und Abnahmekriterien

### 19.1 Geplante Reihenfolge nach ausdrücklicher Freigabe

1. Bestehende Prozessausführung, Parser, Identitätsprüfung und Konfiguration isolieren; bisherigen Ablauf während der Umstellung nachvollziehbar erhalten.
2. WPF-Grundfenster, Gerätebestand, Statusmodell und Hintergrundaktualisierung ergänzen.
3. Einzelaktionen, eigene Sitzungsverwaltung und globale Aktionen mit Vierergrenze ergänzen.
4. Automatisch gespeicherte Einstellungen, Presets, Konfigurationsmigration und Code-Kopplung integrieren.
5. Allgemeine Einstellungen, Infobereich, Diagnosen und Live-Logs vervollständigen.
6. Portable Veröffentlichung sowie Tests mit echten Pixel-Geräten und wechselnden WLAN-Bedingungen durchführen.

### 19.2 Überprüfbare Abnahmefälle

| ID | Szenario | Erwartetes Ergebnis |
| --- | --- | --- |
| A01 | PhonePilot öffnen | Hauptfenster erscheint; erste Suche läuft; keine neue scrcpy-Sitzung startet automatisch |
| A02 | Automatische Aktualisierung | Standard aktiv/5 s; Änderung ausschließlich global; keine überlappenden Runden |
| A03 | Manuell aktualisieren während Suche | Anfrage wird geordnet berücksichtigt; Auswahl bleibt erhalten |
| A04 | Ein Gerät vorhanden | Kompakte Liste ohne leere Platzhalter; Verbinden/Starten verständlich erreichbar |
| A05 | Zwei Geräte starten | Zwei eigene Fenster mit korrekter unabhängiger Zuordnung |
| A06 | Vier Geräte starten | Vier parallele eigene Sitzungen; UI bleibt bedienbar |
| A07 | Fünftes Gerät starten | Kein zusätzlicher Prozess; verständlicher Limit-Hinweis |
| A08 | Doppelklick / konkurrierende Startaktionen | Höchstens eine Sitzung pro Gerät; keine Überbelegung durch Startreservierungen |
| A09 | Ein Gerät stoppen oder Fenster schließen | Nur diese eigene Sitzung endet; andere Sitzungen und ADB bleiben bestehen |
| A10 | Fremder scrcpy-Prozess vorhanden | Kein Übernehmen, kein direktes Stoppen durch PhonePilot |
| A11 | mDNS fehlt, ADB bleibt verbunden | Gerät bleibt verbunden angezeigt; kein erzwungener Sitzungsabbruch |
| A12 | WLAN aus und wieder an; Port wechselt | Neues Ziel wird ermittelt und Identität geprüft; ADB verbindet erneut; beendetes scrcpy startet nicht automatisch |
| A13 | Bewusst Verbindung trennen | Eigene Sitzung gegebenenfalls erklärt beenden; keine eigenen automatischen Wiederverbindungsversuche bis expliziter Aktion |
| A14 | Fehlende/falsche Kopplung | Verständlicher Status, keine Endlosschleife und keine Steuerung eines anderen Geräts |
| A15 | Code-Kopplung mit anschließendem Fund | Kopplungs- und Verbindungsport getrennt; Identität bestätigt; Code weder in Datei noch Log |
| A16 | Kopplung erfolgreich, mDNS fehlt | Erfolg der Kopplung bleibt sichtbar; manuelle flüchtige Verbindungsadresse möglich |
| A17 | Einstellungen bei laufender Sitzung ändern | Gültige Werte automatisch gespeichert; aktive Sitzung unverändert; nächste Sitzung verwendet neue Werte |
| A18 | Ungültige Eingabe / nicht beschreibbarer Ordner | Feldfehler beziehungsweise Nicht-gespeichert-Hinweis; letzte gültige Konfiguration nicht still zerstört |
| A19 | Altes `DeviceSerial`-Format | Gerät übernommen; H.265/40 Mbit/s/-S ohne zusätzliche Bildkanten-/FPS-Grenzen erhalten |
| A20 | Gerät vergessen | Eintrag/Einstellungen entfernt, Kopplung erhalten; erneuter Fund nachvollziehbar |
| A21 | Fenster mit aktiven Sitzungen schließen | Auswahl Weiterlaufen/Beenden/Abbrechen; Merken und spätere Änderung funktionieren |
| A22 | Im Infobereich weiterlaufen | Sitzungen laufen weiter; Hauptfenster und Beenden bleiben erreichbar |
| A23 | Alle starten, ein Gerät schlägt fehl | Andere Geräte starten; Ergebnisse pro Gerät nachvollziehbar |
| A24 | Live-Logs und Export | Laufende Ausgaben erscheinen; Filter/Gerätezuordnung korrekt; Export enthält passende Auswahl |
| A25 | ADB-Server-Neustart ausdrücklich auslösen | Bestätigung nennt globale Wirkung; kein automatischer scrcpy-Neustart |
| A26 | Portable EXE aus anderem Arbeitsverzeichnis | Lokale Werkzeuge und EXE-nahe Konfiguration werden korrekt gefunden |
| A27 | Zweiter PhonePilot-Start | Keine zweite konkurrierende Verwaltung; bestehende Instanz wird verständlich behandelt |
| A28 | Publish wiederholen | Werkzeugpakete vorhanden; persönliche Konfiguration erhalten |
| A29 | Skalierung und kleines Fenster | Keine unzugänglichen Aktionen oder abgeschnittenen Pflichtfelder |
| A30 | Längere Laufzeit mit mehreren Geräten | Logs/Refresh-Aufträge wachsen nicht unbegrenzt; keine falschen oder hängen gebliebenen Sitzungszustände |

Für Parser, Zusammenführung, Identitätszuordnung, Migration und konkurrierende Sitzungsreservierung sind gezielte automatisierte Tests vorgesehen. Echte WLAN-Wechsel, Audio, Fensterfokus, Infobereich, Leistung und portable Veröffentlichung benötigen zusätzlich Integrationstests beziehungsweise manuelle Tests auf Windows mit den tatsächlichen Geräten. Mockups belegen keine bestandenen Funktionstests.

## 20. Noch zu prüfende Detailvorschläge

Die wesentlichen Produktentscheidungen und fünf Ansichten sind abgestimmt. Folgende Details wurden zur Vollständigkeit vorgeschlagen und sind bei Dokumentfreigabe beziehungsweise technischer Erprobung zu bestätigen:

| Detail | Vorgeschlagener Ausgangspunkt | Prüfzeitpunkt |
| --- | --- | --- |
| Intervall-Auswahl | 2/5/10/15/30/60 Sekunden | Dokumentprüfung; Standard 5 s ist festgelegt |
| Automatik deaktiviert | Keine periodischen Abfragen/Retry-Versuche, eigene Prozessüberwachung bleibt aktiv | Dokumentprüfung |
| Profile | Qualität 2560/20/60/H.265; Ausgewogen 1920/8/60/H.264; Schnell 1280/4/30/H.264 | Tests mit ein bis vier Geräten |
| Listenreihenfolge und Überschreitung freier Plätze | Stabil; Sammelstart in Listenreihenfolge | Dokumentprüfung |
| Eingabeauswahl/-grenzen | Übliche Werte plus validierte individuelle Eingabe | Prüfung der gebündelten Werkzeuge |
| Erweitert: Bildausrichtung | Darstellungsausrichtung; automatisch plus Hauptorientierungen | Dokumentprüfung und Werkzeugprüfung |
| Status Startet → Läuft | Bereitschaftssignal/Fensterprüfung passend zur tatsächlichen scrcpy-Version | Implementierungsvalidierung |
| Wiederholungsabstände und Parallelität | 5/10/20/bis 30 s; höchstens zwei ADB-Verbindungsversuche parallel | Integrationsprüfung |
| Prozess-Zeitlimits | Bestehende Grenzen als Ausgangspunkt; kein Limit für laufende Sitzung | Integrationsprüfung |
| JSON-Feldnamen und Migration | Schema 1, lesbare Felder, Sicherung vor Migration | Dokumentprüfung und Migrationstests |
| Log-Puffer und Aufbewahrung | 5000 Anzeigeeinträge; 3 × 5 MiB im Temp-Unterordner | Dokumentprüfung und Langzeittest |
| Fenstergröße und Umbruch | Mindestgröße ungefähr 960 × 640; responsive Karten | GUI-/Skalierungstest |
| Farbschema-Werte | System, Hell, Dunkel | Dokumentprüfung |
| Infobereich-Beenden und Vergessen bei aktiver Sitzung | Ausdrückliche Bestätigung statt versteckter Nebenwirkung | Dokumentprüfung |

Die Vorschläge dieser Liste sind durch die anschließende ausdrückliche Zustimmung des Nutzers ebenfalls freigegeben. Die vorläufigen Leistungswerte bleiben bis zur Hardware-Abnahme Testwerte.

## 21. Technische Referenzen

Die folgenden offiziellen Dokumentationen stützen die Einordnung der Werkzeuge. Maßgeblich für die spätere konkrete Argumentunterstützung bleibt die tatsächlich gebündelte Version; die verlinkten Hauptzweige können sich ändern.

- [Microsoft: WPF-Übersicht](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/) – Layout, Datenbindung und Gestaltung als Grundlage der GUI-Wahl.
- [Android Developers: ADB, WLAN-Kopplung und Geräteverbindungen](https://developer.android.com/tools/adb) – Trennung von ADB-Client/-Server, Code-Kopplung und Wireless Debugging.
- [scrcpy: Verbindungen und Geräteauswahl](https://github.com/Genymobile/scrcpy/blob/master/doc/connection.md) – gezielte Geräteauswahl.
- [scrcpy: Video](https://github.com/Genymobile/scrcpy/blob/master/doc/video.md) – Bildkantenbegrenzung, Bitrate, FPS-Limit, Codec und Encoder.
- [scrcpy: Fenster](https://github.com/Genymobile/scrcpy/blob/master/doc/window.md) – Titel, Vollbild, rahmenloses Fenster und Always-on-top.
- [scrcpy: Audio](https://github.com/Genymobile/scrcpy/blob/master/doc/audio.md) – Standardverhalten und geräteabhängige Unterstützung.

## 22. Versionshistorie

| Version | Datum | Inhalt |
| --- | --- | --- |
| 1.0 | 10.10.2026 | Zusammenführung des aktuellen Konzepts, der fünf bestätigten Mockups, der Architektur, Detailvorschläge und Abnahmekriterien; zur ausdrücklichen Freigabe vorbereitet |
| 1.0, Freigabe | 10.10.2026 | Ausdrückliche Zustimmung zu sämtlichen Vorschlägen und Auftrag zur vollständigen Implementierung; funktionale Anforderungen unverändert |
