using LiteNetLib.Utils;
using UnityEngine;

namespace Assets.Scripts
{
    public sealed class ControlledObjectedSystemServer : ControlledObjectSystemBase
    {
        public override void WriteToPacketStream(NetDataWriter stream)
        {
            // Send id of last move that was received from client
            stream.Put((ushort) SeqLastProcessed);

            // Send new pco state
            CurrentlyControlledObject.Serialize(stream);

            // Send state of all pco that are being replicated by this system
        }

        public override void ReadPacketStream(NetDataReader stream)
        {
            // The players last 3 moves are always transmitted with the last move being the most recent
            PlayerInputsToTransmit[0].Deserialize(stream);
            PlayerInputsToTransmit[1].Deserialize(stream);
            PlayerInputsToTransmit[2].Deserialize(stream);

            Debug.Log("Read client inputs: ");
            Debug.Log($"seq: {PlayerInputsToTransmit[0].Seq} Move:{PlayerInputsToTransmit[0].MoveDirection}");
            Debug.Log($"seq: {PlayerInputsToTransmit[1].Seq} Move:{PlayerInputsToTransmit[1].MoveDirection}");
            Debug.Log($"seq: {PlayerInputsToTransmit[2].Seq} Move:{PlayerInputsToTransmit[2].MoveDirection}");


            // In a 0 packet loss scenario Input [1] was last sequence and input [2] is this sequence
            // but we will look further back, and if they are all new then apply all 3 moves        
            ushort nextMoveSeq = (ushort) (SeqLastProcessed + 1);
            Debug.Log($"LastProcessedMoveSeq: {SeqLastProcessed} NextMove: {nextMoveSeq}");
            int i = 2;
            for (; i >= 0; i--)
            {
                Debug.Log($"_playerInputsToTransmit[{i}].seq: {PlayerInputsToTransmit[i].Seq}");
                if (PlayerInputsToTransmit[i].Seq == nextMoveSeq) break;
            }
            
            // if nextMoveSeq isn't found then i will be -1
            i = i >= 0 ? i : 0;

            // This should always have at least one new move but up to 3
            for (int j = i; j <= 2; j++)
            {
                Debug.Log($"Looking at _playerInputsToTransmit[{j}]");
                CurrentlyControlledObject.ApplyInput(PlayerInputsToTransmit[j]);
                SeqLastProcessed = PlayerInputsToTransmit[j].Seq;
                Debug.Log($"Applied _playerInputsToTransmit[{j}] with seq: {PlayerInputsToTransmit[j].Seq}");
            }
        }
    }
}