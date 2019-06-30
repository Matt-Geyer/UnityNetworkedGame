using System;
using System.Collections.Generic;

namespace Assets.Scripts
{
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
}