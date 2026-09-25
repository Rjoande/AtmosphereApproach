using System;
using System.Collections.Generic;
using AtmosphereAutopilot;
using UnityEngine;

namespace AtmosphereApproach
{
    /// <summary>
    /// APR mode: arms on the runway tuned in NavInstruments, captures localizer then glideslope and
    /// flies the approach down to the decision height. Listed by AtmosphereAutopilot as one of its
    /// high-level autopilots. Preview build: arming and capture detection only, a delegated AA
    /// controller (Cruise Flight or Fly-By-Wire) flies the aircraft.
    /// </summary>
    public sealed class ApproachController : StateController
    {
        const string Tag = "[AtmosphereApproach] ";

        // Window id must be unique among every IMGUI window in the game: "AAPR" as bytes.
        internal ApproachController(Vessel v) : base(v, "Approach controller", 0x41415052) { }

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

        // Controller flying the aircraft while APR is not (yet) steering; activated from inside this
        // module the same way Cruise Flight activates its director, and released in OnDeactivate.
        StateController underlying;

        StateController Underlying { get { return armed_with_cruise ? (StateController)cruise : fbw; } }

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
                        return;
                    }
                    state = ApproachState.Armed;
                    MessageManager.post_status_message("APR armed: " + rwy.shortID);
                }
                else
                {
                    state = ApproachState.Off;
                    gate = "";
                    MessageManager.post_status_message("APR off");
                }
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

        [GlobalSerializable("apr_arm_key")]
        [AutoHotkeyAttr("APR arm/disarm")]
        static KeyCode apr_arm_key = KeyCode.None;

        #endregion

        #region Lifecycle

        protected override void OnActivate()
        {
            NavBridge.EnsureNavAids();
            state = ApproachState.Off;
            gate = "";
            underlying = Underlying;
            underlying.Activate();
            MessageManager.post_status_message("Approach controller enabled");
        }

        protected override void OnDeactivate()
        {
            if (underlying != null)
                underlying.Deactivate();
            underlying = null;
            state = ApproachState.Off;
            gate = "";
            MessageManager.post_status_message("Approach controller disabled");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(apr_arm_key))
                Armed = !Armed;
        }

        #endregion

        #region Control

        NavData nav;
        double track;          // ground track, degrees
        double intercept;      // track - runway course, degrees, -180..180
        double height_above_runway;

        public override void ApplyControl(FlightCtrlState cntrl)
        {
            if (vessel.LandedOrSplashed)
                return;
            if (underlying != Underlying)
            {
                // option changed while active: swap the delegated controller
                underlying.Deactivate();
                underlying = Underlying;
                underlying.Activate();
            }
            UpdateNavigation();
            UpdateState();
            // M0: no steering law yet, the delegated controller flies in every state
            underlying.ApplyControl(cntrl);
        }

        void UpdateNavigation()
        {
            nav = NavBridge.Update();
            RunwayInfo rwy = NavBridge.SelectedRunway;
            if (!nav.valid || rwy == null)
                return;
            Vector3d hv = Vector3d.Exclude(vessel.upAxis, vessel.srf_velocity);
            track = Wrap360(Math.Atan2(Vector3d.Dot(hv, vessel.east), Vector3d.Dot(hv, vessel.north)) * 180.0 / Math.PI);
            intercept = Wrap180(track - nav.runwayHeading);
            height_above_runway = vessel.altitude - rwy.altMSL;
        }

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
                        state = ApproachState.Localizer;
                        gate = "";
                        MessageManager.post_status_message("APR: localizer captured");
                    }
                    break;

                case ApproachState.Localizer:
                    if (loc > 3.0 * loc_capture_deg)
                    {
                        state = ApproachState.Armed;
                        MessageManager.post_status_message("APR: localizer lost");
                    }
                    else if (Math.Abs(nav.gsDeviation) <= gs_capture_deg)
                    {
                        state = ApproachState.Glideslope;
                        gate = "";
                        MessageManager.post_status_message("APR: glideslope captured");
                    }
                    else
                        gate = (nav.gsDeviation > 0 ? "above GS " : "below GS ") + nav.gsDeviation.ToString("+0.00;-0.00") + " deg";
                    break;

                case ApproachState.Glideslope:
                    if (loc > 3.0 * loc_capture_deg)
                    {
                        state = ApproachState.Armed;
                        MessageManager.post_status_message("APR: localizer lost");
                    }
                    else if (height_above_runway < decision_height)
                    {
                        state = ApproachState.Minimums;
                        MessageManager.post_status_message("APR: MINIMUMS");
                    }
                    break;

                case ApproachState.Minimums:
                    // M1: hand over to Fly-By-Wire or switch the master off here
                    break;
            }
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

        #region GUI

        [AutoGuiAttr("Director controller GUI", true)]
        public bool DircGUI { get { return dir_c.IsShown(); } set { if (value) dir_c.ShowGUI(); else dir_c.UnShowGUI(); } }

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
            GUILayout.Space(4.0f);
            GUILayout.Label("Preview build: " + (underlying != null ? underlying.ModuleName : "no controller") + " flies, APR does not steer yet", GUIStyles.labelStyleLeft);
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
            string rwyText = rwy == null ? "no runway" :
                (rwy.isINSTarget ? "INS " : "") + rwy.shortID + " hdg " + rwy.hdg.ToString("000") + " elev " + rwy.altMSL.ToString("0") + " m";
            GUILayout.Label(rwyText, GUIStyles.labelStyleLeft);
            if (GUILayout.Button(">", GUIStyles.toggleButtonStyle, GUILayout.Width(25.0f)))
                NavBridge.StepRunway(1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("GS", GUIStyles.labelStyleLeft, GUILayout.Width(55.0f));
            if (GUILayout.Button("<", GUIStyles.toggleButtonStyle, GUILayout.Width(25.0f)))
                NavBridge.StepGlideslope(-1);
            GUILayout.Label(nav.glideslope.ToString("0.0") + " deg", GUIStyles.labelStyleLeft);
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
                    "   GS " + nav.gsDeviation.ToString("+0.00;-0.00"), GUIStyles.labelStyleLeft);
                GUILayout.Label("above rwy " + height_above_runway.ToString("0") + " m   trk " + track.ToString("000") +
                    "   int " + intercept.ToString("+0;-0") + "   spd " + vessel.srfSpeed.ToString("0") + " m/s", GUIStyles.labelStyleLeft);
            }
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

        #endregion
    }
}
