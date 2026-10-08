# Feature Set 2 – Rennen und Startnummern

| | |
|---|---|
| Status | Umgesetzt (2026-10-08) |
| Bounded Context | `FinishRecording` (siehe CLAUDE.md, Abschnitt 9) |
| Abhängigkeiten | [FEATURE-SET-1](FEATURE-SET-1.md) |

Dieses Dokument beschreibt, **was** das System leisten muss.
Wie es technisch umgesetzt wird, regeln `CLAUDE.md` und `docs/arc42/`.

Entscheidungen vom 2026-10-08: Rennname beim Start überall Pflicht (in der Oberfläche über einen Dialog), eigener
Ordner je Rennen, Startnummern als Passage-Meldung der Zeitmessung, Startnummern in den Metadaten der Aufnahme
(nicht ins Video eingeblendet); offene Punkte OP2-1 bis OP2-6 entschieden (Abschnitt 6).

---

## 1. Ziel

Aufnahmen werden einem Rennen zugeordnet, und die Zeitmessung (RFID) meldet, welcher Fahrer wann die Ziellinie
passiert hat. So lässt sich zu jeder Aufnahme ablesen, welche Fahrer darin zu sehen sind.

## 2. Abgrenzung

Im Umfang:

- Rennname beim Start der Aufnahme
- Ablage der Aufnahmen je Rennen
- Passage-Meldungen (Startnummer und Passagezeit) von externen Programmen
- Anzeige von Rennen und Startnummern in Aufnahmeliste und Wiedergabe

Nicht im Umfang:

- Teilnehmerlisten, Namen, Kategorien, Ergebnisse
- Erkennung von Startnummern im Bild (`CameraDetection`)
- Einblenden der Startnummer in Zielbild, Zielvideo oder Frontvideo

## 3. Begriffe

| Begriff | Bedeutung |
|---|---|
| Rennen | Name, unter dem die Aufnahmen eines Zeitraums abgelegt werden |
| Passage | Meldung der Zeitmessung: eine Startnummer hat zu einer Uhrzeit die Ziellinie passiert |
| Startnummer | Kennung des Fahrers, wie sie die Zeitmessung liefert |

## 4. Anforderungen

### 4.1 Rennen

**FS2-01** Der Zustand Aufnahme kann nur mit einem Rennnamen gestartet werden. In der Oberfläche wird der
Rennname in einem Dialog eingegeben, externe Programme geben ihn im Start-Aufruf mit. Ohne gültigen Rennnamen
wird der Start abgelehnt.

**FS2-02** Der Rennname gilt für alle Aufnahmen, bis die Aufnahme erneut gestartet wird. Ein erneuter Start mit
einem anderen Rennnamen wechselt das Rennen, ohne die Kameras neu zu starten.

**FS2-07** Ein Rennname besteht aus Buchstaben, Ziffern, Leerzeichen und den Zeichen `-`, `_` und `.`, ist
höchstens 120 Zeichen lang und beginnt und endet nicht mit Leerzeichen oder Punkt.

**FS2-08** Ist der Startzustand der Anwendung Aufnahme (FS1-04), startet sie in Vorschau, bis ein Rennname
eingegeben wird.

**FS2-03** Jede Aufnahme speichert ihren Rennnamen.

**FS2-04** Die Aufnahmen eines Rennens liegen in einem eigenen Ordner im Speicherort.

**FS2-05** Die Aufnahmeliste zeigt den Rennnamen jeder Aufnahme und kann nach Rennen filtern.

**FS2-06** Der Zustand Vorschau benötigt keinen Rennnamen, da nichts gespeichert wird.

### 4.2 Passagen

**FS2-10** Externe Programme melden Passagen mit Startnummer und Passagezeit.

**FS2-11** Eine Passage wird der Aufnahme zugeordnet, deren Zeitraum die Passagezeit enthält, auch wenn diese
Aufnahme noch gespeichert wird oder bereits gespeichert ist.

**FS2-12** Enthält keine Aufnahme die Passagezeit, entsteht eine Aufnahme um die Passagezeit mit Vor- und
Nachlauf (FS1-22, FS1-26), so als wäre zu diesem Zeitpunkt ein Zielereignis erkannt worden.

**FS2-17** Eine Passage-Meldung trifft höchstens 5 s nach der Passage ein. Bis dahin hält das System die Bilder
vor, die FS2-11 und FS2-12 benötigen.

