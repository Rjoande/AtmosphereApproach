# AtmosphereApproach (AAPR)

This mod is an add-on for [AtmosphereAutopilot](https://github.com/Boris-Barboris/AtmosphereAutopilot) replicating an **APR** (ILS approach) mode: using reflection on [NavInstruments](https://github.com/linuxgurugamer/NavInstruments) for runway selection and flight parameters, the AP captures the localizer and the glideslope, and flies the approach down to the decision height, where it hands the plane back to you for the flare, the touchdown and the braking. Like the APR button on a general-aviation autopilot panel.

> **Status: alpha.** The approach is flown end to end (localizer intercept and tracking, glideslope descent, hand-over at minimums). Expect rough edges in the tuning; feedback on the [forum thread](https://forum.kerbalspaceprogram.com/topic/231869-1125-atmosphereapproach-landing-controller-for-atmosphereautopilot/) is welcome.

## How it works

AtmosphereAutopilot is built as a library of autopilots: it looks for high-level controllers in every loaded plugin and lists them in its *Autopilot module manager* window. AtmosphereApproach adds one, **Approach controller**, next to AA's bundled *Standard Fly-By-Wire*, *Cruise Flight* and *Mouse Director*. This is not a fork: AtmosphereAutopilot stays untouched and is a required dependency. The controller drives the aircraft through AtmosphereAutopilot's own director and stability controllers, so it inherits their tuning, moderation and craft settings.

The runway and the glideslope are the ones selected in NavInstruments' HSI (or you can step through them from the Approach window): what the HSI shows is what the autopilot flies.

What it does:

- **Arm** APR while flying vectors with Cruise Flight (heading / altitude hold) or by hand with Fly-By-Wire: until capture, that controller keeps flying.
- **Localizer capture** when established within the capture band at a sane intercept angle and far enough from the runway. APR then takes over, intercepts the extended centerline at up to 30 degrees and tracks it, holding the capture altitude until the glideslope is met from below.
- **Glideslope capture** and descent on localizer + glideslope.
- **Minimums**: at the decision height the autopilot announces it and hands the aircraft back to Fly-By-Wire (or switches the master off, by option).
- Too close, above the glideslope or flying away from the runway: no capture, with the reason and an intercept hint shown in the window. Realistic, like a real APR mode: fly out and re-intercept.
- No dependency on thrust: the throttle stays yours, with optional coupling to AtmosphereAutopilot's speed control for jets. Propeller and rotor aircraft work the same way.
- Later: approach speed management, automatic vectors-to-final, go-around, and an unpowered / steep glide profile for shuttle-style landings.

## Requirements

- KSP 1.12.x
- [AtmosphereAutopilot](https://github.com/Boris-Barboris/AtmosphereAutopilot) 1.6.1 (not bundled)
- [NavInstruments Continued](https://github.com/linuxgurugamer/NavInstruments) 0.8.1 or later (not bundled; the plugin refuses to load without it)

## Installation

Drop the `AtmosphereApproach` folder into `GameData`, so you end up with `GameData/AtmosphereApproach/Plugins/AtmosphereApproach.dll`, next to `GameData/AtmosphereAutopilot` and `GameData/NavInstruments`.

## Usage

### Setting up

1. In flight, open AtmosphereAutopilot's *Autopilot module manager* (Shift + master switch key, `P` by default) and switch the master on.
2. Select **Approach controller** and open its GUI. Cruise Flight starts flying the aircraft right away (heading and altitude hold, as you set them in its own window); untick *Arm over Cruise Flight* if you would rather hand-fly the intercept with Fly-By-Wire.
3. Pick the runway and the glideslope with the `<` `>` buttons. The selection is the same as the NavInstruments HSI: changing it in either window updates the other.
4. Optionally bind the *APR arm/disarm* key in AtmosphereAutopilot's *Hotkeys manager*: it arms from any autopilot, selecting the Approach controller and switching the master on if needed.

### Flying the approach

1. **Intercept.** Fly towards the extended centerline at 30 degrees or less, 6 to 10 km from the threshold, **below** the glideslope. The window helps: *GS at X km: Y m MSL* is the altitude of the beam at the outer marker, *intercept hdg* is the suggested heading, *xtk* is your distance from the centerline (R/L).
2. **Arm.** Press **APR** (button or hotkey). The state reads *ARMED* and the line below says what APR is waiting for: *waiting LOC*, *too close* (fly out beyond the minimum final distance and re-intercept), or *intercept angle* (you are flying away from the runway).
3. **Localizer.** When the localizer deviation enters the capture band (2 degrees by default) APR takes over from Cruise Flight / Fly-By-Wire, turns onto the centerline and holds the altitude it had at capture. The window shows the commanded heading and vertical speed and *below GS* with the current deviation.
4. **Glideslope.** When the beam comes down to you (within 0.35 degrees, from below) APR starts the descent and tracks localizer and glideslope together. Capture from above is refused by default: get below the beam and re-arm, or enable *Allow GS capture from above* under *Advanced*.
5. **Minimums.** At the decision height (30 m above the runway by default, or the radar altitude if lower) the screen says *MINIMUMS - autopilot disconnected* and Fly-By-Wire gets the aircraft, with its AoA and G moderation to help the flare. Throttle, flare, touchdown and brakes are yours.

Losing the localizer (beyond three times the capture band) hands the aircraft back to Cruise Flight / Fly-By-Wire with APR re-armed; losing the glideslope goes back to localizer tracking with altitude hold at the current altitude. Pressing APR again, selecting another controller in the manager or switching the master off disarms at any time.

### Speed

The throttle is not touched by default, like a general-aviation autopilot without autothrottle: set your approach speed by hand or with AtmosphereAutopilot's speed control. Tick *Couple AA speed control* to have APR keep the speed control setpoint during the approach (jets and rockets only: the thrust controller has no model for propellers or rotors).

### Options

Main window: *decision height* (per craft), *LOC capture*, *GS capture*, *min final DME*, *Arm over Cruise Flight*, *Couple AA speed control*, *Minimums: master off*. Under *Advanced*: maximum intercept angle, lateral time constant and look-ahead limits, height and vertical-speed gains, flight-path-angle margin, director strength multiplier, glideslope capture from above and decision height on radar altitude. Everything is saved in AtmosphereAutopilot's own settings files (`Global_settings.txt` and the per-craft `designs/` files).

## Building

`dotnet build -c Release` in `Source/` (SDK-style project, .NET Framework 4.7.2). Reference assemblies are taken from the KSP install set by the `KspRoot` property; the build copies the DLL into `GameData/AtmosphereApproach/Plugins/`.

## License

GPL-3.0, see [LICENSE](LICENSE). AtmosphereApproach links against AtmosphereAutopilot (GPL-3.0) and extends its classes. NavInstruments is accessed at run time by reflection only; nothing from either mod is redistributed.

Credits: Boris-Barboris and contributors for AtmosphereAutopilot; kujuman, Ser and linuxgurugamer for NavInstruments.
