using Google.Protobuf;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("LobbyServerTestProj")]
namespace LobbyServer
{
    public class LobbyServer
    {
        // Server configuration
        public ServerConfig config = new ServerConfig();

        // Dictionaries to hold lobbies and servers
        public Dictionary<string, List<Socket>> lobbies = new Dictionary<string, List<Socket>>();
        private Dictionary<string, serverInfo> servers = new Dictionary<string, serverInfo>();

        // Server socket
        private Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        // Next port to assign to a game server
        private int nextServerPort = 3001;

        // Main method
        public static void Main()
        {
            LobbyServer server = new LobbyServer();

            // Ask user if they want to use saved server configuration
            Console.WriteLine("Would you like to use saved server configuration? (Y/N)");
            string response = Console.ReadLine().ToLower();

            // Load or set server configuration based on user's response
            if (response == "y")
                server.LoadServerConfigInfo();
            else if (response == "n")
                server.SetNewServerConfigInfo();
            else
            {
                Console.WriteLine("Invalid response. Using saved server configuration.");
                server.LoadServerConfigInfo();
            }

            // Start the server
            server.StartServer();

            // Keep the main thread running
            while (true)
            {
                Thread.Sleep(1000);
            }
        }

        // Function to set new config info
        private void SetNewServerConfigInfo()
        {
            // Ask user for config info
            Console.WriteLine("Input the path to the game server build");
            config.ServerBuildPath = Console.ReadLine();
            Console.WriteLine("Input master server port");
            config.ServerPort = Convert.ToInt32(Console.ReadLine());

            // Save config info to file
            string configData = JsonSerializer.Serialize(config);
            File.WriteAllText("config.json", configData);
        }

        // Function to load config info from file
        private void LoadServerConfigInfo()
        {
            if (!File.Exists("config.json"))
            {
                Console.WriteLine("Config file not found, Please set new configuration");
                SetNewServerConfigInfo();
            }
            else
            {
                string json = File.ReadAllText("config.json");
                config = JsonSerializer.Deserialize<ServerConfig>(json);
            }
        }

