# Feature Set ZM-1 – Anbindung der Zielkamera an die Zeitmessung

| | |
|---|---|
| Status | Entwurf |
| Betrifft | Zeitmess-Anwendung (RFID-Zeitmessung) |
| Abhängigkeiten | keine; die Schnittstelle der Zielkamera ist in Abschnitt 5 vollständig beschrieben |
| Schnittstelle | REST/JSON über HTTP |

Dieses Dokument beschreibt, **was** die Zeitmess-Anwendung leisten muss, damit die Zielkamera automatisch mit dem
Rennablauf mitläuft und jede erkannte Startnummer in der passenden Zielaufnahme vermerkt wird.

Entscheidungen vom 2026-10-08: ein gemeinsamer Ordner für alle Rennen einer Veranstaltung, dessen Name die Namen
aller Rennen enthält; manuelle Startnummern werden sofort bei der Erfassung gemeldet; die Anbindung wird global
konfiguriert; HTTP im lokalen Netz genügt.

---

## 1. Ziel

Die Zielkamera filmt die Ziellinie und speichert jeden Zieleinlauf als Aufnahme (Zielbild, Zielvideo, Frontvideo).
Die Zeitmessung steuert sie so, dass niemand an der Kamera etwas bedienen muss:

- Mit dem Start des ersten Rennens beginnt die Kamera aufzuzeichnen.
- Mit dem globalen Stopp endet die Aufzeichnung.
- Jede erkannte Startnummer (RFID oder manuell erfasst) wird an die Kamera gemeldet. Die Kamera vermerkt sie in der
  Aufnahme, die den Zeitpunkt der Passage enthält, und markiert sie dort im Zielbild.

## 2. Abgrenzung

Im Umfang:

- Konfiguration der Verbindung zur Zielkamera
- Aufnahme starten und stoppen im Rennablauf
- Melden erkannter Startnummern
- Anzeige des Verbindungszustands

Nicht im Umfang:

- Bedienung der Kamera (Ziellinie, Belichtung, Einstellungen): erfolgt in der Oberfläche der Zielkamera
- Anzeige von Zielbildern oder Videos in der Zeitmessung
- Übernahme von Zeiten aus der Kamera in die Ergebnisliste

## 3. Begriffe

| Begriff | Bedeutung |
|---|---|
| Zielkamera | Eigenständiges System auf einem Rechner an der Ziellinie, das Zieleinläufe aufnimmt und über HTTP gesteuert wird |
| Passage | Eine Startnummer hat zu einem Zeitpunkt die Ziellinie passiert (RFID-Lesung oder manuelle Erfassung) |
| Passagezeit | Uhrzeit der Passage mit Zeitzone, auf Millisekunden genau |
| Rennname | Name, unter dem die Kamera die Aufnahmen ablegt (eigener Ordner je Rennen) |
| API-Schlüssel | Gemeinsames Geheimnis, mit dem sich die Zeitmessung bei der Kamera ausweist |

## 4. Anforderungen

### 4.1 Konfiguration

**ZM-01** Unter **Datenverwaltung** gibt es einen neuen Bereich **Konfiguration → Zielkamera**. Die
Konfiguration gilt global für alle Veranstaltungen und enthält:

| Feld | Pflicht | Beispiel | Bedeutung |
|---|---|---|---|
| Aktiv | ja | an/aus | Schaltet die Anbindung ein oder aus |
| Server | ja | `192.168.178.20` oder `zielkamera` | Name oder IP-Adresse des Kamera-Rechners |
| Port | ja | `5081` | Port der Zielkamera |
| API-Schlüssel | ja | (verdeckte Eingabe) | Wird im Header `X-Api-Key` gesendet |

Server und Port ergeben die Basisadresse `http://<Server>:<Port>` (unverschlüsseltes HTTP im lokalen Netz). Adresse und Schlüssel stellt
der Betreiber der Zielkamera bereit.

**ZM-02** Der API-Schlüssel wird verdeckt angezeigt, nicht protokolliert und nur an die konfigurierte Zielkamera
gesendet.

**ZM-03** Eine Schaltfläche **Verbindung testen** ruft den Zustand der Kamera ab (`GET /api/control/status`) und
zeigt das Ergebnis an: erreichbar mit Betriebszustand, Schlüssel ungültig, oder nicht erreichbar.

**ZM-04** Ist die Anbindung nicht aktiv, sendet die Zeitmessung keine Aufrufe an die Kamera.

### 4.2 Aufnahme starten und stoppen

