using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SimConnectReader.SimConnectFSX;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace ViLA.Extensions.SimConnectReader
{
    public class SimConnectSubscriberPlugin : PluginBase.PluginBase
    {
        /// <summary>
        /// ConfigPath is relative to ViLA, *not* to this dll
        /// </summary>
        public const string ConfigPath = "Plugins/SimConnectReader/config.json";
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private ILogger logger;
        private SimConnectFlightConnector flightConnector;
        private ThrottlingLogic throttlingLogic;
        private PluginConfiguration pluginConfig;
        private int previous = 0;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public override async Task<bool> Start()
        {
            logger = LoggerFactory.CreateLogger<SimConnectSubscriberPlugin>();
            flightConnector = new SimConnectFlightConnector(LoggerFactory.CreateLogger<SimConnectFlightConnector>());
            throttlingLogic = new ThrottlingLogic(LoggerFactory.CreateLogger<ThrottlingLogic>());

            try
            {
                pluginConfig = await GetConfiguration();
            }
            catch (JsonSerializationException ex)
            {
                logger.LogError("Encountered error while loading configuration for SimConnectReader. Skipping...");
                logger.LogDebug(ex, "Exception:");
                return false;
            }

            logger.LogInformation("Starting SimConnect listener...");
            flightConnector.Initialize();
            flightConnector.GenericValuesUpdated += FlightConnector_GenericValuesUpdated;

            RegisterConfiguredValues();

            return true;
        }

        private void RegisterConfiguredValues()
        {
            if (pluginConfig == null || pluginConfig.ToggleValues == null)
                return;

            foreach(TOGGLE_VALUE value in pluginConfig.ToggleValues)
            {
                flightConnector.RegisterSimValue(value);
            }
        }

        public override async Task Stop()
        {

        }

        private void FlightConnector_GenericValuesUpdated(object? sender, ToggleValueUpdatedEventArgs e)
        {
            foreach (KeyValuePair <TOGGLE_VALUE, double> entry in e.GenericValueStatus)
            {
                var name = entry.Key.ToSimConnectString();
                var value = (int)entry.Value;
                Send(name, value);
            }
        }

        /// <summary>
        /// Gets the existing plugin configuration, or writes a new configuration file.
        /// </summary>
        /// <exception cref="JsonSerializationException">Thrown when deserializing the plugin config into the expected POCO fails.</exception>
        private static async Task<PluginConfiguration> GetConfiguration()
        {
            if (File.Exists(ConfigPath))
            {
                var configString = await File.ReadAllTextAsync(ConfigPath);
                return JsonConvert.DeserializeObject<PluginConfiguration>(configString) ?? throw new JsonSerializationException("Result was null");
            }

            var pluginConfig = new PluginConfiguration();
            await File.WriteAllTextAsync(ConfigPath, JsonConvert.SerializeObject(pluginConfig));

            return pluginConfig!;
        }
    }
}
