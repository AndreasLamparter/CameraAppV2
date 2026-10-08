import { describe, expect, it } from 'vitest'
import { baseUrlOptions, controlEndpoints, curlCommand, endpointUrl } from './externalControl'

function endpoint(key: string) {
  const found = controlEndpoints.find((e) => e.key === key)
  if (!found) {
    throw new Error(`no endpoint ${key}`)
  }
  return found
}

const start = endpoint('startRecording')
const status = endpoint('status')

describe('externalControl', () => {
  it('builds the full URL from the base URL of the finish PC', () => {
    expect(endpointUrl('http://192.168.1.20:5081/', start)).toBe('http://192.168.1.20:5081/api/control/start')
  })

  it('builds a curl call with the key and the JSON body, or a placeholder without key', () => {
    expect(curlCommand('http://pc:5081', status, 'X-Api-Key', 'secret')).toBe('curl -X GET "http://pc:5081/api/control/status" -H "X-Api-Key: secret"')
    expect(curlCommand('http://192.168.1.20:5081', start, 'X-Api-Key')).toBe(
      'curl -X POST "http://192.168.1.20:5081/api/control/start" -H "X-Api-Key: <API-KEY>" -H "Content-Type: application/json" -d \'{"mode":"Recording","raceName":"Lauf 1"}\'',
    )
    expect(curlCommand('http://pc:5081', status, 'X-Api-Key')).toBe('curl -X GET "http://pc:5081/api/control/status" -H "X-Api-Key: <API-KEY>"')
  })

  it('offers the page address in addition to the network addresses', () => {
    expect(baseUrlOptions(['http://192.168.1.20:5081'], 'http://localhost:5173')).toEqual(['http://192.168.1.20:5081', 'http://localhost:5173'])
    expect(baseUrlOptions(['http://192.168.1.20:5081'], 'http://192.168.1.20:5081')).toEqual(['http://192.168.1.20:5081'])
  })
})
