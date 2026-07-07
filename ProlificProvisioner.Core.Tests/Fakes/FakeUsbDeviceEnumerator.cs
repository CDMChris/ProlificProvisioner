using ProlificProvisioner.Core.Devices;

namespace ProlificProvisioner.Core.Tests.Fakes;

public sealed class FakeUsbDeviceEnumerator : IUsbDeviceEnumerator
{
    public List<UsbSerialDevice> Devices { get; } = new();

    public IReadOnlyList<UsbSerialDevice> EnumerateProlificDevices() => Devices;
}
