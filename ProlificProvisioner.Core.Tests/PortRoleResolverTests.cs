using ProlificProvisioner.Core.Config;
using ProlificProvisioner.Core.Devices;
using ProlificProvisioner.Core.Workflow;
using Xunit;

namespace ProlificProvisioner.Core.Tests;

public class PortRoleResolverTests
{
    [Fact]
    public void Resolve_ReturnsNull_ForUnlearnedLocation()
    {
        var resolver = new PortRoleResolver(new AppConfig());

        Assert.Null(resolver.Resolve("Port_#0001.Hub_#0001"));
    }

    [Fact]
    public void Learn_ThenResolve_ReturnsTheLearnedRole()
    {
        var resolver = new PortRoleResolver(new AppConfig());

        resolver.Learn("Port_#0001.Hub_#0001", PortRole.DispenseHead);
        resolver.Learn("Port_#0002.Hub_#0001", PortRole.Printer);

        Assert.Equal(PortRole.DispenseHead, resolver.Resolve("Port_#0001.Hub_#0001"));
        Assert.Equal(PortRole.Printer, resolver.Resolve("Port_#0002.Hub_#0001"));
    }

    [Fact]
    public void Learn_Twice_ForSameRole_OverwritesStaleMapping()
    {
        var resolver = new PortRoleResolver(new AppConfig());

        resolver.Learn("Port_#0001.Hub_#0001", PortRole.DispenseHead);
        // Fixture got rewired: dispense-head cable now plugs into a different physical port.
        resolver.Learn("Port_#0003.Hub_#0001", PortRole.DispenseHead);

        Assert.Null(resolver.Resolve("Port_#0001.Hub_#0001"));
        Assert.Equal(PortRole.DispenseHead, resolver.Resolve("Port_#0003.Hub_#0001"));
    }

    [Fact]
    public void IsFullyLearned_RequiresBothRolesMapped()
    {
        var resolver = new PortRoleResolver(new AppConfig());
        Assert.False(resolver.IsFullyLearned());

        resolver.Learn("Port_#0001.Hub_#0001", PortRole.DispenseHead);
        Assert.False(resolver.IsFullyLearned());

        resolver.Learn("Port_#0002.Hub_#0001", PortRole.Printer);
        Assert.True(resolver.IsFullyLearned());
    }
}
