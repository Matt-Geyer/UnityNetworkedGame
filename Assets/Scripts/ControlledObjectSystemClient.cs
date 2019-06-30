using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public sealed class ControlledObjectSystemClient : ControlledObjectSystemBase
    {
        public override void ReadPacketStream(NetDataReader stream)
        {
            // read id of last processed move and use it to update
            // the buffer of stored moves
            SeqLastProcessed = stream.GetUShort();
            Log.Debug($"SeqLastProcessed from server: {SeqLastProcessed}");


            PlayerInputWindow.AckSeq((ushort)SeqLastProcessed);

            Log.Debug("Updated PlayerInputWindow");

            // read state of player obj and set it using remainder of moves in buffer to predict again
            CurrentlyControlledObject.Deserialize(stream);

            Log.Debug("Read controlled object state");


            // read state of all replicated pco and predict
            for (int i = 0; i < PlayerInputWindow.Count; i++)
                CurrentlyControlledObject.ApplyInput(PlayerInputWindow.Input[PlayerInputWindow.First + i]);

            Log.Debug("Finished applying un-acked moves");
        }

        public override void WriteToPacketStream(NetDataWriter stream)
        {
            // write players last 3 moves to stream
            PlayerInputsToTransmit[0].Serialize(stream);
            PlayerInputsToTransmit[1].Serialize(stream);
            PlayerInputsToTransmit[2].Serialize(stream);

            Log.Debug($"PlayerInputsToTransmit[0].seq:{PlayerInputsToTransmit[0].Seq}");
            Log.Debug($"PlayerInputsToTransmit[1].seq:{PlayerInputsToTransmit[1].Seq}");
            Log.Debug($"PlayerInputsToTransmit[2].seq:{PlayerInputsToTransmit[2].Seq}");
        }

       

        public override void UpdateControlledObject()
        {
            // sample move
            int nextSampleIndex = PlayerInputWindow.SampleUserInput();


            if (nextSampleIndex < 0) return;

            Log.Debug(
                $"Current Sample - PlayerInputWindow[{nextSampleIndex}] seq: {PlayerInputWindow.Input[nextSampleIndex].Seq} Move: {PlayerInputWindow.Input[nextSampleIndex].MoveDirection}");

            // apply move 
            CurrentlyControlledObject.ApplyInput(PlayerInputWindow.Input[nextSampleIndex]);

            // Update packets to transmit 
            PlayerInputsToTransmit.RemoveAt(0);
            PlayerInputsToTransmit.Add(PlayerInputWindow.Input[nextSampleIndex]);
        }
    }
}