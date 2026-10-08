# 11 Risiken und technische Schulden

## Risiken

| Risiko | Auswirkung | Umgang |
|---|---|---|
| Reale Kamera erreicht keine 90 Bilder/s (USB-Bandbreite, Belichtungszeit, Treiber) | Gröbere Zeitauflösung | Linienrate wird gemessen und gewarnt (FS1-14); Test mit Hardware ausstehend |
| Treiberlatenz unterscheidet sich zwischen Kameras | Versatz zwischen Zielbild und Frontvideo | Offsets je Kamera; Differenzanzeige in der Wiedergabe zur Kalibrierung (FS1-33) |
| Plötzliche Lichtänderung (Wolke) | Linie gilt als dauerhaft belegt, Ereignisse werden alle 60 s geteilt | Befehl „Hintergrund neu lernen“ (FS1-25); automatische Lösung offen |
| Speicherbedarf: Front-Ringpuffer hält Front-Vorlauf + maximale Dauer + Nachläufe (bei 720p ≈ 3 MB/s, also ≈ 200 MB) | Speicherdruck bei hoher Auflösung | Messen auf dem Ziel-PC; OP-2 (Dauerbetrieb) offen |
| Speicherplatz läuft voll | Aufnahmen scheitern (`recording.saveFailed`) | OP-3 offen |
| ffmpeg-Version zu alt (`-fps_mode` ab 5.1) oder ohne libx264 | `video.encodingFailed` | Voraussetzung dokumentiert; echter Lauf ausstehend |
| Kameraauswahl per Name: der Index kommt aus der Enumeration des Backends (`MFEnumDeviceSources` bzw. DirectShow); Media Foundation öffnet über dieselbe Enumeration | Falsche Kamera geöffnet, falls die Reihenfolgen abweichen (nur beim Rückfall auf OpenCV/DirectShow möglich) | Mit drei Kameras geprüft; bei V4L2 nur Auswahl per Index |
| Vortice.MediaFoundation hängt von SharpGen.Runtime 2.4.2-beta ab | Vorabversion im Kamerapfad | Versionen zentral festgelegt; bei Updates Hardwaretest wiederholen |
| Kameras übernehmen „automatische Belichtung“ über `IAMCameraControl` nicht immer (eMeet: bleibt manuell) | Unerwartet dunkles oder helles Bild | Warnung `camera.exposureNotApplied` in der Live-Ansicht; festen Wert einstellen |
| Die SVPRO hing nach sehr vielen Öffnen/Schließen-Zyklen und lieferte kaum noch Bilder | Ausfall der Zielkamera bis zum Neu-Einstecken | Beobachtet beim Testen; im Dauerbetrieb prüfen (OP-2) |
| Belichtung, Bildrate und Last: lange Belichtung begrenzt die Bildrate (−5 ≈ 1/32 s → ~32 Bilder/s); hohe Auflösung der Frontkamera kostet CPU (Dekodieren, JPEG für den Ringpuffer) | Linienrate unter der eingestellten Bildrate | Zielkamera −7 oder kürzer; Frontkamera 1280×720; Linienraten-Warnung (FS1-14) |
| Aufnahmemodi werden aus DirectShow-Strukturen über feste Offsets gelesen (`VIDEOINFOHEADER`, `VIDEO_STREAM_CONFIG_CAPS`) | Falsche Bildraten in der Auswahl, falls ein Treiber abweicht | Mit SVPRO, eMeet und FJ Camera geprüft; die Kamera misst die tatsächliche Rate |
| Kamera ohne MJPG (z. B. nur YUY2): die Aufnahme fordert trotzdem das konfigurierte `FourCc` an | Treiber wählt ein anderes Format oder eine niedrigere Rate | Auswahl zeigt das Format an; gemessene Bildrate beachten |
| Gerätepfad ändert sich bei Kameras ohne Seriennummer mit dem USB-Port | Rückfall auf den Namen; bei zwei gleichen Modellen `camera.notFound` | Kamera neu auswählen; Ziel- und Frontkamera sind verschiedene Modelle (FEATURE-SET-1 §4) |
| Browser erlauben über HTTP/1.1 sechs Verbindungen je Host; die Live-Ansicht belegt drei mit MJPEG | Zwei offene Live-Tabs im selben Browser blockieren API-Aufrufe | Nur einen Live-Tab je Browser öffnen; bei Bedarf HTTPS mit HTTP/2 oder eine kombinierte Vorschau |

## Technische Schulden

- Die Spaltenauswahl im Browser rechnet mit der Bildbreite = Anzahl Spalten; ein Zielbild mit nachträglich
  skalierter Breite würde das brechen (heute nicht der Fall).

## Offene Punkte

- FEATURE-SET-1 OP-2 bis OP-6 (OP-1 entschieden, ADR-001).
- Interpretationen in [feature-set-1.md](../implementation/feature-set-1.md) warten auf Bestätigung.
- ffmpeg-Lizenz (OP-4) und Echtbetrieb mit Hardware.
