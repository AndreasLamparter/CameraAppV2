# CLAUDE.md

This file defines the technical architecture, engineering rules and working agreement for Claude in this repository.

**Functional and domain requirements do not belong in this file.**

Authoritative functional requirements are located under:

`docs/requirements/`

---

# 1. Requirements

## Source of truths

Functional and domain requirements are defined outside this file.

Primary locations:

```text
docs/requirements/
├── FEATURE-SET-1.md
├── DOMAIN-RULES.md
└── adr/
```

Additional requirement documents may be added later.

Architecture documentation (arc42) is located under:

`docs/arc42/`

It describes how the system is built and must be kept current (see section 43).

Do not duplicate functional rules from these documents in `CLAUDE.md`.

Examples of rules that belong in requirements, not here:

- camera behavior
- finish-line event detection (thresholds, pre-roll, post-roll, maximum duration)
- which media files a finish recording consists of
- playback and synchronization behavior visible to the user
- stop behavior

---

# 2. Requirement precedence

When implementing functionality, use this precedence:

1. Explicit current team decision
2. Current applicable ADR
3. Current Feature Set
4. `DOMAIN-RULES.md`
5. Other current requirement documents
6. Existing implementation
7. Old application

Superseded ADRs do not count.

Existing code is never automatically a requirement.

When code and requirements disagree:

1. identify the conflict;
2. reference the applicable requirement;
3. explain the difference;
4. propose the smallest correct change;
5. update implementation and tests together.

Never silently preserve obsolete behavior because existing tests expect it.

---

# 3. Feature Set discipline

Development is organized by Feature Sets.

Before implementing a feature:

1. Read the requested Feature Set.
2. Read referenced domain rules.
3. Read applicable ADRs.
4. Identify affected bounded contexts.
5. Implement only the requested scope.
6. Add or update tests.
7. Run applicable quality gates.

Do not automatically implement functionality from a later Feature Set.

Future requirements may influence architecture where necessary, but must not cause speculative implementation.

Apply:

**YAGNI**

---

# 4. Repository purpose

This repository contains the finish camera application:

`CameraApp`

It is a rebuild based on the current requirements.

It is **not** a rewrite of the old application.

The old application may be used as technical reference for:

- camera / OCR integration
- camera hardware behavior

Do not copy old architecture or domain rules merely because they exist there.

---

# 5. Backend stack

Use:

- **C# 14**
- **.NET 10 LTS**
- target framework `net10.0`
- **ASP.NET Core 10**
- nullable reference types
- `TreatWarningsAsErrors`
- async/await end-to-end
- xUnit v3
- Microsoft.Testing.Platform

Camera and media infrastructure additionally uses:

- **OpenCvSharp4** (camera capture and image processing, only in `CameraApp.Infrastructure.Camera`)
- **ffmpeg** as an external process for video encoding (runtime prerequisite on the finish PC, see sections 27 and 40)

Do not change:

- .NET version
- C# version
- primary web framework

without an explicit team decision.

---

# 6. Frontend stack

Use:

- **Vue 3**
- Composition API
- `<script setup lang="ts">`
- **TypeScript strict mode**
- **Vite**
- **Pinia**
- **Vue Router**
- **Nuxt UI** (Vue mode via `@nuxt/ui/vite`, Tailwind CSS 4, Lucide icons bundled at build time)
- **vue-i18n**
- **ESLint**
- **Prettier**
- **Vitest**
- **Vue Test Utils**
- **Playwright**

Do not use `any` unless an external API makes it unavoidable and the usage is explicitly documented.

Only free, open-source dependencies (MIT, Apache-2.0, BSD, ISC or comparable).
No commercially licensed frameworks or libraries (e.g. PrimeVue 5 / PrimeUI License).
Check the license before adding or upgrading a dependency.

The finish PC may be offline: no runtime loading of fonts, icons or scripts from external services.
Fonts are bundled through npm packages (e.g. `@fontsource`), icons are registered locally at build time.

---

# 7. Clean Code

Apply:

- SOLID
- DRY
- KISS
- YAGNI
- Boy Scout Rule
- Intention-Revealing Names
- Small Functions
- One Level of Abstraction
- Command-Query Separation
- Tell, Don't Ask
- Law of Demeter
- Fail Fast
- Composition over Inheritance
- Immutability by default

Prefer:

- `record`
- `readonly`
- `init`
- strongly typed IDs
- enums/value objects instead of magic values

Avoid:

- Primitive Obsession
- Feature Envy
- Long Method
- Shotgun Surgery
- Divergent Change
- Data Clumps

