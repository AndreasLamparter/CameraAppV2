import { expect, test, type Page } from '@playwright/test'
import { PIN } from '../playwright.config'

async function login(page: Page): Promise<void> {
  await page.goto('/login')
  await page.getByLabel('PIN').fill(PIN)
  await page.getByRole('button', { name: 'Anmelden' }).click()
  await expect(page.getByRole('heading', { name: 'Live' })).toBeVisible()
}

/** Feature Set 1 through the UI with simulated cameras: record, finish event, playback, delete. */
test('Zielaufnahme über die Oberfläche', async ({ page }) => {
  await login(page)

  // Aufnahme starten: both cameras report a frame rate.
  await page.getByRole('button', { name: 'Aufnahme', exact: true }).click()
  await expect(page.getByTestId('finishCamera-fps')).not.toContainText('0.0 /s', { timeout: 10_000 })
  await expect(page.getByTestId('frontCamera-fps')).not.toContainText('0.0 /s')
  await expect(page.getByAltText('Zielkamera')).toBeVisible()
  await expect(page.getByTestId('finish-line')).toBeVisible()
  await expect(page.getByText('Hintergrund wird gelernt …')).toBeHidden({ timeout: 10_000 })

  // A simulated rider passes the finish line.
  await page.request.put('/api/simulator/occupancy', { data: { occupied: true } })
  await expect(page.getByText(/Zielereignis seit/)).toBeVisible()
  await page.waitForTimeout(800)
  await page.request.put('/api/simulator/occupancy', { data: { occupied: false } })

  // Without ffmpeg the recording keeps finish image and timestamps, and the problem is shown (FS1-43).
  await expect(page.getByText('Letztes Problem beim Speichern')).toBeVisible({ timeout: 20_000 })

  await page.getByRole('link', { name: 'Aufnahmen' }).click()
  const rows = page.locator('tbody tr')
  await expect(rows).toHaveCount(1)
  await expect(rows.first()).toContainText('Video fehlt')

  // Playback: a click into the finish image selects a column and shows its time.
  await rows.first().getByRole('link').click()
  await expect(page.getByTestId('finish-image')).toBeVisible()
  await expect(page.getByText('Zielvideo: Video konnte nicht erzeugt werden: ffmpeg nicht gefunden.')).toBeVisible()
  await expect(page.getByText('Frontvideo: Video konnte nicht erzeugt werden: ffmpeg nicht gefunden.')).toBeVisible()
  const before = await page.getByTestId('column-time').textContent()
  await page.getByTestId('finish-image').click({ position: { x: 5, y: 20 } })
  await expect(page.getByTestId('column-time')).not.toHaveText(before ?? '')
  await expect(page.getByTestId('column-time')).toHaveText(/^\d\d:\d\d:\d\d\.\d{3}$/)

  // Delete with confirmation.
  await page.getByRole('link', { name: 'Zur Liste' }).click()
  await page.getByRole('button', { name: /^Löschen/ }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Löschen' }).click()
  await expect(page.getByText('Noch keine Aufnahmen.')).toBeVisible()

  await page.getByRole('link', { name: 'Live' }).click()
  await page.getByRole('button', { name: 'Stoppen' }).click()
  await expect(page.getByText('Gestoppt').first()).toBeVisible()
})
