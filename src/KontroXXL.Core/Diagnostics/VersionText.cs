using System;

namespace KontroXXL.Core.Diagnostics;

/// <summary>
/// Kullanıcıya gösterilecek sürüm metnini üretir.
/// </summary>
/// <remarks>
/// Spec §8.9 sürümün tek olmasını istiyor: <c>Directory.Build.props</c>'taki
/// <c>&lt;Version&gt;</c>, <c>vpk pack --packVersion</c> ile damgalanan kurulum paketi ve
/// Ayarlar'daki "Hakkında" satırı aynı değeri göstermeli. Derleyici o değeri
/// <c>AssemblyInformationalVersion</c>'a olduğu gibi yazar; <c>AssemblyVersion</c> ise
/// her zaman dört parçaya normalize edilir ("2.2.0" → "2.2.0.0") ve ön-sürüm ekini
/// ("-beta.1") tümden kaybeder. Bu yüzden önce informational sürüm okunur.
/// </remarks>
public static class VersionText
{
    /// <summary>Sürüm bilinemediğinde gösterilecek metin — boş dize gösterilmez.</summary>
    public const string Unknown = "bilinmiyor";

    /// <summary>
    /// <paramref name="informational"/> varsa onu (SourceLink'in eklediği <c>+commit</c>
    /// yapı üstverisi atılarak), yoksa <paramref name="assemblyVersion"/>'ı okunur bir
    /// metne indirger. İkisi de yoksa <see cref="Unknown"/> döner.
    /// </summary>
    public static string Describe(string? informational, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // "2.2.0+ea4e778" → "2.2.0". Ön-sürüm eki ("-beta.1") paket sürümünün
            // parçası olduğu için KORUNUR, yalnızca '+' sonrası atılır.
            var plus = informational.IndexOf('+');
            var trimmed = (plus >= 0 ? informational.Substring(0, plus) : informational).Trim();
            if (trimmed.Length > 0) return trimmed;
            // Yalnızca üstveriden ibaret bir değer ("+abc") boş metne düşerdi;
            // gösterilecek bir şey kalmadığı için assembly sürümüne devam edilir.
        }

        if (assemblyVersion == null) return Unknown;

        // Version, verilmeyen parçaları -1 tutar: new Version(2, 2) → Build = Revision = -1.
        // ToString(n) eksik parça istendiğinde fırlatır, o yüzden mevcut parça sayısını say.
        var revision = assemblyVersion.Revision;
        var build = assemblyVersion.Build;

        if (revision > 0) return assemblyVersion.ToString(4);
        // Revision 0 veya yok: paket damgası hiçbir zaman dördüncü parçayı taşımaz,
        // "2.2.0.0" yerine "2.2.0" göstermek §8.9'un tek-sürüm kriterini korur.
        return build >= 0 ? assemblyVersion.ToString(3) : assemblyVersion.ToString(2) + ".0";
    }
}