---

# 8. Clean Architecture

Apply the Dependency Rule.

Dependencies point inward.

Architecture:

```text
Domain
   ↑
Application
   ↑
Infrastructure
   ↑
API
```

Infrastructure implements ports defined by Application or Domain where appropriate.

Presentation must remain thin.

---

# 9. Bounded Contexts

Current bounded contexts:

```text
FinishRecording
CameraDetection
SharedKernel
```

`FinishRecording` covers the technical recording of the finish line (finish-line camera and front camera).
`CameraDetection` remains responsible for detection (e.g. tags / start numbers).
Both may share camera infrastructure, but not aggregates.

Bounded contexts communicate through explicit contracts and IDs.

Do not directly couple aggregate implementations across bounded contexts.

Infrastructure adapters bridge contexts where necessary.

---

# 10. Project structure

## `src/CameraApp.Domain`

Contains:

- entities
- value objects
- aggregates
- domain events
- domain services
- domain rules

Rules:

- BCL only
- no ASP.NET Core
- no networking
- no file system
- no infrastructure
- no hardware SDK

---

## `src/CameraApp.Application`

Contains:

- use cases
- commands
- queries
- handlers
- ports
- DTOs
- validation
- stable error codes

Application defines interfaces.

Infrastructure implements them.

Do not put hardware implementation here.

---

## `src/CameraApp.Infrastructure`

Contains:

- storage
- durable files
- JSON configuration stores
- infrastructure adapters
- production `TimeProvider`

---

## `src/CameraApp.Infrastructure.Camera`

Contains:

- `ICameraSource` adapters (OpenCvSharp / USB-UVC; selectable capture backend such as DSHOW, MSMF, V4L2)
- capture threads and frame timestamping
- line extraction for the finish-line (slit-scan) image
- frame ring buffers (compressed frames)
- video encoding through the external `ffmpeg` process
- live preview encoding (MJPEG)
- camera simulator / video-file replay

Rules:

- OpenCvSharp and `Mat` do not leave this project; Application and Domain only see their own abstractions and metadata
- industrial camera SDKs or hardware trigger integrations only after an explicit decision
- no race rules in camera adapters

---

## `src/CameraApp.Api`

Contains:

- ASP.NET Core
- Minimal APIs
- authentication
- authorization
- ProblemDetails
- OpenAPI
- SignalR
- dependency injection / composition root
- Vue static files

Keep endpoints thin.

---

## `web`

Contains the Vue frontend.

Prefer feature-oriented structure:

```text
web/src/features/
├── camera/
└── finishRecording/
```

Do not organize the entire application primarily around generic folders such as:

```text
components/
services/
pages/
```

---

# 11. Dependency Injection

Use:

`Microsoft.Extensions.DependencyInjection`

Constructor injection is the default.

Avoid:

- service locator
- static mutable dependencies
- global state

Use:

`IOptions<T>`

for static application configuration.

Runtime-editable configuration uses explicit application ports and durable stores.

---

# 12. Time

Use:

`TimeProvider`

for application time.

Production provides a monotonic implementation where required.

Do not directly use:

```csharp
DateTime.Now
DateTime.UtcNow
```

inside Domain or Application.

Hardware-specific clocks must not leak directly into domain logic.

Camera frame timestamps:

- are taken in `CameraApp.Infrastructure.Camera` immediately after frame acquisition;
- come from one shared monotonic capture clock (`TimeProvider.GetTimestamp()`), anchored once to UTC;
- use the same clock instance for all cameras, so recordings of different cameras are comparable;
- may be corrected per camera by a configurable latency offset; offsets are configuration, not code constants.

Functional definitions of timestamps belong in the requirements.

---

# 13. Async

Use async/await end-to-end.

Every asynchronous public API should accept:

`CancellationToken`

where cancellation is meaningful.

Never use:

```csharp
.Result
.Wait()
```

Use `ConfigureAwait(false)` in reusable library code where appropriate.

Use `IAsyncEnumerable<T>` where streaming is genuinely useful.

Exception: camera capture uses dedicated threads (see sections 27 and 40).

---

# 14. Storage

Data directory:

`CameraApp:Storage:DataDirectory`

Production default:

```text
C:\ProgramData\CameraApp
```

Development may use:

```text
src/CameraApp.Api/data
```

Durable files use atomic replacement through the shared durable-file abstraction.

Do not implement ad-hoc file writing for critical data.

Recorded media (images, videos, timestamp sidecars):

