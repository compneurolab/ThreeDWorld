﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LitJson;
using NetMQ.Sockets;
using System.Net;
using System.Net.Sockets;


/// <summary>
/// Component that forces Unity Rigidbodies physics to only update
/// when the simulation is telling it to. This ensures an even framerate/time
/// for the agent.
/// </summary>
public static class SimulationManager
{
    enum MyLogLevel {
        LogAll,
        Warning,
        Errors,
        None
    }
#region Fields
    public static int numPhysicsFramesPerUpdate = 5;
    private static JsonData _readJsonArgs = null;
    private static int framesToProcess = 0;
    private static int profilerFrames = 1<<19; // Max of 8 << 20
    private static int totalFramesProcessed = 0;
    private static float physicsTimeMultiplier = 1.0f;
    private static int targetFrameRate = 300;
    private static bool _hasFinishedInit = false;
    private static NetMessenger myNetMessenger = null;
    private static MyLogLevel logLevel = MyLogLevel.LogAll;
    private static MyLogLevel stackLogLevel = MyLogLevel.Warning;
	private static string logFileLocation = "output_log.txt";
	private static string portNumber = "5556";
	private static string hostAddress = getHostIP();
        private static string portNumber_info = "5555";
#endregion

#region Properties
    public static bool shouldRun {
        get {
            return framesToProcess > 0;
        }
    }

    
    public static float timeElapsed {
        get {
            return totalFramesProcessed * Time.fixedDeltaTime;
        }
    }

    public static JsonData argsConfig {
        get {
            return _readJsonArgs;
        }
    }

	public static string getPortNumber {
		get {
			return portNumber;
		}
	}

	public static string getHostAddress {
		get {
			return hostAddress;
		}
	}
#endregion

	public static void setArgsConfig(JsonData jsonData) {
		_readJsonArgs = jsonData;
	}

        public static JsonData sendMongoDBsearch(JsonData jsonData){
            Debug.Log("I am in Sending message!" + jsonData.ToJSON());
            return myNetMessenger.SendAndReceiveMongoDB(jsonData);
        }

	private static string getHostIP() {
		IPHostEntry host;
		string localIP = "?";
		host = Dns.GetHostEntry(Dns.GetHostName());
		foreach (IPAddress ip in host.AddressList)
		{
			if (ip.AddressFamily.ToString() == "InterNetwork")
			{
				localIP = ip.ToString();
			}
		}
		return localIP;
	}

    public static bool FinishUpdatingFrames()
    {
        if (framesToProcess > 0)
        {
            --framesToProcess;
            ++totalFramesProcessed;
            Time.timeScale = (framesToProcess > 0) ? physicsTimeMultiplier : 0.0f;
            Time.captureFramerate = (framesToProcess > 0) ? Mathf.FloorToInt(physicsTimeMultiplier / (Time.fixedDeltaTime * numPhysicsFramesPerUpdate)) : 0;
            Application.targetFrameRate = -1;
            return framesToProcess == 0;
        }
        return false;
    }

    public static void ToggleUpdates()
    {
        Application.targetFrameRate = targetFrameRate;
        framesToProcess = numPhysicsFramesPerUpdate;
        Time.timeScale = (framesToProcess > 0) ? physicsTimeMultiplier : 0.0f;
        Time.captureFramerate = (framesToProcess > 0) ? Mathf.FloorToInt(physicsTimeMultiplier / (Time.fixedDeltaTime * numPhysicsFramesPerUpdate)) : 0;
        foreach(Avatar a in myNetMessenger.GetAllAvatars())
            a.readyForSimulation = false;
    }
    
    public static void CheckToggleUpdates()
    {
        if (myNetMessenger.AreAllAvatarsReady())
            ToggleUpdates();
    }

    private static bool TestLogLevel(MyLogLevel myLog, LogType testLog)
    {
        switch(testLog)
        {
            case LogType.Log:
                return myLog <= MyLogLevel.LogAll;
            case LogType.Warning:
                return myLog <= MyLogLevel.Warning;
            default:
                return myLog <= MyLogLevel.Errors;
        }
    }

