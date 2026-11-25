// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Core;

/// <summary>
/// "Örtsün" yerleşiminde yazı boyu sabittir (Mac: CoverFontScale, TV ve OBS sayfası aynı kuralı uygular): her satır
/// kullanıcının seçtiği boyda, kutu çeviriye göre genişler. 3 satıra sığmayan çeviri küçülmez, daha çok satıra bölünür
/// (en çok 3 dengeli satır, pencere daha uzununu kendisi böler). X'te bildirildi (beta.89): eskiden böyle tek bir satır yazıyı %80'e indiriyor
/// ve boy ayarı değişene dek öyle bırakıyordu; yazı boyu satırdan satıra değişiyor, çeviri sıkışıyordu.
/// </summary>
public sealed class CoverFontScale
{
    /// <summary>Satırın yazı boyu çarpanı: hep 1 (seçilen boy).</summary>
    public double Scale(string text, int fontSize) => 1;
}
