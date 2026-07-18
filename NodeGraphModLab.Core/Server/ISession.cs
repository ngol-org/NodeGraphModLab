namespace NodeGraphModLab.Server;

public interface ISession
{
    Task SendAsync(string message);
    NotifyingSnapshotStore SnapshotStore { get; }
    HashSet<string> PinnedSnapshotNodeIds { get; }
}
