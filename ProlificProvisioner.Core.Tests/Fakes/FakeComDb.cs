using ProlificProvisioner.Core.ComPorts;

namespace ProlificProvisioner.Core.Tests.Fakes;

/// <summary>Fake IComDb: records calls in-memory instead of touching the real registry.</summary>
public sealed class FakeComDb : IComDb
{
    public HashSet<int> ReservedPorts { get; } = new();
    public Dictionary<string, string> PortNamesByDevice { get; } = new();
    public bool ThrowOnWrite { get; set; }

    public void Reserve(int comNumber) => ReservedPorts.Add(comNumber);

    public void Release(int comNumber) => ReservedPorts.Remove(comNumber);

    public void WritePortName(string deviceInstanceId, string portName)
    {
        if (ThrowOnWrite)
        {
            throw new InvalidOperationException("Simulated registry write failure.");
        }

        PortNamesByDevice[deviceInstanceId] = portName;
    }
}
