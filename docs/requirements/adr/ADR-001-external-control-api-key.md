# ADR-001 – Authentifizierung externer Programme per API-Key

| | |
|---|---|
| Status | Angenommen (2026-10-07, Teamentscheidung) |
| Betrifft | FEATURE-SET-1, FS1-50, FS1-51, offener Punkt OP-1 |

## Kontext

Externe Programme (z. B. die RFID-Zeitmessung) steuern die Zielkamera: Kameras starten und stoppen, manueller
Auslöser, Hintergrund neu lernen, Zustand abfragen, Anwendung beenden. Diese Aufrufe dürfen nur berechtigte
Aufrufer ausführen (FS1-51). Wie sich die Programme authentifizieren, war offen (OP-1).

## Entscheidung

- Externe Programme senden einen gemeinsamen Schlüssel im HTTP-Header `X-Api-Key`.
- Der Schlüssel wird in der Konfiguration hinterlegt (`TimingApp:ExternalControl:ApiKey`), nicht im Repository.
- Ist kein Schlüssel konfiguriert, ist die externe Steuerung abgeschaltet: Jeder Aufruf mit Schlüssel wird abgelehnt.
- Ein gültiger Schlüssel berechtigt nur zu den Steuerbefehlen und zum Beenden, nicht zu Aufnahmen und Einstellungen.
- Das Beenden der Anwendung ist ausschließlich externen Programmen vorbehalten (die Oberfläche bietet es nicht an).

## Konsequenzen

- Einfach in der RFID-Software umsetzbar (ein Header).
- Der Schlüssel ist ein gemeinsames Geheimnis: Er wird nie protokolliert und muss bei Verdacht ausgetauscht werden.
- Keine Ablaufzeit und keine Zuordnung zu einzelnen Programmen; bei Bedarf in einem späteren ADR erweiterbar.
