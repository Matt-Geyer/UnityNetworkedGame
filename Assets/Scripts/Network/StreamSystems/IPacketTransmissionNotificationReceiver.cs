using System.Collections.Generic;

namespace Assets.Scripts.Network.StreamSystems
{
    public interface IPacketTransmissionNotificationReceiver
    {
        void ReceiveNotifications(List<bool> notifications);
    }
}