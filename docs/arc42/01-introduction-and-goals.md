# 1 Einführung und Ziele

## Zweck

TimingApp ist die neue Zeitmessung für MTB-Rennen. Umgesetzt ist **Feature Set 1**: Zielkamera und Frontkamera
an der Ziellinie (Bounded Context `FinishRecording`). Die Anwendung erzeugt ein Zielbild (Photo-Finish) aus der
Pixelspalte der Ziellinie, nimmt synchron ein Frontvideo auf, speichert Zielereignisse und spielt sie synchron ab.
Die Anforderungen stehen in [FEATURE-SET-1.md](../requirements/FEATURE-SET-1.md), der Umsetzungsstand in
[feature-set-1.md](../implementation/feature-set-1.md).

## Qualitätsziele

| Priorität | Ziel | Bedeutung |
|---|---|---|
| 1 | Datenintegrität | Ein gespeichertes Zielereignis ist vollständig oder gar nicht sichtbar; Stoppen und Beenden verlieren keine laufenden Aufnahmen. |
| 2 | Zeitliche Genauigkeit | Jede Spalte und jedes Frontbild trägt einen Zeitstempel einer gemeinsamen monotonen Uhr; Kamera-Offsets sind kalibrierbar. |
| 3 | Robustheit im Betrieb | Fehlende Kameras, ausbleibende Bilder und fehlendes ffmpeg werden je Ursache angezeigt und legen den Rest nicht lahm. |
| 4 | Offline-Betrieb | Keine Laufzeitabhängigkeit von Internetdiensten (FS1-80). |
| 5 | Testbarkeit ohne Hardware | Simulation über dieselben Ports wie echte Kameras. |

## Stakeholder

| Rolle | Erwartung |
|---|---|
| Bediener am Ziel | Einfache Live-Ansicht, sichere Bedienung, Aufnahmen schnell finden und auswerten |
| RFID-Zeitmessung (externes Programm) | Kameras per API steuern und Zustand abfragen |
| Entwicklung | Klare Grenzen, deterministische Tests, Simulator |
