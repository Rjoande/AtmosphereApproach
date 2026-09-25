using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace AtmosphereApproach
{
    /// <summary>The runway currently tuned in NavInstruments, copied out of its Runway object.</summary>
    public sealed class RunwayInfo
    {
        public string ident = "---";
        public string shortID = "---";
        public string body = "";
        public float hdg;
        public float altMSL;
        public float gsLatitude, gsLongitude;
        public float locLatitude, locLongitude;
        public float outerMarkerDist, middleMarkerDist, innerMarkerDist;
        public bool isINSTarget;
    }

    /// <summary>ILS numbers for the current frame, as NavInstruments computes them for its HSI.</summary>
    public struct NavData
    {
        public bool valid;          // false: NavInstruments missing, no runway on this body, or a read failed
        public double bearing;      // to the localizer antenna, degrees
        public double dme;          // straight-line distance to the glideslope antenna, meters
        public double elevationAngle;
        public float locDeviation;  // degrees, NavInstruments sign convention
        public float gsDeviation;   // elevationAngle - glideslope, degrees
        public float runwayHeading; // runway course projected at the vessel, degrees
        public float glideslope;    // selected glideslope, degrees
    }

    /// <summary>
    /// Reads the tuned runway, the runway list and the live ILS numbers from NavInstruments
    /// (NavUtilitiesUpdated.dll) by reflection: no compile-time reference, so KSP's load order
    /// cannot break our type loading. Member names follow NavInstruments Continued 0.8.1.x.
    /// </summary>
    public static class NavBridge
    {
        const string DllName = "NavUtilitiesUpdated";
        const string Tag = "[AtmosphereApproach] NavBridge: ";

        static bool resolved;
        static bool available;

        static FieldInfo fNavAidsLoaded;
        static MethodInfo mLoadNavAids, mUpdateNav;
        static FieldInfo fSelectedRwy, fSelectedGs, fCurrentBodyRunways, fRwyIdx, fGsList, fGsIdx;
        static FieldInfo fBearing, fDme, fElev, fLocDev, fGsDev, fRwyHdg, fFallback;
        static FieldInfo[] rwyFields;   // Runway fields, same order as RunwayInfo.Copy
        static readonly string[] rwyFieldNames = {
            "ident", "shortID", "body", "hdg", "altMSL", "gsLatitude", "gsLongitude",
            "locLatitude", "locLongitude", "outerMarkerDist", "middleMarkerDist", "innerMarkerDist", "isINSTarget" };

        static object lastRunwayObject;
        static RunwayInfo lastRunway;
        static bool readFailureLogged;

        public static bool Available { get { Resolve(); return available; } }

        static void Resolve()
        {
            if (resolved)
                return;
            resolved = true;
            try
            {
                Assembly asm = null;
                foreach (AssemblyLoader.LoadedAssembly la in AssemblyLoader.loadedAssemblies)
                    if (la.dllName == DllName) { asm = la.assembly; break; }
                if (asm == null)
                {
                    Debug.LogWarning(Tag + DllName + ".dll not found: no runway data");
                    return;
                }
                Type tSettings = asm.GetType("NavInstruments.NavUtilLib.GlobalVariables.Settings", true);
                Type tFlightData = asm.GetType("NavInstruments.NavUtilLib.GlobalVariables.FlightData", true);
                Type tRunway = asm.GetType("NavInstruments.NavUtilLib.Runway", true);
                const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
                const BindingFlags I = BindingFlags.Public | BindingFlags.Instance;

                fNavAidsLoaded = Require(tSettings.GetField("navAidsIsLoaded", S), "Settings.navAidsIsLoaded");
                mLoadNavAids = Require(tSettings.GetMethod("loadNavAids", S), "Settings.loadNavAids");
                mUpdateNav = Require(tFlightData.GetMethod("updateNavigationData", S), "FlightData.updateNavigationData");
                fSelectedRwy = Require(tFlightData.GetField("selectedRwy", S), "FlightData.selectedRwy");
                fSelectedGs = Require(tFlightData.GetField("selectedGlideSlope", S), "FlightData.selectedGlideSlope");
                fCurrentBodyRunways = Require(tFlightData.GetField("currentBodyRunways", S), "FlightData.currentBodyRunways");
                fRwyIdx = Require(tFlightData.GetField("rwyIdx", S), "FlightData.rwyIdx");
                fGsList = Require(tFlightData.GetField("gsList", S), "FlightData.gsList");
                fGsIdx = Require(tFlightData.GetField("gsIdx", S), "FlightData.gsIdx");
                fBearing = Require(tFlightData.GetField("bearing", S), "FlightData.bearing");
                fDme = Require(tFlightData.GetField("dme", S), "FlightData.dme");
                fElev = Require(tFlightData.GetField("elevationAngle", S), "FlightData.elevationAngle");
                fLocDev = Require(tFlightData.GetField("locDeviation", S), "FlightData.locDeviation");
                fGsDev = Require(tFlightData.GetField("gsDeviation", S), "FlightData.gsDeviation");
                fRwyHdg = Require(tFlightData.GetField("runwayHeading", S), "FlightData.runwayHeading");
                fFallback = Require(tFlightData.GetField("fallback", S), "FlightData.fallback");
                rwyFields = new FieldInfo[rwyFieldNames.Length];
                for (int i = 0; i < rwyFieldNames.Length; i++)
                    rwyFields[i] = Require(tRunway.GetField(rwyFieldNames[i], I), "Runway." + rwyFieldNames[i]);

                available = true;
                Debug.Log(Tag + "resolved " + asm.GetName().Name + " " + asm.GetName().Version);
            }
            catch (Exception e)
            {
                available = false;
                Debug.LogError(Tag + "resolution failed, no runway data: " + e.Message);
            }
        }

        static T Require<T>(T member, string name) where T : class
        {
            if (member == null)
                throw new MissingMemberException(name);
            return member;
        }

        /// <summary>NavInstruments loads its runway database lazily, when the HSI is first shown: force it.</summary>
        public static void EnsureNavAids()
        {
            if (!Available)
                return;
            try
            {
                if (!(bool)fNavAidsLoaded.GetValue(null))
                {
                    mLoadNavAids.Invoke(null, null);
                    Debug.Log(Tag + "runway database loaded");
                }
            }
            catch (Exception e) { LogReadFailure(e); }
        }

        /// <summary>Refreshes NavInstruments' data for this frame (idempotent per UT tick) and reads it.</summary>
        public static NavData Update()
        {
            NavData d = new NavData();
            if (!Available)
                return d;
            try
            {
                EnsureNavAids();
                IList gsList = fGsList.GetValue(null) as IList;
                if (gsList == null || gsList.Count == 0)
                    return d;   // updateNavigationData indexes gsList unconditionally
                mUpdateNav.Invoke(null, null);
                if ((bool)fFallback.GetValue(null))
                    return d;
                d.bearing = (double)fBearing.GetValue(null);
                d.dme = (double)fDme.GetValue(null);
                d.elevationAngle = (double)fElev.GetValue(null);
                d.locDeviation = (float)fLocDev.GetValue(null);
                d.gsDeviation = (float)fGsDev.GetValue(null);
                d.runwayHeading = (float)fRwyHdg.GetValue(null);
                d.glideslope = (float)fSelectedGs.GetValue(null);
                d.valid = SelectedRunway != null;
            }
            catch (Exception e) { LogReadFailure(e); d.valid = false; }
            return d;
        }

        /// <summary>Runway selected in NavInstruments (same as the HSI), or null.</summary>
        public static RunwayInfo SelectedRunway
        {
            get
            {
                if (!Available)
                    return null;
                try
                {
                    object rwy = fSelectedRwy.GetValue(null);
                    if (rwy == null)
                    {
                        lastRunwayObject = null;
                        return lastRunway = null;
                    }
                    if (!ReferenceEquals(rwy, lastRunwayObject))
                    {
                        lastRunwayObject = rwy;
                        lastRunway = Copy(rwy);
                    }
                    return lastRunway;
                }
                catch (Exception e) { LogReadFailure(e); return null; }
            }
        }

        static RunwayInfo Copy(object rwy)
        {
            RunwayInfo r = new RunwayInfo();
            r.ident = (string)rwyFields[0].GetValue(rwy);
            r.shortID = (string)rwyFields[1].GetValue(rwy);
            r.body = (string)rwyFields[2].GetValue(rwy);
            r.hdg = (float)rwyFields[3].GetValue(rwy);
            r.altMSL = (float)rwyFields[4].GetValue(rwy);
            r.gsLatitude = (float)rwyFields[5].GetValue(rwy);
            r.gsLongitude = (float)rwyFields[6].GetValue(rwy);
            r.locLatitude = (float)rwyFields[7].GetValue(rwy);
            r.locLongitude = (float)rwyFields[8].GetValue(rwy);
            r.outerMarkerDist = (float)rwyFields[9].GetValue(rwy);
            r.middleMarkerDist = (float)rwyFields[10].GetValue(rwy);
            r.innerMarkerDist = (float)rwyFields[11].GetValue(rwy);
            r.isINSTarget = (bool)rwyFields[12].GetValue(rwy);
            return r;
        }

        public static int RunwayCount { get { return CountOf(fCurrentBodyRunways); } }
        public static int GlideslopeCount { get { return CountOf(fGsList); } }

        static int CountOf(FieldInfo listField)
        {
            if (!Available)
                return 0;
            try
            {
                IList list = listField.GetValue(null) as IList;
                return list == null ? 0 : list.Count;
            }
            catch (Exception e) { LogReadFailure(e); return 0; }
        }

        /// <summary>Moves the runway selection like the HSI's own buttons (wraps around); the HSI follows.</summary>
        public static void StepRunway(int delta) { Step(fRwyIdx, RunwayCount, delta); }

        public static void StepGlideslope(int delta) { Step(fGsIdx, GlideslopeCount, delta); }

        static void Step(FieldInfo idxField, int count, int delta)
        {
            if (!Available || count <= 0)
                return;
            try
            {
                int idx = ((int)idxField.GetValue(null) + delta) % count;
                if (idx < 0)
                    idx += count;
                idxField.SetValue(null, idx);
            }
            catch (Exception e) { LogReadFailure(e); }
        }

        static void LogReadFailure(Exception e)
        {
            if (readFailureLogged)
                return;
            readFailureLogged = true;
            Debug.LogError(Tag + "read failed (logged once): " + e);
        }
    }
}
