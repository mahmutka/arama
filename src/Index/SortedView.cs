namespace Arama.Index
{
    /// <summary>
    /// Ada göre sıralı görünüm: kayıt numaraları ve isimleri aynı sırada, art arda duran bir tampon.
    /// i. ismin karakterleri Names[Offsets[i] .. Offsets[i + 1]) aralığındadır. Kuyruktaki (sonradan değişen)
    /// girdilerin buradaki isimleri eskimiş olabilir; tarayan taraf bayraklarla onları atlar.
    /// </summary>
    internal sealed class SortedView
    {
        public readonly int[] Ids;
        public readonly int[] Offsets;
        public readonly char[] Names;

        public SortedView(int[] ids, int[] offsets, char[] names)
        {
            Ids = ids;
            Offsets = offsets;
            Names = names;
        }
    }
}
