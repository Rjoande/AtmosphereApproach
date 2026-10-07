using System;
using System.Collections.Generic;
using AtmosphereAutopilot;
using UnityEngine;

namespace AtmosphereApproach
{
    /// <summary>
    /// APR mode: arms on the runway tuned in NavInstruments, captures localizer then glideslope and
    /// flies the approach down to the decision height, where it hands the aircraft back to the pilot.
    /// Listed by AtmosphereAutopilot as one of its high-level autopilots. While off or armed a
    /// delegated AA controller (Cruise Flight or Fly-By-Wire) flies; from localizer capture on,
    /// this module steers through AA's Director controller.
    /// </summary>
    public sealed class ApproachController : StateController
    {
        const string Tag = "[AtmosphereApproach] ";
        const double Deg2Rad = Math.PI / 180.0;
        const double Rad2Deg = 180.0 / Math.PI;

        // Window id must be unique among every IMGUI window in the game: "AAPR" as bytes.
        internal ApproachController(Vessel v) : base(v, "Approach controller", 0x41415052)
        {
            window.width = 300.0f;
        }

        FlightModel imodel;
        DirectorController dir_c;
        ProgradeThrustController thrust_c;
        StandardFlyByWire fbw;
        CruiseController cruise;
        TopModuleManager manager;

        public override void InitializeDependencies(Dictionary<Type, AutopilotModule> modules)
        {
            imodel = modules[typeof(FlightModel)] as FlightModel;
            dir_c = modules[typeof(DirectorController)] as DirectorController;
            thrust_c = modules[typeof(ProgradeThrustController)] as ProgradeThrustController;
            fbw = modules[typeof(StandardFlyByWire)] as StandardFlyByWire;
            cruise = modules[typeof(CruiseController)] as CruiseController;
            manager = modules[typeof(TopModuleManager)] as TopModuleManager;
        }

        #region State

        public enum ApproachState { Off, Armed, Localizer, Glideslope, Minimums }

        public ApproachState state = ApproachState.Off;

        /// <summary>Why APR is still waiting (shown in the window), empty when nothing blocks it.</summary>
        public string gate = "";

        public bool Armed
        {
            get { return state != ApproachState.Off; }
            set
            {
                if (value == Armed)
                    return;
                if (value)
                {
                    RunwayInfo rwy = NavBridge.SelectedRunway;
                    if (rwy == null || rwy.body != vessel.mainBody.name)
                    {
                        MessageManager.post_quick_message("APR: no runway tuned on this body");
                        Debug.Log(Tag + "arm refused: no runway on " + vessel.mainBody.name);
                        return;
                    }
                    state = ApproachState.Armed;
                    MessageManager.post_status_message("APR armed: " + rwy.shortID);
                    Debug.Log(Tag + "armed on " + rwy.shortID);
                }
                else
                {
                    state = ApproachState.Off;
                    gate = "";
                    if (Active)
                        FlyByApproach(false);
                    MessageManager.post_status_message("APR off");
                    Debug.Log(Tag + "disarmed");
                }
            }
        }

        #endregion

        #region Who flies

        // Controller flying the aircraft while APR is not steering; activated from inside this module
        // the same way Cruise Flight activates its director, and released before APR takes over.
        StateController underlying;

        // True from localizer capture on: this module owns the Director, thrust controller and model.
        bool steering;

        StateController Underlying { get { return armed_with_cruise ? (StateController)cruise : fbw; } }

        void TakeDelegate(StateController c)
        {
            c.Activate();
            underlying = c;
        }

        void ReleaseDelegate()
        {
            if (underlying == null)
                return;
            underlying.Deactivate();
            underlying = null;
        }

        /// <summary>Switches between the delegated controller (false) and APR's own steering (true).</summary>
        void FlyByApproach(bool on)
        {
            if (on == steering)
                return;
            if (on)
            {
                ReleaseDelegate();
                dir_c.Activate();
                thrust_c.Activate();
                imodel.Activate();
                steering = true;
            }
            else
            {
                dir_c.Deactivate();
                thrust_c.Deactivate();
                imodel.Deactivate();
                steering = false;
                TakeDelegate(Underlying);
            }
        }

