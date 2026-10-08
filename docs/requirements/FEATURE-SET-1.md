# Feature Set 1 – Zielkamera und Frontkamera

| | |
|---|---|
| Status | Entwurf |
| Bounded Context | `FinishRecording` (siehe CLAUDE.md, Abschnitt 9) |
| Abhängigkeiten | keine |

Dieses Dokument beschreibt, **was** das System leisten muss.
Wie es technisch umgesetzt wird, regeln `CLAUDE.md` und `docs/arc42/`.

---

## 1. Ziel

An der Ziellinie eines MTB-Rennens zeichnet das System zwei Kameras zeitsynchron auf:

- Die **Zielkamera** erzeugt ein Zielbild (Photo-Finish). Pro Kamerabild wird nur der Bildstreifen
  auf der Ziellinie verwendet. Die Streifen werden in zeitlicher Reihenfolge nebeneinander gesetzt.
- Die **Frontkamera** filmt die ankommenden Fahrer frontal zur Identifikation.

Die Aufnahmen dienen der Kontrolle und Dokumentation der Zielankünfte.

## 2. Abgrenzung

Im Umfang:

- Erfassung, Speicherung, Wiedergabe und Löschen von Zielaufnahmen
- Steuerung über die Oberfläche und durch externe Programme
- Konfiguration beider Kameras und der Ziellinie

Nicht im Umfang (spätere Feature Sets):

- Erkennung von Startnummern oder AprilTags (`CameraDetection`)
- Verknüpfung von Aufnahmen mit RFID-Passagen, Rennen oder Teilnehmern
- Zeitbestimmung genauer als eine Spalte (Interpolation zwischen Kamerabildern)
- Industriekameras mit Bildausschnitt (ROI) oder Hardware-Trigger
- Automatisches Löschen alter Aufnahmen

## 3. Begriffe

| Begriff | Bedeutung |
|---|---|
| Zielkamera | Kamera, die senkrecht auf die Ziellinie blickt und das Zielbild liefert |
| Frontkamera | Kamera, die die Fahrer von vorne filmt |
| Ziellinie | Die konfigurierte Pixelspalte (bzw. -zeile) im Bild der Zielkamera |
| Spalte | Der Bildstreifen der Ziellinie aus genau einem Bild der Zielkamera, mit eigenem Zeitstempel |
| Zielbild | Alle Spalten einer Aufnahme nebeneinander; die X-Achse ist die Zeit |
| Linienrate | Anzahl erfasster Spalten pro Sekunde |
| Belegung | Anteil der Ziellinie, der sich gegenüber dem leeren Hintergrund verändert hat |
| Zielereignis | Zeitraum, in dem etwas die Ziellinie passiert, einschließlich Vor- und Nachlauf |
| Aufnahme | Gespeichertes Zielereignis mit allen zugehörigen Bildern, Videos und Zeitstempeln |
| Sitzung | Zeitraum zwischen Starten und Stoppen der Kameras |
| Kamera-Offset | Korrekturwert in Millisekunden für die feste Verzögerung einer Kamera |

## 4. Rahmenbedingungen Hardware

| Kamera | Modell | Fachlich relevante Eigenschaften |
|---|---|---|
| Zielkamera | SVPRO Global Shutter USB 2MP (AR0234), 2,8–12 mm Zoomobjektiv | max. 90 Bilder/s in jeder Auflösung, kein Bildausschnitt, kein Trigger-Eingang |
| Frontkamera | beliebige USB-Webcam | typisch 30 Bilder/s |

Daraus folgt:

- Die Linienrate beträgt höchstens 90 Spalten pro Sekunde. Eine Spalte entspricht damit rund 11 ms.
- Die Kameras können nicht per Hardware synchronisiert werden. Die Synchronität entsteht über eine
  gemeinsame Zeitbasis und kalibrierte Kamera-Offsets (FS1-30 bis FS1-33).

## 5. Anforderungen

### 5.1 Betriebszustände

**FS1-01** Das System kennt drei Betriebszustände:

| Zustand | Kameras | Zielereignisse werden erkannt | Aufnahmen werden gespeichert |
|---|---|---|---|
| Gestoppt | aus | nein | nein |
| Vorschau | an | ja | nein |
| Aufnahme | an | ja | ja |

**FS1-02** Zwischen Vorschau und Aufnahme kann ohne Neustart der Kameras gewechselt werden.

**FS1-03** Wird während eines laufenden Zielereignisses gestoppt, wird dieses Ereignis im Zustand
Aufnahme noch vollständig gespeichert.

**FS1-04** Die Anwendung kann so gestartet werden, dass sie sofort in den Zustand Aufnahme wechselt.

**FS1-05** Kann eine Kamera nicht geöffnet werden oder liefert sie keine Bilder, zeigt das System
den Fehler je Kamera an. Eine fehlende Frontkamera verhindert nicht den Betrieb der Zielkamera.

