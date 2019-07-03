using System;

namespace Assets.Scripts.Network.StreamSystems
{
    public class PersistentObjectRep
    {
        private readonly Func<IPersistentObject> _objectFactory;

        public byte Id { get; set; }

        public PersistentObjectRep(Func<IPersistentObject> factory)
        {
            _objectFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public IPersistentObject CreateNew()
        {
            return _objectFactory();
        }
    }
}