        #endregion

        #region Parameters

        [VesselSerializable("decision_height")]
        [AutoGuiAttr("decision height (m)", true, "G4")]
        public double decision_height = 30.0;

        [GlobalSerializable("loc_capture_deg")]
        [AutoGuiAttr("LOC capture (deg)", true, "G3")]
        public double loc_capture_deg = 2.0;

        [GlobalSerializable("gs_capture_deg")]
        [AutoGuiAttr("GS capture (deg)", true, "G3")]
        public double gs_capture_deg = 0.35;

        [GlobalSerializable("min_final_dme")]
        [AutoGuiAttr("min final DME (m)", true, "G5")]
        public double min_final_dme = 5000.0;

        [GlobalSerializable("armed_with_cruise")]
        [AutoGuiAttr("Arm over Cruise Flight", true)]
        public bool armed_with_cruise = true;

        [GlobalSerializable("couple_speed_control")]
        [AutoGuiAttr("Couple AA speed control", true)]
        public bool couple_speed_control = false;

        [GlobalSerializable("handover_master_off")]
        [AutoGuiAttr("Minimums: master off (else FBW)", true)]
        public bool handover_master_off = false;

        // Advanced parameters: serialized like the others, drawn by hand behind the "Advanced" toggle.
        [GlobalSerializable("max_intercept_deg")]
        public double max_intercept_deg = 30.0;

        [GlobalSerializable("tau_lat")]
        public double tau_lat = 10.0;

        [GlobalSerializable("lookahead_min")]
        public double lookahead_min = 300.0;

        [GlobalSerializable("lookahead_max")]
        public double lookahead_max = 3000.0;

        [GlobalSerializable("height_gain")]
        public double height_gain = 0.15;

        [GlobalSerializable("vspeed_gain")]
        public double vspeed_gain = 0.3;

        [GlobalSerializable("fpa_margin")]
        public double fpa_margin = 3.0;

        [VesselSerializable("strength_mult")]
        public double strength_mult = 0.6;

        [GlobalSerializable("allow_gs_from_above")]
        public bool allow_gs_from_above = false;

        [GlobalSerializable("use_radar_dh")]
        public bool use_radar_dh = true;

        bool show_advanced = false;
        bool shrink_window = false;

        // Polled by ApproachHotkeys in every flight frame, not here: AA only updates the active module.
        [GlobalSerializable("apr_arm_key")]
        [AutoHotkeyAttr("APR arm/disarm")]
        internal static KeyCode apr_arm_key = KeyCode.None;

        #endregion

        #region Lifecycle

        protected override void OnActivate()
        {
            NavBridge.EnsureNavAids();
            state = ApproachState.Off;
            gate = "";
            steering = false;
            TakeDelegate(Underlying);
            MessageManager.post_status_message("Approach controller enabled");
        }

        protected override void OnDeactivate()
        {
            if (steering)
            {
                dir_c.Deactivate();
                thrust_c.Deactivate();
                imodel.Deactivate();
                steering = false;
            }
            else
                ReleaseDelegate();
            state = ApproachState.Off;
            gate = "";
            MessageManager.post_status_message("Approach controller disabled");
        }

        #endregion

        #region Navigation

        NavData nav;
        double track;               // ground track, degrees
        double intercept;           // track - runway course, degrees, -180..180
        double height_above_runway;
        double cross_track;         // meters from the extended centerline, + = right of course
        Vector3d up, runway_dir, runway_right;

