# 8 Querschnittliche Konzepte

## Zeit

- Application-Zeit über `TimeProvider`; kein `DateTime.Now` in Domain und Application.
- Kamerazeitstempel: `CaptureClock` (eine Instanz für alle Kameras) = einmal an UTC verankerter monotoner
  Zeitstempel; genommen direkt nach `Grab`, vor dem Dekodieren; plus `CameraSettings.OffsetMs`.
- Gespeichert werden Unix-Mikrosekunden (`UnixMicroseconds`), angezeigt Ortszeit `HH:mm:ss.fff`, abgeschnitten
  (`formatClock` im Frontend, FS1-70). Die Zeitleiste im Zielbild verwendet die lokale Zeitzone des Servers.

## Ablage

```text
<DataDirectory>/
├── settings.json                 Einstellungen (DurableFile)
├── logs/timingapp-YYYYMMDD.log   Serilog
└── media/                        Standard-Medienordner
    ├── Lauf 1/                   Ordner je Rennen (RaceName, FS2-04)
    │   └── 20261007-095959-500/  RecordingId (UTC der ersten Spalte), ggf. Suffix -n, eindeutig über alle Rennen
    │       ├── finish.png        Zielbild mit Zeitleiste
    │       ├── finish.mp4        Zielvideo (falls erzeugt)
    │       ├── front.mp4         Frontvideo (falls erzeugt)
    │       └── recording.json    Metadaten, Zeitstempel, Rennen, Passagen – zuletzt, atomar
    └── 20261006-120000-000/      ältere Aufnahme ohne Rennen
```

- Kritische Dateien schreibt `DurableFile`: temporäre Datei, Flush auf Datenträger, atomares Ersetzen.
- Ein Verzeichnis ohne `recording.json` ist unvollständig und wird nie gelistet oder ausgeliefert.
- Beim Löschen wird `recording.json` zuerst entfernt.
- Eine Aufnahme wird über ihre ID in allen Rennordnern gesucht; ein Ordner mit `recording.json` und gültigem
  ID-Namen ist eine Aufnahme, jeder andere Ordner im Medienordner ein Rennen.
- Später eintreffende Passagen ersetzen `recording.json` atomar (`DurableFile`).
- IDs aus Anfragen werden gegen ein festes Muster geprüft (`RecordingId.Parse`), Dateinamen sind eine feste Liste
  (`RecordingFile`): kein Pfad aus Benutzereingaben.
- Keine Datenbank in Feature Set 1 (Kapitel 9).

## Sicherheit

- Default deny: Fallback-Policy verlangt ein angemeldetes Cookie. Anonym sind nur Login, Logout, `/health`,
  OpenAPI und die SPA-Hülle.
- Policy `control` (Cookie oder API-Key) für `/api/control/*`; Policy `external` (nur API-Key mit Rolle
  `external`) für das Beenden. Das Beenden liegt bewusst außerhalb der Control-Gruppe, weil sich Gruppen- und
  Endpunkt-Policies sonst kombinieren und das Cookie genügen würde.
- PIN und API-Key werden zeitkonstant verglichen und nie protokolliert; Login ist ratenbegrenzt.
- Sicherheitsheader inkl. CSP (`img-src`/`media-src 'self'`), Cookie HttpOnly und SameSite=Strict.

## Fehler

- Erwartete Fehler sind `Result`/`Error` mit stabilen Codes, über HTTP als ProblemDetails mit `code` und `params`.
- Codes von FS-1: `access.wrongPin`, `control.modeInvalid`, `settings.*`, `recording.notFound`,
  `recording.fileNotFound`, `recording.saveFailed`, `camera.openFailed`, `camera.notFound`, `camera.modeNotSupported`, `camera.noFrames`,
  `race.nameRequired`, `race.nameInvalid`, `passage.notRecording`, `passage.startNumberInvalid`,
  `passage.timeRequired`,
  `camera.captureFailed`, `video.ffmpegMissing`, `video.encodingFailed`, `simulator.disabled`.
- Kamerafehler erscheinen je Kamera im Status, Speicherprobleme als `LastProblem` und in den Metadaten.
- Warnungen, die die Aufnahme nicht stoppen, erscheinen je Kamera als `warningCode` (z. B. `camera.exposureNotApplied`).

## Nebenläufigkeit

| Besitzer | Zustand | Übergabe |
|---|---|---|
| Capture-Thread (`CameraWorker`) | Quelle, Mat, Bildraten-Messung | Decoder-Queue (begrenzt, nie blockierend), Column-Channel, Kompressions-Queue, unveränderliche Arrays |
| `ParallelFinishDecoder` | Worker-Tasks, Umsortierung | Spalten in Aufnahmereihenfolge unter einer Sperre an Column-Channel und `LiveStrip` |
| Verarbeitungsschleife (`CaptureSession`) | Hintergrund, Ereignis, Spaltenpuffer, Linienrate | Unveränderlicher `LineStatus`, `RecordingDraft` an den Saver |
| `RecordingSaver` | Speicher-Queue | Ein Leser; Zähler `Pending` |
| `FinishRecordingService` | Aktuelle Sitzung | Befehle über `SemaphoreSlim` serialisiert; Auslöser und Neu-Lernen als volatile Flags |
| `CameraSystem` | Gerätelebenszyklus | Start und Gerätesuche über `SemaphoreSlim`; Start wartet auf `Released` |

## Simulation

`CameraSourceFactory` liefert bei aktivierter Simulation `SyntheticCameraSource` (deterministische Bilder; ein
„Fahrer“ auf der Ziellinie über `PUT /api/simulator/occupancy`) oder `VideoFileCameraSource`. Die Fachlogik ist
dieselbe wie mit echten Kameras. Die Gerätesuche meldet die Geräte „Simulation 0/1“ mit Pfad `simulation:0/1`; die
Auswahl per Name wird für sie genauso aufgelöst wie für echte Kameras.

## Internationalisierung

Texte in `web/src/i18n/messages/<bereich>.ts` (de, en); das Backend liefert nur Codes.
