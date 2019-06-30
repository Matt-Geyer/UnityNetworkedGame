using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public interface IPlayerControlledObjectSystem
    {
        /// <summary>
        /// Start replicating this player controlled object on the client
        /// </summary>
        /// <param name="pco"></param>
        void StartReplicating(PlayerControlledObject pco);

        /// <summary>
        /// Stop replicating this player controlled object on the client
        /// </summary>
        /// <param name="pco"></param>
        void StopReplicating(PlayerControlledObject pco);

        /// <summary>
        ///  Called on the client to write data to be read by ProcessClientToServerStream 
        /// </summary>
        /// <param name="stream"></param>
        void WriteClientToServerStream(NetDataWriter stream);

        /// <summary>
        ///  Called on the server to read and process data written by WriteClientToServerStream 
        /// </summary>
        /// <param name="stream"></param>
        void ProcessClientToServerStream(NetDataReader stream);

        /// <summary>
        ///  Called on the server to write data to be read by ProcessServerToClientStream 
        /// </summary>
        /// <param name="stream"></param>
        void WriteServerToClientStream(NetDataWriter stream);

        /// <summary>
        ///  Called on the client to read and process data written by WriteServerToClientStream 
        /// </summary>
        /// <param name="stream"></param>
        void ProcessServerToClientStream(NetDataReader stream);

        /// <summary>
        /// Sample move, add to buffer, apply
        /// </summary>
        void UpdateControlledObject();

        PlayerControlledObject ControlledObject { get; set; }

    }
}