using System.Text.Json;

namespace NodeGraphModLab.Server;

internal interface IMessageHandler
{
    string MessageType { get; }
    Task HandleAsync(ISession session, JsonElement root);
}
