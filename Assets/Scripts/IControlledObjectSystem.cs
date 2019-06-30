using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public interface IControlledObjectSystem : IPacketStreamReader, IPacketStreamWriter
    {
        /// <summary>
        /// Start replicating this player controlled object on the client
        /// </summary>
        /// <param name="pco"></param>
        void StartReplicating(ControlledObject pco);

        /// <summary>
        /// Stop replicating this player controlled object on the client
        /// </summary>
        /// <param name="pco"></param>
        void StopReplicating(ControlledObject pco);

        /// <summary>
        /// Sample move, add to buffer, apply
        /// </summary>
        void UpdateControlledObject();

        ControlledObject CurrentlyControlledObject { get; set; }

    }
}