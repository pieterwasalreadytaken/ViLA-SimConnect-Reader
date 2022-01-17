using SimConnectReader.SimConnectFSX;

namespace ViLA.Extensions.SimConnectReader
{
    public class PluginConfiguration
    {
        public string? AliasesFileName { get; set; } = "AircraftAliases.json";

        public TOGGLE_VALUE[]? ToggleValues { get; set; } =
            {

            };
    }
}