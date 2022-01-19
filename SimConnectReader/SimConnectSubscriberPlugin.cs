using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SimConnectReader.SimConnectFSX;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;

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
        private Translator translator;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public override async Task<bool> Start()
        {
            logger = LoggerFactory.CreateLogger<SimConnectSubscriberPlugin>();
            flightConnector = new SimConnectFlightConnector(LoggerFactory.CreateLogger<SimConnectFlightConnector>());
            throttlingLogic = new ThrottlingLogic(LoggerFactory.CreateLogger<ThrottlingLogic>());
            translator = new Translator(LoggerFactory.CreateLogger<Translator>());

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

            flightConnector.GenericValuesUpdated += FlightConnector_GenericValuesUpdated;
            flightConnector.Closed += SimConnect_Closed;
            flightConnector.Connected += SimConnect_Connected;

            _ = InitializeSimConnectAsync(flightConnector);

            return true;
        }

        private void SimConnect_Connected(object? sender, ConnectedEventArgs e)
        {
            RegisterConfiguredValues();
            RegisterTriggerValues();
        }

        private async Task InitializeSimConnectAsync(SimConnectFlightConnector simConnect)
        {
            while (true)
            {
                try
                {
                    simConnect.Initialize();
                    simConnect.Send("Connected to ViLa plugin");
                    break;
                }
                catch (COMException ex)
                {
                    logger.LogDebug(ex, "SimConnect error.");
                    await Task.Delay(5000).ConfigureAwait(true);
                }
            }
        }

        private void SimConnect_Closed(object? sender, EventArgs e)
        {
            logger.LogDebug("SimConnect is closed.");
            throttlingLogic.RunAsync(async () =>
            {
                logger.LogDebug("Start reconnecting...");
                var simConnect = sender as SimConnectFlightConnector;

                if (simConnect == null)
                {
                    return;
                }

                await InitializeSimConnectAsync(simConnect);
            }).Forget();
        }

        private void RegisterTriggerValues()
        {
            logger.LogDebug($"Variables from profile config: " + string.Join(" ", Triggers));
            var result = translator.convertFromString(Triggers);
            logger.LogInformation($"Registering {result.Count} variable(s) from profile config");

            flightConnector.RegisterSimValues(result.ToArray());
        }

        private void RegisterConfiguredValues()
        {
            if (pluginConfig == null || pluginConfig.ToggleValues == null)
                return;

            logger.LogDebug($"Variables from plugin config: " + string.Join(" ", pluginConfig.ToggleValues));
            logger.LogInformation($"Registering {pluginConfig.ToggleValues.Length} variable(s) from plugin config");

            flightConnector.RegisterSimValues(pluginConfig.ToggleValues);
        }

        public override async Task Stop()
        {
            flightConnector.CloseConnection();
        }

        private void FlightConnector_GenericValuesUpdated(object? sender, ToggleValueUpdatedEventArgs e)
        {
            foreach (KeyValuePair <TOGGLE_VALUE, double> entry in e.GenericValueStatus)
            {
                var name = entry.Key.ToSimConnectString();
                var value = entry.Value;
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
