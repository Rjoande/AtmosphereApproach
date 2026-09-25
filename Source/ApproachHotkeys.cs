using System;
using System.Collections.Generic;
using AtmosphereAutopilot;
using UnityEngine;

namespace AtmosphereApproach
{
    /// <summary>
    /// Polls the APR hotkey in every flight frame: AtmosphereAutopilot only updates its active
    /// module, while an APR button must work whatever autopilot is flying. Selects the Approach
    /// controller (switching the master on) before arming when needed.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ApproachHotkeys : MonoBehaviour
    {
        const string Tag = "[AtmosphereApproach] ";

        void Update()
        {
            KeyCode key = ApproachController.apr_arm_key;
            if (key == KeyCode.None || !Input.GetKeyDown(key))
                return;
            if (FlightDriver.Pause || !InputLockManager.IsUnlocked(ControlTypes.KEYBOARDINPUT))
                return;
            Vessel v = FlightGlobals.ActiveVessel;
            global::AtmosphereAutopilot.AtmosphereAutopilot aa = global::AtmosphereAutopilot.AtmosphereAutopilot.Instance;
            if (v == null || aa == null)
                return;
            Dictionary<Type, AutopilotModule> modules = aa.getVesselModules(v);
            if (modules == null || !modules.ContainsKey(typeof(TopModuleManager)))
            {
                Debug.Log(Tag + "APR hotkey ignored: no AtmosphereAutopilot manager for " + v.vesselName);
                return;
            }
            TopModuleManager manager = modules[typeof(TopModuleManager)] as TopModuleManager;
            ApproachController apr = GetController(modules);
            if (apr == null || !apr.Active)
            {
                manager.activateAutopilot(typeof(ApproachController));
                aa.mainMenuGUIUpdate();
                apr = GetController(aa.getVesselModules(v));
                Debug.Log(Tag + "APR hotkey: Approach controller selected");
            }
            if (apr == null)
            {
                Debug.LogWarning(Tag + "APR hotkey ignored: Approach controller not registered in AtmosphereAutopilot");
                return;
            }
            apr.Armed = !apr.Armed;
            Debug.Log(Tag + "APR hotkey fired (" + key + "), armed = " + apr.Armed);
        }

        static ApproachController GetController(Dictionary<Type, AutopilotModule> modules)
        {
            AutopilotModule m;
            return modules != null && modules.TryGetValue(typeof(ApproachController), out m) ? m as ApproachController : null;
        }
    }
}
