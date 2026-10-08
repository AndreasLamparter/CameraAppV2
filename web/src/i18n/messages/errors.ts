// Translations of the stable backend error codes (ProblemDetails "code"); parameters come from "params".
const de = {
  unknown: 'Unerwarteter Fehler ({code}).',
  network: 'Server nicht erreichbar.',
  access: {
    wrongPin: 'PIN falsch.',
  },
  control: {
    modeInvalid: 'Ungültiger Betriebszustand.',
  },
  camera: {
    openFailed: 'Kamera kann nicht geöffnet werden.',
    notFound: 'Ausgewählte Kamera nicht angeschlossen.',
    exposureNotApplied: 'Die eingestellte Belichtung wurde von der Kamera nicht übernommen.',
    modeNotSupported: 'Die Kamera bietet die eingestellte Auflösung nicht an.',
    noFrames: 'Kamera liefert keine Bilder.',
    captureFailed: 'Bildaufnahme fehlgeschlagen.',
  },
  video: {
    ffmpegMissing: 'Video konnte nicht erzeugt werden: ffmpeg nicht gefunden.',
    encodingFailed: 'Video konnte nicht erzeugt werden.',
  },
  recording: {
    notFound: 'Aufnahme nicht gefunden.',
    fileNotFound: 'Datei der Aufnahme nicht vorhanden.',
    saveFailed: 'Aufnahme konnte nicht gespeichert werden.',
  },
  simulator: {
    disabled: 'Die Kamerasimulation ist nicht aktiv.',
  },
  settings: {
    mediaDirectoryInvalid: 'Speicherort: bitte einen absoluten Pfad angeben (max. {max} Zeichen).',
    deviceInvalid: 'Gerät: Index zwischen 0 und {max}.',
    deviceNameInvalid: 'Gerät: ungültiger Kameraname (max. {max} Zeichen).',
    resolutionInvalid: 'Auflösung: Breite und Höhe zwischen {min} und {max}.',
    frameRateInvalid: 'Bildrate zwischen {min} und {max}.',
    exposureInvalid: 'Ungültige Belichtung.',
    offsetInvalid: 'Offset zwischen {min} und {max} ms.',
    sameDevice: 'Ziel- und Frontkamera müssen verschiedene Geräte sein.',
    lineWidthInvalid: 'Linienbreite zwischen 1 und {max} Pixel.',
    linePositionInvalid: 'Position der Ziellinie zwischen 0 und {max}.',
    rotationInvalid: 'Ungültige Bilddrehung.',
    pixelThresholdInvalid: 'Schwellwert Pixeländerung zwischen {min} und {max}.',
    occupancyThresholdInvalid: 'Schwellwert Belegung zwischen {min} und {max} %.',
    durationInvalid: 'Zeitwert ({field}) zwischen {min} und {max} s.',
  },
}

const en: typeof de = {
  unknown: 'Unexpected error ({code}).',
  network: 'Server not reachable.',
  access: {
    wrongPin: 'Wrong PIN.',
  },
  control: {
    modeInvalid: 'Invalid operating state.',
  },
  camera: {
    openFailed: 'Camera cannot be opened.',
    notFound: 'Selected camera is not connected.',
    exposureNotApplied: 'The camera did not accept the configured exposure.',
    modeNotSupported: 'The camera does not offer the configured resolution.',
    noFrames: 'Camera delivers no frames.',
    captureFailed: 'Capture failed.',
  },
  video: {
    ffmpegMissing: 'Video could not be created: ffmpeg not found.',
    encodingFailed: 'Video could not be created.',
  },
  recording: {
    notFound: 'Recording not found.',
    fileNotFound: 'File of the recording does not exist.',
    saveFailed: 'Recording could not be saved.',
  },
  simulator: {
    disabled: 'Camera simulation is not active.',
  },
  settings: {
    mediaDirectoryInvalid: 'Storage location: please enter an absolute path (max. {max} characters).',
    deviceInvalid: 'Device: index between 0 and {max}.',
    deviceNameInvalid: 'Device: invalid camera name (max. {max} characters).',
    resolutionInvalid: 'Resolution: width and height between {min} and {max}.',
    frameRateInvalid: 'Frame rate between {min} and {max}.',
    exposureInvalid: 'Invalid exposure.',
    offsetInvalid: 'Offset between {min} and {max} ms.',
    sameDevice: 'Finish and front camera must be different devices.',
    lineWidthInvalid: 'Line width between 1 and {max} pixels.',
    linePositionInvalid: 'Finish line position between 0 and {max}.',
    rotationInvalid: 'Invalid image rotation.',
    pixelThresholdInvalid: 'Pixel change threshold between {min} and {max}.',
    occupancyThresholdInvalid: 'Occupancy threshold between {min} and {max} %.',
    durationInvalid: 'Time value ({field}) between {min} and {max} s.',
  },
}

export default { de, en }
