using System.Collections.Generic;
using AiUnity.NLog.Core;
using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public abstract class ControlledObjectSystemBase : IControlledObjectSystem
    {
        protected readonly NLogger Log;

        protected readonly List<UserInputSample> PlayerInputsToTransmit;

        protected readonly UserInputWindow PlayerInputWindow;

        protected int SeqLastProcessed = -1;

        protected ControlledObjectSystemBase()
        {
            Log = NLogManager.Instance.GetLogger(this);

            PlayerInputWindow = new UserInputWindow
            {
                Sampler = new UserInputUtils()
            };

            PlayerInputWindow.Init(360);

            // Since we are always going to transmit the last 3 moves I figured this
            // was a simple way to simplify the transmit logic to not have to check if there are at least 3 
            for (int i = 0; i < 3; i++)
                PlayerInputWindow.SampleUserInput();

            PlayerInputsToTransmit = new List<UserInputSample>(3)
            {
                PlayerInputWindow.Input[0],
                PlayerInputWindow.Input[1],
                PlayerInputWindow.Input[2]
            };
        }

        public virtual ControlledObject CurrentlyControlledObject { get; set; }
        
        public virtual void UpdateControlledObject()
        {
        }

        public virtual void StartReplicating(ControlledObject pco)
        {
            // start replicating this object which is controlled by a player (not the player who will be receiving this state tho)
        }

        public virtual void StopReplicating(ControlledObject pco)
        {
            // stop replicating a pco
        }

        public abstract void WriteToPacketStream(NetDataWriter stream);
        
        public abstract void ReadPacketStream(NetDataReader stream);
    }
}