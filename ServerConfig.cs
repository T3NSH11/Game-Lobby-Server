namespace LobbyServer
{
    public interface IServerConfig
    {
        string ServerBuildPath { get; set; }
        int ServerPort { get; set; }
    }

    public class ServerConfig : IServerConfig
    {
        public string ServerBuildPath { get; set; }
        public int ServerPort { get; set; }
    }
}
