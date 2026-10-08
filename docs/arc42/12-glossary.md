# 12 Glossar

Fachbegriffe von Feature Set 1 (Zielkamera, Ziellinie, Spalte, Zielbild, Linienrate, Belegung, Zielereignis,
Aufnahme, Sitzung, Kamera-Offset) sind in [FEATURE-SET-1.md, Abschnitt 3](../requirements/FEATURE-SET-1.md#3-begriffe)
definiert. Hier nur die technischen Begriffe.

| Begriff | Bedeutung | Im Code |
|---|---|---|
| Capture-Thread | Dedizierter Thread je Kamera, der Bilder holt und zeitstempelt | `CameraWorker` |
| Capture-Uhr | Gemeinsame monotone Zeitbasis aller Kameras | `CaptureClock` |
| Spalte (technisch) | BGR-Bytes entlang der Ziellinie aus einem Bild mit Zeitstempel | `LineColumn` |
| Bilddrehung | Drehung, die das Bild einer gedreht montierten Zielkamera aufrecht stellt; Ziellinie und Vorschau beziehen sich auf das gedrehte Bild | `ImageRotation` |
| Frontbild (technisch) | JPEG eines Frontkamera-Bildes mit Zeitstempel | `EncodedFrame` |
| Aufnahmefenster | Zeitraum der Spalten bzw. Frontbilder einer Aufnahme | `RecordingWindow` |
| Entwurf | Abgeschlossenes Zielereignis, das gespeichert werden soll | `RecordingDraft` |
| Lernphase | Mittelung der ersten 30 Spalten zum Hintergrund | `FinishLineMonitor.LearningColumns` |
| Laufendes Zielbild | Live-Zielbild der letzten Sekunden | `LiveStrip`, `PreviewKind.FinishStrip` |
| Metadaten | `recording.json`, zuletzt geschrieben; markiert eine Aufnahme als vollständig | `RecordingMetadata` |
