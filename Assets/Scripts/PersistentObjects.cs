using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

public interface IPersistentObject
{
    PersistentObjectRep ObjectRep { get; set; }

    void Serialize(NetDataWriter writer);

    void Deserialize(NetDataReader reader);

}


public class PersistentObjectRep
{
    private readonly Func<IPersistentObject> ObjectFactory;

    public byte Id { get; set; }

    public PersistentObjectRep(Func<IPersistentObject> factory)
    {
        ObjectFactory = factory ?? throw new ArgumentNullException("factory");
    }

    public IPersistentObject CreateNew()
    {
        return ObjectFactory();
    }
}


public class PersistentObjectManager
{
    public static Dictionary<byte, PersistentObjectRep> ObjectReps = new Dictionary<byte, PersistentObjectRep>();

    public static byte NextId;

    public static void RegisterPersistentObject(PersistentObjectRep objectRep)
    {
        objectRep.Id = NextId++;
        if (NextId == 255) throw new Exception("HOLY SHIT MAX PERSISTENT OBJECTS REACHED CHANGE ID TO A LARGER TYPE!");
        ObjectReps[objectRep.Id] = objectRep;
    }

    public static IPersistentObject CreatePersistentObject(byte id)
    {
        return ObjectReps[id].CreateNew();
    }
}