        // Method to start the server
        private void StartServer()
        {
            Console.WriteLine("Starting Server...");
            // Bind the server socket to any IP address on the port specified in config
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, config.ServerPort));
            // Start listening for incoming connections
            serverSocket.Listen(10);
            // Start accepting incoming connections asynchronously
            serverSocket.BeginAccept(new AsyncCallback(AcceptCallback), null);
        }

        // Callback function to handle incoming connections
        private void AcceptCallback(IAsyncResult AR)
        {
            // End the accepting and get the client socket
            Socket clientSocket = serverSocket.EndAccept(AR);
            // Log the connection
            Console.WriteLine("Client connected: " + clientSocket.RemoteEndPoint.ToString());
            // Create a buffer to hold incoming data
            byte[] buffer = new byte[clientSocket.ReceiveBufferSize];
            // Begin receiving data from the clients
            clientSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), new IncomingData { Socket = clientSocket, Buffer = buffer });
            // Continue accepting incoming connections
            serverSocket.BeginAccept(new AsyncCallback(AcceptCallback), null);
        }

        // Callback function to handle incoming data
        private void ReceiveCallback(IAsyncResult AR)
        {
            // Get the state
            IncomingData state = (IncomingData)AR.AsyncState;
            Socket clientSocket = state.Socket;
            // Stop receiving
            clientSocket.EndReceive(AR);
            // Deserialize the received bytes to ClientMessage
            LobbyMessage recievedMessage = LobbyMessage.Parser.ParseFrom(state.Buffer);
            // Get the message type
            LobbyMessage.PayloadOneofCase messageType = recievedMessage.PayloadCase;

            // Handle the message based on type
            switch (messageType)
            {
                case LobbyMessage.PayloadOneofCase.CreateLobby:
                    HandleCreateLobby(clientSocket, recievedMessage.CreateLobby);
                    break;
                case LobbyMessage.PayloadOneofCase.JoinLobby:
                    HandleJoinLobby(clientSocket, recievedMessage.JoinLobby);
                    break;
                case LobbyMessage.PayloadOneofCase.GetLobbies:
                    HandleGetLobbies(clientSocket);
                    break;
                default:
                    Console.WriteLine("Unknown message type: " + messageType);
                    break;
            }

            // Continue receiving data from the client
            clientSocket.BeginReceive(state.Buffer, 0, state.Buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), state);
        }

        // Function to handle CreateLobby message
        internal void HandleCreateLobby(Socket clientSocket, CreateLobbyMessage createLobbyMessage)
        {
            string lobbyName = createLobbyMessage.LobbyName;

            if (!lobbies.TryGetValue(lobbyName, out var lobby))
            {
                lobby = new List<Socket>() { clientSocket };
                lobbies.Add(lobbyName, lobby);

                Process serverProcess = new Process();
                serverProcess.StartInfo.FileName = config.ServerBuildPath;
                serverProcess.StartInfo.Arguments = $"{lobbyName} {nextServerPort}";
                serverProcess.Start();

                serverInfo serverInfo = new serverInfo
                {
                    LobbyName = lobbyName,
                    ServerProcess = serverProcess,
                    ServerIP = GetLocalIPAddress(),
                    ServerPort = nextServerPort
                };

                servers.Add(lobbyName, serverInfo);
                Console.WriteLine($"Server instance started for lobby: {lobbyName} on port {nextServerPort}");

                SendServerInfo(clientSocket, serverInfo);

                nextServerPort++;
            }
            else
            {
                ErrorMessage errorMessage = new ErrorMessage
                {
                    Message = "Lobby name taken"
                };
                byte[] data = errorMessage.ToByteArray();

                if (!System.Diagnostics.Debugger.IsAttached)
                {
                    clientSocket.BeginSend(data, 0, data.Length, SocketFlags.None, new AsyncCallback(SendCallback), clientSocket);
                }
            }
        }

        // Function to handle JoinLobby message
        internal void HandleJoinLobby(Socket clientSocket, JoinLobbyMessage joinLobbyMessage)
        {
            string lobbyName = joinLobbyMessage.LobbyName;

            if (lobbies.ContainsKey(lobbyName))
            {
                lobbies[lobbyName].Add(clientSocket);
                Console.WriteLine("Client joined lobby: " + lobbyName);

                serverInfo serverInfo = servers[lobbyName];

                SendServerInfo(clientSocket, serverInfo);
            }
            else
            {
                ErrorMessage errorMessage = new ErrorMessage
                {
                    Message = "Lobby does not exist"
                };
                byte[] data = errorMessage.ToByteArray();

                if (!System.Diagnostics.Debugger.IsAttached)
                {
                    clientSocket.BeginSend(data, 0, data.Length, SocketFlags.None, new AsyncCallback(SendCallback), clientSocket);
                }
            }
        }

        // Function to handle GetLobbies message
        internal void HandleGetLobbies(Socket clientSocket)
        {
            LobbyListMessage lobbyList = new LobbyListMessage()
            {
                LobbyNames = { lobbies.Keys }
            };

            byte[] data = lobbyList.ToByteArray();
            clientSocket.BeginSend(data, 0, data.Length, SocketFlags.None, new AsyncCallback(SendCallback), clientSocket);
            Console.WriteLine($"Sent list of {lobbyList.LobbyNames.Count} lobbies to client.");
        }

        // Callback function to handle the end of a send operation
        private void SendCallback(IAsyncResult AR)
        {
            // Get the client socket
            Socket socket = (Socket)AR.AsyncState;
            // End the send operation
            socket.EndSend(AR);
        }

        // Function to send GameServerInfo to the client
        private void SendServerInfo(Socket clientSocket, serverInfo serverInfo)
        {
            // Create a new packet with the server's IP address and port
            LobbyInfoMessage lobbyInfoPacket = new LobbyInfoMessage
            {
                LobbyIP = serverInfo.ServerIP.ToString(),
                LobbyPort = serverInfo.ServerPort
            };

            // Serialize the packet
            byte[] data = lobbyInfoPacket.ToByteArray();

            if (!System.Diagnostics.Debugger.IsAttached)
            {
                // Send the data to the client
                clientSocket.BeginSend(data, 0, data.Length, SocketFlags.None, new AsyncCallback(SendCallback), clientSocket);
            }
        }

        // Function to get the local IP address
        public IPAddress GetLocalIPAddress()
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip;
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }
    }

    public struct serverInfo
    {
        public string LobbyName { get; set; }
        public Process ServerProcess { get; set; }
        public IPAddress ServerIP { get; set; }
        public int ServerPort { get; set; }
    }
}
