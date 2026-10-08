# ADR-002 – Anmeldung an der Oberfläche per PIN

| | |
|---|---|
| Status | Angenommen (2026-10-07, Teamentscheidung) |
| Betrifft | FEATURE-SET-1, Oberfläche (FS1-60 bis FS1-64) |

## Kontext

FEATURE-SET-1 legt nicht fest, wer die Oberfläche bedienen darf. CLAUDE.md verlangt standardmäßig geschützte
Endpunkte (default deny).

## Entscheidung

- Jeder Zugriff auf die Oberfläche erfordert eine Anmeldung mit PIN, auch direkt am Ziel-PC.
- Die PIN wird in der Konfiguration hinterlegt (`TimingApp:Access:OperatorPin`), nicht im Repository.
- Die Sitzung ist ein HttpOnly-Cookie; im Browser wird nichts Geheimes gespeichert.
- Anmeldeversuche sind pro Adresse begrenzt (Rate Limit).

## Konsequenzen

- Bedienung von anderen Geräten im Netz ist möglich und gleich geschützt.
- Ohne konfigurierte PIN ist keine Anmeldung möglich.
