# Feature Set 1 – Umsetzungsstand und Interpretationen

Stand: 2026-10-07. Anforderungen: [FEATURE-SET-1.md](../requirements/FEATURE-SET-1.md).
Architektur: [arc42](../arc42/README.md).

## Umgesetzt

Alle Anforderungen FS1-01 bis FS1-80 sind umgesetzt. Jedes Gherkin-Szenario ist durch einen automatischen Test
abgedeckt:

| Szenario | Test |
|---|---|
| Aufnahme starten | `FeatureSet1AcceptanceTests.Szenario_AufnahmeStarten`, Playwright `finish-recording.spec.ts` |
| Vorschau speichert keine Aufnahmen | `Szenario_VorschauSpeichertKeineAufnahmen` |
| Zielereignis wird mit Vor- und Nachlauf gespeichert | `Szenario_ZielereignisWirdMitVorUndNachlaufGespeichert` |
| Frontvideo deckt den Zeitraum der Aufnahme ab | `Szenario_FrontvideoDecktDenZeitraumDerAufnahmeAb` |
| Maximale Dauer eines Zielereignisses | `Szenario_MaximaleDauerEinesZielereignisses` |
| Manueller Auslöser | `Szenario_ManuellerAusloeser` |
| Stoppen während eines Zielereignisses | `Szenario_StoppenWaehrendEinesZielereignisses` |
| Unvollständige Aufnahme wird nicht angezeigt | `StorageTests.Szenario_UnvollstaendigeAufnahmeWirdNichtAngezeigt` |
| Frontkamera fehlt | `Szenario_FrontkameraFehlt` (Application), `CameraPipelineTests.Szenario_FrontkameraFehlt_FinishCameraKeepsRunning` |
| Zu niedrige Linienrate | `Szenario_ZuNiedrigeLinienrate` |
| Video kann nicht erzeugt werden | `Szenario_VideoKannNichtErzeugtWerden`, `ApiTests.Recording_ThroughSimulatedCameras_…`, Playwright |
| Synchrone Wiedergabe per Klick | `views.spec.ts` (PlaybackView) |
| Markierung folgt dem Frontvideo | `playbackSync.spec.ts` (Bildzuordnung beim Abspielen); Browser-Wiedergabe nur mit echtem Video prüfbar |
| Kamera-Offset wird angewendet | `CameraPipelineTests.Szenario_KameraOffsetWirdAngewendet` |
| Aufnahme löschen / Löschen abbrechen | `views.spec.ts` (RecordingsView), `StorageTests.Szenario_AufnahmeLoeschen_…` |
| Einstellungen während des Betriebs ändern | `Szenario_EinstellungenWaehrendDesBetriebsAendern`, `views.spec.ts` (SettingsView) |
| Externe Steuerung / ohne Berechtigung | `ApiTests.Szenario_ExterneSteuerung`, `ApiTests.Szenario_ExterneSteuerungOhneBerechtigung` |
| Anwendung extern beenden | `Szenario_AnwendungExternBeenden`, `ApiTests.Shutdown_ByExternalProgram_StopsTheApplication` |
| Uhrzeit wird abgeschnitten | `format.spec.ts` |

## Teamentscheidungen

- OP-1: externe Programme per `X-Api-Key` – [ADR-001](../requirements/adr/ADR-001-external-control-api-key.md)
- Oberfläche immer mit PIN-Anmeldung – [ADR-002](../requirements/adr/ADR-002-operator-pin-login.md)
- ffmpeg wird nicht mitgeliefert; der Betreiber installiert einen Build (Lizenz: OP-4 weiterhin offen)

## Interpretationen – bitte bestätigen

Die Anforderungen lassen hier Spielraum. Umgesetzt ist jeweils die genannte Variante; jede ist mit wenig Aufwand
änderbar.

1. **Hintergrund während eines Zielereignisses** (FS1-20): Der Hintergrund wird nur angepasst, wenn die Linie frei
   ist *und kein Zielereignis läuft* (auch nicht im Nachlauf) und der manuelle Auslöser nicht aktiv ist. Sonst
   könnte ein Fahrer, dessen Farbe dem Hintergrund ähnelt, in den Hintergrund „eingelernt“ werden; die Linie bliebe
   danach dauerhaft belegt.
2. **Lernphase**: Nach dem Start und nach „Hintergrund neu lernen“ werden die ersten 30 Spalten (bei 90/s ca.
   0,33 s) gemittelt. In dieser Zeit wird kein Zielereignis erkannt.
3. **Wechsel Vorschau → Aufnahme während eines Zielereignisses**: Das Ereignis wird gespeichert, wenn während
   seiner Dauer irgendwann der Zustand Aufnahme bestand.
