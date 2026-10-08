# 4 Lösungsstrategie

| Ziel | Ansatz |
|---|---|
| Fachlogik unabhängig von Hardware | Clean Architecture: Hintergrund, Belegung, Zielereignisse und Aufnahmefenster sind reine Domain-Klassen ohne OpenCV; die Application sieht Kameras nur über `ICameraSystem`. |
| Schnelle, blockierende Kameratreiber | Ein Capture-Thread je Kamera; Daten verlassen ihn nur über Channels, eine begrenzte Kompressions-Queue und unveränderliche Spalten-Arrays. |
| Zeitliche Genauigkeit | Eine gemeinsame `CaptureClock` (monotoner `TimeProvider`-Zeitstempel, einmal an UTC verankert); Zeitstempel direkt nach `Grab`, plus Offset je Kamera. |
| Speichern ohne Datenverlust | Speichern entkoppelt in `RecordingSaver`; `recording.json` wird zuletzt atomar geschrieben. Beim Beenden werden Kameras gestoppt und alle Speichervorgänge abgewartet. |
| Browser-taugliche Videos | ffmpeg erzeugt H.264/yuv420p mit `+faststart`; Medien werden mit Range-Requests ausgeliefert. |
| Testbarkeit | Simulator (synthetisch oder Video-Datei) als `ICameraSource`; Application-Akzeptanztests mit Fakes an allen Ports; Playwright gegen das echte Backend mit Simulator. |
| Keine Datenbank, wo keine nötig ist | Feature Set 1 hat keine relationalen Daten: Aufnahmen liegen als Dateien, Einstellungen als JSON (siehe Kapitel 9). |
