using System;
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

        // Onek eslesmesi: "Avalonia" tek basina "Avalonia.Base"/"Avalonia.Controls"/
        // "Avalonia.Desktop" gibi alt derlemeleri yakalamaz, tam esitlik (Contains)
        // bunlari kacirirdi. Ayni sekilde "AudioSwitcher.AudioApi" oneki
        // "AudioSwitcher.AudioApi.CoreAudio" derlemesini de kapsamali.
        //
        // Not: yasakli bir derleme burada, GetReferencedAssemblies() listesinde,
        // ancak Core kodu o derlemeden gercekten bir TIP KULLANDIGINDA belirir.
        // Kullanilmayan bir PackageReference tek basina burada gorunmez — bu kabul
        // edilebilir, cunku kullanilmayan bir referans katmanlamayi bozamaz.
        var violations = referenced
            .Where(r => Forbidden.Any(f => r.Equals(f, StringComparison.Ordinal)
                                         || r.StartsWith(f + ".", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(violations);
    }
}
