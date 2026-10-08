# Architekturdokumentation (arc42)

Lebende Dokumentation: Sie beschreibt, wie das System **aktuell** gebaut ist. Was das System leisten muss, steht in
[`docs/requirements/`](../requirements/); wie wir bauen, in [`CLAUDE.md`](../../CLAUDE.md).

Last reviewed: 2026-10-08 (Kameraauswahl per Name und Gerätepfad, Aufnahmemodi, Bilddrehung, Media Foundation über Vortice)

| Kapitel | Inhalt |
|---|---|
| [1 Einführung und Ziele](01-introduction-and-goals.md) | Zweck, Qualitätsziele, Stakeholder |
| [2 Randbedingungen](02-constraints.md) | Technik, Organisation, Konventionen |
| [3 Kontextabgrenzung](03-context.md) | Kameras, ffmpeg, externe Programme, Browser |
| [4 Lösungsstrategie](04-solution-strategy.md) | Grundlegende Entscheidungen |
| [5 Bausteinsicht](05-building-blocks.md) | Projekte, Ports, Adapter |
| [6 Laufzeitsicht](06-runtime.md) | Capture-Pipeline, Zielereignis, Speichern, Beenden |
| [7 Verteilungssicht](07-deployment.md) | Ziel-PC, Ports, Laufzeitvoraussetzungen |
| [8 Querschnittliche Konzepte](08-crosscutting-concepts.md) | Zeit, Speicherung, Sicherheit, Fehler, Nebenläufigkeit |
| [9 Architekturentscheidungen](09-decisions.md) | Entscheidungen und ADRs |
| [10 Qualitätsanforderungen](10-quality.md) | Qualitätsszenarien und ihre Absicherung |
| [11 Risiken und technische Schulden](11-risks.md) | Risiken, Schulden, offene Punkte |
| [12 Glossar](12-glossary.md) | Begriffe |

## Welche Änderung betrifft welches Kapitel?

| Änderung | Kapitel |
|---|---|
| Neues oder entferntes Projekt, Bounded Context, Port, Adapter, Context Bridge | 5 |
| Neues externes System, Hardware-Schnittstelle, Endpunktgruppe | 3, 7 |
| Neues externes Werkzeug oder neue Laufzeitvoraussetzung (z. B. ffmpeg) | 3, 7 |
| Neuer Hintergrunddienst, Pipeline-Schritt, Recovery- oder Wartungsablauf | 6 |
| Geändertes Konzept für Persistenz, Ablage, Sicherheit, Zeit, Fehler, Nebenläufigkeit | 8 |
| Wesentliche technische Entscheidung | 9 (oder ADR) |
| Abgeschlossenes Feature Set | 1, 10, 11 |
| Erledigtes Risiko, neue technische Schuld, neuer offener Punkt | 11 |
| Neuer Fachbegriff | 12 |