- are stored on the file system;
- default media root: `<DataDirectory>/media`; the media root is runtime-configurable;
- one directory per recording;
- metadata files are written last through the durable-file abstraction, so incomplete recordings are never treated as complete;
- recording IDs and file names from requests are validated against a fixed pattern / whitelist (no path traversal).

---

# 15. Error model

Expected failures use:

- `Result`
- `Error`
- stable error codes

Exceptions represent unexpected technical failures.

Backend error codes are part of the API contract.

Example:

```text
camera.notFound
```

HTTP failures use RFC 9457 ProblemDetails.

Extensions may include:

```text
code
params
```

Do not generate translated backend error text for normal application errors.

---

# 16. Logging

Use:

`ILogger<T>`

Use structured message templates.

Good:

```csharp
logger.LogInformation(
    "Camera {CameraId} opened with {Width}x{Height} at {Fps} fps",
    cameraId,
    width,
    height,
    fps);
```

Avoid:

```csharp
logger.LogInformation($"Camera {cameraId} opened");
```

Never log:

- authentication secrets
- sensitive credentials

---

# 17. API

Use ASP.NET Core Minimal APIs.

Endpoints should:

1. validate transport-level input;
2. invoke Application;
3. map result to HTTP;
4. contain no substantial business logic.

Use OpenAPI as API contract.

Binary and streaming endpoints are allowed exceptions to JSON responses:

- media files with HTTP range requests (required for video seeking in the browser)
- MJPEG live previews (`multipart/x-mixed-replace`)

They are still described in OpenAPI and protected like all other endpoints.

---

# 18. OpenAPI

Contract snapshot:

```text
openapi/openapi.json
```

Generated frontend types:

```text
web/src/api/generated/schema.ts
```

After endpoint or DTO changes run:

```powershell
scripts\generate-api.ps1
```

Contract tests must fail when the checked-in OpenAPI contract is outdated.

---

# 19. Frontend API access

Components must not call:

- `fetch`
- `axios`

directly.

Use the typed API layer:

```text
web/src/api/client.ts
```

Media and preview URLs (`<img>`, `<video>`) are also built through the API layer, not assembled in components.

Transport DTOs should not automatically become presentation View Models.

Map at boundaries where useful.

---

# 20. Vue conventions

Use:

- Vue 3 Composition API
- Single File Components
- `<script setup>`
- typed props
- typed emits
- composables named `useXxx`
- PascalCase component names

Prefer:

`computed`

over `watch`.

Use `watch` for side effects only.

Use:

- `ref` for simple reactive values
- `reactive` sparingly

Never mutate props.

Always use `:key` with `v-for`.

Do not place `v-if` and `v-for` on the same element.

Lazy-load routes where appropriate.

---

# 21. Pinia

Use Pinia for application-level frontend state.

Prefer one store per feature/bounded-context responsibility.

Prefer setup-store syntax.

Stores coordinate frontend state.

Do not move authoritative business rules from backend/domain into Pinia.

---

# 22. Internationalization

UI supports:

- German
- English

User-facing text belongs in:

```text
web/src/i18n/messages/<area>.ts
```

Pattern:

```ts
const de = { ... }
const en: typeof de = { ... }
```

Components use:

```ts
t('<area>.<key>')
```

Do not hard-code user-facing text into Vue components.

---

# 23. Security

Apply OWASP Top 10 principles.

Backend:

- default-deny authorization
- explicit anonymous endpoints
- validate input
- protect privileged actions
- no secrets in logs

Frontend:

- no authentication tokens in `localStorage`
- prefer HttpOnly cookies
- no untrusted `v-html`
- apply CSP
- treat backend strings as untrusted

---

# 24. Operator access

Operator authorization is the fallback policy.

New endpoints are protected by default.

Anonymous access must be explicitly justified.

Local finish-PC behavior and remote-device authentication follow the applicable technical security design and functional requirements.

Machine-to-machine control (e.g. starting and stopping recording from external software) uses an explicit, documented authentication mechanism. It is never anonymous.

Do not make endpoints anonymous for convenience.

---

# 25. SignalR

SignalR is used for live push where appropriate.

Keep hubs thin.

Hubs transport application state/events.

They do not contain domain rules.

Clients must tolerate disconnect/reconnect.

Where required by the feature, use a fallback such as polling.

Camera and recording state is pushed via SignalR.
Live video previews use HTTP streaming (section 17), not SignalR.

---

# 26. Hardware integrations

Hardware implementations belong behind ports.

Examples:

```text
ICameraSource
IVideoEncoder
```

Domain and Application must not depend directly on:

