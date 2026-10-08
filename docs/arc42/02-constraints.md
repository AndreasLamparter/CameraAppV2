# 2 Randbedingungen

## Technisch

| Randbedingung | Erläuterung |
|---|---|
| .NET 10, C# 14, ASP.NET Core 10 | Vorgabe CLAUDE.md §5; `TreatWarningsAsErrors`, Nullable, `InvariantGlobalization` |
| Vue 3, TypeScript strict, Vite, Pinia, Nuxt UI 4, vue-i18n | Vorgabe CLAUDE.md §6; nur freie Lizenzen |
| OpenCvSharp4 4.11 (Apache-2.0) | Kamerazugriff und Bildverarbeitung, nur in `TimingApp.Infrastructure.Camera` |
| ffmpeg als externer Prozess | H.264-Videos für den Browser; nicht mitgeliefert (Lizenz OP-4 offen) |
| USB/UVC-Kameras ohne Hardware-Trigger | Synchronität nur über gemeinsame Zeitbasis und Offsets |
| Windows-Ziel-PC, offline | Keine externen Ressourcen zur Laufzeit; Icons und Fonts werden gebündelt |
| Port 5080 belegt | Die Alt-Anwendung läuft dort; diese Anwendung nutzt 5081 (Tests 5082, Contract-Generierung 5099) |

## Organisatorisch

- Entwicklung nach Feature Sets; nur der angeforderte Umfang wird umgesetzt (YAGNI).
- `scripts\build.ps1` ist das lokale CI-Gate.
- Kein Git-Commit durch die Entwicklungsunterstützung ohne ausdrücklichen Auftrag.
