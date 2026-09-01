using System.Linq;
using System.Reflection;
using KontroXXL.Core;
using Xunit;

namespace KontroXXL.Core.Tests;

public class ArchitectureTests
{
    // Core'un bağımlılık listesi spec bölüm 4.1'de kilitli.
    // Bu test, birinin Core'a Windows API'si sızdırmasını derleme sonrası yakalar.
    static readonly string[] Forbidden =
    {
        "System.Windows.Forms",
        "System.IO.Ports",
        "System.Management",
        "Microsoft.Win32.Registry",
        "AudioSwitcher.AudioApi",
        "Avalonia",
    };

    [Fact]
    public void Core_does_not_reference_platform_assemblies()
    {
        var core = typeof(CoreMarker).Assembly;
        var referenced = core.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        var violations = referenced.Where(r => Forbidden.Contains(r)).ToArray();

        Assert.Empty(violations);
    }
}