        void UpdateNavigation()
        {
            nav = NavBridge.Update();
            RunwayInfo rwy = NavBridge.SelectedRunway;
            if (!nav.valid || rwy == null)
                return;
            up = (vessel.ReferenceTransform.position - vessel.mainBody.position).normalized;
            Vector3d hv = Vector3d.Exclude(up, vessel.srf_velocity);
            track = Wrap360(Math.Atan2(Vector3d.Dot(hv, vessel.east), Vector3d.Dot(hv, vessel.north)) * Rad2Deg);
            intercept = Wrap180(track - nav.runwayHeading);
            height_above_runway = vessel.altitude - rwy.altMSL;

            // runway course and its right-hand normal in the local horizontal plane (course projected
            // at the vessel by NavInstruments, so no spherical correction is needed here)
            double hdg = nav.runwayHeading * Deg2Rad;
            runway_dir = Vector3d.Exclude(up, vessel.north * Math.Cos(hdg) + vessel.east * Math.Sin(hdg)).normalized;
            runway_right = Vector3d.Exclude(up, vessel.east * Math.Cos(hdg) - vessel.north * Math.Sin(hdg)).normalized;
            Vector3d loc = vessel.mainBody.GetWorldSurfacePosition(rwy.locLatitude, rwy.locLongitude, rwy.altMSL);
            cross_track = Vector3d.Dot(vessel.ReferenceTransform.position - loc, runway_right);
        }

        double HeightForMinimums()
        {
            return use_radar_dh ? Math.Min(height_above_runway, vessel.radarAltitude) : height_above_runway;
        }

        /// <summary>Glideslope altitude (MSL) at the given distance from the antenna.</summary>
        double GlideslopeAltitude(RunwayInfo rwy, double dist)
        {
            return rwy.altMSL + dist * Math.Tan(nav.glideslope * Deg2Rad) + dist * dist / (2.0 * vessel.mainBody.Radius);
        }

        #endregion

        #region State machine

        double hold_altitude;       // altitude held while on the localizer, waiting for the glideslope

        void UpdateState()
        {
            if (state == ApproachState.Off)
                return;
            RunwayInfo rwy = NavBridge.SelectedRunway;
            if (!nav.valid || rwy == null || rwy.body != vessel.mainBody.name)
            {
                Armed = false;
                MessageManager.post_quick_message("APR off: runway lost");
                return;
            }
            double loc = Math.Abs(nav.locDeviation);
            double gs = Math.Abs(nav.gsDeviation);
            switch (state)
            {
                case ApproachState.Armed:
                    if (nav.dme < min_final_dme)
                        gate = "too close: DME " + (nav.dme / 1000.0).ToString("0.0") + " km < " + (min_final_dme / 1000.0).ToString("0.0") + " km, fly out and re-intercept";
                    else if (Math.Abs(intercept) > 90.0)
                        gate = "intercept angle " + intercept.ToString("0") + " deg, flying away from the runway";
                    else if (loc > loc_capture_deg)
                        gate = "waiting LOC (dev " + nav.locDeviation.ToString("+0.0;-0.0") + " deg)";
                    else
                    {
                        hold_altitude = vessel.altitude;
                        Transition(ApproachState.Localizer, "localizer captured, holding " + hold_altitude.ToString("0") + " m");
                        FlyByApproach(true);
                    }
                    break;

                case ApproachState.Localizer:
                    if (loc > 3.0 * loc_capture_deg)
                    {
                        Transition(ApproachState.Armed, "localizer lost");
                        FlyByApproach(false);
                    }
                    else if (gs <= gs_capture_deg && (nav.gsDeviation <= 0.0f || allow_gs_from_above))
                        Transition(ApproachState.Glideslope, "glideslope captured");
                    else if (nav.gsDeviation > 0.0f)
                        gate = "above GS " + nav.gsDeviation.ToString("+0.00") + " deg" +
                            (allow_gs_from_above ? "" : ": no capture from above, get below the beam");
                    else
                        gate = "below GS " + nav.gsDeviation.ToString("0.00") + " deg, holding " + hold_altitude.ToString("0") + " m";
                    break;

                case ApproachState.Glideslope:
                    if (loc > 3.0 * loc_capture_deg)
                    {
                        Transition(ApproachState.Armed, "localizer lost");
                        FlyByApproach(false);
                    }
                    else if (gs > 3.0 * gs_capture_deg)
                    {
                        hold_altitude = vessel.altitude;
                        Transition(ApproachState.Localizer, "glideslope lost, holding " + hold_altitude.ToString("0") + " m");
                    }
                    else if (HeightForMinimums() < decision_height)
                        Transition(ApproachState.Minimums, "MINIMUMS");
                    break;

                case ApproachState.Minimums:
                    break;
            }
        }

