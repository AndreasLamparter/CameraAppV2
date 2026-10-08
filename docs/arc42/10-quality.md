# 10 Qualitätsanforderungen

| Szenario | Erwartung | Absicherung |
|---|---|---|
| Stopp während eines Zielereignisses im Zustand Aufnahme | Aufnahme erscheint vollständig in der Liste | `Szenario_StoppenWaehrendEinesZielereignisses` |
| Aufnahme wird gerade gespeichert | Nicht in der Liste | `Szenario_UnvollstaendigeAufnahmeWirdNichtAngezeigt` |
| Externes Beenden während des Speicherns | Prozess endet erst nach dem Speichern | `Szenario_AnwendungExternBeenden`, `Shutdown_ByExternalProgram_StopsTheApplication` |
| Frontkamera nicht angeschlossen | Fehler je Kamera, Zielkamera nimmt weiter auf | `Szenario_FrontkameraFehlt*` |
| ffmpeg fehlt | Zielbild und Zeitstempel bleiben, Fehler sichtbar | `Szenario_VideoKannNichtErzeugtWerden`, `VideoEncoder_FfmpegMissing_ReturnsStableError`, Playwright |
| Offset −40 ms an der Frontkamera | Alle Frontzeitstempel 40 ms früher | `Szenario_KameraOffsetWirdAngewendet` |
| Externer Aufruf ohne gültigen Schlüssel | Abgelehnt, Zustand unverändert | `Szenario_ExterneSteuerungOhneBerechtigung` |
| Bediener versucht, die Anwendung zu beenden | Abgelehnt | `Shutdown_ByOperator_IsForbidden_…` |
| Pfadmanipulation in Aufnahme-IDs | 404, kein Dateizugriff außerhalb | `RecordingId_InvalidOrTraversal_IsRejected`, `Recording_InvalidOrUnknownId_Returns404` |
| Linienrate unter 95 % | Warnung | `Szenario_ZuNiedrigeLinienrate` |
| API-Vertrag geändert | Test schlägt fehl, bis `scripts\generate-api.ps1` lief | `OpenApi_CheckedInContract_MatchesGeneratedDocument` |

Gates: `scripts\build.ps1` (vue-tsc, ESLint, Vitest, Vite-Build, dotnet build/test, Playwright).

Auf dem Entwicklungs-PC gemessen (i7-1185G7, SVPRO 640×480 bzw. 960×720 + eMeet 1920×1080, beide MJPG über
`MediaFoundationCameraSource`): Zielkamera 87–91 Bilder/s, Frontkamera 25 Bilder/s (Belichtung), über eine Minute stabil.
Über OpenCV (DirectShow bzw. MSMF) schwankte dieselbe Kombination zwischen 1 und 90 Bilder/s.
Noch nicht abgesichert: stabile 90 Bilder/s im Dauerbetrieb, Kalibrierung der Offsets, echte ffmpeg-Ausgabe im
Browser.
