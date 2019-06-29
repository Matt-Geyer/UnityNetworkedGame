using AiUnity.NLog.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NLogToUnity : ILogger
{
    public ILogHandler logHandler { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public bool logEnabled { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public LogType filterLogType { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    private readonly NLogger _log;

    public NLogToUnity(string logName)
    {
        _log = NLogManager.Instance.GetLogger(logName);
    }

    public bool IsLogTypeAllowed(LogType logType)
    {
        throw new NotImplementedException();
    }

    public void Log(LogType logType, object message)
    {
        throw new NotImplementedException();
    }

    public void Log(LogType logType, object message, UnityEngine.Object context)
    {
        throw new NotImplementedException();
    }

    public void Log(LogType logType, string tag, object message)
    {
        throw new NotImplementedException();
    }

    public void Log(LogType logType, string tag, object message, UnityEngine.Object context)
    {
        throw new NotImplementedException();
    }

    public void Log(object message)
    {
        _log.Debug(message.ToString());
    }

    public void Log(string tag, object message)
    {
        throw new NotImplementedException();
    }

    public void Log(string tag, object message, UnityEngine.Object context)
    {
        throw new NotImplementedException();
    }

    public void LogError(string tag, object message)
    {
        throw new NotImplementedException();
    }

    public void LogError(string tag, object message, UnityEngine.Object context)
    {
        throw new NotImplementedException();
    }

    public void LogException(Exception exception)
    {
        throw new NotImplementedException();
    }

    public void LogException(Exception exception, UnityEngine.Object context)
    {
        throw new NotImplementedException();
    }

    public void LogFormat(LogType logType, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void LogWarning(string tag, object message)
    {
        throw new NotImplementedException();
    }

    public void LogWarning(string tag, object message, UnityEngine.Object context)
    {
        throw new NotImplementedException();
    }
}


[RequireComponent(typeof(UdpNetworkBehavior))]
public class GameServerBehavior : MonoBehaviour
{

    public GameObject ObjectPrefab;
    public GameObject PlayerPrefab;

    UdpNetworkBehavior Network;

    // Start is called before the first frame update
    void Start()
    {
        GameServerReactor reactor = new GameServerReactor
        {
            EntityPrefab = ObjectPrefab,
            ClientPrefab = PlayerPrefab
        };

        reactor.Initialize();
        Network = GetComponent<UdpNetworkBehavior>();
        Network.R_GameReactor = reactor;
        Network.ShouldConnect = false;
        Network.ShouldBind = true;
        Network.enabled = true;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
