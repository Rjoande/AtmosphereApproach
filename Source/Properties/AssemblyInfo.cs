using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("AtmosphereApproach")]
[assembly: AssemblyDescription("APR (ILS approach) autopilot mode for AtmosphereAutopilot")]
[assembly: AssemblyProduct("AtmosphereApproach")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Rjoande, GPL-3.0")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

[assembly: KSPAssembly("AtmosphereApproach", 0, 1)]
// Loads AtmosphereAutopilot first (0.0.0 = no KSPAssembly declared) and refuses to load without it.
[assembly: KSPAssemblyDependency("AtmosphereAutopilot", 0, 0)]
// Install gate only: NavInstruments is reached by reflection.
[assembly: KSPAssemblyDependency("NavInstrumentsContinued", 0, 8)]