### 5.2 Zielbild

**FS1-10** Aus jedem Bild der Zielkamera wird genau eine Spalte entnommen und mit dem Zeitpunkt
ihrer Erfassung versehen.

**FS1-11** Position und Breite der Ziellinie sind einstellbar. Ist die Breite größer als ein Pixel,
wird über die Breite gemittelt.

**FS1-12** Die Zeitrichtung des Zielbilds ist umkehrbar, damit die Fahrer im Zielbild in
Fahrtrichtung erscheinen.

**FS1-13** Unter dem Zielbild steht eine Zeitleiste mit Markierungen alle 0,1 s und der Uhrzeit bei
jeder vollen Sekunde.

**FS1-14** Das System misst die tatsächliche Linienrate und zeigt sie an. Liegt sie unter 95 % der
eingestellten Bildrate, wird eine Warnung angezeigt.

### 5.3 Zielereignisse

**FS1-20** Das System lernt den Hintergrund der leeren Ziellinie und passt ihn laufend an
Lichtänderungen an, solange die Linie frei ist.

**FS1-21** Ein Zielereignis beginnt, sobald die Belegung den Schwellwert erreicht. Es endet, wenn die
Belegung für die Dauer des Nachlaufs unter dem Schwellwert bleibt.

**FS1-22** Eine Aufnahme enthält zusätzlich den Vorlauf vor Beginn des Zielereignisses.

**FS1-23** Erreicht ein Zielereignis die maximale Dauer, wird es abgeschlossen und gespeichert. Hält
die Belegung an, beginnt unmittelbar ein neues Zielereignis.

**FS1-24** Ein manueller Auslöser kann ein Zielereignis erzwingen. Solange er aktiv ist, gilt die
Linie als belegt.

**FS1-25** Der Hintergrund kann auf Befehl neu gelernt werden.

**FS1-26** Standardwerte (alle einstellbar):

| Einstellung | Standard |
|---|---|
| Schwellwert Pixeländerung (Grauwert) | 25 |
| Schwellwert Belegung | 2 % |
| Vorlauf Zielkamera | 0,5 s |
| Nachlauf Zielkamera | 1,0 s |
| Maximale Dauer Zielereignis | 60 s |
| Vorlauf Frontkamera | 2,0 s |
| Nachlauf Frontkamera | 2,0 s |

### 5.4 Frontkamera und Synchronisation

**FS1-30** Zu jeder Aufnahme gehört das Frontvideo vom Beginn der Aufnahme minus Vorlauf Frontkamera
bis zum Ende der Aufnahme plus Nachlauf Frontkamera.

**FS1-31** Beide Kameras verwenden dieselbe Zeitbasis. Jede Spalte und jedes Frontbild hat einen
Zeitstempel auf dieser Zeitbasis.

**FS1-32** Für jede Kamera ist ein Offset in Millisekunden einstellbar, der auf ihre Zeitstempel
angerechnet wird.

**FS1-33** Die Wiedergabe zeigt die Zeitdifferenz zwischen der gewählten Spalte und dem angezeigten
Frontbild, damit die Offsets kalibriert werden können.

### 5.5 Speicherung

**FS1-40** Jede Aufnahme besteht aus:

- Zielbild mit Zeitleiste
- Zielvideo (Zielbild als durchlaufendes Video)
- Frontvideo, falls die Frontkamera aktiv war
- Zeitstempel jeder Spalte und jedes Frontbilds

**FS1-41** Aufnahmen werden lokal in einem einstellbaren Ordner gespeichert.

**FS1-42** Eine Aufnahme erscheint erst in der Liste, wenn sie vollständig gespeichert ist.

**FS1-43** Kann ein Video nicht erzeugt werden, bleiben Zielbild und Zeitstempel erhalten, und der
Fehler wird angezeigt.

**FS1-44** Geänderte Einstellungen gelten ab dem nächsten Start der Kameras. Die Oberfläche weist
darauf hin, wenn die Kameras gerade laufen.

### 5.6 Externe Steuerung

**FS1-50** Externe Programme (z. B. die RFID-Zeitmessung) können:

- die Kameras im Zustand Vorschau oder Aufnahme starten
- die Kameras stoppen
- den manuellen Auslöser setzen und zurücksetzen
- den Hintergrund neu lernen lassen
- den aktuellen Zustand abfragen (Betriebszustand, laufendes Zielereignis, Belegung, Linienrate,
  Fehler je Kamera, Anzahl laufender Speichervorgänge)
- die Anwendung beenden; laufende Speichervorgänge werden vorher abgeschlossen

**FS1-51** Externe Aufrufe sind nur für berechtigte Aufrufer möglich.

### 5.7 Oberfläche

**FS1-60** Live-Ansicht:

