# Changelog

## [alpha pre-release]

### Added
- **Approach controller** registered inside AtmosphereAutopilot: it shows up in the *Autopilot module manager* next to Fly-By-Wire, Cruise Flight and Mouse Director, with its own window and an *APR arm/disarm* entry in AtmosphereAutopilot's Hotkeys manager. AtmosphereAutopilot is untouched and required.
- The Approach window shows the runway and glideslope selected in NavInstruments (with `<` `>` buttons that keep the HSI in sync), the live DME, localizer and glideslope deviations, and the APR state: armed, localizer captured, glideslope captured, minimums. NavInstruments is required and read at run time only.
- Capture rules already in place: no localizer capture when too close to the runway, when flying away from it or when off the beam; glideslope captured only from within its band. The window says why APR is still waiting.
- While the controller is selected, Cruise Flight (default) or Fly-By-Wire keeps flying the aircraft, so it can be used today to fly vectors and watch the approach state.

### Not yet
- The controller does not steer on the localizer or the glideslope, and does not hand over at minimums: this is an integration preview.
