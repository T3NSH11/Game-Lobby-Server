using System.Net.Sockets;

namespace LobbyServer
{
    // Class to hold information on incoming data for asynchronous use
    public class IncomingData
    {
        public Socket Socket { get; set; } // The client socket
        public byte[] Buffer { get; set; } // The buffer to hold incoming data
    }
}
