using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    //This class rate limits the update frequency, and asks the server to send us less messages
    public static class DynamicTickWorker
    {
        //Twiddle these knobs
        public const int SEND_TICK_RATE = 5; //5hz
        public const int MASTER_MAX_SECONDARY_VESSELS = 10;
    }
}