- vendor DLLs
- OpenCV
- camera drivers
- ffmpeg
- file-based simulator implementations

Hardware-specific behavior belongs in Infrastructure.

Functional hardware behavior belongs in requirements.

---

# 27. Cameras and media pipeline

Technical rules for `CameraApp.Infrastructure.Camera`:

- One dedicated capture thread per camera (blocking driver API).
- Frame callbacks must be fast and non-blocking: no file I/O, no video encoding, no network calls.
- Callbacks on capture threads must not take locks that start/stop holds while joining capture threads.
- Starting and stopping camera sessions is serialized; a new session must not open a device that is still being released.
- OpenCV `Mat` instances have explicit ownership and are disposed deterministically; continuous operation must not leak memory.
- Persisting and encoding run asynchronously, decoupled from the capture threads.
- Frame rates are measured and reported, not taken from the driver.
- Preview frames are encoded only while a client is watching.

Video encoding:

- through `IVideoEncoder`, implemented with the external `ffmpeg` process (path configurable);
- output H.264, `yuv420p`, `-movflags +faststart` (browser playback and seeking);
- no `mp4v` output from OpenCV (not playable in browsers);
- numeric process arguments are formatted with `CultureInfo.InvariantCulture`.

Synchronization:

- USB/UVC cameras cannot be hardware-synchronized; synchronization uses the shared capture clock plus per-camera offsets (section 12);
- hardware triggers or industrial camera SDKs are added only as `ICameraSource` adapters after an explicit decision.

Errors (camera not found, no frames, ffmpeg missing, encoding failed) are reported with stable error codes and visible status, not only in logs.

---

# 28. Simulation

Hardware integrations should support deterministic simulation where practical.

Simulation exists to:

- develop without hardware
- reproduce failures
- run acceptance tests
- replay recorded input

Simulation must enter the application through the same application-facing ports as live hardware.

Do not build separate business logic for simulation.

Cameras are simulated through `ICameraSource` (replay of recorded video files or synthetic frames with deterministic timestamps).

---

# 29. Raw data and derived data

For hardware input, preserve raw observations separately from derived domain state where required by the applicable feature.

Infrastructure is responsible for durable raw storage.

Application/domain logic derives higher-level concepts.

Never mutate historical raw hardware observations merely to make derived state match a correction.

Exact functional retention and replay rules belong in requirements.

---

# 30. Testing strategy

Apply the Test Pyramid.

Use:

- unit tests
- architecture tests
- integration tests
- acceptance tests
- frontend component tests
- end-to-end tests where valuable

Tests follow:

- Arrange
- Act
- Assert

and F.I.R.S.T.:

- Fast
- Independent
- Repeatable
- Self-validating
- Timely

Test behavior rather than implementation details.

---

# 31. Backend test naming

Use:

```text
Method_Scenario_ExpectedResult
```

Acceptance tests may use:

```text
Szenario_<Gherkin title>
```

when matching requirement scenarios.

---

# 32. Acceptance tests

Acceptance tests should correspond to use cases / Feature Set acceptance criteria.

When production timings are long, tests may inject shorter durations.

Never change production requirements merely to make tests faster.

Use configuration or injected time abstractions.

---

# 33. Architecture tests

Architecture tests enforce:

- Dependency Rule
- project boundaries
- forbidden references
- bounded-context boundaries
- API/Application separation
- no OpenCvSharp reference outside `CameraApp.Infrastructure.Camera`

Passing architecture tests do not remove the need for code review.

---

# 34. Frontend testing

Use:

- Vitest
- Vue Test Utils
- Playwright where appropriate

Follow Testing Library principles:

- test what the user sees
- query by role/accessibility where possible
- avoid testing internal implementation details

Mock at external/adapter boundaries rather than deeply mocking internal components.

---

# 35. Review

Use:

- Fagan Inspection concepts
- OWASP Top 10
- ATAM for significant architectural decisions

Review high-risk changes independently.

Particularly review:

- persistence
- recovery
- concurrency
- timing
- camera capture and media pipeline
- authorization

---

# 36. Concurrency

Make concurrency ownership explicit.

Avoid shared mutable state.

When a bounded context requires serialized mutation, use an explicit synchronization/application mechanism rather than accidental locking spread throughout the code.

Do not introduce concurrency optimizations without a measured need.

Camera capture threads are owned by the camera infrastructure (section 27); data leaves them only through explicit handover (e.g. buffers or channels).

---

# 37. Data integrity

Technical implementation must prioritize:

1. durable writes
2. atomic state transitions
3. recoverability
4. idempotency where appropriate
5. traceability

