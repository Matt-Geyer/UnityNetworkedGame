﻿using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public interface IPersistentObject
    {
        PersistentObjectRep ObjectRep { get; set; }

        void Serialize(NetDataWriter writer);

        void Deserialize(NetDataReader reader);
    }
}