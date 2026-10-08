# Feature Set 2 – Rennen und Startnummern

| | |
|---|---|
| Status | Entwurf |
| Bounded Context | `FinishRecording` (siehe CLAUDE.md, Abschnitt 9) |
| Abhängigkeiten | [FEATURE-SET-1](FEATURE-SET-1.md) |

Dieses Dokument beschreibt, **was** das System leisten muss.
Wie es technisch umgesetzt wird, regeln `CLAUDE.md` und `docs/arc42/`.

Entscheidungen vom 2026-10-08: Rennname beim Start überall Pflicht, eigener Ordner je Rennen, Startnummern
als Passage-Meldung der Zeitmessung, Startnummern in den Metadaten der Aufnahme (nicht ins Video eingeblendet).

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

**FS2-01** Der Zustand Aufnahme kann nur mit einem Rennnamen gestartet werden, über die Oberfläche wie über
externe Programme. Ohne Rennnamen wird der Start abgelehnt.

**FS2-02** Der Rennname gilt für alle Aufnahmen, bis die Aufnahme erneut gestartet wird.

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

  Szenario: Passage in der Vorschau
    Angenommen der Zustand ist "Vorschau"
    Wenn eine Passage gemeldet wird
    Dann wird sie abgelehnt
```

---

## 6. Offene Punkte

Diese Punkte sind nicht entschieden und dürfen nicht stillschweigend umgesetzt werden
(CLAUDE.md, Abschnitt 41).

| Nr. | Frage |
|---|---|
| OP2-1 | FS1-04 (sofort in Aufnahme starten) braucht einen Rennnamen: aus der Konfiguration, oder startet die Anwendung dann in Vorschau? |
| OP2-2 | Wie spät nach der Passage trifft die Meldung höchstens ein? Davon hängt ab, wie lange Spalten und Frontbilder für FS2-12 vorgehalten werden. |
| OP2-3 | Auf welcher Uhr beruht die Passagezeit? Die Uhren von Zeitmessung und Kamera-PC müssen synchron sein (z. B. NTP), oder es braucht einen einstellbaren Versatz. |
| OP2-4 | Welche Zeichen und welche Länge sind im Rennnamen erlaubt (er wird zum Ordnernamen)? |
| OP2-5 | Kann der Rennname während der Aufnahme gewechselt werden, ohne die Kameras neu zu starten? |
| OP2-6 | Was gilt, wenn dieselbe Startnummer mehrfach gemeldet wird (z. B. Runden)? |