        void Transition(ApproachState next, string message)
        {
            state = next;
            gate = "";
            MessageManager.post_status_message("APR: " + message);
            Debug.Log(Tag + message + " (DME " + nav.dme.ToString("0") + " m, LOC " + nav.locDeviation.ToString("0.00") +
                ", GS " + nav.gsDeviation.ToString("0.00") + ", cross-track " + cross_track.ToString("+0;-0") +
                " m, above rwy " + height_above_runway.ToString("0") + " m)");
        }

        /// <summary>Decision height reached: give the aircraft back to the pilot as configured.</summary>
        void Handover()
        {
            MessageManager.post_quick_message("MINIMUMS - autopilot disconnected");
            Debug.Log(Tag + "handover at minimums: " + (handover_master_off ? "master off" : "Fly-By-Wire") +
                " (above rwy " + height_above_runway.ToString("0") + " m, radar " + vessel.radarAltitude.ToString("0") + " m)");
            // both paths call this module's Deactivate(), which resets the state and releases the Director
            if (handover_master_off)
                manager.Active = false;
            else
                manager.activateAutopilot(typeof(StandardFlyByWire));
            global::AtmosphereAutopilot.AtmosphereAutopilot.Instance.mainMenuGUIUpdate();
        }

        static double Wrap360(double a)
        {
            a %= 360.0;
            return a < 0.0 ? a + 360.0 : a;
        }

        static double Wrap180(double a)
        {
            a = Wrap360(a);
            return a > 180.0 ? a - 360.0 : a;
        }

        #endregion

        #region Control

        double desired_heading;     // degrees, shown in the window
        double desired_vspeed;      // m/s, shown in the window

        public override void ApplyControl(FlightCtrlState cntrl)
        {
            if (vessel.LandedOrSplashed)
                return;
            UpdateNavigation();
            UpdateState();
            if (state == ApproachState.Minimums)
            {
                Handover();
                return;
            }
            if (!steering)
            {
                if (underlying != Underlying)
                {
                    // option changed while active: swap the delegated controller
                    ReleaseDelegate();
                    TakeDelegate(Underlying);
                }
                underlying.ApplyControl(cntrl);
                return;
            }
            Steer(cntrl);
        }

        /// <summary>
        /// Lateral: pure pursuit on the extended centerline, intercept angle limited. Vertical: altitude
        /// hold on the localizer, straight-beam tracking on the glideslope. Both handed to the Director
        /// as a velocity direction plus the acceleration Cruise Flight would ask for.
        /// </summary>
        void Steer(FlightCtrlState cntrl)
        {
            if (couple_speed_control && thrust_c.spd_control_enabled)
                thrust_c.ApplyControl(cntrl, thrust_c.setpoint.mps());

            Vector3d planet2ves = vessel.ReferenceTransform.position - vessel.mainBody.position;
            Vector3d hor_v = Vector3d.Exclude(up, imodel.surface_v);
            double vh = Math.Max(hor_v.magnitude, 1.0);
            double vz = Vector3d.Dot(imodel.surface_v, up);

            // lateral
            double lookahead = Common.Clamp(tau_lat * vh, lookahead_min, lookahead_max);
            double psi = Common.Clamp(-Math.Atan2(cross_track, lookahead) * Rad2Deg, max_intercept_deg);
            Vector3d heading_dir = runway_dir * Math.Cos(psi * Deg2Rad) + runway_right * Math.Sin(psi * Deg2Rad);
            desired_heading = Wrap360(nav.runwayHeading + psi);

            // vertical
            double margin_vz = vh * Math.Tan(fpa_margin * Deg2Rad);
            if (state == ApproachState.Localizer)
                desired_vspeed = Common.Clamp(height_gain * (hold_altitude - vessel.altitude), margin_vz);
            else
            {
                double gs_error = nav.dme * Math.Tan(nav.gsDeviation * Deg2Rad);               // m, + = above the beam
                double fpa_nom = nav.glideslope + nav.dme / vessel.mainBody.Radius * Rad2Deg;  // straight beam over a curved surface
                desired_vspeed = -vh * Math.Tan(fpa_nom * Deg2Rad) - height_gain * gs_error;
                desired_vspeed = Common.Clamp(desired_vspeed, -vh * Math.Tan((nav.glideslope + fpa_margin) * Deg2Rad), margin_vz);
            }

            Vector3d desired_velocity = (heading_dir * vh + up * desired_vspeed).normalized;
            Vector3d level_acc = -up * hor_v.sqrMagnitude / planet2ves.magnitude;
            Vector3d vert_acc = up * vspeed_gain * (desired_vspeed - vz);

            double old_strength = dir_c.strength;
            dir_c.strength *= strength_mult;
            dir_c.ApplyControl(cntrl, desired_velocity, level_acc + vert_acc);
            dir_c.strength = old_strength;
        }