4. **Maximale Dauer** (FS1-23): Das Folgeereignis beginnt mit derselben Spalte, mit der das erste endet. Seine
   Aufnahme enthält wieder den Vorlauf, überlappt also um den Vorlauf mit der vorherigen.
5. **Stoppen während eines Zielereignisses** (FS1-03): Das Ereignis endet mit der letzten erfassten Spalte; Nachlauf
   und Front-Nachlauf enthalten nur, was bis zum Stopp erfasst wurde.
6. **Fenstergrenzen**: Aufnahme und Frontvideo enthalten zusätzlich die letzte Spalte bzw. das letzte Frontbild vor
   dem Fensterbeginn und das erste nach dem Fensterende. So gilt „nicht später als / nicht früher als“ auch dann,
   wenn kein Bild genau auf der Grenze liegt.
7. **Frontvideo** (FS1-30, FS1-40): Ein Frontvideo entsteht ab zwei Frontbildern. Fehlt die Frontkamera, wird die
   Aufnahme ohne Frontvideo gespeichert; das gilt nicht als Speicherproblem, der Kamerafehler wird separat angezeigt.
8. **Speicherort** (FS1-41, FS1-44): Neue Aufnahmen landen im Ordner, der beim Start der Kameras eingestellt war. Die
   Liste zeigt immer den aktuell eingestellten Ordner.
9. **Zielvideo** (FS1-40): Ein 640 Pixel breites Fenster läuft über das Zielbild, eine Spalte pro Videobild mit der
   gemessenen Linienrate, also in Echtzeit.
10. **Linienraten-Warnung** (FS1-14): Gemessen wird über die letzten 2 s, frühestens nach 1 s Messdauer.
11. **Belichtung** (FS1-64): Treiberwert (log2 Sekunden, z. B. −7 ≈ 1/128 s) oder automatisch. Die Belichtungszeit
    begrenzt die Bildrate; für 90 Bilder/s höchstens −7.
12. **Geräteauswahl**: per Kamera aus der Gerätesuche; gespeichert werden Gerätename und Gerätepfad (Media
    Foundation bzw. DirectShow, je nach Backend; verglichen ohne das Schnittstellen-Suffix `#{…}`). Beim Kamerastart wird der Index über den Pfad, sonst über den Namen gesucht,
    wenn genau eine Kamera so heißt; sonst `camera.notFound`, es wird keine andere Kamera geöffnet. Ohne Gerätenamen
    (Standardeinstellungen, ältere Einstellungsdateien, V4L2) gilt der Index.
    Die Gerätesuche liefert die Aufnahmemodi der Kamera; bei der Auswahl einer Kamera wird ein Modus vorgewählt
    (Zielkamera: höchste Bildrate, dann größtes Bild; Frontkamera: größtes Bild, dann höchste Bildrate). Breite, Höhe
    und Bildrate bleiben änderbar. Liegt die Ziellinie danach außerhalb des Bildes, zeigt die Oberfläche einen Hinweis;
    die Linie wird nicht verschoben.
13. **Bilddrehung der Zielkamera**: 0°, 90° rechts, 90° links, 180°. Live-Bild, Ziellinie (immer senkrecht, Position
    vom linken Rand des gedrehten Bildes) und Zielbild beziehen sich auf das gedrehte Bild, die Fahrer stehen darin
    aufrecht; die Zeitleiste bleibt unter dem Zielbild (FS1-13). Ersetzt die frühere Einstellung „Ausrichtung“
    (Spalte/Zeile): „waagerecht“ wird beim Laden zu „90° rechts“ mit derselben Pixelzeile.
14. **Zielbild-Format**: PNG (verlustfrei); die Zeitleiste ist Teil des Bildes. Die Wiedergabe zeigt sie unabhängig
    vom vertikalen Zoom in Originalhöhe.

## Noch offen

- OP-2 Dauerbetrieb, OP-3 Speicherplatz, OP-4 ffmpeg-Lizenz, OP-5 Bounded Context, OP-6 Genauigkeit:
  unverändert offen, nicht umgesetzt.
- **Test mit echter Hardware teilweise erfolgt**: SVPRO AR0234 mit 87–91 Bilder/s (MJPG, Media Foundation direkt)
  gleichzeitig mit der eMeet-Webcam (25 Bilder/s); offen: Dauerbetrieb, Kamera-Offset kalibrieren.
- **Echter ffmpeg-Lauf steht aus**: Auf dem Entwicklungs-PC ist kein ffmpeg installiert. Die Argumente sind
  getestet, die Videoerzeugung selbst nicht (`-fps_mode` setzt ffmpeg ≥ 5.1 voraus).
