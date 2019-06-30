using System;

namespace Assets.Scripts
{
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
}