- Livebild beider Kameras; die Ziellinie ist im Bild der Zielkamera markiert, ihre Farbe zeigt
  frei, belegt oder laufende Aufnahme
- das laufende Zielbild der letzten Sekunden
- Bedienung aller Befehle aus FS1-50 außer dem Beenden der Anwendung
- Linienrate, Bildrate und Fehler je Kamera

**FS1-61** Aufnahmeliste: Datum und Uhrzeit, Dauer, Linienrate, Frontvideo vorhanden, Speichergröße.
Neue Aufnahmen erscheinen ohne manuelles Neuladen.

**FS1-62** Wiedergabe einer Aufnahme:

- Frontvideo und Zielbild werden gemeinsam angezeigt
- das Zielbild ist horizontal und vertikal zoombar und scrollbar
- ein Klick ins Zielbild wählt eine Spalte und zeigt das zeitlich nächste Frontbild
- beim Abspielen des Frontvideos folgt die Markierung im Zielbild
- schrittweises Bewegen um eine Spalte und um ein Frontbild per Tastatur
- Anzeige der Uhrzeit der gewählten Spalte und des angezeigten Frontbilds sowie ihrer Differenz
- Abspielen des Zielvideos
- Herunterladen von Zielbild, Zielvideo und Frontvideo

**FS1-63** Aufnahmen können einzeln gelöscht werden, jeweils nach Bestätigung.

**FS1-64** Einstellungen: Speicherort, beide Kameras (Gerät, Auflösung, Bildrate, Belichtung,
Offset; Frontkamera zusätzlich aktiv/inaktiv), Ziellinie, Zielereignis-Werte aus FS1-26. Zusätzlich zeigen
die Einstellungen die Aufrufe für externe Programme aus FS1-50 mit der Adresse dieses Rechners und den
Schlüssel der externen Steuerung (verdeckt, aufdeckbar) zum Kopieren an.

**FS1-65** Die Oberfläche ist auf Deutsch und Englisch verfügbar.

### 5.8 Zeitdarstellung

**FS1-70** Uhrzeiten werden in Ortszeit als `HH:mm:ss.fff` angezeigt. Nachkommastellen werden
abgeschnitten, nicht gerundet.

### 5.9 Betrieb

**FS1-80** Das System funktioniert ohne Internetverbindung.

---

## 6. Akzeptanzkriterien

