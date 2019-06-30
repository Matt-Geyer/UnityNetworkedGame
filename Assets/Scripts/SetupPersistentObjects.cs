using UnityEngine;

namespace Assets.Scripts
{
    public class SetupPersistentObjects : MonoBehaviour
    {
        // Start is called before the first frame update
        void Start()
        {
            PersistentObjectRep replicatableGameObjectRep = new PersistentObjectRep(() => new ReplicatableGameObject());
            ReplicatableGameObject.StaticObjectRep = replicatableGameObjectRep;
            PersistentObjectManager.RegisterPersistentObject(replicatableGameObjectRep);
        }
    }
}