**ZM-10** Wird das **erste Rennen** einer Veranstaltung gestartet, startet die Zeitmessung die Aufnahme der
Kamera (`POST /api/control/start`, Abschnitt 5.1). Als Rennname sendet sie die Namen **aller Rennen der
Veranstaltung** in ihrer Startreihenfolge, verbunden mit ` - ` (Beispiel: `Lauf 1 - Lauf 2 - Elite`). Die Kamera
legt dadurch alle Aufnahmen der Veranstaltung in einem gemeinsamen Ordner mit diesem Namen ab.

**ZM-11** Werden weitere Rennen gestartet, während die Aufnahme läuft, sendet die Zeitmessung keinen weiteren
Start-Aufruf.

**ZM-12** Beim **globalen Stopp** stoppt die Zeitmessung die Kamera (`POST /api/control/stop`, Abschnitt 5.2).
Die Kamera speichert dabei eine laufende Aufnahme noch vollständig.

**ZM-13** Der gesendete Rennname erfüllt die Regeln der Kamera: Buchstaben, Ziffern, Leerzeichen und `-`, `_`,
`.`, höchstens 120 Zeichen, nicht mit Leerzeichen oder Punkt beginnend oder endend. Die Zeitmessung bildet ihn so:

1. In jedem Rennnamen werden andere Zeichen durch `-` ersetzt.
2. Die Rennnamen werden mit ` - ` verbunden (ZM-10).
3. Ist das Ergebnis länger als 120 Zeichen, wird es auf 120 Zeichen gekürzt.
4. Leerzeichen und Punkte am Anfang und Ende werden entfernt.

### 4.3 Startnummern melden

**ZM-20** Bei **jeder erkannten Startnummer**, per RFID oder manuell erfasst, meldet die Zeitmessung eine Passage
mit Startnummer und Passagezeit (`POST /api/passages`, Abschnitt 5.3).

**ZM-21** Die Passage wird **sofort** gemeldet, spätestens **5 s** nach der Passage. Die Kamera hält die Bilder
nur so lange vor; später gemeldete Passagen verwirft sie. Manuell erfasste Startnummern werden im Moment der
Erfassung gemeldet.

**ZM-22** Die Passagezeit ist die Zeit der Passage (RFID-Lesung bzw. Zeitpunkt der manuellen Erfassung), nicht
der Zeitpunkt des Sendens, mit Zeitzone und Millisekunden.

**ZM-23** Wird dieselbe Startnummer mehrfach erkannt (z. B. Runden), wird jede Erkennung gemeldet.

**ZM-24** Nachträgliche Korrekturen in der Zeitmessung (Startnummer geändert, Passage gelöscht) werden nicht an
die Kamera gemeldet.

**ZM-25** Die Uhren von Zeitmess- und Kamera-Rechner werden synchron gehalten (NTP). Ein verbleibender fester
Versatz wird in der Kamera eingestellt, nicht in der Zeitmessung.

### 4.4 Fehlerverhalten

**ZM-30** Die Aufrufe an die Kamera dürfen die Zeitmessung nicht verzögern oder blockieren: Sie laufen im
Hintergrund mit einer Zeitüberschreitung von 2 s.

**ZM-31** Schlägt ein Aufruf fehl (nicht erreichbar, Zeitüberschreitung, Fehlercode), protokolliert die
Zeitmessung den Fehler mit Fehlercode und zeigt den Zustand der Anbindung sichtbar an (z. B. Symbol rot/grün).
Die Zeitmessung arbeitet ohne Kamera normal weiter.

**ZM-32** Eine fehlgeschlagene Passage wird höchstens so lange erneut gesendet, wie sie noch innerhalb von 5 s
nach der Passage ankommt. Start und Stopp werden nach einem Fehler bis zu dreimal im Abstand von 2 s wiederholt.

---

## 5. Schnittstelle der Zielkamera

Basisadresse: `http://<Server>:<Port>` (ZM-01). Jeder Aufruf sendet die Header
`X-Api-Key: <API-Schlüssel>` und, wenn ein Body gesendet wird, `Content-Type: application/json`.

Fehler antworten als RFC 9457 ProblemDetails mit einem stabilen Code im Feld `code`, z. B.:

```json
{ "status": 400, "code": "race.nameInvalid", "params": { "max": 120 } }
```

Ohne oder mit falschem Schlüssel antwortet die Kamera mit `401`.

### 5.1 Aufnahme starten

```http
POST /api/control/start
{ "mode": "Recording", "raceName": "Lauf 1" }
```

| Antwort | Bedeutung |
|---|---|
| `200` mit Zustand | Aufnahme läuft (auch wenn sie schon lief; ein anderer Rennname wechselt das Rennen) |
| `400 race.nameRequired` | Rennname fehlt |
| `400 race.nameInvalid` | Rennname verletzt ZM-13 |

### 5.2 Aufnahme stoppen

```http
POST /api/control/stop
```

| Antwort | Bedeutung |
|---|---|
| `200` mit Zustand | Kameras gestoppt; laufende Aufnahmen werden noch gespeichert |

