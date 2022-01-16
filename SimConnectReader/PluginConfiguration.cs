using SimConnectReader.SimConnectFSX;

namespace ViLA.Extensions.SimConnectReader
{
    public class PluginConfiguration
    {
        public string? AliasesFileName { get; set; } = "AircraftAliases.json";

        public TOGGLE_VALUE[]? ToggleValues { get; set; } =
            {
                TOGGLE_VALUE.ELECTRICAL_MASTER_BATTERY,
                TOGGLE_VALUE.AVIONICS_MASTER_SWITCH,
                TOGGLE_VALUE.AUTOPILOT_MASTER,
                TOGGLE_VALUE.AUTOPILOT_ALTITUDE_LOCK,
                TOGGLE_VALUE.AUTOPILOT_HEADING_LOCK,
                TOGGLE_VALUE.AUTOPILOT_NAV1_LOCK,
                TOGGLE_VALUE.AUTOPILOT_VERTICAL_HOLD,
                TOGGLE_VALUE.AUTOPILOT_APPROACH_HOLD
            };
    }
}