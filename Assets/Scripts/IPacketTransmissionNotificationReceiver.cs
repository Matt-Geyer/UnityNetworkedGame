﻿using System.Collections.Generic;

namespace Assets.Scripts
{
    public interface IPacketTransmissionNotificationReceiver
    {
        void ReceiveNotifications(List<PacketTransmissionRecord> notifications);
    }
}