**FS2-18** Die Uhren von Zeitmessung und Kamera-Rechner werden synchron gehalten (z. B. NTP). Zusätzlich ist ein
Versatz in Millisekunden einstellbar, der auf jede Passagezeit angerechnet wird (Standard 0 ms).

**FS2-19** Jede Meldung ist eine eigene Passage, auch wenn dieselbe Startnummer mehrfach gemeldet wird.

**FS2-13** Passagen werden nur im Zustand Aufnahme angenommen.

**FS2-14** Eine Aufnahme speichert alle ihr zugeordneten Passagen (Startnummer und Passagezeit).

**FS2-15** Die Aufnahmeliste zeigt die Startnummern jeder Aufnahme. Die Wiedergabe markiert jede Passage im
Zielbild an ihrer Passagezeit.

**FS2-16** Passage-Meldungen sind nur für berechtigte Aufrufer möglich (wie FS1-51).

---

## 5. Akzeptanzkriterien

```gherkin
Funktionalität: Rennen und Startnummern

  Szenario: Aufnahme ohne Rennname
    Angenommen die Kameras sind gestoppt
    Wenn die Aufnahme ohne Rennname gestartet wird
    Dann wird der Start abgelehnt

  Szenario: Aufnahmen werden je Rennen abgelegt
    Angenommen die Aufnahme wurde mit dem Rennnamen "Lauf 1" gestartet
    Wenn ein Zielereignis gespeichert wird
    Dann liegt die Aufnahme im Ordner des Rennens "Lauf 1"
    Und die Aufnahmeliste zeigt "Lauf 1" bei dieser Aufnahme

  Szenario: Passage während einer Aufnahme
    Angenommen der Zustand ist "Aufnahme"
    Und eine Aufnahme umfasst 10:00:00.000 bis 10:00:04.000
    Wenn die Passage der Startnummer 42 um 10:00:02.000 gemeldet wird
    Dann enthält diese Aufnahme die Startnummer 42 mit der Passagezeit 10:00:02.000

  Szenario: Passage ohne erkanntes Zielereignis
    Angenommen der Zustand ist "Aufnahme" und die Ziellinie war frei
    Wenn die Passage der Startnummer 7 um 10:05:00.000 gemeldet wird
    Dann entsteht eine Aufnahme, die 10:05:00.000 enthält
    Und sie enthält die Startnummer 7

  Szenario: Rennen wechseln
    Angenommen der Zustand ist "Aufnahme" mit dem Rennnamen "Lauf 1"
    Wenn die Aufnahme mit dem Rennnamen "Lauf 2" gestartet wird
    Dann werden die Kameras nicht neu gestartet
    Und folgende Aufnahmen liegen im Ordner des Rennens "Lauf 2"

  Szenario: Ungültiger Rennname
    Wenn die Aufnahme mit dem Rennnamen "../Lauf" gestartet wird
    Dann wird der Start abgelehnt

  Szenario: Versatz der Zeitmessung
    Angenommen der Versatz der Zeitmessung beträgt +200 ms
    Wenn die Passage der Startnummer 42 um 10:00:02.000 gemeldet wird
    Dann wird sie mit der Passagezeit 10:00:02.200 gespeichert

  Szenario: Passage in der Vorschau
    Angenommen der Zustand ist "Vorschau"
    Wenn eine Passage gemeldet wird
    Dann wird sie abgelehnt
```

---

## 6. Entschiedene Punkte

| Nr. | Frage | Entscheidung (2026-10-08) |
|---|---|---|
| OP2-1 | Rennname beim Startzustand Aufnahme (FS1-04)? | Nur über den Dialog; die Anwendung startet in Vorschau (FS2-08) |
| OP2-2 | Wie spät trifft eine Passage-Meldung ein? | Höchstens 5 s nach der Passage (FS2-17) |
| OP2-3 | Uhr der Passagezeit? | Uhren synchron (NTP) und einstellbarer Versatz (FS2-18) |
| OP2-4 | Zeichen und Länge des Rennnamens? | FS2-07 |
| OP2-5 | Rennwechsel ohne Neustart der Kameras? | Ja, durch erneuten Start (FS2-02) |
| OP2-6 | Dieselbe Startnummer mehrfach? | Jede Meldung ist eine eigene Passage (FS2-19) |
