# Feature Set 2 – Umsetzungsstand und Interpretationen

Stand: 2026-10-08. Anforderungen: [FEATURE-SET-2.md](../requirements/FEATURE-SET-2.md).
Architektur: [arc42](../arc42/README.md), Laufzeit der Passagen in Kapitel 6.

## Umgesetzt

Alle Anforderungen FS2-01 bis FS2-19 sind umgesetzt. Jedes Gherkin-Szenario ist durch einen automatischen Test
abgedeckt:

| Szenario | Test |
|---|---|
| Aufnahme ohne Rennname | `FeatureSet2AcceptanceTests.Szenario_AufnahmeOhneRennname`, `ApiTests.Szenario_AufnahmeOhneGueltigenRennnamen_IsRejected` |
| Aufnahmen werden je Rennen abgelegt | `FeatureSet2AcceptanceTests.Szenario_AufnahmenWerdenJeRennenAbgelegt`, `StorageTests.Szenario_AufnahmenWerdenJeRennenAbgelegt`, `ApiTests.Recording_ThroughSimulatedCameras_IsListedPlayableAndDeletable` |
| Passage während einer Aufnahme | `FeatureSet2AcceptanceTests.Szenario_PassageWaehrendEinerAufnahme` |
| Passage ohne erkanntes Zielereignis | `FeatureSet2AcceptanceTests.Szenario_PassageOhneErkanntesZielereignis` |
| Rennen wechseln | `FeatureSet2AcceptanceTests.Szenario_RennenWechseln` |
| Ungültiger Rennname | `RaceTests.RaceName_NotAllowed_ReturnsStableCode`, `ApiTests.Szenario_AufnahmeOhneGueltigenRennnamen_IsRejected` |
| Versatz der Zeitmessung | `FeatureSet2AcceptanceTests.Szenario_VersatzDerZeitmessung` |
| Passage in der Vorschau | `FeatureSet2AcceptanceTests.Szenario_PassageInDerVorschau`, `ApiTests.Szenario_PassageInDerVorschau_IsRejected` |

Durch die Oberfläche: `web/e2e/finish-recording.spec.ts` (Rennname-Dialog, Rennen in der Liste).

## Interpretationen

Auslegungen, die die Anforderungen offen lassen. Sie warten auf Bestätigung.

1. **Zuordnung zum laufenden Zielereignis**: Eine Passage gehört zum laufenden, zu speichernden Zielereignis, wenn
   ihre Zeit nicht vor dessen Beginn minus Vorlauf liegt, also im Fenster seiner Aufnahme.
2. **Mehrere Passagen ohne Zielereignis**: Liegt eine Passage im Fenster einer noch nicht gespeicherten Aufnahme um
   andere Passagen (Vorlauf vor der ersten bis Nachlauf nach der letzten), teilen sie sich eine Aufnahme.
3. **Zielereignis nach Passage**: Beginnt ein Zielereignis, solange eine Aufnahme um Passagen noch wartet, übernimmt
   es deren Passagen, wenn sie in seinem Fenster liegen; sonst entstehen zwei Aufnahmen.
4. **Zu späte Passage**: Ist eine Passage mehr als 5 s alt und sind ihre Bilder nicht mehr gepuffert, wird sie
   protokolliert und verworfen (FS2-17 schließt diesen Fall aus).
5. **Startnummer**: 1 bis 20 Buchstaben, Ziffern oder `-` (Leerzeichen am Rand werden entfernt).
6. **Startzustand Aufnahme** (FS2-08): Die Anwendung startet in Vorschau; die Aufnahme beginnt erst mit dem
   Rennnamen aus dem Dialog.
7. **Rennname in der Vorschau**: Ein Wechsel nach Vorschau behält den Rennnamen; ein erneuter Start der Aufnahme
   fragt ihn wieder ab (vorbelegt).
8. **Ältere Aufnahmen** ohne Rennen bleiben im Medienordner und erscheinen in der Liste als „Ohne Rennen“.
