# PhonePilot – aktuelle README-Screenshots

Erzeugt am 10.10.2026 aus dem aktuellen Quellstand mit `--render-preview`. Die Bilder zeigen implementierte WPF-Inhalte ohne native Windows-Titelleiste. Gerätekennungen (`DEMO-PHONE-01` und `DEMO-PHONE-02`), Adressen, Meldungen und Sitzungen sind erfundene Beispieldaten; es wurden keine echten Geräte verbunden oder persönliche Einstellungen verwendet. Die Vorschau verwendet für Adressen `192.0.2.10` und `192.0.2.11`.

| Bild | Inhalt |
| --- | --- |
| [01-steuerung.png](01-steuerung.png) | Geräteübersicht und Steuerung |
| [02-allgemein.png](02-allgemein.png) | Allgemeine Einstellungen |
| [03-einstellungen.png](03-einstellungen.png) | Geräteeinstellungen |
| [04-erweitert.png](04-erweitert.png) | Erweiterte Optionen und Diagnose |
| [05-logs.png](05-logs.png) | Logansicht mit Beispielmeldungen |
| [06-dunkel.png](06-dunkel.png) | Dunkles Farbschema |
| [07-kompakt.png](07-kompakt.png) | Kompakte Ansicht, 960 × 680 |
| [08-qr-kopplung.png](08-qr-kopplung.png) | QR-Panel ohne umgebenden Kopplungsdialog |
| [checks.txt](checks.txt) | Ergebnis der Offline-Prüfungen |

Die Hauptansichten messen 1440 × 940, der allgemeine Dialog 620 × 700 und das QR-Panel 620 × 640 Bildpixel bei 96 DPI. Scrollbare Tabs zeigen den jeweils sichtbaren Ausschnitt. Der QR-Code gehört ausschließlich zu einer nicht aktiven Beispielsitzung und lässt sich nicht zur Kopplung verwenden.

Reproduzieren aus dem Projektverzeichnis; zuerst die Vorschau mit den aktuellen Demo-Daten bauen:

```powershell
dotnet build .\PhonePilot.csproj -c Release -o .\artifacts\readme-preview
.\artifacts\readme-preview\PhonePilot.exe --render-preview .\docs\screenshots\2026-10-10
```

Dieser Befehl ersetzt Bilder und Prüfausgabe in diesem Ordner. Bedienung und Architektur beschreibt das [Projekt-README](../../../README.md).
