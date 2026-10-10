# PhonePilot – Anwendungssymbol

`PhonePilot.png` zeigt das gewählte Motiv aus Smartphone, WLAN und Mauszeiger. Die blaue Kachel wurde für eine gut erkennbare Darstellung in der Windows-Taskleiste angepasst. Frühere Entwürfe und Bildgenerierungs-Prompts gehören nicht zum aktuellen Projektbestand.

`PhonePilot.ico` enthält transparente 32-Bit-Bilder in den Größen 16, 20, 24, 32, 40, 48, 64, 128 und 256 Pixel. Die ICO-Datei wird als Windows-Ressource in die EXE sowie als WPF-Ressource für Fenster und Infobereich eingebunden. Das PNG erscheint in der Kopfzeile der Anwendung. Beide Ressourcen sind eingebettet; neben der veröffentlichten EXE werden keine zusätzlichen Icon-Dateien benötigt.

Beim Verpacken ins ICO werden transparente Außenränder anhand der sichtbaren Grafik (Alpha ab 128) entfernt. Die Kachel wird auf eine exakt quadratische Windows-Symbolfläche eingepasst. Eine deckende blaue Grundfläche und eine quadratische Maske mit einheitlichem Eckenradius (ein Sechstel der Seitenlänge) garantieren gleiche sichtbare Breite und Höhe, unabhängig von kleinen Abweichungen im generierten PNG. Die Symbolfläche wird vollständig genutzt; nur die abgerundeten Ecken sind transparent. Smartphone, WLAN und Mauszeiger bleiben das Motiv.

Bei Änderung des Quellbildes die ICO-Datei vor dem Build erneut erzeugen:

```powershell
.\tools\Build-Icon.ps1
```
