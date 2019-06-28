using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SetupPersistentObjects : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        PersistentObjectRep ReplicatableGameObjectRep = new PersistentObjectRep(() => new ReplicatableGameObject());
        ReplicatableGameObject.StaticObjectRep = ReplicatableGameObjectRep;
        PersistentObjectManager.RegisterPersistentObject(ReplicatableGameObjectRep);
    }
}
