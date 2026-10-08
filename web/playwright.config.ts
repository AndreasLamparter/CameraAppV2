import { defineConfig } from '@playwright/test'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

// End-to-end against the real backend with simulated cameras. Private port and a temporary data directory:
// never touches real data and never the old application on port 5080.
export const E2E_PORT = 5082
export const PIN = '2468'
const dataDirectory = join(tmpdir(), `timingapp-camera-e2e-${Date.now()}`)

export default defineConfig({
  testDir: 'e2e',
  timeout: 90_000,
  workers: 1,
  use: {
    baseURL: `http://127.0.0.1:${E2E_PORT}`,
    channel: 'chrome',
    locale: 'de-DE',
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'dotnet run --no-build --no-launch-profile --project ../src/TimingApp.Api',
    url: `http://127.0.0.1:${E2E_PORT}/health`,
    reuseExistingServer: false,
    timeout: 60_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Testing',
      Kestrel__Endpoints__Http__Url: `http://127.0.0.1:${E2E_PORT}`,
      TimingApp__Storage__DataDirectory: dataDirectory,
      TimingApp__Access__OperatorPin: PIN,
      TimingApp__Camera__Simulation__Enabled: 'true',
      // No ffmpeg: deterministic video failure path (FS1-43), independent of the PC.
      TimingApp__Camera__FfmpegPath: join(dataDirectory, 'no-ffmpeg.exe'),
    },
  },
})