    private static void HandleLog(string logString, string stackTrace, LogType type)
    {
		Debug.Log ("Right before Handle Log");
	#if !UNITY_EDITOR
        if (TestLogLevel(logLevel, type))
        {
            string output = string.Format("\n{1}: {0}\n", logString, type);
            System.IO.File.AppendAllText(logFileLocation, output);
            if (TestLogLevel(stackLogLevel, type))
            {
                // Write out stack trace
                System.IO.File.AppendAllText(logFileLocation, "\nSTACK: " + System.Environment.StackTrace + "\n");
            }
        }
	#endif
		Debug.Log ("Right after Handle Log");
    }

    private static void ReadLogLevel(JsonData json, ref MyLogLevel value)
    {
        if (json != null && json.IsString)
        {
            string testVal = ((string)json).ToLowerInvariant();
            if (testVal == "log")
                value = MyLogLevel.LogAll;
            if (testVal == "warning")
                value = MyLogLevel.Warning;
            if (testVal == "error")
                value = MyLogLevel.Errors;
            if (testVal == "none")
                value = MyLogLevel.None;
        }
    }
		
    public static string ReadConfigFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;
        if (!System.IO.File.Exists(fileName))
        {
            Debug.LogWarningFormat("Couldn't open configuration file at {0}", fileName);
            return null;
        }
        return System.IO.File.ReadAllText(fileName);
    }

    public static void ParseJsonInfo(string fileName)
    {
        string testConfigInfo = ReadConfigFile(fileName);
#if UNITY_EDITOR
        // Read sample config if we have no config
        if (testConfigInfo == null)
        {
            Debug.Log("Reading sample config");
            const string configLoc = "Assets/Scripts/sample_config.txt";
            TextAsset sampleConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(configLoc);
            if (sampleConfig != null)
            {
                Debug.Log("Found sample config: " + sampleConfig.text);
                testConfigInfo = sampleConfig.text;
            }
        }
#endif
        if (testConfigInfo == null)
            return;
        _readJsonArgs = JsonMapper.ToObject(testConfigInfo);
        if (_readJsonArgs!= null)
        {
            ReadLogLevel(_readJsonArgs["log_level"], ref logLevel);
            ReadLogLevel(_readJsonArgs["stack_log_level"], ref stackLogLevel);
            logFileLocation = _readJsonArgs["output_log_file"].ReadString(logFileLocation);
        }
        Debug.LogFormat("Completed reading configuration at {0}:\n{1}", fileName, _readJsonArgs.ToJSON());
    }

    // Should use argument -executeMethod SimulationManager.Init
    public static void Init()
    {
		//used for permissions purposes. when binary generates output logs, they do so under root, 
		//and the editor does not have permissions to overwrite them.
	#if UNITY_EDITOR
		SimulationManager.logFileLocation = "output_log.txt";
	#endif

        if (_hasFinishedInit)
            return;
        System.IO.File.WriteAllText(logFileLocation, "Starting Initialization:\n");
        Application.logMessageReceived += HandleLog;
        List<string> args = new List<string>(System.Environment.GetCommandLineArgs());
		Debug.Log ("args: " + args.ToString());

		// default settings
		int screenWidth = Screen.width;
		int screenHeight = Screen.height;
		string preferredImageFormat = "png";
		bool shouldCreateServer = true;
		bool shouldCreateTestClient = false;
		bool debugNetworkMessages = false;
		bool logSimpleTimeInfo = false;
		bool logDetailedTimeInfo = false;
		bool saveDebugImageFiles = false;
		string environmentScene = "Empty";


        // Parse arguments
        {
            string output = "Args: ";
            foreach (string arg in args)
            {
				Debug.Log ("Arg: " + arg);
                output += "'" + arg + "' ";
				if (arg.StartsWith ("-port=")) {
					try {
						portNumber = arg.Substring ("-port=".IndexOf ("=") + 1);
					} catch {
						Debug.LogWarning ("No port number!");
					}
				} else if (arg.StartsWith ("-address=")) {
					try {
						hostAddress = arg.Substring ("-address=".IndexOf ("=") + 1);
					} catch {
						Debug.LogWarning ("No host address!"); 
					}
				} else if (arg.StartsWith ("-screenWidth=")) {
					try {
						screenWidth = int.Parse (arg.Substring ("-screenWidth=".IndexOf ("=") + 1));
					} catch {
						Debug.LogWarning ("No screen width!"); 
					}
				} else if (arg.StartsWith ("-screenHeight=")) {
					try {
						screenHeight = int.Parse (arg.Substring ("-screenHeight=".IndexOf ("=") + 1));
					} catch {
						Debug.LogWarning ("No screen height!"); 
					}
				} else if (arg.StartsWith ("-numTimeSteps=")) {
					try {
						numPhysicsFramesPerUpdate = int.Parse (arg.Substring ("-numTimeSteps=".IndexOf ("=") + 1));
					} catch {
						Debug.LogWarning ("No num time steps!"); 
					}
				} else if (arg.StartsWith ("-timeStep=")) {
					try {
						Time.fixedDeltaTime = int.Parse (arg.Substring ("-timeStep=".IndexOf ("=") + 1));
					} catch {
						Debug.LogWarning ("No time step duration!"); 
					}
				} else if (arg.StartsWith ("-profilerFrames=")) {
					try {
						profilerFrames = int.Parse (arg.Substring ("-profilerFrames=".IndexOf ("=") + 1));
					} catch {
						Debug.LogWarning ("No profiler frames!"); 
					}
				} else if (arg.StartsWith ("-preferredImageFormat=")) {
					try {
						preferredImageFormat = arg.Substring ("-preferredImageFormat=".IndexOf ("=") + 1);
					} catch {
						Debug.LogWarning ("No targetFPS!"); 
					}
				} else if (arg.StartsWith ("-shouldCreateServer")) {
					shouldCreateServer = true;
				} else if (arg.StartsWith ("-shouldCreateTestClient")) {
					shouldCreateTestClient = true;
				} else if (arg.StartsWith ("-debugNetworkMessages")) {
					debugNetworkMessages = true;
				} else if (arg.StartsWith ("-logSimpleTimingInfo")) {
					logSimpleTimeInfo = true;
				} else if (arg.StartsWith ("-logDetailedTimingInfo")) {
					logDetailedTimeInfo = true;
				} else if (arg.StartsWith ("-targetFPS")) {
					try {
						targetFrameRate = int.Parse (arg.Substring ("-targetFPS=".IndexOf ("=") + 1));
					} catch {
						Debug.LogWarning ("No target FPS!"); 
					}
				} else if (arg.StartsWith ("-saveDebugImageFiles=")) {
					saveDebugImageFiles = true;
				}
            }
            Debug.Log(output);
        }

        Screen.SetResolution(screenWidth, screenHeight, Screen.fullScreen);

        physicsTimeMultiplier = targetFrameRate * (Time.fixedDeltaTime * numPhysicsFramesPerUpdate * 1.05f);
        // Multiplier must be float between 0 and 100.0f
        if (physicsTimeMultiplier > 100)
        {
            targetFrameRate = Mathf.FloorToInt(targetFrameRate * 100f / physicsTimeMultiplier);
            physicsTimeMultiplier = targetFrameRate * (Time.fixedDeltaTime * numPhysicsFramesPerUpdate * 1.05f);
        }
        QualitySettings.vSyncCount = 0;
        Profiler.maxNumberOfSamplesPerFrame = profilerFrames;
        Application.targetFrameRate = targetFrameRate;
		// Debug.LogFormat("Setting target render FPS to {0} with speedup: {1} with phys timestep of {2} and {3} phys frames, maxDT: {4}", targetFrameRate, physicsTimeMultiplier, Time.fixedDeltaTime, numPhysicsFramesPerUpdate, Time.maximumDeltaTime);

        // Init NetMessenger
        myNetMessenger = GameObject.FindObjectOfType<NetMessenger>();
		Debug.Log (portNumber);
        if (myNetMessenger != null)
			myNetMessenger.Init(hostAddress, portNumber, portNumber_info, shouldCreateTestClient,shouldCreateServer, debugNetworkMessages, 
				logSimpleTimeInfo, logDetailedTimeInfo, preferredImageFormat, saveDebugImageFiles, environmentScene);
        else
            Debug.LogWarning("Couldn't find a NetMessenger to Initialize!");

        _hasFinishedInit = true;
	}
}