```gherkin
Funktionalität: Zielkamera und Frontkamera

  Szenario: Aufnahme starten
    Angenommen die Kameras sind gestoppt
    Wenn die Aufnahme gestartet wird
    Dann ist der Zustand "Aufnahme"
    Und für beide Kameras wird eine Bildrate angezeigt

  Szenario: Vorschau speichert keine Aufnahmen
    Angenommen der Zustand ist "Vorschau"
    Wenn ein Fahrer die Ziellinie passiert
    Dann wird ein Zielereignis angezeigt
    Und die Aufnahmeliste bleibt unverändert

  Szenario: Zielereignis wird mit Vor- und Nachlauf gespeichert
    Angenommen der Zustand ist "Aufnahme"
    Und der Vorlauf beträgt 0,5 s und der Nachlauf 1,0 s
    Wenn die Ziellinie von 10:00:00.000 bis 10:00:02.000 belegt ist
    Dann entsteht eine Aufnahme, deren erste Spalte nicht später als 09:59:59.500 liegt
    Und deren letzte Spalte nicht früher als 10:00:03.000 liegt

  Szenario: Frontvideo deckt den Zeitraum der Aufnahme ab
    Angenommen der Zustand ist "Aufnahme" und die Frontkamera ist aktiv
    Und Vorlauf und Nachlauf der Frontkamera betragen je 2,0 s
    Wenn eine Aufnahme von 09:59:59.500 bis 10:00:03.000 gespeichert wird
    Dann beginnt das Frontvideo nicht später als 09:59:57.500
    Und es endet nicht früher als 10:00:05.000

  Szenario: Maximale Dauer eines Zielereignisses
    Angenommen der Zustand ist "Aufnahme" und die maximale Dauer beträgt 60 s
    Wenn die Ziellinie 90 s ununterbrochen belegt ist
    Dann entstehen zwei aufeinanderfolgende Aufnahmen

  Szenario: Manueller Auslöser
    Angenommen der Zustand ist "Aufnahme" und die Ziellinie ist frei
    Wenn der manuelle Auslöser gesetzt und nach 3 s zurückgesetzt wird
    Dann entsteht eine Aufnahme

  Szenario: Stoppen während eines Zielereignisses
    Angenommen der Zustand ist "Aufnahme" und ein Zielereignis läuft
    Wenn die Kameras gestoppt werden
    Dann erscheint die Aufnahme dieses Zielereignisses vollständig in der Liste

  Szenario: Unvollständige Aufnahme wird nicht angezeigt
    Angenommen eine Aufnahme wird gerade gespeichert
    Wenn die Aufnahmeliste abgefragt wird
    Dann ist diese Aufnahme nicht enthalten

  Szenario: Frontkamera fehlt
    Angenommen die Frontkamera ist aktiviert, aber nicht angeschlossen
    Wenn die Aufnahme gestartet wird
    Dann wird für die Frontkamera ein Fehler angezeigt
    Und Zielereignisse werden weiterhin ohne Frontvideo gespeichert

  Szenario: Zu niedrige Linienrate
    Angenommen die Bildrate der Zielkamera ist auf 90 eingestellt
    Wenn die gemessene Linienrate 80 Spalten pro Sekunde beträgt
    Dann wird eine Warnung zur Linienrate angezeigt

  Szenario: Video kann nicht erzeugt werden
    Angenommen die Videoerzeugung ist nicht möglich
    Wenn ein Zielereignis gespeichert wird
    Dann enthält die Aufnahme das Zielbild und die Zeitstempel
    Und ein Fehler zur Videoerzeugung wird angezeigt

  Szenario: Synchrone Wiedergabe per Klick
    Angenommen eine Aufnahme mit Frontvideo ist geöffnet
    Wenn eine Spalte im Zielbild angeklickt wird
    Dann zeigt das Frontvideo das Bild mit dem nächstgelegenen Zeitstempel
    Und die Zeitdifferenz zwischen Spalte und Frontbild wird angezeigt

  Szenario: Markierung folgt dem Frontvideo
    Angenommen eine Aufnahme mit Frontvideo ist geöffnet
    Wenn das Frontvideo abgespielt wird
    Dann steht die Markierung im Zielbild auf der Spalte mit dem nächstgelegenen Zeitstempel

  Szenario: Kamera-Offset wird angewendet
    Angenommen der Offset der Frontkamera ist auf -40 ms eingestellt
    Wenn eine Aufnahme gespeichert wird
    Dann sind alle Zeitstempel der Frontbilder um 40 ms früher als ohne Offset

  Szenario: Aufnahme löschen
    Angenommen eine Aufnahme ist in der Liste
    Wenn das Löschen gewählt und bestätigt wird
    Dann ist die Aufnahme nicht mehr in der Liste
    Und ihre Dateien sind entfernt

  Szenario: Löschen abbrechen
    Angenommen eine Aufnahme ist in der Liste
    Wenn das Löschen gewählt und abgebrochen wird
    Dann ist die Aufnahme weiterhin in der Liste

  Szenario: Einstellungen während des Betriebs ändern
    Angenommen der Zustand ist "Aufnahme"
    Wenn der Nachlauf geändert und gespeichert wird
    Dann wird darauf hingewiesen, dass die Änderung ab dem nächsten Start gilt

  Szenario: Externe Steuerung
    Angenommen ein berechtigtes externes Programm
    Wenn es die Aufnahme startet und danach den Zustand abfragt
    Dann ist der Zustand "Aufnahme"

  Szenario: Externe Steuerung ohne Berechtigung
    Angenommen ein externes Programm ohne Berechtigung
    Wenn es die Aufnahme startet
    Dann wird der Aufruf abgelehnt
    Und der Zustand bleibt unverändert

  Szenario: Anwendung extern beenden
    Angenommen eine Aufnahme wird gerade gespeichert
    Wenn ein berechtigtes externes Programm die Anwendung beendet
    Dann wird die Aufnahme vollständig gespeichert, bevor die Anwendung endet

  Szenario: Uhrzeit wird abgeschnitten
    Angenommen eine Spalte hat den Zeitstempel 10:00:00.4567
    Wenn die Spalte ausgewählt wird
    Dann wird "10:00:00.456" angezeigt
```

---

## 7. Offene Punkte

Diese Punkte sind nicht entschieden und dürfen nicht stillschweigend umgesetzt werden
(CLAUDE.md, Abschnitt 44).

| Nr. | Frage |
|---|---|
| OP-1 | ~~Wie authentifizieren sich externe Programme (FS1-51)?~~ Entschieden: [ADR-001](adr/ADR-001-external-control-api-key.md) |
| OP-2 | Wie lange muss das System am Stück laufen (z. B. ein Renntag)? |
| OP-3 | Soll bei wenig freiem Speicherplatz gewarnt oder die Aufnahme gestoppt werden? |
| OP-4 | Ist der Lizenzrahmen des Video-Encoders (ffmpeg-Build) bestätigt? |
| OP-5 | Bleibt `FinishRecording` ein eigener Bounded Context oder wird er Teil von `CameraDetection`? |
| OP-6 | Ist die Zielbild-Genauigkeit von ca. 11 ms für den Einsatzzweck ausreichend, oder wird Interpolation (spätere Feature Sets) benötigt? |