### 5.3 Passage melden

```http
POST /api/passages
{ "startNumber": "42", "time": "2026-10-08T10:00:02.123+02:00" }
```

| Feld | Regel |
|---|---|
| `startNumber` | 1–20 Zeichen: Buchstaben, Ziffern, `-` |
| `time` | ISO 8601 mit Zeitzone, Millisekunden |

| Antwort | Bedeutung |
|---|---|
| `202` | Angenommen; die Kamera ordnet die Passage im Hintergrund einer Aufnahme zu |
| `400 passage.startNumberInvalid` | Startnummer verletzt die Regel |
| `400 passage.timeRequired` | Passagezeit fehlt |
| `409 passage.notRecording` | Die Kamera ist nicht im Zustand Aufnahme (z. B. noch nicht gestartet) |

### 5.4 Zustand abfragen (Verbindung testen)

```http
GET /api/control/status
```

Antwort `200` u. a. mit `mode` (`Stopped`, `Preview`, `Recording`), `raceName`, Fehlern je Kamera und
`pendingSaves`.

---

## 6. Akzeptanzkriterien

```gherkin
Funktionalität: Anbindung der Zielkamera

  Szenario: Verbindung testen
    Angenommen Server, Port und API-Schlüssel der Zielkamera sind eingetragen
    Wenn "Verbindung testen" gewählt wird
    Dann wird "erreichbar" mit dem Betriebszustand der Kamera angezeigt

  Szenario: Falscher API-Schlüssel
    Angenommen der eingetragene API-Schlüssel ist falsch
    Wenn "Verbindung testen" gewählt wird
    Dann wird "Schlüssel ungültig" angezeigt

  Szenario: Start mit dem ersten Rennen
    Angenommen die Anbindung ist aktiv und keine Aufnahme läuft
    Und die Veranstaltung hat die Rennen "Lauf 1" und "Lauf 2"
    Wenn das Rennen "Lauf 1" gestartet wird
    Dann sendet die Zeitmessung den Start der Aufnahme mit dem Rennnamen "Lauf 1 - Lauf 2"

  Szenario: Weiteres Rennen
    Angenommen die Aufnahme wurde mit dem Rennnamen "Lauf 1 - Lauf 2" gestartet
    Wenn das Rennen "Lauf 2" gestartet wird
    Dann sendet die Zeitmessung keinen weiteren Start-Aufruf

  Szenario: Globaler Stopp
    Angenommen die Aufnahme läuft
    Wenn der globale Stopp ausgelöst wird
    Dann sendet die Zeitmessung den Stopp der Aufnahme

  Szenario: RFID-Passage
    Angenommen die Aufnahme läuft
    Wenn die Startnummer 42 um 10:00:02.123 per RFID erkannt wird
    Dann meldet die Zeitmessung innerhalb von 5 s die Passage 42 mit der Passagezeit 10:00:02.123

  Szenario: Manuell erfasste Startnummer
    Angenommen die Aufnahme läuft
    Wenn die Startnummer 7 um 10:05:00.000 manuell erfasst wird
    Dann meldet die Zeitmessung die Passage 7 mit der Passagezeit 10:05:00.000

  Szenario: Kamera nicht erreichbar
    Angenommen die Zielkamera ist nicht erreichbar
    Wenn eine Startnummer erkannt wird
    Dann wird die Passage in der Zeitmessung normal erfasst
    Und der Zustand der Anbindung wird als gestört angezeigt

  Szenario: Rennname mit unerlaubten Zeichen
    Angenommen die Veranstaltung hat die Rennen "Lauf 1/Elite" und "Lauf 2"
    Wenn das erste Rennen gestartet wird
    Dann sendet die Zeitmessung den Rennnamen "Lauf 1-Elite - Lauf 2"

  Szenario: Zu langer Rennname
    Angenommen die Namen aller Rennen ergeben verbunden 135 Zeichen
    Wenn das erste Rennen gestartet wird
    Dann sendet die Zeitmessung einen Rennnamen mit höchstens 120 Zeichen
```

---

## 7. Entschiedene Punkte

| Nr. | Frage | Entscheidung (2026-10-08) |
|---|---|---|
| ZM-OP-1 | Ablage bei mehreren Rennen? | Ein gemeinsamer Ordner; sein Name enthält die Namen aller Rennen (ZM-10, ZM-13) |
| ZM-OP-2 | Manuell erfasste Startnummern? | Werden sofort bei der Erfassung gemeldet (ZM-21) |
| ZM-OP-3 | Konfiguration pro Veranstaltung oder global? | Global (ZM-01) |
| ZM-OP-4 | HTTPS nötig? | Nein, HTTP im lokalen Netz genügt (ZM-01) |
