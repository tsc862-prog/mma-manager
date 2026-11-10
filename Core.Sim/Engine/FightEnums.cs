namespace MmaManager.Core.Sim.Engine;

/// <summary>Where the fight currently is. Drives which actions are available.</summary>
internal enum Phase { Standing, Clinch, Ground }

/// <summary>
/// Ground control positions in ascending dominance for the top fighter. Higher
/// positions give more ground-strike damage and submission threat, and are
/// harder for the bottom fighter to escape or sweep from.
/// </summary>
internal enum GroundPos { Guard, HalfGuard, SideControl, Mount, BackControl }
