using Assets.Scripts.Network.StreamSystems;
using UnityEngine;

namespace Assets.Scripts
{
    public class SetupPersistentObjects : MonoBehaviour
    {
        void Awake()
        {
            PersistentObjectRep replicatableGameObjectRep = new PersistentObjectRep(() => new ReplicatableGameObject());
            ReplicatableGameObject.StaticObjectRep = replicatableGameObjectRep;
            PersistentObjectManager.RegisterPersistentObject(replicatableGameObjectRep);

            PersistentObjectRep testEventObjectRep = new PersistentObjectRep(() => new TestEvent());
            TestEvent.StaticObjectRep = testEventObjectRep;
            PersistentObjectManager.RegisterPersistentObject(testEventObjectRep);
        }
    }
}