Functional definitions of what constitutes data loss belong in the applicable requirements.

---

# 38. Commands

From repository root:

```powershell
scripts\build.ps1
```

Backend:

```powershell
dotnet build
dotnet test
```

Run API:

```powershell
cd src\CameraApp.Api
dotnet run --launch-profile http
```

Frontend:

```powershell
cd web
npm run dev
```

API generation:

```powershell
scripts\generate-api.ps1
```

Runtime prerequisite for camera recording: `ffmpeg` on `PATH` or configured path.

---

# 39. Development ports

Backend development default:

```text
http://0.0.0.0:5081
```

Frontend:

```text
http://localhost:5173
```

Vite proxies:

```text
/api
/hubs
/health
```

Port `5080` is occupied by the old application. Never use it for this application,
including launch profiles, Vite proxy targets, tests and documentation examples.

Override example (e.g. local-only binding):

```powershell
$env:Kestrel__Endpoints__Http__Url="http://127.0.0.1:5081"
```

---

# 40. Known technical deviations

Do not silently change these.

Raise them when relevant.

- Tests currently use plain xUnit `Assert` and hand-written fakes.
- FluentAssertions and NSubstitute are not currently referenced.
- CQRS handlers are hand-written rather than using MediatR.
- Runtime-editable settings use durable JSON stores rather than `IOptions<T>`.
- `CA1716` and `CA1000` are disabled in `.editorconfig`.
- `scripts\build.ps1` currently acts as the local CI gate.
- Camera capture uses dedicated threads instead of async/await (blocking driver APIs).
- `ffmpeg` is an external runtime prerequisite, invoked as a separate process and not linked. The license of the ffmpeg build in use (e.g. GPL with libx264) requires explicit team confirmation.

---

# 41. Working agreement

Before a substantial implementation:

1. identify the Feature Set;
2. read its requirements;
3. read applicable ADRs;
4. identify affected bounded contexts;
5. propose a short implementation plan;
6. implement the smallest complete slice;
7. test it;
8. report deviations or unresolved decisions.

Do not:

- invent domain rules;
- silently resolve open requirements;
- implement later Feature Sets;
- add dependencies without asking;
- bypass architecture boundaries for speed;
- duplicate requirements inside code comments or this file unnecessarily.

---

# 42. Definition of Done

A task is complete only when all applicable checks pass.

Backend:

```text
dotnet build
dotnet test
```

Frontend:

```text
vue-tsc
eslint
vitest
vite build
```

Cross-stack:

```text
scripts\build.ps1
```

Endpoint / DTO change:

```text
scripts\generate-api.ps1
```

Camera / media pipeline change:

- simulator/replay test through `ICameraSource`
- failure behavior tested (camera missing, no frames, ffmpeg missing)
- measured frame rate and camera offset validated on real hardware where relevant

Requirement change:

- requirement document updated
- affected acceptance tests updated
- do not duplicate the changed rule into `CLAUDE.md`

Architecture change:

- affected `docs/arc42/` chapters updated in the same change (see section 43)

---

# 43. Architecture documentation (arc42)

`docs/arc42/` is the living architecture documentation. It must always reflect the current code.

Before finishing any task, check whether it changed something documented there. If so, update the affected
chapter in the same change. The mapping of change types to chapters is in `docs/arc42/README.md`.

Typical triggers:

- new or removed project, bounded context, port, context bridge or adapter (chapter 5)
- new external system, hardware interface or endpoint group (chapters 3, 7)
- new external tool or runtime prerequisite such as `ffmpeg` (chapters 3, 7)
- new background service, pipeline step, recovery or maintenance flow (chapter 6)
- changed persistence, storage layout, security, time, error or concurrency concept (chapter 8)
- significant technical decision (chapter 9, or an ADR)
- completed Feature Set (chapters 1, 10, 11)
- resolved risk, new technical debt or new open point (chapter 11)
- new domain term (chapter 12)

Rules:

- describe how the system is built; reference `docs/requirements/` instead of restating functional rules;
- keep diagrams (Mermaid) consistent with the code;
- verify names of types, files, tests and configuration keys against the code before writing them;
- update the "Last reviewed" line in `docs/arc42/README.md` when chapters change;
- report in the task summary which chapters were updated, or that none were affected.

---

# 44. Final rule

`CLAUDE.md` defines:

**how we build the system.**

`docs/requirements/` defines:

**what the system must do.**

`docs/arc42/` documents:

**how the system is currently built.**

Keep these responsibilities separate.