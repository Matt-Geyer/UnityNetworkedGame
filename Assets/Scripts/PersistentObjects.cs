using LiteNetLib.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class PersistentObject
{
    public abstract void Serialize(NetDataWriter writer);

    public abstract void Deserialize(NetDataReader reader);

    public abstract void SetStaticRep(PersistentObjectRep rep);
}


public class PersistentObjectRep
{
    private readonly Func<PersistentObject> ObjectFactory;

    public byte Id { get; set; }

    public PersistentObjectRep(Func<PersistentObject> factory)
    {
        ObjectFactory = factory ?? throw new ArgumentNullException("factory");
    }

    public PersistentObject CreateObject()
    {
        return ObjectFactory();
    }
}


public class PersistentObjectManager
{
    public Dictionary<byte, PersistentObjectRep> ObjectReps;

    public byte NextId;

    public PersistentObjectManager()
    {
        ObjectReps = new Dictionary<byte, PersistentObjectRep>();
    }

    public void RegisterPersistentObject(PersistentObjectRep objectRep)
    {
        objectRep.Id = NextId++;
        if (NextId == 255) throw new Exception("HOLY SHIT MAX PERSISTENT OBJECTS REACHED CHANGE ID TO A LARGER TYPE!");
        ObjectReps[objectRep.Id] = objectRep;
    }

    public PersistentObject CreateObject(byte id)
    {
        return ObjectReps[id].CreateObject();
    }
}


