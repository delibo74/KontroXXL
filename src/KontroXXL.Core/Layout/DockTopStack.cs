using System.Collections.Generic;

namespace KontroXXL.Core.Layout;

/// <summary>
/// Üst üste yığılan <c>Dock.Top</c> panellerinin hangi sırayla eklenmesi gerektiği.
/// </summary>
/// <remarks>
/// 2026-09-04 CANLI HATA: NAS Dashboard'da havuz/uyarı/servis bölümleri sayfanın
/// ÜSTÜNDE, NAS özeti (donut'lar, REBOOT/SHUTDOWN) ALTINDA görünüyordu; NAS Apps'te de
/// "MANAGED APPLICATIONS" başlığı listenin altına düşüyordu. Kullanıcının tarifi:
/// "üstte gözükmesi gereken detaylar alta kayıyor".
///
/// Sebep WinForms'un yerleşim kuralı: aynı ebeveyne eklenen <c>Dock.Top</c> kontroller
/// ekleme sırasının TERSİNE yığılır — EN SON eklenen EN ÜSTTE durur. Kodda bölümler
/// okunası bir sırayla (önce üstteki) eklendiği için görüntü tam ters çıkıyordu.
///
/// Bu kural burada tek bir yerde, WinForms'tan bağımsız ve test edilebilir biçimde
/// duruyor; çağıran taraf görmek istediği sırayı (üstten alta) yazar, dönüşümü
/// düşünmek zorunda kalmaz.
/// </remarks>
public static class DockTopStack
{
    /// <summary>
    /// İstenen görsel sırayı (üstten alta) <c>Controls.Add</c> çağrı sırasına çevirir.
    /// </summary>
    public static IReadOnlyList<T> ToInsertionOrder<T>(IReadOnlyList<T>? topToBottom)
    {
        if (topToBottom == null) return new T[0];

        var result = new T[topToBottom.Count];
        for (int i = 0; i < topToBottom.Count; i++)
            result[i] = topToBottom[topToBottom.Count - 1 - i];
        return result;
    }
}
