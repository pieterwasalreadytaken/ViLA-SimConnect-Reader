using Microsoft.Extensions.Logging;
using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SimConnectReader.SimConnectFSX
{
    public class SimConnectFlightConnector : IFlightConnector
    {
        public event EventHandler<AircraftStatusUpdatedEventArgs> AircraftStatusUpdated;
        public event EventHandler<ToggleValueUpdatedEventArgs> GenericValuesUpdated;
        public event EventHandler<InvalidEventRegisteredEventArgs> InvalidEventRegistered;
        public event EventHandler<ConnectedEventArgs> Connected;
        public event EventHandler Closed;

        // Extra SimConnect functions via native pointer
        IntPtr hSimConnect;
        [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int /* HRESULT */ SimConnect_GetLastSentPacketID(IntPtr hSimConnect, out uint /* DWORD */ dwSendID);

        private EventWaitHandle simConnectEventHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        private Thread simConnectReceiveThread = null;

        private const int StatusDelayMilliseconds = 100;

        /// <summary>
        /// This is a reference counter to make sure we do not deregister variables that are still in use.
        /// </summary>
        private readonly Dictionary<TOGGLE_VALUE, int> genericValues = new Dictionary<TOGGLE_VALUE, int>();

        private readonly object lockLists = new object();

        // User-defined win32 event
        const int WM_USER_SIMCONNECT = 0x0402;
        private readonly ILogger<SimConnectFlightConnector> logger;

        public IntPtr Handle { get; private set; }

        private SimConnect simconnect = null;
        private CancellationTokenSource cts = null;

        public SimConnectFlightConnector(ILogger<SimConnectFlightConnector> logger)
        {
            this.logger = logger;
        }

        private void SimConnectMessageReceiveThreadHandler()
        {
            while (true)
            {
                simConnectEventHandle.WaitOne();

                try
                {
                    simconnect.ReceiveMessage();
                }
                catch (Exception ex)
                {
                    RecoverFromError(ex);
                }
            }
        }

        // Set up the SimConnect event handlers
        public void Initialize()
        {
            if (simconnect != null)
            {
                logger.LogWarning("Initialization is already done. Cancelled this request.");
                return;
            }

            simconnect = new SimConnect("ViLA SimConnect Plugin", IntPtr.Zero, WM_USER_SIMCONNECT, simConnectEventHandle, 0);

            // Get direct access to the SimConnect handle, to use functions otherwise not supported.
            FieldInfo fiSimConnect = typeof(SimConnect).GetField("hSimConnect", BindingFlags.NonPublic | BindingFlags.Instance);
            hSimConnect = (IntPtr)fiSimConnect.GetValue(simconnect);

            // Start listening Thread
            simConnectReceiveThread = new Thread(new ThreadStart(SimConnectMessageReceiveThreadHandler));
            simConnectReceiveThread.IsBackground = true;
            simConnectReceiveThread.Start();

            // listen to connect and quit msgs
            simconnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(Simconnect_OnRecvOpen);
            simconnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(Simconnect_OnRecvQuit);

            // listen to exceptions
            simconnect.OnRecvException += Simconnect_OnRecvException;
            
            // listen to data
            simconnect.OnRecvSimobjectDataBytype += Simconnect_OnRecvSimobjectDataBytypeAsync;
            simconnect.OnRecvSystemState += Simconnect_OnRecvSystemState;

            RegisterFlightStatusDefinition();

            isGenericValueRegistered = false;
            RegisterGenericValues();
        }

        public void CloseConnection()
        {
            try
            {
                logger.LogDebug("Trying to cancel request loop");
                cts?.Cancel();
                cts = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"Cannot cancel request loop! Error: {ex.Message}");
            }
            try
            {
                // Dispose serves the same purpose as SimConnect_Close()
                simconnect?.Dispose();
                simconnect = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"Cannot unsubscribe events! Error: {ex.Message}");
            }
        }

        private void RegisterFlightStatusDefinition()
        {
            void AddToFlightStatusDefinition(string simvar, string unit, SIMCONNECT_DATATYPE type)
            {
                simconnect.AddToDataDefinition(DEFINITIONS.FlightStatus, simvar, unit, type, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            }

            AddToFlightStatusDefinition("SIMULATION RATE", "number", SIMCONNECT_DATATYPE.INT32);

            AddToFlightStatusDefinition("PLANE LATITUDE", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("PLANE LONGITUDE", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("PLANE ALTITUDE", "Feet", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("PLANE ALT ABOVE GROUND", "Feet", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("PLANE PITCH DEGREES", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("PLANE BANK DEGREES", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("PLANE HEADING DEGREES TRUE", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("PLANE HEADING DEGREES MAGNETIC", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("GROUND ALTITUDE", "Meters", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("GROUND VELOCITY", "Knots", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("AIRSPEED INDICATED", "Knots", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("VERTICAL SPEED", "Feet per minute", SIMCONNECT_DATATYPE.FLOAT64);

            AddToFlightStatusDefinition("FUEL TOTAL QUANTITY", "Gallons", SIMCONNECT_DATATYPE.FLOAT64);

            AddToFlightStatusDefinition("AMBIENT WIND VELOCITY", "Feet per second", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("AMBIENT WIND DIRECTION", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);

            AddToFlightStatusDefinition("SIM ON GROUND", "number", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("STALL WARNING", "number", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("OVERSPEED WARNING", "number", SIMCONNECT_DATATYPE.INT32);

            #region Autopilot

            AddToFlightStatusDefinition("AUTOPILOT MASTER", "number", SIMCONNECT_DATATYPE.INT32);

            AddToFlightStatusDefinition("AUTOPILOT HEADING LOCK", "number", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("AUTOPILOT HEADING LOCK DIR", "Degrees", SIMCONNECT_DATATYPE.INT32);

            AddToFlightStatusDefinition("AUTOPILOT NAV1 LOCK", "number", SIMCONNECT_DATATYPE.INT32);

            AddToFlightStatusDefinition("AUTOPILOT APPROACH HOLD", "number", SIMCONNECT_DATATYPE.INT32);

            AddToFlightStatusDefinition("AUTOPILOT ALTITUDE LOCK", "number", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("AUTOPILOT ALTITUDE LOCK VAR", "Feet", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("AUTOPILOT ALTITUDE LOCK VAR:1", "Feet", SIMCONNECT_DATATYPE.INT32);

            AddToFlightStatusDefinition("AUTOPILOT VERTICAL HOLD", "number", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("AUTOPILOT VERTICAL HOLD VAR", "Feet per minute", SIMCONNECT_DATATYPE.INT32);

            AddToFlightStatusDefinition("AUTOPILOT FLIGHT LEVEL CHANGE", "number", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("AUTOPILOT AIRSPEED HOLD VAR", "Knots", SIMCONNECT_DATATYPE.INT32);

            #endregion

            AddToFlightStatusDefinition("KOHLSMAN SETTING MB", "number", SIMCONNECT_DATATYPE.INT32);

            AddToFlightStatusDefinition("TRANSPONDER CODE:1", "Hz", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("COM ACTIVE FREQUENCY:1", "kHz", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("COM ACTIVE FREQUENCY:2", "kHz", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("AVIONICS MASTER SWITCH", "number", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("NAV OBS:1", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("NAV OBS:2", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("ADF CARD", "Degrees", SIMCONNECT_DATATYPE.FLOAT64);
            AddToFlightStatusDefinition("ADF ACTIVE FREQUENCY:1", "Hz", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("ADF STANDBY FREQUENCY:1", "Hz", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("ADF ACTIVE FREQUENCY:2", "Hz", SIMCONNECT_DATATYPE.INT32);
            AddToFlightStatusDefinition("ADF STANDBY FREQUENCY:2", "Hz", SIMCONNECT_DATATYPE.INT32);

            // IMPORTANT: register it with the simconnect managed wrapper marshaller
            // if you skip this step, you will only receive a uint in the .dwData field.
            simconnect.RegisterDataDefineStruct<FlightStatusStruct>(DEFINITIONS.FlightStatus);
            logger.LogDebug("Registered aircarft info");
        }

        private void Simconnect_OnRecvSimobjectDataBytypeAsync(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            // Must be general SimObject information
            switch (data.dwRequestID)
            {
                case (uint)DATA_REQUESTS.FLIGHT_STATUS:
                    {
                        var flightStatus = data.dwData[0] as FlightStatusStruct?;

                        if (flightStatus.HasValue)
                        {
                            AircraftStatusUpdated?.Invoke(this, new AircraftStatusUpdatedEventArgs(
                                new AircraftStatus
                                {
                                    //SimTime = flightStatus.Value.SimTime,
                                    //SimRate = flightStatus.Value.SimRate,
                                    Latitude = flightStatus.Value.Latitude,
                                    Longitude = flightStatus.Value.Longitude,
                                    Altitude = flightStatus.Value.Altitude,
                                    AltitudeAboveGround = flightStatus.Value.AltitudeAboveGround,
                                    Pitch = flightStatus.Value.Pitch,
                                    Bank = flightStatus.Value.Bank,
                                    Heading = flightStatus.Value.MagneticHeading,
                                    TrueHeading = flightStatus.Value.TrueHeading,
                                    GroundSpeed = flightStatus.Value.GroundSpeed,
                                    IndicatedAirSpeed = flightStatus.Value.IndicatedAirSpeed,
                                    VerticalSpeed = flightStatus.Value.VerticalSpeed,
                                    FuelTotalQuantity = flightStatus.Value.FuelTotalQuantity,
                                    IsOnGround = flightStatus.Value.IsOnGround == 1,
                                    StallWarning = flightStatus.Value.StallWarning == 1,
                                    OverspeedWarning = flightStatus.Value.OverspeedWarning == 1,
                                    IsAutopilotOn = flightStatus.Value.IsAutopilotOn == 1,
                                    IsApHdgOn = flightStatus.Value.IsApHdgOn == 1,
                                    ApHeading = flightStatus.Value.ApHdg,
                                    IsApNavOn = flightStatus.Value.IsApNavOn == 1,
                                    IsApAprOn = flightStatus.Value.IsApAprOn == 1,
                                    IsApAltOn = flightStatus.Value.IsApAltOn == 1,
                                    ApAltitude0 = flightStatus.Value.ApAlt0,
                                    ApAltitude1 = flightStatus.Value.ApAlt1,
                                    IsApVsOn = flightStatus.Value.IsApVsOn == 1,
                                    IsApFlcOn = flightStatus.Value.IsApFlcOn == 1,
                                    ApAirspeed = flightStatus.Value.ApAirspeed,
                                    ApVs = flightStatus.Value.ApVs,
                                    QNHMbar = flightStatus.Value.QNHmbar,
                                    Transponder = flightStatus.Value.Transponder.ToString().PadLeft(4, '0'),
                                    FreqencyCom1 = flightStatus.Value.Com1,
                                    FreqencyCom2 = flightStatus.Value.Com2,
                                    IsAvMasterOn = flightStatus.Value.AvMasterOn == 1,
                                    Nav1OBS = flightStatus.Value.Nav1OBS,
                                    Nav2OBS = flightStatus.Value.Nav2OBS,
                                    ADFCard = flightStatus.Value.ADFCard,
                                    ADFActiveFrequency1 = flightStatus.Value.ADFActive1,
                                    ADFStandbyFrequency1 = flightStatus.Value.ADFStandby1,
                                    ADFActiveFrequency2 = flightStatus.Value.ADFActive2,
                                    ADFStandbyFrequency2 = flightStatus.Value.ADFStandby2,
                                }));
                        }
                        else
                        {
                            // Cast failed
                            logger.LogError("Cannot cast to FlightStatusStruct!");
                        }
                    }
                    break;

                case (uint)DATA_REQUESTS.TOGGLE_VALUE_DATA:
                    {
                        var result = new Dictionary<TOGGLE_VALUE, double>();
                        lock (lockLists)
                        {
                            if (data.dwDefineCount != genericValues.Count)
                            {
                                logger.LogError("Incompatible array count {actual}, expected {expected}. Skipping received data", data.dwDefineCount, genericValues.Count);
                                return;
                            }

                            var dataArray = data.dwData[0] as GenericValuesStruct?;

                            if (!dataArray.HasValue)
                            {
                                logger.LogError("Invalid data received");
                                return;
                            }

                            for (int i = 0; i < data.dwDefineCount; i++)
                            {
                                var genericValue = genericValues.Keys.ElementAt(i);
                                result.Add(genericValue, dataArray.Value.Get(i));
                            }
                        }

                        GenericValuesUpdated?.Invoke(this, new ToggleValueUpdatedEventArgs(result));
                    }
                    break;
            }
        }

        private void Simconnect_OnRecvSystemState(SimConnect sender, SIMCONNECT_RECV_SYSTEM_STATE data)
        {
            switch (data.dwRequestID)
            {
                case (int)DATA_REQUESTS.FLIGHT_PLAN:
                    if (!string.IsNullOrEmpty(data.szString))
                    {
                        logger.LogInformation("Receive flight plan {flightPlan}", data.szString);
                    }
                    break;
            }
        }

        void Simconnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            logger.LogInformation("Connected to Flight Simulator");
            Connected?.Invoke(this, new ConnectedEventArgs(0));

            cts?.Cancel();
            cts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(StatusDelayMilliseconds);
                        await smGeneric.WaitAsync();
                        try
                        {
                            cts?.Token.ThrowIfCancellationRequested();
                            simconnect?.RequestDataOnSimObjectType(DATA_REQUESTS.FLIGHT_STATUS, DEFINITIONS.FlightStatus, 0, SIMCONNECT_SIMOBJECT_TYPE.USER);

                            if (genericValues.Count > 0 && isGenericValueRegistered)
                            {
                                simconnect?.RequestDataOnSimObjectType(DATA_REQUESTS.TOGGLE_VALUE_DATA, DEFINITIONS.GenericData, 0, SIMCONNECT_SIMOBJECT_TYPE.USER);
                            }
                        }
                        finally
                        {
                            smGeneric.Release();
                        }
                    }
                }
                catch (TaskCanceledException) { }
            });
        }

        // The case where the user closes Flight Simulator
        public void Simconnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            logger.LogInformation("Flight Simulator has exited");
            CloseConnection();
            Closed?.Invoke(this, new EventArgs());
        }

        public void Simconnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            logger.LogError("Exception received: {error} from {sendID}", (SIMCONNECT_EXCEPTION)data.dwException, data.dwSendID);
            switch ((SIMCONNECT_EXCEPTION)data.dwException)
            {
                case SIMCONNECT_EXCEPTION.ERROR:
                    // Try to reconnect on unknown error
                    CloseConnection();
                    Closed?.Invoke(this, new EventArgs());
                    break;

                case SIMCONNECT_EXCEPTION.NAME_UNRECOGNIZED:
                    InvalidEventRegistered?.Invoke(this, new InvalidEventRegisteredEventArgs(data.dwSendID));
                    break;

                case SIMCONNECT_EXCEPTION.VERSION_MISMATCH:
                    // HACK: when sending an event repeatedly,
                    // SimConnect might sendd thihs error and stop reacting and responding.
                    // The workaround would be to force a reconnection.
                    CloseConnection();
                    Closed?.Invoke(this, new EventArgs());
                    break;
            }
        }

        private void RecoverFromError(Exception exception)
        {
            // 0xC000014B: CTD
            // 0xC00000B0: Sim has exited or any generic SimConnect error
            // 0xC000014B: STATUS_PIPE_BROKEN
            logger.LogWarning(exception, "Exception received");
            CloseConnection();
            Closed?.Invoke(this, new EventArgs());
        }

        private uint GetLastSendID()
        {
            SimConnect_GetLastSentPacketID(hSimConnect, out uint dwSendID);
            return dwSendID;
        }

        #region Generic Buttons

        public uint? RegisterToggleEvent(Enum eventEnum, string eventName)
        {
            if (simconnect == null) return null;

            logger.LogInformation("RegisterEvent {action} {simConnectAction}", eventEnum, eventName);
            simconnect.MapClientEventToSimEvent(eventEnum, eventName);

            return GetLastSendID();
        }

        public void RegisterSimValues(params TOGGLE_VALUE[] simValues)
        {
            var changed = false;
            lock (lockLists)
            {
                logger.LogDebug("Registering {values}", string.Join(", ", simValues));
                foreach (var simValue in simValues)
                {
                    if (genericValues.ContainsKey(simValue))
                    {
                        genericValues[simValue]++;
                    }
                    else
                    {
                        genericValues.Add(simValue, 1);
                        changed = true;
                    }
                }
            }
            if (changed)
            {
                RegisterGenericValues();
            }
        }

        public void RegisterSimValue(TOGGLE_VALUE simValue)
        {
            var changed = false;
            lock (lockLists)
            {
                if (genericValues.ContainsKey(simValue))
                {
                    genericValues[simValue]++;
                }
                else
                {
                    genericValues.Add(simValue, 1);
                    changed = true;
                }
            }
            if (changed)
            {
                RegisterGenericValues();
            }
        }

        public void Send(string message)
        {
            simconnect?.Text(SIMCONNECT_TEXT_TYPE.PRINT_BLACK, 3, EVENTS.MESSAGE_RECEIVED, message);
        }

        public void DeRegisterSimValues(params TOGGLE_VALUE[] simValues)
        {
            var changed = false;
            lock (lockLists)
            {
                logger.LogDebug("De-Registering {values}", string.Join(", ", simValues));
                foreach (var simValue in simValues)
                {
                    if (genericValues.ContainsKey(simValue))
                    {
                        var currentCount = genericValues[simValue];
                        if (currentCount > 1)
                        {
                            genericValues[simValue]--;
                        }
                        else
                        {
                            genericValues.Remove(simValue);
                            changed = true;
                        }
                    }
                }
            }
            if (changed)
            {
                RegisterGenericValues();
            }
        }

        private CancellationTokenSource ctsGeneric = null;
        private readonly object lockGeneric = new object();
        private readonly SemaphoreSlim smGeneric = new SemaphoreSlim(1);
        private bool isGenericValueRegistered = false;

        private void RegisterGenericValues()
        {
            if (simconnect == null) return;

            CancellationTokenSource cts;
            lock (lockGeneric)
            {
                ctsGeneric?.Cancel();
                cts = ctsGeneric = new CancellationTokenSource();
            }

            Task.Run(async () =>
            {
                await smGeneric.WaitAsync();
                try
                {

                    await Task.Delay(500, cts.Token);
                    cts.Token.ThrowIfCancellationRequested();

                    if (simconnect == null) return;

                    if (isGenericValueRegistered)
                    {
                        logger.LogInformation("Clearing Data definition");
                        simconnect.ClearDataDefinition(DEFINITIONS.GenericData);
                        isGenericValueRegistered = false;
                    }

                    if (genericValues.Count == 0)
                    {
                        logger.LogInformation("Registration is not needed.");
                    }
                    else
                    {
                        var log = "Registering generic data structure:";

                        foreach (TOGGLE_VALUE simValue in genericValues.Keys)
                        {
                            string value = simValue.ToSimConnectString();
                            var simUnit = ValueLibrary.GetUnit(simValue);
                            log += string.Format("\n- {0} {1} {2}", simValue, value, simUnit);

                            simconnect.AddToDataDefinition(
                                DEFINITIONS.GenericData,
                                value,
                                simUnit,
                                SIMCONNECT_DATATYPE.FLOAT64,
                                0.0f,
                                SimConnect.SIMCONNECT_UNUSED
                            );
                        }

                        logger.LogInformation(log);

                        simconnect.RegisterDataDefineStruct<GenericValuesStruct>(DEFINITIONS.GenericData);

                        isGenericValueRegistered = true;
                    }
                }
                catch (TaskCanceledException)
                {
                    logger.LogDebug("Registration is cancelled.");
                }
                finally
                {
                    smGeneric.Release();
                }
            });
        }
        #endregion
    }
}