        #endregion

        #region GUI

        [AutoGuiAttr("Director controller GUI", true)]
        public bool DircGUI { get { return dir_c.IsShown(); } set { if (value) dir_c.ShowGUI(); else dir_c.UnShowGUI(); } }

        [AutoGuiAttr("Thrust controller GUI", true)]
        public bool PTCGUI { get { return thrust_c.IsShown(); } set { if (value) thrust_c.ShowGUI(); else thrust_c.UnShowGUI(); } }

        // Runs after GUILayout.Window has returned: a height reset made inside _drawGUI is overwritten
        // by the window's return value, so the window is shrunk here and IMGUI refits it next frame.
        protected override void OnGUICustom()
        {
            if (!shrink_window)
                return;
            shrink_window = false;
            window.height = 0.0f;
        }

        protected override void _drawGUI(int id)
        {
            close_button();
            GUILayout.BeginVertical();
            if (!NavBridge.Available)
                GUILayout.Label("NavInstruments not found", GUIStyles.labelStyleLeft);
            else
                DrawNavigation();
            GUILayout.Space(8.0f);
            AutoGUI.AutoDrawObject(this);
            bool was_advanced = show_advanced;
            show_advanced = GUILayout.Toggle(show_advanced, "Advanced", GUIStyles.toggleButtonStyle);
            if (show_advanced)
                DrawAdvanced();
            else if (was_advanced)
                shrink_window = true;
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        void DrawNavigation()
        {
            RunwayInfo rwy = NavBridge.SelectedRunway;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Runway", GUIStyles.labelStyleLeft, GUILayout.Width(55.0f));
            if (GUILayout.Button("<", GUIStyles.toggleButtonStyle, GUILayout.Width(25.0f)))
                NavBridge.StepRunway(-1);
            GUILayout.Label(rwy == null ? "no runway" : (rwy.isINSTarget ? "INS " : "") + rwy.shortID, GUIStyles.labelStyleLeft, GUILayout.Width(90.0f));
            if (GUILayout.Button(">", GUIStyles.toggleButtonStyle, GUILayout.Width(25.0f)))
                NavBridge.StepRunway(1);
            GUILayout.EndHorizontal();
            if (rwy != null)
                GUILayout.Label("        hdg " + rwy.hdg.ToString("000") + "   elev " + rwy.altMSL.ToString("0") + " m   " + rwy.ident, GUIStyles.labelStyleLeft);

            GUILayout.BeginHorizontal();
            GUILayout.Label("GS", GUIStyles.labelStyleLeft, GUILayout.Width(55.0f));
            if (GUILayout.Button("<", GUIStyles.toggleButtonStyle, GUILayout.Width(25.0f)))
                NavBridge.StepGlideslope(-1);
            GUILayout.Label(nav.glideslope.ToString("0.0") + " deg", GUIStyles.labelStyleLeft, GUILayout.Width(90.0f));
            if (GUILayout.Button(">", GUIStyles.toggleButtonStyle, GUILayout.Width(25.0f)))
                NavBridge.StepGlideslope(1);
            GUILayout.EndHorizontal();

            GUILayout.Space(4.0f);
            Armed = GUILayout.Toggle(Armed, "APR  " + StateText(), GUIStyles.toggleButtonStyle);
            if (gate.Length > 0)
                GUILayout.Label(gate, GUIStyles.labelStyleLeft);

            if (nav.valid && rwy != null)
            {
                GUILayout.Label("DME " + (nav.dme / 1000.0).ToString("0.0") + " km   LOC " + nav.locDeviation.ToString("+0.0;-0.0") +
                    " deg   GS " + nav.gsDeviation.ToString("+0.00;-0.00") + " deg", GUIStyles.labelStyleLeft);
                GUILayout.Label("above rwy " + height_above_runway.ToString("0") + " m   spd " + vessel.srfSpeed.ToString("0") + " m/s" +
                    SpeedHoldText(), GUIStyles.labelStyleLeft);
                GUILayout.Label("track " + track.ToString("000") + "   rwy " + nav.runwayHeading.ToString("000") + "   intercept " + intercept.ToString("+0;-0") +
                    " deg   xtk " + (Math.Abs(cross_track) / 1000.0).ToString("0.00") + (cross_track >= 0.0 ? " km R" : " km L"), GUIStyles.labelStyleLeft);
                if (state == ApproachState.Armed)
                {
                    double d = Math.Max(rwy.outerMarkerDist, min_final_dme);
                    GUILayout.Label("GS at " + (d / 1000.0).ToString("0.0") + " km: " + GlideslopeAltitude(rwy, d).ToString("0") + " m MSL   intercept hdg " +
                        Wrap360(nav.runwayHeading + (cross_track >= 0.0 ? -max_intercept_deg : max_intercept_deg)).ToString("000"), GUIStyles.labelStyleLeft);
                }
            }
            GUILayout.Label(FlyingText(), GUIStyles.labelStyleLeft);
        }

        string StateText()
        {
            switch (state)
            {
                case ApproachState.Armed: return "ARMED";
                case ApproachState.Localizer: return "LOC captured - GS armed";
                case ApproachState.Glideslope: return "LOC + GS captured";
                case ApproachState.Minimums: return "MINIMUMS";
                default: return "off";
            }
        }

        string FlyingText()
        {
            if (!steering)
                return (underlying != null ? underlying.ModuleName : "no controller") + " flies";
            if (state == ApproachState.Localizer)
                return "APR flies: hdg " + desired_heading.ToString("000") + "   hold " + hold_altitude.ToString("0") + " m   V/S " + desired_vspeed.ToString("+0.0;-0.0");
            return "APR flies: hdg " + desired_heading.ToString("000") + "   V/S " + desired_vspeed.ToString("+0.0;-0.0") + " m/s";
        }

        string SpeedHoldText()
        {
            if (!couple_speed_control)
                return "";
            if (!thrust_c.spd_control_enabled)
                return "   (speed control off)";
            return "   hold " + thrust_c.setpoint.mps().ToString("0");
        }

        void DrawAdvanced()
        {
            DrawNumber("max intercept (deg)", ref max_intercept_deg, "G3");
            DrawNumber("lateral time const (s)", ref tau_lat, "G3");
            DrawNumber("lookahead min (m)", ref lookahead_min, "G4");
            DrawNumber("lookahead max (m)", ref lookahead_max, "G4");
            DrawNumber("height gain (1/s)", ref height_gain, "G3");
            DrawNumber("V/S gain (1/s)", ref vspeed_gain, "G3");
            DrawNumber("FPA margin (deg)", ref fpa_margin, "G3");
            DrawNumber("director strength mult", ref strength_mult, "G3");
            allow_gs_from_above = GUILayout.Toggle(allow_gs_from_above, "Allow GS capture from above", GUIStyles.toggleButtonStyle);
            use_radar_dh = GUILayout.Toggle(use_radar_dh, "Decision height on radar altitude", GUIStyles.toggleButtonStyle);
        }

        static void DrawNumber(string label, ref double value, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUIStyles.labelStyleLeft);
            string text = GUILayout.TextField(value.ToString(format), GUIStyles.textBoxStyle);
            double parsed;
            if (double.TryParse(text, out parsed))
                value = parsed;
            GUILayout.EndHorizontal();
        }

        #endregion
    }
}
