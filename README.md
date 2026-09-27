# AtmosphereApproach (AAPR)

This mod is an add-on for [AtmosphereAutopilot](https://github.com/Boris-Barboris/AtmosphereAutopilot) replicating an **APR** (ILS approach) mode: using reflection on [NavInstruments](https://github.com/linuxgurugamer/NavInstruments) for runway selection and flight parameters, the AP captures the localizer and the glideslope, and flies the approach down to the decision height, where it hands the plane back to you for the flare, the touchdown and the braking. Like the APR button on a general-aviation autopilot panel.

> **Status: pre-alpha.** The current build is an integration spike: it registers as a new autopilot inside AtmosphereAutopilot, reads the runway and the live ILS deviations from NavInstruments and shows the arm / capture state, but it does not steer the aircraft yet.

## How it works

AtmosphereAutopilot is built as a library of autopilots: it looks for high-level controllers in every loaded plugin and lists them in its *Autopilot module manager* window. AtmosphereApproach adds one, **Approach controller**, next to AA's bundled *Standard Fly-By-Wire*, *Cruise Flight* and *Mouse Director*. This is not a fork: AtmosphereAutopilot stays untouched and is a required dependency. The controller drives the aircraft through AtmosphereAutopilot's own director and stability controllers, so it inherits their tuning, moderation and craft settings.

The runway and the glideslope are the ones selected in NavInstruments' HSI (or you can step through them from the Approach window): what the HSI shows is what the autopilot flies.

Planned behaviour (see the CHANGELOG for what is actually shipped):

- **Arm** APR while flying vectors with Cruise Flight (heading / altitude hold) or by hand with Fly-By-Wire.
- **Localizer capture** when established within the capture band at a sane intercept angle and far enough from the runway; then altitude hold until the glideslope is met from below.
- **Glideslope capture** and descent on localizer + glideslope.
- **Minimums**: at the decision height the autopilot announces it and hands the aircraft back (Fly-By-Wire stays on to help the flare).
- Too close, above the glideslope or flying away from the runway: no capture, with the reason and an intercept hint shown in the window. Realistic, like a real APR mode: fly out and re-intercept.
- No dependency on thrust: throttle stays yours (optional coupling with AtmosphereAutopilot's speed control for jets). Propeller and rotor aircraft work the same way.
- Later: automatic vectors-to-final, go-around, and an unpowered / steep glide profile for shuttle-style landings.

## Requirements

- KSP 1.12.x
- [AtmosphereAutopilot](https://github.com/Boris-Barboris/AtmosphereAutopilot) 1.6.1 (not bundled)
- [NavInstruments Continued](https://github.com/linuxgurugamer/NavInstruments) 0.8.1 or later (not bundled; the plugin refuses to load without it)

## Installation

Drop the `AtmosphereApproach` folder into `GameData`, so you end up with `GameData/AtmosphereApproach/Plugins/AtmosphereApproach.dll`, next to `GameData/AtmosphereAutopilot` and `GameData/NavInstruments`.

## Usage

1. In flight, open AtmosphereAutopilot's *Autopilot module manager* (Shift + master switch key, `P` by default) and switch the master on.
2. Select **Approach controller** and open its GUI.
3. Pick the runway and the glideslope (same selection as the NavInstruments HSI).
4. Fly towards the localizer, below the glideslope, and press **APR**. The window shows the state: armed, localizer captured, glideslope captured, minimums.
5. An *APR arm/disarm* hotkey is available in AtmosphereAutopilot's *Hotkeys manager*.

## Building

`dotnet build -c Release` in `Source/` (SDK-style project, .NET Framework 4.7.2). Reference assemblies are taken from the KSP install set by the `KspRoot` property; the build copies the DLL into `GameData/AtmosphereApproach/Plugins/`.

## License

GPL-3.0, see [LICENSE](LICENSE). AtmosphereApproach links against AtmosphereAutopilot (GPL-3.0) and extends its classes. NavInstruments is accessed at run time by reflection only; nothing from either mod is redistributed.

Credits: Boris-Barboris and contributors for AtmosphereAutopilot; kujuman, Ser and linuxgurugamer for NavInstruments.
